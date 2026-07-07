import Ze, { useMemo as de, useState as F, useEffect as G, memo as be, forwardRef as Sc, useRef as ie, useCallback as re, useContext as $n, createContext as Lr, useLayoutEffect as Sd, lazy as Cd, Suspense as Ed, useReducer as Id, useId as Cc } from "react";
import { ListChecks as kd, Save as Ec, EyeOff as js, Shield as Ss, AlertTriangle as Co, SlidersHorizontal as zr, Activity as Ic, Search as Fo, Check as Gt, Boxes as Ko, Zap as Ad, Play as Ot, Terminal as _d, ListTree as Vr, GitBranch as kc, Plus as Ht, Trash2 as bn, AlertCircle as ft, Wrench as Dd, Copy as Td, X as Ac, Sparkles as ot, RotateCcw as Or, ChevronDown as _c, ChevronRight as Mt, GripVertical as Dc, Maximize2 as Eo, ChevronUp as $d, Package as Tc, Undo2 as Md, Redo2 as Pd, Network as Rd, Download as Ld, ChevronLeft as Io, Minimize2 as Cs, Workflow as $c, Code2 as zd } from "lucide-react";
import { useQuery as Mc, useQueryClient as Vd, useMutation as Od } from "@tanstack/react-query";
import { useTablistKeyboard as Hd } from "@elsa-workflows/studio-ui";
function Wd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Yi = { exports: {} }, rn = {};
var Es;
function Bd() {
  if (Es) return rn;
  Es = 1;
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
  return rn.Fragment = t, rn.jsx = n, rn.jsxs = n, rn;
}
var Is;
function Fd() {
  return Is || (Is = 1, Yi.exports = Bd()), Yi.exports;
}
var r = Fd();
let Pc;
function Kd(e) {
  Pc = e;
}
function ks() {
  return Pc;
}
const Xd = "String", qd = "singleline";
function Yd(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Hr(e, t = "Single") {
  return { alias: (e ?? "").trim() || Xd, collectionKind: t };
}
function Rc(e) {
  const t = e.type ?? e.Type;
  if (ko(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: Yd(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return ko(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: As(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: As(e) ? "Array" : "Single" };
}
function As(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function _s(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Xo() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function Ud(e, t) {
  const n = new Set(t);
  let o = 1, i = `${e}${o}`;
  for (; n.has(i); )
    o += 1, i = `${e}${o}`;
  return i;
}
function Zd(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hr(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Gd(e, t) {
  return { ...e, ...t };
}
function Jd(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Qd(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function ef(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hr(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: qd,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function tf(e, t) {
  return { ...e, ...t };
}
function nf(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hr(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function of(e, t) {
  return { ...e, ...t };
}
function rf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Lc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : rf(e || t);
}
function sf(e, t) {
  return Lc(e, t).replace(/StorageDriver$/, "");
}
function ko(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function mn(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const af = ["name", "Name"], zc = ["name", "Name"], cf = ["storageDriverType", "StorageDriverType"], Vc = ["referenceKey", "ReferenceKey"], lf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Mn(e) {
  return Oc(e, pf);
}
function qo(e) {
  return Oc(e, hf);
}
function Oc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Hc(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = ho(e.variables, pr)), Array.isArray(e.inputs) && (n.inputs = ho(e.inputs, pr)), Array.isArray(e.outputs) && (n.outputs = ho(e.outputs, (o) => Wc(o, !1))), n;
}
function Hc(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !Ge(o.payload)) return n;
  let i = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(wf) && (s[a] = c.map((u) => Hc(u, t)), i = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (s.variables = ho(o.payload.variables, pr), i = !0), i ? { ...n, structure: { ...o, payload: s } } : n;
}
function ho(e, t) {
  return e.map((n) => Ge(n) && !Array.isArray(n) ? t(n) : n);
}
function pr(e) {
  return Wc(e, !0);
}
const uf = [
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
function Wc(e, t) {
  const n = df(e, uf);
  return mn(e, Vc).trim() || (n.referenceKey = Xo()), n.type = Rc(e), t && (n.storageDriverType = ff(e.storageDriverType ?? e.StorageDriverType)), n;
}
function df(e, t) {
  const n = new Set(t), o = {};
  for (const [i, s] of Object.entries(e))
    n.has(i) || (o[i] = s);
  return o;
}
function ff(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (Ge(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function pf(e) {
  const t = [], n = {};
  for (const [i, s] of Object.entries(e))
    lf.has(i) || (xf(s) ? t.push({
      referenceKey: gf(i),
      value: mf(s.expression)
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
function hf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!Ge(o) || typeof o.referenceKey != "string") continue;
    const i = Ge(o.value) ? o.value : {};
    n[yf(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof i.expressionType == "string" ? i.expressionType : "Literal",
        value: i.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function gf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function yf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function mf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && Ge(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && Ge(e.value) ? { value: Ds(e.value), expressionType: "Object" } : { value: Ds(e.value), expressionType: t };
}
function Ds(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function xf(e) {
  if (!Ge(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return Ge(t) && typeof t.type == "string";
}
function wf(e) {
  return Ge(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Ge(e) {
  return typeof e == "object" && e !== null;
}
const Pn = "elsa.sequence.structure", Jt = "elsa.flowchart.structure";
function Bc(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const i = De(n).find((a) => a.id === o.slotId);
    if (!i) return null;
    const s = i.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function vf(e, t, n = (o) => o.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (i, s) => {
    const a = De(i);
    if (a[0]?.activities.some((c) => c.nodeId === t)) return s;
    for (const c of a)
      for (const u of c.activities) {
        const l = o(u, [...s, { ownerNodeId: u.nodeId, slotId: c.id, label: n(u) }]);
        if (l) return l;
      }
    return null;
  };
  return o(e, []);
}
function Nn(e, t) {
  const n = Bc(e, t);
  if (!n) return null;
  const o = De(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function De(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = zf(t), i = Ui(n.activities);
  return i ? [{
    id: `${t.kind}:activities`,
    label: Vf(t),
    property: "activities",
    mode: o,
    activities: i
  }] : Object.entries(n).filter(([, s]) => Ui(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: Hf(s),
    property: s,
    mode: "generic",
    activities: Ui(a) ?? []
  }));
}
function Fc(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), i = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = i.get(a.nodeId) ?? Of(e.slot.mode, c);
    return qc(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? Yc(e.owner) : Df(e.slot, s)
  };
}
function hr(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), i = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [qc(e, o, { x: i.x, y: i.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function bf(e, t, n, o = null) {
  const i = new Map(t.map((c) => [c.activityExecutionId, c])), s = $s(t, (c) => c.authoredActivityId || c.executableNodeId), a = $s(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? i.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Zc(u), f = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), p = {
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
function Wr(e, t) {
  return e?.structure?.kind === Jt || Ef(t) ? "flowchart" : e?.structure?.kind === Pn || If(t) ? "sequence" : "unsupported";
}
function gr(e, t, n) {
  if (t.length === 0) {
    const c = De(e)[0];
    return c ? jn(e, c, n) : e;
  }
  const [o, ...i] = t, s = De(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? gr(c, i, n) : c);
  return jn(e, s, a);
}
function Kc(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...i] = t, s = De(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Kc(c, i, n) : c);
  return jn(e, s, a);
}
function Xc(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = De(e);
  if (o.length === 0) return e;
  let i = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = Xc(u, t, n);
      return l !== u && (i = !0), l;
    });
    i && (s = jn(s, a, c));
  }
  return i ? s : e;
}
function jn(e, t, n) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: {
        ...e.structure.payload,
        [t.property]: n
      }
    }
  } : e;
}
function Nf(e, t, n, o = []) {
  const i = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    i.set(a.nodeId, a);
  const s = t.map((a) => i.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), jn(e.owner, e.slot, s);
}
function jf(e, t) {
  return {
    ...e,
    structure: _f(e.structure, t)
  };
}
function Sf(e, t) {
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
function yr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Af(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? kf(t) : n;
}
function qc(e, t, n, o = {}) {
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
      icon: Yo(t),
      childSlots: De(e),
      acceptsInbound: Tf(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Uc(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function Yo(e) {
  if (!e) return "activity";
  const t = Cf(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), i = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : i.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Cf(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Ef(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function If(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function kf(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Af(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Pn,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Jt,
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
function _f(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const i of n) {
    if (!Br(i)) continue;
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
function Df(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Yc(e) {
  if (e.structure?.kind !== Jt) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const i = n.source, s = n.target;
    if (!i?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Rf) : [];
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
function Uc(e, t) {
  const n = Ts(e.cases);
  if (Mf(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...go(t?.designFacets),
    ...go(t?.ports),
    ...go(t?.outputs)
  ];
  if (o.length > 0) return Pf(o);
  const i = Ts(e.outcomes);
  return i.length > 0 ? i.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function Tf(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Ao(e, t, n, o) {
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
function $f(e, t, n) {
  const o = Ao(t.source, n, t.sourceHandle ?? "Done", void 0), i = Ao(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, i);
}
function Ui(e) {
  return Array.isArray(e) ? e.filter(Lf) : null;
}
function Mf(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function go(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Br(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...go(n.ports));
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
function Pf(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ts(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function $s(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const i = t(o);
    i && n.set(i, [...n.get(i) ?? [], o]);
  }
  return n;
}
function Zc(e) {
  return [...e].sort((t, n) => Ms(n).localeCompare(Ms(t)))[0];
}
function Ms(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Rf(e) {
  return Br(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Br(e) {
  return typeof e == "object" && e !== null;
}
function Lf(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function zf(e) {
  return e.kind === Pn ? "sequence" : e.kind === Jt ? "flowchart" : "generic";
}
function Vf(e) {
  return e.kind === Pn || e.kind === Jt, "Activities";
}
function Of(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Hf(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const _o = "workflow", Wf = /* @__PURE__ */ new Set([Pn, Jt]);
function Bf(e) {
  const t = e?.structure?.kind;
  return !!t && Wf.has(t);
}
function Gc(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(ko) : [];
}
function Ff(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Kf(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== _o ? t : _o
  };
}
function Jc(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return Jc(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Xf(e) {
  if (!e) return "";
  const t = [`workflow:${Ps(e.variables)}`], n = (o) => {
    const i = De(o), s = i.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${o.nodeId}:${Ps(Gc(o))}>${s.join(",")}`), i.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function Ps(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function qf(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const Ne = "/_elsa/workflow-management", Yf = "/publishing", xn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Uf(e) {
  return Mc({
    queryKey: xn.activityAvailabilitySettings,
    queryFn: () => hp(e)
  });
}
function Zf(e) {
  return Mc({
    queryKey: xn.activityAvailabilityDiagnostics,
    queryFn: () => tl(e)
  });
}
function Gf(e) {
  const t = Vd();
  return Od({
    mutationFn: (n) => gp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: xn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: xn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: xn.activities });
    }
  });
}
async function Jf(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${Ne}/definitions?${n.toString()}`);
}
async function Qf(e, t) {
  const n = await e.http.getJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: qo(n.draft.state) } } : n;
}
async function ep(e, t, n) {
  const o = await e.http.postJson(
    `${Ne}/design/scoped-variables/analyze`,
    { state: Mn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const Zi = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function tp(e, t, n) {
  const o = de(() => Xf(t), [t]), [i, s] = F(() => Zi("loading"));
  return G(() => {
    if (!t) {
      s(Zi("unavailable"));
      return;
    }
    let a = !1;
    return s((c) => ({ ...c, status: "loading" })), ep(e, t, n).then(
      (c) => {
        a || s({ ...c, status: "ready" });
      },
      () => {
        a || s(Zi("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, o]), i;
}
async function np(e, t) {
  const n = await e.http.getJson(`${Ne}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: qo(n.state) };
}
async function op(e, t) {
  return e.http.postJson(`${Ne}/definitions`, t);
}
async function ip(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
}
async function rp(e, t) {
  await e.http.postJson(`${Ne}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function sp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function ap(e, t, n) {
  return e.http.requestJson(
    `${Ne}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function cp(e, t) {
  const n = await e.http.putJson(
    `${Ne}/drafts/${encodeURIComponent(t.id)}`,
    { state: Mn(t.state), layout: t.layout }
  );
  return { ...n, state: qo(n.state) };
}
async function lp(e, t) {
  return e.http.postJson(`${Ne}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function up(e, t) {
  return e.http.postJson(`${Ne}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function dp(e, t) {
  const n = { ...t, state: Mn(t.state) };
  try {
    return await e.http.postJson(`${Yf}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const i = Np(o);
    if (i) return i;
    throw o;
  }
}
async function Qc(e, t) {
  return e.http.postJson(`${Ne}/executables/${encodeURIComponent(t)}/run`, {});
}
async function el(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function fp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function pp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Fr(e) {
  return e.http.getJson(`${Ne}/activities`);
}
async function hp(e) {
  return e.http.getJson(`${Ne}/activities/availability/settings`);
}
async function gp(e, t) {
  return e.http.putJson(`${Ne}/activities/availability/settings`, t);
}
async function tl(e) {
  return e.http.getJson(`${Ne}/activities/availability/diagnostics`);
}
async function yp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Rs(t) : Rs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function mp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : yo;
}
async function xp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => wp(o));
}
function wp(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, o = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || o;
}
async function vp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => bp(o));
}
function bp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Uo(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (i) {
      n = i;
    }
  throw n;
}
function Rs(e) {
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
function Np(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ls(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ls(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ls(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const yo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], jp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], Sp = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function yt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? jp[e] ?? "Available" : "Available";
}
function Do(e) {
  const t = yt(e);
  return Sp[t] ?? t;
}
function Cp(e) {
  return yt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Ep(e) {
  return yt(e) !== "Available";
}
function Ip(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function kp(e) {
  return e === "Only" ? 1 : 0;
}
function zs(e) {
  const t = e?.rules;
  return {
    mode: Ip(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function Ap(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function _p(e) {
  return [...e?.items ?? []].filter(Ap).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => To(t).localeCompare(To(n)));
}
function Dp(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = yt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Vs(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function To(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return Tp(n) || e?.activityTypeKey || "Activity";
}
function Tp(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function $p(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => Ep(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((i) => i && n.has(i)) : !1) ?? null;
}
function Mp({ context: e }) {
  const t = Uf(e), n = Zf(e), o = Gf(e), i = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = F(() => zs(i)), [d, f] = F(""), [p, h] = F(null);
  G(() => {
    l(zs(i));
  }, [i]);
  const g = de(() => _p(s), [s]), m = de(() => Dp(s), [s]), w = s?.sets ?? [], x = de(() => {
    const _ = d.trim().toLowerCase();
    return _ ? g.filter(
      (R) => To(R).toLowerCase().includes(_) || (R.activityTypeKey ?? "").toLowerCase().includes(_)
    ) : g;
  }, [g, d]), N = new Set(u.activityTypes), y = new Set(u.sets), v = g.filter((_) => yt(_.state) === "BlockedByHostBaseline").length, j = g.filter((_) => yt(_.state) === "HiddenByManagementSettings").length, b = o.error ?? t.error ?? n.error, S = b instanceof Error ? b.message : b ? "Activity availability could not be loaded." : null, E = (_) => l((R) => ({ ...R, mode: _ })), D = (_) => l((R) => ({ ...R, activityTypes: Vs(R.activityTypes, _) })), L = (_) => l((R) => ({ ...R, sets: Vs(R.sets, _) })), A = () => {
    h(null), o.mutate(
      {
        scope: i?.scope ?? "host-default",
        mode: kp(u.mode),
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
          /* @__PURE__ */ r.jsx(kd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ r.jsx(Ec, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => E("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(js, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => E("Only"), disabled: a || c, children: [
          /* @__PURE__ */ r.jsx(Ss, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ r.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Ss, { size: 14 }),
          " ",
          v,
          " host blocked"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(js, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Co, { size: 14 }),
          " ",
          m.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(zr, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: w.map((_) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx("input", { type: "checkbox", checked: y.has(_.name), disabled: a || c, onChange: () => L(_.name) }),
          /* @__PURE__ */ r.jsx("span", { children: _.name }),
          /* @__PURE__ */ r.jsx("code", { children: (_.activityTypeKeys ?? []).length })
        ] }, _.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Ic, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ r.jsx(Fo, { size: 14 }),
            /* @__PURE__ */ r.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (_) => f(_.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && x.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          x.map((_) => {
            const C = yt(_.state) === "BlockedByHostBaseline", I = _.activityTypeKey ?? _.activityDefinitionId ?? "";
            return /* @__PURE__ */ r.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(I),
                  disabled: a || c || C,
                  onChange: () => D(I)
                }
              ),
              /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ r.jsx("strong", { children: To(_) }),
                /* @__PURE__ */ r.jsx("code", { children: _.activityTypeKey })
              ] }),
              /* @__PURE__ */ r.jsx("em", { className: `availability-state ${Cp(_.state)}`, children: Do(_.state) })
            ] }, I);
          })
        ] })
      ] }),
      m.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Co, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: m.map((_) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: _.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Do(_.state) })
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
var Pp = { value: () => {
} };
function Zo() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new mo(n);
}
function mo(e) {
  this._ = e;
}
function Rp(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", i = n.indexOf(".");
    if (i >= 0 && (o = n.slice(i + 1), n = n.slice(0, i)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
mo.prototype = Zo.prototype = {
  constructor: mo,
  on: function(e, t) {
    var n = this._, o = Rp(e + "", n), i, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((i = (e = o[s]).type) && (i = Lp(n[i], e.name))) return i;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (i = (e = o[s]).type) n[i] = Os(n[i], e.name, t);
      else if (t == null) for (i in n) n[i] = Os(n[i], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new mo(e);
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
function Lp(e, t) {
  for (var n = 0, o = e.length, i; n < o; ++n)
    if ((i = e[n]).name === t)
      return i.value;
}
function Os(e, t, n) {
  for (var o = 0, i = e.length; o < i; ++o)
    if (e[o].name === t) {
      e[o] = Pp, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var mr = "http://www.w3.org/1999/xhtml";
const Hs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: mr,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Go(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Hs.hasOwnProperty(t) ? { space: Hs[t], local: e } : e;
}
function zp(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === mr && t.documentElement.namespaceURI === mr ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Vp(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function nl(e) {
  var t = Go(e);
  return (t.local ? Vp : zp)(t);
}
function Op() {
}
function Kr(e) {
  return e == null ? Op : function() {
    return this.querySelector(e);
  };
}
function Hp(e) {
  typeof e != "function" && (e = Kr(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new ze(o, this._parents);
}
function Wp(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Bp() {
  return [];
}
function ol(e) {
  return e == null ? Bp : function() {
    return this.querySelectorAll(e);
  };
}
function Fp(e) {
  return function() {
    return Wp(e.apply(this, arguments));
  };
}
function Kp(e) {
  typeof e == "function" ? e = Fp(e) : e = ol(e);
  for (var t = this._groups, n = t.length, o = [], i = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), i.push(u));
  return new ze(o, i);
}
function il(e) {
  return function() {
    return this.matches(e);
  };
}
function rl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Xp = Array.prototype.find;
function qp(e) {
  return function() {
    return Xp.call(this.children, e);
  };
}
function Yp() {
  return this.firstElementChild;
}
function Up(e) {
  return this.select(e == null ? Yp : qp(typeof e == "function" ? e : rl(e)));
}
var Zp = Array.prototype.filter;
function Gp() {
  return Array.from(this.children);
}
function Jp(e) {
  return function() {
    return Zp.call(this.children, e);
  };
}
function Qp(e) {
  return this.selectAll(e == null ? Gp : Jp(typeof e == "function" ? e : rl(e)));
}
function eh(e) {
  typeof e != "function" && (e = il(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ze(o, this._parents);
}
function sl(e) {
  return new Array(e.length);
}
function th() {
  return new ze(this._enter || this._groups.map(sl), this._parents);
}
function $o(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
$o.prototype = {
  constructor: $o,
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
function nh(e) {
  return function() {
    return e;
  };
}
function oh(e, t, n, o, i, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new $o(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (i[a] = c);
}
function ih(e, t, n, o, i, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? i[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new $o(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (i[c] = u);
}
function rh(e) {
  return e.__data__;
}
function sh(e, t) {
  if (!arguments.length) return Array.from(this, rh);
  var n = t ? ih : oh, o = this._parents, i = this._groups;
  typeof e != "function" && (e = nh(e));
  for (var s = i.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], f = i[l], p = f.length, h = ah(e.call(d, d && d.__data__, l, o)), g = h.length, m = c[l] = new Array(g), w = a[l] = new Array(g), x = u[l] = new Array(p);
    n(d, f, m, w, x, h, t);
    for (var N = 0, y = 0, v, j; N < g; ++N)
      if (v = m[N]) {
        for (N >= y && (y = N + 1); !(j = w[y]) && ++y < g; ) ;
        v._next = j || null;
      }
  }
  return a = new ze(a, o), a._enter = c, a._exit = u, a;
}
function ah(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function ch() {
  return new ze(this._exit || this._groups.map(sl), this._parents);
}
function lh(e, t, n) {
  var o = this.enter(), i = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (i = t(i), i && (i = i.selection())), n == null ? s.remove() : n(s), o && i ? o.merge(i).order() : i;
}
function uh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, i = n.length, s = o.length, a = Math.min(i, s), c = new Array(i), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, p = c[u] = new Array(f), h, g = 0; g < f; ++g)
      (h = l[g] || d[g]) && (p[g] = h);
  for (; u < i; ++u)
    c[u] = n[u];
  return new ze(c, this._parents);
}
function dh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], i = o.length - 1, s = o[i], a; --i >= 0; )
      (a = o[i]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function fh(e) {
  e || (e = ph);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, o = n.length, i = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = i[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new ze(i, this._parents).order();
}
function ph(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function hh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function gh() {
  return Array.from(this);
}
function yh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length; i < s; ++i) {
      var a = o[i];
      if (a) return a;
    }
  return null;
}
function mh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function xh() {
  return !this.node();
}
function wh(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var i = t[n], s = 0, a = i.length, c; s < a; ++s)
      (c = i[s]) && e.call(c, c.__data__, s, i);
  return this;
}
function vh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function bh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Nh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function jh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Sh(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Ch(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Eh(e, t) {
  var n = Go(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? bh : vh : typeof t == "function" ? n.local ? Ch : Sh : n.local ? jh : Nh)(n, t));
}
function al(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Ih(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function kh(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Ah(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function _h(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Ih : typeof t == "function" ? Ah : kh)(e, t, n ?? "")) : Wt(this.node(), e);
}
function Wt(e, t) {
  return e.style.getPropertyValue(t) || al(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Dh(e) {
  return function() {
    delete this[e];
  };
}
function Th(e, t) {
  return function() {
    this[e] = t;
  };
}
function $h(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Mh(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Dh : typeof t == "function" ? $h : Th)(e, t)) : this.node()[e];
}
function cl(e) {
  return e.trim().split(/^|\s+/);
}
function Xr(e) {
  return e.classList || new ll(e);
}
function ll(e) {
  this._node = e, this._names = cl(e.getAttribute("class") || "");
}
ll.prototype = {
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
function ul(e, t) {
  for (var n = Xr(e), o = -1, i = t.length; ++o < i; ) n.add(t[o]);
}
function dl(e, t) {
  for (var n = Xr(e), o = -1, i = t.length; ++o < i; ) n.remove(t[o]);
}
function Ph(e) {
  return function() {
    ul(this, e);
  };
}
function Rh(e) {
  return function() {
    dl(this, e);
  };
}
function Lh(e, t) {
  return function() {
    (t.apply(this, arguments) ? ul : dl)(this, e);
  };
}
function zh(e, t) {
  var n = cl(e + "");
  if (arguments.length < 2) {
    for (var o = Xr(this.node()), i = -1, s = n.length; ++i < s; ) if (!o.contains(n[i])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Lh : t ? Ph : Rh)(n, t));
}
function Vh() {
  this.textContent = "";
}
function Oh(e) {
  return function() {
    this.textContent = e;
  };
}
function Hh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Wh(e) {
  return arguments.length ? this.each(e == null ? Vh : (typeof e == "function" ? Hh : Oh)(e)) : this.node().textContent;
}
function Bh() {
  this.innerHTML = "";
}
function Fh(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Kh(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Xh(e) {
  return arguments.length ? this.each(e == null ? Bh : (typeof e == "function" ? Kh : Fh)(e)) : this.node().innerHTML;
}
function qh() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Yh() {
  return this.each(qh);
}
function Uh() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Zh() {
  return this.each(Uh);
}
function Gh(e) {
  var t = typeof e == "function" ? e : nl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Jh() {
  return null;
}
function Qh(e, t) {
  var n = typeof e == "function" ? e : nl(e), o = t == null ? Jh : typeof t == "function" ? t : Kr(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function eg() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function tg() {
  return this.each(eg);
}
function ng() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function og() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function ig(e) {
  return this.select(e ? og : ng);
}
function rg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function sg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function ag(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function cg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, i = t.length, s; n < i; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function lg(e, t, n) {
  return function() {
    var o = this.__on, i, s = sg(t);
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
function ug(e, t, n) {
  var o = ag(e + ""), i, s = o.length, a;
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
  for (c = t ? lg : cg, i = 0; i < s; ++i) this.each(c(o[i], t, n));
  return this;
}
function fl(e, t, n) {
  var o = al(e), i = o.CustomEvent;
  typeof i == "function" ? i = new i(t, n) : (i = o.document.createEvent("Event"), n ? (i.initEvent(t, n.bubbles, n.cancelable), i.detail = n.detail) : i.initEvent(t, !1, !1)), e.dispatchEvent(i);
}
function dg(e, t) {
  return function() {
    return fl(this, e, t);
  };
}
function fg(e, t) {
  return function() {
    return fl(this, e, t.apply(this, arguments));
  };
}
function pg(e, t) {
  return this.each((typeof t == "function" ? fg : dg)(e, t));
}
function* hg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], i = 0, s = o.length, a; i < s; ++i)
      (a = o[i]) && (yield a);
}
var pl = [null];
function ze(e, t) {
  this._groups = e, this._parents = t;
}
function Rn() {
  return new ze([[document.documentElement]], pl);
}
function gg() {
  return this;
}
ze.prototype = Rn.prototype = {
  constructor: ze,
  select: Hp,
  selectAll: Kp,
  selectChild: Up,
  selectChildren: Qp,
  filter: eh,
  data: sh,
  enter: th,
  exit: ch,
  join: lh,
  merge: uh,
  selection: gg,
  order: dh,
  sort: fh,
  call: hh,
  nodes: gh,
  node: yh,
  size: mh,
  empty: xh,
  each: wh,
  attr: Eh,
  style: _h,
  property: Mh,
  classed: zh,
  text: Wh,
  html: Xh,
  raise: Yh,
  lower: Zh,
  append: Gh,
  insert: Qh,
  remove: tg,
  clone: ig,
  datum: rg,
  on: ug,
  dispatch: pg,
  [Symbol.iterator]: hg
};
function Pe(e) {
  return typeof e == "string" ? new ze([[document.querySelector(e)]], [document.documentElement]) : new ze([[e]], pl);
}
function yg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function We(e, t) {
  if (e = yg(e), t === void 0 && (t = e.currentTarget), t) {
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
const mg = { passive: !1 }, Sn = { capture: !0, passive: !1 };
function Gi(e) {
  e.stopImmediatePropagation();
}
function Lt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function hl(e) {
  var t = e.document.documentElement, n = Pe(e).on("dragstart.drag", Lt, Sn);
  "onselectstart" in t ? n.on("selectstart.drag", Lt, Sn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function gl(e, t) {
  var n = e.document.documentElement, o = Pe(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Lt, Sn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const no = (e) => () => e;
function xr(e, {
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
xr.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function xg(e) {
  return !e.ctrlKey && !e.button;
}
function wg() {
  return this.parentNode;
}
function vg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function bg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function yl() {
  var e = xg, t = wg, n = vg, o = bg, i = {}, s = Zo("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(o).on("touchstart.drag", w).on("touchmove.drag", x, mg).on("touchend.drag touchcancel.drag", N).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var b = y(this, t.call(this, v, j), v, j, "mouse");
      b && (Pe(v.view).on("mousemove.drag", g, Sn).on("mouseup.drag", m, Sn), hl(v.view), Gi(v), l = !1, c = v.clientX, u = v.clientY, b("start", v));
    }
  }
  function g(v) {
    if (Lt(v), !l) {
      var j = v.clientX - c, b = v.clientY - u;
      l = j * j + b * b > f;
    }
    i.mouse("drag", v);
  }
  function m(v) {
    Pe(v.view).on("mousemove.drag mouseup.drag", null), gl(v.view, l), Lt(v), i.mouse("end", v);
  }
  function w(v, j) {
    if (e.call(this, v, j)) {
      var b = v.changedTouches, S = t.call(this, v, j), E = b.length, D, L;
      for (D = 0; D < E; ++D)
        (L = y(this, S, v, j, b[D].identifier, b[D])) && (Gi(v), L("start", v, b[D]));
    }
  }
  function x(v) {
    var j = v.changedTouches, b = j.length, S, E;
    for (S = 0; S < b; ++S)
      (E = i[j[S].identifier]) && (Lt(v), E("drag", v, j[S]));
  }
  function N(v) {
    var j = v.changedTouches, b = j.length, S, E;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < b; ++S)
      (E = i[j[S].identifier]) && (Gi(v), E("end", v, j[S]));
  }
  function y(v, j, b, S, E, D) {
    var L = s.copy(), A = We(D || b, j), _, R, C;
    if ((C = n.call(v, new xr("beforestart", {
      sourceEvent: b,
      target: p,
      identifier: E,
      active: a,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: L
    }), S)) != null)
      return _ = C.x - A[0] || 0, R = C.y - A[1] || 0, function I(k, T, M) {
        var $ = A, B;
        switch (k) {
          case "start":
            i[E] = I, B = a++;
            break;
          case "end":
            delete i[E], --a;
          // falls through
          case "drag":
            A = We(M || T, j), B = a;
            break;
        }
        L.call(
          k,
          v,
          new xr(k, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: E,
            active: B,
            x: A[0] + _,
            y: A[1] + R,
            dx: A[0] - $[0],
            dy: A[1] - $[1],
            dispatch: L
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : no(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : no(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : no(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (o = typeof v == "function" ? v : no(!!v), p) : o;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function qr(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function ml(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Ln() {
}
var Cn = 0.7, Mo = 1 / Cn, zt = "\\s*([+-]?\\d+)\\s*", En = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ue = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ng = /^#([0-9a-f]{3,8})$/, jg = new RegExp(`^rgb\\(${zt},${zt},${zt}\\)$`), Sg = new RegExp(`^rgb\\(${Ue},${Ue},${Ue}\\)$`), Cg = new RegExp(`^rgba\\(${zt},${zt},${zt},${En}\\)$`), Eg = new RegExp(`^rgba\\(${Ue},${Ue},${Ue},${En}\\)$`), Ig = new RegExp(`^hsl\\(${En},${Ue},${Ue}\\)$`), kg = new RegExp(`^hsla\\(${En},${Ue},${Ue},${En}\\)$`), Ws = {
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
qr(Ln, wt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Bs,
  // Deprecated! Use color.formatHex.
  formatHex: Bs,
  formatHex8: Ag,
  formatHsl: _g,
  formatRgb: Fs,
  toString: Fs
});
function Bs() {
  return this.rgb().formatHex();
}
function Ag() {
  return this.rgb().formatHex8();
}
function _g() {
  return xl(this).formatHsl();
}
function Fs() {
  return this.rgb().formatRgb();
}
function wt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ng.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Ks(t) : n === 3 ? new Me(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? oo(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? oo(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = jg.exec(e)) ? new Me(t[1], t[2], t[3], 1) : (t = Sg.exec(e)) ? new Me(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Cg.exec(e)) ? oo(t[1], t[2], t[3], t[4]) : (t = Eg.exec(e)) ? oo(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ig.exec(e)) ? Ys(t[1], t[2] / 100, t[3] / 100, 1) : (t = kg.exec(e)) ? Ys(t[1], t[2] / 100, t[3] / 100, t[4]) : Ws.hasOwnProperty(e) ? Ks(Ws[e]) : e === "transparent" ? new Me(NaN, NaN, NaN, 0) : null;
}
function Ks(e) {
  return new Me(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function oo(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Me(e, t, n, o);
}
function Dg(e) {
  return e instanceof Ln || (e = wt(e)), e ? (e = e.rgb(), new Me(e.r, e.g, e.b, e.opacity)) : new Me();
}
function wr(e, t, n, o) {
  return arguments.length === 1 ? Dg(e) : new Me(e, t, n, o ?? 1);
}
function Me(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
qr(Me, wr, ml(Ln, {
  brighter(e) {
    return e = e == null ? Mo : Math.pow(Mo, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Cn : Math.pow(Cn, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Me(mt(this.r), mt(this.g), mt(this.b), Po(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Xs,
  // Deprecated! Use color.formatHex.
  formatHex: Xs,
  formatHex8: Tg,
  formatRgb: qs,
  toString: qs
}));
function Xs() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}`;
}
function Tg() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}${gt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function qs() {
  const e = Po(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${mt(this.r)}, ${mt(this.g)}, ${mt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Po(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function mt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function gt(e) {
  return e = mt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ys(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Be(e, t, n, o);
}
function xl(e) {
  if (e instanceof Be) return new Be(e.h, e.s, e.l, e.opacity);
  if (e instanceof Ln || (e = wt(e)), !e) return new Be();
  if (e instanceof Be) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, i = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - i, u = (s + i) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + i : 2 - s - i, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Be(a, c, u, e.opacity);
}
function $g(e, t, n, o) {
  return arguments.length === 1 ? xl(e) : new Be(e, t, n, o ?? 1);
}
function Be(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
qr(Be, $g, ml(Ln, {
  brighter(e) {
    return e = e == null ? Mo : Math.pow(Mo, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Cn : Math.pow(Cn, e), new Be(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, i = 2 * n - o;
    return new Me(
      Ji(e >= 240 ? e - 240 : e + 120, i, o),
      Ji(e, i, o),
      Ji(e < 120 ? e + 240 : e - 120, i, o),
      this.opacity
    );
  },
  clamp() {
    return new Be(Us(this.h), io(this.s), io(this.l), Po(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Po(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Us(this.h)}, ${io(this.s) * 100}%, ${io(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Us(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function io(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Ji(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Yr = (e) => () => e;
function Mg(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Pg(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function Rg(e) {
  return (e = +e) == 1 ? wl : function(t, n) {
    return n - t ? Pg(t, n, e) : Yr(isNaN(t) ? n : t);
  };
}
function wl(e, t) {
  var n = t - e;
  return n ? Mg(e, n) : Yr(isNaN(e) ? t : e);
}
const Ro = (function e(t) {
  var n = Rg(t);
  function o(i, s) {
    var a = n((i = wr(i)).r, (s = wr(s)).r), c = n(i.g, s.g), u = n(i.b, s.b), l = wl(i.opacity, s.opacity);
    return function(d) {
      return i.r = a(d), i.g = c(d), i.b = u(d), i.opacity = l(d), i + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Lg(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), i;
  return function(s) {
    for (i = 0; i < n; ++i) o[i] = e[i] * (1 - s) + t[i] * s;
    return o;
  };
}
function zg(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Vg(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, i = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) i[a] = wn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = i[a](c);
    return s;
  };
}
function Og(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function Ye(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function Hg(e, t) {
  var n = {}, o = {}, i;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (i in t)
    i in e ? n[i] = wn(e[i], t[i]) : o[i] = t[i];
  return function(s) {
    for (i in n) o[i] = n[i](s);
    return o;
  };
}
var vr = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Qi = new RegExp(vr.source, "g");
function Wg(e) {
  return function() {
    return e;
  };
}
function Bg(e) {
  return function(t) {
    return e(t) + "";
  };
}
function vl(e, t) {
  var n = vr.lastIndex = Qi.lastIndex = 0, o, i, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = vr.exec(e)) && (i = Qi.exec(t)); )
    (s = i.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (i = i[0]) ? c[a] ? c[a] += i : c[++a] = i : (c[++a] = null, u.push({ i: a, x: Ye(o, i) })), n = Qi.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Bg(u[0].x) : Wg(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function wn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Yr(t) : (n === "number" ? Ye : n === "string" ? (o = wt(t)) ? (t = o, Ro) : vl : t instanceof wt ? Ro : t instanceof Date ? Og : zg(t) ? Lg : Array.isArray(t) ? Vg : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Hg : Ye)(e, t);
}
var Zs = 180 / Math.PI, br = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function bl(e, t, n, o, i, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: i,
    translateY: s,
    rotate: Math.atan2(t, e) * Zs,
    skewX: Math.atan(u) * Zs,
    scaleX: a,
    scaleY: c
  };
}
var ro;
function Fg(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? br : bl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Kg(e) {
  return e == null || (ro || (ro = document.createElementNS("http://www.w3.org/2000/svg", "g")), ro.setAttribute("transform", e), !(e = ro.transform.baseVal.consolidate())) ? br : (e = e.matrix, bl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Nl(e, t, n, o) {
  function i(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push("translate(", null, t, null, n);
      g.push({ i: m - 4, x: Ye(l, f) }, { i: m - 2, x: Ye(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(i(f) + "rotate(", null, o) - 2, x: Ye(l, d) })) : d && f.push(i(f) + "rotate(" + d + o);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(i(f) + "skewX(", null, o) - 2, x: Ye(l, d) }) : d && f.push(i(f) + "skewX(" + d + o);
  }
  function u(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var m = h.push(i(h) + "scale(", null, ",", null, ")");
      g.push({ i: m - 4, x: Ye(l, f) }, { i: m - 2, x: Ye(d, p) });
    } else (f !== 1 || p !== 1) && h.push(i(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var g = -1, m = p.length, w; ++g < m; ) f[(w = p[g]).i] = w.x(h);
      return f.join("");
    };
  };
}
var Xg = Nl(Fg, "px, ", "px)", "deg)"), qg = Nl(Kg, ", ", ")", ")"), Yg = 1e-12;
function Gs(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ug(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Zg(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const xo = (function e(t, n, o) {
  function i(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, g = f - u, m = h * h + g * g, w, x;
    if (m < Yg)
      x = Math.log(p / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * x)
        ];
      };
    else {
      var N = Math.sqrt(m), y = (p * p - l * l + o * m) / (2 * l * n * N), v = (p * p - l * l - o * m) / (2 * p * n * N), j = Math.log(Math.sqrt(y * y + 1) - y), b = Math.log(Math.sqrt(v * v + 1) - v);
      x = (b - j) / t, w = function(S) {
        var E = S * x, D = Gs(j), L = l / (n * N) * (D * Zg(t * E + j) - Ug(j));
        return [
          c + L * h,
          u + L * g,
          l * D / Gs(t * E + j)
        ];
      };
    }
    return w.duration = x * 1e3 * t / Math.SQRT2, w;
  }
  return i.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, i;
})(Math.SQRT2, 2, 4);
var Bt = 0, ln = 0, sn = 0, jl = 1e3, Lo, un, zo = 0, vt = 0, Jo = 0, In = typeof performance == "object" && performance.now ? performance : Date, Sl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ur() {
  return vt || (Sl(Gg), vt = In.now() + Jo);
}
function Gg() {
  vt = 0;
}
function Vo() {
  this._call = this._time = this._next = null;
}
Vo.prototype = Cl.prototype = {
  constructor: Vo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ur() : +n) + (t == null ? 0 : +t), !this._next && un !== this && (un ? un._next = this : Lo = this, un = this), this._call = e, this._time = n, Nr();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Nr());
  }
};
function Cl(e, t, n) {
  var o = new Vo();
  return o.restart(e, t, n), o;
}
function Jg() {
  Ur(), ++Bt;
  for (var e = Lo, t; e; )
    (t = vt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Bt;
}
function Js() {
  vt = (zo = In.now()) + Jo, Bt = ln = 0;
  try {
    Jg();
  } finally {
    Bt = 0, ey(), vt = 0;
  }
}
function Qg() {
  var e = In.now(), t = e - zo;
  t > jl && (Jo -= t, zo = e);
}
function ey() {
  for (var e, t = Lo, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Lo = n);
  un = e, Nr(o);
}
function Nr(e) {
  if (!Bt) {
    ln && (ln = clearTimeout(ln));
    var t = e - vt;
    t > 24 ? (e < 1 / 0 && (ln = setTimeout(Js, e - In.now() - Jo)), sn && (sn = clearInterval(sn))) : (sn || (zo = In.now(), sn = setInterval(Qg, jl)), Bt = 1, Sl(Js));
  }
}
function Qs(e, t, n) {
  var o = new Vo();
  return t = t == null ? 0 : +t, o.restart((i) => {
    o.stop(), e(i + t);
  }, t, n), o;
}
var ty = Zo("start", "end", "cancel", "interrupt"), ny = [], El = 0, ea = 1, jr = 2, wo = 3, ta = 4, Sr = 5, vo = 6;
function Qo(e, t, n, o, i, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  oy(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: i,
    // For context during callback.
    on: ty,
    tween: ny,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: El
  });
}
function Zr(e, t) {
  var n = Xe(e, t);
  if (n.state > El) throw new Error("too late; already scheduled");
  return n;
}
function Je(e, t) {
  var n = Xe(e, t);
  if (n.state > wo) throw new Error("too late; already running");
  return n;
}
function Xe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function oy(e, t, n) {
  var o = e.__transition, i;
  o[t] = n, n.timer = Cl(s, 0, n.time);
  function s(l) {
    n.state = ea, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ea) return u();
    for (d in o)
      if (h = o[d], h.name === n.name) {
        if (h.state === wo) return Qs(a);
        h.state === ta ? (h.state = vo, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[d]) : +d < t && (h.state = vo, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[d]);
      }
    if (Qs(function() {
      n.state === wo && (n.state = ta, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = jr, n.on.call("start", e, e.__data__, n.index, n.group), n.state === jr) {
      for (n.state = wo, i = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (i[++f] = h);
      i.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Sr, 1), f = -1, p = i.length; ++f < p; )
      i[f].call(e, d);
    n.state === Sr && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = vo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function bo(e, t) {
  var n = e.__transition, o, i, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      i = o.state > jr && o.state < Sr, o.state = vo, o.timer.stop(), o.on.call(i ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function iy(e) {
  return this.each(function() {
    bo(this, e);
  });
}
function ry(e, t) {
  var n, o;
  return function() {
    var i = Je(this, e), s = i.tween;
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
function sy(e, t, n) {
  var o, i;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = Je(this, e), a = s.tween;
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
function ay(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Xe(this.node(), n).tween, i = 0, s = o.length, a; i < s; ++i)
      if ((a = o[i]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? ry : sy)(n, e, t));
}
function Gr(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var i = Je(this, o);
    (i.value || (i.value = {}))[t] = n.apply(this, arguments);
  }), function(i) {
    return Xe(i, o).value[t];
  };
}
function Il(e, t) {
  var n;
  return (typeof t == "number" ? Ye : t instanceof wt ? Ro : (n = wt(t)) ? (t = n, Ro) : vl)(e, t);
}
function cy(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function ly(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function uy(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function dy(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function fy(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function py(e, t, n) {
  var o, i, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c)));
  };
}
function hy(e, t) {
  var n = Go(e), o = n === "transform" ? qg : Il;
  return this.attrTween(e, typeof t == "function" ? (n.local ? py : fy)(n, o, Gr(this, "attr." + e, t)) : t == null ? (n.local ? ly : cy)(n) : (n.local ? dy : uy)(n, o, t));
}
function gy(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function yy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function my(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && yy(e, s)), n;
  }
  return i._value = t, i;
}
function xy(e, t) {
  var n, o;
  function i() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && gy(e, s)), n;
  }
  return i._value = t, i;
}
function wy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Go(e);
  return this.tween(n, (o.local ? my : xy)(o, t));
}
function vy(e, t) {
  return function() {
    Zr(this, e).delay = +t.apply(this, arguments);
  };
}
function by(e, t) {
  return t = +t, function() {
    Zr(this, e).delay = t;
  };
}
function Ny(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? vy : by)(t, e)) : Xe(this.node(), t).delay;
}
function jy(e, t) {
  return function() {
    Je(this, e).duration = +t.apply(this, arguments);
  };
}
function Sy(e, t) {
  return t = +t, function() {
    Je(this, e).duration = t;
  };
}
function Cy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? jy : Sy)(t, e)) : Xe(this.node(), t).duration;
}
function Ey(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    Je(this, e).ease = t;
  };
}
function Iy(e) {
  var t = this._id;
  return arguments.length ? this.each(Ey(t, e)) : Xe(this.node(), t).ease;
}
function ky(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    Je(this, e).ease = n;
  };
}
function Ay(e) {
  if (typeof e != "function") throw new Error();
  return this.each(ky(this._id, e));
}
function _y(e) {
  typeof e != "function" && (e = il(e));
  for (var t = this._groups, n = t.length, o = new Array(n), i = 0; i < n; ++i)
    for (var s = t[i], a = s.length, c = o[i] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new it(o, this._parents, this._name, this._id);
}
function Dy(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, i = n.length, s = Math.min(o, i), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < o; ++c)
    a[c] = t[c];
  return new it(a, this._parents, this._name, this._id);
}
function Ty(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $y(e, t, n) {
  var o, i, s = Ty(t) ? Zr : Je;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (i = (o = c).copy()).on(t, n), a.on = i;
  };
}
function My(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Xe(this.node(), n).on.on(e) : this.each($y(n, e, t));
}
function Py(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Ry() {
  return this.on("end.remove", Py(this._id));
}
function Ly(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Kr(e));
  for (var o = this._groups, i = o.length, s = new Array(i), a = 0; a < i; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, Qo(l[p], t, n, p, l, Xe(d, n)));
  return new it(s, this._parents, t, n);
}
function zy(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ol(e));
  for (var o = this._groups, i = o.length, s = [], a = [], c = 0; c < i; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, g = Xe(d, n), m = 0, w = p.length; m < w; ++m)
          (h = p[m]) && Qo(h, t, n, m, p, g);
        s.push(p), a.push(d);
      }
  return new it(s, a, t, n);
}
var Vy = Rn.prototype.constructor;
function Oy() {
  return new Vy(this._groups, this._parents);
}
function Hy(e, t) {
  var n, o, i;
  return function() {
    var s = Wt(this, e), a = (this.style.removeProperty(e), Wt(this, e));
    return s === a ? null : s === n && a === o ? i : i = t(n = s, o = a);
  };
}
function kl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Wy(e, t, n) {
  var o, i = n + "", s;
  return function() {
    var a = Wt(this, e);
    return a === i ? null : a === o ? s : s = t(o = a, n);
  };
}
function By(e, t, n) {
  var o, i, s;
  return function() {
    var a = Wt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Wt(this, e))), a === u ? null : a === o && u === i ? s : (i = u, s = t(o = a, c));
  };
}
function Fy(e, t) {
  var n, o, i, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = Je(this, e), l = u.on, d = u.value[s] == null ? c || (c = kl(t)) : void 0;
    (l !== n || i !== d) && (o = (n = l).copy()).on(a, i = d), u.on = o;
  };
}
function Ky(e, t, n) {
  var o = (e += "") == "transform" ? Xg : Il;
  return t == null ? this.styleTween(e, Hy(e, o)).on("end.style." + e, kl(e)) : typeof t == "function" ? this.styleTween(e, By(e, o, Gr(this, "style." + e, t))).each(Fy(this._id, e)) : this.styleTween(e, Wy(e, o, t), n).on("end.style." + e, null);
}
function Xy(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function qy(e, t, n) {
  var o, i;
  function s() {
    var a = t.apply(this, arguments);
    return a !== i && (o = (i = a) && Xy(e, a, n)), o;
  }
  return s._value = t, s;
}
function Yy(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, qy(e, t, n ?? ""));
}
function Uy(e) {
  return function() {
    this.textContent = e;
  };
}
function Zy(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function Gy(e) {
  return this.tween("text", typeof e == "function" ? Zy(Gr(this, "text", e)) : Uy(e == null ? "" : e + ""));
}
function Jy(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Qy(e) {
  var t, n;
  function o() {
    var i = e.apply(this, arguments);
    return i !== n && (t = (n = i) && Jy(i)), t;
  }
  return o._value = e, o;
}
function em(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Qy(e));
}
function tm() {
  for (var e = this._name, t = this._id, n = Al(), o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Xe(u, t);
        Qo(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new it(o, this._parents, e, n);
}
function nm() {
  var e, t, n = this, o = n._id, i = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --i === 0 && s();
    } };
    n.each(function() {
      var l = Je(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), i === 0 && s();
  });
}
var om = 0;
function it(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Al() {
  return ++om;
}
var tt = Rn.prototype;
it.prototype = {
  constructor: it,
  select: Ly,
  selectAll: zy,
  selectChild: tt.selectChild,
  selectChildren: tt.selectChildren,
  filter: _y,
  merge: Dy,
  selection: Oy,
  transition: tm,
  call: tt.call,
  nodes: tt.nodes,
  node: tt.node,
  size: tt.size,
  empty: tt.empty,
  each: tt.each,
  on: My,
  attr: hy,
  attrTween: wy,
  style: Ky,
  styleTween: Yy,
  text: Gy,
  textTween: em,
  remove: Ry,
  tween: ay,
  delay: Ny,
  duration: Cy,
  ease: Iy,
  easeVarying: Ay,
  end: nm,
  [Symbol.iterator]: tt[Symbol.iterator]
};
function im(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var rm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: im
};
function sm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function am(e) {
  var t, n;
  e instanceof it ? (t = e._id, e = e._name) : (t = Al(), (n = rm).time = Ur(), e = e == null ? null : e + "");
  for (var o = this._groups, i = o.length, s = 0; s < i; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Qo(u, e, t, l, a, n || sm(u, t));
  return new it(o, this._parents, e, t);
}
Rn.prototype.interrupt = iy;
Rn.prototype.transition = am;
const so = (e) => () => e;
function cm(e, {
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
function nt(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
nt.prototype = {
  constructor: nt,
  scale: function(e) {
    return e === 1 ? this : new nt(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new nt(this.k, this.x + this.k * e, this.y + this.k * t);
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
var ei = new nt(1, 0, 0);
_l.prototype = nt.prototype;
function _l(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return ei;
  return e.__zoom;
}
function er(e) {
  e.stopImmediatePropagation();
}
function an(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function lm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function um() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function na() {
  return this.__zoom || ei;
}
function dm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function fm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function pm(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], i = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    i > o ? (o + i) / 2 : Math.min(0, o) || Math.max(0, i),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Dl() {
  var e = lm, t = um, n = pm, o = dm, i = fm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = xo, l = Zo("start", "zoom", "end"), d, f, p, h = 500, g = 150, m = 0, w = 10;
  function x(C) {
    C.property("__zoom", na).on("wheel.zoom", E, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", L).filter(i).on("touchstart.zoom", A).on("touchmove.zoom", _).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  x.transform = function(C, I, k, T) {
    var M = C.selection ? C.selection() : C;
    M.property("__zoom", na), C !== M ? j(C, I, k, T) : M.interrupt().each(function() {
      b(this, arguments).event(T).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, x.scaleBy = function(C, I, k, T) {
    x.scaleTo(C, function() {
      var M = this.__zoom.k, $ = typeof I == "function" ? I.apply(this, arguments) : I;
      return M * $;
    }, k, T);
  }, x.scaleTo = function(C, I, k, T) {
    x.transform(C, function() {
      var M = t.apply(this, arguments), $ = this.__zoom, B = k == null ? v(M) : typeof k == "function" ? k.apply(this, arguments) : k, W = $.invert(B), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(y(N($, O), B, W), M, a);
    }, k, T);
  }, x.translateBy = function(C, I, k, T) {
    x.transform(C, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof k == "function" ? k.apply(this, arguments) : k
      ), t.apply(this, arguments), a);
    }, null, T);
  }, x.translateTo = function(C, I, k, T, M) {
    x.transform(C, function() {
      var $ = t.apply(this, arguments), B = this.__zoom, W = T == null ? v($) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(ei.translate(W[0], W[1]).scale(B.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof k == "function" ? -k.apply(this, arguments) : -k
      ), $, a);
    }, T, M);
  };
  function N(C, I) {
    return I = Math.max(s[0], Math.min(s[1], I)), I === C.k ? C : new nt(I, C.x, C.y);
  }
  function y(C, I, k) {
    var T = I[0] - k[0] * C.k, M = I[1] - k[1] * C.k;
    return T === C.x && M === C.y ? C : new nt(C.k, T, M);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, I, k, T) {
    C.on("start.zoom", function() {
      b(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      b(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var M = this, $ = arguments, B = b(M, $).event(T), W = t.apply(M, $), O = k == null ? v(W) : typeof k == "function" ? k.apply(M, $) : k, U = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), Y = M.__zoom, te = typeof I == "function" ? I.apply(M, $) : I, le = u(Y.invert(O).concat(U / Y.k), te.invert(O).concat(U / te.k));
      return function(Z) {
        if (Z === 1) Z = te;
        else {
          var P = le(Z), q = U / P[2];
          Z = new nt(q, O[0] - P[0] * q, O[1] - P[1] * q);
        }
        B.zoom(null, Z);
      };
    });
  }
  function b(C, I, k) {
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
      var I = Pe(this.that).datum();
      l.call(
        C,
        this.that,
        new cm(C, {
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
    var k = b(this, I).event(C), T = this.__zoom, M = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, o.apply(this, arguments)))), $ = We(C);
    if (k.wheel)
      (k.mouse[0][0] !== $[0] || k.mouse[0][1] !== $[1]) && (k.mouse[1] = T.invert(k.mouse[0] = $)), clearTimeout(k.wheel);
    else {
      if (T.k === M) return;
      k.mouse = [$, T.invert($)], bo(this), k.start();
    }
    an(C), k.wheel = setTimeout(B, g), k.zoom("mouse", n(y(N(T, M), k.mouse[0], k.mouse[1]), k.extent, a));
    function B() {
      k.wheel = null, k.end();
    }
  }
  function D(C, ...I) {
    if (p || !e.apply(this, arguments)) return;
    var k = C.currentTarget, T = b(this, I, !0).event(C), M = Pe(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), $ = We(C, k), B = C.clientX, W = C.clientY;
    hl(C.view), er(C), T.mouse = [$, this.__zoom.invert($)], bo(this), T.start();
    function O(Y) {
      if (an(Y), !T.moved) {
        var te = Y.clientX - B, le = Y.clientY - W;
        T.moved = te * te + le * le > m;
      }
      T.event(Y).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = We(Y, k), T.mouse[1]), T.extent, a));
    }
    function U(Y) {
      M.on("mousemove.zoom mouseup.zoom", null), gl(Y.view, T.moved), an(Y), T.event(Y).end();
    }
  }
  function L(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = this.__zoom, T = We(C.changedTouches ? C.changedTouches[0] : C, this), M = k.invert(T), $ = k.k * (C.shiftKey ? 0.5 : 2), B = n(y(N(k, $), T, M), t.apply(this, I), a);
      an(C), c > 0 ? Pe(this).transition().duration(c).call(j, B, T, C) : Pe(this).call(x.transform, B, T, C);
    }
  }
  function A(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = C.touches, T = k.length, M = b(this, I, C.changedTouches.length === T).event(C), $, B, W, O;
      for (er(C), B = 0; B < T; ++B)
        W = k[B], O = We(W, this), O = [O, this.__zoom.invert(O), W.identifier], M.touch0 ? !M.touch1 && M.touch0[2] !== O[2] && (M.touch1 = O, M.taps = 0) : (M.touch0 = O, $ = !0, M.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (M.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), bo(this), M.start());
    }
  }
  function _(C, ...I) {
    if (this.__zooming) {
      var k = b(this, I).event(C), T = C.changedTouches, M = T.length, $, B, W, O;
      for (an(C), $ = 0; $ < M; ++$)
        B = T[$], W = We(B, this), k.touch0 && k.touch0[2] === B.identifier ? k.touch0[0] = W : k.touch1 && k.touch1[2] === B.identifier && (k.touch1[0] = W);
      if (B = k.that.__zoom, k.touch1) {
        var U = k.touch0[0], Y = k.touch0[1], te = k.touch1[0], le = k.touch1[1], Z = (Z = te[0] - U[0]) * Z + (Z = te[1] - U[1]) * Z, P = (P = le[0] - Y[0]) * P + (P = le[1] - Y[1]) * P;
        B = N(B, Math.sqrt(Z / P)), W = [(U[0] + te[0]) / 2, (U[1] + te[1]) / 2], O = [(Y[0] + le[0]) / 2, (Y[1] + le[1]) / 2];
      } else if (k.touch0) W = k.touch0[0], O = k.touch0[1];
      else return;
      k.zoom("touch", n(y(B, W, O), k.extent, a));
    }
  }
  function R(C, ...I) {
    if (this.__zooming) {
      var k = b(this, I).event(C), T = C.changedTouches, M = T.length, $, B;
      for (er(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), $ = 0; $ < M; ++$)
        B = T[$], k.touch0 && k.touch0[2] === B.identifier ? delete k.touch0 : k.touch1 && k.touch1[2] === B.identifier && delete k.touch1;
      if (k.touch1 && !k.touch0 && (k.touch0 = k.touch1, delete k.touch1), k.touch0) k.touch0[1] = this.__zoom.invert(k.touch0[0]);
      else if (k.end(), k.taps === 2 && (B = We(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < w)) {
        var W = Pe(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return x.wheelDelta = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : so(+C), x) : o;
  }, x.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : so(!!C), x) : e;
  }, x.touchable = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : so(!!C), x) : i;
  }, x.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : so([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), x) : t;
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
const Ve = {
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
}, kn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Tl = ["Enter", " ", "Escape"], $l = {
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
var Ft;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(Ft || (Ft = {}));
var xt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(xt || (xt = {}));
var An;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(An || (An = {}));
const Ml = {
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
var lt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(lt || (lt = {}));
var Oo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Oo || (Oo = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const oa = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Pl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Rl = (e) => "id" in e && "source" in e && "target" in e, hm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Jr = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), zn = (e, t = [0, 0]) => {
  const { width: n, height: o } = rt(e), i = e.origin ?? t, s = n * i[0], a = o * i[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, gm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, i) => {
    const s = typeof i == "string";
    let a = !t.nodeLookup && !s ? i : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(i) : Jr(i) ? i : t.nodeLookup.get(i.id));
    const c = a ? Ho(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ti(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return ni(n);
}, Vn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((i) => {
    (t.filter === void 0 || t.filter(i)) && (n = ti(n, Ho(i)), o = !0);
  }), o ? ni(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Qr = (e, t, [n, o, i] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...Qt(t, [n, o, i]),
    width: t.width / i,
    height: t.height / i
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, m = _n(c, Xt(l)), w = (h ?? 0) * (g ?? 0), x = s && m > 0;
    (!l.internals.handleBounds || x || m >= w || l.dragging) && u.push(l);
  }
  return u;
}, ym = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function mm(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((i) => i.id)) : null;
  return e.forEach((i) => {
    i.measured.width && i.measured.height && (t?.includeHiddenNodes || !i.hidden) && (!o || o.has(i.id)) && n.set(i.id, i);
  }), n;
}
async function xm({ nodes: e, width: t, height: n, panZoom: o, minZoom: i, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = mm(e, a), u = Vn(c), l = ts(u, t, n, a?.minZoom ?? i, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Ll({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: i, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || i;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ve.error005());
    else {
      const h = c.measured.width, g = c.measured.height;
      h && g && (f = [
        [u, l],
        [u + h, l + g]
      ]);
    }
  else c && Nt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Nt(f) ? bt(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ve.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function wm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: i }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), g = !h && p.parentId && a.find((m) => m.id === p.parentId);
    (h || g) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = o.filter((p) => p.deletable !== !1), d = ym(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((g) => g.id === p.id) && d.push(p);
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
const Kt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), bt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Kt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Kt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function zl(e, t, n) {
  const { width: o, height: i } = rt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return bt(e, [
    [s, a],
    [s + o, a + i]
  ], t);
}
const ia = (e, t, n) => e < t ? Kt(Math.abs(e - t), 1, t) / t : e > n ? -Kt(Math.abs(e - n), 1, t) / t : 0, es = (e, t, n = 15, o = 40) => {
  const i = ia(e.x, o, t.width - o) * n, s = ia(e.y, o, t.height - o) * n;
  return [i, s];
}, ti = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Cr = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), ni = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Xt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Jr(e) ? e.internals.positionAbsolute : zn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Ho = (e, t = [0, 0]) => {
  const { x: n, y: o } = Jr(e) ? e.internals.positionAbsolute : zn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Vl = (e, t) => ni(ti(Cr(e), Cr(t))), _n = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ra = (e) => Fe(e.width) && Fe(e.height) && Fe(e.x) && Fe(e.y), Fe = (e) => !isNaN(e) && isFinite(e), Ol = (e, t) => (n, o) => {
}, On = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Qt = ({ x: e, y: t }, [n, o, i], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / i,
    y: (t - o) / i
  };
  return s ? On(c, a) : c;
}, qt = ({ x: e, y: t }, [n, o, i]) => ({
  x: e * i + n,
  y: t * i + o
});
function $t(e, t) {
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
function vm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = $t(e, n), i = $t(e, t);
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
    const o = $t(e.top ?? e.y ?? 0, n), i = $t(e.bottom ?? e.y ?? 0, n), s = $t(e.left ?? e.x ?? 0, t), a = $t(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: i, left: s, x: s + a, y: o + i };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function bm(e, t, n, o, i, s) {
  const { x: a, y: c } = qt(e, [t, n, o]), { x: u, y: l } = qt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = i - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ts = (e, t, n, o, i, s) => {
  const a = vm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = Kt(l, o, i), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, g = n / 2 - p * d, m = bm(e, h, g, d, t, n), w = {
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
}, Dn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Nt(e) {
  return e != null && e !== "parent";
}
function rt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Hl(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Wl(e, t = { width: 0, height: 0 }, n, o, i) {
  const s = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || i;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function sa(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Nm() {
  let e, t;
  return { promise: new Promise((o, i) => {
    e = o, t = i;
  }), resolve: e, reject: t };
}
function jm(e) {
  return { ...$l, ...e || {} };
}
function vn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: i }) {
  const { x: s, y: a } = Ke(e), c = Qt({ x: s - (i?.left ?? 0), y: a - (i?.top ?? 0) }, o), { x: u, y: l } = n ? On(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ns = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Bl = (e) => e?.getRootNode?.() || window?.document, Sm = ["INPUT", "SELECT", "TEXTAREA"];
function Fl(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Sm.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Kl = (e) => "clientX" in e, Ke = (e, t) => {
  const n = Kl(e), o = n ? e.clientX : e.touches?.[0].clientX, i = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: i - (t?.top ?? 0)
  };
}, aa = (e, t, n, o, i) => {
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
      ...ns(a)
    };
  });
};
function Xl({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: i, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + i * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function ao(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function ca({ pos: e, x1: t, y1: n, x2: o, y2: i, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - ao(t - o, s), n];
    case ne.Right:
      return [t + ao(o - t, s), n];
    case ne.Top:
      return [t, n - ao(n - i, s)];
    case ne.Bottom:
      return [t, n + ao(i - n, s)];
  }
}
function ql({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = ca({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i,
    c: a
  }), [l, d] = ca({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, g] = Xl({
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
    g
  ];
}
function Yl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const i = Math.abs(n - e) / 2, s = n < e ? n + i : n - i, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, i, a];
}
function Cm({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: i = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = i && n ? o + 1e3 : o, c = Math.max(e.parentId || i && e.selected ? e.internals.z : 0, t.parentId || i && t.selected ? t.internals.z : 0);
  return a + c;
}
function Em({ sourceNode: e, targetNode: t, width: n, height: o, transform: i }) {
  const s = ti(Ho(e), Ho(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -i[0] / i[2],
    y: -i[1] / i[2],
    width: n / i[2],
    height: o / i[2]
  };
  return _n(a, ni(s)) > 0;
}
const Ul = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Im = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), km = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ve.error006()), t;
  const o = n.getEdgeId || Ul;
  let i;
  return Rl(e) ? i = { ...e } : i = {
    ...e,
    id: o(e)
  }, Im(i, t) ? t : (i.sourceHandle === null && delete i.sourceHandle, i.targetHandle === null && delete i.targetHandle, t.concat(i));
}, Am = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: i, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Ve.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Ve.error007(i)), n;
  const c = o.getEdgeId || Ul, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : i,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== i).concat(u);
};
function Zl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [i, s, a, c] = Yl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, i, s, a, c];
}
const la = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, _m = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, ua = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Dm({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: i, offset: s, stepPosition: a }) {
  const c = la[t], u = la[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = _m({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let g = [], m, w;
  const x = { x: 0, y: 0 }, N = { x: 0, y: 0 }, [, , y, v] = Yl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (m = i.x ?? l.x + (d.x - l.x) * a, w = i.y ?? (l.y + d.y) / 2) : (m = i.x ?? (l.x + d.x) / 2, w = i.y ?? l.y + (d.y - l.y) * a);
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
        c[p] === h ? x[p] = (l[p] > e[p] ? -1 : 1) * I : N[p] = (d[p] > n[p] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const C = p === "x" ? "y" : "x", I = c[p] === u[C], k = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!I && k || I && T) || c[p] !== 1 && (!I && T || I && k)) && (g = p === "x" ? E : D);
    }
    const L = { x: l.x + x.x, y: l.y + x.y }, A = { x: d.x + N.x, y: d.y + N.y }, _ = Math.max(Math.abs(L.x - g[0].x), Math.abs(A.x - g[0].x)), R = Math.max(Math.abs(L.y - g[0].y), Math.abs(A.y - g[0].y));
    _ >= R ? (m = (L.x + A.x) / 2, w = g[0].y) : (m = g[0].x, w = (L.y + A.y) / 2);
  }
  const j = { x: l.x + x.x, y: l.y + x.y }, b = { x: d.x + N.x, y: d.y + N.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== g[0].x || j.y !== g[0].y ? [j] : [],
    ...g,
    ...b.x !== g[g.length - 1].x || b.y !== g[g.length - 1].y ? [b] : [],
    n
  ], m, w, y, v];
}
function Tm(e, t, n, o) {
  const i = Math.min(ua(e, t) / 2, ua(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + i * l},${a}Q ${s},${a} ${s},${a + i * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + i * u}Q ${s},${a} ${s + i * c},${a}`;
}
function Wo({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, g, m] = Dm({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: i },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${f[0].x} ${f[0].y}`;
  for (let x = 1; x < f.length - 1; x++)
    w += Tm(f[x - 1], f[x], f[x + 1], a);
  return w += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [w, p, h, g, m];
}
function da(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $m(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!da(t) || !da(n))
    return null;
  const o = t.internals.handleBounds || fa(t.handles), i = n.internals.handleBounds || fa(n.handles), s = pa(o?.source ?? [], e.sourceHandle), a = pa(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ft.Strict ? i?.target ?? [] : (i?.target ?? []).concat(i?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ve.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = jt(t, s, c), d = jt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function fa(e) {
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
function jt(e, t, n = ne.Left, o = !1) {
  const i = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? rt(e);
  if (o)
    return { x: i + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: i + a / 2, y: s };
    case ne.Right:
      return { x: i + a, y: s + c / 2 };
    case ne.Bottom:
      return { x: i + a / 2, y: s + c };
    case ne.Left:
      return { x: i, y: s + c / 2 };
  }
}
function pa(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Er(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Mm(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: i }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || i].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Er(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const Gl = 1e3, Pm = 10, os = {
  nodeOrigin: [0, 0],
  nodeExtent: kn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Rm = {
  ...os,
  checkEquality: !0
};
function is(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Lm(e, t, n) {
  const o = is(os, n);
  for (const i of e.values())
    if (i.parentId)
      ss(i, e, t, o);
    else {
      const s = zn(i, o.nodeOrigin), a = Nt(i.extent) ? i.extent : o.nodeExtent, c = bt(s, a, rt(i));
      i.internals.positionAbsolute = c;
    }
}
function zm(e, t) {
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
function rs(e) {
  return e === "manual";
}
function Ir(e, t, n, o = {}) {
  const i = is(Rm, o), s = { i: 0 }, a = new Map(t), c = i?.elevateNodesOnSelect && !rs(i.zIndexMode) ? Gl : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (i.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = zn(d, i.nodeOrigin), h = Nt(d.extent) ? d.extent : i.nodeExtent, g = bt(p, h, rt(d));
      f = {
        ...i.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: zm(d, f),
          z: Jl(d, c, i.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ss(f, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Vm(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ss(e, t, n, o, i) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = is(os, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Vm(e, n), i && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++i.i, d.internals.z = d.internals.z + i.i * Pm), i && d.internals.rootParentIndex !== void 0 && (i.i = d.internals.rootParentIndex);
  const f = s && !rs(u) ? Gl : 0, { x: p, y: h, z: g } = Om(e, d, a, c, f, u), { positionAbsolute: m } = e.internals, w = p !== m.x || h !== m.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: p, y: h } : m,
      z: g
    }
  });
}
function Jl(e, t, n) {
  const o = Fe(e.zIndex) ? e.zIndex : 0;
  return rs(n) ? o : o + (e.selected ? t : 0);
}
function Om(e, t, n, o, i, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = rt(e), l = zn(e, n), d = Nt(e.extent) ? bt(l, e.extent, u) : l;
  let f = bt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = zl(f, u, t));
  const p = Jl(e, i, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function as(e, t, n, o = [0, 0]) {
  const i = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? Xt(c), l = Vl(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = rt(c), f = c.origin ?? o, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), m = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * f[0], x = (m - d.height) * f[1];
    (p > 0 || h > 0 || w || x) && (i.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + w,
        y: c.position.y - h + x
      }
    }), n.get(u)?.forEach((N) => {
      e.some((y) => y.id === N.id) || i.push({
        id: N.id,
        type: "position",
        position: {
          x: N.position.x + p,
          y: N.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && i.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (p ? f[0] * p - w : 0),
        height: m + (h ? f[1] * h - x : 0)
      }
    });
  }), i;
}
function Hm(e, t, n, o, i, s, a) {
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
    const m = ns(h.nodeElement), w = g.measured.width !== m.width || g.measured.height !== m.height;
    if (!!(m.width && m.height && (w || !g.internals.handleBounds || h.force))) {
      const N = h.nodeElement.getBoundingClientRect(), y = Nt(g.extent) ? g.extent : s;
      let { positionAbsolute: v } = g.internals;
      g.parentId && g.extent === "parent" ? v = zl(v, m, t.get(g.parentId)) : y && (v = bt(v, y, m));
      const j = {
        ...g,
        measured: m,
        internals: {
          ...g.internals,
          positionAbsolute: v,
          handleBounds: {
            source: aa("source", h.nodeElement, N, f, g.id),
            target: aa("target", h.nodeElement, N, f, g.id)
          }
        }
      };
      t.set(g.id, j), g.parentId && ss(j, t, n, { nodeOrigin: i, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: m
      }), g.expandParent && g.parentId && p.push({
        id: g.id,
        parentId: g.parentId,
        rect: Xt(j, i)
      }));
    }
  }
  if (p.length > 0) {
    const h = as(p, t, n, i);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Wm({ delta: e, panZoom: t, transform: n, translateExtent: o, width: i, height: s }) {
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
function ha(e, t, n, o, i, s) {
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
function Ql(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: i, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: i, target: s, sourceHandle: a, targetHandle: c }, l = `${i}-${a}--${s}-${c}`, d = `${s}-${c}--${i}-${a}`;
    ha("source", u, d, e, i, a), ha("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function eu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : eu(n, t) : !1;
}
function ga(e, t, n) {
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
function Bm(e, t, n, o) {
  const i = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !eu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function tr({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function Fm({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const i = e.values().next().value;
  if (!i)
    return null;
  const s = {
    x: n - i.distance.x,
    y: o - i.distance.y
  }, a = On(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Km({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: i }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, g = !1, m = null;
  function w({ noDragClassName: N, handleSelector: y, domNode: v, isSelectable: j, nodeId: b, nodeClickDistance: S = 0 }) {
    p = Pe(v);
    function E({ x: _, y: R }) {
      const { nodeLookup: C, nodeExtent: I, snapGrid: k, snapToGrid: T, nodeOrigin: M, onNodeDrag: $, onSelectionDrag: B, onError: W, updateNodePositions: O } = t();
      s = { x: _, y: R };
      let U = !1;
      const Y = c.size > 1, te = Y && I ? Cr(Vn(c)) : null, le = Y && T ? Fm({
        dragItems: c,
        snapGrid: k,
        x: _,
        y: R
      }) : null;
      for (const [Z, P] of c) {
        if (!C.has(Z))
          continue;
        let q = { x: _ - P.distance.x, y: R - P.distance.y };
        T && (q = le ? {
          x: Math.round(q.x + le.x),
          y: Math.round(q.y + le.y)
        } : On(q, k));
        let ae = null;
        if (Y && I && !P.extent && te) {
          const { positionAbsolute: oe } = P.internals, ue = oe.x - te.x + I[0][0], V = oe.x + P.measured.width - te.x2 + I[1][0], ee = oe.y - te.y + I[0][1], ge = oe.y + P.measured.height - te.y2 + I[1][1];
          ae = [
            [ue, ee],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = Ll({
          nodeId: Z,
          nextPosition: q,
          nodeLookup: C,
          nodeExtent: ae || I,
          nodeOrigin: M,
          onError: W
        });
        U = U || P.position.x !== ce.x || P.position.y !== ce.y, P.position = ce, P.internals.positionAbsolute = J;
      }
      if (g = g || U, !!U && (O(c, !0), m && (o || $ || !b && B))) {
        const [Z, P] = tr({
          nodeId: b,
          dragItems: c,
          nodeLookup: C
        });
        o?.(m, c, Z, P), $?.(m, Z, P), b || B?.(m, P);
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
      const [k, T] = es(l, d, C);
      (k !== 0 || T !== 0) && (s.x = (s.x ?? 0) - k / _[2], s.y = (s.y ?? 0) - T / _[2], await R({ x: k, y: T }) && E(s)), a = requestAnimationFrame(D);
    }
    function L(_) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: I, transform: k, snapGrid: T, snapToGrid: M, selectNodesOnDrag: $, onNodeDragStart: B, onSelectionDragStart: W, unselectNodesAndEdges: O } = t();
      f = !0, (!$ || !j) && !C && b && (R.get(b)?.selected || O()), j && $ && b && e?.(b);
      const U = vn(_.sourceEvent, { transform: k, snapGrid: T, snapToGrid: M, containerBounds: d });
      if (s = U, c = Bm(R, I, U, b), c.size > 0 && (n || B || !b && W)) {
        const [Y, te] = tr({
          nodeId: b,
          dragItems: c,
          nodeLookup: R
        });
        n?.(_.sourceEvent, c, Y, te), B?.(_.sourceEvent, Y, te), b || W?.(_.sourceEvent, te);
      }
    }
    const A = yl().clickDistance(S).on("start", (_) => {
      const { domNode: R, nodeDragThreshold: C, transform: I, snapGrid: k, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, g = !1, m = _.sourceEvent, C === 0 && L(_), s = vn(_.sourceEvent, { transform: I, snapGrid: k, snapToGrid: T, containerBounds: d }), l = Ke(_.sourceEvent, d);
    }).on("drag", (_) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: I, snapToGrid: k, nodeDragThreshold: T, nodeLookup: M } = t(), $ = vn(_.sourceEvent, { transform: C, snapGrid: I, snapToGrid: k, containerBounds: d });
      if (m = _.sourceEvent, (_.sourceEvent.type === "touchmove" && _.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      b && !M.has(b)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const B = Ke(_.sourceEvent, d), W = B.x - l.x, O = B.y - l.y;
          Math.sqrt(W * W + O * O) > T && L(_);
        }
        (s.x !== $.xSnapped || s.y !== $.ySnapped) && c && f && (l = Ke(_.sourceEvent, d), E($));
      }
    }).on("end", (_) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: I, onSelectionDragStop: k } = t();
        if (g && (C(c, !1), g = !1), i || I || !b && k) {
          const [T, M] = tr({
            nodeId: b,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          i?.(_.sourceEvent, c, T, M), I?.(_.sourceEvent, T, M), b || k?.(_.sourceEvent, M);
        }
      }
    }).filter((_) => {
      const R = _.target;
      return !_.button && (!N || !ga(R, `.${N}`, v)) && (!y || ga(R, y, v));
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
function Xm(e, t, n) {
  const o = [], i = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    _n(i, Xt(s)) > 0 && o.push(s);
  return o;
}
const qm = 250;
function Ym(e, t, n, o) {
  let i = [], s = 1 / 0;
  const a = Xm(e, n, t + qm);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = jt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function tu(e, t, n, o, i, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = i === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...jt(a, u, u.position, !0) } : u;
}
function nu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Um(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const ou = () => !0;
function Zm(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: i, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: g, onConnect: m, onConnectEnd: w, isValidConnection: x = ou, onReconnectEnd: N, updateConnection: y, getTransform: v, getFromHandle: j, autoPanSpeed: b, dragThreshold: S = 1, handleDomNode: E }) {
  const D = Bl(e.target);
  let L = 0, A;
  const { x: _, y: R } = Ke(e), C = nu(s, E), I = c?.getBoundingClientRect();
  let k = !1;
  if (!I || !C)
    return;
  const T = tu(i, C, o, u, t);
  if (!T)
    return;
  let M = Ke(e, I), $ = !1, B = null, W = !1, O = null;
  function U() {
    if (!d || !I)
      return;
    const [ce, J] = es(M, I, b);
    p({ x: ce, y: J }), L = requestAnimationFrame(U);
  }
  const Y = {
    ...T,
    nodeId: i,
    type: C,
    position: T.position
  }, te = u.get(i);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: jt(te, Y, ne.Left, !0),
    fromHandle: Y,
    fromPosition: Y.position,
    fromNode: te,
    to: M,
    toHandle: null,
    toPosition: oa[Y.position],
    toNode: null,
    pointer: M
  };
  function P() {
    k = !0, y(Z), g?.(e, { nodeId: i, handleId: o, handleType: C });
  }
  S === 0 && P();
  function q(ce) {
    if (!k) {
      const { x: ge, y: we } = Ke(ce), Ae = ge - _, Ie = we - R;
      if (!(Ae * Ae + Ie * Ie > S * S))
        return;
      P();
    }
    if (!j() || !Y) {
      ae(ce);
      return;
    }
    const J = v();
    M = Ke(ce, I), A = Ym(Qt(M, J, !1, [1, 1]), n, u, Y), $ || (U(), $ = !0);
    const oe = iu(ce, {
      handle: A,
      connectionMode: t,
      fromNodeId: i,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: x,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = oe.handleDomNode, B = oe.connection, W = Um(!!A, oe.isValid);
    const ue = u.get(i), V = ue ? jt(ue, Y, ne.Left, !0) : Z.from, ee = {
      ...Z,
      from: V,
      isValid: W,
      to: oe.toHandle && W ? qt({ x: oe.toHandle.x, y: oe.toHandle.y }, J) : M,
      toHandle: oe.toHandle,
      toPosition: W && oe.toHandle ? oe.toHandle.position : oa[Y.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: M
    };
    y(ee), Z = ee;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (k) {
        (A || O) && B && W && m?.(B);
        const { inProgress: J, ...oe } = Z, ue = {
          ...oe,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        w?.(ce, ue), s && N?.(ce, ue);
      }
      h(), cancelAnimationFrame(L), $ = !1, W = !1, B = null, O = null, D.removeEventListener("mousemove", q), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", q), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", q), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", q), D.addEventListener("touchend", ae);
}
function iu(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: i, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = ou, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = Ke(e), m = a.elementFromPoint(h, g), w = m?.classList.contains(`${c}-flow__handle`) ? m : p, x = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const N = nu(void 0, w), y = w.getAttribute("data-nodeid"), v = w.getAttribute("data-handleid"), j = w.classList.contains("connectable"), b = w.classList.contains("connectableend");
    if (!y || !N)
      return x;
    const S = {
      source: f ? y : o,
      sourceHandle: f ? v : i,
      target: f ? o : y,
      targetHandle: f ? i : v
    };
    x.connection = S;
    const D = j && b && (n === Ft.Strict ? f && N === "source" || !f && N === "target" : y !== o || v !== i);
    x.isValid = D && l(S), x.toHandle = tu(y, N, v, d, n, !0);
  }
  return x;
}
const kr = {
  onPointerDown: Zm,
  isValid: iu
};
function Gm({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const i = Pe(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = y.sourceEvent.ctrlKey && Dn() ? 10 : 1, b = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, b * j);
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
      ], b = [j[0] - m[0], j[1] - m[1]];
      m = j;
      const S = o() * Math.max(v[2], Math.log(v[2])) * (h ? -1 : 1), E = {
        x: v[0] - b[0] * S,
        y: v[1] - b[1] * S
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: E.x,
        y: E.y,
        zoom: v[2]
      }, D, c);
    }, N = Dl().on("start", w).on("zoom", f ? x : null).on("zoom.wheel", p ? g : null);
    i.call(N, {});
  }
  function a() {
    i.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: We
  };
}
const oi = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), nr = ({ x: e, y: t, zoom: n }) => ei.translate(e, t).scale(n), Pt = (e, t) => e.target.closest(`.${t}`), ru = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Jm = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, or = (e, t = 0, n = Jm, o = () => {
}) => {
  const i = typeof t == "number" && t > 0;
  return i || o(), i ? e.transition().duration(t).ease(n).on("end", o) : e;
}, su = (e) => {
  const t = e.ctrlKey && Dn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Qm({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: i, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Pt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = We(d), x = su(d), N = f * Math.pow(2, x);
      o.scaleTo(n, N, w, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = i === xt.Vertical ? 0 : d.deltaX * p, g = i === xt.Horizontal ? 0 : d.deltaY * p;
    !Dn() && d.shiftKey && i !== xt.Vertical && (h = d.deltaY * p, g = 0), o.translateBy(
      n,
      -(h / f) * s,
      -(g / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const m = oi(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, m), e.panScrollTimeout = setTimeout(() => {
      l?.(d, m), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, m));
  };
}
function ex({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, i) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = Pt(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, i);
  };
}
function tx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const i = oi(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = i, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, i);
  };
}
function nx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: i }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && ru(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), i && !s.sourceEvent?.internal && i?.(s.sourceEvent, oi(s.transform));
  };
}
function ox({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: i, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && ru(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), i)) {
      const c = oi(a.transform);
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
function ix({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: i, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Pt(f, `${l}-flow__node`) || Pt(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !p && !i && !s && !n || a || d && !g || Pt(f, c) && g || Pt(f, u) && (!g || i && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !i && !h && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const m = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && m;
  };
}
function rx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: i, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Dl().scaleExtent([t, n]).translateExtent(o), p = Pe(e).call(f);
  N({
    x: i.x,
    y: i.y,
    zoom: Kt(i.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const h = p.on("wheel.zoom"), g = p.on("dblclick.zoom");
  f.wheelDelta(su);
  async function m(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? wn : xo).transform(or(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function w({ noWheelClassName: A, noPanClassName: _, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: I, panOnDrag: k, panOnScrollMode: T, panOnScrollSpeed: M, preventScrolling: $, zoomOnPinch: B, zoomOnScroll: W, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: Y, onTransformChange: te, connectionInProgress: le, paneClickDistance: Z, selectionOnDrag: P }) {
    C && !l.isZoomingOrPanning && x();
    const q = I && !U && !C;
    f.clickDistance(P ? 1 / 0 : !Fe(Z) || Z < 0 ? 0 : Z);
    const ae = q ? Qm({
      zoomPanValues: l,
      noWheelClassName: A,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: T,
      panOnScrollSpeed: M,
      zoomOnPinch: B,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : ex({
      noWheelClassName: A,
      preventScrolling: $,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = tx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = nx({
      zoomPanValues: l,
      panOnDrag: k,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: te
    });
    f.on("zoom", J);
    const oe = ox({
      zoomPanValues: l,
      panOnDrag: k,
      panOnScroll: I,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", oe);
    const ue = ix({
      zoomActivationKeyPressed: U,
      panOnDrag: k,
      zoomOnScroll: W,
      panOnScroll: I,
      zoomOnDoubleClick: O,
      zoomOnPinch: B,
      userSelectionActive: C,
      noPanClassName: _,
      noWheelClassName: A,
      lib: Y,
      connectionInProgress: le
    });
    f.filter(ue), O ? p.on("dblclick.zoom", g) : p.on("dblclick.zoom", null);
  }
  function x() {
    f.on("zoom", null);
  }
  async function N(A, _, R) {
    const C = nr(A), I = f?.constrain()(C, _, R);
    return I && await m(I), I;
  }
  async function y(A, _) {
    const R = nr(A);
    return await m(R, _), R;
  }
  function v(A) {
    if (p) {
      const _ = nr(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, _, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? _l(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function b(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? wn : xo).scaleTo(or(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? wn : xo).scaleBy(or(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function E(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function L(A) {
    const _ = !Fe(A) || A < 0 ? 0 : A;
    f?.clickDistance(_);
  }
  return {
    update: w,
    destroy: x,
    setViewport: y,
    setViewportConstrained: N,
    getViewport: j,
    scaleTo: b,
    scaleBy: S,
    setScaleExtent: E,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: L
  };
}
var Yt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Yt || (Yt = {}));
function sx({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: i, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && i && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ya(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), i = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: i
  };
}
function at(e, t) {
  return Math.max(0, t - e);
}
function ct(e, t) {
  return Math.max(0, e - t);
}
function co(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ma(e, t) {
  return e ? !t : t;
}
function ax(e, t, n, o, i, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: g } = n, { minWidth: m, maxWidth: w, minHeight: x, maxHeight: N } = o, { x: y, y: v, width: j, height: b, aspectRatio: S } = e;
  let E = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? g - e.pointerY : 0);
  const L = j + (u ? -E : E), A = b + (l ? -D : D), _ = -s[0] * j, R = -s[1] * b;
  let C = co(L, m, w), I = co(A, x, N);
  if (a) {
    let M = 0, $ = 0;
    u && E < 0 ? M = at(y + E + _, a[0][0]) : !u && E > 0 && (M = ct(y + L + _, a[1][0])), l && D < 0 ? $ = at(v + D + R, a[0][1]) : !l && D > 0 && ($ = ct(v + A + R, a[1][1])), C = Math.max(C, M), I = Math.max(I, $);
  }
  if (c) {
    let M = 0, $ = 0;
    u && E > 0 ? M = ct(y + E, c[0][0]) : !u && E < 0 && (M = at(y + L, c[1][0])), l && D > 0 ? $ = ct(v + D, c[0][1]) : !l && D < 0 && ($ = at(v + A, c[1][1])), C = Math.max(C, M), I = Math.max(I, $);
  }
  if (i) {
    if (d) {
      const M = co(L / S, x, N) * S;
      if (C = Math.max(C, M), a) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = ct(v + R + L / S, a[1][1]) * S : $ = at(v + R + (u ? E : -E) / S, a[0][1]) * S, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = at(v + L / S, c[1][1]) * S : $ = ct(v + (u ? E : -E) / S, c[0][1]) * S, C = Math.max(C, $);
      }
    }
    if (f) {
      const M = co(A * S, m, w) / S;
      if (I = Math.max(I, M), a) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = ct(y + A * S + _, a[1][0]) / S : $ = at(y + (l ? D : -D) * S + _, a[0][0]) / S, I = Math.max(I, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = at(y + A * S, c[1][0]) / S : $ = ct(y + (l ? D : -D) * S, c[0][0]) / S, I = Math.max(I, $);
      }
    }
  }
  D = D + (D < 0 ? I : -I), E = E + (E < 0 ? C : -C), i && (p ? L > A * S ? D = (ma(u, l) ? -E : E) / S : E = (ma(u, l) ? -D : D) * S : d ? (D = E / S, l = u) : (E = D * S, u = l));
  const k = u ? y + E : y, T = l ? v + D : v;
  return {
    width: j + (u ? -E : E),
    height: b + (l ? -D : D),
    x: s[0] * E * (u ? -1 : 1) + k,
    y: s[1] * D * (l ? -1 : 1) + T
  };
}
const au = { width: 0, height: 0, x: 0, y: 0 }, cx = {
  ...au,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function lx(e, t, n) {
  const o = t.position.x + e.position.x, i = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, i - u],
    [o + s - c, i + a - u]
  ];
}
function ux({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: i }) {
  const s = Pe(e);
  let a = {
    controlDirection: ya("bottom-right"),
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
    let x = { ...au }, N = { ...cx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: ya(l)
    };
    let y, v = null, j = [], b, S, E, D = !1;
    const L = yl().on("start", (A) => {
      const { nodeLookup: _, transform: R, snapGrid: C, snapToGrid: I, nodeOrigin: k, paneDomNode: T } = n();
      if (y = _.get(t), !y)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: M, ySnapped: $ } = vn(A.sourceEvent, {
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
      }, N = {
        ...x,
        pointerX: M,
        pointerY: $,
        aspectRatio: x.width / x.height
      }, b = void 0, S = Nt(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (b = _.get(y.parentId)), b && y.extent === "parent" && (S = [
        [0, 0],
        [b.measured.width, b.measured.height]
      ]), j = [], E = void 0;
      for (const [B, W] of _)
        if (W.parentId === t && (j.push({
          id: B,
          position: { ...W.position },
          extent: W.extent
        }), W.extent === "parent" || W.expandParent)) {
          const O = lx(W, y, W.origin ?? k);
          E ? E = [
            [Math.min(O[0][0], E[0][0]), Math.min(O[0][1], E[0][1])],
            [Math.max(O[1][0], E[1][0]), Math.max(O[1][1], E[1][1])]
          ] : E = O;
        }
      h?.(A, { ...x });
    }).on("drag", (A) => {
      const { transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I } = n(), k = vn(A.sourceEvent, {
        transform: _,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!y)
        return;
      const { x: M, y: $, width: B, height: W } = x, O = {}, U = y.origin ?? I, { width: Y, height: te, x: le, y: Z } = ax(N, a.controlDirection, k, a.boundaries, a.keepAspectRatio, U, S, E), P = Y !== B, q = te !== W, ae = le !== M && P, ce = Z !== $ && q;
      if (!ae && !ce && !P && !q)
        return;
      if ((ae || ce || U[0] === 1 || U[1] === 1) && (O.x = ae ? le : x.x, O.y = ce ? Z : x.y, x.x = O.x, x.y = O.y, j.length > 0)) {
        const V = le - M, ee = Z - $;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - V + U[0] * (Y - B),
            y: ge.position.y - ee + U[1] * (te - W)
          }, T.push(ge);
      }
      if ((P || q) && (O.width = P && (!a.resizeDirection || a.resizeDirection === "horizontal") ? Y : x.width, O.height = q && (!a.resizeDirection || a.resizeDirection === "vertical") ? te : x.height, x.width = O.width, x.height = O.height), b && y.expandParent) {
        const V = U[0] * (O.width ?? 0);
        O.x && O.x < V && (x.x = V, N.x = N.x - (O.x - V));
        const ee = U[1] * (O.height ?? 0);
        O.y && O.y < ee && (x.y = ee, N.y = N.y - (O.y - ee));
      }
      const J = sx({
        width: x.width,
        prevWidth: B,
        height: x.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...x, direction: J };
      w?.(A, oe) !== !1 && (D = !0, g?.(A, oe), o(O, T));
    }).on("end", (A) => {
      D && (m?.(A, { ...x }), i?.({ ...x }), D = !1);
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
var ir = { exports: {} }, rr = {}, sr = { exports: {} }, ar = {};
var xa;
function dx() {
  if (xa) return ar;
  xa = 1;
  var e = Ze;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, i = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), g = o({ inst: { value: h, getSnapshot: p } }), m = g[0].inst, w = g[1];
    return s(
      function() {
        m.value = h, m.getSnapshot = p, u(m) && w({ inst: m });
      },
      [f, h, p]
    ), i(
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
  return ar.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, ar;
}
var wa;
function fx() {
  return wa || (wa = 1, sr.exports = dx()), sr.exports;
}
var va;
function px() {
  if (va) return rr;
  va = 1;
  var e = Ze, t = fx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, i = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return rr.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var g = s(null);
    if (g.current === null) {
      var m = { hasValue: !1, value: null };
      g.current = m;
    } else m = g.current;
    g = c(
      function() {
        function x(b) {
          if (!N) {
            if (N = !0, y = b, b = p(b), h !== void 0 && m.hasValue) {
              var S = m.value;
              if (h(S, b))
                return v = S;
            }
            return v = b;
          }
          if (S = v, o(y, b)) return S;
          var E = p(b);
          return h !== void 0 && h(S, E) ? (y = b, S) : (y = b, v = E);
        }
        var N = !1, y, v, j = f === void 0 ? null : f;
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
    var w = i(l, g[0], g[1]);
    return a(
      function() {
        m.hasValue = !0, m.value = w;
      },
      [w]
    ), u(w), w;
  }, rr;
}
var ba;
function hx() {
  return ba || (ba = 1, ir.exports = px()), ir.exports;
}
var gx = hx();
const yx = /* @__PURE__ */ Wd(gx), mx = {}, Na = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((g) => g(t, h));
    }
  }, i = () => t, u = { setState: o, getState: i, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (mx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, i, u);
  return u;
}, xx = (e) => e ? Na(e) : Na, { useDebugValue: wx } = Ze, { useSyncExternalStoreWithSelector: vx } = yx, bx = (e) => e;
function cu(e, t = bx, n) {
  const o = vx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return wx(o), o;
}
const ja = (e, t) => {
  const n = xx(e), o = (i, s = t) => cu(n, i, s);
  return Object.assign(o, n), o;
}, Nx = (e, t) => e ? ja(e, t) : ja;
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
var cr = { exports: {} }, _e = {};
var Sa;
function jx() {
  if (Sa) return _e;
  Sa = 1;
  var e = Ze;
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
var Ca;
function Sx() {
  if (Ca) return cr.exports;
  Ca = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), cr.exports = jx(), cr.exports;
}
var Cx = Sx();
const ii = Lr(null), Ex = ii.Provider, lu = Ve.error001("react");
function pe(e, t) {
  const n = $n(ii);
  if (n === null)
    throw new Error(lu);
  return cu(n, e, t);
}
function xe() {
  const e = $n(ii);
  if (e === null)
    throw new Error(lu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ea = { display: "none" }, Ix = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, uu = "react-flow__node-desc", du = "react-flow__edge-desc", kx = "react-flow__aria-live", Ax = (e) => e.ariaLiveMessage, _x = (e) => e.ariaLabelConfig;
function Dx({ rfId: e }) {
  const t = pe(Ax);
  return r.jsx("div", { id: `${kx}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ix, children: t });
}
function Tx({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(_x);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${uu}-${e}`, style: Ea, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${du}-${e}`, style: Ea, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Dx, { rfId: e })] });
}
const ri = Sc(({ position: e = "top-left", children: t, className: n, style: o, ...i }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Se(["react-flow__panel", n, ...a]), style: o, ref: s, ...i, children: t });
});
ri.displayName = "Panel";
function $x({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(ri, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Mx = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, lo = (e) => e.id;
function Px(e, t) {
  return me(e.selectedNodes.map(lo), t.selectedNodes.map(lo)) && me(e.selectedEdges.map(lo), t.selectedEdges.map(lo));
}
function Rx({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = pe(Mx, Px);
  return G(() => {
    const i = { nodes: n, edges: o };
    e?.(i), t.getState().onSelectionChangeHandlers.forEach((s) => s(i));
  }, [n, o, e]), null;
}
const Lx = (e) => !!e.onSelectionChangeHandlers;
function zx({ onSelectionChange: e }) {
  const t = pe(Lx);
  return e || t ? r.jsx(Rx, { onSelectionChange: e }) : null;
}
const fu = [0, 0], Vx = { x: 0, y: 0, zoom: 1 }, Ox = [
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
], Ia = [...Ox, "rfId"], Hx = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), ka = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: kn,
  nodeOrigin: fu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Wx(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: i, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Hx, me), l = xe();
  G(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = ka, c();
  }), []);
  const d = ie(ka);
  return G(
    () => {
      for (const f of Ia) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? o(p) : f === "maxZoom" ? i(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: jm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ia.map((f) => e[f])
  ), null;
}
function Aa() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Bx(e) {
  const [t, n] = F(e === "system" ? null : e);
  return G(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Aa(), i = () => n(o?.matches ? "dark" : "light");
    return i(), o?.addEventListener("change", i), () => {
      o?.removeEventListener("change", i);
    };
  }, [e]), t !== null ? t : Aa()?.matches ? "dark" : "light";
}
const _a = typeof document < "u" ? document : null;
function Tn(e = null, t = { target: _a, actInsideInputWithModifier: !0 }) {
  const [n, o] = F(!1), i = ie(!1), s = ie(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
    const u = t?.target ?? _a, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (i.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!i.current || i.current && !l) && Fl(h))
          return !1;
        const m = Ta(h.code, c);
        if (s.current.add(h[m]), Da(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, x = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (i.current || !x) && h.preventDefault(), o(!0);
        }
      }, f = (h) => {
        const g = Ta(h.code, c);
        Da(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), i.current = !1;
      }, p = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, o]), n;
}
function Da(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((i) => t.has(i)));
}
function Ta(e, t) {
  return t.includes(e) ? "code" : "key";
}
const Fx = () => {
  const e = xe();
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
      const { width: o, height: i, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = ts(t, o, i, s, a, n?.padding ?? 0.1);
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
      return Qt(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: i, y: s } = o.getBoundingClientRect(), a = qt(t, n);
      return {
        x: a.x + i,
        y: a.y + s
      };
    }
  }), []);
};
function pu(e, t) {
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
      Kx(u, c);
    n.push(c);
  }
  return i.length && i.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Kx(e, t) {
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
function hu(e, t) {
  return pu(e, t);
}
function gu(e, t) {
  return pu(e, t);
}
function ht(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Rt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [i, s] of e) {
    const a = t.has(i);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(ht(s.id, a)));
  }
  return o;
}
function $a({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((i) => [i.id, i]));
  for (const [i, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: i });
  }
  for (const [i] of t)
    o.get(i) === void 0 && n.push({ id: i, type: "remove" });
  return n;
}
function Ma(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const yu = Ol();
function mu(e, t, n = {}) {
  return km(e, t, {
    ...n,
    onError: n.onError ?? yu
  });
}
function Xx(e, t, n, o = { shouldReplaceId: !0 }) {
  return Am(e, t, n, {
    ...o,
    onError: o.onError ?? yu
  });
}
const Pa = (e) => hm(e), qx = (e) => Rl(e);
function xu(e) {
  return Sc(e);
}
const Yx = typeof window < "u" ? Sd : G;
function Ra(e) {
  const [t, n] = F(BigInt(0)), [o] = F(() => Ux(() => n((i) => i + BigInt(1))));
  return Yx(() => {
    const i = o.get();
    i.length && (e(i), o.reset());
  }, [t]), o;
}
function Ux(e) {
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
const wu = Lr(null);
function Zx({ children: e }) {
  const t = xe(), n = re((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let m = u;
    for (const x of c)
      m = typeof x == "function" ? x(m) : x;
    let w = $a({
      items: m,
      lookup: p
    });
    for (const x of g.values())
      w = x(w);
    d && l(m), w.length > 0 ? f?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: x, nodes: N, setNodes: y } = t.getState();
      x && y(N);
    });
  }, []), o = Ra(n), i = re((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    d ? l(h) : f && f($a({
      items: h,
      lookup: p
    }));
  }, []), s = Ra(i), a = de(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return r.jsx(wu.Provider, { value: a, children: e });
}
function Gx() {
  const e = $n(wu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Jx = (e) => !!e.panZoom;
function cs() {
  const e = Fx(), t = xe(), n = Gx(), o = pe(Jx), i = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), g = Pa(f) ? f : p.get(f.id), m = g.parentId ? Wl(g.position, g.measured, g.parentId, p, h) : g.position, w = {
        ...g,
        position: m,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Xt(w);
    }, l = (f, p, h = { replace: !1 }) => {
      a((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && Pa(w) ? w : { ...m, ...w };
        }
        return m;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((g) => g.map((m) => {
        if (m.id === f) {
          const w = typeof p == "function" ? p(m) : p;
          return h.replace && qx(w) ? w : { ...m, ...w };
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
        const { nodes: h, edges: g, onNodesDelete: m, onEdgesDelete: w, triggerNodeChanges: x, triggerEdgeChanges: N, onDelete: y, onBeforeDelete: v } = t.getState(), { nodes: j, edges: b } = await wm({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: g,
          onBeforeDelete: v
        }), S = b.length > 0, E = j.length > 0;
        if (S) {
          const D = b.map(Ma);
          w?.(b), N(D);
        }
        if (E) {
          const D = j.map(Ma);
          m?.(j), x(D);
        }
        return (E || S) && y?.({ nodes: j, edges: b }), { deletedNodes: j, deletedEdges: b };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const g = ra(f), m = g ? f : u(f), w = h !== void 0;
        return m ? (h || t.getState().nodes).filter((x) => {
          const N = t.getState().nodeLookup.get(x.id);
          if (N && !g && (x.id === f.id || !N.internals.positionAbsolute))
            return !1;
          const y = Xt(w ? x : N), v = _n(y, m);
          return p && v > 0 || v >= y.width * y.height || v >= m.width * m.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const m = ra(f) ? f : u(f);
        if (!m)
          return !1;
        const w = _n(m, p);
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
        return gm(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Nm();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return de(() => ({
    ...i,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const La = (e) => e.selected, Qx = typeof window < "u" ? window : void 0;
function ew({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = cs(), i = Tn(e, { actInsideInputWithModifier: !1 }), s = Tn(t, { target: Qx });
  G(() => {
    if (i) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(La), edges: a.filter(La) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [i]), G(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function tw(e) {
  const t = xe();
  G(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ns(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Ve.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const si = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, nw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function ow({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: i = 0.5, panOnScrollMode: s = xt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: g, noWheelClassName: m, noPanClassName: w, onViewportChange: x, isControlledViewport: N, paneClickDistance: y, selectionOnDrag: v }) {
  const j = xe(), b = ie(null), { userSelectionActive: S, lib: E, connectionInProgress: D } = pe(nw, me), L = Tn(p), A = ie();
  tw(b);
  const _ = re((R) => {
    x?.({ x: R[0], y: R[1], zoom: R[2] }), N || j.setState({ transform: R });
  }, [x, N]);
  return G(() => {
    if (b.current) {
      A.current = rx({
        domNode: b.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (k) => j.setState((T) => T.paneDragging === k ? T : { paneDragging: k }),
        onPanZoomStart: (k, T) => {
          const { onViewportChangeStart: M, onMoveStart: $ } = j.getState();
          $?.(k, T), M?.(T);
        },
        onPanZoom: (k, T) => {
          const { onViewportChange: M, onMove: $ } = j.getState();
          $?.(k, T), M?.(T);
        },
        onPanZoomEnd: (k, T) => {
          const { onViewportChangeEnd: M, onMoveEnd: $ } = j.getState();
          $?.(k, T), M?.(T);
        }
      });
      const { x: R, y: C, zoom: I } = A.current.getViewport();
      return j.setState({
        panZoom: A.current,
        transform: [R, C, I],
        domNode: b.current.closest(".react-flow")
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
    i,
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
  ]), r.jsx("div", { className: "react-flow__renderer", ref: b, style: si, children: g });
}
const iw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function rw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(iw, me);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const lr = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, sw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function aw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = An.Full, panOnDrag: o, autoPanOnSelection: i, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: g, children: m }) {
  const w = ie(0), x = xe(), { userSelectionActive: N, elementsSelectable: y, dragging: v, connectionInProgress: j, panBy: b, autoPanSpeed: S } = pe(sw, me), E = y && (e || N), D = ie(null), L = ie(), A = ie(/* @__PURE__ */ new Set()), _ = ie(/* @__PURE__ */ new Set()), R = ie(!1), C = ie({ x: 0, y: 0 }), I = ie(!1), k = (P) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(P), x.getState().resetSelectedElements(), x.setState({ nodesSelectionActive: !1 });
  }, T = (P) => {
    if (Array.isArray(o) && o?.includes(2)) {
      P.preventDefault();
      return;
    }
    d?.(P);
  }, M = f ? (P) => f(P) : void 0, $ = (P) => {
    R.current && (P.stopPropagation(), R.current = !1);
  }, B = (P) => {
    const { domNode: q, transform: ae } = x.getState();
    if (L.current = q?.getBoundingClientRect(), !L.current)
      return;
    const ce = P.target === D.current;
    if (!ce && !!P.target.closest(".nokey") || !e || !(a && ce || t) || P.button !== 0 || !P.isPrimary)
      return;
    P.target?.setPointerCapture?.(P.pointerId), R.current = !1;
    const { x: ue, y: V } = Ke(P.nativeEvent, L.current), ee = Qt({ x: ue, y: V }, ae);
    x.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ee.x,
        startY: ee.y,
        x: ue,
        y: V
      }
    }), ce || (P.stopPropagation(), P.preventDefault());
  };
  function W(P, q) {
    const { userSelectionRect: ae } = x.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: oe, connectionLookup: ue, triggerNodeChanges: V, triggerEdgeChanges: ee, defaultEdgeOptions: ge } = x.getState(), we = { x: ae.startX, y: ae.startY }, { x: Ae, y: Ie } = qt(we, ce), Te = {
      startX: we.x,
      startY: we.y,
      x: P < Ae ? P : Ae,
      y: q < Ie ? q : Ie,
      width: Math.abs(P - Ae),
      height: Math.abs(q - Ie)
    }, Qe = A.current, Oe = _.current;
    A.current = new Set(Qr(J, Te, ce, n === An.Partial, !0).map((H) => H.id)), _.current = /* @__PURE__ */ new Set();
    const z = ge?.selectable ?? !0;
    for (const H of A.current) {
      const K = ue.get(H);
      if (K)
        for (const { edgeId: X } of K.values()) {
          const se = oe.get(X);
          se && (se.selectable ?? z) && _.current.add(X);
        }
    }
    if (!sa(Qe, A.current)) {
      const H = Rt(J, A.current, !0);
      V(H);
    }
    if (!sa(Oe, _.current)) {
      const H = Rt(oe, _.current);
      ee(H);
    }
    x.setState({
      userSelectionRect: Te,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!i || !L.current)
      return;
    const [P, q] = es(C.current, L.current, S);
    b({ x: P, y: q }).then((ae) => {
      if (!R.current || !ae) {
        w.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = C.current;
      W(ce, J), w.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(w.current), w.current = 0, I.current = !1;
  };
  G(() => () => U(), []);
  const Y = (P) => {
    const { userSelectionRect: q, transform: ae, resetSelectedElements: ce } = x.getState();
    if (!L.current || !q)
      return;
    const { x: J, y: oe } = Ke(P.nativeEvent, L.current);
    C.current = { x: J, y: oe };
    const ue = qt({ x: q.startX, y: q.startY }, ae);
    if (!R.current) {
      const V = t ? 0 : s;
      if (Math.hypot(J - ue.x, oe - ue.y) <= V)
        return;
      ce(), c?.(P);
    }
    R.current = !0, I.current || (O(), I.current = !0), W(J, oe);
  }, te = (P) => {
    P.button === 0 && (P.target?.releasePointerCapture?.(P.pointerId), !N && P.target === D.current && x.getState().userSelectionRect && k?.(P), x.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(P), x.setState({
      nodesSelectionActive: A.current.size > 0
    })), U());
  }, le = (P) => {
    P.target?.releasePointerCapture?.(P.pointerId), U();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return r.jsxs("div", { className: Se(["react-flow__pane", { draggable: Z, dragging: v, selection: e }]), onClick: E ? void 0 : lr(k, D), onContextMenu: lr(T, D), onWheel: lr(M, D), onPointerEnter: E ? void 0 : p, onPointerMove: E ? Y : h, onPointerUp: E ? te : void 0, onPointerCancel: E ? le : void 0, onPointerDownCapture: E ? B : void 0, onClickCapture: E ? $ : void 0, onPointerLeave: g, ref: D, style: si, children: [m, r.jsx(rw, {})] });
}
function Ar({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: i, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ve.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : i([e]);
}
function vu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: i, isSelectable: s, nodeClickDistance: a }) {
  const c = xe(), [u, l] = F(!1), d = ie();
  return G(() => {
    d.current = Km({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Ar({
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
const cw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function bu() {
  const e = xe();
  return re((n) => {
    const { nodeExtent: o, snapToGrid: i, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = cw(a), h = i ? s[0] : 5, g = i ? s[1] : 5, m = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, x] of l) {
      if (!p(x))
        continue;
      let N = {
        x: x.internals.positionAbsolute.x + m,
        y: x.internals.positionAbsolute.y + w
      };
      i && (N = On(N, s));
      const { position: y, positionAbsolute: v } = Ll({
        nodeId: x.id,
        nextPosition: N,
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
const ls = Lr(null), lw = ls.Provider;
ls.Consumer;
const Nu = () => $n(ls), uw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), dw = (e, t, n) => (o) => {
  const { connectionClickStartHandle: i, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: i?.nodeId === e && i?.id === t && i?.type === n,
    isPossibleEndHandle: s === Ft.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!i,
    valid: d && l
  };
};
function fw({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: i = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const g = a || null, m = e === "target", w = xe(), x = Nu(), { connectOnClick: N, noPanClassName: y, rfId: v } = pe(uw, me), { connectingFrom: j, connectingTo: b, clickConnecting: S, isPossibleEndHandle: E, connectionInProcess: D, clickConnectionInProcess: L, valid: A } = pe(dw(x, g, e), me);
  x || w.getState().onError?.("010", Ve.error010());
  const _ = (I) => {
    const { defaultEdgeOptions: k, onConnect: T, hasDefaultEdges: M } = w.getState(), $ = {
      ...k,
      ...I
    };
    if (M) {
      const { edges: B, setEdges: W, onError: O } = w.getState();
      W(mu($, B, { onError: O }));
    }
    T?.($), c?.($);
  }, R = (I) => {
    if (!x)
      return;
    const k = Kl(I.nativeEvent);
    if (i && (k && I.button === 0 || !k)) {
      const T = w.getState();
      kr.onPointerDown(I.nativeEvent, {
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
        onConnectEnd: (...M) => w.getState().onConnectEnd?.(...M),
        updateConnection: T.updateConnection,
        onConnect: _,
        isValidConnection: n || ((...M) => w.getState().isValidConnection?.(...M) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    k ? d?.(I) : f?.(I);
  }, C = (I) => {
    const { onClickConnectStart: k, onClickConnectEnd: T, connectionClickStartHandle: M, connectionMode: $, isValidConnection: B, lib: W, rfId: O, nodeLookup: U, connection: Y } = w.getState();
    if (!x || !M && !i)
      return;
    if (!M) {
      k?.(I.nativeEvent, { nodeId: x, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: x, type: e, id: g } });
      return;
    }
    const te = Bl(I.target), le = n || B, { connection: Z, isValid: P } = kr.isValid(I.nativeEvent, {
      handle: {
        nodeId: x,
        id: g,
        type: e
      },
      connectionMode: $,
      fromNodeId: M.nodeId,
      fromHandleId: M.id || null,
      fromType: M.type,
      isValidConnection: le,
      flowId: O,
      doc: te,
      lib: W,
      nodeLookup: U
    });
    P && Z && _(Z);
    const q = structuredClone(Y);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, T?.(I, q), w.setState({ connectionClickStartHandle: null });
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
      connectable: o,
      connectablestart: i,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: b,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || E) && (D || L ? s : i)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: N ? C : void 0, ref: h, ...p, children: u });
}
const Ut = be(xu(fw));
function pw({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(Ut, { type: "source", position: n, isConnectable: t })] });
}
function hw({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(Ut, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(Ut, { type: "source", position: o, isConnectable: t })] });
}
function gw() {
  return null;
}
function yw({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(Ut, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Bo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, za = {
  input: pw,
  default: hw,
  output: yw,
  group: gw
};
function mw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const xw = (e) => {
  const { width: t, height: n, x: o, y: i } = Vn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Fe(t) ? t : null,
    height: Fe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${i}px)`
  };
};
function ww({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: i, height: s, transformString: a, userSelectionActive: c } = pe(xw, me), u = bu(), l = ie(null);
  G(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && i !== null && s !== null;
  if (vu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const g = o.getState().nodes.filter((m) => m.selected);
    e(h, g);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Bo, h.key) && (h.preventDefault(), u({
      direction: Bo[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: i,
    height: s
  } }) });
}
const Va = typeof window < "u" ? window : void 0, vw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function ju({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: m, zoomActivationKeyCode: w, elementsSelectable: x, zoomOnScroll: N, zoomOnPinch: y, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: E, autoPanOnSelection: D, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, preventScrolling: C, onSelectionContextMenu: I, noWheelClassName: k, noPanClassName: T, disableKeyboardA11y: M, onViewportChange: $, isControlledViewport: B }) {
  const { nodesSelectionActive: W, userSelectionActive: O } = pe(vw, me), U = Tn(l, { target: Va }), Y = Tn(m, { target: Va }), te = Y || E, le = Y || v, Z = d && te !== !0, P = U || O || Z;
  return ew({ deleteKeyCode: u, multiSelectionKeyCode: g }), r.jsx(ow, { onPaneContextMenu: s, elementsSelectable: x, zoomOnScroll: N, zoomOnPinch: y, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: !U && te, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, zoomActivationKeyCode: w, preventScrolling: C, noWheelClassName: k, noPanClassName: T, onViewportChange: $, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: Z, children: r.jsxs(aw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: i, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: te, autoPanOnSelection: D, isSelecting: !!P, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: Z, children: [e, W && r.jsx(ww, { onSelectionContextMenu: I, noPanClassName: T, disableKeyboardA11y: M })] }) });
}
ju.displayName = "FlowRenderer";
const bw = be(ju), Nw = (e) => (t) => e ? Qr(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function jw(e) {
  return pe(re(Nw(e), [e]), me);
}
const Sw = (e) => e.updateNodeInternals;
function Cw() {
  const e = pe(Sw), [t] = F(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Ew({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const i = xe(), s = ie(null), a = ie(null), c = ie(e.sourcePosition), u = ie(e.targetPosition), l = ie(t), d = n && !!e.internals.handleBounds;
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
function Iw({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: i, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: g, rfId: m, nodeTypes: w, nodeClickDistance: x, onError: N }) {
  const { node: y, internals: v, isParent: j } = pe((P) => {
    const q = P.nodeLookup.get(e), ae = P.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: ae
    };
  }, me);
  let b = y.type || "default", S = w?.[b] || za[b];
  S === void 0 && (N?.("003", Ve.error003(b)), b = "default", S = w?.default || za.default);
  const E = !!(y.draggable || c && typeof y.draggable > "u"), D = !!(y.selectable || u && typeof y.selectable > "u"), L = !!(y.connectable || l && typeof y.connectable > "u"), A = !!(y.focusable || d && typeof y.focusable > "u"), _ = xe(), R = Hl(y), C = Ew({ node: y, nodeType: b, hasDimensions: R, resizeObserver: f }), I = vu({
    nodeRef: C,
    disabled: y.hidden || !E,
    noDragClassName: p,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: x
  }), k = bu();
  if (y.hidden)
    return null;
  const T = rt(y), M = mw(y), $ = D || E || t || n || o || i, B = n ? (P) => n(P, { ...v.userNode }) : void 0, W = o ? (P) => o(P, { ...v.userNode }) : void 0, O = i ? (P) => i(P, { ...v.userNode }) : void 0, U = s ? (P) => s(P, { ...v.userNode }) : void 0, Y = a ? (P) => a(P, { ...v.userNode }) : void 0, te = (P) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: ae } = _.getState();
    D && (!q || !E || ae > 0) && Ar({
      id: e,
      store: _,
      nodeRef: C
    }), t && t(P, { ...v.userNode });
  }, le = (P) => {
    if (!(Fl(P.nativeEvent) || g)) {
      if (Tl.includes(P.key) && D) {
        const q = P.key === "Escape";
        Ar({
          id: e,
          store: _,
          unselect: q,
          nodeRef: C
        });
      } else if (E && y.selected && Object.prototype.hasOwnProperty.call(Bo, P.key)) {
        P.preventDefault();
        const { ariaLabelConfig: q } = _.getState();
        _.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: P.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), k({
          direction: Bo[P.key],
          factor: P.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (g || !C.current?.matches(":focus-visible"))
      return;
    const { transform: P, width: q, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = _.getState();
    if (!ce)
      return;
    Qr(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: q, height: ae }, P, !0).length > 0 || J(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: P[2]
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
      selectable: D,
      parent: j,
      draggable: E,
      dragging: I
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...y.style,
    ...M
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: W, onMouseLeave: O, onContextMenu: U, onClick: te, onDoubleClick: Y, onKeyDown: A ? le : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? Z : void 0, role: y.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${uu}-${m}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: r.jsx(lw, { value: e, children: r.jsx(S, { id: e, data: y.data, type: b, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: y.selected ?? !1, selectable: D, draggable: E, deletable: y.deletable ?? !0, isConnectable: L, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: I, dragHandle: y.dragHandle, zIndex: v.z, parentId: y.parentId, ...T }) }) });
}
var kw = be(Iw);
const Aw = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Su(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, onError: s } = pe(Aw, me), a = jw(e.onlyRenderVisibleElements), c = Cw();
  return r.jsx("div", { className: "react-flow__nodes", style: si, children: a.map((u) => (
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
    r.jsx(kw, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: i, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Su.displayName = "NodeRenderer";
const _w = be(Su);
function Dw(e) {
  return pe(re((n) => {
    if (!e)
      return n.edges.map((i) => i.id);
    const o = [];
    if (n.width && n.height)
      for (const i of n.edges) {
        const s = n.nodeLookup.get(i.source), a = n.nodeLookup.get(i.target);
        s && a && Em({
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
const Tw = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $w = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Oa = {
  [Oo.Arrow]: Tw,
  [Oo.ArrowClosed]: $w
};
function Mw(e) {
  const t = xe();
  return de(() => Object.prototype.hasOwnProperty.call(Oa, e) ? Oa[e] : (t.getState().onError?.("009", Ve.error009(e)), null), [e]);
}
const Pw = ({ id: e, type: t, color: n, width: o = 12.5, height: i = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Mw(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${i}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Cu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), o = pe((s) => s.defaultEdgeOptions), i = de(() => Mm(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return i.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: i.map((s) => r.jsx(Pw, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Cu.displayName = "MarkerDefinitions";
var Rw = be(Cu);
function Eu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: i = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = F({ x: 1, y: 0, width: 0, height: 0 }), h = Se(["react-flow__edge-textwrapper", l]), g = ie(null);
  return G(() => {
    if (g.current) {
      const m = g.current.getBBox();
      p({
        x: m.x,
        y: m.y,
        width: m.width,
        height: m.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [i && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
Eu.displayName = "EdgeText";
const Lw = be(Eu);
function Hn({ path: e, labelX: t, labelY: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Fe(t) && Fe(n) ? r.jsx(Lw, { x: t, y: n, label: o, labelStyle: i, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ha({ pos: e, x1: t, y1: n, x2: o, y2: i }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + i)];
}
function Iu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: i, targetPosition: s = ne.Top }) {
  const [a, c] = Ha({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: i
  }), [u, l] = Ha({
    pos: s,
    x1: o,
    y1: i,
    x2: e,
    y2: t
  }), [d, f, p, h] = Xl({
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
function ku(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x }) => {
    const [N, y, v] = Iu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return r.jsx(Hn, { id: j, path: N, labelX: y, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: x });
  });
}
const zw = ku({ isInternal: !1 }), Au = ku({ isInternal: !0 });
zw.displayName = "SimpleBezierEdge";
Au.displayName = "SimpleBezierEdgeInternal";
function _u(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: N }) => {
    const [y, v, j] = Wo({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: i,
      targetY: s,
      targetPosition: g,
      borderRadius: x?.borderRadius,
      offset: x?.offset,
      stepPosition: x?.stepPosition
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(Hn, { id: b, path: y, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: m, markerStart: w, interactionWidth: N });
  });
}
const Du = _u({ isInternal: !1 }), Tu = _u({ isInternal: !0 });
Du.displayName = "SmoothStepEdge";
Tu.displayName = "SmoothStepEdgeInternal";
function $u(e) {
  return be(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return r.jsx(Du, { ...n, id: o, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Vw = $u({ isInternal: !1 }), Mu = $u({ isInternal: !0 });
Vw.displayName = "StepEdge";
Mu.displayName = "StepEdgeInternal";
function Pu(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m }) => {
    const [w, x, N] = Zl({ sourceX: n, sourceY: o, targetX: i, targetY: s }), y = e.isInternal ? void 0 : t;
    return r.jsx(Hn, { id: y, path: w, labelX: x, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: m });
  });
}
const Ow = Pu({ isInternal: !1 }), Ru = Pu({ isInternal: !0 });
Ow.displayName = "StraightEdge";
Ru.displayName = "StraightEdgeInternal";
function Lu(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, pathOptions: x, interactionWidth: N }) => {
    const [y, v, j] = ql({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: i,
      targetY: s,
      targetPosition: c,
      curvature: x?.curvature
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(Hn, { id: b, path: y, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: m, markerStart: w, interactionWidth: N });
  });
}
const Hw = Lu({ isInternal: !1 }), zu = Lu({ isInternal: !0 });
Hw.displayName = "BezierEdge";
zu.displayName = "BezierEdgeInternal";
const Wa = {
  default: zu,
  straight: Ru,
  step: Mu,
  smoothstep: Tu,
  simplebezier: Au
}, Ba = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Ww = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Bw = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Fa = "react-flow__edgeupdater";
function Ka({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: i, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: i, onMouseEnter: s, onMouseOut: a, className: Se([Fa, `${Fa}-${c}`]), cx: Ww(t, o, e), cy: Bw(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Fw({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: i, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const g = xe(), m = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: b, domNode: S, connectionMode: E, connectionRadius: D, lib: L, onConnectStart: A, cancelConnection: _, nodeLookup: R, rfId: C, panBy: I, updateConnection: k } = g.getState(), T = j.type === "target", M = (W, O) => {
      p(!1), f?.(W, n, j.type, O);
    }, $ = (W) => l?.(n, W), B = (W, O) => {
      p(!0), d?.(v, n, j.type), A?.(W, O);
    };
    kr.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: b,
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
      onConnect: $,
      onConnectStart: B,
      onConnectEnd: (...W) => g.getState().onConnectEnd?.(...W),
      onReconnectEnd: M,
      updateConnection: k,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, w = (v) => m(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), x = (v) => m(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), N = () => h(!0), y = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Ka, { position: c, centerX: o, centerY: i, radius: t, onMouseDown: w, onMouseEnter: N, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && r.jsx(Ka, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: x, onMouseEnter: N, onMouseOut: y, type: "target" })] });
}
function Kw({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: i, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: g, edgeTypes: m, noPanClassName: w, onError: x, disableKeyboardA11y: N }) {
  let y = pe((J) => J.edgeLookup.get(e));
  const v = pe((J) => J.defaultEdgeOptions);
  y = v ? { ...v, ...y } : y;
  let j = y.type || "default", b = m?.[j] || Wa[j];
  b === void 0 && (x?.("011", Ve.error011(j)), j = "default", b = m?.default || Wa.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), E = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), D = !!(y.selectable || o && typeof y.selectable > "u"), L = ie(null), [A, _] = F(!1), [R, C] = F(!1), I = xe(), { zIndex: k, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O } = pe(re((J) => {
    const oe = J.nodeLookup.get(y.source), ue = J.nodeLookup.get(y.target);
    if (!oe || !ue)
      return {
        zIndex: y.zIndex,
        ...Ba
      };
    const V = $m({
      id: e,
      sourceNode: oe,
      targetNode: ue,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: x
    });
    return {
      zIndex: Cm({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: oe,
        targetNode: ue,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...V || Ba
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), me), U = de(() => y.markerStart ? `url('#${Er(y.markerStart, g)}')` : void 0, [y.markerStart, g]), Y = de(() => y.markerEnd ? `url('#${Er(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || M === null || $ === null || B === null)
    return null;
  const te = (J) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: ue, multiSelectionActive: V } = I.getState();
    D && (I.setState({ nodesSelectionActive: !1 }), y.selected && V ? (ue({ nodes: [], edges: [y] }), L.current?.blur()) : oe([e])), i && i(J, y);
  }, le = s ? (J) => {
    s(J, { ...y });
  } : void 0, Z = a ? (J) => {
    a(J, { ...y });
  } : void 0, P = c ? (J) => {
    c(J, { ...y });
  } : void 0, q = u ? (J) => {
    u(J, { ...y });
  } : void 0, ae = l ? (J) => {
    l(J, { ...y });
  } : void 0, ce = (J) => {
    if (!N && Tl.includes(J.key) && D) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: ue } = I.getState();
      J.key === "Escape" ? (L.current?.blur(), oe({ edges: [y] })) : ue([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: k }, children: r.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !D && !i,
      updating: A,
      selectable: D
    }
  ]), onClick: te, onDoubleClick: le, onContextMenu: Z, onMouseEnter: P, onMouseMove: q, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${du}-${g}` : void 0, ref: L, ...y.domAttributes, children: [!R && r.jsx(b, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: D, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: U, markerEnd: Y, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), E && r.jsx(Fw, { edge: y, isReconnectable: E, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O, setUpdateHover: _, setReconnecting: C })] }) });
}
var Xw = be(Kw);
const qw = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Vu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: i, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: m }) {
  const { edgesFocusable: w, edgesReconnectable: x, elementsSelectable: N, onError: y } = pe(qw, me), v = Dw(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(Rw, { defaultColor: e, rfId: n }), v.map((j) => r.jsx(Xw, { id: j, edgesFocusable: w, edgesReconnectable: x, elementsSelectable: N, noPanClassName: i, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: m }, j))] });
}
Vu.displayName = "EdgeRenderer";
const Yw = be(Vu), Uw = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Zw({ children: e }) {
  const t = pe(Uw);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Gw(e) {
  const t = cs(), n = ie(!1);
  G(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Jw = (e) => e.panZoom?.syncViewport;
function Qw(e) {
  const t = pe(Jw), n = xe();
  return G(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function ev(e) {
  return e.connection.inProgress ? { ...e.connection, to: Qt(e.connection.to, e.transform) } : { ...e.connection };
}
function tv(e) {
  return ev;
}
function nv(e) {
  const t = tv();
  return pe(t, me);
}
const ov = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function iv({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: i, width: s, height: a, isValid: c, inProgress: u } = pe(ov, me);
  return !(s && i && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Se(["react-flow__connection", Pl(c)]), children: r.jsx(Ou, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Ou = ({ style: e, type: t = lt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: i, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = nv();
  if (!i)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Pl(o), toNode: d, toHandle: f, pointer: h });
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
    case lt.Bezier:
      [g] = ql(m);
      break;
    case lt.SimpleBezier:
      [g] = Iu(m);
      break;
    case lt.Step:
      [g] = Wo({
        ...m,
        borderRadius: 0
      });
      break;
    case lt.SmoothStep:
      [g] = Wo(m);
      break;
    default:
      [g] = Zl(m);
  }
  return r.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ou.displayName = "ConnectionLine";
const rv = {};
function Xa(e = rv) {
  ie(e), xe(), G(() => {
  }, [e]);
}
function sv() {
  xe(), ie(!1), G(() => {
  }, []);
}
function Hu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: i, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: m, connectionLineComponent: w, connectionLineContainerStyle: x, selectionKeyCode: N, selectionOnDrag: y, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: b, zoomActivationKeyCode: S, deleteKeyCode: E, onlyRenderVisibleElements: D, elementsSelectable: L, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, preventScrolling: I, defaultMarkerColor: k, zoomOnScroll: T, zoomOnPinch: M, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: W, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: Y, onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: P, onPaneScroll: q, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: oe, onEdgeMouseEnter: ue, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, noDragClassName: Te, noWheelClassName: Qe, noPanClassName: Oe, disableKeyboardA11y: z, nodeExtent: H, rfId: K, viewport: X, onViewportChange: se }) {
  return Xa(e), Xa(t), sv(), Gw(n), Qw(X), r.jsx(bw, { onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: P, onPaneContextMenu: ae, onPaneScroll: q, paneClickDistance: ce, deleteKeyCode: E, selectionKeyCode: N, selectionOnDrag: y, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: b, zoomActivationKeyCode: S, elementsSelectable: L, zoomOnScroll: T, zoomOnPinch: M, zoomOnDoubleClick: O, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: W, panOnDrag: U, autoPanOnSelection: Y, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Te, noWheelClassName: Qe, noPanClassName: Oe, disableKeyboardA11y: z, onViewportChange: se, isControlledViewport: !!X, children: r.jsxs(Zw, { children: [r.jsx(Yw, { edgeTypes: t, onEdgeClick: i, onEdgeDoubleClick: a, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, onlyRenderVisibleElements: D, onEdgeContextMenu: oe, onEdgeMouseEnter: ue, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, defaultMarkerColor: k, noPanClassName: Oe, disableKeyboardA11y: z, rfId: K }), r.jsx(iv, { style: m, type: g, component: w, containerStyle: x }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(_w, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: Oe, noDragClassName: Te, disableKeyboardA11y: z, nodeExtent: H, rfId: K }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Hu.displayName = "GraphView";
const av = be(Hu), cv = Ol(), qa = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = o ?? t ?? [], N = n ?? e ?? [], y = d ?? [0, 0], v = f ?? kn;
  Ql(m, w, x);
  const { nodesInitialized: j } = Ir(N, h, g, {
    nodeOrigin: y,
    nodeExtent: v,
    zIndexMode: p
  });
  let b = [0, 0, 1];
  if (a && i && s) {
    const S = Vn(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: E, y: D, zoom: L } = ts(S, i, s, u, l, c?.padding ?? 0.1);
    b = [E, D, L];
  }
  return {
    rfId: "1",
    width: i ?? 0,
    height: s ?? 0,
    transform: b,
    nodes: N,
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
    translateExtent: kn,
    nodeExtent: v,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: Ft.Strict,
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
    connection: { ...Ml },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: cv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: $l,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, lv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: i, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Nx((h, g) => {
  async function m() {
    const { nodeLookup: w, panZoom: x, fitViewOptions: N, fitViewResolver: y, width: v, height: j, minZoom: b, maxZoom: S } = g();
    x && (await xm({
      nodes: w,
      width: v,
      height: j,
      panZoom: x,
      minZoom: b,
      maxZoom: S
    }, N), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...qa({
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
    setNodes: (w) => {
      const { nodeLookup: x, parentLookup: N, nodeOrigin: y, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: b, nodesSelectionActive: S } = g(), { nodesInitialized: E, hasSelectedNodes: D } = Ir(w, x, N, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: b
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
      const { connectionLookup: x, edgeLookup: N } = g();
      Ql(x, N, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, x) => {
      if (w) {
        const { setNodes: N } = g();
        N(w), h({ hasDefaultNodes: !0 });
      }
      if (x) {
        const { setEdges: N } = g();
        N(x), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: x, nodeLookup: N, parentLookup: y, domNode: v, nodeOrigin: j, nodeExtent: b, debug: S, fitViewQueued: E, zIndexMode: D } = g(), { changes: L, updatedInternals: A } = Hm(w, N, y, v, j, b, D);
      A && (Lm(N, y, { nodeOrigin: j, nodeExtent: b, zIndexMode: D }), E ? (m(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), L?.length > 0 && (S && console.log("React Flow: trigger node changes", L), x?.(L)));
    },
    updateNodePositions: (w, x = !1) => {
      const N = [];
      let y = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: b, updateConnection: S, onNodesChangeMiddlewareMap: E } = g();
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
        if (A && b.inProgress && b.fromNode.id === A.id) {
          const C = jt(A, b.fromHandle, ne.Left, !0);
          S({ ...b, from: C });
        }
        _ && A.parentId && N.push({
          id: D,
          parentId: A.parentId,
          rect: {
            ...L.internals.positionAbsolute,
            width: L.measured.width ?? 0,
            height: L.measured.height ?? 0
          }
        }), y.push(R);
      }
      if (N.length > 0) {
        const { parentLookup: D, nodeOrigin: L } = g(), A = as(N, v, D, L);
        y.push(...A);
      }
      for (const D of E.values())
        y = D(y);
      j(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: x, setNodes: N, nodes: y, hasDefaultNodes: v, debug: j } = g();
      if (w?.length) {
        if (v) {
          const b = hu(w, y);
          N(b);
        }
        j && console.log("React Flow: trigger node changes", w), x?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: x, setEdges: N, edges: y, hasDefaultEdges: v, debug: j } = g();
      if (w?.length) {
        if (v) {
          const b = gu(w, y);
          N(b);
        }
        j && console.log("React Flow: trigger edge changes", w), x?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: x, edgeLookup: N, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: j } = g();
      if (x) {
        const b = w.map((S) => ht(S, !0));
        v(b);
        return;
      }
      v(Rt(y, /* @__PURE__ */ new Set([...w]), !0)), j(Rt(N));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: x, edgeLookup: N, nodeLookup: y, triggerNodeChanges: v, triggerEdgeChanges: j } = g();
      if (x) {
        const b = w.map((S) => ht(S, !0));
        j(b);
        return;
      }
      j(Rt(N, /* @__PURE__ */ new Set([...w]))), v(Rt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: x } = {}) => {
      const { edges: N, nodes: y, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: b } = g(), S = w || y, E = x || N, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const _ = v.get(A.id);
        _ && (_.selected = !1), D.push(ht(A.id, !1));
      }
      const L = [];
      for (const A of E)
        A.selected && L.push(ht(A.id, !1));
      j(D), b(L);
    },
    setMinZoom: (w) => {
      const { panZoom: x, maxZoom: N } = g();
      x?.setScaleExtent([w, N]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: x, minZoom: N } = g();
      x?.setScaleExtent([N, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: x, triggerNodeChanges: N, triggerEdgeChanges: y, elementsSelectable: v } = g();
      if (!v)
        return;
      const j = x.reduce((S, E) => E.selected ? [...S, ht(E.id, !1)] : S, []), b = w.reduce((S, E) => E.selected ? [...S, ht(E.id, !1)] : S, []);
      N(j), y(b);
    },
    setNodeExtent: (w) => {
      const { nodes: x, nodeLookup: N, parentLookup: y, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: b, zIndexMode: S } = g();
      w[0][0] === b[0][0] && w[0][1] === b[0][1] && w[1][0] === b[1][0] && w[1][1] === b[1][1] || (Ir(x, N, y, {
        nodeOrigin: v,
        nodeExtent: w,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: x, width: N, height: y, panZoom: v, translateExtent: j } = g();
      return Wm({ delta: w, panZoom: v, transform: x, translateExtent: j, width: N, height: y });
    },
    setCenter: async (w, x, N) => {
      const { width: y, height: v, maxZoom: j, panZoom: b } = g();
      if (!b)
        return !1;
      const S = typeof N?.zoom < "u" ? N.zoom : j;
      return await b.setViewport({
        x: y / 2 - w * S,
        y: v / 2 - x * S,
        zoom: S
      }, { duration: N?.duration, ease: N?.ease, interpolate: N?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Ml }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...qa() })
  };
}, Object.is);
function uv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: i, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [g] = F(() => lv({
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
  return r.jsx(Ex, { value: g, children: r.jsx(Zx, { children: h }) });
}
function dv({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: i, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return $n(ii) ? r.jsx(r.Fragment, { children: e }) : r.jsx(uv, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: i, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const fv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function pv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: i, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, onNodeMouseEnter: N, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: b, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: I, onSelectionDragStop: k, onSelectionContextMenu: T, onSelectionStart: M, onSelectionEnd: $, onBeforeDelete: B, connectionMode: W, connectionLineType: O = lt.Bezier, connectionLineStyle: U, connectionLineComponent: Y, connectionLineContainerStyle: te, deleteKeyCode: le = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: P = !1, selectionMode: q = An.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = Dn() ? "Meta" : "Control", zoomActivationKeyCode: J = Dn() ? "Meta" : "Control", snapToGrid: oe, snapGrid: ue, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ee, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, nodeOrigin: Te = fu, edgesFocusable: Qe, edgesReconnectable: Oe, elementsSelectable: z = !0, defaultViewport: H = Vx, minZoom: K = 0.5, maxZoom: X = 2, translateExtent: se = kn, preventScrolling: he = !0, nodeExtent: ye, defaultMarkerColor: je = "#b1b1b7", zoomOnScroll: Ee = !0, zoomOnPinch: He = !0, panOnScroll: ke = !1, panOnScrollSpeed: li = 0.5, panOnScrollMode: pt = xt.Free, zoomOnDoubleClick: ui = !0, panOnDrag: Bn = !0, onPaneClick: It, onPaneMouseEnter: di, onPaneMouseMove: qe, onPaneMouseLeave: kt, onPaneScroll: fi, onPaneContextMenu: pi, paneClickDistance: hi = 1, nodeClickDistance: st = 0, children: gi, onReconnect: At, onReconnectStart: yi, onReconnectEnd: _t, onEdgeContextMenu: mi, onEdgeDoubleClick: Fn, onEdgeMouseEnter: Kn, onEdgeMouseMove: xi, onEdgeMouseLeave: tn, reconnectRadius: wi = 10, onNodesChange: vi, onEdgesChange: bi, noDragClassName: nn = "nodrag", noWheelClassName: Ni = "nowheel", noPanClassName: Xn = "nopan", fitView: qn, fitViewOptions: Yn, connectOnClick: ji, attributionPosition: Si, proOptions: Ci, defaultEdgeOptions: Ei, elevateNodesOnSelect: Ii = !0, elevateEdgesOnSelect: ki = !1, disableKeyboardA11y: Un = !1, autoPanOnConnect: Ai, autoPanOnNodeDrag: _i, autoPanOnSelection: Di = !0, autoPanSpeed: Zn, connectionRadius: Gn, isValidConnection: Jn, onError: Ti, style: $i, id: Qn, nodeDragThreshold: Mi, connectionDragThreshold: Pi, viewport: Ri, onViewportChange: Li, width: zi, height: Vi, colorMode: Oi = "light", debug: Hi, onScroll: eo, ariaLabelConfig: Wi, zIndexMode: to = "basic", ...Bi }, Dt) {
  const Tt = Qn || "1", Fi = Bx(Oi), Ki = re((on) => {
    on.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), eo?.(on);
  }, [eo]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Bi, onScroll: Ki, style: { ...$i, ...fv }, ref: Dt, className: Se(["react-flow", i, Fi]), id: Qn, role: "application", children: r.jsxs(dv, { nodes: e, edges: t, width: zi, height: Vi, fitView: qn, fitViewOptions: Yn, minZoom: K, maxZoom: X, nodeOrigin: Te, nodeExtent: ye, zIndexMode: to, children: [r.jsx(Wx, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: g, onConnectEnd: m, onClickConnectStart: w, onClickConnectEnd: x, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, edgesFocusable: Qe, edgesReconnectable: Oe, elementsSelectable: z, elevateNodesOnSelect: Ii, elevateEdgesOnSelect: ki, minZoom: K, maxZoom: X, nodeExtent: ye, onNodesChange: vi, onEdgesChange: bi, snapToGrid: oe, snapGrid: ue, connectionMode: W, translateExtent: se, connectOnClick: ji, defaultEdgeOptions: Ei, fitView: qn, fitViewOptions: Yn, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onSelectionDrag: I, onSelectionDragStart: C, onSelectionDragStop: k, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Xn, nodeOrigin: Te, rfId: Tt, autoPanOnConnect: Ai, autoPanOnNodeDrag: _i, autoPanSpeed: Zn, onError: Ti, connectionRadius: Gn, isValidConnection: Jn, selectNodesOnDrag: ee, nodeDragThreshold: Mi, connectionDragThreshold: Pi, onBeforeDelete: B, debug: Hi, ariaLabelConfig: Wi, zIndexMode: to }), r.jsx(av, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: N, onNodeMouseMove: y, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: b, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: Y, connectionLineContainerStyle: te, selectionKeyCode: Z, selectionOnDrag: P, selectionMode: q, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: V, defaultViewport: H, translateExtent: se, minZoom: K, maxZoom: X, preventScrolling: he, zoomOnScroll: Ee, zoomOnPinch: He, zoomOnDoubleClick: ui, panOnScroll: ke, panOnScrollSpeed: li, panOnScrollMode: pt, panOnDrag: Bn, autoPanOnSelection: Di, onPaneClick: It, onPaneMouseEnter: di, onPaneMouseMove: qe, onPaneMouseLeave: kt, onPaneScroll: fi, onPaneContextMenu: pi, paneClickDistance: hi, nodeClickDistance: st, onSelectionContextMenu: T, onSelectionStart: M, onSelectionEnd: $, onReconnect: At, onReconnectStart: yi, onReconnectEnd: _t, onEdgeContextMenu: mi, onEdgeDoubleClick: Fn, onEdgeMouseEnter: Kn, onEdgeMouseMove: xi, onEdgeMouseLeave: tn, reconnectRadius: wi, defaultMarkerColor: je, noDragClassName: nn, noWheelClassName: Ni, noPanClassName: Xn, rfId: Tt, disableKeyboardA11y: Un, nodeExtent: ye, viewport: Ri, onViewportChange: Li }), r.jsx(zx, { onSelectionChange: R }), gi, r.jsx($x, { proOptions: Ci, position: Si }), r.jsx(Tx, { rfId: Tt, disableKeyboardA11y: Un })] }) });
}
var Wu = xu(pv);
const hv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function gv({ children: e }) {
  const t = pe(hv);
  return t ? Cx.createPortal(e, t) : null;
}
function yv({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, o]) });
}
function mv({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
}
var ut;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(ut || (ut = {}));
const xv = {
  [ut.Dots]: 1,
  [ut.Lines]: 1,
  [ut.Cross]: 6
}, wv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Bu({
  id: e,
  variant: t = ut.Dots,
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
  const f = ie(null), { transform: p, patternId: h } = pe(wv, me), g = o || xv[t], m = t === ut.Dots, w = t === ut.Cross, x = Array.isArray(n) ? n : [n, n], N = [x[0] * p[2] || 1, x[1] * p[2] || 1], y = g * p[2], v = Array.isArray(s) ? s : [s, s], j = w ? [y, y] : N, b = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Se(["react-flow__background", l]), style: {
    ...u,
    ...si,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: S, x: p[0] % N[0], y: p[1] % N[1], width: N[0], height: N[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${b[0]},-${b[1]})`, children: m ? r.jsx(mv, { radius: y / 2, className: d }) : r.jsx(yv, { dimensions: j, lineWidth: i, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Bu.displayName = "Background";
const Fu = be(Bu);
function vv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function bv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Nv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function jv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Sv() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function uo({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const Cv = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Ku({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: i, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const g = xe(), { isInteractive: m, minZoomReached: w, maxZoomReached: x, ariaLabelConfig: N } = pe(Cv, me), { zoomIn: y, zoomOut: v, fitView: j } = cs(), b = () => {
    y(), s?.();
  }, S = () => {
    v(), a?.();
  }, E = () => {
    j(i), c?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !m,
      nodesConnectable: !m,
      elementsSelectable: !m
    }), u?.(!m);
  }, L = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(ri, { className: Se(["react-flow__controls", L, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? N["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(uo, { onClick: b, className: "react-flow__controls-zoomin", title: N["controls.zoomIn.ariaLabel"], "aria-label": N["controls.zoomIn.ariaLabel"], disabled: x, children: r.jsx(vv, {}) }), r.jsx(uo, { onClick: S, className: "react-flow__controls-zoomout", title: N["controls.zoomOut.ariaLabel"], "aria-label": N["controls.zoomOut.ariaLabel"], disabled: w, children: r.jsx(bv, {}) })] }), n && r.jsx(uo, { className: "react-flow__controls-fitview", onClick: E, title: N["controls.fitView.ariaLabel"], "aria-label": N["controls.fitView.ariaLabel"], children: r.jsx(Nv, {}) }), o && r.jsx(uo, { className: "react-flow__controls-interactive", onClick: D, title: N["controls.interactive.ariaLabel"], "aria-label": N["controls.interactive.ariaLabel"], children: m ? r.jsx(Sv, {}) : r.jsx(jv, {}) }), d] });
}
Ku.displayName = "Controls";
const Xu = be(Ku);
function Ev({ id: e, x: t, y: n, width: o, height: i, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: g, backgroundColor: m } = s || {}, w = a || g || m;
  return r.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: o, height: i, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (x) => h(x, e) : void 0 });
}
const Iv = be(Ev), kv = (e) => e.nodes.map((t) => t.id), ur = (e) => e instanceof Function ? e : () => e;
function Av({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: i,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = Iv,
  onClick: a
}) {
  const c = pe(kv, me), u = ur(t), l = ur(e), d = ur(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(Dv, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: i, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function _v({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: i, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((g) => {
    const m = g.nodeLookup.get(e);
    if (!m)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = m.internals.userNode, { x, y: N } = m.internals.positionAbsolute, { width: y, height: v } = rt(w);
    return {
      node: w,
      x,
      y: N,
      width: y,
      height: v
    };
  }, me);
  return !l || l.hidden || !Hl(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: i, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const Dv = be(_v);
var Tv = be(Av);
const $v = 200, Mv = 150, Pv = (e) => !e.hidden, Rv = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Vl(Vn(e.nodeLookup, { filter: Pv }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Lv = "react-flow__minimap-desc";
function qu({
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
  onNodeClick: g,
  pannable: m = !1,
  zoomable: w = !1,
  ariaLabel: x,
  inversePan: N,
  zoomStep: y = 1,
  offsetScale: v = 5
}) {
  const j = xe(), b = ie(null), { boundingRect: S, viewBB: E, rfId: D, panZoom: L, translateExtent: A, flowWidth: _, flowHeight: R, ariaLabelConfig: C } = pe(Rv, me), I = e?.width ?? $v, k = e?.height ?? Mv, T = S.width / I, M = S.height / k, $ = Math.max(T, M), B = $ * I, W = $ * k, O = v * $, U = S.x - (B - S.width) / 2 - O, Y = S.y - (W - S.height) / 2 - O, te = B + O * 2, le = W + O * 2, Z = `${Lv}-${D}`, P = ie(0), q = ie();
  P.current = $, G(() => {
    if (b.current && L)
      return q.current = Gm({
        domNode: b.current,
        panZoom: L,
        getTransform: () => j.getState().transform,
        getViewScale: () => P.current
      }), () => {
        q.current?.destroy();
      };
  }, [L]), G(() => {
    q.current?.update({
      translateExtent: A,
      width: _,
      height: R,
      inversePan: N,
      pannable: m,
      zoomStep: y,
      zoomable: w
    });
  }, [m, w, N, y, A, _, R]);
  const ae = h ? (oe) => {
    const [ue, V] = q.current?.pointer(oe) || [0, 0];
    h(oe, { x: ue, y: V });
  } : void 0, ce = g ? re((oe, ue) => {
    const V = j.getState().nodeLookup.get(ue).internals.userNode;
    g(oe, V);
  }, []) : void 0, J = x ?? C["minimap.ariaLabel"];
  return r.jsx(ri, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: I, height: k, viewBox: `${U} ${Y} ${te} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: b, onClick: ae, children: [J && r.jsx("title", { id: Z, children: J }), r.jsx(Tv, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: i, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${Y - O}h${te + O * 2}v${le + O * 2}h${-te - O * 2}z
        M${E.x},${E.y}h${E.width}v${E.height}h${-E.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
qu.displayName = "MiniMap";
const Yu = be(qu), zv = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Vv = {
  [Yt.Line]: "right",
  [Yt.Handle]: "bottom-right"
};
function Ov({ nodeId: e, position: t, variant: n = Yt.Handle, className: o, style: i = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: g, onResizeStart: m, onResize: w, onResizeEnd: x }) {
  const N = Nu(), y = typeof e == "string" ? e : N, v = xe(), j = ie(null), b = n === Yt.Handle, S = pe(re(zv(b && h), [b, h]), me), E = ie(null), D = t ?? Vv[n];
  G(() => {
    if (!(!j.current || !y))
      return E.current || (E.current = ux({
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
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: I, nodeOrigin: k } = v.getState(), T = [], M = { x: A.x, y: A.y }, $ = C.get(y);
          if ($ && $.expandParent && $.parentId) {
            const B = $.origin ?? k, W = A.width ?? $.measured.width ?? 0, O = A.height ?? $.measured.height ?? 0, U = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: W,
                height: O,
                ...Wl({
                  x: A.x ?? $.position.x,
                  y: A.y ?? $.position.y
                }, { width: W, height: O }, $.parentId, C, B)
              }
            }, Y = as([U], C, I, k);
            T.push(...Y), M.x = A.x ? Math.max(B[0] * W, A.x) : void 0, M.y = A.y ? Math.max(B[1] * O, A.y) : void 0;
          }
          if (M.x !== void 0 && M.y !== void 0) {
            const B = {
              id: y,
              type: "position",
              position: { ...M }
            };
            T.push(B);
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
          for (const B of _) {
            const W = {
              ...B,
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
  return r.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...L, n, o]), ref: j, style: {
    ...i,
    scale: S,
    ...a && { [b ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
be(Ov);
function Hv(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Mn(e.state),
    layout: e.layout
  };
}
function Wv(e) {
  return JSON.stringify(
    {
      state: Mn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Bv(e, t) {
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
      state: qo(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Fv(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), i = URL.createObjectURL(o), s = document.createElement("a");
  s.href = i, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(i);
}
function Ya({
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
function Kv({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: i = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = de(
    () => d ? Cd(d) : null,
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
        f ? /* @__PURE__ */ r.jsx(Ed, { fallback: /* @__PURE__ */ r.jsx(
          Ya,
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
          Ya,
          {
            document: e,
            readOnly: n,
            minHeight: i,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(Xv, { diagnostics: u })
      ]
    }
  );
}
function Xv({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", i = qv(t);
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
function qv(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const Yv = { language: "json", displayName: "JSON" };
function Uv({ draft: e, onApply: t }) {
  const n = de(() => Wv(e), [e]), [o, i] = F(n), [s, a] = F(n), [c, u] = F(null);
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
          /* @__PURE__ */ r.jsx(Gt, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      Kv,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: Yv,
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
function Le(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function us(e, t) {
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
function Zt(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function ds(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(kc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(Vr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(_d, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Ot, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(Ad, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(Ko, { size: 15 });
  }
}
const Zv = ["Single", "Array", "List", "HashSet"];
function Uu(e) {
  const [t, n] = F(null), [o, i] = F(null);
  G(() => {
    let u = !1;
    return xp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), vp(e).then(
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
  const s = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = _s(u);
      return {
        value: l,
        label: Lc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: sf(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = _s(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function Gv(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Jv(e, t, n) {
  return {
    add: () => {
      const o = Ud(n.namePrefix, e.map((i) => mn(i, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, i) => t(e.map((s, a) => a === o ? n.patch(s, i) : s)),
    remove: (o) => t(e.filter((i, s) => s !== o))
  };
}
function Ua({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: i, onChange: s }) {
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
const Qv = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function e0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: Zv.map((o) => /* @__PURE__ */ r.jsx("option", { value: o, children: Qv[o] }, o)) });
}
function t0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function n0({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const i = t0(t, e);
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
function o0({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: i, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(Ht, { size: 14 }),
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
function i0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(bn, { size: 14 }) }) });
}
function r0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (o) => n(o.target.checked) });
}
function fs({
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
  const { add: g, update: m, remove: w } = Jv(e, h, {
    namePrefix: i,
    nameKeys: s,
    create: (y) => l(y, Gv(t)),
    patch: d
  }), x = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], N = i.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    o0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: x,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, v) => {
        const j = mn(y, s), b = Rc(y), S = mn(y, Vc), E = S ? p?.get(S) : void 0, D = b.collectionKind === "Single" ? o(b.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${i} name`, value: j, onChange: (L) => m(v, { name: L.target.value }) }),
            E ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: E, children: E }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            Ua,
            {
              ariaLabel: `${i} type`,
              value: b.alias,
              options: t,
              placeholder: "Type",
              onChange: (L) => m(v, { type: { alias: L, collectionKind: b.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            e0,
            {
              ariaLabel: `${i} collection kind`,
              value: b.collectionKind,
              onChange: (L) => m(v, { type: { alias: b.alias, collectionKind: L } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            n0,
            {
              ariaLabel: `${i} default value`,
              value: Qd(y.default),
              editor: D,
              onChange: (L) => m(v, { default: Jd(L) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            Ua,
            {
              ariaLabel: `${i} storage driver`,
              value: mn(y, cf),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (L) => m(v, { storageDriverType: L || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            r0,
            {
              ariaLabel: `${i} required`,
              checked: y.isRequired === !0,
              onChange: (L) => m(v, { isRequired: L })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(i0, { label: `Remove ${N} ${j || v + 1}`, onRemove: () => w(v) })
        ] }, v);
      })
    }
  );
}
function Zu({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: i = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    fs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Variable",
      nameKeys: af,
      title: i,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => Zd({ name: l, alias: d }),
      patch: (l, d) => Gd(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function s0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: i }) {
  return /* @__PURE__ */ r.jsx(
    fs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: zc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => ef({ name: s, alias: a }),
      patch: (s, a) => tf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: i
    }
  );
}
function a0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: i }) {
  return /* @__PURE__ */ r.jsx(
    fs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: zc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => nf({ name: s, alias: a }),
      patch: (s, a) => of(s, a),
      columns: { default: !1, storage: !1 },
      onChange: i
    }
  );
}
function No(e) {
  return (e ?? []).filter(ko);
}
function c0({ context: e, variables: t, title: n, addLabel: o, emptyLabel: i, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Uu(e);
  return /* @__PURE__ */ r.jsx(
    Zu,
    {
      items: No(t),
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
function l0({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [i, s] = F(e?.name ?? ""), [a, c] = F(e?.description ?? "");
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
function u0({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: i }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = Uu(n), u = No(t.state.variables), l = No(t.state.inputs), d = No(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      l0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: i
      }
    ),
    /* @__PURE__ */ r.jsx(
      Zu,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      s0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      a0,
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
        /* @__PURE__ */ r.jsx("time", { children: Le(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const Za = "application/x-elsa-activity-version-id", d0 = 6, f0 = 1200, p0 = 250, h0 = [10, 25, 50], g0 = 10, Ga = "elsa-studio-workflow-palette-width", Ja = "elsa-studio-workflow-inspector-width", Qa = "elsa-studio-workflow-palette-collapsed", ec = "elsa-studio-workflow-inspector-collapsed", Gu = "elsa-studio-workflow-side-panel-maximized", dn = 180, fn = 460, y0 = 260, pn = 260, hn = 560, m0 = 320, tc = 42, fo = 16, Ju = Ze.createContext(null), Qu = Ze.createContext(null);
function x0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function ed(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function St(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Ct(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function w0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const o = e.match(/\{[\s\S]*?\}/);
  o && t.push(o[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = nc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const i = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return nc(i, s);
}
function nc(e, t) {
  const n = typeof e == "string" ? oc(e) : void 0, o = typeof t == "string" ? oc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function oc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function v0(e, t) {
  return e.rootActivityVersionId ?? b0(t, e.rootKind)?.activityVersionId ?? null;
}
function b0(e, t) {
  return e.find((n) => N0(n) === t);
}
function N0(e) {
  return e ? j0(e) ? "flowchart" : S0(e) ? "sequence" : null : null;
}
function _r(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((i, s) => Ce(i).localeCompare(Ce(s)))
  }));
}
function j0(e) {
  return Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function S0(e) {
  return Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function C0(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function td(e) {
  return _0(e.rootActivityType) || e.rootActivityType;
}
function E0(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function I0(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function k0(e, t) {
  return ic(t) - ic(e);
}
function ic(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function nd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function od(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function A0(e) {
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
function _0(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function D0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    po(t, n.typeName, n), po(t, n.name, n), po(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    po(t, o, n);
  }
  return t;
}
function T0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(gn(o?.activityTypeKey)) ?? n.get(gn(Zt(o?.activityTypeKey))) ?? n.get(gn(o?.displayName)) ?? n.get(gn(e.activityVersionId)) ?? null;
}
function po(e, t, n) {
  const o = gn(t);
  o && !e.has(o) && e.set(o, n);
}
function gn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function rc(e, t, n, o) {
  const i = ai();
  if (!i) return t;
  const s = i.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? jo(a, n, o) : t;
}
function sc(e, t) {
  const n = ai();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function $0() {
  const e = ai();
  if (!e) return null;
  const t = e.getItem(Gu);
  return t === "palette" || t === "inspector" ? t : null;
}
function ai() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function cn(e, t) {
  const n = ai();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function jo(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function id(e) {
  switch (M0(e)) {
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
function M0(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function P0(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (Wr(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const i = Nn(n, []);
  return new Set(i?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function ac(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function cc(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function lc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function R0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function rd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function L0(e) {
  const t = rd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function z0(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Re(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function V0(e) {
  return cd(Re(e));
}
function sd(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ce(o) : void 0
  });
  for (const i of De(e))
    for (const s of i.activities) sd(s, t, n);
  return n;
}
function ad(e, t = []) {
  if (!e) return t;
  for (const n of Yc(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of De(e))
    for (const o of n.activities) ad(o, t);
  return t;
}
function yn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function O0(e) {
  return `${e.id}-${cd(JSON.stringify(e.state))}`;
}
function cd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ps(e) {
  return e.status.toLowerCase() === "rejected";
}
function H0(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function W0(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return B0(e, n) ? `Run ${t} was not found.` : n;
}
function B0(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((i) => typeof i == "string" && /not found/i.test(i));
  } catch {
    return /not found/i.test(t);
  }
}
function en({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const ld = { workflowActivity: F0 }, ud = { workflow: X0 };
function F0({ data: e, selected: t }) {
  const n = e, o = n.runtime, i = !n.suppressFlowPorts, s = i ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = K0(n), u = Ze.useContext(Qu)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        i && n.acceptsInbound ? /* @__PURE__ */ r.jsx(Ut, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Do(u.state)}`, children: /* @__PURE__ */ r.jsx(Co, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: ds(n.icon) }),
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
          o.status ? /* @__PURE__ */ r.jsx(en, { status: o.status, subStatus: o.subStatus }) : null,
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
          return /* @__PURE__ */ r.jsxs(Ze.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ r.jsx(Ut, { type: "source", position: ne.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function K0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((i) => !!i).join(" · ");
}
function X0(e) {
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
  } = e, p = Ze.useContext(Ju), [h, g] = F(!1), [m, w, x] = Wo({ sourceX: n, sourceY: o, targetX: i, targetY: s, sourcePosition: a, targetPosition: c }), N = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      Hn,
      {
        id: t,
        path: m,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: N ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: x,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    p ? /* @__PURE__ */ r.jsx(gv, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", N ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${x}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => p.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ r.jsx(Ht, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(bn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function q0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: i }) {
  const [s, a] = F(""), [c, u] = F(0), l = ie(null), d = ie(null), f = de(() => {
    const N = s.trim().toLowerCase(), y = n.filter(C0);
    return N ? y.filter((v) => Ce(v).toLowerCase().includes(N) || v.activityTypeKey.toLowerCase().includes(N) || (v.category ?? "").toLowerCase().includes(N) || (v.description ?? "").toLowerCase().includes(N)) : y;
  }, [n, s]), p = de(() => _r(f), [f]), h = de(() => p.flatMap((N) => N.activities), [p]);
  G(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), G(() => {
    const N = (v) => {
      l.current?.contains(v.target) || i();
    }, y = (v) => {
      v.key === "Escape" && i();
    };
    return document.addEventListener("mousedown", N, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", N, !0), document.removeEventListener("keydown", y);
    };
  }, [i]);
  const g = (N) => {
    if (N.key === "ArrowDown")
      N.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (N.key === "ArrowUp")
      N.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (N.key === "Enter") {
      N.preventDefault();
      const y = h[c];
      y && o(y);
    }
  }, m = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let x = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: m, top: w }, onMouseDown: (N) => N.stopPropagation(), onClick: (N) => N.stopPropagation(), children: [
    /* @__PURE__ */ r.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: s,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (N) => {
          a(N.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ r.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No matching activities." }) : p.map((N) => /* @__PURE__ */ r.jsxs("section", { children: [
      /* @__PURE__ */ r.jsx("h4", { children: N.category }),
      N.activities.map((y) => {
        x += 1;
        const v = x, j = v === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(v),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Ce(y) }),
              /* @__PURE__ */ r.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, N.category)) })
  ] });
}
function So({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  const i = Hd(t.map((s) => s.id), n, o);
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
function uc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Y0 = "Expressions/UnresolvedVariable";
function U0(e) {
  return String(e.type ?? e.code ?? "");
}
function Z0(e) {
  return U0(e) === Y0;
}
function G0(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...i] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: i.length > 0 ? i.join("/") : null
  };
}
function J0(e) {
  return (e ?? []).filter(Z0).map((t) => ({
    error: t,
    path: G0(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function Q0({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(Gt, { size: 14 }),
      " No validation errors"
    ] });
  const o = J0(n), i = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(ft, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(Dd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function eb({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ps(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(ft, { size: 16 }) : /* @__PURE__ */ r.jsx(Gt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function tb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ps(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(en, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(ft, { size: 14 }),
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
        /* @__PURE__ */ r.jsx("dd", { children: dc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: dc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? Le(e.expiresAt) : "None", children: e.expiresAt ? Le(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function dc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function hs({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function gs({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(Ko, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function Wn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(ft, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function dd({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(Gt, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Vt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: i }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await A0(e), o(n);
    } catch {
      i(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(Td, { size: 12 }) });
}
function nb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [i, s] = F("loading"), [a, c] = F(""), [u, l] = F(""), [d, f] = F(null), [p, h] = F([]), g = n?.trim().toLowerCase() ?? "", m = de(
    () => g ? p.filter((S) => E0(S, g)) : p,
    [g, p]
  ), w = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, E) => S.localeCompare(E)),
    [p]
  ), x = St(t, "weaver.workflows.explain-executable"), N = re(async () => {
    s("loading"), c("");
    try {
      h(await el(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  G(() => {
    N();
  }, [N]);
  const y = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await Qc(e, S.artifactId), D = od(E);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, v = (S) => {
    x && Ct(t, x, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, b = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        N();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ r.jsx(Fo, { size: 14 }),
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
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: w.map((S) => /* @__PURE__ */ r.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ r.jsx(Ac, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsx(Wn, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(dd, { status: u, run: d }) : null,
    i === "loading" ? /* @__PURE__ */ r.jsx(hs, {}) : null,
    i === "ready" && m.length === 0 ? /* @__PURE__ */ r.jsx(
      gs,
      {
        icon: /* @__PURE__ */ r.jsx(Ot, { size: 22 }),
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
            /* @__PURE__ */ r.jsx(Vt, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: b })
          ] }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ r.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ r.jsx(Vt, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: b })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ r.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ r.jsx(Vt, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ r.jsx(ob, { executable: S, onCopied: j, onCopyFailed: b }),
        /* @__PURE__ */ r.jsx("span", { children: td(S) }),
        /* @__PURE__ */ r.jsx("span", { children: Le(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ r.jsx(Ot, { size: 13 }),
            " Run"
          ] }),
          x ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ r.jsx(ot, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function ob({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, i = e.sourceVersion;
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: nd(e.sourceKind) }),
    o ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ r.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ r.jsx(Vt, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    i ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      i
    ] }) : null
  ] });
}
function ib({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [i, s] = F("loading"), [a, c] = F(""), [u, l] = F(""), [d, f] = F(null), [p, h] = F([]), g = St(t, "weaver.workflows.explain-executable"), m = re(async () => {
    s("loading"), c("");
    try {
      const j = await el(e);
      h(j.filter((b) => I0(b, n)).sort(k0)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  G(() => {
    m();
  }, [m, o]);
  const w = async (j) => {
    l(""), f(null), c("");
    try {
      const b = await Qc(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: od(b) }), l(`Started ${j.artifactId}`);
    } catch (b) {
      c(b instanceof Error ? b.message : String(b));
    }
  }, x = (j) => {
    g && Ct(t, g, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, N = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (j) => {
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
        /* @__PURE__ */ r.jsx(Or, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: N, children: "Open list" })
    ] }),
    i === "failed" ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(ft, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(dd, { status: u, run: d, compact: !0 }) : null,
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
        /* @__PURE__ */ r.jsx("span", { children: Le(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ r.jsx(Vt, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ r.jsx(Vt, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            nd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: td(j) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          w(j);
        }, children: [
          /* @__PURE__ */ r.jsx(Ot, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => x(j), children: [
          /* @__PURE__ */ r.jsx(ot, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function rb() {
  const [e, t] = F(() => rc(Ga, y0, dn, fn)), [n, o] = F(() => rc(Ja, m0, pn, hn)), [i, s] = F(() => sc(Qa, !1)), [a, c] = F(() => sc(ec, !1)), [u, l] = F($0);
  G(() => {
    cn(Ga, String(e));
  }, [e]), G(() => {
    cn(Ja, String(n));
  }, [n]), G(() => {
    cn(Qa, String(i));
  }, [i]), G(() => {
    cn(ec, String(a));
  }, [a]), G(() => {
    cn(Gu, u);
  }, [u]), G(() => {
    if (!u) return;
    const y = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [u]);
  const d = re((y) => {
    l((v) => v === y ? null : v), y === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = re((y) => {
    y === "palette" ? s(!1) : c(!1), l((v) => v === y ? null : y);
  }, []), p = re((y, v) => {
    l(null), y === "palette" ? (s(!1), t((j) => jo(j + v, dn, fn))) : (c(!1), o((j) => jo(j + v, pn, hn)));
  }, []), h = re((y, v) => {
    v.preventDefault(), l(null), y === "palette" ? s(!1) : c(!1);
    const j = v.clientX, b = y === "palette" ? e : n, S = y === "palette" ? dn : pn, E = y === "palette" ? fn : hn;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const _ = y === "palette" ? A.clientX - j : j - A.clientX, R = jo(b + _, S, E);
      y === "palette" ? t(R) : o(R);
    }, L = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", L), window.removeEventListener("pointercancel", L);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", L), window.addEventListener("pointercancel", L);
  }, [n, e]), g = re((y, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(y, y === "palette" ? -fo : fo)) : v.key === "ArrowRight" ? (v.preventDefault(), p(y, y === "palette" ? fo : -fo)) : v.key === "Home" ? (v.preventDefault(), y === "palette" ? t(dn) : o(pn)) : v.key === "End" && (v.preventDefault(), y === "palette" ? t(fn) : o(hn));
  }, [p]), m = !i && u !== "inspector", w = !a && u !== "palette", x = [
    "wf-editor-body",
    i ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), N = {
    "--wf-palette-width": `${i ? tc : e}px`,
    "--wf-inspector-width": `${a ? tc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: i,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: m,
    inspectorExpanded: w,
    editorBodyClassName: x,
    editorBodyStyle: N,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: g
  };
}
const sb = 50;
function fc() {
  return { past: [], future: [] };
}
function ab(e) {
  return e.past.length > 0;
}
function cb(e) {
  return e.future.length > 0;
}
function pc(e, t, n = sb) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function lb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function ub(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function db({ draft: e, restoreDraft: t }) {
  const n = ie(fc()), o = ie(null), i = ie(""), s = ie(!1), [a, c] = F(0), u = re((m) => {
    n.current = fc(), o.current = m ? yn(m) : null, i.current = m ? Re(m) : "", s.current = !1, c(0);
  }, []);
  G(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const m = Re(e);
    if (m === i.current) return;
    const w = window.setTimeout(() => {
      const x = o.current;
      x && (n.current = pc(n.current, x), c((N) => N + 1)), o.current = yn(e), i.current = m;
    }, p0);
    return () => window.clearTimeout(w);
  }, [e]);
  const l = re(() => {
    if (!e) return;
    const m = Re(e);
    if (m === i.current) return;
    const w = o.current;
    w && (n.current = pc(n.current, w)), o.current = yn(e), i.current = m;
  }, [e]), d = re((m) => {
    s.current = !0, o.current = yn(m), i.current = Re(m), t(m), c((w) => w + 1);
  }, [t]), f = re(() => {
    if (!e) return;
    l();
    const m = lb(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), p = re(() => {
    if (!e) return;
    l();
    const m = ub(n.current, e);
    m && (n.current = m.history, d(m.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: g } = de(() => {
    const m = !!e && !!o.current && Re(e) !== i.current;
    return {
      canUndoNow: ab(n.current) || m,
      canRedoNow: cb(n.current) && !m
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: g };
}
const fb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function pb(e, t) {
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
function hb() {
  const [e, t] = Id(pb, fb), n = de(() => ({
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
const gb = 320, yb = 140;
function mb(e, t, n) {
  return n === "sequence" ? xb(e) : wb(e, t);
}
function xb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function wb(e, t) {
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
      n.set(p, { x: d * gb, y: h * yb });
    });
  return n;
}
function vb({
  draft: e,
  scope: t,
  scopeOwner: n,
  catalog: o,
  catalogByVersion: i,
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
  const [g, m] = F([]), [w, x] = F([]), [N, y] = F(null), [v, j] = F(null), [b, S] = F(null), E = ie(null), D = ie(null), L = ie(null), A = ie(null), _ = ie(!1);
  G(() => {
    if (!n) {
      m([]), x([]);
      return;
    }
    const z = s ? hr(n, o, e?.layout ?? []) : t ? Fc(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    m(z.nodes), x(z.edges);
  }, [o, e?.layout, s, t, n]);
  const R = re((z, H) => {
    if (e?.state.rootActivity && s)
      return;
    const K = yr(z, cc(z));
    if (!e?.state.rootActivity) {
      d(
        ({ draft: X }) => X ? { ...X, state: { ...X.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (!t) {
      if (!De(K)[0]) {
        p(""), h("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      d(({ draft: se }) => {
        if (!se?.state.rootActivity) return null;
        const he = se.state.rootActivity, ye = gr(K, [], [he]), je = H ? [
          ...se.layout.filter((Ee) => Ee.nodeId !== he.nodeId),
          {
            nodeId: he.nodeId,
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
      const he = Nn(X.state.rootActivity, se);
      if (!he) return null;
      const ye = gr(X.state.rootActivity, se, [...he.slot.activities, K]), je = H ? [
        ...X.layout.filter((Ee) => Ee.nodeId !== K.nodeId),
        {
          nodeId: K.nodeId,
          x: Math.round(H.x),
          y: Math.round(H.y)
        }
      ] : X.layout;
      return {
        ...X,
        layout: je,
        state: {
          ...X.state,
          rootActivity: ye
        }
      };
    }, K.nodeId);
  }, [e?.state.rootActivity, s, t, d, h, p]), C = re((z, H) => {
    const K = yr(z, cc(z)), X = {
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
        icon: Yo(z),
        childSlots: De(K),
        acceptsInbound: String(z.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Uc(K, z)
      }
    };
    return { activityNode: K, node: X };
  }, []), I = re((z, H, K = []) => {
    s || l(({ draft: X, frames: se }) => {
      if (!X) return null;
      const he = Sf(X.layout, z), ye = X.state.rootActivity;
      if (!ye) return { ...X, layout: he };
      const je = Nn(ye, se);
      if (!je) return { ...X, layout: he };
      const Ee = Nf(je, z, H, K), He = je.slot.mode === "flowchart" ? jf(Ee, H) : Ee;
      return {
        ...X,
        layout: he,
        state: {
          ...X.state,
          rootActivity: Kc(ye, se, He)
        }
      };
    });
  }, [s, l]), k = re((z, H) => {
    if (!E.current) return null;
    const K = E.current.getBoundingClientRect();
    return N ? N.screenToFlowPosition({ x: z, y: H }) : {
      x: z - K.left,
      y: H - K.top
    };
  }, [N]), T = re((z, H) => document.elementFromPoint(z, H)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), M = re((z, H, K) => {
    const X = g.find((ke) => ke.id === H.source), se = g.find((ke) => ke.id === H.target), he = X && se ? R0(X, se) : X ? lc(X) : K, ye = C(z, he), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], He = $f(w, H, ye.node.id);
    m(Ee), x(He), f(ye.node.id), I(Ee, He, [ye.activityNode]);
  }, [I, C, w, g, f]), $ = re((z, H, K) => {
    if (!c || !E.current) return !1;
    const X = E.current.getBoundingClientRect();
    if (!(H >= X.left && H <= X.right && K >= X.top && K <= X.bottom)) return !1;
    const he = k(H, K);
    if (!he) return !1;
    if (a) {
      const ye = T(H, K), je = ye ? w.find((Ee) => Ee.id === ye) : void 0;
      if (je)
        return M(z, je, he), !0;
    }
    return R(z, he), !0;
  }, [R, c, w, T, a, M, k]);
  G(() => {
    const z = (K) => {
      const X = L.current;
      if (!X) return;
      Math.hypot(K.clientX - X.startX, K.clientY - X.startY) >= d0 && (X.dragging = !0);
    }, H = (K) => {
      const X = L.current;
      if (L.current = null, !X?.dragging || !E.current || A.current) return;
      const se = E.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (_.current = !0, window.setTimeout(() => {
        _.current = !1;
      }, 0), $(X.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", z), window.addEventListener("pointerup", H), window.addEventListener("pointercancel", H), () => {
      window.removeEventListener("pointermove", z), window.removeEventListener("pointerup", H), window.removeEventListener("pointercancel", H);
    };
  }, [N, $]);
  const B = (z, H) => {
    A.current = { activityVersionId: H.activityVersionId, handledDrop: !1 }, z.dataTransfer.setData(Za, H.activityVersionId), z.dataTransfer.setData("text/plain", H.activityVersionId), z.dataTransfer.effectAllowed = "copy";
  }, W = (z, H) => {
    const K = A.current;
    A.current = null, !K?.handledDrop && (z.clientX === 0 && z.clientY === 0 || $(H, z.clientX, z.clientY) && (_.current = !0, window.setTimeout(() => {
      _.current = !1;
    }, 0)));
  }, O = (z, H) => {
    z.button === 0 && (L.current = {
      activity: H,
      startX: z.clientX,
      startY: z.clientY,
      dragging: !1
    });
  }, U = (z) => {
    _.current || c && R(z);
  }, Y = (z) => {
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
    const H = z.dataTransfer.getData(Za) || z.dataTransfer.getData("text/plain");
    if (!H || (z.stopPropagation(), A.current?.activityVersionId === H && (A.current.handledDrop = !0), !c)) return;
    const K = i.get(H);
    K && $(K, z.clientX, z.clientY);
  }, Z = () => {
    if (!a) return;
    const z = E.current?.getBoundingClientRect();
    z && j({
      kind: "fromEmpty",
      clientX: z.left + z.width / 2,
      clientY: z.top + z.height / 2
    });
  }, P = (z) => {
    const H = s ? z.filter((K) => K.type === "select") : z;
    H.length !== 0 && m((K) => hu(H, K));
  }, q = (z) => {
    s || x((H) => gu(z, H));
  }, ae = (z) => !z.source || !z.target || z.source === z.target || !a ? !1 : !z.targetHandle, ce = (z) => {
    if (!e?.state.rootActivity || !t || !a || !ae(z)) return;
    const H = Ao(z.source, z.target, z.sourceHandle ?? "Done", z.targetHandle ?? void 0), K = mu(H, w);
    x(K), I(g, K);
  }, J = () => {
    I(g, w);
  }, oe = !s && g.length > 0, ue = re(() => {
    if (s || g.length === 0) return;
    const z = t?.slot.mode === "sequence" ? "sequence" : "flowchart", H = mb(g, w, z), K = g.map((X) => {
      const se = H.get(X.id);
      return se ? { ...X, position: se } : X;
    });
    m(K), I(K, w), window.requestAnimationFrame(() => N?.fitView({ padding: 0.2 })), p("Rearranged the canvas.");
  }, [w, g, t, s, I, N, p]), V = (z, H) => {
    if (!H.nodeId || H.handleType === "target") {
      D.current = null;
      return;
    }
    D.current = {
      nodeId: H.nodeId,
      handleId: H.handleId ?? null
    };
  }, ee = (z, H) => {
    const K = z0(D.current, H);
    if (D.current = null, !K || !a || H.toNode || H.toHandle || L0(z)) return;
    const X = rd(z);
    j({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: X.x,
      clientY: X.y
    });
  }, ge = (z, H) => {
    if (!a || !ae(H)) return;
    const K = Xx(z, {
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
  }, Ie = re((z) => {
    if (s) return;
    const H = w.filter((K) => K.id !== z);
    x(H), I(g, H);
  }, [I, w, s, g]), Te = re((z, H, K) => {
    a && j({ kind: "spliceEdge", edgeId: z, clientX: H, clientY: K });
  }, [a]), Qe = (z) => {
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
      const se = g.find((ke) => ke.id === H.sourceNodeId), he = se ? lc(se) : K, ye = C(z, he), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], He = [...w, Ao(H.sourceNodeId, ye.node.id, H.sourceHandleId ?? "Done")];
      m(Ee), x(He), f(ye.node.id), I(Ee, He, [ye.activityNode]);
      return;
    }
    const X = w.find((se) => se.id === H.edgeId);
    X && M(z, X, K);
  }, Oe = de(() => ({
    highlightedEdgeId: b,
    deleteEdge: Ie,
    requestInsertActivity: Te
  }), [Ie, b, Te]);
  return {
    nodes: g,
    edges: w,
    canvasRef: E,
    setReactFlowInstance: y,
    connectMenu: v,
    setConnectMenu: j,
    edgeActions: Oe,
    onNodesChange: P,
    onEdgesChange: q,
    onNodesDelete: we,
    onEdgesDelete: Ae,
    isValidConnection: ae,
    onConnect: ce,
    onConnectStart: V,
    onConnectEnd: ee,
    onReconnect: ge,
    commitLayout: J,
    canAutoLayout: oe,
    autoLayout: ue,
    onCanvasDragOver: Y,
    onCanvasDragLeave: te,
    onCanvasDrop: le,
    openEmptyConnectMenu: Z,
    onConnectMenuPick: Qe,
    onPaletteClick: U,
    onPaletteDragStart: B,
    onPaletteDragEnd: W,
    onPalettePointerDown: O
  };
}
const bb = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function fd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ys(e) {
  return fd(e.name);
}
function Nb(e, t) {
  const n = ys(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : hd(o, t);
}
function pd(e, t) {
  return hd(e[ys(t)], t);
}
function jb(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function Sb(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function hc(e, t, n) {
  return {
    ...e,
    [ys(t)]: n
  };
}
function Cb(e, t) {
  return t.isWrapped === !1 ? Nb(e, t) : pd(e, t).expression.value;
}
function hd(e, t) {
  return $b(e) ? {
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
const Eb = /* @__PURE__ */ new Set([
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
function Ib(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const i = t.slice(0, o), s = (i.split(".").pop() ?? i).toLowerCase();
  return Eb.has(s) ? { elementTypeName: kb(t.slice(o)) } : null;
}
function kb(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function Ab(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function _b(e) {
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
function Db(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Tb(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function dr(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [i] = o.splice(t, 1);
  return o.splice(n, 0, i), o;
}
function $b(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function gd(e) {
  return ms(e?.trim() ?? "") || e;
}
function ms(e) {
  if (!e) return "";
  const t = Mb(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${ms(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const i = gc(t.slice(0, o)), s = Pb(t.slice(o));
    return s.length > 0 ? `${i}<${s.join(", ")}>` : i;
  }
  return gc(t);
}
function Mb(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const o = e[n];
    if (o === "[") t++;
    else if (o === "]") t--;
    else if (o === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function gc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Pb(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = yc(e, t);
  return n == null ? [] : Rb(n).map((o) => {
    const i = o.trim(), s = i.startsWith("[") ? yc(i, 0) ?? i : i;
    return ms(s);
  }).filter(Boolean);
}
function yc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function Rb(e) {
  const t = [];
  let n = 0, o = 0;
  for (let i = 0; i < e.length; i++) {
    const s = e[i];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(o, i)), o = i + 1);
  }
  return t.push(e.slice(o)), t.map((i) => i.trim()).filter(Boolean);
}
const mc = "elsa-studio:apply-workflow-graph-operation-batch", xc = "elsa-studio:undo-workflow-graph-operation-batch", Lb = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function zb(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = Xb(e), i = md(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = Kb(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = $e(d.activityId) ?? u.temporaryReferences?.[0], p = Fb(f ?? $e(d.displayName) ?? $e(d.activityType) ?? "weaver-activity", i), h = Vb(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), o.state.rootActivity && Ob(o.state.rootActivity, h);
      const g = dt(d.position) ? Dr(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = wc(o.layout, p, g));
      continue;
    }
    if (l === "set-root") {
      const f = fr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Et(d.activityId, s);
      if (!f || !xs(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = wc(o.layout, f, Dr(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = fr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      Bb(f, $e(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = fr(o, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = dt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Et(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = yd(o.state.rootActivity, f), o.layout = o.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      Hb(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      Wb(o, d, s);
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
function Vb(e, t, n) {
  const o = e.parameters ?? {}, i = $e(o.activityVersionId) ?? $e(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === i || a.activityTypeKey === i || a.displayName === $e(o.displayName));
  return s ? yr(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: i,
    inputs: [],
    outputs: [],
    ...$e(o.displayName) ? { displayName: $e(o.displayName) } : {},
    designer: { position: Dr(o.position, { x: 280, y: 160 }) }
  };
}
function Ob(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = ws(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Hb(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const i = Et(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Et(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!i || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = $e(t.connectionId) ?? `flow-${i}-${s}`;
  a.connections = [
    ...c.filter((l) => !dt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: i, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function Wb(e, t, n) {
  const o = e.state.rootActivity, i = o?.structure?.payload.connections;
  if (!Array.isArray(i)) return;
  const s = $e(t.connectionId), a = Et(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Et(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = i.filter((u) => {
    if (!dt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = dt(u.source) ? u.source.nodeId : void 0, d = dt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function Bb(e, t, n) {
  const o = dt(n);
  e[fd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function fr(e, t, n, o) {
  const i = Et(t, n);
  return i ? xs(e.state.rootActivity, i) ?? o.get(i) ?? null : null;
}
function Et(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function xs(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of xd(e)) {
    const o = xs(n, t);
    if (o) return o;
  }
  return null;
}
function yd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = ws(e);
  if (n) {
    const o = n.map((i) => yd(i, t)).filter((i) => !!i);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function md(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of xd(e)) md(n, t);
  return t;
}
function xd(e) {
  return ws(e) ?? [];
}
function ws(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function wc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Dr(e, t) {
  const n = dt(e) ? e : {}, o = Number(n.x), i = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.y
  };
}
function Fb(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, i = 2;
  for (; t.has(o); )
    o = `${n}-${i}`, i += 1;
  return t.add(o), o;
}
function Kb(e) {
  return typeof e == "number" ? Lb[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function $e(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function Xb(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function dt(e) {
  return typeof e == "object" && e !== null;
}
function qb({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: o,
  setStatus: i,
  setError: s
}) {
  const a = ie(/* @__PURE__ */ new Map());
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
        const p = yn(e), h = zb(e, d.batch, n), g = `weaver-batch-${Date.now()}`;
        a.current.set(g, p), o(h.draft, h.finalActivityIds.at(-1) ?? null), i(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: g } });
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
    return window.addEventListener(mc, c), window.addEventListener(xc, u), () => {
      window.removeEventListener(mc, c), window.removeEventListener(xc, u);
    };
  }, [n, t, e, o, i, s]);
}
function Yb({ context: e, draft: t, editDraft: n, setStatus: o, setError: i }) {
  const [s, a] = F(!1), c = ie(""), u = ie(0), l = ie(Promise.resolve()), d = re((p) => {
    c.current = p ? Re(p) : "";
  }, []), f = re(async (p, h) => {
    const g = async () => {
      const w = ++u.current, x = Re(p);
      i("");
      try {
        const N = await cp(e, p), y = Re(N);
        return c.current = y, n(({ draft: v }) => !v || v.id !== N.id ? null : Re(v) === x ? N : { ...v, validationErrors: N.validationErrors }), w === u.current && o(h), N;
      } catch (N) {
        throw w === u.current && (o(""), i(N instanceof Error ? N.message : String(N))), N;
      }
    }, m = l.current.then(g, g);
    return l.current = m.catch(() => {
    }), m;
  }, [e, n, o, i]);
  return G(() => {
    if (!s || !t || Re(t) === c.current) return;
    o("Autosaving...");
    const h = window.setTimeout(() => {
      f(t, "Autosaved").catch(() => {
      });
    }, f0);
    return () => window.clearTimeout(h);
  }, [s, t, f, o]), { saveDraft: f, autosaveEnabled: s, setAutosaveEnabled: a, markSaved: d };
}
function Ub({ context: e, definitionId: t, resetHistory: n, loadDraft: o, markSaved: i, setError: s }) {
  const [a, c] = F(null), [u, l] = F([]), [d, f] = F([]), [p, h] = F(null), [g, m] = F(yo), [w, x] = F("loading"), N = re(async () => {
    s(""), x("loading");
    const [y, v, j, b, S] = await Promise.all([
      Qf(e, t),
      Fr(e),
      yp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      mp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: yo })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      tl(e).then(
        (D) => D,
        () => null
      )
    ]), E = y.draft ?? null;
    c(y), i(E), n(E), o(E), l(v.activities ?? []), f(j.descriptors), h(S), m(b.descriptors.length > 0 ? b.descriptors : yo), x(j.ok ? "ready" : "failed");
  }, [e, t, n, o, i, s]);
  return G(() => {
    N().catch((y) => s(y instanceof Error ? y.message : String(y)));
  }, [N, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: g,
    descriptorStatus: w,
    reload: N
  };
}
function Zb({ context: e, details: t, setDetails: n, setStatus: o }) {
  const i = ie(null), s = ie(null), a = ie({});
  G(() => {
    i.current = t;
  }, [t]);
  const c = re(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = i.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || ap(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => o("Couldn't save name/description."));
  }, [e, n, o]), u = re((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return G(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function Gb({
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
  const g = re(() => {
    if (!t) return;
    const N = n?.definition.name;
    Fv(Hv(t, N), N), d("Exported workflow as JSON.");
  }, [t, n, d]), m = re(async () => {
    if (!(!t || o)) {
      l("saving"), d("Saving...");
      try {
        await i(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, o, i, l, d]), w = re(async () => {
    if (!(!t || o)) {
      l("promoting"), d("Saving...");
      try {
        await i(t, "Saved"), d("Promoting...");
        const N = await lp(e, t.id), y = await up(e, N.versionId);
        u(y.artifactId), d(`Published ${y.artifactVersion}`), await s();
      } catch (N) {
        d(""), f(N instanceof Error ? N.message : String(N));
      } finally {
        l("idle");
      }
    }
  }, [t, o, e, i, s, u, l, d, f]), x = re(async () => {
    if (!t?.state.rootActivity || o) return;
    const N = t, y = Re(N);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = O0(N);
      l("testRunStarting"), d("Starting test run...");
      const j = await dp(e, {
        definitionId: N.definitionId,
        snapshotId: v,
        state: N.state
      });
      a({ draftSignature: y, view: j }), p("runtime"), h(!1), d(ps(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, o, e, c, a, p, h, l, d, f]);
  return { exportJson: g, save: m, promoteAndPublish: w, run: x };
}
function Jb({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: o,
  catalog: i,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(i.map((b) => [b.activityVersionId, b])), [i]), l = re(
    (b) => $p([b.activityVersionId, b.activityTypeKey], a),
    [a]
  ), d = de(() => D0(s), [s]), f = de(() => Bc(c, n), [c, n]), p = Wr(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", g = de(() => h ? null : Nn(c, n), [c, n, h]), m = de(() => h && f?.nodeId === o ? f : g?.slot.activities.find((b) => b.nodeId === o) ?? null, [h, g, f, o]), w = de(
    () => m ? T0(m, u, d) : null,
    [u, d, m]
  ), x = de(
    () => m ? l({ activityVersionId: m.activityVersionId, activityTypeKey: u.get(m.activityVersionId)?.activityTypeKey }) : null,
    [l, u, m]
  ), N = m ? De(m) : [], y = tp(e, t?.state, o), v = p === "flowchart" && g?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: g,
    selectedNode: m,
    selectedDescriptor: w,
    selectedNodeAvailability: x,
    selectedSlots: N,
    scopedVariableAnalysis: y,
    isFlowchartDesigner: v,
    canAddActivitiesToCanvas: !c || !h
  };
}
function Qb({
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
        revision: V0(t),
        selectedNodeId: o,
        selectedActivityType: i?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: sd(t.state.rootActivity, s),
        connections: ad(t.state.rootActivity),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, i, n, o]);
}
function eN({
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
      /* @__PURE__ */ r.jsx(Fo, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ r.jsx(_c, { size: 14 }) : /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), g = h ? `wf-palette-description-${p.activityVersionId}` : void 0, m = Ce(p), w = Yo(p);
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
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": w, "aria-hidden": "true", children: ds(w) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: m }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: g, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Dc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const wd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), tN = "Variable";
function nN({
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
  const d = uN(l), f = i.length > 0 ? i : bb;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        oN,
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
function oN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: i,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: i, readOnly: u }, d = Pr(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? pd(e, t) : null, h = p?.expression.type ?? "Literal", g = Cb(e, t), m = h.toLowerCase(), x = p && (m === "literal" || m === "object") && !Ab(t) ? Ib(t.typeName) : null, N = x ? Pr(n, t, { ...l, scope: "collection" }) : void 0, y = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: i,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = y ? bd(o, y) : null, j = v?.surfaces.inline, b = v && y ? Nd(v, y, g) : [], S = x != null, E = !!(p && !S && dN(t, d?.id)), D = !!(p && !S && fN(t, d?.id)), [L, A] = F(!1), _ = (k) => {
    const T = p ? jb(p, k) : k;
    c(hc(e, t, T));
  }, R = (k) => {
    p && c(hc(e, t, Sb(p, k)));
  }, C = x ? N ? Tr(N.component, t, g, u, { ...l, scope: "collection" }, _) : /* @__PURE__ */ r.jsx(
    rN,
    {
      input: t,
      elementTypeName: x.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: _
    }
  ) : null, I = h === tN && p ? /* @__PURE__ */ r.jsx(
    cN,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: _
    }
  ) : C ?? (j && y ? /* @__PURE__ */ r.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: _
    }
  ) : Tr(f, t, g, u, l, _));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: gd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !E ? /* @__PURE__ */ r.jsx(
      $r,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: i,
        disabled: u,
        onChange: R
      }
    ) : null,
    E ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        I,
        Rr(b)
      ] }),
      /* @__PURE__ */ r.jsx(
        $r,
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
          children: /* @__PURE__ */ r.jsx(Eo, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      I,
      Rr(b)
    ] }),
    D && !E ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ r.jsx(Eo, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    L ? /* @__PURE__ */ r.jsx(
      sN,
      {
        input: t,
        value: g,
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
function iN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function rN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: i,
  disabled: s,
  onChange: a
}) {
  const c = _b(n), u = Tb(e, t), l = { ...i, scope: "element" }, d = Pr(o, u, l)?.component, f = e.displayName || e.name, p = (j, b) => a(c.map((S, E) => E === j ? b : S)), [h, g] = F(null), [m, w] = F(null), x = () => {
    g(null), w(null);
  }, N = (j) => (b) => {
    g(j), b.dataTransfer.effectAllowed = "move", b.dataTransfer.setData("text/plain", String(j));
  }, y = (j) => (b) => {
    h !== null && (b.preventDefault(), b.dataTransfer.dropEffect = "move", m !== j && w(j));
  }, v = (j) => (b) => {
    b.preventDefault(), h !== null && h !== j && a(dr(c, h, j)), x();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((j, b) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: iN(b, h, m),
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
              onDragStart: N(b),
              onDragEnd: x,
              children: /* @__PURE__ */ r.jsx(Dc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ r.jsx("div", { className: "wf-collection-item-editor", children: Tr(d, u, j, s, l, (S) => p(b, S)) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} up`,
                disabled: s || b === 0,
                onClick: () => a(dr(c, b, b - 1)),
                children: /* @__PURE__ */ r.jsx($d, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} down`,
                disabled: s || b === c.length - 1,
                onClick: () => a(dr(c, b, b + 1)),
                children: /* @__PURE__ */ r.jsx(_c, { size: 13 })
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
                children: /* @__PURE__ */ r.jsx(bn, { size: 13 })
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
        onClick: () => a([...c, Db(t)]),
        children: [
          /* @__PURE__ */ r.jsx(Ht, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function sN({
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
  const d = Cc(), f = e.displayName || e.name, p = {
    activity: i,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = bd(s, p), g = h?.surfaces.expanded, m = h ? Nd(h, p, t) : [], w = g ? null : lN(s, p);
  return G(() => {
    const x = (N) => {
      N.key === "Escape" && l();
    };
    return window.addEventListener("keydown", x), () => window.removeEventListener("keydown", x);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(Ac, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          $r,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: gd(e.typeName) })
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
      Rr(m)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Tr(e, t, n, o, i, s) {
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
function $r({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: i = "block",
  onChange: s
}) {
  const [a, c] = F(!1), u = Cc(), l = n.find((f) => f.type === t), d = [
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
const Mr = "::";
function vd(e) {
  return !e || e === _o ? _o : e;
}
function vc(e, t) {
  return `${vd(t)}${Mr}${e}`;
}
function aN(e) {
  const t = e.indexOf(Mr);
  if (t < 0) return null;
  const n = e.slice(t + Mr.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function cN({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: i }) {
  const s = Jc(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? vc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === vd(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const f = aN(d.target.value);
          f && i(Kf(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = vc(d.referenceKey, d.scopeId);
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
function Pr(e, t, n) {
  return [...e].sort((o, i) => (o.order ?? 500) - (i.order ?? 500)).find((o) => o.supports(t, n));
}
function bd(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Nd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function lN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", i = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return i ? `${s} ${i}` : s;
}
function Rr(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function uN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function dN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !wd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function fN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !wd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function pN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: o,
  selectedDescriptor: i,
  selectedNodeAvailability: s,
  propertyEditors: a,
  expressionEditors: c,
  expressionDescriptors: u,
  descriptorStatus: l,
  scopedVariableAnalysis: d,
  onSelectedActivityChange: f,
  onEnterSlot: p
}) {
  if (!t)
    return /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  const h = De(t);
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
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
      /* @__PURE__ */ r.jsx(Co, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Do(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      nN,
      {
        activity: t,
        descriptor: i,
        editors: a,
        expressionEditors: c,
        expressionDescriptors: u,
        descriptorStatus: l,
        visibleVariables: d.visibleVariables,
        scopeStatus: d.status,
        onChange: f
      }
    ),
    Bf(t) ? /* @__PURE__ */ r.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ r.jsx(
      c0,
      {
        context: e,
        variables: Gc(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: qf(d.shadowingWarnings, t.nodeId),
        onChange: (g) => f(Ff(t, g))
      }
    ) }) : null,
    h.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      h.map((g) => /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => p(t, g.id, `${n} / ${g.label}`), children: [
        g.label,
        /* @__PURE__ */ r.jsxs("small", { children: [
          g.activities.length,
          " activit",
          g.activities.length === 1 ? "y" : "ies"
        ] })
      ] }, g.id))
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] });
}
function hN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: i,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const c = hb(), { draft: u, frames: l, selectedNodeId: d, testRun: f, publishedArtifactId: p } = c.state, {
    loadDraft: h,
    replaceDraftByBatch: g,
    editDraft: m,
    editDraftAndSelect: w,
    select: x,
    navigateToScope: N,
    resetToRoot: y,
    enterSlot: v,
    startTestRun: j,
    clearTestRun: b,
    setPublishedArtifact: S
  } = c, [E, D] = F(""), [L, A] = F(""), [_, R] = F("idle"), [C, I] = F(() => /* @__PURE__ */ new Set()), [k, T] = F(""), [M, $] = F("activities"), [B, W] = F("inspector"), [O, U] = F("designer"), {
    paletteWidth: Y,
    inspectorWidth: te,
    paletteCollapsed: le,
    inspectorCollapsed: Z,
    maximizedSidePanel: P,
    setInspectorCollapsed: q,
    paletteExpanded: ae,
    inspectorExpanded: ce,
    editorBodyClassName: J,
    editorBodyStyle: oe,
    toggleSidePanelCollapsed: ue,
    toggleSidePanelMaximized: V,
    startSidePanelResize: ee,
    handleSidePanelResizeKeyDown: ge
  } = rb(), { resetHistory: we, undo: Ae, redo: Ie, canUndoNow: Te, canRedoNow: Qe } = db({ draft: u, restoreDraft: h }), { saveDraft: Oe, autosaveEnabled: z, setAutosaveEnabled: H, markSaved: K } = Yb({ context: e, draft: u, editDraft: m, setStatus: A, setError: D }), {
    details: X,
    setDetails: se,
    catalog: he,
    activityDescriptors: ye,
    availabilityDiagnostics: je,
    expressionDescriptors: Ee,
    descriptorStatus: He,
    reload: ke
  } = Ub({ context: e, definitionId: t, resetHistory: we, loadDraft: h, markSaved: K, setError: D }), { updateDefinitionMeta: li } = Zb({ context: e, details: X, setDetails: se, setStatus: A }), {
    catalogByVersion: pt,
    availabilityLookup: ui,
    scopeOwner: Bn,
    isUnsupportedDesigner: It,
    scope: di,
    selectedNode: qe,
    selectedDescriptor: kt,
    selectedNodeAvailability: fi,
    selectedSlots: pi,
    scopedVariableAnalysis: hi,
    isFlowchartDesigner: st,
    canAddActivitiesToCanvas: gi
  } = Jb({ context: e, draft: u, frames: l, selectedNodeId: d, catalog: he, activityDescriptors: ye, availabilityDiagnostics: je }), At = de(() => _r(he), [he]), yi = de(() => {
    const Q = k.trim().toLowerCase();
    if (!Q) return At;
    const fe = he.filter((ve) => Ce(ve).toLowerCase().includes(Q) || ve.activityTypeKey.toLowerCase().includes(Q) || (ve.category ?? "").toLowerCase().includes(Q) || (ve.description ?? "").toLowerCase().includes(Q));
    return _r(fe);
  }, [he, k, At]), _t = _ !== "idle", mi = !!u?.state.rootActivity && !_t, Fn = St(n, "weaver.workflows.find-draft-risks"), Kn = St(n, "weaver.workflows.propose-update"), xi = vb({
    draft: u,
    scope: di,
    scopeOwner: Bn,
    catalog: he,
    catalogByVersion: pt,
    isUnsupportedDesigner: It,
    isFlowchartDesigner: st,
    canAddActivitiesToCanvas: gi,
    selectedNodeId: d,
    editDraft: m,
    editDraftAndSelect: w,
    select: x,
    setStatus: A,
    setError: D
  }), {
    nodes: tn,
    edges: wi,
    canvasRef: vi,
    setReactFlowInstance: bi,
    connectMenu: nn,
    setConnectMenu: Ni,
    edgeActions: Xn,
    onNodesChange: qn,
    onEdgesChange: Yn,
    onNodesDelete: ji,
    onEdgesDelete: Si,
    isValidConnection: Ci,
    onConnect: Ei,
    onConnectStart: Ii,
    onConnectEnd: ki,
    onReconnect: Un,
    commitLayout: Ai,
    canAutoLayout: _i,
    autoLayout: Di,
    onCanvasDragOver: Zn,
    onCanvasDragLeave: Gn,
    onCanvasDrop: Jn,
    openEmptyConnectMenu: Ti,
    onConnectMenuPick: $i,
    onPaletteClick: Qn,
    onPaletteDragStart: Mi,
    onPaletteDragEnd: Pi,
    onPalettePointerDown: Ri
  } = xi;
  qb({ draft: u, details: X, catalog: he, replaceDraftByBatch: g, setStatus: A, setError: D }), Qb({ details: X, draft: u, selectedNode: qe, selectedNodeId: d, selectedDescriptor: kt, catalogByVersion: pt }), G(() => {
    I((Q) => {
      let fe = !1;
      const ve = new Set(Q);
      for (const et of At)
        ve.has(et.category) || (ve.add(et.category), fe = !0);
      return fe ? ve : Q;
    });
  }, [At]);
  const { exportJson: Li, save: zi, promoteAndPublish: Vi, run: Oi } = Gb({
    context: e,
    draft: u,
    details: X,
    busy: _t,
    saveDraft: Oe,
    reload: ke,
    startTestRun: j,
    clearTestRun: b,
    setPublishedArtifact: S,
    setOperation: R,
    setStatus: A,
    setError: D,
    setActiveRightPanelId: W,
    setInspectorCollapsed: q
  }), Hi = re((Q) => {
    m(({ draft: fe }) => fe ? { ...fe, state: Q(fe.state) } : null);
  }, [m]), eo = re((Q) => {
    if (!u) return "No draft is loaded.";
    const fe = Bv(Q, u);
    return fe.ok ? (h(fe.draft), A("Applied workflow JSON."), null) : fe.error;
  }, [u, h]);
  G(() => {
    const Q = (fe) => {
      if (O !== "designer" || !(fe.metaKey || fe.ctrlKey)) return;
      const ve = fe.target;
      if (ve && (ve.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(ve.tagName))) return;
      const et = fe.key.toLowerCase();
      et === "z" && !fe.shiftKey ? (fe.preventDefault(), Ae()) : (et === "z" && fe.shiftKey || et === "y") && (fe.preventDefault(), Ie());
    };
    return window.addEventListener("keydown", Q), () => window.removeEventListener("keydown", Q);
  }, [O, Ae, Ie]);
  const Wi = re((Q) => {
    m(({ draft: fe }) => {
      const ve = fe?.state.rootActivity;
      return !fe || !ve ? null : {
        ...fe,
        state: {
          ...fe.state,
          rootActivity: Xc(ve, Q.nodeId, () => Q)
        }
      };
    });
  }, [m]), to = re((Q) => {
    if (!Q) return;
    const fe = u?.state.rootActivity;
    if (!fe) return;
    const ve = vf(fe, Q, (et) => {
      const Ns = pt.get(et.activityVersionId);
      return Ns ? Ce(Ns) : et.nodeId;
    });
    ve && (U("designer"), N(ve, Q), q(!1));
  }, [u?.state.rootActivity, pt, q, N]), Bi = (Q) => {
    I((fe) => {
      const ve = new Set(fe);
      return ve.has(Q) ? ve.delete(Q) : ve.add(Q), ve;
    });
  };
  if (!X || !u)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: E || "Loading workflow editor..." });
  const Dt = f?.draftSignature === Re(u) ? f.view : null, Tt = Dt && L.startsWith("Test run") ? "" : L, Fi = (Q) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Q)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Ki = {
    definition: X.definition,
    draft: u,
    selectedActivity: qe,
    selectedActivityDescriptor: kt,
    selectedActivitySlots: pi,
    catalog: he,
    currentScopeOwner: Bn,
    frames: l
  }, on = s.map((Q) => {
    const fe = Q.component;
    return {
      id: Q.id,
      title: Q.title,
      side: Q.side,
      order: Q.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(fe, { context: Ki })
    };
  }), Xi = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Ko, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        eN,
        {
          paletteSearch: k,
          onSearchChange: T,
          groups: yi,
          expandedCategories: C,
          onToggleCategory: Bi,
          onActivityClick: Qn,
          onActivityDragStart: Mi,
          onActivityDragEnd: Pi,
          onActivityPointerDown: Ri
        }
      )
    },
    ...on.filter((Q) => Q.side === "left")
  ].sort(uc), qi = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(Vr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        pN,
        {
          context: e,
          selectedNode: qe,
          selectedNodeLabel: qe ? tn.find((Q) => Q.id === qe.nodeId)?.data.label ?? qe.nodeId : "",
          selectedActivityType: qe ? kt?.typeName ?? pt.get(qe.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: kt,
          selectedNodeAvailability: fi,
          propertyEditors: o,
          expressionEditors: i,
          expressionDescriptors: Ee,
          descriptorStatus: He,
          scopedVariableAnalysis: hi,
          onSelectedActivityChange: Wi,
          onEnterSlot: v
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Ot, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(tb, { testRun: Dt, onOpenRun: Fi })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Tc, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        ib,
        {
          context: e,
          ai: n,
          definitionId: X.definition.id,
          publishedArtifactId: p
        }
      )
    },
    ...on.filter((Q) => Q.side === "right")
  ].sort(uc), vs = Xi.find((Q) => Q.id === M) ?? Xi[0], bs = qi.find((Q) => Q.id === B) ?? qi[0], jd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx($c, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(zd, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(zr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: X.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      Tt ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(Gt, { size: 13 }),
        " ",
        Tt
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
              disabled: !Te,
              onClick: Ae,
              children: /* @__PURE__ */ r.jsx(Md, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Qe,
              onClick: Ie,
              children: /* @__PURE__ */ r.jsx(Pd, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !_i,
              onClick: Di,
              children: /* @__PURE__ */ r.jsx(Rd, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: z, onChange: (Q) => H(Q.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        Fn ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(n, Fn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ r.jsx(ot, { size: 15 }),
          " Risks"
        ] }) : null,
        Kn ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(n, Kn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ r.jsx(ot, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Li, children: [
          /* @__PURE__ */ r.jsx(Ld, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: _t, onClick: () => {
          zi();
        }, children: [
          /* @__PURE__ */ r.jsx(Ec, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: _t, onClick: () => {
          Vi();
        }, children: [
          /* @__PURE__ */ r.jsx(kc, { size: 15 }),
          " Promote"
        ] }),
        Dt ? /* @__PURE__ */ r.jsx(
          eb,
          {
            testRun: Dt,
            onOpenDetails: () => {
              W("runtime"), q(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !mi,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Oi();
            },
            children: [
              /* @__PURE__ */ r.jsx(Ot, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    E ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(ft, { size: 16 }),
      " ",
      E
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: J, style: oe, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            So,
            {
              label: "Activities panel tabs",
              tabs: Xi,
              activeTabId: vs.id,
              onSelect: $
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": le ? "Expand activities panel" : "Collapse activities panel",
                title: le ? "Expand" : "Collapse",
                onClick: () => ue("palette"),
                children: le ? /* @__PURE__ */ r.jsx(Mt, { size: 14 }) : /* @__PURE__ */ r.jsx(Io, { size: 14 })
              }
            ),
            le ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: P === "palette" ? "Restore" : "Maximize",
                onClick: () => V("palette"),
                children: P === "palette" ? /* @__PURE__ */ r.jsx(Cs, { size: 14 }) : /* @__PURE__ */ r.jsx(Eo, { size: 14 })
              }
            )
          ] })
        ] }),
        ae ? vs.render() : null
      ] }),
      ae && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": dn,
          "aria-valuemax": fn,
          "aria-valuenow": Y,
          tabIndex: 0,
          onPointerDown: (Q) => ee("palette", Q),
          onKeyDown: (Q) => ge("palette", Q)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          So,
          {
            label: "Editor view tabs",
            tabs: jd,
            activeTabId: O,
            onSelect: (Q) => U(Q)
          }
        ) }),
        O === "code" ? /* @__PURE__ */ r.jsx(Uv, { draft: u, onApply: eo }) : O === "properties" ? /* @__PURE__ */ r.jsx(u0, { details: X, draft: u, context: e, onStateChange: Hi, onDefinitionMetaChange: li }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => y(), children: "Root" }),
            l.map((Q, fe) => /* @__PURE__ */ r.jsxs(Ze.Fragment, { children: [
              /* @__PURE__ */ r.jsx(Mt, { size: 13 }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => N(l.slice(0, fe + 1), null), children: Q.label })
            ] }, `${Q.ownerNodeId}-${Q.slotId}-${fe}`))
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: vi, onDragOver: Zn, onDragLeave: Gn, onDrop: Jn, children: [
            /* @__PURE__ */ r.jsx(Ju.Provider, { value: Xn, children: /* @__PURE__ */ r.jsx(Qu.Provider, { value: ui, children: /* @__PURE__ */ r.jsxs(
              Wu,
              {
                nodes: tn,
                edges: wi,
                nodeTypes: ld,
                edgeTypes: ud,
                onInit: bi,
                onNodesChange: qn,
                onEdgesChange: Yn,
                onNodesDelete: ji,
                onEdgesDelete: Si,
                onConnect: Ei,
                onConnectStart: st ? Ii : void 0,
                onConnectEnd: st ? ki : void 0,
                onReconnect: st ? Un : void 0,
                isValidConnection: Ci,
                onDragOver: Zn,
                onDragLeave: Gn,
                onDrop: Jn,
                onPaneClick: () => x(null),
                onNodeClick: (Q, fe) => x(fe.id),
                onNodeDragStop: It ? void 0 : Ai,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: st,
                nodesDraggable: !It,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: It ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(Fu, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(Xu, {}),
                  /* @__PURE__ */ r.jsx(Yu, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            st && tn.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Ti(), children: [
              /* @__PURE__ */ r.jsx(Ht, { size: 15 }),
              " Add activity"
            ] }) : null,
            nn ? /* @__PURE__ */ r.jsx(
              q0,
              {
                clientX: nn.clientX,
                clientY: nn.clientY,
                activities: he,
                onPick: $i,
                onClose: () => Ni(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(Q0, { draft: u, onRepair: to })
        ] })
      ] }),
      ce && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": pn,
          "aria-valuemax": hn,
          "aria-valuenow": te,
          tabIndex: 0,
          onPointerDown: (Q) => ee("inspector", Q),
          onKeyDown: (Q) => ge("inspector", Q)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            So,
            {
              label: "Inspector panel tabs",
              tabs: qi,
              activeTabId: bs.id,
              onSelect: W
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
                onClick: () => ue("inspector"),
                children: Z ? /* @__PURE__ */ r.jsx(Io, { size: 14 }) : /* @__PURE__ */ r.jsx(Mt, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: P === "inspector" ? "Restore" : "Maximize",
                onClick: () => V("inspector"),
                children: P === "inspector" ? /* @__PURE__ */ r.jsx(Cs, { size: 14 }) : /* @__PURE__ */ r.jsx(Eo, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? bs.render() : null
      ] })
    ] })
  ] });
}
function gN({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: i }) {
  const s = ed(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => i(Number(u.target.value)), children: h0.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(Io, { size: 14 }),
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
        /* @__PURE__ */ r.jsx(Mt, { size: 14 })
      ] })
    ] })
  ] });
}
const yN = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function mN({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: i, onClose: s, onSubmit: a }) {
  const [c, u] = F(!1), [l, d] = F(""), [f, p] = F(!1), [h, g] = F(null), [m, w] = F(null), x = ie(null), N = ie(e);
  N.current = e;
  const y = ie(i);
  y.current = i;
  const v = re((b) => {
    const S = { ...N.current };
    b.name && (S.name = b.name), b.description && (S.description = b.description), y.current(S), g(null), w(null);
  }, []);
  G(() => {
    if (o)
      return n.onPromptResult((b) => {
        if (b.requestId !== x.current) return;
        if (x.current = null, p(!1), b.status !== "completed") {
          w(b.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = w0(b.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        b.autoApply ? v(S) : g(S);
      });
  }, [n, o, v]);
  const j = () => {
    if (!o) return;
    const b = o.createPrompt({ draft: N.current, intent: l });
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
          o ? /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((b) => !b),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ r.jsx(ot, { size: 13 }),
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
                onChange: (b) => d(b.target.value),
                onKeyDown: (b) => {
                  (b.metaKey || b.ctrlKey) && b.key === "Enter" && (b.preventDefault(), j());
                }
              }
            )
          ] }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: f, children: [
            /* @__PURE__ */ r.jsx(ot, { size: 13 }),
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
              onChange: (b) => i({ ...e, name: b.target.value })
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
              onChange: (b) => i({ ...e, description: b.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ r.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: yN.map((b) => {
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
                  onChange: () => i({ ...e, rootKind: b.value, rootActivityVersionId: null })
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
function xN({ context: e, ai: t, onOpen: n }) {
  const [o, i] = F(""), [s, a] = F("active"), [c, u] = F(1), [l, d] = F(g0), [f, p] = F("loading"), [h, g] = F(""), [m, w] = F(""), [x, N] = F([]), [y, v] = F(0), [j, b] = F(() => /* @__PURE__ */ new Set()), [S, E] = F(null), [D, L] = F(!1), [A, _] = F([]), [R, C] = F("idle"), I = ie(null), k = de(() => x.map((V) => V.id), [x]), T = St(t, "weaver.workflows.suggest-create-metadata"), M = St(t, "weaver.workflows.explain-definition"), $ = k.filter((V) => j.has(V)).length, B = k.length > 0 && $ === k.length, W = re(async () => {
    p("loading"), g("");
    try {
      const V = await Jf(e, { search: o, state: s, page: c, pageSize: l }), ee = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, we = ed(ge, l);
      if (ge > 0 && c > we) {
        u(we);
        return;
      }
      N(ee ? V.definitions : x0(V.definitions, c, l)), v(ge), p("ready");
    } catch (V) {
      g(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, o, s, c, l]);
  G(() => {
    W();
  }, [W]), G(() => {
    I.current && (I.current.indeterminate = $ > 0 && !B);
  }, [B, $]);
  const O = re(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await Fr(e);
        _(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), g(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), U = () => {
    g(""), w(""), E({ name: "", description: "", rootKind: "flowchart" }), O();
  }, Y = async () => {
    if (S?.name.trim()) {
      L(!0), g(""), w("");
      try {
        const V = await op(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: v0(S, A)
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
  }, Z = () => b(/* @__PURE__ */ new Set()), P = (V, ee) => {
    b((ge) => {
      const we = new Set(ge);
      return ee ? we.add(V) : we.delete(V), we;
    });
  }, q = (V) => {
    b((ee) => {
      const ge = new Set(ee);
      for (const we of k)
        V ? ge.add(we) : ge.delete(we);
      return ge;
    });
  }, ae = (V) => {
    a(V), u(1), Z();
  }, ce = (V) => {
    i(V), u(1), Z();
  }, J = async (V) => {
    if (await ks().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await ip(e, V.id), P(V.id, !1), w(`Deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, oe = async (V) => {
    w(""), g("");
    try {
      await rp(e, V.id), P(V.id, !1), w(`Restored ${V.name}`), await le();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, ue = async (V) => {
    if (await ks().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await sp(e, V.id), P(V.id, !1), w(`Permanently deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
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
        /* @__PURE__ */ r.jsx(Fo, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: o, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ r.jsx(Ht, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(Wn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(ft, { size: 16 }),
      " ",
      h
    ] }) : null,
    m ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(Gt, { size: 14 }),
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
    f === "loading" ? /* @__PURE__ */ r.jsx(hs, {}) : null,
    f === "ready" && x.length === 0 ? /* @__PURE__ */ r.jsx(
      gs,
      {
        icon: /* @__PURE__ */ r.jsx(Tc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: U, children: [
          /* @__PURE__ */ r.jsx(Ht, { size: 15 }),
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
              ref: I,
              type: "checkbox",
              checked: B,
              onChange: (V) => q(V.target.checked),
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
            "aria-selected": j.has(V.id),
            tabIndex: 0,
            onClick: () => n(V.id),
            onKeyDown: (ee) => {
              ee.currentTarget === ee.target && (ee.key !== "Enter" && ee.key !== " " || (ee.preventDefault(), n(V.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(V.id),
                  onChange: (ee) => P(V.id, ee.target.checked),
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
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), te(V.id);
                }, children: "Artifacts" }),
                M ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(t, M, V), children: [
                  /* @__PURE__ */ r.jsx(ot, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(bn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  oe(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(Or, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ue(V);
                }, children: [
                  /* @__PURE__ */ r.jsx(bn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        gN,
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
      mN,
      {
        draft: S,
        creating: D,
        ai: t,
        suggestMetadataAction: T,
        onChange: (V) => E(V),
        onClose: () => E(null),
        onSubmit: Y
      }
    ) : null
  ] });
}
function wN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const i = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => bN(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = i.get(a.activityType), u = Yo(c), l = c ? Ce(c) : Zt(a.activityType) ?? a.activityType, d = Zt(a.activityType) ?? a.activityType, f = NN(a.startedAt ?? a.scheduledAt), p = us(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: ds(u) }),
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
          /* @__PURE__ */ r.jsx(vN, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function vN({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function bN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => bc(t.activity) - bc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function bc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function NN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function jN({ context: e }) {
  const [t, n] = F("loading"), [o, i] = F(""), [s, a] = F(""), [c, u] = F(""), [l, d] = F([]), f = re(async () => {
    n("loading"), i("");
    try {
      const h = await fp(e, {
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
    t === "failed" ? /* @__PURE__ */ r.jsx(Wn, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(hs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      gs,
      {
        icon: /* @__PURE__ */ r.jsx(Ko, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: id(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(en, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: us(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function SN({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, i] = F("loading"), [s, a] = F(""), [c, u] = F(null), [l, d] = F(null), f = St(t, "weaver.workflows.explain-instance"), p = re(async () => {
    if (!n) {
      a("No workflow execution id was provided."), i("failed");
      return;
    }
    i("loading"), a("");
    try {
      const m = await pp(e, n), [w, x] = await Promise.all([
        np(e, m.instance.definitionVersionId).then(
          (N) => ({ definitionVersion: N, error: "" }),
          (N) => ({ definitionVersion: null, error: N instanceof Error ? N.message : String(N) })
        ),
        Fr(e)
      ]);
      u({
        details: m,
        definitionVersion: w.definitionVersion,
        definitionVersionError: w.error,
        activityCatalog: x.activities
      }), d(null), i("ready");
    } catch (m) {
      u(null), a(W0(m, n)), i("failed");
    }
  }, [e, n]);
  G(() => {
    p();
  }, [p]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, g = () => {
    const m = c?.details.instance.definitionId;
    m && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(m)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ r.jsx(Io, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: g, children: [
        /* @__PURE__ */ r.jsx($c, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        p();
      }, children: [
        /* @__PURE__ */ r.jsx(Or, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(t, f, c.details), children: [
        /* @__PURE__ */ r.jsx(ot, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ r.jsx(Wn, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ r.jsx(
        CN,
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
        EN,
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
          graphNodeIds: c.definitionVersion ? P0(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function CN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: i, onSelectEvidence: s }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Wr(c, u), d = l === "unsupported" ? null : Nn(c, []), f = l === "unsupported" ? hr(c, n, e.layout) : d ? Fc(d, n, e.layout) : hr(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: bf(p, o.activities, o.incidents, i),
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
      /* @__PURE__ */ r.jsx(en, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: H0(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        Wu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: ld,
          edgeTypes: ud,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(Fu, {}),
            /* @__PURE__ */ r.jsx(Yu, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(Xu, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function EN({ ai: e, action: t, summary: n, details: o, state: i, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = F("timeline");
  if (!n)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const p = o?.incidents.length ?? 0, h = IN(o?.activities ?? [], a), g = (w) => {
    c?.(w), f("activity");
  }, m = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(Vr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Ic, { size: 14 }), render: () => null },
    { id: "issues", title: p > 0 ? `Issues (${p})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(ft, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(zr, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ r.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(e, t, o ?? n), children: [
        /* @__PURE__ */ r.jsx(ot, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ r.jsx(So, { label: "Workflow run tabs", tabs: m, activeTabId: d, onSelect: (w) => f(w) }) }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(Wn, { message: s }) : null,
    i === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ r.jsx(
      wN,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: g
      }
    ) : d === "activity" ? /* @__PURE__ */ r.jsx(kN, { activity: h, activityCatalog: l }) : d === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsx(AN, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ r.jsx(_N, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(en, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ r.jsx("dd", { children: id(n.runKind) }),
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
  ] });
}
function IN(e, t) {
  if (!t) return null;
  const n = e.find((i) => i.activityExecutionId === t);
  if (n) return n;
  const o = e.filter((i) => i.executableNodeId === t || i.authoredActivityId === t);
  return o.length > 0 ? Zc(o) : null;
}
function kN({ activity: e, activityCatalog: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsx("p", { children: "No activity selected." })
    ] });
  const o = t.find((i) => i.activityTypeKey === e.activityType)?.displayName || Zt(e.activityType) || e.activityType;
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
      /* @__PURE__ */ r.jsx("dd", { children: o }),
      /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(en, { status: e.status, subStatus: e.subStatus }) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.activityExecutionId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.authoredActivityId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        Zt(e.activityType) ?? e.activityType,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: e.activityTypeVersion })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ r.jsx("dd", { children: Le(e.startedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ r.jsx("dd", { children: Le(e.completedAt) }),
      /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
      /* @__PURE__ */ r.jsx("dd", { children: us(e.startedAt, e.completedAt) || "Unknown" }),
      /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.bookmarkIds.length }),
      /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.incidentIds.length })
    ] })
  ] });
}
function AN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function _N({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(ac(s))), i = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? ac(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: Zt(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ r.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      i.map((s) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ r.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function DN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: i
}) {
  const [s, a] = F(Nc);
  G(() => {
    const u = () => a(Nc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ r.jsx(hN, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: i, onBack: () => c(null) }) : /* @__PURE__ */ r.jsx(ci, { title: "Definitions", children: /* @__PURE__ */ r.jsx(xN, { context: e, ai: t, onOpen: c }) });
}
function TN({ context: e, ai: t }) {
  const [n, o] = F(jc);
  G(() => {
    const s = () => o(jc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const i = re((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(ci, { title: "Executables", children: /* @__PURE__ */ r.jsx(nb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) });
}
function $N({ context: e }) {
  return /* @__PURE__ */ r.jsx(ci, { title: "Runs", children: /* @__PURE__ */ r.jsx(jN, { context: e }) });
}
function MN({ context: e, ai: t }) {
  const n = PN();
  return /* @__PURE__ */ r.jsx(ci, { title: "Run", children: /* @__PURE__ */ r.jsx(SN, { context: e, ai: t, workflowExecutionId: n }) });
}
function ci({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Nc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function jc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function PN() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function ON(e) {
  Kd(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(DN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(TN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx($N, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(MN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(Mp, { context: e.backend })
      }
    ]
  });
}
export {
  L0 as isConnectEndOverExistingWorkflowNode,
  ON as register,
  z0 as resolveConnectEndSource
};
