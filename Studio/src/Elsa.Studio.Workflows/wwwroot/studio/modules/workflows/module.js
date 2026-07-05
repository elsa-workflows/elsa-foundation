import Ze, { useMemo as de, useState as F, useEffect as G, memo as be, forwardRef as jc, useRef as re, useCallback as ie, useContext as Dn, createContext as Li, useLayoutEffect as bd, lazy as Nd, Suspense as jd, useReducer as Sd, useId as Sc } from "react";
import { ListChecks as Cd, Save as Cc, EyeOff as Ns, Shield as js, AlertTriangle as So, SlidersHorizontal as zi, Activity as Ed, Search as Fo, Check as Zt, Boxes as Ko, Zap as Id, Play as Ot, Terminal as kd, ListTree as Vi, GitBranch as Ec, Plus as Ht, Trash2 as wn, AlertCircle as ft, Wrench as Ad, Copy as _d, X as Ic, Sparkles as ot, RotateCcw as Oi, ChevronDown as kc, ChevronRight as Mt, GripVertical as Ac, Maximize2 as Co, ChevronUp as Dd, Package as _c, Undo2 as Td, Redo2 as $d, Network as Md, Download as Pd, ChevronLeft as Eo, Minimize2 as Ss, Workflow as Rd, Code2 as Ld } from "lucide-react";
import { useQuery as Dc, useQueryClient as zd, useMutation as Vd } from "@tanstack/react-query";
import { useTablistKeyboard as Od } from "@elsa-workflows/studio-ui";
function Hd(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Yr = { exports: {} }, nn = {};
var Cs;
function Wd() {
  if (Cs) return nn;
  Cs = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(o, r, i) {
    var a = null;
    if (i !== void 0 && (a = "" + i), r.key !== void 0 && (a = "" + r.key), "key" in r) {
      i = {};
      for (var c in r)
        c !== "key" && (i[c] = r[c]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: o,
      key: a,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return nn.Fragment = t, nn.jsx = n, nn.jsxs = n, nn;
}
var Es;
function Bd() {
  return Es || (Es = 1, Yr.exports = Wd()), Yr.exports;
}
var s = Bd();
let Tc;
function Fd(e) {
  Tc = e;
}
function Is() {
  return Tc;
}
const Kd = "String", Xd = "singleline";
function qd(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Hi(e, t = "Single") {
  return { alias: (e ?? "").trim() || Kd, collectionKind: t };
}
function $c(e) {
  const t = e.type ?? e.Type;
  if (Io(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: qd(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Io(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: ks(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: ks(e) ? "Array" : "Single" };
}
function ks(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function As(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Xo() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function Yd(e, t) {
  const n = new Set(t);
  let o = 1, r = `${e}${o}`;
  for (; n.has(r); )
    o += 1, r = `${e}${o}`;
  return r;
}
function Ud(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hi(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function Zd(e, t) {
  return { ...e, ...t };
}
function Gd(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Jd(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Qd(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hi(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Xd,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function ef(e, t) {
  return { ...e, ...t };
}
function tf(e) {
  return {
    referenceKey: Xo(),
    name: e.name,
    type: Hi(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function nf(e, t) {
  return { ...e, ...t };
}
function of(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Mc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : of(e || t);
}
function rf(e, t) {
  return Mc(e, t).replace(/StorageDriver$/, "");
}
function Io(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function gn(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const sf = ["name", "Name"], Pc = ["name", "Name"], af = ["storageDriverType", "StorageDriverType"], Rc = ["referenceKey", "ReferenceKey"], cf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Tn(e) {
  return Lc(e, ff);
}
function qo(e) {
  return Lc(e, pf);
}
function Lc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = zc(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = po(e.variables, pi)), Array.isArray(e.inputs) && (n.inputs = po(e.inputs, pi)), Array.isArray(e.outputs) && (n.outputs = po(e.outputs, (o) => Vc(o, !1))), n;
}
function zc(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !Ge(o.payload)) return n;
  let r = !1;
  const i = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(xf) && (i[a] = c.map((u) => zc(u, t)), r = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (i.variables = po(o.payload.variables, pi), r = !0), r ? { ...n, structure: { ...o, payload: i } } : n;
}
function po(e, t) {
  return e.map((n) => Ge(n) && !Array.isArray(n) ? t(n) : n);
}
function pi(e) {
  return Vc(e, !0);
}
const lf = [
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
function Vc(e, t) {
  const n = uf(e, lf);
  return gn(e, Rc).trim() || (n.referenceKey = Xo()), n.type = $c(e), t && (n.storageDriverType = df(e.storageDriverType ?? e.StorageDriverType)), n;
}
function uf(e, t) {
  const n = new Set(t), o = {};
  for (const [r, i] of Object.entries(e))
    n.has(r) || (o[r] = i);
  return o;
}
function df(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (Ge(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function ff(e) {
  const t = [], n = {};
  for (const [r, i] of Object.entries(e))
    cf.has(r) || (mf(i) ? t.push({
      referenceKey: hf(r),
      value: yf(i.expression)
    }) : n[r] = i);
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
function pf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!Ge(o) || typeof o.referenceKey != "string") continue;
    const r = Ge(o.value) ? o.value : {};
    n[gf(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function hf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function gf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function yf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && Ge(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && Ge(e.value) ? { value: _s(e.value), expressionType: "Object" } : { value: _s(e.value), expressionType: t };
}
function _s(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function mf(e) {
  if (!Ge(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return Ge(t) && typeof t.type == "string";
}
function xf(e) {
  return Ge(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function Ge(e) {
  return typeof e == "object" && e !== null;
}
const $n = "elsa.sequence.structure", Gt = "elsa.flowchart.structure";
function Oc(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = De(n).find((a) => a.id === o.slotId);
    if (!r) return null;
    const i = r.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!i) return null;
    n = i;
  }
  return n;
}
function wf(e, t, n = (o) => o.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (r, i) => {
    const a = De(r);
    if (a[0]?.activities.some((c) => c.nodeId === t)) return i;
    for (const c of a)
      for (const u of c.activities) {
        const l = o(u, [...i, { ownerNodeId: u.nodeId, slotId: c.id, label: n(u) }]);
        if (l) return l;
      }
    return null;
  };
  return o(e, []);
}
function vn(e, t) {
  const n = Oc(e, t);
  if (!n) return null;
  const o = De(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function De(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = zf(t), r = Ur(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: Vf(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, i]) => Ur(i)).map(([i, a]) => ({
    id: `${t.kind}:${i}`,
    label: Hf(i),
    property: i,
    mode: "generic",
    activities: Ur(a) ?? []
  }));
}
function Hc(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), i = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? Of(e.slot.mode, c);
    return Fc(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: i,
    edges: e.slot.mode === "flowchart" ? Kc(e.owner) : _f(e.slot, i)
  };
}
function hi(e, t, n) {
  const o = t.find((i) => i.activityVersionId === e.activityVersionId), r = n.find((i) => i.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Fc(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function vf(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), i = Ts(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ts(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = i.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Pf(u), f = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), p = {
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
function Wi(e, t) {
  return e?.structure?.kind === Gt || Cf(t) ? "flowchart" : e?.structure?.kind === $n || Ef(t) ? "sequence" : "unsupported";
}
function gi(e, t, n) {
  if (t.length === 0) {
    const c = De(e)[0];
    return c ? bn(e, c, n) : e;
  }
  const [o, ...r] = t, i = De(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const a = i.activities.map((c) => c.nodeId === o.ownerNodeId ? gi(c, r, n) : c);
  return bn(e, i, a);
}
function Wc(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, i = De(e).find((c) => c.id === o.slotId);
  if (!i) return e;
  const a = i.activities.map((c) => c.nodeId === o.ownerNodeId ? Wc(c, r, n) : c);
  return bn(e, i, a);
}
function Bc(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = De(e);
  if (o.length === 0) return e;
  let r = !1, i = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = Bc(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (i = bn(i, a, c));
  }
  return r ? i : e;
}
function bn(e, t, n) {
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
function bf(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const i = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && i.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), bn(e.owner, e.slot, i);
}
function Nf(e, t) {
  return {
    ...e,
    structure: Af(e.structure, t)
  };
}
function jf(e, t) {
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
function yi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: kf(e)
  };
}
function Ce(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? If(t) : n;
}
function Fc(e, t, n, o = {}) {
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
      acceptsInbound: Df(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : Xc(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function Yo(e) {
  if (!e) return "activity";
  const t = Sf(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ce(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", i = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : i === "trigger" ? "trigger" : "activity";
}
function Sf(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Cf(e) {
  return !!e && (Ce(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Ef(e) {
  return !!e && (Ce(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function If(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function kf(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: $n,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: Gt,
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
function Af(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Bi(r)) continue;
    const i = r.id;
    typeof i == "string" && o.set(i, r);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((r) => {
        const i = o.get(r.id) ?? {}, a = r.data?.vertices, { vertices: c, ...u } = i;
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
function _f(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Kc(e) {
  if (e.structure?.kind !== Gt) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, i = n.target;
    if (!r?.nodeId || !i?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(Rf) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${o}-${r.nodeId}-${i.nodeId}`,
      source: r.nodeId,
      target: i.nodeId,
      sourceHandle: r.port,
      targetHandle: i.port && i.port !== "Done" ? i.port : void 0,
      type: "workflow",
      label: r.port && r.port !== "Done" ? r.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => n !== null) : [];
}
function Xc(e, t) {
  const n = Ds(e.cases);
  if ($f(e, t) && n.length > 0)
    return [...n.map((i) => ({ name: i, displayName: i })), { name: "Default", displayName: "Default" }];
  const o = [
    ...ho(t?.designFacets),
    ...ho(t?.ports),
    ...ho(t?.outputs)
  ];
  if (o.length > 0) return Mf(o);
  const r = Ds(e.outcomes);
  return r.length > 0 ? r.map((i) => ({ name: i, displayName: i })) : [{ name: "Done", displayName: "Done" }];
}
function Df(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function ko(e, t, n, o) {
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
function Tf(e, t, n) {
  const o = ko(t.source, n, t.sourceHandle ?? "Done", void 0), r = ko(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((i) => i.id !== t.id).concat(o, r);
}
function Ur(e) {
  return Array.isArray(e) ? e.filter(Lf) : null;
}
function $f(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function ho(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Bi(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...ho(n.ports));
      continue;
    }
    const o = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", r = n.isBrowsable !== !1 && n.browsable !== !1, i = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (r && o.toLowerCase() === "flow" && i) {
      const a = typeof n.displayName == "string" ? n.displayName : i;
      t.push({ name: i, displayName: a });
    }
  }
  return t;
}
function Mf(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ds(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ts(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function Pf(e) {
  return [...e].sort((t, n) => $s(n).localeCompare($s(t)))[0];
}
function $s(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function Rf(e) {
  return Bi(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Bi(e) {
  return typeof e == "object" && e !== null;
}
function Lf(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function zf(e) {
  return e.kind === $n ? "sequence" : e.kind === Gt ? "flowchart" : "generic";
}
function Vf(e) {
  return e.kind === $n || e.kind === Gt, "Activities";
}
function Of(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function Hf(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const Ao = "workflow", Wf = /* @__PURE__ */ new Set([$n, Gt]);
function Bf(e) {
  const t = e?.structure?.kind;
  return !!t && Wf.has(t);
}
function qc(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Io) : [];
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
    declaringScopeId: t && t !== Ao ? t : Ao
  };
}
function Yc(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return Yc(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Xf(e) {
  if (!e) return "";
  const t = [`workflow:${Ms(e.variables)}`], n = (o) => {
    const r = De(o), i = r.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${o.nodeId}:${Ms(qc(o))}>${i.join(",")}`), r.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function Ms(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function qf(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const Ne = "/_elsa/workflow-management", Yf = "/publishing", yn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function Uf(e) {
  return Dc({
    queryKey: yn.activityAvailabilitySettings,
    queryFn: () => hp(e)
  });
}
function Zf(e) {
  return Dc({
    queryKey: yn.activityAvailabilityDiagnostics,
    queryFn: () => Gc(e)
  });
}
function Gf(e) {
  const t = zd();
  return Vd({
    mutationFn: (n) => gp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: yn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: yn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: yn.activities });
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
    { state: Tn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const Zr = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function tp(e, t, n) {
  const o = de(() => Xf(t), [t]), [r, i] = F(() => Zr("loading"));
  return G(() => {
    if (!t) {
      i(Zr("unavailable"));
      return;
    }
    let a = !1;
    return i((c) => ({ ...c, status: "loading" })), ep(e, t, n).then(
      (c) => {
        a || i({ ...c, status: "ready" });
      },
      () => {
        a || i(Zr("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, o]), r;
}
async function np(e, t) {
  const n = await e.http.getJson(`${Ne}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: qo(n.state) };
}
async function op(e, t) {
  return e.http.postJson(`${Ne}/definitions`, t);
}
async function rp(e, t) {
  await e.http.deleteJson(`${Ne}/definitions/${encodeURIComponent(t)}`);
}
async function ip(e, t) {
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
    { state: Tn(t.state), layout: t.layout }
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
  const n = { ...t, state: Tn(t.state) };
  try {
    return await e.http.postJson(`${Yf}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = Np(o);
    if (r) return r;
    throw o;
  }
}
async function Uc(e, t) {
  return e.http.postJson(`${Ne}/executables/${encodeURIComponent(t)}/run`, {});
}
async function Zc(e) {
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
async function Fi(e) {
  return e.http.getJson(`${Ne}/activities`);
}
async function hp(e) {
  return e.http.getJson(`${Ne}/activities/availability/settings`);
}
async function gp(e, t) {
  return e.http.putJson(`${Ne}/activities/availability/settings`, t);
}
async function Gc(e) {
  return e.http.getJson(`${Ne}/activities/availability/diagnostics`);
}
async function yp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ps(t) : Ps(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function mp(e) {
  const t = await Uo(e, [
    `${Ne}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : go;
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
    } catch (r) {
      n = r;
    }
  throw n;
}
function Ps(e) {
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
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Rs(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Rs(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Rs(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const go = [
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
function _o(e) {
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
function Ls(e) {
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
  return [...e?.items ?? []].filter(Ap).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Do(t).localeCompare(Do(n)));
}
function Dp(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = yt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function zs(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function Do(e) {
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
  return (t?.items ?? []).find((o) => Ep(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
function Mp({ context: e }) {
  const t = Uf(e), n = Zf(e), o = Gf(e), r = t.data ?? null, i = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = F(() => Ls(r)), [d, f] = F(""), [p, h] = F(null);
  G(() => {
    l(Ls(r));
  }, [r]);
  const g = de(() => _p(i), [i]), x = de(() => Dp(i), [i]), v = i?.sets ?? [], m = de(() => {
    const _ = d.trim().toLowerCase();
    return _ ? g.filter(
      (R) => Do(R).toLowerCase().includes(_) || (R.activityTypeKey ?? "").toLowerCase().includes(_)
    ) : g;
  }, [g, d]), N = new Set(u.activityTypes), y = new Set(u.sets), w = g.filter((_) => yt(_.state) === "BlockedByHostBaseline").length, j = g.filter((_) => yt(_.state) === "HiddenByManagementSettings").length, b = o.error ?? t.error ?? n.error, S = b instanceof Error ? b.message : b ? "Activity availability could not be loaded." : null, E = (_) => l((R) => ({ ...R, mode: _ })), D = (_) => l((R) => ({ ...R, activityTypes: zs(R.activityTypes, _) })), L = (_) => l((R) => ({ ...R, sets: zs(R.sets, _) })), A = () => {
    h(null), o.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: kp(u.mode),
        rules: { activityTypes: u.activityTypes, sets: u.sets }
      },
      { onSuccess: () => h("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ s.jsxs("h2", { children: [
          /* @__PURE__ */ s.jsx(Cd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ s.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ s.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ s.jsx(Cc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ s.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ s.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ s.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ s.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => E("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ s.jsx(Ns, { size: 15 }),
          /* @__PURE__ */ s.jsxs("span", { children: [
            /* @__PURE__ */ s.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ s.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => E("Only"), disabled: a || c, children: [
          /* @__PURE__ */ s.jsx(js, { size: 15 }),
          /* @__PURE__ */ s.jsxs("span", { children: [
            /* @__PURE__ */ s.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ s.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ s.jsxs("span", { children: [
          /* @__PURE__ */ s.jsx(js, { size: 14 }),
          " ",
          w,
          " host blocked"
        ] }),
        /* @__PURE__ */ s.jsxs("span", { children: [
          /* @__PURE__ */ s.jsx(Ns, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ s.jsxs("span", { children: [
          /* @__PURE__ */ s.jsx(So, { size: 14 }),
          " ",
          x.length,
          " unresolved"
        ] })
      ] }),
      v.length > 0 && /* @__PURE__ */ s.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ s.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ s.jsx(zi, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ s.jsx("div", { className: "availability-set-list", children: v.map((_) => /* @__PURE__ */ s.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ s.jsx("input", { type: "checkbox", checked: y.has(_.name), disabled: a || c, onChange: () => L(_.name) }),
          /* @__PURE__ */ s.jsx("span", { children: _.name }),
          /* @__PURE__ */ s.jsx("code", { children: (_.activityTypeKeys ?? []).length })
        ] }, _.name)) })
      ] }),
      /* @__PURE__ */ s.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ s.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ s.jsx(Ed, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ s.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ s.jsx(Fo, { size: 14 }),
            /* @__PURE__ */ s.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (_) => f(_.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((_) => {
            const C = yt(_.state) === "BlockedByHostBaseline", I = _.activityTypeKey ?? _.activityDefinitionId ?? "";
            return /* @__PURE__ */ s.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ s.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(I),
                  disabled: a || c || C,
                  onChange: () => D(I)
                }
              ),
              /* @__PURE__ */ s.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ s.jsx("strong", { children: Do(_) }),
                /* @__PURE__ */ s.jsx("code", { children: _.activityTypeKey })
              ] }),
              /* @__PURE__ */ s.jsx("em", { className: `availability-state ${Cp(_.state)}`, children: _o(_.state) })
            ] }, I);
          })
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ s.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ s.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ s.jsx(So, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ s.jsx("div", { className: "availability-unresolved-list", children: x.map((_) => /* @__PURE__ */ s.jsxs("span", { children: [
          /* @__PURE__ */ s.jsx("strong", { children: _.referenceName }),
          /* @__PURE__ */ s.jsx("em", { children: _o(_.state) })
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
  return new yo(n);
}
function yo(e) {
  this._ = e;
}
function Rp(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
yo.prototype = Zo.prototype = {
  constructor: yo,
  on: function(e, t) {
    var n = this._, o = Rp(e + "", n), r, i = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++i < a; ) if ((r = (e = o[i]).type) && (r = Lp(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++i < a; )
      if (r = (e = o[i]).type) n[r] = Vs(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Vs(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new yo(e);
  },
  call: function(e, t) {
    if ((r = arguments.length - 2) > 0) for (var n = new Array(r), o = 0, r, i; o < r; ++o) n[o] = arguments[o + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (i = this._[e], o = 0, r = i.length; o < r; ++o) i[o].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var o = this._[e], r = 0, i = o.length; r < i; ++r) o[r].value.apply(t, n);
  }
};
function Lp(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Vs(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = Pp, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var mi = "http://www.w3.org/1999/xhtml";
const Os = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: mi,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Go(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Os.hasOwnProperty(t) ? { space: Os[t], local: e } : e;
}
function zp(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === mi && t.documentElement.namespaceURI === mi ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Vp(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function Jc(e) {
  var t = Go(e);
  return (t.local ? Vp : zp)(t);
}
function Op() {
}
function Ki(e) {
  return e == null ? Op : function() {
    return this.querySelector(e);
  };
}
function Hp(e) {
  typeof e != "function" && (e = Ki(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = i[d]) && (l = e.call(u, u.__data__, d, i)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Le(o, this._parents);
}
function Wp(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Bp() {
  return [];
}
function Qc(e) {
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
  typeof e == "function" ? e = Fp(e) : e = Qc(e);
  for (var t = this._groups, n = t.length, o = [], r = [], i = 0; i < n; ++i)
    for (var a = t[i], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Le(o, r);
}
function el(e) {
  return function() {
    return this.matches(e);
  };
}
function tl(e) {
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
  return this.select(e == null ? Yp : qp(typeof e == "function" ? e : tl(e)));
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
  return this.selectAll(e == null ? Gp : Jp(typeof e == "function" ? e : tl(e)));
}
function eh(e) {
  typeof e != "function" && (e = el(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new Le(o, this._parents);
}
function nl(e) {
  return new Array(e.length);
}
function th() {
  return new Le(this._enter || this._groups.map(nl), this._parents);
}
function To(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
To.prototype = {
  constructor: To,
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
function oh(e, t, n, o, r, i) {
  for (var a = 0, c, u = t.length, l = i.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = i[a], o[a] = c) : n[a] = new To(e, i[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function rh(e, t, n, o, r, i, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = i.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, i[c], c, i) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = i[c], l.delete(h)) : n[c] = new To(e, i[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function ih(e) {
  return e.__data__;
}
function sh(e, t) {
  if (!arguments.length) return Array.from(this, ih);
  var n = t ? rh : oh, o = this._parents, r = this._groups;
  typeof e != "function" && (e = nh(e));
  for (var i = r.length, a = new Array(i), c = new Array(i), u = new Array(i), l = 0; l < i; ++l) {
    var d = o[l], f = r[l], p = f.length, h = ah(e.call(d, d && d.__data__, l, o)), g = h.length, x = c[l] = new Array(g), v = a[l] = new Array(g), m = u[l] = new Array(p);
    n(d, f, x, v, m, h, t);
    for (var N = 0, y = 0, w, j; N < g; ++N)
      if (w = x[N]) {
        for (N >= y && (y = N + 1); !(j = v[y]) && ++y < g; ) ;
        w._next = j || null;
      }
  }
  return a = new Le(a, o), a._enter = c, a._exit = u, a;
}
function ah(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function ch() {
  return new Le(this._exit || this._groups.map(nl), this._parents);
}
function lh(e, t, n) {
  var o = this.enter(), r = this, i = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? i.remove() : n(i), o && r ? o.merge(r).order() : r;
}
function uh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, i = o.length, a = Math.min(r, i), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], f = l.length, p = c[u] = new Array(f), h, g = 0; g < f; ++g)
      (h = l[g] || d[g]) && (p[g] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Le(c, this._parents);
}
function dh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, i = o[r], a; --r >= 0; )
      (a = o[r]) && (i && a.compareDocumentPosition(i) ^ 4 && i.parentNode.insertBefore(a, i), i = a);
  return this;
}
function fh(e) {
  e || (e = ph);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), i = 0; i < o; ++i) {
    for (var a = n[i], c = a.length, u = r[i] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Le(r, this._parents).order();
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
    for (var o = e[t], r = 0, i = o.length; r < i; ++r) {
      var a = o[r];
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
    for (var r = t[n], i = 0, a = r.length, c; i < a; ++i)
      (c = r[i]) && e.call(c, c.__data__, i, r);
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
function ol(e) {
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
  return e.style.getPropertyValue(t) || ol(e).getComputedStyle(e, null).getPropertyValue(t);
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
function rl(e) {
  return e.trim().split(/^|\s+/);
}
function Xi(e) {
  return e.classList || new il(e);
}
function il(e) {
  this._node = e, this._names = rl(e.getAttribute("class") || "");
}
il.prototype = {
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
function sl(e, t) {
  for (var n = Xi(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function al(e, t) {
  for (var n = Xi(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function Ph(e) {
  return function() {
    sl(this, e);
  };
}
function Rh(e) {
  return function() {
    al(this, e);
  };
}
function Lh(e, t) {
  return function() {
    (t.apply(this, arguments) ? sl : al)(this, e);
  };
}
function zh(e, t) {
  var n = rl(e + "");
  if (arguments.length < 2) {
    for (var o = Xi(this.node()), r = -1, i = n.length; ++r < i; ) if (!o.contains(n[r])) return !1;
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
  var t = typeof e == "function" ? e : Jc(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Jh() {
  return null;
}
function Qh(e, t) {
  var n = typeof e == "function" ? e : Jc(e), o = t == null ? Jh : typeof t == "function" ? t : Ki(t);
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
function rg(e) {
  return this.select(e ? og : ng);
}
function ig(e) {
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
      for (var n = 0, o = -1, r = t.length, i; n < r; ++n)
        i = t[n], (!e.type || i.type === e.type) && i.name === e.name ? this.removeEventListener(i.type, i.listener, i.options) : t[++o] = i;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function lg(e, t, n) {
  return function() {
    var o = this.__on, r, i = sg(t);
    if (o) {
      for (var a = 0, c = o.length; a < c; ++a)
        if ((r = o[a]).type === e.type && r.name === e.name) {
          this.removeEventListener(r.type, r.listener, r.options), this.addEventListener(r.type, r.listener = i, r.options = n), r.value = t;
          return;
        }
    }
    this.addEventListener(e.type, i, n), r = { type: e.type, name: e.name, value: t, listener: i, options: n }, o ? o.push(r) : this.__on = [r];
  };
}
function ug(e, t, n) {
  var o = ag(e + ""), r, i = o.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (r = 0, d = c[u]; r < i; ++r)
          if ((a = o[r]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? lg : cg, r = 0; r < i; ++r) this.each(c(o[r], t, n));
  return this;
}
function cl(e, t, n) {
  var o = ol(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function dg(e, t) {
  return function() {
    return cl(this, e, t);
  };
}
function fg(e, t) {
  return function() {
    return cl(this, e, t.apply(this, arguments));
  };
}
function pg(e, t) {
  return this.each((typeof t == "function" ? fg : dg)(e, t));
}
function* hg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, i = o.length, a; r < i; ++r)
      (a = o[r]) && (yield a);
}
var ll = [null];
function Le(e, t) {
  this._groups = e, this._parents = t;
}
function Mn() {
  return new Le([[document.documentElement]], ll);
}
function gg() {
  return this;
}
Le.prototype = Mn.prototype = {
  constructor: Le,
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
  clone: rg,
  datum: ig,
  on: ug,
  dispatch: pg,
  [Symbol.iterator]: hg
};
function Pe(e) {
  return typeof e == "string" ? new Le([[document.querySelector(e)]], [document.documentElement]) : new Le([[e]], ll);
}
function yg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function He(e, t) {
  if (e = yg(e), t === void 0 && (t = e.currentTarget), t) {
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
const mg = { passive: !1 }, Nn = { capture: !0, passive: !1 };
function Gr(e) {
  e.stopImmediatePropagation();
}
function Lt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ul(e) {
  var t = e.document.documentElement, n = Pe(e).on("dragstart.drag", Lt, Nn);
  "onselectstart" in t ? n.on("selectstart.drag", Lt, Nn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function dl(e, t) {
  var n = e.document.documentElement, o = Pe(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Lt, Nn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const to = (e) => () => e;
function xi(e, {
  sourceEvent: t,
  subject: n,
  target: o,
  identifier: r,
  active: i,
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
    active: { value: i, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
xi.prototype.on = function() {
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
function fl() {
  var e = xg, t = wg, n = vg, o = bg, r = {}, i = Zo("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(w) {
    w.on("mousedown.drag", h).filter(o).on("touchstart.drag", v).on("touchmove.drag", m, mg).on("touchend.drag touchcancel.drag", N).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(w, j) {
    if (!(d || !e.call(this, w, j))) {
      var b = y(this, t.call(this, w, j), w, j, "mouse");
      b && (Pe(w.view).on("mousemove.drag", g, Nn).on("mouseup.drag", x, Nn), ul(w.view), Gr(w), l = !1, c = w.clientX, u = w.clientY, b("start", w));
    }
  }
  function g(w) {
    if (Lt(w), !l) {
      var j = w.clientX - c, b = w.clientY - u;
      l = j * j + b * b > f;
    }
    r.mouse("drag", w);
  }
  function x(w) {
    Pe(w.view).on("mousemove.drag mouseup.drag", null), dl(w.view, l), Lt(w), r.mouse("end", w);
  }
  function v(w, j) {
    if (e.call(this, w, j)) {
      var b = w.changedTouches, S = t.call(this, w, j), E = b.length, D, L;
      for (D = 0; D < E; ++D)
        (L = y(this, S, w, j, b[D].identifier, b[D])) && (Gr(w), L("start", w, b[D]));
    }
  }
  function m(w) {
    var j = w.changedTouches, b = j.length, S, E;
    for (S = 0; S < b; ++S)
      (E = r[j[S].identifier]) && (Lt(w), E("drag", w, j[S]));
  }
  function N(w) {
    var j = w.changedTouches, b = j.length, S, E;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < b; ++S)
      (E = r[j[S].identifier]) && (Gr(w), E("end", w, j[S]));
  }
  function y(w, j, b, S, E, D) {
    var L = i.copy(), A = He(D || b, j), _, R, C;
    if ((C = n.call(w, new xi("beforestart", {
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
            r[E] = I, B = a++;
            break;
          case "end":
            delete r[E], --a;
          // falls through
          case "drag":
            A = He(M || T, j), B = a;
            break;
        }
        L.call(
          k,
          w,
          new xi(k, {
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
  return p.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : to(!!w), p) : e;
  }, p.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : to(w), p) : t;
  }, p.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : to(w), p) : n;
  }, p.touchable = function(w) {
    return arguments.length ? (o = typeof w == "function" ? w : to(!!w), p) : o;
  }, p.on = function() {
    var w = i.on.apply(i, arguments);
    return w === i ? p : w;
  }, p.clickDistance = function(w) {
    return arguments.length ? (f = (w = +w) * w, p) : Math.sqrt(f);
  }, p;
}
function qi(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function pl(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function Pn() {
}
var jn = 0.7, $o = 1 / jn, zt = "\\s*([+-]?\\d+)\\s*", Sn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", Ue = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ng = /^#([0-9a-f]{3,8})$/, jg = new RegExp(`^rgb\\(${zt},${zt},${zt}\\)$`), Sg = new RegExp(`^rgb\\(${Ue},${Ue},${Ue}\\)$`), Cg = new RegExp(`^rgba\\(${zt},${zt},${zt},${Sn}\\)$`), Eg = new RegExp(`^rgba\\(${Ue},${Ue},${Ue},${Sn}\\)$`), Ig = new RegExp(`^hsl\\(${Sn},${Ue},${Ue}\\)$`), kg = new RegExp(`^hsla\\(${Sn},${Ue},${Ue},${Sn}\\)$`), Hs = {
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
qi(Pn, wt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ws,
  // Deprecated! Use color.formatHex.
  formatHex: Ws,
  formatHex8: Ag,
  formatHsl: _g,
  formatRgb: Bs,
  toString: Bs
});
function Ws() {
  return this.rgb().formatHex();
}
function Ag() {
  return this.rgb().formatHex8();
}
function _g() {
  return hl(this).formatHsl();
}
function Bs() {
  return this.rgb().formatRgb();
}
function wt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ng.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Fs(t) : n === 3 ? new Me(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? no(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? no(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = jg.exec(e)) ? new Me(t[1], t[2], t[3], 1) : (t = Sg.exec(e)) ? new Me(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Cg.exec(e)) ? no(t[1], t[2], t[3], t[4]) : (t = Eg.exec(e)) ? no(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ig.exec(e)) ? qs(t[1], t[2] / 100, t[3] / 100, 1) : (t = kg.exec(e)) ? qs(t[1], t[2] / 100, t[3] / 100, t[4]) : Hs.hasOwnProperty(e) ? Fs(Hs[e]) : e === "transparent" ? new Me(NaN, NaN, NaN, 0) : null;
}
function Fs(e) {
  return new Me(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function no(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new Me(e, t, n, o);
}
function Dg(e) {
  return e instanceof Pn || (e = wt(e)), e ? (e = e.rgb(), new Me(e.r, e.g, e.b, e.opacity)) : new Me();
}
function wi(e, t, n, o) {
  return arguments.length === 1 ? Dg(e) : new Me(e, t, n, o ?? 1);
}
function Me(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
qi(Me, wi, pl(Pn, {
  brighter(e) {
    return e = e == null ? $o : Math.pow($o, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? jn : Math.pow(jn, e), new Me(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Me(mt(this.r), mt(this.g), mt(this.b), Mo(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Ks,
  // Deprecated! Use color.formatHex.
  formatHex: Ks,
  formatHex8: Tg,
  formatRgb: Xs,
  toString: Xs
}));
function Ks() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}`;
}
function Tg() {
  return `#${gt(this.r)}${gt(this.g)}${gt(this.b)}${gt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Xs() {
  const e = Mo(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${mt(this.r)}, ${mt(this.g)}, ${mt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Mo(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function mt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function gt(e) {
  return e = mt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function qs(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new We(e, t, n, o);
}
function hl(e) {
  if (e instanceof We) return new We(e.h, e.s, e.l, e.opacity);
  if (e instanceof Pn || (e = wt(e)), !e) return new We();
  if (e instanceof We) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), i = Math.max(t, n, o), a = NaN, c = i - r, u = (i + r) / 2;
  return c ? (t === i ? a = (n - o) / c + (n < o) * 6 : n === i ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? i + r : 2 - i - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new We(a, c, u, e.opacity);
}
function $g(e, t, n, o) {
  return arguments.length === 1 ? hl(e) : new We(e, t, n, o ?? 1);
}
function We(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
qi(We, $g, pl(Pn, {
  brighter(e) {
    return e = e == null ? $o : Math.pow($o, e), new We(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? jn : Math.pow(jn, e), new We(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new Me(
      Jr(e >= 240 ? e - 240 : e + 120, r, o),
      Jr(e, r, o),
      Jr(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new We(Ys(this.h), oo(this.s), oo(this.l), Mo(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Mo(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Ys(this.h)}, ${oo(this.s) * 100}%, ${oo(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Ys(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function oo(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function Jr(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Yi = (e) => () => e;
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
  return (e = +e) == 1 ? gl : function(t, n) {
    return n - t ? Pg(t, n, e) : Yi(isNaN(t) ? n : t);
  };
}
function gl(e, t) {
  var n = t - e;
  return n ? Mg(e, n) : Yi(isNaN(e) ? t : e);
}
const Po = (function e(t) {
  var n = Rg(t);
  function o(r, i) {
    var a = n((r = wi(r)).r, (i = wi(i)).r), c = n(r.g, i.g), u = n(r.b, i.b), l = gl(r.opacity, i.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function Lg(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(i) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - i) + t[r] * i;
    return o;
  };
}
function zg(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Vg(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), i = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = mn(e[a], t[a]);
  for (; a < n; ++a) i[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) i[a] = r[a](c);
    return i;
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
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = mn(e[r], t[r]) : o[r] = t[r];
  return function(i) {
    for (r in n) o[r] = n[r](i);
    return o;
  };
}
var vi = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, Qr = new RegExp(vi.source, "g");
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
function yl(e, t) {
  var n = vi.lastIndex = Qr.lastIndex = 0, o, r, i, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = vi.exec(e)) && (r = Qr.exec(t)); )
    (i = r.index) > n && (i = t.slice(n, i), c[a] ? c[a] += i : c[++a] = i), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: Ye(o, r) })), n = Qr.lastIndex;
  return n < t.length && (i = t.slice(n), c[a] ? c[a] += i : c[++a] = i), c.length < 2 ? u[0] ? Bg(u[0].x) : Wg(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function mn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? Yi(t) : (n === "number" ? Ye : n === "string" ? (o = wt(t)) ? (t = o, Po) : yl : t instanceof wt ? Po : t instanceof Date ? Og : zg(t) ? Lg : Array.isArray(t) ? Vg : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Hg : Ye)(e, t);
}
var Us = 180 / Math.PI, bi = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function ml(e, t, n, o, r, i) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: i,
    rotate: Math.atan2(t, e) * Us,
    skewX: Math.atan(u) * Us,
    scaleX: a,
    scaleY: c
  };
}
var ro;
function Fg(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? bi : ml(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Kg(e) {
  return e == null || (ro || (ro = document.createElementNS("http://www.w3.org/2000/svg", "g")), ro.setAttribute("transform", e), !(e = ro.transform.baseVal.consolidate())) ? bi : (e = e.matrix, ml(e.a, e.b, e.c, e.d, e.e, e.f));
}
function xl(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function i(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var x = h.push("translate(", null, t, null, n);
      g.push({ i: x - 4, x: Ye(l, f) }, { i: x - 2, x: Ye(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, o) - 2, x: Ye(l, d) })) : d && f.push(r(f) + "rotate(" + d + o);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, o) - 2, x: Ye(l, d) }) : d && f.push(r(f) + "skewX(" + d + o);
  }
  function u(l, d, f, p, h, g) {
    if (l !== f || d !== p) {
      var x = h.push(r(h) + "scale(", null, ",", null, ")");
      g.push({ i: x - 4, x: Ye(l, f) }, { i: x - 2, x: Ye(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), i(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var g = -1, x = p.length, v; ++g < x; ) f[(v = p[g]).i] = v.x(h);
      return f.join("");
    };
  };
}
var Xg = xl(Fg, "px, ", "px)", "deg)"), qg = xl(Kg, ", ", ")", ")"), Yg = 1e-12;
function Zs(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Ug(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Zg(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const mo = (function e(t, n, o) {
  function r(i, a) {
    var c = i[0], u = i[1], l = i[2], d = a[0], f = a[1], p = a[2], h = d - c, g = f - u, x = h * h + g * g, v, m;
    if (x < Yg)
      m = Math.log(p / l) / t, v = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var N = Math.sqrt(x), y = (p * p - l * l + o * x) / (2 * l * n * N), w = (p * p - l * l - o * x) / (2 * p * n * N), j = Math.log(Math.sqrt(y * y + 1) - y), b = Math.log(Math.sqrt(w * w + 1) - w);
      m = (b - j) / t, v = function(S) {
        var E = S * m, D = Zs(j), L = l / (n * N) * (D * Zg(t * E + j) - Ug(j));
        return [
          c + L * h,
          u + L * g,
          l * D / Zs(t * E + j)
        ];
      };
    }
    return v.duration = m * 1e3 * t / Math.SQRT2, v;
  }
  return r.rho = function(i) {
    var a = Math.max(1e-3, +i), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Bt = 0, an = 0, on = 0, wl = 1e3, Ro, cn, Lo = 0, vt = 0, Jo = 0, Cn = typeof performance == "object" && performance.now ? performance : Date, vl = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ui() {
  return vt || (vl(Gg), vt = Cn.now() + Jo);
}
function Gg() {
  vt = 0;
}
function zo() {
  this._call = this._time = this._next = null;
}
zo.prototype = bl.prototype = {
  constructor: zo,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ui() : +n) + (t == null ? 0 : +t), !this._next && cn !== this && (cn ? cn._next = this : Ro = this, cn = this), this._call = e, this._time = n, Ni();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Ni());
  }
};
function bl(e, t, n) {
  var o = new zo();
  return o.restart(e, t, n), o;
}
function Jg() {
  Ui(), ++Bt;
  for (var e = Ro, t; e; )
    (t = vt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Bt;
}
function Gs() {
  vt = (Lo = Cn.now()) + Jo, Bt = an = 0;
  try {
    Jg();
  } finally {
    Bt = 0, ey(), vt = 0;
  }
}
function Qg() {
  var e = Cn.now(), t = e - Lo;
  t > wl && (Jo -= t, Lo = e);
}
function ey() {
  for (var e, t = Ro, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Ro = n);
  cn = e, Ni(o);
}
function Ni(e) {
  if (!Bt) {
    an && (an = clearTimeout(an));
    var t = e - vt;
    t > 24 ? (e < 1 / 0 && (an = setTimeout(Gs, e - Cn.now() - Jo)), on && (on = clearInterval(on))) : (on || (Lo = Cn.now(), on = setInterval(Qg, wl)), Bt = 1, vl(Gs));
  }
}
function Js(e, t, n) {
  var o = new zo();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var ty = Zo("start", "end", "cancel", "interrupt"), ny = [], Nl = 0, Qs = 1, ji = 2, xo = 3, ea = 4, Si = 5, wo = 6;
function Qo(e, t, n, o, r, i) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  oy(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: ty,
    tween: ny,
    time: i.time,
    delay: i.delay,
    duration: i.duration,
    ease: i.ease,
    timer: null,
    state: Nl
  });
}
function Zi(e, t) {
  var n = Xe(e, t);
  if (n.state > Nl) throw new Error("too late; already scheduled");
  return n;
}
function Je(e, t) {
  var n = Xe(e, t);
  if (n.state > xo) throw new Error("too late; already running");
  return n;
}
function Xe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function oy(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = bl(i, 0, n.time);
  function i(l) {
    n.state = Qs, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== Qs) return u();
    for (d in o)
      if (h = o[d], h.name === n.name) {
        if (h.state === xo) return Js(a);
        h.state === ea ? (h.state = wo, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[d]) : +d < t && (h.state = wo, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[d]);
      }
    if (Js(function() {
      n.state === xo && (n.state = ea, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = ji, n.on.call("start", e, e.__data__, n.index, n.group), n.state === ji) {
      for (n.state = xo, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Si, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === Si && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = wo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function vo(e, t) {
  var n = e.__transition, o, r, i = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        i = !1;
        continue;
      }
      r = o.state > ji && o.state < Si, o.state = wo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    i && delete e.__transition;
  }
}
function ry(e) {
  return this.each(function() {
    vo(this, e);
  });
}
function iy(e, t) {
  var n, o;
  return function() {
    var r = Je(this, e), i = r.tween;
    if (i !== n) {
      o = n = i;
      for (var a = 0, c = o.length; a < c; ++a)
        if (o[a].name === t) {
          o = o.slice(), o.splice(a, 1);
          break;
        }
    }
    r.tween = o;
  };
}
function sy(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var i = Je(this, e), a = i.tween;
    if (a !== o) {
      r = (o = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = r.length; u < l; ++u)
        if (r[u].name === t) {
          r[u] = c;
          break;
        }
      u === l && r.push(c);
    }
    i.tween = r;
  };
}
function ay(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Xe(this.node(), n).tween, r = 0, i = o.length, a; r < i; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? iy : sy)(n, e, t));
}
function Gi(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = Je(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Xe(r, o).value[t];
  };
}
function jl(e, t) {
  var n;
  return (typeof t == "number" ? Ye : t instanceof wt ? Po : (n = wt(t)) ? (t = n, Po) : yl)(e, t);
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
  var o, r = n + "", i;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function dy(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function fy(e, t, n) {
  var o, r, i;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c)));
  };
}
function py(e, t, n) {
  var o, r, i;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c)));
  };
}
function hy(e, t) {
  var n = Go(e), o = n === "transform" ? qg : jl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? py : fy)(n, o, Gi(this, "attr." + e, t)) : t == null ? (n.local ? ly : cy)(n) : (n.local ? dy : uy)(n, o, t));
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
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && yy(e, i)), n;
  }
  return r._value = t, r;
}
function xy(e, t) {
  var n, o;
  function r() {
    var i = t.apply(this, arguments);
    return i !== o && (n = (o = i) && gy(e, i)), n;
  }
  return r._value = t, r;
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
    Zi(this, e).delay = +t.apply(this, arguments);
  };
}
function by(e, t) {
  return t = +t, function() {
    Zi(this, e).delay = t;
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
  typeof e != "function" && (e = el(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var i = t[r], a = i.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = i[l]) && e.call(u, u.__data__, l, i) && c.push(u);
  return new rt(o, this._parents, this._name, this._id);
}
function Dy(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, i = Math.min(o, r), a = new Array(o), c = 0; c < i; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < o; ++c)
    a[c] = t[c];
  return new rt(a, this._parents, this._name, this._id);
}
function Ty(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function $y(e, t, n) {
  var o, r, i = Ty(t) ? Zi : Je;
  return function() {
    var a = i(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
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
  typeof e != "function" && (e = Ki(e));
  for (var o = this._groups, r = o.length, i = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = i[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, Qo(l[p], t, n, p, l, Xe(d, n)));
  return new rt(i, this._parents, t, n);
}
function zy(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Qc(e));
  for (var o = this._groups, r = o.length, i = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, g = Xe(d, n), x = 0, v = p.length; x < v; ++x)
          (h = p[x]) && Qo(h, t, n, x, p, g);
        i.push(p), a.push(d);
      }
  return new rt(i, a, t, n);
}
var Vy = Mn.prototype.constructor;
function Oy() {
  return new Vy(this._groups, this._parents);
}
function Hy(e, t) {
  var n, o, r;
  return function() {
    var i = Wt(this, e), a = (this.style.removeProperty(e), Wt(this, e));
    return i === a ? null : i === n && a === o ? r : r = t(n = i, o = a);
  };
}
function Sl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Wy(e, t, n) {
  var o, r = n + "", i;
  return function() {
    var a = Wt(this, e);
    return a === r ? null : a === o ? i : i = t(o = a, n);
  };
}
function By(e, t, n) {
  var o, r, i;
  return function() {
    var a = Wt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Wt(this, e))), a === u ? null : a === o && u === r ? i : (r = u, i = t(o = a, c));
  };
}
function Fy(e, t) {
  var n, o, r, i = "style." + t, a = "end." + i, c;
  return function() {
    var u = Je(this, e), l = u.on, d = u.value[i] == null ? c || (c = Sl(t)) : void 0;
    (l !== n || r !== d) && (o = (n = l).copy()).on(a, r = d), u.on = o;
  };
}
function Ky(e, t, n) {
  var o = (e += "") == "transform" ? Xg : jl;
  return t == null ? this.styleTween(e, Hy(e, o)).on("end.style." + e, Sl(e)) : typeof t == "function" ? this.styleTween(e, By(e, o, Gi(this, "style." + e, t))).each(Fy(this._id, e)) : this.styleTween(e, Wy(e, o, t), n).on("end.style." + e, null);
}
function Xy(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function qy(e, t, n) {
  var o, r;
  function i() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && Xy(e, a, n)), o;
  }
  return i._value = t, i;
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
  return this.tween("text", typeof e == "function" ? Zy(Gi(this, "text", e)) : Uy(e == null ? "" : e + ""));
}
function Jy(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Qy(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Jy(r)), t;
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
  for (var e = this._name, t = this._id, n = Cl(), o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var a = o[i], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Xe(u, t);
        Qo(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new rt(o, this._parents, e, n);
}
function nm() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(i, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && i();
    } };
    n.each(function() {
      var l = Je(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && i();
  });
}
var om = 0;
function rt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Cl() {
  return ++om;
}
var tt = Mn.prototype;
rt.prototype = {
  constructor: rt,
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
function rm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var im = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: rm
};
function sm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function am(e) {
  var t, n;
  e instanceof rt ? (t = e._id, e = e._name) : (t = Cl(), (n = im).time = Ui(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, i = 0; i < r; ++i)
    for (var a = o[i], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Qo(u, e, t, l, a, n || sm(u, t));
  return new rt(o, this._parents, e, t);
}
Mn.prototype.interrupt = ry;
Mn.prototype.transition = am;
const io = (e) => () => e;
function cm(e, {
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
var er = new nt(1, 0, 0);
El.prototype = nt.prototype;
function El(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return er;
  return e.__zoom;
}
function ei(e) {
  e.stopImmediatePropagation();
}
function rn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function lm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function um() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ta() {
  return this.__zoom || er;
}
function dm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function fm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function pm(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], i = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > i ? (i + a) / 2 : Math.min(0, i) || Math.max(0, a)
  );
}
function Il() {
  var e = lm, t = um, n = pm, o = dm, r = fm, i = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = mo, l = Zo("start", "zoom", "end"), d, f, p, h = 500, g = 150, x = 0, v = 10;
  function m(C) {
    C.property("__zoom", ta).on("wheel.zoom", E, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", L).filter(r).on("touchstart.zoom", A).on("touchmove.zoom", _).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(C, I, k, T) {
    var M = C.selection ? C.selection() : C;
    M.property("__zoom", ta), C !== M ? j(C, I, k, T) : M.interrupt().each(function() {
      b(this, arguments).event(T).start().zoom(null, typeof I == "function" ? I.apply(this, arguments) : I).end();
    });
  }, m.scaleBy = function(C, I, k, T) {
    m.scaleTo(C, function() {
      var M = this.__zoom.k, $ = typeof I == "function" ? I.apply(this, arguments) : I;
      return M * $;
    }, k, T);
  }, m.scaleTo = function(C, I, k, T) {
    m.transform(C, function() {
      var M = t.apply(this, arguments), $ = this.__zoom, B = k == null ? w(M) : typeof k == "function" ? k.apply(this, arguments) : k, W = $.invert(B), O = typeof I == "function" ? I.apply(this, arguments) : I;
      return n(y(N($, O), B, W), M, a);
    }, k, T);
  }, m.translateBy = function(C, I, k, T) {
    m.transform(C, function() {
      return n(this.__zoom.translate(
        typeof I == "function" ? I.apply(this, arguments) : I,
        typeof k == "function" ? k.apply(this, arguments) : k
      ), t.apply(this, arguments), a);
    }, null, T);
  }, m.translateTo = function(C, I, k, T, M) {
    m.transform(C, function() {
      var $ = t.apply(this, arguments), B = this.__zoom, W = T == null ? w($) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(er.translate(W[0], W[1]).scale(B.k).translate(
        typeof I == "function" ? -I.apply(this, arguments) : -I,
        typeof k == "function" ? -k.apply(this, arguments) : -k
      ), $, a);
    }, T, M);
  };
  function N(C, I) {
    return I = Math.max(i[0], Math.min(i[1], I)), I === C.k ? C : new nt(I, C.x, C.y);
  }
  function y(C, I, k) {
    var T = I[0] - k[0] * C.k, M = I[1] - k[1] * C.k;
    return T === C.x && M === C.y ? C : new nt(C.k, T, M);
  }
  function w(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, I, k, T) {
    C.on("start.zoom", function() {
      b(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      b(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var M = this, $ = arguments, B = b(M, $).event(T), W = t.apply(M, $), O = k == null ? w(W) : typeof k == "function" ? k.apply(M, $) : k, U = Math.max(W[1][0] - W[0][0], W[1][1] - W[0][1]), Y = M.__zoom, te = typeof I == "function" ? I.apply(M, $) : I, le = u(Y.invert(O).concat(U / Y.k), te.invert(O).concat(U / te.k));
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
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        I
      );
    }
  };
  function E(C, ...I) {
    if (!e.apply(this, arguments)) return;
    var k = b(this, I).event(C), T = this.__zoom, M = Math.max(i[0], Math.min(i[1], T.k * Math.pow(2, o.apply(this, arguments)))), $ = He(C);
    if (k.wheel)
      (k.mouse[0][0] !== $[0] || k.mouse[0][1] !== $[1]) && (k.mouse[1] = T.invert(k.mouse[0] = $)), clearTimeout(k.wheel);
    else {
      if (T.k === M) return;
      k.mouse = [$, T.invert($)], vo(this), k.start();
    }
    rn(C), k.wheel = setTimeout(B, g), k.zoom("mouse", n(y(N(T, M), k.mouse[0], k.mouse[1]), k.extent, a));
    function B() {
      k.wheel = null, k.end();
    }
  }
  function D(C, ...I) {
    if (p || !e.apply(this, arguments)) return;
    var k = C.currentTarget, T = b(this, I, !0).event(C), M = Pe(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", U, !0), $ = He(C, k), B = C.clientX, W = C.clientY;
    ul(C.view), ei(C), T.mouse = [$, this.__zoom.invert($)], vo(this), T.start();
    function O(Y) {
      if (rn(Y), !T.moved) {
        var te = Y.clientX - B, le = Y.clientY - W;
        T.moved = te * te + le * le > x;
      }
      T.event(Y).zoom("mouse", n(y(T.that.__zoom, T.mouse[0] = He(Y, k), T.mouse[1]), T.extent, a));
    }
    function U(Y) {
      M.on("mousemove.zoom mouseup.zoom", null), dl(Y.view, T.moved), rn(Y), T.event(Y).end();
    }
  }
  function L(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = this.__zoom, T = He(C.changedTouches ? C.changedTouches[0] : C, this), M = k.invert(T), $ = k.k * (C.shiftKey ? 0.5 : 2), B = n(y(N(k, $), T, M), t.apply(this, I), a);
      rn(C), c > 0 ? Pe(this).transition().duration(c).call(j, B, T, C) : Pe(this).call(m.transform, B, T, C);
    }
  }
  function A(C, ...I) {
    if (e.apply(this, arguments)) {
      var k = C.touches, T = k.length, M = b(this, I, C.changedTouches.length === T).event(C), $, B, W, O;
      for (ei(C), B = 0; B < T; ++B)
        W = k[B], O = He(W, this), O = [O, this.__zoom.invert(O), W.identifier], M.touch0 ? !M.touch1 && M.touch0[2] !== O[2] && (M.touch1 = O, M.taps = 0) : (M.touch0 = O, $ = !0, M.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (M.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), vo(this), M.start());
    }
  }
  function _(C, ...I) {
    if (this.__zooming) {
      var k = b(this, I).event(C), T = C.changedTouches, M = T.length, $, B, W, O;
      for (rn(C), $ = 0; $ < M; ++$)
        B = T[$], W = He(B, this), k.touch0 && k.touch0[2] === B.identifier ? k.touch0[0] = W : k.touch1 && k.touch1[2] === B.identifier && (k.touch1[0] = W);
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
      for (ei(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), $ = 0; $ < M; ++$)
        B = T[$], k.touch0 && k.touch0[2] === B.identifier ? delete k.touch0 : k.touch1 && k.touch1[2] === B.identifier && delete k.touch1;
      if (k.touch1 && !k.touch0 && (k.touch0 = k.touch1, delete k.touch1), k.touch0) k.touch0[1] = this.__zoom.invert(k.touch0[0]);
      else if (k.end(), k.taps === 2 && (B = He(B, this), Math.hypot(f[0] - B[0], f[1] - B[1]) < v)) {
        var W = Pe(this).on("dblclick.zoom");
        W && W.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : io(+C), m) : o;
  }, m.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : io(!!C), m) : e;
  }, m.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : io(!!C), m) : r;
  }, m.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : io([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), m) : t;
  }, m.scaleExtent = function(C) {
    return arguments.length ? (i[0] = +C[0], i[1] = +C[1], m) : [i[0], i[1]];
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
    return arguments.length ? (x = (C = +C) * C, m) : Math.sqrt(x);
  }, m.tapDistance = function(C) {
    return arguments.length ? (v = +C, m) : v;
  }, m;
}
const ze = {
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
}, En = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], kl = ["Enter", " ", "Escape"], Al = {
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
var In;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(In || (In = {}));
const _l = {
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
var Vo;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Vo || (Vo = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const na = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Dl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Tl = (e) => "id" in e && "source" in e && "target" in e, hm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Ji = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Rn = (e, t = [0, 0]) => {
  const { width: n, height: o } = it(e), r = e.origin ?? t, i = n * r[0], a = o * r[1];
  return {
    x: e.position.x - i,
    y: e.position.y - a
  };
}, gm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const i = typeof r == "string";
    let a = !t.nodeLookup && !i ? r : void 0;
    t.nodeLookup && (a = i ? t.nodeLookup.get(r) : Ji(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? Oo(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return tr(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return nr(n);
}, Ln = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = tr(n, Oo(r)), o = !0);
  }), o ? nr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Qi = (e, t, [n, o, r] = [0, 0, 1], i = !1, a = !1) => {
  const c = {
    ...Jt(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, x = kn(c, Xt(l)), v = (h ?? 0) * (g ?? 0), m = i && x > 0;
    (!l.internals.handleBounds || m || x >= v || l.dragging) && u.push(l);
  }
  return u;
}, ym = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function mm(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function xm({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: i }, a) {
  if (e.size === 0)
    return !0;
  const c = mm(e, a), u = Ln(c), l = ts(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? i, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function $l({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: i }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      i?.("005", ze.error005());
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
  return (a.measured.width === void 0 || a.measured.height === void 0) && i?.("015", ze.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function wm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const i = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = i.has(p.id), g = !h && p.parentId && a.find((x) => x.id === p.parentId);
    (h || g) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = o.filter((p) => p.deletable !== !1), d = ym(a, u);
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
const Kt = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), bt = (e = { x: 0, y: 0 }, t, n) => ({
  x: Kt(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: Kt(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ml(e, t, n) {
  const { width: o, height: r } = it(n), { x: i, y: a } = n.internals.positionAbsolute;
  return bt(e, [
    [i, a],
    [i + o, a + r]
  ], t);
}
const oa = (e, t, n) => e < t ? Kt(Math.abs(e - t), 1, t) / t : e > n ? -Kt(Math.abs(e - n), 1, t) / t : 0, es = (e, t, n = 15, o = 40) => {
  const r = oa(e.x, o, t.width - o) * n, i = oa(e.y, o, t.height - o) * n;
  return [r, i];
}, tr = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Ci = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), nr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), Xt = (e, t = [0, 0]) => {
  const { x: n, y: o } = Ji(e) ? e.internals.positionAbsolute : Rn(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, Oo = (e, t = [0, 0]) => {
  const { x: n, y: o } = Ji(e) ? e.internals.positionAbsolute : Rn(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Pl = (e, t) => nr(tr(Ci(e), Ci(t))), kn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ra = (e) => Be(e.width) && Be(e.height) && Be(e.x) && Be(e.y), Be = (e) => !isNaN(e) && isFinite(e), Rl = (e, t) => (n, o) => {
}, zn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), Jt = ({ x: e, y: t }, [n, o, r], i = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return i ? zn(c, a) : c;
}, qt = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
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
    const o = $t(e, n), r = $t(e, t);
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
    const o = $t(e.top ?? e.y ?? 0, n), r = $t(e.bottom ?? e.y ?? 0, n), i = $t(e.left ?? e.x ?? 0, t), a = $t(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: i, x: i + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function bm(e, t, n, o, r, i) {
  const { x: a, y: c } = qt(e, [t, n, o]), { x: u, y: l } = qt({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, f = i - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const ts = (e, t, n, o, r, i) => {
  const a = vm(i, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = Kt(l, o, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, g = n / 2 - p * d, x = bm(e, h, g, d, t, n), v = {
    left: Math.min(x.left - a.left, 0),
    top: Math.min(x.top - a.top, 0),
    right: Math.min(x.right - a.right, 0),
    bottom: Math.min(x.bottom - a.bottom, 0)
  };
  return {
    x: h - v.left + v.right,
    y: g - v.top + v.bottom,
    zoom: d
  };
}, An = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Nt(e) {
  return e != null && e !== "parent";
}
function it(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Ll(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function zl(e, t = { width: 0, height: 0 }, n, o, r) {
  const i = { ...e }, a = o.get(n);
  if (a) {
    const c = a.origin || r;
    i.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], i.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return i;
}
function ia(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Nm() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function jm(e) {
  return { ...Al, ...e || {} };
}
function xn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: i, y: a } = Fe(e), c = Jt({ x: i - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? zn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ns = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Vl = (e) => e?.getRootNode?.() || window?.document, Sm = ["INPUT", "SELECT", "TEXTAREA"];
function Ol(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Sm.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const Hl = (e) => "clientX" in e, Fe = (e, t) => {
  const n = Hl(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, sa = (e, t, n, o, r) => {
  const i = t.querySelectorAll(`.${e}`);
  return !i || !i.length ? null : Array.from(i).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: r,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / o,
      y: (c.top - n.top) / o,
      ...ns(a)
    };
  });
};
function Wl({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: i, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + i * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function so(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function aa({ pos: e, x1: t, y1: n, x2: o, y2: r, c: i }) {
  switch (e) {
    case ne.Left:
      return [t - so(t - o, i), n];
    case ne.Right:
      return [t + so(o - t, i), n];
    case ne.Top:
      return [t, n - so(n - r, i)];
    case ne.Bottom:
      return [t, n + so(r - n, i)];
  }
}
function Bl({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = aa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, d] = aa({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, g] = Wl({
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
function Fl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, i = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [i, c, r, a];
}
function Cm({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: i = "basic" }) {
  if (i === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function Em({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const i = tr(Oo(e), Oo(t));
  i.x === i.x2 && (i.x2 += 1), i.y === i.y2 && (i.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return kn(a, nr(i)) > 0;
}
const Kl = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, Im = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), km = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", ze.error006()), t;
  const o = n.getEdgeId || Kl;
  let r;
  return Tl(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, Im(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, Am = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...i } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", ze.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", ze.error007(r)), n;
  const c = o.getEdgeId || Kl, u = {
    ...i,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function Xl({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, i, a, c] = Fl({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, i, a, c];
}
const ca = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, _m = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, la = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Dm({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: i, stepPosition: a }) {
  const c = ca[t], u = ca[o], l = { x: e.x + c.x * i, y: e.y + c.y * i }, d = { x: n.x + u.x * i, y: n.y + u.y * i }, f = _m({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let g = [], x, v;
  const m = { x: 0, y: 0 }, N = { x: 0, y: 0 }, [, , y, w] = Fl({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (x = r.x ?? l.x + (d.x - l.x) * a, v = r.y ?? (l.y + d.y) / 2) : (x = r.x ?? (l.x + d.x) / 2, v = r.y ?? l.y + (d.y - l.y) * a);
    const E = [
      { x, y: l.y },
      { x, y: d.y }
    ], D = [
      { x: l.x, y: v },
      { x: d.x, y: v }
    ];
    c[p] === h ? g = p === "x" ? E : D : g = p === "x" ? D : E;
  } else {
    const E = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? g = c.x === h ? D : E : g = c.y === h ? E : D, t === o) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= i) {
        const I = Math.min(i - 1, i - C);
        c[p] === h ? m[p] = (l[p] > e[p] ? -1 : 1) * I : N[p] = (d[p] > n[p] ? -1 : 1) * I;
      }
    }
    if (t !== o) {
      const C = p === "x" ? "y" : "x", I = c[p] === u[C], k = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!I && k || I && T) || c[p] !== 1 && (!I && T || I && k)) && (g = p === "x" ? E : D);
    }
    const L = { x: l.x + m.x, y: l.y + m.y }, A = { x: d.x + N.x, y: d.y + N.y }, _ = Math.max(Math.abs(L.x - g[0].x), Math.abs(A.x - g[0].x)), R = Math.max(Math.abs(L.y - g[0].y), Math.abs(A.y - g[0].y));
    _ >= R ? (x = (L.x + A.x) / 2, v = g[0].y) : (x = g[0].x, v = (L.y + A.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, b = { x: d.x + N.x, y: d.y + N.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== g[0].x || j.y !== g[0].y ? [j] : [],
    ...g,
    ...b.x !== g[g.length - 1].x || b.y !== g[g.length - 1].y ? [b] : [],
    n
  ], x, v, y, w];
}
function Tm(e, t, n, o) {
  const r = Math.min(la(e, t) / 2, la(t, n) / 2, o), { x: i, y: a } = t;
  if (e.x === i && i === n.x || e.y === a && a === n.y)
    return `L${i} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${i + r * l},${a}Q ${i},${a} ${i},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${i},${a + r * u}Q ${i},${a} ${i + r * c},${a}`;
}
function Ho({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, g, x] = Dm({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: i,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let v = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    v += Tm(f[m - 1], f[m], f[m + 1], a);
  return v += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [v, p, h, g, x];
}
function ua(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function $m(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ua(t) || !ua(n))
    return null;
  const o = t.internals.handleBounds || da(t.handles), r = n.internals.handleBounds || da(n.handles), i = fa(o?.source ?? [], e.sourceHandle), a = fa(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === Ft.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!i || !a)
    return e.onError?.("008", ze.error008(i ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = i?.position || ne.Bottom, u = a?.position || ne.Top, l = jt(t, i, c), d = jt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function da(e) {
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
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, i = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? it(e);
  if (o)
    return { x: r + a / 2, y: i + c / 2 };
  switch (t?.position ?? n) {
    case ne.Top:
      return { x: r + a / 2, y: i };
    case ne.Right:
      return { x: r + a, y: i + c / 2 };
    case ne.Bottom:
      return { x: r + a / 2, y: i + c };
    case ne.Left:
      return { x: r, y: i + c / 2 };
  }
}
function fa(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ei(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function Mm(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const i = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ei(u, t);
      i.has(l) || (a.push({ id: l, color: u.color || n, ...u }), i.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const ql = 1e3, Pm = 10, os = {
  nodeOrigin: [0, 0],
  nodeExtent: En,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Rm = {
  ...os,
  checkEquality: !0
};
function rs(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function Lm(e, t, n) {
  const o = rs(os, n);
  for (const r of e.values())
    if (r.parentId)
      ss(r, e, t, o);
    else {
      const i = Rn(r, o.nodeOrigin), a = Nt(r.extent) ? r.extent : o.nodeExtent, c = bt(i, a, it(r));
      r.internals.positionAbsolute = c;
    }
}
function zm(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], o = [];
  for (const r of e.handles) {
    const i = {
      id: r.id,
      width: r.width ?? 1,
      height: r.height ?? 1,
      nodeId: e.id,
      x: r.x,
      y: r.y,
      position: r.position,
      type: r.type
    };
    r.type === "source" ? n.push(i) : r.type === "target" && o.push(i);
  }
  return {
    source: n,
    target: o
  };
}
function is(e) {
  return e === "manual";
}
function Ii(e, t, n, o = {}) {
  const r = rs(Rm, o), i = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !is(r.zIndexMode) ? ql : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Rn(d, r.nodeOrigin), h = Nt(d.extent) ? d.extent : r.nodeExtent, g = bt(p, h, it(d));
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
          handleBounds: zm(d, f),
          z: Yl(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ss(f, t, n, o, i), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Vm(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ss(e, t, n, o, r) {
  const { elevateNodesOnSelect: i, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = rs(os, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Vm(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * Pm), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = i && !is(u) ? ql : 0, { x: p, y: h, z: g } = Om(e, d, a, c, f, u), { positionAbsolute: x } = e.internals, v = p !== x.x || h !== x.y;
  (v || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: v ? { x: p, y: h } : x,
      z: g
    }
  });
}
function Yl(e, t, n) {
  const o = Be(e.zIndex) ? e.zIndex : 0;
  return is(n) ? o : o + (e.selected ? t : 0);
}
function Om(e, t, n, o, r, i) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = it(e), l = Rn(e, n), d = Nt(e.extent) ? bt(l, e.extent, u) : l;
  let f = bt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (f = Ml(f, u, t));
  const p = Yl(e, r, i), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function as(e, t, n, o = [0, 0]) {
  const r = [], i = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = i.get(a.parentId)?.expandedRect ?? Xt(c), l = Pl(u, a.rect);
    i.set(a.parentId, { expandedRect: l, parent: c });
  }
  return i.size > 0 && i.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = it(c), f = c.origin ?? o, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), x = Math.max(d.height, Math.round(a.height)), v = (g - d.width) * f[0], m = (x - d.height) * f[1];
    (p > 0 || h > 0 || v || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + v,
        y: c.position.y - h + m
      }
    }), n.get(u)?.forEach((N) => {
      e.some((y) => y.id === N.id) || r.push({
        id: N.id,
        type: "position",
        position: {
          x: N.position.x + p,
          y: N.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (p ? f[0] * p - v : 0),
        height: x + (h ? f[1] * h - m : 0)
      }
    });
  }), r;
}
function Hm(e, t, n, o, r, i, a) {
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
    const x = ns(h.nodeElement), v = g.measured.width !== x.width || g.measured.height !== x.height;
    if (!!(x.width && x.height && (v || !g.internals.handleBounds || h.force))) {
      const N = h.nodeElement.getBoundingClientRect(), y = Nt(g.extent) ? g.extent : i;
      let { positionAbsolute: w } = g.internals;
      g.parentId && g.extent === "parent" ? w = Ml(w, x, t.get(g.parentId)) : y && (w = bt(w, y, x));
      const j = {
        ...g,
        measured: x,
        internals: {
          ...g.internals,
          positionAbsolute: w,
          handleBounds: {
            source: sa("source", h.nodeElement, N, f, g.id),
            target: sa("target", h.nodeElement, N, f, g.id)
          }
        }
      };
      t.set(g.id, j), g.parentId && ss(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, v && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: x
      }), g.expandParent && g.parentId && p.push({
        id: g.id,
        parentId: g.parentId,
        rect: Xt(j, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = as(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Wm({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: i }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [r, i]
  ], o);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function pa(e, t, n, o, r, i) {
  let a = r;
  const c = o.get(a) || /* @__PURE__ */ new Map();
  o.set(a, c.set(n, t)), a = `${r}-${e}`;
  const u = o.get(a) || /* @__PURE__ */ new Map();
  if (o.set(a, u.set(n, t)), i) {
    a = `${r}-${e}-${i}`;
    const l = o.get(a) || /* @__PURE__ */ new Map();
    o.set(a, l.set(n, t));
  }
}
function Ul(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: i, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: i, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${i}-${c}`, d = `${i}-${c}--${r}-${a}`;
    pa("source", u, d, e, r, a), pa("target", u, l, e, i, c), t.set(o.id, o);
  }
}
function Zl(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : Zl(n, t) : !1;
}
function ha(e, t, n) {
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
  const r = /* @__PURE__ */ new Map();
  for (const [i, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !Zl(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(i);
      c && r.set(i, {
        id: i,
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
function ti({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
  const i = n.get(e)?.internals.userNode;
  return [
    i ? {
      ...i,
      position: t.get(e)?.position || i.position,
      dragging: o
    } : r[0],
    r
  ];
}
function Fm({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const i = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = zn(i, t);
  return {
    x: a.x - i.x,
    y: a.y - i.y
  };
}
function Km({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let i = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, g = !1, x = null;
  function v({ noDragClassName: N, handleSelector: y, domNode: w, isSelectable: j, nodeId: b, nodeClickDistance: S = 0 }) {
    p = Pe(w);
    function E({ x: _, y: R }) {
      const { nodeLookup: C, nodeExtent: I, snapGrid: k, snapToGrid: T, nodeOrigin: M, onNodeDrag: $, onSelectionDrag: B, onError: W, updateNodePositions: O } = t();
      i = { x: _, y: R };
      let U = !1;
      const Y = c.size > 1, te = Y && I ? Ci(Ln(c)) : null, le = Y && T ? Fm({
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
        } : zn(q, k));
        let ae = null;
        if (Y && I && !P.extent && te) {
          const { positionAbsolute: oe } = P.internals, ue = oe.x - te.x + I[0][0], V = oe.x + P.measured.width - te.x2 + I[1][0], ee = oe.y - te.y + I[0][1], ge = oe.y + P.measured.height - te.y2 + I[1][1];
          ae = [
            [ue, ee],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = $l({
          nodeId: Z,
          nextPosition: q,
          nodeLookup: C,
          nodeExtent: ae || I,
          nodeOrigin: M,
          onError: W
        });
        U = U || P.position.x !== ce.x || P.position.y !== ce.y, P.position = ce, P.internals.positionAbsolute = J;
      }
      if (g = g || U, !!U && (O(c, !0), x && (o || $ || !b && B))) {
        const [Z, P] = ti({
          nodeId: b,
          dragItems: c,
          nodeLookup: C
        });
        o?.(x, c, Z, P), $?.(x, Z, P), b || B?.(x, P);
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
      (k !== 0 || T !== 0) && (i.x = (i.x ?? 0) - k / _[2], i.y = (i.y ?? 0) - T / _[2], await R({ x: k, y: T }) && E(i)), a = requestAnimationFrame(D);
    }
    function L(_) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: I, transform: k, snapGrid: T, snapToGrid: M, selectNodesOnDrag: $, onNodeDragStart: B, onSelectionDragStart: W, unselectNodesAndEdges: O } = t();
      f = !0, (!$ || !j) && !C && b && (R.get(b)?.selected || O()), j && $ && b && e?.(b);
      const U = xn(_.sourceEvent, { transform: k, snapGrid: T, snapToGrid: M, containerBounds: d });
      if (i = U, c = Bm(R, I, U, b), c.size > 0 && (n || B || !b && W)) {
        const [Y, te] = ti({
          nodeId: b,
          dragItems: c,
          nodeLookup: R
        });
        n?.(_.sourceEvent, c, Y, te), B?.(_.sourceEvent, Y, te), b || W?.(_.sourceEvent, te);
      }
    }
    const A = fl().clickDistance(S).on("start", (_) => {
      const { domNode: R, nodeDragThreshold: C, transform: I, snapGrid: k, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, g = !1, x = _.sourceEvent, C === 0 && L(_), i = xn(_.sourceEvent, { transform: I, snapGrid: k, snapToGrid: T, containerBounds: d }), l = Fe(_.sourceEvent, d);
    }).on("drag", (_) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: I, snapToGrid: k, nodeDragThreshold: T, nodeLookup: M } = t(), $ = xn(_.sourceEvent, { transform: C, snapGrid: I, snapToGrid: k, containerBounds: d });
      if (x = _.sourceEvent, (_.sourceEvent.type === "touchmove" && _.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      b && !M.has(b)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const B = Fe(_.sourceEvent, d), W = B.x - l.x, O = B.y - l.y;
          Math.sqrt(W * W + O * O) > T && L(_);
        }
        (i.x !== $.xSnapped || i.y !== $.ySnapped) && c && f && (l = Fe(_.sourceEvent, d), E($));
      }
    }).on("end", (_) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: I, onSelectionDragStop: k } = t();
        if (g && (C(c, !1), g = !1), r || I || !b && k) {
          const [T, M] = ti({
            nodeId: b,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          r?.(_.sourceEvent, c, T, M), I?.(_.sourceEvent, T, M), b || k?.(_.sourceEvent, M);
        }
      }
    }).filter((_) => {
      const R = _.target;
      return !_.button && (!N || !ha(R, `.${N}`, w)) && (!y || ha(R, y, w));
    });
    p.call(A);
  }
  function m() {
    p?.on(".drag", null);
  }
  return {
    update: v,
    destroy: m
  };
}
function Xm(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const i of t.values())
    kn(r, Xt(i)) > 0 && o.push(i);
  return o;
}
const qm = 250;
function Ym(e, t, n, o) {
  let r = [], i = 1 / 0;
  const a = Xm(e, n, t + qm);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: f } = jt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < i ? (r = [{ ...l, x: d, y: f }], i = p) : p === i && r.push({ ...l, x: d, y: f }));
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
function Gl(e, t, n, o, r, i = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && i ? { ...u, ...jt(a, u, u.position, !0) } : u;
}
function Jl(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Um(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const Ql = () => !0;
function Zm(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: i, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: g, onConnect: x, onConnectEnd: v, isValidConnection: m = Ql, onReconnectEnd: N, updateConnection: y, getTransform: w, getFromHandle: j, autoPanSpeed: b, dragThreshold: S = 1, handleDomNode: E }) {
  const D = Vl(e.target);
  let L = 0, A;
  const { x: _, y: R } = Fe(e), C = Jl(i, E), I = c?.getBoundingClientRect();
  let k = !1;
  if (!I || !C)
    return;
  const T = Gl(r, C, o, u, t);
  if (!T)
    return;
  let M = Fe(e, I), $ = !1, B = null, W = !1, O = null;
  function U() {
    if (!d || !I)
      return;
    const [ce, J] = es(M, I, b);
    p({ x: ce, y: J }), L = requestAnimationFrame(U);
  }
  const Y = {
    ...T,
    nodeId: r,
    type: C,
    position: T.position
  }, te = u.get(r);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: jt(te, Y, ne.Left, !0),
    fromHandle: Y,
    fromPosition: Y.position,
    fromNode: te,
    to: M,
    toHandle: null,
    toPosition: na[Y.position],
    toNode: null,
    pointer: M
  };
  function P() {
    k = !0, y(Z), g?.(e, { nodeId: r, handleId: o, handleType: C });
  }
  S === 0 && P();
  function q(ce) {
    if (!k) {
      const { x: ge, y: we } = Fe(ce), Ae = ge - _, Ie = we - R;
      if (!(Ae * Ae + Ie * Ie > S * S))
        return;
      P();
    }
    if (!j() || !Y) {
      ae(ce);
      return;
    }
    const J = w();
    M = Fe(ce, I), A = Ym(Jt(M, J, !1, [1, 1]), n, u, Y), $ || (U(), $ = !0);
    const oe = eu(ce, {
      handle: A,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = oe.handleDomNode, B = oe.connection, W = Um(!!A, oe.isValid);
    const ue = u.get(r), V = ue ? jt(ue, Y, ne.Left, !0) : Z.from, ee = {
      ...Z,
      from: V,
      isValid: W,
      to: oe.toHandle && W ? qt({ x: oe.toHandle.x, y: oe.toHandle.y }, J) : M,
      toHandle: oe.toHandle,
      toPosition: W && oe.toHandle ? oe.toHandle.position : na[Y.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: M
    };
    y(ee), Z = ee;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (k) {
        (A || O) && B && W && x?.(B);
        const { inProgress: J, ...oe } = Z, ue = {
          ...oe,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        v?.(ce, ue), i && N?.(ce, ue);
      }
      h(), cancelAnimationFrame(L), $ = !1, W = !1, B = null, O = null, D.removeEventListener("mousemove", q), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", q), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", q), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", q), D.addEventListener("touchend", ae);
}
function eu(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: i, doc: a, lib: c, flowId: u, isValidConnection: l = Ql, nodeLookup: d }) {
  const f = i === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = Fe(e), x = a.elementFromPoint(h, g), v = x?.classList.contains(`${c}-flow__handle`) ? x : p, m = {
    handleDomNode: v,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (v) {
    const N = Jl(void 0, v), y = v.getAttribute("data-nodeid"), w = v.getAttribute("data-handleid"), j = v.classList.contains("connectable"), b = v.classList.contains("connectableend");
    if (!y || !N)
      return m;
    const S = {
      source: f ? y : o,
      sourceHandle: f ? w : r,
      target: f ? o : y,
      targetHandle: f ? r : w
    };
    m.connection = S;
    const D = j && b && (n === Ft.Strict ? f && N === "source" || !f && N === "target" : y !== o || w !== r);
    m.isValid = D && l(S), m.toHandle = Gl(y, N, w, d, n, !0);
  }
  return m;
}
const ki = {
  onPointerDown: Zm,
  isValid: eu
};
function Gm({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = Pe(e);
  function i({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), j = y.sourceEvent.ctrlKey && An() ? 10 : 1, b = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = w[2] * Math.pow(2, b * j);
      t.scaleTo(S);
    };
    let x = [0, 0];
    const v = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (x = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const w = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const j = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], b = [j[0] - x[0], j[1] - x[1]];
      x = j;
      const S = o() * Math.max(w[2], Math.log(w[2])) * (h ? -1 : 1), E = {
        x: w[0] - b[0] * S,
        y: w[1] - b[1] * S
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: E.x,
        y: E.y,
        zoom: w[2]
      }, D, c);
    }, N = Il().on("start", v).on("zoom", f ? m : null).on("zoom.wheel", p ? g : null);
    r.call(N, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: i,
    destroy: a,
    pointer: He
  };
}
const or = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), ni = ({ x: e, y: t, zoom: n }) => er.translate(e, t).scale(n), Pt = (e, t) => e.target.closest(`.${t}`), tu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Jm = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, oi = (e, t = 0, n = Jm, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, nu = (e) => {
  const t = e.ctrlKey && An() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Qm({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: i, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Pt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const v = He(d), m = nu(d), N = f * Math.pow(2, m);
      o.scaleTo(n, N, v, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === xt.Vertical ? 0 : d.deltaX * p, g = r === xt.Horizontal ? 0 : d.deltaY * p;
    !An() && d.shiftKey && r !== xt.Vertical && (h = d.deltaY * p, g = 0), o.translateBy(
      n,
      -(h / f) * i,
      -(g / f) * i,
      // @ts-ignore
      { internal: !0 }
    );
    const x = or(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, x), e.panScrollTimeout = setTimeout(() => {
      l?.(d, x), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, x));
  };
}
function ex({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const i = o.type === "wheel", a = !t && i && !o.ctrlKey, c = Pt(o, e);
    if (o.ctrlKey && i && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function tx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = or(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function nx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (i) => {
    e.usedRightMouseButton = !!(n && tu(t, e.mouseButton ?? 0)), i.sourceEvent?.sync || o([i.transform.x, i.transform.y, i.transform.k]), r && !i.sourceEvent?.internal && r?.(i.sourceEvent, or(i.transform));
  };
}
function ox({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: i }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, i && tu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && i(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = or(a.transform);
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
function rx({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: i, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, g = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Pt(f, `${l}-flow__node`) || Pt(f, `${l}-flow__edge`)))
      return !0;
    if (!o && !p && !r && !i && !n || a || d && !g || Pt(f, c) && g || Pt(f, u) && (!g || r && g && !e) || !n && f.ctrlKey && g)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !r && !h && g || !o && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(o) && !o.includes(f.button) && f.type === "mousedown")
      return !1;
    const x = Array.isArray(o) && o.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || g) && x;
  };
}
function ix({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: i, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Il().scaleExtent([t, n]).translateExtent(o), p = Pe(e).call(f);
  N({
    x: r.x,
    y: r.y,
    zoom: Kt(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const h = p.on("wheel.zoom"), g = p.on("dblclick.zoom");
  f.wheelDelta(nu);
  async function x(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? mn : mo).transform(oi(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function v({ noWheelClassName: A, noPanClassName: _, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: I, panOnDrag: k, panOnScrollMode: T, panOnScrollSpeed: M, preventScrolling: $, zoomOnPinch: B, zoomOnScroll: W, zoomOnDoubleClick: O, zoomActivationKeyPressed: U, lib: Y, onTransformChange: te, connectionInProgress: le, paneClickDistance: Z, selectionOnDrag: P }) {
    C && !l.isZoomingOrPanning && m();
    const q = I && !U && !C;
    f.clickDistance(P ? 1 / 0 : !Be(Z) || Z < 0 ? 0 : Z);
    const ae = q ? Qm({
      zoomPanValues: l,
      noWheelClassName: A,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: T,
      panOnScrollSpeed: M,
      zoomOnPinch: B,
      onPanZoomStart: a,
      onPanZoom: i,
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
      onPanZoom: i,
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
    const ue = rx({
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
  function m() {
    f.on("zoom", null);
  }
  async function N(A, _, R) {
    const C = ni(A), I = f?.constrain()(C, _, R);
    return I && await x(I), I;
  }
  async function y(A, _) {
    const R = ni(A);
    return await x(R, _), R;
  }
  function w(A) {
    if (p) {
      const _ = ni(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, _, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? El(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function b(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? mn : mo).scaleTo(oi(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, _) {
    return p ? new Promise((R) => {
      f?.interpolate(_?.interpolate === "linear" ? mn : mo).scaleBy(oi(p, _?.duration, _?.ease, () => R(!0)), A);
    }) : !1;
  }
  function E(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function L(A) {
    const _ = !Be(A) || A < 0 ? 0 : A;
    f?.clickDistance(_);
  }
  return {
    update: v,
    destroy: m,
    setViewport: y,
    setViewportConstrained: N,
    getViewport: j,
    scaleTo: b,
    scaleBy: S,
    setScaleExtent: E,
    setTranslateExtent: D,
    syncViewport: w,
    setClickDistance: L
  };
}
var Yt;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(Yt || (Yt = {}));
function sx({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: i }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && i && (u[1] = u[1] * -1), u;
}
function ga(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function at(e, t) {
  return Math.max(0, t - e);
}
function ct(e, t) {
  return Math.max(0, e - t);
}
function ao(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ya(e, t) {
  return e ? !t : t;
}
function ax(e, t, n, o, r, i, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: g } = n, { minWidth: x, maxWidth: v, minHeight: m, maxHeight: N } = o, { x: y, y: w, width: j, height: b, aspectRatio: S } = e;
  let E = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? g - e.pointerY : 0);
  const L = j + (u ? -E : E), A = b + (l ? -D : D), _ = -i[0] * j, R = -i[1] * b;
  let C = ao(L, x, v), I = ao(A, m, N);
  if (a) {
    let M = 0, $ = 0;
    u && E < 0 ? M = at(y + E + _, a[0][0]) : !u && E > 0 && (M = ct(y + L + _, a[1][0])), l && D < 0 ? $ = at(w + D + R, a[0][1]) : !l && D > 0 && ($ = ct(w + A + R, a[1][1])), C = Math.max(C, M), I = Math.max(I, $);
  }
  if (c) {
    let M = 0, $ = 0;
    u && E > 0 ? M = ct(y + E, c[0][0]) : !u && E < 0 && (M = at(y + L, c[1][0])), l && D > 0 ? $ = ct(w + D, c[0][1]) : !l && D < 0 && ($ = at(w + A, c[1][1])), C = Math.max(C, M), I = Math.max(I, $);
  }
  if (r) {
    if (d) {
      const M = ao(L / S, m, N) * S;
      if (C = Math.max(C, M), a) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = ct(w + R + L / S, a[1][1]) * S : $ = at(w + R + (u ? E : -E) / S, a[0][1]) * S, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = at(w + L / S, c[1][1]) * S : $ = ct(w + (u ? E : -E) / S, c[0][1]) * S, C = Math.max(C, $);
      }
    }
    if (f) {
      const M = ao(A * S, x, v) / S;
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
  D = D + (D < 0 ? I : -I), E = E + (E < 0 ? C : -C), r && (p ? L > A * S ? D = (ya(u, l) ? -E : E) / S : E = (ya(u, l) ? -D : D) * S : d ? (D = E / S, l = u) : (E = D * S, u = l));
  const k = u ? y + E : y, T = l ? w + D : w;
  return {
    width: j + (u ? -E : E),
    height: b + (l ? -D : D),
    x: i[0] * E * (u ? -1 : 1) + k,
    y: i[1] * D * (l ? -1 : 1) + T
  };
}
const ou = { width: 0, height: 0, x: 0, y: 0 }, cx = {
  ...ou,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function lx(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, i = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * i, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + i - c, r + a - u]
  ];
}
function ux({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const i = Pe(e);
  let a = {
    controlDirection: ga("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: g, onResizeEnd: x, shouldResize: v }) {
    let m = { ...ou }, N = { ...cx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: ga(l)
    };
    let y, w = null, j = [], b, S, E, D = !1;
    const L = fl().on("start", (A) => {
      const { nodeLookup: _, transform: R, snapGrid: C, snapToGrid: I, nodeOrigin: k, paneDomNode: T } = n();
      if (y = _.get(t), !y)
        return;
      w = T?.getBoundingClientRect() ?? null;
      const { xSnapped: M, ySnapped: $ } = xn(A.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: I,
        containerBounds: w
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, N = {
        ...m,
        pointerX: M,
        pointerY: $,
        aspectRatio: m.width / m.height
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
      h?.(A, { ...m });
    }).on("drag", (A) => {
      const { transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I } = n(), k = xn(A.sourceEvent, {
        transform: _,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: w
      }), T = [];
      if (!y)
        return;
      const { x: M, y: $, width: B, height: W } = m, O = {}, U = y.origin ?? I, { width: Y, height: te, x: le, y: Z } = ax(N, a.controlDirection, k, a.boundaries, a.keepAspectRatio, U, S, E), P = Y !== B, q = te !== W, ae = le !== M && P, ce = Z !== $ && q;
      if (!ae && !ce && !P && !q)
        return;
      if ((ae || ce || U[0] === 1 || U[1] === 1) && (O.x = ae ? le : m.x, O.y = ce ? Z : m.y, m.x = O.x, m.y = O.y, j.length > 0)) {
        const V = le - M, ee = Z - $;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - V + U[0] * (Y - B),
            y: ge.position.y - ee + U[1] * (te - W)
          }, T.push(ge);
      }
      if ((P || q) && (O.width = P && (!a.resizeDirection || a.resizeDirection === "horizontal") ? Y : m.width, O.height = q && (!a.resizeDirection || a.resizeDirection === "vertical") ? te : m.height, m.width = O.width, m.height = O.height), b && y.expandParent) {
        const V = U[0] * (O.width ?? 0);
        O.x && O.x < V && (m.x = V, N.x = N.x - (O.x - V));
        const ee = U[1] * (O.height ?? 0);
        O.y && O.y < ee && (m.y = ee, N.y = N.y - (O.y - ee));
      }
      const J = sx({
        width: m.width,
        prevWidth: B,
        height: m.height,
        prevHeight: W,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...m, direction: J };
      v?.(A, oe) !== !1 && (D = !0, g?.(A, oe), o(O, T));
    }).on("end", (A) => {
      D && (x?.(A, { ...m }), r?.({ ...m }), D = !1);
    });
    i.call(L);
  }
  function u() {
    i.on(".drag", null);
  }
  return {
    update: c,
    destroy: u
  };
}
var ri = { exports: {} }, ii = {}, si = { exports: {} }, ai = {};
var ma;
function dx() {
  if (ma) return ai;
  ma = 1;
  var e = Ze;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, i = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), g = o({ inst: { value: h, getSnapshot: p } }), x = g[0].inst, v = g[1];
    return i(
      function() {
        x.value = h, x.getSnapshot = p, u(x) && v({ inst: x });
      },
      [f, h, p]
    ), r(
      function() {
        return u(x) && v({ inst: x }), f(function() {
          u(x) && v({ inst: x });
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
  return ai.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, ai;
}
var xa;
function fx() {
  return xa || (xa = 1, si.exports = dx()), si.exports;
}
var wa;
function px() {
  if (wa) return ii;
  wa = 1;
  var e = Ze, t = fx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, i = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return ii.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var g = i(null);
    if (g.current === null) {
      var x = { hasValue: !1, value: null };
      g.current = x;
    } else x = g.current;
    g = c(
      function() {
        function m(b) {
          if (!N) {
            if (N = !0, y = b, b = p(b), h !== void 0 && x.hasValue) {
              var S = x.value;
              if (h(S, b))
                return w = S;
            }
            return w = b;
          }
          if (S = w, o(y, b)) return S;
          var E = p(b);
          return h !== void 0 && h(S, E) ? (y = b, S) : (y = b, w = E);
        }
        var N = !1, y, w, j = f === void 0 ? null : f;
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
    var v = r(l, g[0], g[1]);
    return a(
      function() {
        x.hasValue = !0, x.value = v;
      },
      [v]
    ), u(v), v;
  }, ii;
}
var va;
function hx() {
  return va || (va = 1, ri.exports = px()), ri.exports;
}
var gx = hx();
const yx = /* @__PURE__ */ Hd(gx), mx = {}, ba = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((g) => g(t, h));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (mx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, xx = (e) => e ? ba(e) : ba, { useDebugValue: wx } = Ze, { useSyncExternalStoreWithSelector: vx } = yx, bx = (e) => e;
function ru(e, t = bx, n) {
  const o = vx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return wx(o), o;
}
const Na = (e, t) => {
  const n = xx(e), o = (r, i = t) => ru(n, r, i);
  return Object.assign(o, n), o;
}, Nx = (e, t) => e ? Na(e, t) : Na;
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
var ci = { exports: {} }, _e = {};
var ja;
function jx() {
  if (ja) return _e;
  ja = 1;
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
  }, r = /* @__PURE__ */ Symbol.for("react.portal");
  function i(u, l, d) {
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
    return i(u, l, null, d);
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
var Sa;
function Sx() {
  if (Sa) return ci.exports;
  Sa = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), ci.exports = jx(), ci.exports;
}
var Cx = Sx();
const rr = Li(null), Ex = rr.Provider, iu = ze.error001("react");
function pe(e, t) {
  const n = Dn(rr);
  if (n === null)
    throw new Error(iu);
  return ru(n, e, t);
}
function xe() {
  const e = Dn(rr);
  if (e === null)
    throw new Error(iu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ca = { display: "none" }, Ix = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, su = "react-flow__node-desc", au = "react-flow__edge-desc", kx = "react-flow__aria-live", Ax = (e) => e.ariaLiveMessage, _x = (e) => e.ariaLabelConfig;
function Dx({ rfId: e }) {
  const t = pe(Ax);
  return s.jsx("div", { id: `${kx}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Ix, children: t });
}
function Tx({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(_x);
  return s.jsxs(s.Fragment, { children: [s.jsx("div", { id: `${su}-${e}`, style: Ca, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), s.jsx("div", { id: `${au}-${e}`, style: Ca, children: n["edge.a11yDescription.default"] }), !t && s.jsx(Dx, { rfId: e })] });
}
const ir = jc(({ position: e = "top-left", children: t, className: n, style: o, ...r }, i) => {
  const a = `${e}`.split("-");
  return s.jsx("div", { className: Se(["react-flow__panel", n, ...a]), style: o, ref: i, ...r, children: t });
});
ir.displayName = "Panel";
function $x({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : s.jsx(ir, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: s.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Mx = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, co = (e) => e.id;
function Px(e, t) {
  return me(e.selectedNodes.map(co), t.selectedNodes.map(co)) && me(e.selectedEdges.map(co), t.selectedEdges.map(co));
}
function Rx({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = pe(Mx, Px);
  return G(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((i) => i(r));
  }, [n, o, e]), null;
}
const Lx = (e) => !!e.onSelectionChangeHandlers;
function zx({ onSelectionChange: e }) {
  const t = pe(Lx);
  return e || t ? s.jsx(Rx, { onSelectionChange: e }) : null;
}
const cu = [0, 0], Vx = { x: 0, y: 0, zoom: 1 }, Ox = [
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
], Ea = [...Ox, "rfId"], Hx = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Ia = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: En,
  nodeOrigin: cu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Wx(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: i, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(Hx, me), l = xe();
  G(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Ia, c();
  }), []);
  const d = re(Ia);
  return G(
    () => {
      for (const f of Ea) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? o(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? i(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: jm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Ea.map((f) => e[f])
  ), null;
}
function ka() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Bx(e) {
  const [t, n] = F(e === "system" ? null : e);
  return G(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = ka(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : ka()?.matches ? "dark" : "light";
}
const Aa = typeof document < "u" ? document : null;
function _n(e = null, t = { target: Aa, actInsideInputWithModifier: !0 }) {
  const [n, o] = F(!1), r = re(!1), i = re(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
    const u = t?.target ?? Aa, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && Ol(h))
          return !1;
        const x = Da(h.code, c);
        if (i.current.add(h[x]), _a(a, i.current, !1)) {
          const v = h.composedPath?.()?.[0] || h.target, m = v?.nodeName === "BUTTON" || v?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && h.preventDefault(), o(!0);
        }
      }, f = (h) => {
        const g = Da(h.code, c);
        _a(a, i.current, !0) ? (o(!1), i.current.clear()) : i.current.delete(h[g]), h.key === "Meta" && i.current.clear(), r.current = !1;
      }, p = () => {
        i.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, o]), n;
}
function _a(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Da(e, t) {
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
      const { transform: [o, r, i], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? o,
        y: t.y ?? r,
        zoom: t.zoom ?? i
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, o] = e.getState().transform;
      return { x: t, y: n, zoom: o };
    },
    setCenter: async (t, n, o) => e.getState().setCenter(t, n, o),
    fitBounds: async (t, n) => {
      const { width: o, height: r, minZoom: i, maxZoom: a, panZoom: c } = e.getState(), u = ts(t, o, r, i, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: o, snapGrid: r, snapToGrid: i, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? r, f = n.snapToGrid ?? i;
      return Jt(l, o, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: i } = o.getBoundingClientRect(), a = qt(t, n);
      return {
        x: a.x + r,
        y: a.y + i
      };
    }
  }), []);
};
function lu(e, t) {
  const n = [], o = /* @__PURE__ */ new Map(), r = [];
  for (const i of e)
    if (i.type === "add") {
      r.push(i);
      continue;
    } else if (i.type === "remove" || i.type === "replace")
      o.set(i.id, [i]);
    else {
      const a = o.get(i.id);
      a ? a.push(i) : o.set(i.id, [i]);
    }
  for (const i of t) {
    const a = o.get(i.id);
    if (!a) {
      n.push(i);
      continue;
    }
    if (a[0].type === "remove")
      continue;
    if (a[0].type === "replace") {
      n.push({ ...a[0].item });
      continue;
    }
    const c = { ...i };
    for (const u of a)
      Kx(u, c);
    n.push(c);
  }
  return r.length && r.forEach((i) => {
    i.index !== void 0 ? n.splice(i.index, 0, { ...i.item }) : n.push({ ...i.item });
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
function uu(e, t) {
  return lu(e, t);
}
function du(e, t) {
  return lu(e, t);
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
  for (const [r, i] of e) {
    const a = t.has(r);
    !(i.selected === void 0 && !a) && i.selected !== a && (n && (i.selected = a), o.push(ht(i.id, a)));
  }
  return o;
}
function Ta({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, i] of e.entries()) {
    const a = t.get(i.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== i && n.push({ id: i.id, item: i, type: "replace" }), c === void 0 && n.push({ item: i, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function $a(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const fu = Rl();
function pu(e, t, n = {}) {
  return km(e, t, {
    ...n,
    onError: n.onError ?? fu
  });
}
function Xx(e, t, n, o = { shouldReplaceId: !0 }) {
  return Am(e, t, n, {
    ...o,
    onError: o.onError ?? fu
  });
}
const Ma = (e) => hm(e), qx = (e) => Tl(e);
function hu(e) {
  return jc(e);
}
const Yx = typeof window < "u" ? bd : G;
function Pa(e) {
  const [t, n] = F(BigInt(0)), [o] = F(() => Ux(() => n((r) => r + BigInt(1))));
  return Yx(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
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
const gu = Li(null);
function Zx({ children: e }) {
  const t = xe(), n = ie((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let x = u;
    for (const m of c)
      x = typeof m == "function" ? m(x) : m;
    let v = Ta({
      items: x,
      lookup: p
    });
    for (const m of g.values())
      v = m(v);
    d && l(x), v.length > 0 ? f?.(v) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: N, setNodes: y } = t.getState();
      m && y(N);
    });
  }, []), o = Pa(n), r = ie((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    d ? l(h) : f && f(Ta({
      items: h,
      lookup: p
    }));
  }, []), i = Pa(r), a = de(() => ({ nodeQueue: o, edgeQueue: i }), []);
  return s.jsx(gu.Provider, { value: a, children: e });
}
function Gx() {
  const e = Dn(gu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Jx = (e) => !!e.panZoom;
function cs() {
  const e = Fx(), t = xe(), n = Gx(), o = pe(Jx), r = de(() => {
    const i = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), g = Ma(f) ? f : p.get(f.id), x = g.parentId ? zl(g.position, g.measured, g.parentId, p, h) : g.position, v = {
        ...g,
        position: x,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return Xt(v);
    }, l = (f, p, h = { replace: !1 }) => {
      a((g) => g.map((x) => {
        if (x.id === f) {
          const v = typeof p == "function" ? p(x) : p;
          return h.replace && Ma(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((g) => g.map((x) => {
        if (x.id === f) {
          const v = typeof p == "function" ? p(x) : p;
          return h.replace && qx(v) ? v : { ...x, ...v };
        }
        return x;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((f) => ({ ...f })),
      getNode: (f) => i(f)?.internals.userNode,
      getInternalNode: i,
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [g, x, v] = h;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: p.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: x,
            zoom: v
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: g, onNodesDelete: x, onEdgesDelete: v, triggerNodeChanges: m, triggerEdgeChanges: N, onDelete: y, onBeforeDelete: w } = t.getState(), { nodes: j, edges: b } = await wm({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: g,
          onBeforeDelete: w
        }), S = b.length > 0, E = j.length > 0;
        if (S) {
          const D = b.map($a);
          v?.(b), N(D);
        }
        if (E) {
          const D = j.map($a);
          x?.(j), m(D);
        }
        return (E || S) && y?.({ nodes: j, edges: b }), { deletedNodes: j, deletedEdges: b };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const g = ra(f), x = g ? f : u(f), v = h !== void 0;
        return x ? (h || t.getState().nodes).filter((m) => {
          const N = t.getState().nodeLookup.get(m.id);
          if (N && !g && (m.id === f.id || !N.internals.positionAbsolute))
            return !1;
          const y = Xt(v ? m : N), w = kn(y, x);
          return p && w > 0 || w >= y.width * y.height || w >= x.width * x.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const x = ra(f) ? f : u(f);
        if (!x)
          return !1;
        const v = kn(x, p);
        return h && v > 0 || v >= p.width * p.height || v >= x.width * x.height;
      },
      updateNode: l,
      updateNodeData: (f, p, h = { replace: !1 }) => {
        l(f, (g) => {
          const x = typeof p == "function" ? p(g) : p;
          return h.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (f, p, h = { replace: !1 }) => {
        d(f, (g) => {
          const x = typeof p == "function" ? p(g) : p;
          return h.replace ? { ...g, data: x } : { ...g, data: { ...g.data, ...x } };
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
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ra = (e) => e.selected, Qx = typeof window < "u" ? window : void 0;
function ew({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = cs(), r = _n(e, { actInsideInputWithModifier: !1 }), i = _n(t, { target: Qx });
  G(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Ra), edges: a.filter(Ra) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), G(() => {
    n.setState({ multiSelectionActive: i });
  }, [i]);
}
function tw(e) {
  const t = xe();
  G(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ns(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", ze.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const sr = {
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
function ow({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: i = xt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: g, noWheelClassName: x, noPanClassName: v, onViewportChange: m, isControlledViewport: N, paneClickDistance: y, selectionOnDrag: w }) {
  const j = xe(), b = re(null), { userSelectionActive: S, lib: E, connectionInProgress: D } = pe(nw, me), L = _n(p), A = re();
  tw(b);
  const _ = ie((R) => {
    m?.({ x: R[0], y: R[1], zoom: R[2] }), N || j.setState({ transform: R });
  }, [m, N]);
  return G(() => {
    if (b.current) {
      A.current = ix({
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
      panOnScrollSpeed: r,
      panOnScrollMode: i,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: L,
      preventScrolling: h,
      noPanClassName: v,
      userSelectionActive: S,
      noWheelClassName: x,
      lib: E,
      onTransformChange: _,
      connectionInProgress: D,
      selectionOnDrag: w,
      paneClickDistance: y
    });
  }, [
    e,
    t,
    n,
    o,
    r,
    i,
    a,
    c,
    L,
    h,
    v,
    S,
    x,
    E,
    _,
    D,
    w,
    y
  ]), s.jsx("div", { className: "react-flow__renderer", ref: b, style: sr, children: g });
}
const rw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function iw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(rw, me);
  return e && t ? s.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const li = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, sw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function aw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = In.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: i, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: g, children: x }) {
  const v = re(0), m = xe(), { userSelectionActive: N, elementsSelectable: y, dragging: w, connectionInProgress: j, panBy: b, autoPanSpeed: S } = pe(sw, me), E = y && (e || N), D = re(null), L = re(), A = re(/* @__PURE__ */ new Set()), _ = re(/* @__PURE__ */ new Set()), R = re(!1), C = re({ x: 0, y: 0 }), I = re(!1), k = (P) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(P), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, T = (P) => {
    if (Array.isArray(o) && o?.includes(2)) {
      P.preventDefault();
      return;
    }
    d?.(P);
  }, M = f ? (P) => f(P) : void 0, $ = (P) => {
    R.current && (P.stopPropagation(), R.current = !1);
  }, B = (P) => {
    const { domNode: q, transform: ae } = m.getState();
    if (L.current = q?.getBoundingClientRect(), !L.current)
      return;
    const ce = P.target === D.current;
    if (!ce && !!P.target.closest(".nokey") || !e || !(a && ce || t) || P.button !== 0 || !P.isPrimary)
      return;
    P.target?.setPointerCapture?.(P.pointerId), R.current = !1;
    const { x: ue, y: V } = Fe(P.nativeEvent, L.current), ee = Jt({ x: ue, y: V }, ae);
    m.setState({
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
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: oe, connectionLookup: ue, triggerNodeChanges: V, triggerEdgeChanges: ee, defaultEdgeOptions: ge } = m.getState(), we = { x: ae.startX, y: ae.startY }, { x: Ae, y: Ie } = qt(we, ce), Te = {
      startX: we.x,
      startY: we.y,
      x: P < Ae ? P : Ae,
      y: q < Ie ? q : Ie,
      width: Math.abs(P - Ae),
      height: Math.abs(q - Ie)
    }, Qe = A.current, Ve = _.current;
    A.current = new Set(Qi(J, Te, ce, n === In.Partial, !0).map((H) => H.id)), _.current = /* @__PURE__ */ new Set();
    const z = ge?.selectable ?? !0;
    for (const H of A.current) {
      const K = ue.get(H);
      if (K)
        for (const { edgeId: X } of K.values()) {
          const se = oe.get(X);
          se && (se.selectable ?? z) && _.current.add(X);
        }
    }
    if (!ia(Qe, A.current)) {
      const H = Rt(J, A.current, !0);
      V(H);
    }
    if (!ia(Ve, _.current)) {
      const H = Rt(oe, _.current);
      ee(H);
    }
    m.setState({
      userSelectionRect: Te,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !L.current)
      return;
    const [P, q] = es(C.current, L.current, S);
    b({ x: P, y: q }).then((ae) => {
      if (!R.current || !ae) {
        v.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = C.current;
      W(ce, J), v.current = requestAnimationFrame(O);
    });
  }
  const U = () => {
    cancelAnimationFrame(v.current), v.current = 0, I.current = !1;
  };
  G(() => () => U(), []);
  const Y = (P) => {
    const { userSelectionRect: q, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!L.current || !q)
      return;
    const { x: J, y: oe } = Fe(P.nativeEvent, L.current);
    C.current = { x: J, y: oe };
    const ue = qt({ x: q.startX, y: q.startY }, ae);
    if (!R.current) {
      const V = t ? 0 : i;
      if (Math.hypot(J - ue.x, oe - ue.y) <= V)
        return;
      ce(), c?.(P);
    }
    R.current = !0, I.current || (O(), I.current = !0), W(J, oe);
  }, te = (P) => {
    P.button === 0 && (P.target?.releasePointerCapture?.(P.pointerId), !N && P.target === D.current && m.getState().userSelectionRect && k?.(P), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(P), m.setState({
      nodesSelectionActive: A.current.size > 0
    })), U());
  }, le = (P) => {
    P.target?.releasePointerCapture?.(P.pointerId), U();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return s.jsxs("div", { className: Se(["react-flow__pane", { draggable: Z, dragging: w, selection: e }]), onClick: E ? void 0 : li(k, D), onContextMenu: li(T, D), onWheel: li(M, D), onPointerEnter: E ? void 0 : p, onPointerMove: E ? Y : h, onPointerUp: E ? te : void 0, onPointerCancel: E ? le : void 0, onPointerDownCapture: E ? B : void 0, onClickCapture: E ? $ : void 0, onPointerLeave: g, ref: D, style: sr, children: [x, s.jsx(iw, {})] });
}
function Ai({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: i, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", ze.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (i({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function yu({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: i, nodeClickDistance: a }) {
  const c = xe(), [u, l] = F(!1), d = re();
  return G(() => {
    d.current = Km({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Ai({
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
        isSelectable: i,
        nodeId: r,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, o, t, i, e, r, a]), u;
}
const cw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function mu() {
  const e = xe();
  return ie((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: i, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = cw(a), h = r ? i[0] : 5, g = r ? i[1] : 5, x = n.direction.x * h * n.factor, v = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!p(m))
        continue;
      let N = {
        x: m.internals.positionAbsolute.x + x,
        y: m.internals.positionAbsolute.y + v
      };
      r && (N = zn(N, i));
      const { position: y, positionAbsolute: w } = $l({
        nodeId: m.id,
        nextPosition: N,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = w, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const ls = Li(null), lw = ls.Provider;
ls.Consumer;
const xu = () => Dn(ls), uw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), dw = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: i, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: i === Ft.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function fw({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: i = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const g = a || null, x = e === "target", v = xe(), m = xu(), { connectOnClick: N, noPanClassName: y, rfId: w } = pe(uw, me), { connectingFrom: j, connectingTo: b, clickConnecting: S, isPossibleEndHandle: E, connectionInProcess: D, clickConnectionInProcess: L, valid: A } = pe(dw(m, g, e), me);
  m || v.getState().onError?.("010", ze.error010());
  const _ = (I) => {
    const { defaultEdgeOptions: k, onConnect: T, hasDefaultEdges: M } = v.getState(), $ = {
      ...k,
      ...I
    };
    if (M) {
      const { edges: B, setEdges: W, onError: O } = v.getState();
      W(pu($, B, { onError: O }));
    }
    T?.($), c?.($);
  }, R = (I) => {
    if (!m)
      return;
    const k = Hl(I.nativeEvent);
    if (r && (k && I.button === 0 || !k)) {
      const T = v.getState();
      ki.onPointerDown(I.nativeEvent, {
        handleDomNode: I.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: x,
        handleId: g,
        nodeId: m,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...M) => v.getState().onConnectEnd?.(...M),
        updateConnection: T.updateConnection,
        onConnect: _,
        isValidConnection: n || ((...M) => v.getState().isValidConnection?.(...M) ?? !0),
        getTransform: () => v.getState().transform,
        getFromHandle: () => v.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    k ? d?.(I) : f?.(I);
  }, C = (I) => {
    const { onClickConnectStart: k, onClickConnectEnd: T, connectionClickStartHandle: M, connectionMode: $, isValidConnection: B, lib: W, rfId: O, nodeLookup: U, connection: Y } = v.getState();
    if (!m || !M && !r)
      return;
    if (!M) {
      k?.(I.nativeEvent, { nodeId: m, handleId: g, handleType: e }), v.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const te = Vl(I.target), le = n || B, { connection: Z, isValid: P } = ki.isValid(I.nativeEvent, {
      handle: {
        nodeId: m,
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
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, T?.(I, q), v.setState({ connectionClickStartHandle: null });
  };
  return s.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${w}-${m}-${g}-${e}`, className: Se([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !x,
      target: x,
      connectable: o,
      connectablestart: r,
      connectableend: i,
      clickconnecting: S,
      connectingfrom: j,
      connectingto: b,
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!D || E) && (D || L ? i : r)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: N ? C : void 0, ref: h, ...p, children: u });
}
const Ut = be(hu(fw));
function pw({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return s.jsxs(s.Fragment, { children: [e?.label, s.jsx(Ut, { type: "source", position: n, isConnectable: t })] });
}
function hw({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return s.jsxs(s.Fragment, { children: [s.jsx(Ut, { type: "target", position: n, isConnectable: t }), e?.label, s.jsx(Ut, { type: "source", position: o, isConnectable: t })] });
}
function gw() {
  return null;
}
function yw({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return s.jsxs(s.Fragment, { children: [s.jsx(Ut, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Wo = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, La = {
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
  const { width: t, height: n, x: o, y: r } = Ln(e.nodeLookup, {
    filter: (i) => !!i.selected
  });
  return {
    width: Be(t) ? t : null,
    height: Be(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function ww({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: i, transformString: a, userSelectionActive: c } = pe(xw, me), u = mu(), l = re(null);
  G(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && i !== null;
  if (yu({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const g = o.getState().nodes.filter((x) => x.selected);
    e(h, g);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Wo, h.key) && (h.preventDefault(), u({
      direction: Wo[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return s.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: s.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: i
  } }) });
}
const za = typeof window < "u" ? window : void 0, vw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function wu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: x, zoomActivationKeyCode: v, elementsSelectable: m, zoomOnScroll: N, zoomOnPinch: y, panOnScroll: w, panOnScrollSpeed: j, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: E, autoPanOnSelection: D, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, preventScrolling: C, onSelectionContextMenu: I, noWheelClassName: k, noPanClassName: T, disableKeyboardA11y: M, onViewportChange: $, isControlledViewport: B }) {
  const { nodesSelectionActive: W, userSelectionActive: O } = pe(vw, me), U = _n(l, { target: za }), Y = _n(x, { target: za }), te = Y || E, le = Y || w, Z = d && te !== !0, P = U || O || Z;
  return ew({ deleteKeyCode: u, multiSelectionKeyCode: g }), s.jsx(ow, { onPaneContextMenu: i, elementsSelectable: m, zoomOnScroll: N, zoomOnPinch: y, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: b, zoomOnDoubleClick: S, panOnDrag: !U && te, defaultViewport: L, translateExtent: A, minZoom: _, maxZoom: R, zoomActivationKeyCode: v, preventScrolling: C, noWheelClassName: k, noPanClassName: T, onViewportChange: $, isControlledViewport: B, paneClickDistance: c, selectionOnDrag: Z, children: s.jsxs(aw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: i, onPaneScroll: a, panOnDrag: te, autoPanOnSelection: D, isSelecting: !!P, selectionMode: f, selectionKeyPressed: U, paneClickDistance: c, selectionOnDrag: Z, children: [e, W && s.jsx(ww, { onSelectionContextMenu: I, noPanClassName: T, disableKeyboardA11y: M })] }) });
}
wu.displayName = "FlowRenderer";
const bw = be(wu), Nw = (e) => (t) => e ? Qi(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function jw(e) {
  return pe(ie(Nw(e), [e]), me);
}
const Sw = (e) => e.updateNodeInternals;
function Cw() {
  const e = pe(Sw), [t] = F(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const o = /* @__PURE__ */ new Map();
    n.forEach((r) => {
      const i = r.target.getAttribute("data-id");
      o.set(i, {
        id: i,
        nodeElement: r.target,
        force: !0
      });
    }), e(o);
  }));
  return G(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Ew({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), i = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return G(() => {
    i.current && !e.hidden && (!d || a.current !== i.current) && (a.current && o?.unobserve(a.current), o?.observe(i.current), a.current = i.current);
  }, [d, e.hidden]), G(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), G(() => {
    if (i.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: i.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), i;
}
function Iw({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: i, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: g, rfId: x, nodeTypes: v, nodeClickDistance: m, onError: N }) {
  const { node: y, internals: w, isParent: j } = pe((P) => {
    const q = P.nodeLookup.get(e), ae = P.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: ae
    };
  }, me);
  let b = y.type || "default", S = v?.[b] || La[b];
  S === void 0 && (N?.("003", ze.error003(b)), b = "default", S = v?.default || La.default);
  const E = !!(y.draggable || c && typeof y.draggable > "u"), D = !!(y.selectable || u && typeof y.selectable > "u"), L = !!(y.connectable || l && typeof y.connectable > "u"), A = !!(y.focusable || d && typeof y.focusable > "u"), _ = xe(), R = Ll(y), C = Ew({ node: y, nodeType: b, hasDimensions: R, resizeObserver: f }), I = yu({
    nodeRef: C,
    disabled: y.hidden || !E,
    noDragClassName: p,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: m
  }), k = mu();
  if (y.hidden)
    return null;
  const T = it(y), M = mw(y), $ = D || E || t || n || o || r, B = n ? (P) => n(P, { ...w.userNode }) : void 0, W = o ? (P) => o(P, { ...w.userNode }) : void 0, O = r ? (P) => r(P, { ...w.userNode }) : void 0, U = i ? (P) => i(P, { ...w.userNode }) : void 0, Y = a ? (P) => a(P, { ...w.userNode }) : void 0, te = (P) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: ae } = _.getState();
    D && (!q || !E || ae > 0) && Ai({
      id: e,
      store: _,
      nodeRef: C
    }), t && t(P, { ...w.userNode });
  }, le = (P) => {
    if (!(Ol(P.nativeEvent) || g)) {
      if (kl.includes(P.key) && D) {
        const q = P.key === "Escape";
        Ai({
          id: e,
          store: _,
          unselect: q,
          nodeRef: C
        });
      } else if (E && y.selected && Object.prototype.hasOwnProperty.call(Wo, P.key)) {
        P.preventDefault();
        const { ariaLabelConfig: q } = _.getState();
        _.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: P.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), k({
          direction: Wo[P.key],
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
    Qi(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: q, height: ae }, P, !0).length > 0 || J(y.position.x + T.width / 2, y.position.y + T.height / 2, {
      zoom: P[2]
    });
  };
  return s.jsx("div", { className: Se([
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
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...y.style,
    ...M
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: B, onMouseMove: W, onMouseLeave: O, onContextMenu: U, onClick: te, onDoubleClick: Y, onKeyDown: A ? le : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? Z : void 0, role: y.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${su}-${x}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: s.jsx(lw, { value: e, children: s.jsx(S, { id: e, data: y.data, type: b, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: y.selected ?? !1, selectable: D, draggable: E, deletable: y.deletable ?? !0, isConnectable: L, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: I, dragHandle: y.dragHandle, zIndex: w.z, parentId: y.parentId, ...T }) }) });
}
var kw = be(Iw);
const Aw = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function vu(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: i } = pe(Aw, me), a = jw(e.onlyRenderVisibleElements), c = Cw();
  return s.jsx("div", { className: "react-flow__nodes", style: sr, children: a.map((u) => (
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
    s.jsx(kw, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: i }, u)
  )) });
}
vu.displayName = "NodeRenderer";
const _w = be(vu);
function Dw(e) {
  return pe(ie((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const i = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        i && a && Em({
          sourceNode: i,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && o.push(r.id);
      }
    return o;
  }, [e]), me);
}
const Tw = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return s.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, $w = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return s.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Va = {
  [Vo.Arrow]: Tw,
  [Vo.ArrowClosed]: $w
};
function Mw(e) {
  const t = xe();
  return de(() => Object.prototype.hasOwnProperty.call(Va, e) ? Va[e] : (t.getState().onError?.("009", ze.error009(e)), null), [e]);
}
const Pw = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: i = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Mw(t);
  return u ? s.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: i, orient: c, refX: "0", refY: "0", children: s.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, bu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((i) => i.edges), o = pe((i) => i.defaultEdgeOptions), r = de(() => Mm(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? s.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: s.jsx("defs", { children: r.map((i) => s.jsx(Pw, { id: i.id, type: i.type, color: i.color, width: i.width, height: i.height, markerUnits: i.markerUnits, strokeWidth: i.strokeWidth, orient: i.orient }, i.id)) }) }) : null;
};
bu.displayName = "MarkerDefinitions";
var Rw = be(bu);
function Nu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: i, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = F({ x: 1, y: 0, width: 0, height: 0 }), h = Se(["react-flow__edge-textwrapper", l]), g = re(null);
  return G(() => {
    if (g.current) {
      const x = g.current.getBBox();
      p({
        x: x.x,
        y: x.y,
        width: x.width,
        height: x.height
      });
    }
  }, [n]), n ? s.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [r && s.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: i, rx: c, ry: c }), s.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
Nu.displayName = "EdgeText";
const Lw = be(Nu);
function Vn({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return s.jsxs(s.Fragment, { children: [s.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), l ? s.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Be(t) && Be(n) ? s.jsx(Lw, { x: t, y: n, label: o, labelStyle: r, labelShowBg: i, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Oa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function ju({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: i = ne.Top }) {
  const [a, c] = Oa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Oa({
    pos: i,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = Wl({
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
function Su(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: x, markerStart: v, interactionWidth: m }) => {
    const [N, y, w] = ju({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: i,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return s.jsx(Vn, { id: j, path: N, labelX: y, labelY: w, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: x, markerStart: v, interactionWidth: m });
  });
}
const zw = Su({ isInternal: !1 }), Cu = Su({ isInternal: !0 });
zw.displayName = "SimpleBezierEdge";
Cu.displayName = "SimpleBezierEdgeInternal";
function Eu(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: N }) => {
    const [y, w, j] = Ho({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: r,
      targetY: i,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), b = e.isInternal ? void 0 : t;
    return s.jsx(Vn, { id: b, path: y, labelX: w, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: x, markerStart: v, interactionWidth: N });
  });
}
const Iu = Eu({ isInternal: !1 }), ku = Eu({ isInternal: !0 });
Iu.displayName = "SmoothStepEdge";
ku.displayName = "SmoothStepEdgeInternal";
function Au(e) {
  return be(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return s.jsx(Iu, { ...n, id: o, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Vw = Au({ isInternal: !1 }), _u = Au({ isInternal: !0 });
Vw.displayName = "StepEdge";
_u.displayName = "StepEdgeInternal";
function Du(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: x }) => {
    const [v, m, N] = Xl({ sourceX: n, sourceY: o, targetX: r, targetY: i }), y = e.isInternal ? void 0 : t;
    return s.jsx(Vn, { id: y, path: v, labelX: m, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: g, interactionWidth: x });
  });
}
const Ow = Du({ isInternal: !1 }), Tu = Du({ isInternal: !0 });
Ow.displayName = "StraightEdge";
Tu.displayName = "StraightEdgeInternal";
function $u(e) {
  return be(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: x, markerStart: v, pathOptions: m, interactionWidth: N }) => {
    const [y, w, j] = Bl({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: i,
      targetPosition: c,
      curvature: m?.curvature
    }), b = e.isInternal ? void 0 : t;
    return s.jsx(Vn, { id: b, path: y, labelX: w, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: g, markerEnd: x, markerStart: v, interactionWidth: N });
  });
}
const Hw = $u({ isInternal: !1 }), Mu = $u({ isInternal: !0 });
Hw.displayName = "BezierEdge";
Mu.displayName = "BezierEdgeInternal";
const Ha = {
  default: Mu,
  straight: Tu,
  step: _u,
  smoothstep: ku,
  simplebezier: Cu
}, Wa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Ww = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Bw = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, Ba = "react-flow__edgeupdater";
function Fa({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: i, onMouseOut: a, type: c }) {
  return s.jsx("circle", { onMouseDown: r, onMouseEnter: i, onMouseOut: a, className: Se([Ba, `${Ba}-${c}`]), cx: Ww(t, o, e), cy: Bw(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function Fw({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: i, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const g = xe(), x = (w, j) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: b, domNode: S, connectionMode: E, connectionRadius: D, lib: L, onConnectStart: A, cancelConnection: _, nodeLookup: R, rfId: C, panBy: I, updateConnection: k } = g.getState(), T = j.type === "target", M = (W, O) => {
      p(!1), f?.(W, n, j.type, O);
    }, $ = (W) => l?.(n, W), B = (W, O) => {
      p(!0), d?.(w, n, j.type), A?.(W, O);
    };
    ki.onPointerDown(w.nativeEvent, {
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
      handleDomNode: w.currentTarget
    });
  }, v = (w) => x(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (w) => x(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), N = () => h(!0), y = () => h(!1);
  return s.jsxs(s.Fragment, { children: [(e === !0 || e === "source") && s.jsx(Fa, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: v, onMouseEnter: N, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && s.jsx(Fa, { position: u, centerX: i, centerY: a, radius: t, onMouseDown: m, onMouseEnter: N, onMouseOut: y, type: "target" })] });
}
function Kw({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: i, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: g, edgeTypes: x, noPanClassName: v, onError: m, disableKeyboardA11y: N }) {
  let y = pe((J) => J.edgeLookup.get(e));
  const w = pe((J) => J.defaultEdgeOptions);
  y = w ? { ...w, ...y } : y;
  let j = y.type || "default", b = x?.[j] || Ha[j];
  b === void 0 && (m?.("011", ze.error011(j)), j = "default", b = x?.default || Ha.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), E = typeof f < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), D = !!(y.selectable || o && typeof y.selectable > "u"), L = re(null), [A, _] = F(!1), [R, C] = F(!1), I = xe(), { zIndex: k, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O } = pe(ie((J) => {
    const oe = J.nodeLookup.get(y.source), ue = J.nodeLookup.get(y.target);
    if (!oe || !ue)
      return {
        zIndex: y.zIndex,
        ...Wa
      };
    const V = $m({
      id: e,
      sourceNode: oe,
      targetNode: ue,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
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
      ...V || Wa
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), me), U = de(() => y.markerStart ? `url('#${Ei(y.markerStart, g)}')` : void 0, [y.markerStart, g]), Y = de(() => y.markerEnd ? `url('#${Ei(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || T === null || M === null || $ === null || B === null)
    return null;
  const te = (J) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: ue, multiSelectionActive: V } = I.getState();
    D && (I.setState({ nodesSelectionActive: !1 }), y.selected && V ? (ue({ nodes: [], edges: [y] }), L.current?.blur()) : oe([e])), r && r(J, y);
  }, le = i ? (J) => {
    i(J, { ...y });
  } : void 0, Z = a ? (J) => {
    a(J, { ...y });
  } : void 0, P = c ? (J) => {
    c(J, { ...y });
  } : void 0, q = u ? (J) => {
    u(J, { ...y });
  } : void 0, ae = l ? (J) => {
    l(J, { ...y });
  } : void 0, ce = (J) => {
    if (!N && kl.includes(J.key) && D) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: ue } = I.getState();
      J.key === "Escape" ? (L.current?.blur(), oe({ edges: [y] })) : ue([e]);
    }
  };
  return s.jsx("svg", { style: { zIndex: k }, children: s.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    y.className,
    v,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !D && !r,
      updating: A,
      selectable: D
    }
  ]), onClick: te, onDoubleClick: le, onContextMenu: Z, onMouseEnter: P, onMouseMove: q, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${au}-${g}` : void 0, ref: L, ...y.domAttributes, children: [!R && s.jsx(b, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: D, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: U, markerEnd: Y, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), E && s.jsx(Fw, { edge: y, isReconnectable: E, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: M, targetX: $, targetY: B, sourcePosition: W, targetPosition: O, setUpdateHover: _, setReconnecting: C })] }) });
}
var Xw = be(Kw);
const qw = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Pu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: i, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: x }) {
  const { edgesFocusable: v, edgesReconnectable: m, elementsSelectable: N, onError: y } = pe(qw, me), w = Dw(t);
  return s.jsxs("div", { className: "react-flow__edges", children: [s.jsx(Rw, { defaultColor: e, rfId: n }), w.map((j) => s.jsx(Xw, { id: j, edgesFocusable: v, edgesReconnectable: m, elementsSelectable: N, noPanClassName: r, onReconnect: i, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: x }, j))] });
}
Pu.displayName = "EdgeRenderer";
const Yw = be(Pu), Uw = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Zw({ children: e }) {
  const t = pe(Uw);
  return s.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function Gw(e) {
  const t = cs(), n = re(!1);
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
  return e.connection.inProgress ? { ...e.connection, to: Jt(e.connection.to, e.transform) } : { ...e.connection };
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
function rv({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: i, height: a, isValid: c, inProgress: u } = pe(ov, me);
  return !(i && r && u) ? null : s.jsx("svg", { style: e, width: i, height: a, className: "react-flow__connectionline react-flow__container", children: s.jsx("g", { className: Se(["react-flow__connection", Dl(c)]), children: s.jsx(Ru, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Ru = ({ style: e, type: t = lt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: i, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = nv();
  if (!r)
    return;
  if (n)
    return s.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: i.x, fromY: i.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Dl(o), toNode: d, toHandle: f, pointer: h });
  let g = "";
  const x = {
    sourceX: i.x,
    sourceY: i.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: p
  };
  switch (t) {
    case lt.Bezier:
      [g] = Bl(x);
      break;
    case lt.SimpleBezier:
      [g] = ju(x);
      break;
    case lt.Step:
      [g] = Ho({
        ...x,
        borderRadius: 0
      });
      break;
    case lt.SmoothStep:
      [g] = Ho(x);
      break;
    default:
      [g] = Xl(x);
  }
  return s.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Ru.displayName = "ConnectionLine";
const iv = {};
function Ka(e = iv) {
  re(e), xe(), G(() => {
  }, [e]);
}
function sv() {
  xe(), re(!1), G(() => {
  }, []);
}
function Lu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: i, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: x, connectionLineComponent: v, connectionLineContainerStyle: m, selectionKeyCode: N, selectionOnDrag: y, selectionMode: w, multiSelectionKeyCode: j, panActivationKeyCode: b, zoomActivationKeyCode: S, deleteKeyCode: E, onlyRenderVisibleElements: D, elementsSelectable: L, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, preventScrolling: I, defaultMarkerColor: k, zoomOnScroll: T, zoomOnPinch: M, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: W, zoomOnDoubleClick: O, panOnDrag: U, autoPanOnSelection: Y, onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: P, onPaneScroll: q, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: oe, onEdgeMouseEnter: ue, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, noDragClassName: Te, noWheelClassName: Qe, noPanClassName: Ve, disableKeyboardA11y: z, nodeExtent: H, rfId: K, viewport: X, onViewportChange: se }) {
  return Ka(e), Ka(t), sv(), Gw(n), Qw(X), s.jsx(bw, { onPaneClick: te, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: P, onPaneContextMenu: ae, onPaneScroll: q, paneClickDistance: ce, deleteKeyCode: E, selectionKeyCode: N, selectionOnDrag: y, selectionMode: w, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: b, zoomActivationKeyCode: S, elementsSelectable: L, zoomOnScroll: T, zoomOnPinch: M, zoomOnDoubleClick: O, panOnScroll: $, panOnScrollSpeed: B, panOnScrollMode: W, panOnDrag: U, autoPanOnSelection: Y, defaultViewport: A, translateExtent: _, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: I, noDragClassName: Te, noWheelClassName: Qe, noPanClassName: Ve, disableKeyboardA11y: z, onViewportChange: se, isControlledViewport: !!X, children: s.jsxs(Zw, { children: [s.jsx(Yw, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: we, onReconnectStart: Ae, onReconnectEnd: Ie, onlyRenderVisibleElements: D, onEdgeContextMenu: oe, onEdgeMouseEnter: ue, onEdgeMouseMove: V, onEdgeMouseLeave: ee, reconnectRadius: ge, defaultMarkerColor: k, noPanClassName: Ve, disableKeyboardA11y: z, rfId: K }), s.jsx(rv, { style: x, type: g, component: v, containerStyle: m }), s.jsx("div", { className: "react-flow__edgelabel-renderer" }), s.jsx(_w, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: i, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: Ve, noDragClassName: Te, disableKeyboardA11y: z, nodeExtent: H, rfId: K }), s.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Lu.displayName = "GraphView";
const av = be(Lu), cv = Rl(), Xa = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), m = o ?? t ?? [], N = n ?? e ?? [], y = d ?? [0, 0], w = f ?? En;
  Ul(x, v, m);
  const { nodesInitialized: j } = Ii(N, h, g, {
    nodeOrigin: y,
    nodeExtent: w,
    zIndexMode: p
  });
  let b = [0, 0, 1];
  if (a && r && i) {
    const S = Ln(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: E, y: D, zoom: L } = ts(S, r, i, u, l, c?.padding ?? 0.1);
    b = [E, D, L];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: i ?? 0,
    transform: b,
    nodes: N,
    nodesInitialized: j,
    nodeLookup: h,
    parentLookup: g,
    edges: m,
    edgeLookup: v,
    connectionLookup: x,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: En,
    nodeExtent: w,
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
    connection: { ..._l },
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
    ariaLabelConfig: Al,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, lv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: i, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Nx((h, g) => {
  async function x() {
    const { nodeLookup: v, panZoom: m, fitViewOptions: N, fitViewResolver: y, width: w, height: j, minZoom: b, maxZoom: S } = g();
    m && (await xm({
      nodes: v,
      width: w,
      height: j,
      panZoom: m,
      minZoom: b,
      maxZoom: S
    }, N), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Xa({
      nodes: e,
      edges: t,
      width: r,
      height: i,
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
    setNodes: (v) => {
      const { nodeLookup: m, parentLookup: N, nodeOrigin: y, elevateNodesOnSelect: w, fitViewQueued: j, zIndexMode: b, nodesSelectionActive: S } = g(), { nodesInitialized: E, hasSelectedNodes: D } = Ii(v, m, N, {
        nodeOrigin: y,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: b
      }), L = S && D;
      j && E ? (x(), h({
        nodes: v,
        nodesInitialized: E,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: L
      })) : h({ nodes: v, nodesInitialized: E, nodesSelectionActive: L });
    },
    setEdges: (v) => {
      const { connectionLookup: m, edgeLookup: N } = g();
      Ul(m, N, v), h({ edges: v });
    },
    setDefaultNodesAndEdges: (v, m) => {
      if (v) {
        const { setNodes: N } = g();
        N(v), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: N } = g();
        N(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (v) => {
      const { triggerNodeChanges: m, nodeLookup: N, parentLookup: y, domNode: w, nodeOrigin: j, nodeExtent: b, debug: S, fitViewQueued: E, zIndexMode: D } = g(), { changes: L, updatedInternals: A } = Hm(v, N, y, w, j, b, D);
      A && (Lm(N, y, { nodeOrigin: j, nodeExtent: b, zIndexMode: D }), E ? (x(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), L?.length > 0 && (S && console.log("React Flow: trigger node changes", L), m?.(L)));
    },
    updateNodePositions: (v, m = !1) => {
      const N = [];
      let y = [];
      const { nodeLookup: w, triggerNodeChanges: j, connection: b, updateConnection: S, onNodesChangeMiddlewareMap: E } = g();
      for (const [D, L] of v) {
        const A = w.get(D), _ = !!(A?.expandParent && A?.parentId && L?.position), R = {
          id: D,
          type: "position",
          position: _ ? {
            x: Math.max(0, L.position.x),
            y: Math.max(0, L.position.y)
          } : L.position,
          dragging: m
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
        const { parentLookup: D, nodeOrigin: L } = g(), A = as(N, w, D, L);
        y.push(...A);
      }
      for (const D of E.values())
        y = D(y);
      j(y);
    },
    triggerNodeChanges: (v) => {
      const { onNodesChange: m, setNodes: N, nodes: y, hasDefaultNodes: w, debug: j } = g();
      if (v?.length) {
        if (w) {
          const b = uu(v, y);
          N(b);
        }
        j && console.log("React Flow: trigger node changes", v), m?.(v);
      }
    },
    triggerEdgeChanges: (v) => {
      const { onEdgesChange: m, setEdges: N, edges: y, hasDefaultEdges: w, debug: j } = g();
      if (v?.length) {
        if (w) {
          const b = du(v, y);
          N(b);
        }
        j && console.log("React Flow: trigger edge changes", v), m?.(v);
      }
    },
    addSelectedNodes: (v) => {
      const { multiSelectionActive: m, edgeLookup: N, nodeLookup: y, triggerNodeChanges: w, triggerEdgeChanges: j } = g();
      if (m) {
        const b = v.map((S) => ht(S, !0));
        w(b);
        return;
      }
      w(Rt(y, /* @__PURE__ */ new Set([...v]), !0)), j(Rt(N));
    },
    addSelectedEdges: (v) => {
      const { multiSelectionActive: m, edgeLookup: N, nodeLookup: y, triggerNodeChanges: w, triggerEdgeChanges: j } = g();
      if (m) {
        const b = v.map((S) => ht(S, !0));
        j(b);
        return;
      }
      j(Rt(N, /* @__PURE__ */ new Set([...v]))), w(Rt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: v, edges: m } = {}) => {
      const { edges: N, nodes: y, nodeLookup: w, triggerNodeChanges: j, triggerEdgeChanges: b } = g(), S = v || y, E = m || N, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const _ = w.get(A.id);
        _ && (_.selected = !1), D.push(ht(A.id, !1));
      }
      const L = [];
      for (const A of E)
        A.selected && L.push(ht(A.id, !1));
      j(D), b(L);
    },
    setMinZoom: (v) => {
      const { panZoom: m, maxZoom: N } = g();
      m?.setScaleExtent([v, N]), h({ minZoom: v });
    },
    setMaxZoom: (v) => {
      const { panZoom: m, minZoom: N } = g();
      m?.setScaleExtent([N, v]), h({ maxZoom: v });
    },
    setTranslateExtent: (v) => {
      g().panZoom?.setTranslateExtent(v), h({ translateExtent: v });
    },
    resetSelectedElements: () => {
      const { edges: v, nodes: m, triggerNodeChanges: N, triggerEdgeChanges: y, elementsSelectable: w } = g();
      if (!w)
        return;
      const j = m.reduce((S, E) => E.selected ? [...S, ht(E.id, !1)] : S, []), b = v.reduce((S, E) => E.selected ? [...S, ht(E.id, !1)] : S, []);
      N(j), y(b);
    },
    setNodeExtent: (v) => {
      const { nodes: m, nodeLookup: N, parentLookup: y, nodeOrigin: w, elevateNodesOnSelect: j, nodeExtent: b, zIndexMode: S } = g();
      v[0][0] === b[0][0] && v[0][1] === b[0][1] && v[1][0] === b[1][0] && v[1][1] === b[1][1] || (Ii(m, N, y, {
        nodeOrigin: w,
        nodeExtent: v,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: v }));
    },
    panBy: (v) => {
      const { transform: m, width: N, height: y, panZoom: w, translateExtent: j } = g();
      return Wm({ delta: v, panZoom: w, transform: m, translateExtent: j, width: N, height: y });
    },
    setCenter: async (v, m, N) => {
      const { width: y, height: w, maxZoom: j, panZoom: b } = g();
      if (!b)
        return !1;
      const S = typeof N?.zoom < "u" ? N.zoom : j;
      return await b.setViewport({
        x: y / 2 - v * S,
        y: w / 2 - m * S,
        zoom: S
      }, { duration: N?.duration, ease: N?.ease, interpolate: N?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ..._l }
      });
    },
    updateConnection: (v) => {
      h({ connection: v });
    },
    reset: () => h({ ...Xa() })
  };
}, Object.is);
function uv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: i, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [g] = F(() => lv({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: o,
    width: r,
    height: i,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: p
  }));
  return s.jsx(Ex, { value: g, children: s.jsx(Zx, { children: h }) });
}
function dv({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: i, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Dn(rr) ? s.jsx(s.Fragment, { children: e }) : s.jsx(uv, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: i, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const fv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function pv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: i, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: g, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, onNodeMouseEnter: N, onNodeMouseMove: y, onNodeMouseLeave: w, onNodeContextMenu: j, onNodeDoubleClick: b, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: I, onSelectionDragStop: k, onSelectionContextMenu: T, onSelectionStart: M, onSelectionEnd: $, onBeforeDelete: B, connectionMode: W, connectionLineType: O = lt.Bezier, connectionLineStyle: U, connectionLineComponent: Y, connectionLineContainerStyle: te, deleteKeyCode: le = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: P = !1, selectionMode: q = In.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = An() ? "Meta" : "Control", zoomActivationKeyCode: J = An() ? "Meta" : "Control", snapToGrid: oe, snapGrid: ue, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: ee, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, nodeOrigin: Te = cu, edgesFocusable: Qe, edgesReconnectable: Ve, elementsSelectable: z = !0, defaultViewport: H = Vx, minZoom: K = 0.5, maxZoom: X = 2, translateExtent: se = En, preventScrolling: he = !0, nodeExtent: ye, defaultMarkerColor: je = "#b1b1b7", zoomOnScroll: Ee = !0, zoomOnPinch: Oe = !0, panOnScroll: ke = !1, panOnScrollSpeed: lr = 0.5, panOnScrollMode: pt = xt.Free, zoomOnDoubleClick: ur = !0, panOnDrag: Wn = !0, onPaneClick: It, onPaneMouseEnter: dr, onPaneMouseMove: qe, onPaneMouseLeave: kt, onPaneScroll: fr, onPaneContextMenu: pr, paneClickDistance: hr = 1, nodeClickDistance: st = 0, children: gr, onReconnect: At, onReconnectStart: yr, onReconnectEnd: _t, onEdgeContextMenu: mr, onEdgeDoubleClick: Bn, onEdgeMouseEnter: Fn, onEdgeMouseMove: xr, onEdgeMouseLeave: Qt, reconnectRadius: wr = 10, onNodesChange: vr, onEdgesChange: br, noDragClassName: en = "nodrag", noWheelClassName: Nr = "nowheel", noPanClassName: Kn = "nopan", fitView: Xn, fitViewOptions: qn, connectOnClick: jr, attributionPosition: Sr, proOptions: Cr, defaultEdgeOptions: Er, elevateNodesOnSelect: Ir = !0, elevateEdgesOnSelect: kr = !1, disableKeyboardA11y: Yn = !1, autoPanOnConnect: Ar, autoPanOnNodeDrag: _r, autoPanOnSelection: Dr = !0, autoPanSpeed: Un, connectionRadius: Zn, isValidConnection: Gn, onError: Tr, style: $r, id: Jn, nodeDragThreshold: Mr, connectionDragThreshold: Pr, viewport: Rr, onViewportChange: Lr, width: zr, height: Vr, colorMode: Or = "light", debug: Hr, onScroll: Qn, ariaLabelConfig: Wr, zIndexMode: eo = "basic", ...Br }, Dt) {
  const Tt = Jn || "1", Fr = Bx(Or), Kr = ie((tn) => {
    tn.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Qn?.(tn);
  }, [Qn]);
  return s.jsx("div", { "data-testid": "rf__wrapper", ...Br, onScroll: Kr, style: { ...$r, ...fv }, ref: Dt, className: Se(["react-flow", r, Fr]), id: Jn, role: "application", children: s.jsxs(dv, { nodes: e, edges: t, width: zr, height: Vr, fitView: Xn, fitViewOptions: qn, minZoom: K, maxZoom: X, nodeOrigin: Te, nodeExtent: ye, zIndexMode: eo, children: [s.jsx(Wx, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: g, onConnectEnd: x, onClickConnectStart: v, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: we, nodesConnectable: Ae, nodesFocusable: Ie, edgesFocusable: Qe, edgesReconnectable: Ve, elementsSelectable: z, elevateNodesOnSelect: Ir, elevateEdgesOnSelect: kr, minZoom: K, maxZoom: X, nodeExtent: ye, onNodesChange: vr, onEdgesChange: br, snapToGrid: oe, snapGrid: ue, connectionMode: W, translateExtent: se, connectOnClick: jr, defaultEdgeOptions: Er, fitView: Xn, fitViewOptions: qn, onNodesDelete: L, onEdgesDelete: A, onDelete: _, onNodeDragStart: S, onNodeDrag: E, onNodeDragStop: D, onSelectionDrag: I, onSelectionDragStart: C, onSelectionDragStop: k, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Kn, nodeOrigin: Te, rfId: Tt, autoPanOnConnect: Ar, autoPanOnNodeDrag: _r, autoPanSpeed: Un, onError: Tr, connectionRadius: Zn, isValidConnection: Gn, selectNodesOnDrag: ee, nodeDragThreshold: Mr, connectionDragThreshold: Pr, onBeforeDelete: B, debug: Hr, ariaLabelConfig: Wr, zIndexMode: eo }), s.jsx(av, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: N, onNodeMouseMove: y, onNodeMouseLeave: w, onNodeContextMenu: j, onNodeDoubleClick: b, nodeTypes: i, edgeTypes: a, connectionLineType: O, connectionLineStyle: U, connectionLineComponent: Y, connectionLineContainerStyle: te, selectionKeyCode: Z, selectionOnDrag: P, selectionMode: q, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: V, defaultViewport: H, translateExtent: se, minZoom: K, maxZoom: X, preventScrolling: he, zoomOnScroll: Ee, zoomOnPinch: Oe, zoomOnDoubleClick: ur, panOnScroll: ke, panOnScrollSpeed: lr, panOnScrollMode: pt, panOnDrag: Wn, autoPanOnSelection: Dr, onPaneClick: It, onPaneMouseEnter: dr, onPaneMouseMove: qe, onPaneMouseLeave: kt, onPaneScroll: fr, onPaneContextMenu: pr, paneClickDistance: hr, nodeClickDistance: st, onSelectionContextMenu: T, onSelectionStart: M, onSelectionEnd: $, onReconnect: At, onReconnectStart: yr, onReconnectEnd: _t, onEdgeContextMenu: mr, onEdgeDoubleClick: Bn, onEdgeMouseEnter: Fn, onEdgeMouseMove: xr, onEdgeMouseLeave: Qt, reconnectRadius: wr, defaultMarkerColor: je, noDragClassName: en, noWheelClassName: Nr, noPanClassName: Kn, rfId: Tt, disableKeyboardA11y: Yn, nodeExtent: ye, viewport: Rr, onViewportChange: Lr }), s.jsx(zx, { onSelectionChange: R }), gr, s.jsx($x, { proOptions: Cr, position: Sr }), s.jsx(Tx, { rfId: Tt, disableKeyboardA11y: Yn })] }) });
}
var zu = hu(pv);
const hv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function gv({ children: e }) {
  const t = pe(hv);
  return t ? Cx.createPortal(e, t) : null;
}
function yv({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return s.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, o]) });
}
function mv({ radius: e, className: t }) {
  return s.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
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
function Vu({
  id: e,
  variant: t = ut.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: o,
  lineWidth: r = 1,
  offset: i = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = re(null), { transform: p, patternId: h } = pe(wv, me), g = o || xv[t], x = t === ut.Dots, v = t === ut.Cross, m = Array.isArray(n) ? n : [n, n], N = [m[0] * p[2] || 1, m[1] * p[2] || 1], y = g * p[2], w = Array.isArray(i) ? i : [i, i], j = v ? [y, y] : N, b = [
    w[0] * p[2] || 1 + j[0] / 2,
    w[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return s.jsxs("svg", { className: Se(["react-flow__background", l]), style: {
    ...u,
    ...sr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [s.jsx("pattern", { id: S, x: p[0] % N[0], y: p[1] % N[1], width: N[0], height: N[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${b[0]},-${b[1]})`, children: x ? s.jsx(mv, { radius: y / 2, className: d }) : s.jsx(yv, { dimensions: j, lineWidth: r, variant: t, className: d }) }), s.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Vu.displayName = "Background";
const Ou = be(Vu);
function vv() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: s.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function bv() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: s.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Nv() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: s.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function jv() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: s.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function Sv() {
  return s.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: s.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function lo({ children: e, className: t, ...n }) {
  return s.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const Cv = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function Hu({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: i, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const g = xe(), { isInteractive: x, minZoomReached: v, maxZoomReached: m, ariaLabelConfig: N } = pe(Cv, me), { zoomIn: y, zoomOut: w, fitView: j } = cs(), b = () => {
    y(), i?.();
  }, S = () => {
    w(), a?.();
  }, E = () => {
    j(r), c?.();
  }, D = () => {
    g.setState({
      nodesDraggable: !x,
      nodesConnectable: !x,
      elementsSelectable: !x
    }), u?.(!x);
  }, L = p === "horizontal" ? "horizontal" : "vertical";
  return s.jsxs(ir, { className: Se(["react-flow__controls", L, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? N["controls.ariaLabel"], children: [t && s.jsxs(s.Fragment, { children: [s.jsx(lo, { onClick: b, className: "react-flow__controls-zoomin", title: N["controls.zoomIn.ariaLabel"], "aria-label": N["controls.zoomIn.ariaLabel"], disabled: m, children: s.jsx(vv, {}) }), s.jsx(lo, { onClick: S, className: "react-flow__controls-zoomout", title: N["controls.zoomOut.ariaLabel"], "aria-label": N["controls.zoomOut.ariaLabel"], disabled: v, children: s.jsx(bv, {}) })] }), n && s.jsx(lo, { className: "react-flow__controls-fitview", onClick: E, title: N["controls.fitView.ariaLabel"], "aria-label": N["controls.fitView.ariaLabel"], children: s.jsx(Nv, {}) }), o && s.jsx(lo, { className: "react-flow__controls-interactive", onClick: D, title: N["controls.interactive.ariaLabel"], "aria-label": N["controls.interactive.ariaLabel"], children: x ? s.jsx(Sv, {}) : s.jsx(jv, {}) }), d] });
}
Hu.displayName = "Controls";
const Wu = be(Hu);
function Ev({ id: e, x: t, y: n, width: o, height: r, style: i, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: g, backgroundColor: x } = i || {}, v = a || g || x;
  return s.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: v,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (m) => h(m, e) : void 0 });
}
const Iv = be(Ev), kv = (e) => e.nodes.map((t) => t.id), ui = (e) => e instanceof Function ? e : () => e;
function Av({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: i = Iv,
  onClick: a
}) {
  const c = pe(kv, me), u = ui(t), l = ui(e), d = ui(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return s.jsx(s.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    s.jsx(Dv, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: i, onClick: a, shapeRendering: f }, p)
  )) });
}
function _v({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: i, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((g) => {
    const x = g.nodeLookup.get(e);
    if (!x)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const v = x.internals.userNode, { x: m, y: N } = x.internals.positionAbsolute, { width: y, height: w } = it(v);
    return {
      node: v,
      x: m,
      y: N,
      width: y,
      height: w
    };
  }, me);
  return !l || l.hidden || !Ll(l) ? null : s.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: i, shapeRendering: a, onClick: u, id: l.id });
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
    boundingRect: e.nodeLookup.size > 0 ? Pl(Ln(e.nodeLookup, { filter: Pv }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, Lv = "react-flow__minimap-desc";
function Bu({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: o,
  nodeClassName: r = "",
  nodeBorderRadius: i = 5,
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
  pannable: x = !1,
  zoomable: v = !1,
  ariaLabel: m,
  inversePan: N,
  zoomStep: y = 1,
  offsetScale: w = 5
}) {
  const j = xe(), b = re(null), { boundingRect: S, viewBB: E, rfId: D, panZoom: L, translateExtent: A, flowWidth: _, flowHeight: R, ariaLabelConfig: C } = pe(Rv, me), I = e?.width ?? $v, k = e?.height ?? Mv, T = S.width / I, M = S.height / k, $ = Math.max(T, M), B = $ * I, W = $ * k, O = w * $, U = S.x - (B - S.width) / 2 - O, Y = S.y - (W - S.height) / 2 - O, te = B + O * 2, le = W + O * 2, Z = `${Lv}-${D}`, P = re(0), q = re();
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
      pannable: x,
      zoomStep: y,
      zoomable: v
    });
  }, [x, v, N, y, A, _, R]);
  const ae = h ? (oe) => {
    const [ue, V] = q.current?.pointer(oe) || [0, 0];
    h(oe, { x: ue, y: V });
  } : void 0, ce = g ? ie((oe, ue) => {
    const V = j.getState().nodeLookup.get(ue).internals.userNode;
    g(oe, V);
  }, []) : void 0, J = m ?? C["minimap.ariaLabel"];
  return s.jsx(ir, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: s.jsxs("svg", { width: I, height: k, viewBox: `${U} ${Y} ${te} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: b, onClick: ae, children: [J && s.jsx("title", { id: Z, children: J }), s.jsx(Tv, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: i, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), s.jsx("path", { className: "react-flow__minimap-mask", d: `M${U - O},${Y - O}h${te + O * 2}v${le + O * 2}h${-te - O * 2}z
        M${E.x},${E.y}h${E.width}v${E.height}h${-E.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
Bu.displayName = "MiniMap";
const Fu = be(Bu), zv = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Vv = {
  [Yt.Line]: "right",
  [Yt.Handle]: "bottom-right"
};
function Ov({ nodeId: e, position: t, variant: n = Yt.Handle, className: o, style: r = void 0, children: i, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: g, onResizeStart: x, onResize: v, onResizeEnd: m }) {
  const N = xu(), y = typeof e == "string" ? e : N, w = xe(), j = re(null), b = n === Yt.Handle, S = pe(ie(zv(b && h), [b, h]), me), E = re(null), D = t ?? Vv[n];
  G(() => {
    if (!(!j.current || !y))
      return E.current || (E.current = ux({
        domNode: j.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: A, transform: _, snapGrid: R, snapToGrid: C, nodeOrigin: I, domNode: k } = w.getState();
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
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: I, nodeOrigin: k } = w.getState(), T = [], M = { x: A.x, y: A.y }, $ = C.get(y);
          if ($ && $.expandParent && $.parentId) {
            const B = $.origin ?? k, W = A.width ?? $.measured.width ?? 0, O = A.height ?? $.measured.height ?? 0, U = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: W,
                height: O,
                ...zl({
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
          w.getState().triggerNodeChanges([R]);
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
        onResizeStart: x,
        onResize: v,
        onResizeEnd: m,
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
    x,
    v,
    m,
    g
  ]);
  const L = D.split("-");
  return s.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...L, n, o]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [b ? "backgroundColor" : "borderColor"]: a }
  }, children: i });
}
be(Ov);
function Hv(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Tn(e.state),
    layout: e.layout
  };
}
function Wv(e) {
  return JSON.stringify(
    {
      state: Tn(e.state),
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
      state: qo(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function Fv(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), i = document.createElement("a");
  i.href = r, i.download = `${n}.json`, document.body.appendChild(i), i.click(), i.remove(), URL.revokeObjectURL(r);
}
function qa({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: o,
  onChange: r
}) {
  const i = (a) => {
    t || r({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ s.jsx(
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
      onChange: i
    }
  );
}
function Kv({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: i,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = de(
    () => d ? Nd(d) : null,
    [d]
  );
  return /* @__PURE__ */ s.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": o,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ s.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ s.jsx("span", { children: l }),
          /* @__PURE__ */ s.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ s.jsx(jd, { fallback: /* @__PURE__ */ s.jsx(
          qa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ), children: /* @__PURE__ */ s.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ) }) : /* @__PURE__ */ s.jsx(
          qa,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: c
          }
        ),
        /* @__PURE__ */ s.jsx(Xv, { diagnostics: u })
      ]
    }
  );
}
function Xv({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ s.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = qv(t);
    return /* @__PURE__ */ s.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${o}`,
        children: [
          t.code ? /* @__PURE__ */ s.jsx("span", { children: t.code }) : null,
          r ? /* @__PURE__ */ s.jsx("small", { children: r }) : null,
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
  const n = de(() => Wv(e), [e]), [o, r] = F(n), [i, a] = F(n), [c, u] = F(null);
  G(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== i, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(o));
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ s.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ s.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(i), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ s.jsx(Zt, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ s.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ s.jsx(
      Kv,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: Yv,
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
function Ke(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function Ku(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), o = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(o) || o < n) return "";
  const r = Math.round((o - n) / 1e3);
  if (r < 60) return `${r}s`;
  const i = Math.floor(r / 60), a = r % 60;
  if (i < 60) return a ? `${i}m ${a}s` : `${i}m`;
  const c = Math.floor(i / 60), u = i % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function Bo(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function us(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ s.jsx(Ec, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ s.jsx(Vi, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ s.jsx(kd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ s.jsx(Ot, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ s.jsx(Id, { size: 15 });
    default:
      return /* @__PURE__ */ s.jsx(Ko, { size: 15 });
  }
}
const Zv = ["Single", "Array", "List", "HashSet"];
function Xu(e) {
  const [t, n] = F(null), [o, r] = F(null);
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
        u || r(l);
      },
      () => {
        u || r([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const i = de(
    () => t && t.length > 0 ? t.map((u) => {
      const l = As(u);
      return {
        value: l,
        label: Mc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: rf(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = As(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: i, storageOptions: a, editorForAlias: c };
}
function Gv(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function Jv(e, t, n) {
  return {
    add: () => {
      const o = Yd(n.namePrefix, e.map((r) => gn(r, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, r) => t(e.map((i, a) => a === o ? n.patch(i, r) : i)),
    remove: (o) => t(e.filter((r, i) => i !== o))
  };
}
function Ya({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: r, onChange: i }) {
  if (!t)
    return /* @__PURE__ */ s.jsx(
      "input",
      {
        type: "text",
        "aria-label": r,
        value: e,
        placeholder: n,
        onChange: (l) => i(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ s.jsxs("select", { "aria-label": r, value: e, onChange: (l) => i(l.target.value), children: [
    o ? /* @__PURE__ */ s.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ s.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ s.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ s.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ s.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const Qv = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function e0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ s.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: Zv.map((o) => /* @__PURE__ */ s.jsx("option", { value: o, children: Qv[o] }, o)) });
}
function t0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function n0({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const r = t0(t, e);
  return r && t === "checkbox" ? /* @__PURE__ */ s.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (i) => o(i.target.checked ? "true" : "false")
    }
  ) : r && (t === "number" || t === "date") ? /* @__PURE__ */ s.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (i) => o(i.target.value)
    }
  ) : /* @__PURE__ */ s.jsx(
    "input",
    {
      type: "text",
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      disabled: t === "none",
      onChange: (i) => o(i.target.value)
    }
  );
}
function o0({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: r, onAdd: i, children: a }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ s.jsx("h3", { children: e }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-properties-add", onClick: i, children: [
        /* @__PURE__ */ s.jsx(Ht, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    r ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ s.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ s.jsx("thead", { children: /* @__PURE__ */ s.jsxs("tr", { children: [
        o.map((c) => /* @__PURE__ */ s.jsx("th", { children: c }, c)),
        /* @__PURE__ */ s.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ s.jsx("tbody", { children: a })
    ] })
  ] });
}
function r0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ s.jsx(wn, { size: 14 }) }) });
}
function i0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ s.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (o) => n(o.target.checked) });
}
function ds({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: o,
  namePrefix: r,
  nameKeys: i,
  title: a,
  addLabel: c,
  emptyLabel: u,
  create: l,
  patch: d,
  columns: f,
  warnings: p,
  onChange: h
}) {
  const { add: g, update: x, remove: v } = Jv(e, h, {
    namePrefix: r,
    nameKeys: i,
    create: (y) => l(y, Gv(t)),
    patch: d
  }), m = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], N = r.toLowerCase();
  return /* @__PURE__ */ s.jsx(
    o0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, w) => {
        const j = gn(y, i), b = $c(y), S = gn(y, Rc), E = S ? p?.get(S) : void 0, D = b.collectionKind === "Single" ? o(b.alias) : "text";
        return /* @__PURE__ */ s.jsxs("tr", { children: [
          /* @__PURE__ */ s.jsxs("td", { children: [
            /* @__PURE__ */ s.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (L) => x(w, { name: L.target.value }) }),
            E ? /* @__PURE__ */ s.jsx("span", { className: "wf-properties-warning", role: "note", title: E, children: E }) : null
          ] }),
          /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx(
            Ya,
            {
              ariaLabel: `${r} type`,
              value: b.alias,
              options: t,
              placeholder: "Type",
              onChange: (L) => x(w, { type: { alias: L, collectionKind: b.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx(
            e0,
            {
              ariaLabel: `${r} collection kind`,
              value: b.collectionKind,
              onChange: (L) => x(w, { type: { alias: b.alias, collectionKind: L } })
            }
          ) }),
          f.default ? /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx(
            n0,
            {
              ariaLabel: `${r} default value`,
              value: Jd(y.default),
              editor: D,
              onChange: (L) => x(w, { default: Gd(L) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx(
            Ya,
            {
              ariaLabel: `${r} storage driver`,
              value: gn(y, af),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (L) => x(w, { storageDriverType: L || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ s.jsx("td", { children: /* @__PURE__ */ s.jsx(
            i0,
            {
              ariaLabel: `${r} required`,
              checked: y.isRequired === !0,
              onChange: (L) => x(w, { isRequired: L })
            }
          ) }) : null,
          /* @__PURE__ */ s.jsx(r0, { label: `Remove ${N} ${j || w + 1}`, onRemove: () => v(w) })
        ] }, w);
      })
    }
  );
}
function qu({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: r = "Variables", addLabel: i = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ s.jsx(
    ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Variable",
      nameKeys: sf,
      title: r,
      addLabel: i,
      emptyLabel: a,
      create: (l, d) => Ud({ name: l, alias: d }),
      patch: (l, d) => Zd(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function s0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ s.jsx(
    ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: Pc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (i, a) => Qd({ name: i, alias: a }),
      patch: (i, a) => ef(i, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function a0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ s.jsx(
    ds,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: Pc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (i, a) => tf({ name: i, alias: a }),
      patch: (i, a) => nf(i, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function bo(e) {
  return (e ?? []).filter(Io);
}
function c0({ context: e, variables: t, title: n, addLabel: o, emptyLabel: r, warnings: i, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = Xu(e);
  return /* @__PURE__ */ s.jsx(
    qu,
    {
      items: bo(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: o,
      emptyLabel: r,
      warnings: i,
      onChange: a
    }
  );
}
function l0({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [r, i] = F(e?.name ?? ""), [a, c] = F(e?.description ?? "");
  G(() => {
    i(e?.name ?? "");
  }, [e?.name]), G(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const d = r.trim();
    d && d !== (e?.name ?? "") ? n?.({ name: d }) : d || i(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ s.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ s.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ s.jsx("dt", { children: /* @__PURE__ */ s.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ s.jsx("dd", { children: o ? /* @__PURE__ */ s.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: r,
          onChange: (d) => i(d.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ s.jsx("dt", { children: /* @__PURE__ */ s.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ s.jsx("dd", { children: o ? /* @__PURE__ */ s.jsx(
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
      ) : e?.description?.trim() ? e.description : /* @__PURE__ */ s.jsx("span", { className: "wf-muted", children: "No description" }) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Definition ID" }),
      /* @__PURE__ */ s.jsx("dd", { children: /* @__PURE__ */ s.jsx("code", { children: t }) })
    ] })
  ] });
}
function u0({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: r }) {
  const { typeOptions: i, storageOptions: a, editorForAlias: c } = Xu(n), u = bo(t.state.variables), l = bo(t.state.inputs), d = bo(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ s.jsx(
      l0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ s.jsx(
      qu,
      {
        items: u,
        typeOptions: i,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ s.jsx(
      s0,
      {
        items: l,
        typeOptions: i,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ s.jsx(
      a0,
      {
        items: d,
        typeOptions: i,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => o((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ s.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ s.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ s.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ s.jsxs("li", { children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ s.jsx("time", { children: Ke(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const Ua = "application/x-elsa-activity-version-id", d0 = 6, f0 = 1200, p0 = 250, h0 = [10, 25, 50], g0 = 10, Za = "elsa-studio-workflow-palette-width", Ga = "elsa-studio-workflow-inspector-width", Ja = "elsa-studio-workflow-palette-collapsed", Qa = "elsa-studio-workflow-inspector-collapsed", Yu = "elsa-studio-workflow-side-panel-maximized", ln = 180, un = 460, y0 = 260, dn = 260, fn = 560, m0 = 320, ec = 42, uo = 16, Uu = Ze.createContext(null), Zu = Ze.createContext(null);
function x0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function Gu(e, t) {
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
      const c = JSON.parse(a.trim()), u = tc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], i = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return tc(r, i);
}
function tc(e, t) {
  const n = typeof e == "string" ? nc(e) : void 0, o = typeof t == "string" ? nc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function nc(e) {
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
function _i(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, i) => Ce(r).localeCompare(Ce(i)))
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
function Ju(e) {
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
  return oc(t) - oc(e);
}
function oc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Qu(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function ed(e) {
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
    fo(t, n.typeName, n), fo(t, n.name, n), fo(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    fo(t, o, n);
  }
  return t;
}
function T0(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(pn(o?.activityTypeKey)) ?? n.get(pn(Bo(o?.activityTypeKey))) ?? n.get(pn(o?.displayName)) ?? n.get(pn(e.activityVersionId)) ?? null;
}
function fo(e, t, n) {
  const o = pn(t);
  o && !e.has(o) && e.set(o, n);
}
function pn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function rc(e, t, n, o) {
  const r = ar();
  if (!r) return t;
  const i = r.getItem(e);
  if (i == null) return t;
  const a = Number(i);
  return Number.isFinite(a) ? No(a, n, o) : t;
}
function ic(e, t) {
  const n = ar();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function $0() {
  const e = ar();
  if (!e) return null;
  const t = e.getItem(Yu);
  return t === "palette" || t === "inspector" ? t : null;
}
function ar() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function sn(e, t) {
  const n = ar();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function No(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function td(e) {
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
  const o = t.find((i) => i.activityVersionId === n.activityVersionId);
  if (Wi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = vn(n, []);
  return new Set(r?.slot.activities.map((i) => i.nodeId) ?? [n.nodeId]);
}
function sc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function ac(e) {
  return `${Ce(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function cc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function R0(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function nd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function L0(e) {
  const t = nd(e);
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
  return id(Re(e));
}
function od(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ce(o) : void 0
  });
  for (const r of De(e))
    for (const i of r.activities) od(i, t, n);
  return n;
}
function rd(e, t = []) {
  if (!e) return t;
  for (const n of Kc(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of De(e))
    for (const o of n.activities) rd(o, t);
  return t;
}
function hn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function O0(e) {
  return `${e.id}-${id(JSON.stringify(e.state))}`;
}
function id(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function fs(e) {
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
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function On({ status: e, subStatus: t }) {
  return /* @__PURE__ */ s.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const sd = { workflowActivity: F0 }, ad = { workflow: X0 };
function F0({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, i = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = K0(n), u = Ze.useContext(Zu)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ s.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ s.jsx(Ut, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ s.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${_o(u.state)}`, children: /* @__PURE__ */ s.jsx(So, { size: 13 }) }) : null,
        /* @__PURE__ */ s.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ s.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: us(n.icon) }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ s.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ s.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-slot-badge", children: [
          n.childSlots.length,
          " slot",
          n.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ s.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ s.jsx(On, { status: o.status, subStatus: o.subStatus }) : null,
          o.incidentCount > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.incidentCount,
            " incident",
            o.incidentCount === 1 ? "" : "s"
          ] }) : null,
          o.faultCount > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-node-runtime-count", children: [
            o.faultCount,
            " faults"
          ] }) : null
        ] }) : null,
        i.map((l, d) => {
          const f = `${(d + 1) / (i.length + 1) * 100}%`;
          return /* @__PURE__ */ s.jsxs(Ze.Fragment, { children: [
            /* @__PURE__ */ s.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ s.jsx(Ut, { type: "source", position: ne.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function K0(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function X0(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: o,
    targetX: r,
    targetY: i,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Ze.useContext(Uu), [h, g] = F(!1), [x, v, m] = Ho({ sourceX: n, sourceY: o, targetX: r, targetY: i, sourcePosition: a, targetPosition: c }), N = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsx(
      Vn,
      {
        id: t,
        path: x,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: N ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: v,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    p ? /* @__PURE__ */ s.jsx(gv, { children: /* @__PURE__ */ s.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", N ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${v}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => p.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ s.jsx(Ht, { size: 12 }) }),
          /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ s.jsx(wn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function q0({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [i, a] = F(""), [c, u] = F(0), l = re(null), d = re(null), f = de(() => {
    const N = i.trim().toLowerCase(), y = n.filter(C0);
    return N ? y.filter((w) => Ce(w).toLowerCase().includes(N) || w.activityTypeKey.toLowerCase().includes(N) || (w.category ?? "").toLowerCase().includes(N) || (w.description ?? "").toLowerCase().includes(N)) : y;
  }, [n, i]), p = de(() => _i(f), [f]), h = de(() => p.flatMap((N) => N.activities), [p]);
  G(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), G(() => {
    const N = (w) => {
      l.current?.contains(w.target) || r();
    }, y = (w) => {
      w.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", N, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", N, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
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
  }, x = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), v = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ s.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: x, top: v }, onMouseDown: (N) => N.stopPropagation(), onClick: (N) => N.stopPropagation(), children: [
    /* @__PURE__ */ s.jsx(
      "input",
      {
        ref: d,
        type: "search",
        value: i,
        placeholder: "Search activities...",
        "aria-label": "Search activities",
        onChange: (N) => {
          a(N.target.value), u(0);
        },
        onKeyDown: g
      }
    ),
    /* @__PURE__ */ s.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ s.jsx("p", { children: "No matching activities." }) : p.map((N) => /* @__PURE__ */ s.jsxs("section", { children: [
      /* @__PURE__ */ s.jsx("h4", { children: N.category }),
      N.activities.map((y) => {
        m += 1;
        const w = m, j = w === c;
        return /* @__PURE__ */ s.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": j,
            className: j ? "active" : "",
            onMouseEnter: () => u(w),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ s.jsx("strong", { children: Ce(y) }),
              /* @__PURE__ */ s.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, N.category)) })
  ] });
}
function jo({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  const r = Od(t.map((i) => i.id), n, o);
  return /* @__PURE__ */ s.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: r, children: t.map((i) => {
    const a = i.id === n;
    return /* @__PURE__ */ s.jsxs(
      "button",
      {
        "data-tab-id": i.id,
        type: "button",
        role: "tab",
        "aria-selected": a,
        tabIndex: a ? 0 : -1,
        className: a ? "active" : "",
        title: i.title,
        onClick: () => o(i.id),
        children: [
          i.icon ? /* @__PURE__ */ s.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: i.icon }) : null,
          /* @__PURE__ */ s.jsx("span", { children: i.title })
        ]
      },
      i.id
    );
  }) });
}
function lc(e, t) {
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
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...r] = t, i = n === "$workflow";
  return {
    nodeId: !n || i ? null : n,
    isWorkflowScope: i,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: r.length > 0 ? r.join("/") : null
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
    return /* @__PURE__ */ s.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ s.jsx(Zt, { size: 14 }),
      " No validation errors"
    ] });
  const o = J0(n), r = new Map(o.map((i) => [i.error, i]));
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ s.jsx(ft, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      o.length > 0 ? /* @__PURE__ */ s.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        o.length,
        " invalid variable reference",
        o.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ s.jsx("ul", { className: "wf-validation-list", children: n.map((i, a) => {
      const c = r.get(i);
      return /* @__PURE__ */ s.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ s.jsx("span", { className: "wf-validation-message", children: i.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ s.jsx(Ad, { size: 12 }),
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
  const n = fs(e);
  return /* @__PURE__ */ s.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ s.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ s.jsx(ft, { size: 16 }) : /* @__PURE__ */ s.jsx(Zt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function tb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ s.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = fs(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ s.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ s.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ s.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ s.jsx(On, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ s.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ s.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ s.jsx(ft, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    /* @__PURE__ */ s.jsxs("dl", { className: "wf-runtime-meta", children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Dispatch" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.commandDispatchStatus ?? e.status, children: e.commandDispatchStatus ?? e.status })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Test Run" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.testRunId, children: e.testRunId })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.artifactId ?? "None", children: e.artifactId ?? "None" })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Run / Instance" }),
        /* @__PURE__ */ s.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => t(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ s.jsx("dd", { children: uc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ s.jsx("dd", { children: uc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ s.jsx("dd", { title: e.expiresAt ? Ke(e.expiresAt) : "None", children: e.expiresAt ? Ke(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function uc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ps({ rows: e = 5 }) {
  return /* @__PURE__ */ s.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ s.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function hs({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ s.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ s.jsx(Ko, { size: 22 }) }),
    /* @__PURE__ */ s.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ s.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function Hn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ s.jsx(ft, { size: 18 }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ s.jsx("strong", { children: t }),
      /* @__PURE__ */ s.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function cd({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ s.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ s.jsx(Zt, { size: n ? 13 : 14 }),
    /* @__PURE__ */ s.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function Vt({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const i = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await A0(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ s.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    i(a);
  }, children: /* @__PURE__ */ s.jsx(_d, { size: 12 }) });
}
function nb({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, i] = F("loading"), [a, c] = F(""), [u, l] = F(""), [d, f] = F(null), [p, h] = F([]), g = n?.trim().toLowerCase() ?? "", x = de(
    () => g ? p.filter((S) => E0(S, g)) : p,
    [g, p]
  ), v = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, E) => S.localeCompare(E)),
    [p]
  ), m = St(t, "weaver.workflows.explain-executable"), N = ie(async () => {
    i("loading"), c("");
    try {
      h(await Zc(e)), i("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), i("failed");
    }
  }, [e]);
  G(() => {
    N();
  }, [N]);
  const y = async (S) => {
    l(""), f(null), c("");
    try {
      const E = await Uc(e, S.artifactId), D = ed(E);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, w = (S) => {
    m && Ct(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
  }, j = (S) => {
    c(""), f(null), l(`Copied ${S}`);
  }, b = (S) => {
    l(""), f(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        N();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ s.jsx(Fo, { size: 14 }),
        /* @__PURE__ */ s.jsx(
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
      /* @__PURE__ */ s.jsx("datalist", { id: "wf-executable-definition-options", children: v.map((S) => /* @__PURE__ */ s.jsx("option", { value: S }, S)) }),
      n ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(null), children: [
        /* @__PURE__ */ s.jsx(Ic, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ s.jsx(Hn, { message: a }) : null,
    u ? /* @__PURE__ */ s.jsx(cd, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ s.jsx(ps, {}) : null,
    r === "ready" && x.length === 0 ? /* @__PURE__ */ s.jsx(
      hs,
      {
        icon: /* @__PURE__ */ s.jsx(Ot, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && x.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ s.jsx("span", { children: "Version" }),
        /* @__PURE__ */ s.jsx("span", { children: "Source" }),
        /* @__PURE__ */ s.jsx("span", { children: "Root" }),
        /* @__PURE__ */ s.jsx("span", { children: "Published" }),
        /* @__PURE__ */ s.jsx("span", { children: "Actions" })
      ] }),
      x.map((S) => /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ s.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ s.jsx(Vt, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: b })
          ] }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ s.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ s.jsx(Vt, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: b })
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ s.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ s.jsx(Vt, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: b })
        ] }),
        /* @__PURE__ */ s.jsx(ob, { executable: S, onCopied: j, onCopyFailed: b }),
        /* @__PURE__ */ s.jsx("span", { children: Ju(S) }),
        /* @__PURE__ */ s.jsx("span", { children: Ke(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ s.jsx(Ot, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => w(S), children: [
            /* @__PURE__ */ s.jsx(ot, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function ob({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ s.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ s.jsx("span", { className: "wf-source-kind", children: Qu(e.sourceKind) }),
    o ? /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ s.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ s.jsx(Vt, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ s.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function rb({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, i] = F("loading"), [a, c] = F(""), [u, l] = F(""), [d, f] = F(null), [p, h] = F([]), g = St(t, "weaver.workflows.explain-executable"), x = ie(async () => {
    i("loading"), c("");
    try {
      const j = await Zc(e);
      h(j.filter((b) => I0(b, n)).sort(k0)), i("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), i("failed");
    }
  }, [e, n]);
  G(() => {
    x();
  }, [x, o]);
  const v = async (j) => {
    l(""), f(null), c("");
    try {
      const b = await Uc(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: ed(b) }), l(`Started ${j.artifactId}`);
    } catch (b) {
      c(b instanceof Error ? b.message : String(b));
    }
  }, m = (j) => {
    g && Ct(t, g, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
  }, N = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (j) => {
    c(""), f(null), l(`Copied ${j}`);
  }, w = (j) => {
    l(""), f(null), c(`Could not copy ${j}.`);
  };
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ s.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
        x();
      }, children: [
        /* @__PURE__ */ s.jsx(Oi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: N, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ s.jsx(ft, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ s.jsx(cd, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && p.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && p.length > 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((j) => /* @__PURE__ */ s.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": j.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            j.artifactVersion
          ] }),
          j.artifactId === o ? /* @__PURE__ */ s.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ s.jsx("span", { children: Ke(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ s.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ s.jsx(Vt, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: w })
        ] }),
        /* @__PURE__ */ s.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ s.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ s.jsx(Vt, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: w })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("dl", { children: [
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ s.jsxs("dd", { children: [
            Qu(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ s.jsxs("div", { children: [
          /* @__PURE__ */ s.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ s.jsx("dd", { children: Ju(j) })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
          v(j);
        }, children: [
          /* @__PURE__ */ s.jsx(Ot, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ s.jsx(ot, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function ib() {
  const [e, t] = F(() => rc(Za, y0, ln, un)), [n, o] = F(() => rc(Ga, m0, dn, fn)), [r, i] = F(() => ic(Ja, !1)), [a, c] = F(() => ic(Qa, !1)), [u, l] = F($0);
  G(() => {
    sn(Za, String(e));
  }, [e]), G(() => {
    sn(Ga, String(n));
  }, [n]), G(() => {
    sn(Ja, String(r));
  }, [r]), G(() => {
    sn(Qa, String(a));
  }, [a]), G(() => {
    sn(Yu, u);
  }, [u]), G(() => {
    if (!u) return;
    const y = (w) => {
      w.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [u]);
  const d = ie((y) => {
    l((w) => w === y ? null : w), y === "palette" ? i((w) => !w) : c((w) => !w);
  }, []), f = ie((y) => {
    y === "palette" ? i(!1) : c(!1), l((w) => w === y ? null : y);
  }, []), p = ie((y, w) => {
    l(null), y === "palette" ? (i(!1), t((j) => No(j + w, ln, un))) : (c(!1), o((j) => No(j + w, dn, fn)));
  }, []), h = ie((y, w) => {
    w.preventDefault(), l(null), y === "palette" ? i(!1) : c(!1);
    const j = w.clientX, b = y === "palette" ? e : n, S = y === "palette" ? ln : dn, E = y === "palette" ? un : fn;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const _ = y === "palette" ? A.clientX - j : j - A.clientX, R = No(b + _, S, E);
      y === "palette" ? t(R) : o(R);
    }, L = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", L), window.removeEventListener("pointercancel", L);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", L), window.addEventListener("pointercancel", L);
  }, [n, e]), g = ie((y, w) => {
    w.key === "ArrowLeft" ? (w.preventDefault(), p(y, y === "palette" ? -uo : uo)) : w.key === "ArrowRight" ? (w.preventDefault(), p(y, y === "palette" ? uo : -uo)) : w.key === "Home" ? (w.preventDefault(), y === "palette" ? t(ln) : o(dn)) : w.key === "End" && (w.preventDefault(), y === "palette" ? t(un) : o(fn));
  }, [p]), x = !r && u !== "inspector", v = !a && u !== "palette", m = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), N = {
    "--wf-palette-width": `${r ? ec : e}px`,
    "--wf-inspector-width": `${a ? ec : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: x,
    inspectorExpanded: v,
    editorBodyClassName: m,
    editorBodyStyle: N,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: g
  };
}
const sb = 50;
function dc() {
  return { past: [], future: [] };
}
function ab(e) {
  return e.past.length > 0;
}
function cb(e) {
  return e.future.length > 0;
}
function fc(e, t, n = sb) {
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
  const n = re(dc()), o = re(null), r = re(""), i = re(!1), [a, c] = F(0), u = ie((x) => {
    n.current = dc(), o.current = x ? hn(x) : null, r.current = x ? Re(x) : "", i.current = !1, c(0);
  }, []);
  G(() => {
    if (!e) return;
    if (i.current) {
      i.current = !1;
      return;
    }
    const x = Re(e);
    if (x === r.current) return;
    const v = window.setTimeout(() => {
      const m = o.current;
      m && (n.current = fc(n.current, m), c((N) => N + 1)), o.current = hn(e), r.current = x;
    }, p0);
    return () => window.clearTimeout(v);
  }, [e]);
  const l = ie(() => {
    if (!e) return;
    const x = Re(e);
    if (x === r.current) return;
    const v = o.current;
    v && (n.current = fc(n.current, v)), o.current = hn(e), r.current = x;
  }, [e]), d = ie((x) => {
    i.current = !0, o.current = hn(x), r.current = Re(x), t(x), c((v) => v + 1);
  }, [t]), f = ie(() => {
    if (!e) return;
    l();
    const x = lb(n.current, e);
    x && (n.current = x.history, d(x.snapshot));
  }, [e, l, d]), p = ie(() => {
    if (!e) return;
    l();
    const x = ub(n.current, e);
    x && (n.current = x.history, d(x.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: g } = de(() => {
    const x = !!e && !!o.current && Re(e) !== r.current;
    return {
      canUndoNow: ab(n.current) || x,
      canRedoNow: cb(n.current) && !x
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
  const [e, t] = Sd(pb, fb), n = de(() => ({
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
    enterSlot(o, r, i) {
      t({ type: "slotEntered", frame: { ownerNodeId: o.nodeId, slotId: r, label: i } });
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
  const o = new Set(e.map((d) => d.id)), r = t.filter((d) => o.has(d.source) && o.has(d.target)), i = /* @__PURE__ */ new Set();
  for (const d of r)
    i.add(d.source), i.add(d.target);
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
  const c = Math.max(0, ...e.filter((d) => i.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = i.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const f = i.has(d.id) ? a.get(d.id) ?? 0 : u, p = l.get(f);
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
  catalogByVersion: r,
  isUnsupportedDesigner: i,
  isFlowchartDesigner: a,
  canAddActivitiesToCanvas: c,
  selectedNodeId: u,
  editDraft: l,
  editDraftAndSelect: d,
  select: f,
  setStatus: p,
  setError: h
}) {
  const [g, x] = F([]), [v, m] = F([]), [N, y] = F(null), [w, j] = F(null), [b, S] = F(null), E = re(null), D = re(null), L = re(null), A = re(null), _ = re(!1);
  G(() => {
    if (!n) {
      x([]), m([]);
      return;
    }
    const z = i ? hi(n, o, e?.layout ?? []) : t ? Hc(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    x(z.nodes), m(z.edges);
  }, [o, e?.layout, i, t, n]);
  const R = ie((z, H) => {
    if (e?.state.rootActivity && i)
      return;
    const K = yi(z, ac(z));
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
        const he = se.state.rootActivity, ye = gi(K, [], [he]), je = H ? [
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
      const he = vn(X.state.rootActivity, se);
      if (!he) return null;
      const ye = gi(X.state.rootActivity, se, [...he.slot.activities, K]), je = H ? [
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
  }, [e?.state.rootActivity, i, t, d, h, p]), C = ie((z, H) => {
    const K = yi(z, ac(z)), X = {
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
        sourcePorts: Xc(K, z)
      }
    };
    return { activityNode: K, node: X };
  }, []), I = ie((z, H, K = []) => {
    i || l(({ draft: X, frames: se }) => {
      if (!X) return null;
      const he = jf(X.layout, z), ye = X.state.rootActivity;
      if (!ye) return { ...X, layout: he };
      const je = vn(ye, se);
      if (!je) return { ...X, layout: he };
      const Ee = bf(je, z, H, K), Oe = je.slot.mode === "flowchart" ? Nf(Ee, H) : Ee;
      return {
        ...X,
        layout: he,
        state: {
          ...X.state,
          rootActivity: Wc(ye, se, Oe)
        }
      };
    });
  }, [i, l]), k = ie((z, H) => {
    if (!E.current) return null;
    const K = E.current.getBoundingClientRect();
    return N ? N.screenToFlowPosition({ x: z, y: H }) : {
      x: z - K.left,
      y: H - K.top
    };
  }, [N]), T = ie((z, H) => document.elementFromPoint(z, H)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), M = ie((z, H, K) => {
    const X = g.find((ke) => ke.id === H.source), se = g.find((ke) => ke.id === H.target), he = X && se ? R0(X, se) : X ? cc(X) : K, ye = C(z, he), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], Oe = Tf(v, H, ye.node.id);
    x(Ee), m(Oe), f(ye.node.id), I(Ee, Oe, [ye.activityNode]);
  }, [I, C, v, g, f]), $ = ie((z, H, K) => {
    if (!c || !E.current) return !1;
    const X = E.current.getBoundingClientRect();
    if (!(H >= X.left && H <= X.right && K >= X.top && K <= X.bottom)) return !1;
    const he = k(H, K);
    if (!he) return !1;
    if (a) {
      const ye = T(H, K), je = ye ? v.find((Ee) => Ee.id === ye) : void 0;
      if (je)
        return M(z, je, he), !0;
    }
    return R(z, he), !0;
  }, [R, c, v, T, a, M, k]);
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
    A.current = { activityVersionId: H.activityVersionId, handledDrop: !1 }, z.dataTransfer.setData(Ua, H.activityVersionId), z.dataTransfer.setData("text/plain", H.activityVersionId), z.dataTransfer.effectAllowed = "copy";
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
    const H = z.dataTransfer.getData(Ua) || z.dataTransfer.getData("text/plain");
    if (!H || (z.stopPropagation(), A.current?.activityVersionId === H && (A.current.handledDrop = !0), !c)) return;
    const K = r.get(H);
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
    const H = i ? z.filter((K) => K.type === "select") : z;
    H.length !== 0 && x((K) => uu(H, K));
  }, q = (z) => {
    i || m((H) => du(z, H));
  }, ae = (z) => !z.source || !z.target || z.source === z.target || !a ? !1 : !z.targetHandle, ce = (z) => {
    if (!e?.state.rootActivity || !t || !a || !ae(z)) return;
    const H = ko(z.source, z.target, z.sourceHandle ?? "Done", z.targetHandle ?? void 0), K = pu(H, v);
    m(K), I(g, K);
  }, J = () => {
    I(g, v);
  }, oe = !i && g.length > 0, ue = ie(() => {
    if (i || g.length === 0) return;
    const z = t?.slot.mode === "sequence" ? "sequence" : "flowchart", H = mb(g, v, z), K = g.map((X) => {
      const se = H.get(X.id);
      return se ? { ...X, position: se } : X;
    });
    x(K), I(K, v), window.requestAnimationFrame(() => N?.fitView({ padding: 0.2 })), p("Rearranged the canvas.");
  }, [v, g, t, i, I, N, p]), V = (z, H) => {
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
    const X = nd(z);
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
    }, v, { shouldReplaceId: !1 });
    m(K), I(g, K);
  }, we = (z) => {
    if (i || z.length === 0) return;
    const H = new Set(z.map((se) => se.id)), K = g.filter((se) => !H.has(se.id)), X = v.filter((se) => !H.has(se.source) && !H.has(se.target));
    x(K), m(X), u && H.has(u) && f(null), I(K, X);
  }, Ae = (z) => {
    if (i || z.length === 0) return;
    const H = new Set(z.map((X) => X.id)), K = v.filter((X) => !H.has(X.id));
    m(K), I(g, K);
  }, Ie = ie((z) => {
    if (i) return;
    const H = v.filter((K) => K.id !== z);
    m(H), I(g, H);
  }, [I, v, i, g]), Te = ie((z, H, K) => {
    a && j({ kind: "spliceEdge", edgeId: z, clientX: H, clientY: K });
  }, [a]), Qe = (z) => {
    const H = w;
    if (!H) return;
    j(null);
    const K = k(H.clientX, H.clientY) ?? { x: 0, y: 0 };
    if (H.kind === "fromEmpty") {
      const se = C(z, K), ye = [...g.map((je) => je.selected ? { ...je, selected: !1 } : je), se.node];
      x(ye), f(se.node.id), I(ye, v, [se.activityNode]);
      return;
    }
    if (H.kind === "fromPort") {
      const se = g.find((ke) => ke.id === H.sourceNodeId), he = se ? cc(se) : K, ye = C(z, he), Ee = [...g.map((ke) => ke.selected ? { ...ke, selected: !1 } : ke), ye.node], Oe = [...v, ko(H.sourceNodeId, ye.node.id, H.sourceHandleId ?? "Done")];
      x(Ee), m(Oe), f(ye.node.id), I(Ee, Oe, [ye.activityNode]);
      return;
    }
    const X = v.find((se) => se.id === H.edgeId);
    X && M(z, X, K);
  }, Ve = de(() => ({
    highlightedEdgeId: b,
    deleteEdge: Ie,
    requestInsertActivity: Te
  }), [Ie, b, Te]);
  return {
    nodes: g,
    edges: v,
    canvasRef: E,
    setReactFlowInstance: y,
    connectMenu: w,
    setConnectMenu: j,
    edgeActions: Ve,
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
function ld(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function gs(e) {
  return ld(e.name);
}
function Nb(e, t) {
  const n = gs(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : dd(o, t);
}
function ud(e, t) {
  return dd(e[gs(t)], t);
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
function pc(e, t, n) {
  return {
    ...e,
    [gs(t)]: n
  };
}
function Cb(e, t) {
  return t.isWrapped === !1 ? Nb(e, t) : ud(e, t).expression.value;
}
function dd(e, t) {
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
  const r = t.slice(0, o), i = (r.split(".").pop() ?? r).toLowerCase();
  return Eb.has(i) ? { elementTypeName: kb(t.slice(o)) } : null;
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
function di(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [r] = o.splice(t, 1);
  return o.splice(n, 0, r), o;
}
function $b(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function fd(e) {
  return ys(e?.trim() ?? "") || e;
}
function ys(e) {
  if (!e) return "";
  const t = Mb(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${ys(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const r = hc(t.slice(0, o)), i = Pb(t.slice(o));
    return i.length > 0 ? `${r}<${i.join(", ")}>` : r;
  }
  return hc(t);
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
function hc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Pb(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = gc(e, t);
  return n == null ? [] : Rb(n).map((o) => {
    const r = o.trim(), i = r.startsWith("[") ? gc(r, 0) ?? r : r;
    return ys(i);
  }).filter(Boolean);
}
function gc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function Rb(e) {
  const t = [];
  let n = 0, o = 0;
  for (let r = 0; r < e.length; r++) {
    const i = e[r];
    i === "[" ? n++ : i === "]" ? n-- : i === "," && n === 0 && (t.push(e.slice(o, r)), o = r + 1);
  }
  return t.push(e.slice(o)), t.map((r) => r.trim()).filter(Boolean);
}
const yc = "elsa-studio:apply-workflow-graph-operation-batch", mc = "elsa-studio:undo-workflow-graph-operation-batch", Lb = [
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
  const o = Xb(e), r = hd(o.state.rootActivity), i = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = Kb(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = $e(d.activityId) ?? u.temporaryReferences?.[0], p = Fb(f ?? $e(d.displayName) ?? $e(d.activityType) ?? "weaver-activity", r), h = Vb(u, p, n);
      a.set(p, h), c.push(p), f && i.set(f, p), o.state.rootActivity && Ob(o.state.rootActivity, h);
      const g = dt(d.position) ? Di(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = xc(o.layout, p, g));
      continue;
    }
    if (l === "set-root") {
      const f = fi(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Et(d.activityId, i);
      if (!f || !ms(o.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = xc(o.layout, f, Di(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = fi(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      Bb(f, $e(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = fi(o, d.activityId, i, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = dt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Et(d.activityId, i);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = pd(o.state.rootActivity, f), o.layout = o.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      Hb(o, d, i);
      continue;
    }
    if (l === "disconnect-activities") {
      Wb(o, d, i);
      continue;
    }
    throw new Error(`Weaver batch operation '${String(u.kind || "unknown")}' is not supported by this designer apply path.`);
  }
  if (!o.state.rootActivity) throw new Error("Weaver batch did not produce a root activity.");
  return o.sourceVersionId = null, {
    draft: o,
    appliedCount: t.operations.length,
    finalActivityIds: c,
    temporaryReferences: Object.fromEntries(i),
    summary: `Applied ${t.operations.length} workflow operation${t.operations.length === 1 ? "" : "s"} to the working draft.`
  };
}
function Vb(e, t, n) {
  const o = e.parameters ?? {}, r = $e(o.activityVersionId) ?? $e(o.activityType) ?? "Elsa.Workflows.Activity", i = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === $e(o.displayName));
  return i ? yi(i, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...$e(o.displayName) ? { displayName: $e(o.displayName) } : {},
    designer: { position: Di(o.position, { x: 280, y: 160 }) }
  };
}
function Ob(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = xs(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Hb(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Et(t.sourceActivityId ?? t.sourceId ?? t.from, n), i = Et(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !i) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = $e(t.connectionId) ?? `flow-${r}-${i}`;
  a.connections = [
    ...c.filter((l) => !dt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: $e(t.outcome) ?? $e(t.sourcePort) ?? "Done" },
      target: { nodeId: i }
    }
  ];
}
function Wb(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const i = $e(t.connectionId), a = Et(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Et(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!dt(u)) return !0;
    if (i && u.id === i) return !1;
    const l = dt(u.source) ? u.source.nodeId : void 0, d = dt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function Bb(e, t, n) {
  const o = dt(n);
  e[ld(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function fi(e, t, n, o) {
  const r = Et(t, n);
  return r ? ms(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Et(e, t) {
  const n = $e(e);
  return n ? t.get(n) ?? n : null;
}
function ms(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of gd(e)) {
    const o = ms(n, t);
    if (o) return o;
  }
  return null;
}
function pd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = xs(e);
  if (n) {
    const o = n.map((r) => pd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function hd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of gd(e)) hd(n, t);
  return t;
}
function gd(e) {
  return xs(e) ?? [];
}
function xs(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function xc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Di(e, t) {
  const n = dt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function Fb(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
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
  setStatus: r,
  setError: i
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
        const p = hn(e), h = zb(e, d.batch, n), g = `weaver-batch-${Date.now()}`;
        a.current.set(g, p), o(h.draft, h.finalActivityIds.at(-1) ?? null), r(h.summary), i(""), d.respond({ ok: !0, result: { ...h, undoToken: g } });
      } catch (p) {
        const h = p instanceof Error ? p.message : String(p);
        i(h), d.respond({ ok: !1, message: h });
      }
    }, u = (l) => {
      const d = l.detail;
      if (!d?.undoToken || !d.respond) return;
      const f = a.current.get(d.undoToken);
      if (!f) {
        d.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      a.current.delete(d.undoToken), o(f, null), r("Restored workflow draft before Weaver batch."), i(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(yc, c), window.addEventListener(mc, u), () => {
      window.removeEventListener(yc, c), window.removeEventListener(mc, u);
    };
  }, [n, t, e, o, r, i]);
}
function Yb({ context: e, draft: t, editDraft: n, setStatus: o, setError: r }) {
  const [i, a] = F(!1), c = re(""), u = re(0), l = re(Promise.resolve()), d = ie((p) => {
    c.current = p ? Re(p) : "";
  }, []), f = ie(async (p, h) => {
    const g = async () => {
      const v = ++u.current, m = Re(p);
      r("");
      try {
        const N = await cp(e, p), y = Re(N);
        return c.current = y, n(({ draft: w }) => !w || w.id !== N.id ? null : Re(w) === m ? N : { ...w, validationErrors: N.validationErrors }), v === u.current && o(h), N;
      } catch (N) {
        throw v === u.current && (o(""), r(N instanceof Error ? N.message : String(N))), N;
      }
    }, x = l.current.then(g, g);
    return l.current = x.catch(() => {
    }), x;
  }, [e, n, o, r]);
  return G(() => {
    if (!i || !t || Re(t) === c.current) return;
    o("Autosaving...");
    const h = window.setTimeout(() => {
      f(t, "Autosaved").catch(() => {
      });
    }, f0);
    return () => window.clearTimeout(h);
  }, [i, t, f, o]), { saveDraft: f, autosaveEnabled: i, setAutosaveEnabled: a, markSaved: d };
}
function Ub({ context: e, definitionId: t, resetHistory: n, loadDraft: o, markSaved: r, setError: i }) {
  const [a, c] = F(null), [u, l] = F([]), [d, f] = F([]), [p, h] = F(null), [g, x] = F(go), [v, m] = F("loading"), N = ie(async () => {
    i(""), m("loading");
    const [y, w, j, b, S] = await Promise.all([
      Qf(e, t),
      Fi(e),
      yp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      mp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: go })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      Gc(e).then(
        (D) => D,
        () => null
      )
    ]), E = y.draft ?? null;
    c(y), r(E), n(E), o(E), l(w.activities ?? []), f(j.descriptors), h(S), x(b.descriptors.length > 0 ? b.descriptors : go), m(j.ok ? "ready" : "failed");
  }, [e, t, n, o, r, i]);
  return G(() => {
    N().catch((y) => i(y instanceof Error ? y.message : String(y)));
  }, [N, i]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: g,
    descriptorStatus: v,
    reload: N
  };
}
function Zb({ context: e, details: t, setDetails: n, setStatus: o }) {
  const r = re(null), i = re(null), a = re({});
  G(() => {
    r.current = t;
  }, [t]);
  const c = ie(() => {
    i.current !== null && (window.clearTimeout(i.current), i.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || ap(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => o("Couldn't save name/description."));
  }, [e, n, o]), u = ie((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, i.current !== null && window.clearTimeout(i.current), i.current = window.setTimeout(c, 800);
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
  saveDraft: r,
  reload: i,
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
    const N = n?.definition.name;
    Fv(Hv(t, N), N), d("Exported workflow as JSON.");
  }, [t, n, d]), x = ie(async () => {
    if (!(!t || o)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, o, r, l, d]), v = ie(async () => {
    if (!(!t || o)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const N = await lp(e, t.id), y = await up(e, N.versionId);
        u(y.artifactId), d(`Published ${y.artifactVersion}`), await i();
      } catch (N) {
        d(""), f(N instanceof Error ? N.message : String(N));
      } finally {
        l("idle");
      }
    }
  }, [t, o, e, r, i, u, l, d, f]), m = ie(async () => {
    if (!t?.state.rootActivity || o) return;
    const N = t, y = Re(N);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const w = O0(N);
      l("testRunStarting"), d("Starting test run...");
      const j = await dp(e, {
        definitionId: N.definitionId,
        snapshotId: w,
        state: N.state
      });
      a({ draftSignature: y, view: j }), p("runtime"), h(!1), d(fs(j) ? "Test run rejected" : "Test run dispatched");
    } catch (w) {
      d(""), f(w instanceof Error ? w.message : String(w));
    } finally {
      l("idle");
    }
  }, [t, o, e, c, a, p, h, l, d, f]);
  return { exportJson: g, save: x, promoteAndPublish: v, run: m };
}
function Jb({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: o,
  catalog: r,
  activityDescriptors: i,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(r.map((b) => [b.activityVersionId, b])), [r]), l = ie(
    (b) => $p([b.activityVersionId, b.activityTypeKey], a),
    [a]
  ), d = de(() => D0(i), [i]), f = de(() => Oc(c, n), [c, n]), p = Wi(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", g = de(() => h ? null : vn(c, n), [c, n, h]), x = de(() => h && f?.nodeId === o ? f : g?.slot.activities.find((b) => b.nodeId === o) ?? null, [h, g, f, o]), v = de(
    () => x ? T0(x, u, d) : null,
    [u, d, x]
  ), m = de(
    () => x ? l({ activityVersionId: x.activityVersionId, activityTypeKey: u.get(x.activityVersionId)?.activityTypeKey }) : null,
    [l, u, x]
  ), N = x ? De(x) : [], y = tp(e, t?.state, o), w = p === "flowchart" && g?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: g,
    selectedNode: x,
    selectedDescriptor: v,
    selectedNodeAvailability: m,
    selectedSlots: N,
    scopedVariableAnalysis: y,
    isFlowchartDesigner: w,
    canAddActivitiesToCanvas: !c || !h
  };
}
function Qb({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: o,
  selectedDescriptor: r,
  catalogByVersion: i
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
        selectedActivityType: r?.typeName ?? (n ? i.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: od(t.state.rootActivity, i),
        connections: rd(t.state.rootActivity),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [i, e, t, r, n, o]);
}
function eN({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: o,
  onToggleCategory: r,
  onActivityClick: i,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ s.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ s.jsx(Fo, { size: 14, "aria-hidden": "true" }),
      /* @__PURE__ */ s.jsx(
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
    /* @__PURE__ */ s.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: n.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : n.map((d) => {
      const f = l || o.has(d.category);
      return /* @__PURE__ */ s.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ s.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => r(d.category),
            children: [
              f ? /* @__PURE__ */ s.jsx(kc, { size: 14 }) : /* @__PURE__ */ s.jsx(Mt, { size: 14 }),
              /* @__PURE__ */ s.jsx("span", { children: d.category }),
              /* @__PURE__ */ s.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ s.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), g = h ? `wf-palette-description-${p.activityVersionId}` : void 0, x = Ce(p), v = Yo(p);
          return /* @__PURE__ */ s.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ce(p),
              "aria-describedby": g,
              onClick: () => i(p),
              onDragStart: (m) => a(m, p),
              onDragEnd: (m) => c(m, p),
              onPointerDown: (m) => u(m, p),
              children: [
                /* @__PURE__ */ s.jsx("span", { className: "wf-activity-icon", "data-icon": v, "aria-hidden": "true", children: us(v) }),
                /* @__PURE__ */ s.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ s.jsx("strong", { children: x }),
                  h ? /* @__PURE__ */ s.jsx("small", { id: g, children: h }) : null
                ] }),
                /* @__PURE__ */ s.jsx(Ac, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const yd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), tN = "Variable";
function nN({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  descriptorStatus: i,
  visibleVariables: a,
  scopeStatus: c,
  onChange: u
}) {
  if (i === "loading")
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Loading activity properties..." });
  if (!t)
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "No activity descriptor is available for this activity." });
  const l = t.inputs.filter((p) => p.isBrowsable !== !1).sort((p, h) => (p.order ?? 0) - (h.order ?? 0) || p.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = uN(l), f = r.length > 0 ? r : bb;
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ s.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ s.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ s.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ s.jsx(
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
  expressionDescriptors: r,
  visibleVariables: i,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Pi(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? ud(e, t) : null, h = p?.expression.type ?? "Literal", g = Cb(e, t), x = h.toLowerCase(), m = p && (x === "literal" || x === "object") && !Ab(t) ? Ib(t.typeName) : null, N = m ? Pi(n, t, { ...l, scope: "collection" }) : void 0, y = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, w = y ? xd(o, y) : null, j = w?.surfaces.inline, b = w && y ? wd(w, y, g) : [], S = m != null, E = !!(p && !S && dN(t, d?.id)), D = !!(p && !S && fN(t, d?.id)), [L, A] = F(!1), _ = (k) => {
    const T = p ? jb(p, k) : k;
    c(pc(e, t, T));
  }, R = (k) => {
    p && c(pc(e, t, Sb(p, k)));
  }, C = m ? N ? Ti(N.component, t, g, u, { ...l, scope: "collection" }, _) : /* @__PURE__ */ s.jsx(
    iN,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: _
    }
  ) : null, I = h === tN && p ? /* @__PURE__ */ s.jsx(
    cN,
    {
      value: g,
      visibleVariables: i,
      scopeStatus: a,
      disabled: u,
      onChange: _
    }
  ) : C ?? (j && y ? /* @__PURE__ */ s.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: _
    }
  ) : Ti(f, t, g, u, l, _));
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ s.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ s.jsx("span", { children: fd(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ s.jsx("p", { children: t.description }) : null,
    p && !E ? /* @__PURE__ */ s.jsx(
      $i,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: R
      }
    ) : null,
    E ? /* @__PURE__ */ s.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-expression-editor", children: [
        I,
        Ri(b)
      ] }),
      /* @__PURE__ */ s.jsx(
        $i,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: R
        }
      ),
      D ? /* @__PURE__ */ s.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => A(!0),
          children: /* @__PURE__ */ s.jsx(Co, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      I,
      Ri(b)
    ] }),
    D && !E ? /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ s.jsx(Co, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    L ? /* @__PURE__ */ s.jsx(
      sN,
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
function rN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function iN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: r,
  disabled: i,
  onChange: a
}) {
  const c = _b(n), u = Tb(e, t), l = { ...r, scope: "element" }, d = Pi(o, u, l)?.component, f = e.displayName || e.name, p = (j, b) => a(c.map((S, E) => E === j ? b : S)), [h, g] = F(null), [x, v] = F(null), m = () => {
    g(null), v(null);
  }, N = (j) => (b) => {
    g(j), b.dataTransfer.effectAllowed = "move", b.dataTransfer.setData("text/plain", String(j));
  }, y = (j) => (b) => {
    h !== null && (b.preventDefault(), b.dataTransfer.dropEffect = "move", x !== j && v(j));
  }, w = (j) => (b) => {
    b.preventDefault(), h !== null && h !== j && a(di(c, h, j)), m();
  };
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ s.jsx("ul", { className: "wf-collection-items", children: c.map((j, b) => /* @__PURE__ */ s.jsxs(
      "li",
      {
        className: rN(b, h, x),
        onDragOver: y(b),
        onDrop: w(b),
        children: [
          /* @__PURE__ */ s.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !i,
              "aria-label": `Drag ${f} item ${b + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: N(b),
              onDragEnd: m,
              children: /* @__PURE__ */ s.jsx(Ac, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ s.jsx("div", { className: "wf-collection-item-editor", children: Ti(d, u, j, i, l, (S) => p(b, S)) }),
          /* @__PURE__ */ s.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} up`,
                disabled: i || b === 0,
                onClick: () => a(di(c, b, b - 1)),
                children: /* @__PURE__ */ s.jsx(Dd, { size: 13 })
              }
            ),
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} down`,
                disabled: i || b === c.length - 1,
                onClick: () => a(di(c, b, b + 1)),
                children: /* @__PURE__ */ s.jsx(kc, { size: 13 })
              }
            ),
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${b + 1}`,
                disabled: i,
                onClick: () => a(c.filter((S, E) => E !== b)),
                children: /* @__PURE__ */ s.jsx(wn, { size: 13 })
              }
            )
          ] })
        ]
      },
      b
    )) }),
    /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: i,
        onClick: () => a([...c, Db(t)]),
        children: [
          /* @__PURE__ */ s.jsx(Ht, { size: 13 }),
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
  activity: r,
  expressionEditors: i,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = Sc(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = xd(i, p), g = h?.surfaces.expanded, x = h ? wd(h, p, t) : [], v = g ? null : lN(i, p);
  return G(() => {
    const m = (N) => {
      N.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ s.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ s.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ s.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ s.jsx(Ic, { size: 16 }) })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ s.jsx(
          $i,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ s.jsx("span", { children: fd(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ s.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ s.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: p,
          onChange: c
        }
      ) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
        v ? /* @__PURE__ */ s.jsx("p", { className: "wf-expression-editor-hint", children: v }) : null,
        /* @__PURE__ */ s.jsx(
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
      Ri(x)
    ] }),
    /* @__PURE__ */ s.jsxs("footer", { children: [
      /* @__PURE__ */ s.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Ti(e, t, n, o, r, i) {
  return e ? /* @__PURE__ */ s.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: o,
      context: r,
      onChange: i
    }
  ) : /* @__PURE__ */ s.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: o, onChange: (a) => i(a.target.value) });
}
function $i({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: i
}) {
  const [a, c] = F(!1), u = Sc(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ s.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
    f.currentTarget.contains(f.relatedTarget) || c(!1);
  }, children: [
    /* @__PURE__ */ s.jsx(
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
        children: /* @__PURE__ */ s.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ s.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((f) => {
      const p = f.displayName || f.type, h = f.type === t;
      return /* @__PURE__ */ s.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": h,
          className: h ? "selected" : "",
          onClick: () => {
            i(f.type), c(!1);
          },
          children: p
        },
        f.type
      );
    }) }) : null
  ] });
}
const Mi = "::";
function md(e) {
  return !e || e === Ao ? Ao : e;
}
function wc(e, t) {
  return `${md(t)}${Mi}${e}`;
}
function aN(e) {
  const t = e.indexOf(Mi);
  if (t < 0) return null;
  const n = e.slice(t + Mi.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function cN({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: r }) {
  const i = Yc(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = i && (a || t.some((d) => d.referenceKey === i.referenceKey)) ? i : null, u = c ? wc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === md(c.declaringScopeId)
  );
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ s.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const f = aN(d.target.value);
          f && r(Kf(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ s.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ s.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = wc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ s.jsxs("option", { value: f, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, f);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ s.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ s.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Pi(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function xd(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function wd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function lN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), i = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${i} ${r}` : i;
}
function Ri(e) {
  return e.length === 0 ? null : /* @__PURE__ */ s.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ s.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ s.jsx("span", { children: t.code }) : null,
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
  if (e.uiHint?.toLowerCase() === "multiline" || t && !yd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function fN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !yd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function pN({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: o,
  selectedDescriptor: r,
  selectedNodeAvailability: i,
  propertyEditors: a,
  expressionEditors: c,
  expressionDescriptors: u,
  descriptorStatus: l,
  scopedVariableAnalysis: d,
  onSelectedActivityChange: f,
  onEnterSlot: p
}) {
  if (!t)
    return /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  const h = De(t);
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ s.jsx("h3", { children: n }),
    /* @__PURE__ */ s.jsxs("dl", { children: [
      /* @__PURE__ */ s.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ s.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ s.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ s.jsx("dd", { children: o }),
      /* @__PURE__ */ s.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ s.jsx("dd", { children: t.activityVersionId })
    ] }),
    i ? /* @__PURE__ */ s.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ s.jsx(So, { size: 14 }),
      /* @__PURE__ */ s.jsxs("span", { children: [
        "No longer available for new use · ",
        _o(i.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ s.jsx(
      nN,
      {
        activity: t,
        descriptor: r,
        editors: a,
        expressionEditors: c,
        expressionDescriptors: u,
        descriptorStatus: l,
        visibleVariables: d.visibleVariables,
        scopeStatus: d.status,
        onChange: f
      }
    ),
    Bf(t) ? /* @__PURE__ */ s.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ s.jsx(
      c0,
      {
        context: e,
        variables: qc(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: qf(d.shadowingWarnings, t.nodeId),
        onChange: (g) => f(Ff(t, g))
      }
    ) }) : null,
    h.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ s.jsx("span", { children: "Embedded slots" }),
      h.map((g) => /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => p(t, g.id, `${n} / ${g.label}`), children: [
        g.label,
        /* @__PURE__ */ s.jsxs("small", { children: [
          g.activities.length,
          " activit",
          g.activities.length === 1 ? "y" : "ies"
        ] })
      ] }, g.id))
    ] }) : /* @__PURE__ */ s.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] });
}
function hN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: i,
  onBack: a
}) {
  const c = hb(), { draft: u, frames: l, selectedNodeId: d, testRun: f, publishedArtifactId: p } = c.state, {
    loadDraft: h,
    replaceDraftByBatch: g,
    editDraft: x,
    editDraftAndSelect: v,
    select: m,
    navigateToScope: N,
    resetToRoot: y,
    enterSlot: w,
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
  } = ib(), { resetHistory: we, undo: Ae, redo: Ie, canUndoNow: Te, canRedoNow: Qe } = db({ draft: u, restoreDraft: h }), { saveDraft: Ve, autosaveEnabled: z, setAutosaveEnabled: H, markSaved: K } = Yb({ context: e, draft: u, editDraft: x, setStatus: A, setError: D }), {
    details: X,
    setDetails: se,
    catalog: he,
    activityDescriptors: ye,
    availabilityDiagnostics: je,
    expressionDescriptors: Ee,
    descriptorStatus: Oe,
    reload: ke
  } = Ub({ context: e, definitionId: t, resetHistory: we, loadDraft: h, markSaved: K, setError: D }), { updateDefinitionMeta: lr } = Zb({ context: e, details: X, setDetails: se, setStatus: A }), {
    catalogByVersion: pt,
    availabilityLookup: ur,
    scopeOwner: Wn,
    isUnsupportedDesigner: It,
    scope: dr,
    selectedNode: qe,
    selectedDescriptor: kt,
    selectedNodeAvailability: fr,
    selectedSlots: pr,
    scopedVariableAnalysis: hr,
    isFlowchartDesigner: st,
    canAddActivitiesToCanvas: gr
  } = Jb({ context: e, draft: u, frames: l, selectedNodeId: d, catalog: he, activityDescriptors: ye, availabilityDiagnostics: je }), At = de(() => _i(he), [he]), yr = de(() => {
    const Q = k.trim().toLowerCase();
    if (!Q) return At;
    const fe = he.filter((ve) => Ce(ve).toLowerCase().includes(Q) || ve.activityTypeKey.toLowerCase().includes(Q) || (ve.category ?? "").toLowerCase().includes(Q) || (ve.description ?? "").toLowerCase().includes(Q));
    return _i(fe);
  }, [he, k, At]), _t = _ !== "idle", mr = !!u?.state.rootActivity && !_t, Bn = St(n, "weaver.workflows.find-draft-risks"), Fn = St(n, "weaver.workflows.propose-update"), xr = vb({
    draft: u,
    scope: dr,
    scopeOwner: Wn,
    catalog: he,
    catalogByVersion: pt,
    isUnsupportedDesigner: It,
    isFlowchartDesigner: st,
    canAddActivitiesToCanvas: gr,
    selectedNodeId: d,
    editDraft: x,
    editDraftAndSelect: v,
    select: m,
    setStatus: A,
    setError: D
  }), {
    nodes: Qt,
    edges: wr,
    canvasRef: vr,
    setReactFlowInstance: br,
    connectMenu: en,
    setConnectMenu: Nr,
    edgeActions: Kn,
    onNodesChange: Xn,
    onEdgesChange: qn,
    onNodesDelete: jr,
    onEdgesDelete: Sr,
    isValidConnection: Cr,
    onConnect: Er,
    onConnectStart: Ir,
    onConnectEnd: kr,
    onReconnect: Yn,
    commitLayout: Ar,
    canAutoLayout: _r,
    autoLayout: Dr,
    onCanvasDragOver: Un,
    onCanvasDragLeave: Zn,
    onCanvasDrop: Gn,
    openEmptyConnectMenu: Tr,
    onConnectMenuPick: $r,
    onPaletteClick: Jn,
    onPaletteDragStart: Mr,
    onPaletteDragEnd: Pr,
    onPalettePointerDown: Rr
  } = xr;
  qb({ draft: u, details: X, catalog: he, replaceDraftByBatch: g, setStatus: A, setError: D }), Qb({ details: X, draft: u, selectedNode: qe, selectedNodeId: d, selectedDescriptor: kt, catalogByVersion: pt }), G(() => {
    I((Q) => {
      let fe = !1;
      const ve = new Set(Q);
      for (const et of At)
        ve.has(et.category) || (ve.add(et.category), fe = !0);
      return fe ? ve : Q;
    });
  }, [At]);
  const { exportJson: Lr, save: zr, promoteAndPublish: Vr, run: Or } = Gb({
    context: e,
    draft: u,
    details: X,
    busy: _t,
    saveDraft: Ve,
    reload: ke,
    startTestRun: j,
    clearTestRun: b,
    setPublishedArtifact: S,
    setOperation: R,
    setStatus: A,
    setError: D,
    setActiveRightPanelId: W,
    setInspectorCollapsed: q
  }), Hr = ie((Q) => {
    x(({ draft: fe }) => fe ? { ...fe, state: Q(fe.state) } : null);
  }, [x]), Qn = ie((Q) => {
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
  const Wr = ie((Q) => {
    x(({ draft: fe }) => {
      const ve = fe?.state.rootActivity;
      return !fe || !ve ? null : {
        ...fe,
        state: {
          ...fe.state,
          rootActivity: Bc(ve, Q.nodeId, () => Q)
        }
      };
    });
  }, [x]), eo = ie((Q) => {
    if (!Q) return;
    const fe = u?.state.rootActivity;
    if (!fe) return;
    const ve = wf(fe, Q, (et) => {
      const bs = pt.get(et.activityVersionId);
      return bs ? Ce(bs) : et.nodeId;
    });
    ve && (U("designer"), N(ve, Q), q(!1));
  }, [u?.state.rootActivity, pt, q, N]), Br = (Q) => {
    I((fe) => {
      const ve = new Set(fe);
      return ve.has(Q) ? ve.delete(Q) : ve.add(Q), ve;
    });
  };
  if (!X || !u)
    return /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: E || "Loading workflow editor..." });
  const Dt = f?.draftSignature === Re(u) ? f.view : null, Tt = Dt && L.startsWith("Test run") ? "" : L, Fr = (Q) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Q)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Kr = {
    definition: X.definition,
    draft: u,
    selectedActivity: qe,
    selectedActivityDescriptor: kt,
    selectedActivitySlots: pr,
    catalog: he,
    currentScopeOwner: Wn,
    frames: l
  }, tn = i.map((Q) => {
    const fe = Q.component;
    return {
      id: Q.id,
      title: Q.title,
      side: Q.side,
      order: Q.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ s.jsx(fe, { context: Kr })
    };
  }), Xr = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ s.jsx(Ko, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(
        eN,
        {
          paletteSearch: k,
          onSearchChange: T,
          groups: yr,
          expandedCategories: C,
          onToggleCategory: Br,
          onActivityClick: Jn,
          onActivityDragStart: Mr,
          onActivityDragEnd: Pr,
          onActivityPointerDown: Rr
        }
      )
    },
    ...tn.filter((Q) => Q.side === "left")
  ].sort(lc), qr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ s.jsx(Vi, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(
        pN,
        {
          context: e,
          selectedNode: qe,
          selectedNodeLabel: qe ? Qt.find((Q) => Q.id === qe.nodeId)?.data.label ?? qe.nodeId : "",
          selectedActivityType: qe ? kt?.typeName ?? pt.get(qe.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: kt,
          selectedNodeAvailability: fr,
          propertyEditors: o,
          expressionEditors: r,
          expressionDescriptors: Ee,
          descriptorStatus: Oe,
          scopedVariableAnalysis: hr,
          onSelectedActivityChange: Wr,
          onEnterSlot: w
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ s.jsx(Ot, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(tb, { testRun: Dt, onOpenRun: Fr })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ s.jsx(_c, { size: 15 }),
      render: () => /* @__PURE__ */ s.jsx(
        rb,
        {
          context: e,
          ai: n,
          definitionId: X.definition.id,
          publishedArtifactId: p
        }
      )
    },
    ...tn.filter((Q) => Q.side === "right")
  ].sort(lc), ws = Xr.find((Q) => Q.id === M) ?? Xr[0], vs = qr.find((Q) => Q.id === B) ?? qr[0], vd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ s.jsx(Rd, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ s.jsx(Ld, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ s.jsx(zi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ s.jsx(Mt, { size: 14 }),
      /* @__PURE__ */ s.jsx("strong", { children: X.definition.name }),
      /* @__PURE__ */ s.jsx("span", { className: "wf-chip", children: "Draft" }),
      Tt ? /* @__PURE__ */ s.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ s.jsx(Zt, { size: 13 }),
        " ",
        Tt
      ] }) : null,
      /* @__PURE__ */ s.jsxs("div", { className: "wf-editor-actions", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-canvas-tools", role: "group", "aria-label": "Canvas tools", children: [
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Undo",
              title: "Undo (Ctrl+Z)",
              disabled: !Te,
              onClick: Ae,
              children: /* @__PURE__ */ s.jsx(Td, { size: 16 })
            }
          ),
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Qe,
              onClick: Ie,
              children: /* @__PURE__ */ s.jsx($d, { size: 16 })
            }
          ),
          /* @__PURE__ */ s.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !_r,
              onClick: Dr,
              children: /* @__PURE__ */ s.jsx(Md, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ s.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: z, onChange: (Q) => H(Q.target.checked) }),
          /* @__PURE__ */ s.jsx("span", { children: "Autosave" })
        ] }),
        Bn ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Ct(n, Bn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ s.jsx(ot, { size: 15 }),
          " Risks"
        ] }) : null,
        Fn ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Ct(n, Fn, { definition: X.definition, draft: u }), children: [
          /* @__PURE__ */ s.jsx(ot, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ s.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Lr, children: [
          /* @__PURE__ */ s.jsx(Pd, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: _t, onClick: () => {
          zr();
        }, children: [
          /* @__PURE__ */ s.jsx(Cc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ s.jsxs("button", { type: "button", disabled: _t, onClick: () => {
          Vr();
        }, children: [
          /* @__PURE__ */ s.jsx(Ec, { size: 15 }),
          " Promote"
        ] }),
        Dt ? /* @__PURE__ */ s.jsx(
          eb,
          {
            testRun: Dt,
            onOpenDetails: () => {
              W("runtime"), q(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ s.jsxs(
          "button",
          {
            type: "button",
            disabled: !mr,
            title: u.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Or();
            },
            children: [
              /* @__PURE__ */ s.jsx(Ot, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    E ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(ft, { size: 16 }),
      " ",
      E
    ] }) : null,
    /* @__PURE__ */ s.jsxs("div", { className: J, style: oe, children: [
      /* @__PURE__ */ s.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ s.jsx(
            jo,
            {
              label: "Activities panel tabs",
              tabs: Xr,
              activeTabId: ws.id,
              onSelect: $
            }
          ),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": le ? "Expand activities panel" : "Collapse activities panel",
                title: le ? "Expand" : "Collapse",
                onClick: () => ue("palette"),
                children: le ? /* @__PURE__ */ s.jsx(Mt, { size: 14 }) : /* @__PURE__ */ s.jsx(Eo, { size: 14 })
              }
            ),
            le ? null : /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: P === "palette" ? "Restore" : "Maximize",
                onClick: () => V("palette"),
                children: P === "palette" ? /* @__PURE__ */ s.jsx(Ss, { size: 14 }) : /* @__PURE__ */ s.jsx(Co, { size: 14 })
              }
            )
          ] })
        ] }),
        ae ? ws.render() : null
      ] }),
      ae && !P ? /* @__PURE__ */ s.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": ln,
          "aria-valuemax": un,
          "aria-valuenow": Y,
          tabIndex: 0,
          onPointerDown: (Q) => ee("palette", Q),
          onKeyDown: (Q) => ge("palette", Q)
        }
      ) : /* @__PURE__ */ s.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ s.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ s.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ s.jsx(
          jo,
          {
            label: "Editor view tabs",
            tabs: vd,
            activeTabId: O,
            onSelect: (Q) => U(Q)
          }
        ) }),
        O === "code" ? /* @__PURE__ */ s.jsx(Uv, { draft: u, onApply: Qn }) : O === "properties" ? /* @__PURE__ */ s.jsx(u0, { details: X, draft: u, context: e, onStateChange: Hr, onDefinitionMetaChange: lr }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          /* @__PURE__ */ s.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => y(), children: "Root" }),
            l.map((Q, fe) => /* @__PURE__ */ s.jsxs(Ze.Fragment, { children: [
              /* @__PURE__ */ s.jsx(Mt, { size: 13 }),
              /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => N(l.slice(0, fe + 1), null), children: Q.label })
            ] }, `${Q.ownerNodeId}-${Q.slotId}-${fe}`))
          ] }),
          /* @__PURE__ */ s.jsxs("div", { className: "wf-canvas", ref: vr, onDragOver: Un, onDragLeave: Zn, onDrop: Gn, children: [
            /* @__PURE__ */ s.jsx(Uu.Provider, { value: Kn, children: /* @__PURE__ */ s.jsx(Zu.Provider, { value: ur, children: /* @__PURE__ */ s.jsxs(
              zu,
              {
                nodes: Qt,
                edges: wr,
                nodeTypes: sd,
                edgeTypes: ad,
                onInit: br,
                onNodesChange: Xn,
                onEdgesChange: qn,
                onNodesDelete: jr,
                onEdgesDelete: Sr,
                onConnect: Er,
                onConnectStart: st ? Ir : void 0,
                onConnectEnd: st ? kr : void 0,
                onReconnect: st ? Yn : void 0,
                isValidConnection: Cr,
                onDragOver: Un,
                onDragLeave: Zn,
                onDrop: Gn,
                onPaneClick: () => m(null),
                onNodeClick: (Q, fe) => m(fe.id),
                onNodeDragStop: It ? void 0 : Ar,
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
                  /* @__PURE__ */ s.jsx(Ou, { gap: 18, size: 1 }),
                  /* @__PURE__ */ s.jsx(Wu, {}),
                  /* @__PURE__ */ s.jsx(Fu, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            st && Qt.length === 0 ? /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Tr(), children: [
              /* @__PURE__ */ s.jsx(Ht, { size: 15 }),
              " Add activity"
            ] }) : null,
            en ? /* @__PURE__ */ s.jsx(
              q0,
              {
                clientX: en.clientX,
                clientY: en.clientY,
                activities: he,
                onPick: $r,
                onClose: () => Nr(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ s.jsx(Q0, { draft: u, onRepair: eo })
        ] })
      ] }),
      ce && !P ? /* @__PURE__ */ s.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": dn,
          "aria-valuemax": fn,
          "aria-valuenow": te,
          tabIndex: 0,
          onPointerDown: (Q) => ee("inspector", Q),
          onKeyDown: (Q) => ge("inspector", Q)
        }
      ) : /* @__PURE__ */ s.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ s.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ s.jsx(
            jo,
            {
              label: "Inspector panel tabs",
              tabs: qr,
              activeTabId: vs.id,
              onSelect: W
            }
          ),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Z ? "Expand inspector panel" : "Collapse inspector panel",
                title: Z ? "Expand" : "Collapse",
                onClick: () => ue("inspector"),
                children: Z ? /* @__PURE__ */ s.jsx(Eo, { size: 14 }) : /* @__PURE__ */ s.jsx(Mt, { size: 14 })
              }
            ),
            Z ? null : /* @__PURE__ */ s.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: P === "inspector" ? "Restore" : "Maximize",
                onClick: () => V("inspector"),
                children: P === "inspector" ? /* @__PURE__ */ s.jsx(Ss, { size: 14 }) : /* @__PURE__ */ s.jsx(Co, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? vs.render() : null
      ] })
    ] })
  ] });
}
function gN({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const i = Gu(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
  return /* @__PURE__ */ s.jsxs("div", { className: "wf-pagination", "aria-label": "Workflow definition pagination", children: [
    /* @__PURE__ */ s.jsxs("span", { className: "wf-pagination-summary", "aria-live": "polite", children: [
      "Showing ",
      a,
      "-",
      c,
      " of ",
      n
    ] }),
    /* @__PURE__ */ s.jsxs("label", { className: "wf-page-size", children: [
      "Rows",
      /* @__PURE__ */ s.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: h0.map((u) => /* @__PURE__ */ s.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ s.jsx(Eo, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ s.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        i
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => o(e + 1), disabled: e >= i, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ s.jsx(Mt, { size: 14 })
      ] })
    ] })
  ] });
}
const yN = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function mN({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: r, onClose: i, onSubmit: a }) {
  const [c, u] = F(!1), [l, d] = F(""), [f, p] = F(!1), [h, g] = F(null), [x, v] = F(null), m = re(null), N = re(e);
  N.current = e;
  const y = re(r);
  y.current = r;
  const w = ie((b) => {
    const S = { ...N.current };
    b.name && (S.name = b.name), b.description && (S.description = b.description), y.current(S), g(null), v(null);
  }, []);
  G(() => {
    if (o)
      return n.onPromptResult((b) => {
        if (b.requestId !== m.current) return;
        if (m.current = null, p(!1), b.status !== "completed") {
          v(b.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = w0(b.text);
        if (!S) {
          v("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        b.autoApply ? w(S) : g(S);
      });
  }, [n, o, w]);
  const j = () => {
    if (!o) return;
    const b = o.createPrompt({ draft: N.current, intent: l });
    if (!b) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    m.current = S, p(!0), g(null), v(null), n.dispatchPrompt({ ...b, requestId: S });
  };
  return /* @__PURE__ */ s.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ s.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ s.jsxs(
    "form",
    {
      onSubmit: (b) => {
        b.preventDefault(), a();
      },
      children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-dialog-heading", children: [
          /* @__PURE__ */ s.jsx("h3", { id: "workflow-create-title", children: "Create Workflow" }),
          o ? /* @__PURE__ */ s.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((b) => !b),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ s.jsx(ot, { size: 13 }),
                " ",
                o.label
              ]
            }
          ) : null
        ] }),
        o && c ? /* @__PURE__ */ s.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
          /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
            /* @__PURE__ */ s.jsx("span", { children: "What should this workflow do?" }),
            /* @__PURE__ */ s.jsx(
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
          /* @__PURE__ */ s.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-ai-action", onClick: j, disabled: f, children: [
            /* @__PURE__ */ s.jsx(ot, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          x ? /* @__PURE__ */ s.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: x }) : null,
          h ? /* @__PURE__ */ s.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ s.jsx("p", { children: /* @__PURE__ */ s.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ s.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ s.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => w(h), children: "Apply" }),
              /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => g(null), children: "Dismiss" })
            ] })
          ] }) : null
        ] }) : null,
        /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Display name" }),
          /* @__PURE__ */ s.jsx(
            "input",
            {
              autoFocus: !0,
              "aria-label": "Display name",
              value: e.name,
              onChange: (b) => r({ ...e, name: b.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("label", { className: "wf-form-field", children: [
          /* @__PURE__ */ s.jsx("span", { children: "Description" }),
          /* @__PURE__ */ s.jsx(
            "textarea",
            {
              "aria-label": "Description",
              rows: 3,
              value: e.description,
              onChange: (b) => r({ ...e, description: b.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ s.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ s.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ s.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: yN.map((b) => {
            const S = e.rootKind === b.value;
            return /* @__PURE__ */ s.jsxs("label", { className: "wf-root-card", "data-checked": S || void 0, children: [
              /* @__PURE__ */ s.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": b.label,
                  value: b.value,
                  checked: S,
                  onChange: () => r({ ...e, rootKind: b.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ s.jsx("span", { className: "wf-root-card-title", children: b.label }),
              /* @__PURE__ */ s.jsx("span", { className: "wf-root-card-hint", children: b.hint })
            ] }, b.value);
          }) })
        ] }),
        /* @__PURE__ */ s.jsxs("div", { className: "wf-dialog-actions", children: [
          /* @__PURE__ */ s.jsx("button", { type: "button", onClick: i, disabled: t, children: "Cancel" }),
          /* @__PURE__ */ s.jsx("button", { type: "submit", disabled: t || !e.name.trim(), children: t ? "Creating..." : "Create" })
        ] })
      ]
    }
  ) }) });
}
function xN({ context: e, ai: t, onOpen: n }) {
  const [o, r] = F(""), [i, a] = F("active"), [c, u] = F(1), [l, d] = F(g0), [f, p] = F("loading"), [h, g] = F(""), [x, v] = F(""), [m, N] = F([]), [y, w] = F(0), [j, b] = F(() => /* @__PURE__ */ new Set()), [S, E] = F(null), [D, L] = F(!1), [A, _] = F([]), [R, C] = F("idle"), I = re(null), k = de(() => m.map((V) => V.id), [m]), T = St(t, "weaver.workflows.suggest-create-metadata"), M = St(t, "weaver.workflows.explain-definition"), $ = k.filter((V) => j.has(V)).length, B = k.length > 0 && $ === k.length, W = ie(async () => {
    p("loading"), g("");
    try {
      const V = await Jf(e, { search: o, state: i, page: c, pageSize: l }), ee = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, we = Gu(ge, l);
      if (ge > 0 && c > we) {
        u(we);
        return;
      }
      N(ee ? V.definitions : x0(V.definitions, c, l)), w(ge), p("ready");
    } catch (V) {
      g(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, o, i, c, l]);
  G(() => {
    W();
  }, [W]), G(() => {
    I.current && (I.current.indeterminate = $ > 0 && !B);
  }, [B, $]);
  const O = ie(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await Fi(e);
        _(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), g(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), U = () => {
    g(""), v(""), E({ name: "", description: "", rootKind: "flowchart" }), O();
  }, Y = async () => {
    if (S?.name.trim()) {
      L(!0), g(""), v("");
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
    if (m.length === 1 && c > 1) {
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
    r(V), u(1), Z();
  }, J = async (V) => {
    if (await Is().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      v(""), g("");
      try {
        await rp(e, V.id), P(V.id, !1), v(`Deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, oe = async (V) => {
    v(""), g("");
    try {
      await ip(e, V.id), P(V.id, !1), v(`Restored ${V.name}`), await le();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, ue = async (V) => {
    if (await Is().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      v(""), g("");
      try {
        await sp(e, V.id), P(V.id, !1), v(`Permanently deleted ${V.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ s.jsx("button", { type: "button", className: i === "active" ? "active" : "", "aria-selected": i === "active", onClick: () => ae("active"), children: "Active" }),
        /* @__PURE__ */ s.jsx("button", { type: "button", className: i === "deleted" ? "active" : "", "aria-selected": i === "deleted", onClick: () => ae("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ s.jsx(Fo, { size: 15 }),
        /* @__PURE__ */ s.jsx("input", { value: o, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        W();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ s.jsxs("button", { type: "button", title: "Create workflow", onClick: U, children: [
        /* @__PURE__ */ s.jsx(Ht, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ s.jsx(Hn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ s.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ s.jsx(ft, { size: 16 }),
      " ",
      h
    ] }) : null,
    x ? /* @__PURE__ */ s.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ s.jsx(Zt, { size: 14 }),
      " ",
      x
    ] }) : null,
    j.size > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ s.jsxs("span", { children: [
        j.size,
        " selected"
      ] }),
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ s.jsx(ps, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ s.jsx(
      hs,
      {
        icon: /* @__PURE__ */ s.jsx(_c, { size: 22 }),
        title: `No ${i} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ s.jsxs("button", { type: "button", className: "wf-link-button", onClick: U, children: [
          /* @__PURE__ */ s.jsx(Ht, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ s.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ s.jsx(
            "input",
            {
              ref: I,
              type: "checkbox",
              checked: B,
              onChange: (V) => q(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ s.jsx("span", { children: "Name" }),
          /* @__PURE__ */ s.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ s.jsx("span", { children: i === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ s.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ s.jsx("span", { children: "Actions" })
        ] }),
        m.map((V) => /* @__PURE__ */ s.jsxs(
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
              /* @__PURE__ */ s.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ s.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(V.id),
                  onChange: (ee) => P(V.id, ee.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ s.jsxs("span", { children: [
                /* @__PURE__ */ s.jsx("strong", { children: V.name }),
                /* @__PURE__ */ s.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ s.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ s.jsx("span", { children: i === "deleted" ? Ke(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ s.jsx("span", { children: Ke(V.lastModifiedAt) }),
              /* @__PURE__ */ s.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: i === "active" ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
                /* @__PURE__ */ s.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ s.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), te(V.id);
                }, children: "Artifacts" }),
                M ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Ct(t, M, V), children: [
                  /* @__PURE__ */ s.jsx(ot, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ s.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(V);
                }, children: [
                  /* @__PURE__ */ s.jsx(wn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
                /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
                  oe(V);
                }, children: [
                  /* @__PURE__ */ s.jsx(Oi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ s.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  ue(V);
                }, children: [
                  /* @__PURE__ */ s.jsx(wn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ s.jsx(
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
    S ? /* @__PURE__ */ s.jsx(
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
  const r = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), i = de(() => bN(e), [e]);
  return i.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ s.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: i.map((a) => {
    const c = r.get(a.activityType), u = Yo(c), l = c ? Ce(c) : Bo(a.activityType) ?? a.activityType, d = Bo(a.activityType) ?? a.activityType, f = NN(a.startedAt ?? a.scheduledAt), p = Ku(a.startedAt, a.completedAt);
    return /* @__PURE__ */ s.jsx("li", { children: /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ s.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: us(u) }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ s.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ s.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ s.jsxs("span", { className: "wf-timeline-meta", children: [
            f ? /* @__PURE__ */ s.jsx("time", { children: f }) : null,
            p ? /* @__PURE__ */ s.jsxs("small", { children: [
              "took ",
              p
            ] }) : null
          ] }),
          /* @__PURE__ */ s.jsx(vN, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function vN({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ s.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function bN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => vc(t.activity) - vc(n.activity) || t.index - n.index).map((t) => t.activity);
}
function vc(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function NN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function jN({ context: e }) {
  const [t, n] = F("loading"), [o, r] = F(""), [i, a] = F(""), [c, u] = F(""), [l, d] = F([]), f = ie(async () => {
    n("loading"), r("");
    try {
      const h = await fp(e, {
        status: i || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, i]);
  G(() => {
    f();
  }, [f]);
  const p = (h) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(h)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsx("button", { type: "button", onClick: () => {
        f();
      }, children: "Refresh" }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Status" }),
        /* @__PURE__ */ s.jsxs("select", { "aria-label": "Workflow run status", value: i, onChange: (h) => a(h.target.value), children: [
          /* @__PURE__ */ s.jsx("option", { value: "", children: "All statuses" }),
          /* @__PURE__ */ s.jsx("option", { value: "Pending", children: "Pending" }),
          /* @__PURE__ */ s.jsx("option", { value: "Running", children: "Running" }),
          /* @__PURE__ */ s.jsx("option", { value: "Suspended", children: "Suspended" }),
          /* @__PURE__ */ s.jsx("option", { value: "Completed", children: "Completed" }),
          /* @__PURE__ */ s.jsx("option", { value: "Faulted", children: "Faulted" }),
          /* @__PURE__ */ s.jsx("option", { value: "Cancelled", children: "Cancelled" })
        ] })
      ] }),
      /* @__PURE__ */ s.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ s.jsxs("select", { "aria-label": "Run Kind", value: c, onChange: (h) => u(h.target.value), children: [
          /* @__PURE__ */ s.jsx("option", { value: "", children: "All kinds" }),
          /* @__PURE__ */ s.jsx("option", { value: "TestRun", children: "Test Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "PublishedRun", children: "Published Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "BackgroundWeaverRun", children: "Background Weaver Run" }),
          /* @__PURE__ */ s.jsx("option", { value: "Unknown", children: "Unknown / legacy" })
        ] })
      ] })
    ] }),
    t === "failed" ? /* @__PURE__ */ s.jsx(Hn, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ s.jsx(ps, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ s.jsx(
      hs,
      {
        icon: /* @__PURE__ */ s.jsx(Ko, { size: 22 }),
        title: "No workflow runs yet",
        description: "Run a published workflow executable to create execution history here."
      }
    ) : null,
    t === "ready" && l.length > 0 ? /* @__PURE__ */ s.jsxs("div", { className: "wf-grid wf-instance-grid", role: "table", "aria-label": "Workflow runs", children: [
      /* @__PURE__ */ s.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ s.jsx("span", { children: "Run" }),
        /* @__PURE__ */ s.jsx("span", { children: "Kind" }),
        /* @__PURE__ */ s.jsx("span", { children: "Status" }),
        /* @__PURE__ */ s.jsx("span", { children: "Definition" }),
        /* @__PURE__ */ s.jsx("span", { children: "Activity" }),
        /* @__PURE__ */ s.jsx("span", { children: "Started" }),
        /* @__PURE__ */ s.jsx("span", { children: "Duration" })
      ] }),
      l.map((h) => /* @__PURE__ */ s.jsxs(
        "button",
        {
          type: "button",
          className: "wf-grid-row",
          role: "row",
          "aria-label": `Inspect workflow run ${h.workflowExecutionId}`,
          onClick: () => p(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ s.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ s.jsx("span", { children: td(h.runKind) }),
            /* @__PURE__ */ s.jsx("span", { children: /* @__PURE__ */ s.jsx(On, { status: h.status, subStatus: h.subStatus }) }),
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsx("strong", { children: h.definitionId }),
              /* @__PURE__ */ s.jsx("small", { children: h.definitionVersionId })
            ] }),
            /* @__PURE__ */ s.jsxs("span", { children: [
              /* @__PURE__ */ s.jsxs("strong", { children: [
                h.activityCount,
                " activities"
              ] }),
              /* @__PURE__ */ s.jsxs("small", { children: [
                h.incidentCount,
                " incidents"
              ] })
            ] }),
            /* @__PURE__ */ s.jsx("span", { children: Ke(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ s.jsx("span", { children: Ku(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function SN({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = F("loading"), [i, a] = F(""), [c, u] = F(null), [l, d] = F(null), f = St(t, "weaver.workflows.explain-instance"), p = ie(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const g = await pp(e, n), [x, v] = await Promise.all([
        np(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Fi(e)
      ]);
      u({
        details: g,
        definitionVersion: x.definitionVersion,
        definitionVersionError: x.error,
        activityCatalog: v.activities
      }), d(null), r("ready");
    } catch (g) {
      u(null), a(W0(g, n)), r("failed");
    }
  }, [e, n]);
  G(() => {
    p();
  }, [p]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
    /* @__PURE__ */ s.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ s.jsx(Eo, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => {
        p();
      }, children: [
        /* @__PURE__ */ s.jsx(Oi, { size: 14 }),
        " Refresh"
      ] }),
      c && f ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Ct(t, f, c.details), children: [
        /* @__PURE__ */ s.jsx(ot, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ s.jsx(Hn, { message: i }) : null,
    o === "ready" && c ? /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ s.jsx(
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
      /* @__PURE__ */ s.jsx(
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
function CN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: i }) {
  const a = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Wi(c, u), d = l === "unsupported" ? null : vn(c, []), f = l === "unsupported" ? hi(c, n, e.layout) : d ? Hc(d, n, e.layout) : hi(c, n, e.layout), p = f.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: vf(p, o.activities, o.incidents, r),
      edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
    };
  }, [n, e, o, r]);
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Workflow run canvas", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Definition version" }),
        /* @__PURE__ */ s.jsx("h3", { children: e ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          e.definition.name,
          " ",
          /* @__PURE__ */ s.jsx("small", { children: e.version })
        ] }) : /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
          "Definition graph unavailable ",
          /* @__PURE__ */ s.jsx("small", { children: o.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ s.jsx(On, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ s.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ s.jsx("small", { children: H0(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ s.jsxs(
        zu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: sd,
          edgeTypes: ad,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => i(u.id),
          onPaneClick: () => i(null),
          children: [
            /* @__PURE__ */ s.jsx(Ou, {}),
            /* @__PURE__ */ s.jsx(Fu, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ s.jsx(Wu, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function EN({ ai: e, action: t, summary: n, details: o, state: r, error: i, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, f] = F("timeline");
  if (!n)
    return /* @__PURE__ */ s.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const p = o?.incidents.length ?? 0, h = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ s.jsx(Vi, { size: 14 }), render: () => null },
    { id: "issues", title: p > 0 ? `Issues (${p})` : "Issues", order: 1, icon: /* @__PURE__ */ s.jsx(ft, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ s.jsx(zi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ s.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ s.jsxs("header", { children: [
      /* @__PURE__ */ s.jsxs("div", { children: [
        /* @__PURE__ */ s.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ s.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ s.jsxs("button", { type: "button", onClick: () => Ct(e, t, o ?? n), children: [
        /* @__PURE__ */ s.jsx(ot, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ s.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ s.jsx(jo, { label: "Workflow run tabs", tabs: h, activeTabId: d, onSelect: (g) => f(g) }) }),
    r === "loading" ? /* @__PURE__ */ s.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ s.jsx(Hn, { message: i }) : null,
    r === "ready" && o ? /* @__PURE__ */ s.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ s.jsx(
      wN,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : d === "issues" ? /* @__PURE__ */ s.jsxs(s.Fragment, { children: [
      /* @__PURE__ */ s.jsx(IN, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ s.jsx(kN, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ s.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ s.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ s.jsx("dd", { children: /* @__PURE__ */ s.jsx(On, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ s.jsx("dd", { children: td(n.runKind) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Artifact" }),
      /* @__PURE__ */ s.jsxs("dd", { children: [
        n.artifactId,
        " ",
        /* @__PURE__ */ s.jsx("small", { children: n.artifactVersion })
      ] }),
      /* @__PURE__ */ s.jsx("dt", { children: "Definition" }),
      /* @__PURE__ */ s.jsxs("dd", { children: [
        n.definitionId,
        " ",
        /* @__PURE__ */ s.jsx("small", { children: n.definitionVersionId })
      ] }),
      /* @__PURE__ */ s.jsx("dt", { children: "Created" }),
      /* @__PURE__ */ s.jsx("dd", { children: Ke(n.createdAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ s.jsx("dd", { children: Ke(n.startedAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ s.jsx("dd", { children: Ke(n.completedAt) }),
      /* @__PURE__ */ s.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ s.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function IN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ s.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ s.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((o) => /* @__PURE__ */ s.jsxs(
      "button",
      {
        type: "button",
        className: "wf-instance-incident",
        "data-severity": o.severity.toLowerCase(),
        "data-selected": o.incidentId === t,
        onClick: () => n?.(o.incidentId),
        children: [
          /* @__PURE__ */ s.jsx("strong", { children: o.failureType }),
          /* @__PURE__ */ s.jsxs("span", { children: [
            o.status,
            " · ",
            o.severity
          ] }),
          /* @__PURE__ */ s.jsx("p", { children: o.message })
        ]
      },
      o.incidentId
    ))
  ] });
}
function kN({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((i) => [i.activityExecutionId, i])), o = e.activities.filter((i) => !t.has(sc(i))), r = e.incidents.filter((i) => {
    const a = i.activityExecutionId ? n.get(i.activityExecutionId) : null, c = i.executableNodeId ?? (a ? sc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ s.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ s.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((i) => /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ s.jsx("strong", { children: Bo(i.activityType) ?? i.activityType }),
        /* @__PURE__ */ s.jsx("small", { children: i.activityExecutionId })
      ] }, `activity-${i.activityExecutionId}`)),
      r.map((i) => /* @__PURE__ */ s.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ s.jsx("strong", { children: i.failureType }),
        /* @__PURE__ */ s.jsx("small", { children: i.incidentId })
      ] }, `incident-${i.incidentId}`))
    ] })
  ] });
}
function AN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [i, a] = F(bc);
  G(() => {
    const u = () => a(bc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return i ? /* @__PURE__ */ s.jsx(hN, { context: e, definitionId: i, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ s.jsx(cr, { title: "Definitions", children: /* @__PURE__ */ s.jsx(xN, { context: e, ai: t, onOpen: c }) });
}
function _N({ context: e, ai: t }) {
  const [n, o] = F(Nc);
  G(() => {
    const i = () => o(Nc());
    return window.addEventListener("popstate", i), () => window.removeEventListener("popstate", i);
  }, []);
  const r = ie((i) => {
    const a = i?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ s.jsx(cr, { title: "Executables", children: /* @__PURE__ */ s.jsx(nb, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function DN({ context: e }) {
  return /* @__PURE__ */ s.jsx(cr, { title: "Runs", children: /* @__PURE__ */ s.jsx(jN, { context: e }) });
}
function TN({ context: e, ai: t }) {
  const n = $N();
  return /* @__PURE__ */ s.jsx(cr, { title: "Run", children: /* @__PURE__ */ s.jsx(SN, { context: e, ai: t, workflowExecutionId: n }) });
}
function cr({ title: e, children: t }) {
  return /* @__PURE__ */ s.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ s.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ s.jsxs("div", { children: [
      /* @__PURE__ */ s.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ s.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function bc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Nc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function $N() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function zN(e) {
  Fd(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ s.jsx(AN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ s.jsx(_N, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ s.jsx(DN, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ s.jsx(TN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ s.jsx(Mp, { context: e.backend })
      }
    ]
  });
}
export {
  L0 as isConnectEndOverExistingWorkflowNode,
  zN as register,
  z0 as resolveConnectEndSource
};
