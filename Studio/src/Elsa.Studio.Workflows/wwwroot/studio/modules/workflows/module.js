import it, { useMemo as ue, useState as U, useEffect as Q, memo as Ne, forwardRef as Rc, useRef as re, useCallback as se, useContext as no, createContext as Ui, useLayoutEffect as nf, useId as zc, lazy as of, Suspense as rf } from "react";
import { ListChecks as sf, Save as Lc, EyeOff as Ps, Shield as Rs, AlertTriangle as Qo, SlidersHorizontal as Xi, Activity as af, Search as vr, Maximize2 as er, GripVertical as Vc, ChevronUp as cf, ChevronDown as Oc, Trash2 as Kn, Plus as tn, X as Hc, Boxes as br, Zap as lf, Play as nn, Terminal as uf, ListTree as qi, GitBranch as Wc, Check as fn, AlertCircle as jt, Wrench as df, Copy as ff, Sparkles as ut, RotateCcw as Yi, Package as Fc, ChevronRight as Yt, Undo2 as pf, Redo2 as hf, Network as gf, Download as yf, ChevronLeft as tr, Minimize2 as zs, Workflow as mf, Code2 as xf } from "lucide-react";
import { useQuery as Bc, useQueryClient as wf, useMutation as vf } from "@tanstack/react-query";
function bf(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var oi = { exports: {} }, kn = {};
var Ls;
function Nf() {
  if (Ls) return kn;
  Ls = 1;
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
  return kn.Fragment = t, kn.jsx = n, kn.jsxs = n, kn;
}
var Vs;
function jf() {
  return Vs || (Vs = 1, oi.exports = Nf()), oi.exports;
}
var i = jf();
let Kc;
function Sf(e) {
  Kc = e;
}
function Os() {
  return Kc;
}
const Ef = "String", Cf = "singleline";
function If(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Zi(e, t = "Single") {
  return { alias: (e ?? "").trim() || Ef, collectionKind: t };
}
function Uc(e) {
  const t = e.type ?? e.Type;
  if (nr(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: If(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return nr(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: Hs(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: Hs(e) ? "Array" : "Single" };
}
function Hs(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Ws(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Nr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function kf(e, t) {
  const n = new Set(t);
  let o = 1, r = `${e}${o}`;
  for (; n.has(r); )
    o += 1, r = `${e}${o}`;
  return r;
}
function Af(e) {
  return {
    referenceKey: Nr(),
    name: e.name,
    type: Zi(e.alias),
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
    referenceKey: Nr(),
    name: e.name,
    type: Zi(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: Cf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Mf(e, t) {
  return { ...e, ...t };
}
function Pf(e) {
  return {
    referenceKey: Nr(),
    name: e.name,
    type: Zi(e.alias),
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
function Xc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : zf(e || t);
}
function Lf(e, t) {
  return Xc(e, t).replace(/StorageDriver$/, "");
}
function nr(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function On(e, t) {
  for (const n of t) {
    const o = e[n];
    if (o != null && typeof o != "object") return String(o);
  }
  return "";
}
const Vf = ["name", "Name"], qc = ["name", "Name"], Of = ["storageDriverType", "StorageDriverType"], Yc = ["referenceKey", "ReferenceKey"], Hf = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function oo(e) {
  return Zc(e, Kf);
}
function jr(e) {
  return Zc(e, Uf);
}
function Zc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Gc(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Wo(e.variables, Ni)), Array.isArray(e.inputs) && (n.inputs = Wo(e.inputs, Ni)), Array.isArray(e.outputs) && (n.outputs = Wo(e.outputs, (o) => Jc(o, !1))), n;
}
function Gc(e, t) {
  const n = t(e), o = n.structure;
  if (!o || !st(o.payload)) return n;
  let r = !1;
  const s = { ...o.payload };
  for (const [a, c] of Object.entries(o.payload))
    Array.isArray(c) && c.length > 0 && c.every(Gf) && (s[a] = c.map((u) => Gc(u, t)), r = !0);
  return Array.isArray(o.payload.variables) && o.payload.variables.length > 0 && (s.variables = Wo(o.payload.variables, Ni), r = !0), r ? { ...n, structure: { ...o, payload: s } } : n;
}
function Wo(e, t) {
  return e.map((n) => st(n) && !Array.isArray(n) ? t(n) : n);
}
function Ni(e) {
  return Jc(e, !0);
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
function Jc(e, t) {
  const n = Ff(e, Wf);
  return On(e, Yc).trim() || (n.referenceKey = Nr()), n.type = Uc(e), t && (n.storageDriverType = Bf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Ff(e, t) {
  const n = new Set(t), o = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (o[r] = s);
  return o;
}
function Bf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (st(e)) {
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
      referenceKey: Xf(r),
      value: Yf(s.expression)
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
function Uf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const o of t) {
    if (!st(o) || typeof o.referenceKey != "string") continue;
    const r = st(o.value) ? o.value : {};
    n[qf(o.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Xf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function qf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Yf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && st(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && st(e.value) ? { value: Fs(e.value), expressionType: "Object" } : { value: Fs(e.value), expressionType: t };
}
function Fs(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Zf(e) {
  if (!st(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return st(t) && typeof t.type == "string";
}
function Gf(e) {
  return st(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function st(e) {
  return typeof e == "object" && e !== null;
}
const ro = "elsa.sequence.structure", pn = "elsa.flowchart.structure";
function Qc(e, t) {
  if (!e) return null;
  let n = e;
  for (const o of t) {
    const r = Me(n).find((a) => a.id === o.slotId);
    if (!r) return null;
    const s = r.activities.find((a) => a.nodeId === o.ownerNodeId);
    if (!s) return null;
    n = s;
  }
  return n;
}
function Jf(e, t, n = (o) => o.nodeId) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (r, s) => {
    const a = Me(r);
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
function Hn(e, t) {
  const n = Qc(e, t);
  if (!n) return null;
  const o = Me(n)[0];
  return o ? { owner: n, slot: o } : null;
}
function Me(e) {
  const t = e.structure;
  if (!t || !t.payload || typeof t.payload != "object") return [];
  const n = t.payload, o = mp(t), r = ri(n.activities);
  return r ? [{
    id: `${t.kind}:activities`,
    label: xp(t),
    property: "activities",
    mode: o,
    activities: r
  }] : Object.entries(n).filter(([, s]) => ri(s)).map(([s, a]) => ({
    id: `${t.kind}:${s}`,
    label: vp(s),
    property: s,
    mode: "generic",
    activities: ri(a) ?? []
  }));
}
function el(e, t, n) {
  const o = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = o.get(a.activityVersionId), l = r.get(a.nodeId) ?? wp(e.slot.mode, c);
    return ol(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? rl(e.owner) : lp(e.slot, s)
  };
}
function ji(e, t, n) {
  const o = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [ol(e, o, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Qf(e, t, n, o = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = Ks(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ks(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = hp(u), p = o === c.id || u.some((h) => h.activityExecutionId === o) || l.some((h) => h.incidentId === o), f = {
      status: d?.status,
      subStatus: d?.subStatus,
      activityExecutionId: d?.activityExecutionId,
      faultCount: u.reduce((h, g) => h + g.faultCount + g.aggregateFaultCount, 0),
      incidentCount: l.length,
      hasBlockingIncident: l.some((h) => h.isBlocking),
      selected: p
    };
    return {
      ...c,
      selected: p,
      className: p ? "wf-runtime-node-selected" : c.className,
      data: {
        ...c.data,
        runtime: f
      }
    };
  });
}
function Gi(e, t) {
  return e?.structure?.kind === pn || rp(t) ? "flowchart" : e?.structure?.kind === ro || ip(t) ? "sequence" : "unsupported";
}
function Si(e, t, n) {
  if (t.length === 0) {
    const c = Me(e)[0];
    return c ? Un(e, c, n) : e;
  }
  const [o, ...r] = t, s = Me(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? Si(c, r, n) : c);
  return Un(e, s, a);
}
function tl(e, t, n) {
  if (t.length === 0) return n;
  const [o, ...r] = t, s = Me(e).find((c) => c.id === o.slotId);
  if (!s) return e;
  const a = s.activities.map((c) => c.nodeId === o.ownerNodeId ? tl(c, r, n) : c);
  return Un(e, s, a);
}
function nl(e, t, n) {
  if (e.nodeId === t) return n(e);
  const o = Me(e);
  if (o.length === 0) return e;
  let r = !1, s = e;
  for (const a of o) {
    const c = a.activities.map((u) => {
      const l = nl(u, t, n);
      return l !== u && (r = !0), l;
    });
    r && (s = Un(s, a, c));
  }
  return r ? s : e;
}
function Un(e, t, n) {
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
function ep(e, t, n, o = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of o)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Un(e.owner, e.slot, s);
}
function tp(e, t) {
  return {
    ...e,
    structure: cp(e.structure, t)
  };
}
function np(e, t) {
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
function Ei(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: ap(e)
  };
}
function Ee(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? sp(t) : n;
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
      label: t ? Ee(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: or(t),
      childSlots: Me(e),
      acceptsInbound: up(e, t),
      sourcePorts: o.suppressFlowPorts ? [] : il(e, t),
      suppressFlowPorts: o.suppressFlowPorts
    }
  };
}
function or(e) {
  if (!e) return "activity";
  const t = op(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), o = Ee(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || o === "flowchart" ? "flowchart" : n.endsWith(".sequence") || o === "sequence" ? "sequence" : n.includes("writeline") || o.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function op(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function rp(e) {
  return !!e && (Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function ip(e) {
  return !!e && (Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function sp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function ap(e) {
  return e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: ro,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: pn,
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
function cp(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], o = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!Ji(r)) continue;
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
function lp(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, o) => ({
    id: `sequence-${n.id}-${t[o + 1].id}`,
    source: n.id,
    target: t[o + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function rl(e) {
  if (e.structure?.kind !== pn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, o) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(gp) : [];
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
  }).filter((n) => !!n) : [];
}
function il(e, t) {
  const n = Bs(e.cases);
  if (fp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const o = [
    ...Fo(t?.designFacets),
    ...Fo(t?.ports),
    ...Fo(t?.outputs)
  ];
  if (o.length > 0) return pp(o);
  const r = Bs(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function up(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function rr(e, t, n, o) {
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
function dp(e, t, n) {
  const o = rr(t.source, n, t.sourceHandle ?? "Done", void 0), r = rr(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(o, r);
}
function ri(e) {
  return Array.isArray(e) ? e.filter(yp) : null;
}
function fp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, o = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || o === "FlowSwitch";
}
function Fo(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!Ji(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Fo(n.ports));
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
function pp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Bs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ks(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e) {
    const r = t(o);
    r && n.set(r, [...n.get(r) ?? [], o]);
  }
  return n;
}
function hp(e) {
  return [...e].sort((t, n) => Us(n).localeCompare(Us(t)))[0];
}
function Us(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function gp(e) {
  return Ji(e) && typeof e.x == "number" && typeof e.y == "number";
}
function Ji(e) {
  return typeof e == "object" && e !== null;
}
function yp(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function mp(e) {
  return e.kind === ro ? "sequence" : e.kind === pn ? "flowchart" : "generic";
}
function xp(e) {
  return e.kind === ro || e.kind === pn, "Activities";
}
function wp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function vp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const ir = "workflow", bp = /* @__PURE__ */ new Set([ro, pn]);
function Np(e) {
  const t = e?.structure?.kind;
  return !!t && bp.has(t);
}
function sl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(nr) : [];
}
function jp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function Sp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== ir ? t : ir
  };
}
function al(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return al(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function Ep(e) {
  if (!e) return "";
  const t = [`workflow:${Xs(e.variables)}`], n = (o) => {
    const r = Me(o), s = r.flatMap((a) => a.activities.map((c) => c.nodeId));
    t.push(`${o.nodeId}:${Xs(sl(o))}>${s.join(",")}`), r.forEach((a) => a.activities.forEach(n));
  };
  return e.rootActivity && n(e.rootActivity), t.join(";");
}
function Xs(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function Cp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const o of e ?? [])
    o.scopeId === t && n.set(o.referenceKey, `Shadows "${o.name}" declared in an outer scope.`);
  return n;
}
const je = "/_elsa/workflow-management", Ip = "/publishing", Wn = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function kp(e) {
  return Bc({
    queryKey: Wn.activityAvailabilitySettings,
    queryFn: () => Xp(e)
  });
}
function Ap(e) {
  return Bc({
    queryKey: Wn.activityAvailabilityDiagnostics,
    queryFn: () => ul(e)
  });
}
function _p(e) {
  const t = wf();
  return vf({
    mutationFn: (n) => qp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: Wn.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: Wn.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: Wn.activities });
    }
  });
}
async function Dp(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), o = t.search.trim();
  return o && n.set("search", o), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function Tp(e, t) {
  const n = await e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: jr(n.draft.state) } } : n;
}
async function $p(e, t, n) {
  const o = await e.http.postJson(
    `${je}/design/scoped-variables/analyze`,
    { state: oo(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(o?.visibleVariables) ? o.visibleVariables : [],
    shadowingWarnings: Array.isArray(o?.shadowingWarnings) ? o.shadowingWarnings : []
  };
}
const ii = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Mp(e, t, n) {
  const o = ue(() => Ep(t), [t]), [r, s] = U(() => ii("loading"));
  return Q(() => {
    if (!t) {
      s(ii("unavailable"));
      return;
    }
    let a = !1;
    return s((c) => ({ ...c, status: "loading" })), $p(e, t, n).then(
      (c) => {
        a || s({ ...c, status: "ready" });
      },
      () => {
        a || s(ii("unavailable"));
      }
    ), () => {
      a = !0;
    };
  }, [e, n, o]), r;
}
async function Pp(e, t) {
  const n = await e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: jr(n.state) };
}
async function Rp(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function zp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function Lp(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Vp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Op(e, t, n) {
  return e.http.requestJson(
    `${je}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Hp(e, t) {
  const n = await e.http.putJson(
    `${je}/drafts/${encodeURIComponent(t.id)}`,
    { state: oo(t.state), layout: t.layout }
  );
  return { ...n, state: jr(n.state) };
}
async function Wp(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Fp(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Bp(e, t) {
  const n = { ...t, state: oo(t.state) };
  try {
    return await e.http.postJson(`${Ip}/workflows/drafts/test-runs`, n);
  } catch (o) {
    const r = th(o);
    if (r) return r;
    throw o;
  }
}
async function cl(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function ll(e) {
  return e.http.getJson("/_demo/workflows/executables");
}
async function Kp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const o = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${o ? `?${o}` : ""}`);
}
async function Up(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Qi(e) {
  return e.http.getJson(`${je}/activities`);
}
async function Xp(e) {
  return e.http.getJson(`${je}/activities/availability/settings`);
}
async function qp(e, t) {
  return e.http.putJson(`${je}/activities/availability/settings`, t);
}
async function ul(e) {
  return e.http.getJson(`${je}/activities/availability/diagnostics`);
}
async function Yp(e) {
  const t = await Sr(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? qs(t) : qs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Zp(e) {
  const t = await Sr(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Bo;
}
async function Gp(e) {
  const t = await Sr(e, [
    `${je}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => Jp(o));
}
function Jp(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, o = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || o;
}
async function Qp(e) {
  const t = await Sr(e, [
    `${je}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((o) => eh(o));
}
function eh(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Sr(e, t) {
  let n;
  for (const o of t)
    try {
      return await e.http.getJson(o);
    } catch (r) {
      n = r;
    }
  throw n;
}
function qs(e) {
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
function th(e) {
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
const Bo = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], nh = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], oh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function _t(e) {
  return typeof e == "string" ? e : typeof e == "number" ? nh[e] ?? "Available" : "Available";
}
function sr(e) {
  const t = _t(e);
  return oh[t] ?? t;
}
function rh(e) {
  return _t(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ih(e) {
  return _t(e) !== "Available";
}
function sh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function ah(e) {
  return e === "Only" ? 1 : 0;
}
function Zs(e) {
  const t = e?.rules;
  return {
    mode: sh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function ch(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function lh(e) {
  return [...e?.items ?? []].filter(ch).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => ar(t).localeCompare(ar(n)));
}
function uh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = _t(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Gs(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, o) => n.localeCompare(o));
}
function ar(e) {
  const t = e?.displayName?.trim();
  if (t) return t;
  const n = e?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return dh(n) || e?.activityTypeKey || "Activity";
}
function dh(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").trim();
}
function fh(e, t) {
  const n = new Set(e.filter((o) => !!o));
  return (t?.items ?? []).find((o) => ih(o.state) ? [o.activityDefinitionId, o.activityTypeKey, o.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
function ph({ context: e }) {
  const t = kp(e), n = Ap(e), o = _p(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = o.isPending, [u, l] = U(() => Zs(r)), [d, p] = U(""), [f, h] = U(null);
  Q(() => {
    l(Zs(r));
  }, [r]);
  const g = ue(() => lh(s), [s]), b = ue(() => uh(s), [s]), w = s?.sets ?? [], m = ue(() => {
    const A = d.trim().toLowerCase();
    return A ? g.filter(
      (z) => ar(z).toLowerCase().includes(A) || (z.activityTypeKey ?? "").toLowerCase().includes(A)
    ) : g;
  }, [g, d]), j = new Set(u.activityTypes), y = new Set(u.sets), x = g.filter((A) => _t(A.state) === "BlockedByHostBaseline").length, N = g.filter((A) => _t(A.state) === "HiddenByManagementSettings").length, v = o.error ?? t.error ?? n.error, S = v instanceof Error ? v.message : v ? "Activity availability could not be loaded." : null, k = (A) => l((z) => ({ ...z, mode: A })), M = (A) => l((z) => ({ ...z, activityTypes: Gs(z.activityTypes, A) })), R = (A) => l((z) => ({ ...z, sets: Gs(z.sets, A) })), T = () => {
    h(null), o.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: ah(u.mode),
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
          /* @__PURE__ */ i.jsx(sf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "availability-save", onClick: T, disabled: a || c, children: [
        /* @__PURE__ */ i.jsx(Lc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      f && !S && /* @__PURE__ */ i.jsx("div", { className: "availability-banner availability-banner-success", children: f }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => k("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(Ps, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => k("Only"), disabled: a || c, children: [
          /* @__PURE__ */ i.jsx(Rs, { size: 15 }),
          /* @__PURE__ */ i.jsxs("span", { children: [
            /* @__PURE__ */ i.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ i.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Rs, { size: 14 }),
          " ",
          x,
          " host blocked"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Ps, { size: 14 }),
          " ",
          N,
          " management hidden"
        ] }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx(Qo, { size: 14 }),
          " ",
          b.length,
          " unresolved"
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(Xi, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-set-list", children: w.map((A) => /* @__PURE__ */ i.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ i.jsx("input", { type: "checkbox", checked: y.has(A.name), disabled: a || c, onChange: () => R(A.name) }),
          /* @__PURE__ */ i.jsx("span", { children: A.name }),
          /* @__PURE__ */ i.jsx("code", { children: (A.activityTypeKeys ?? []).length })
        ] }, A.name)) })
      ] }),
      /* @__PURE__ */ i.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ i.jsx(af, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ i.jsx(vr, { size: 14 }),
            /* @__PURE__ */ i.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (A) => p(A.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { className: "availability-activity-list", children: [
          a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && g.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && g.length > 0 && m.length === 0 && /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((A) => {
            const E = _t(A.state) === "BlockedByHostBaseline", D = A.activityTypeKey ?? A.activityDefinitionId ?? "";
            return /* @__PURE__ */ i.jsxs("label", { className: `availability-activity-option ${E ? "disabled" : ""}`, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(D),
                  disabled: a || c || E,
                  onChange: () => M(D)
                }
              ),
              /* @__PURE__ */ i.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ i.jsx("strong", { children: ar(A) }),
                /* @__PURE__ */ i.jsx("code", { children: A.activityTypeKey })
              ] }),
              /* @__PURE__ */ i.jsx("em", { className: `availability-state ${rh(A.state)}`, children: sr(A.state) })
            ] }, D);
          })
        ] })
      ] }),
      b.length > 0 && /* @__PURE__ */ i.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ i.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ i.jsx(Qo, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ i.jsx("div", { className: "availability-unresolved-list", children: b.map((A) => /* @__PURE__ */ i.jsxs("span", { children: [
          /* @__PURE__ */ i.jsx("strong", { children: A.referenceName }),
          /* @__PURE__ */ i.jsx("em", { children: sr(A.state) })
        ] }, `${A.layer}-${A.referenceKind}-${A.referenceName}`)) })
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
var hh = { value: () => {
} };
function Er() {
  for (var e = 0, t = arguments.length, n = {}, o; e < t; ++e) {
    if (!(o = arguments[e] + "") || o in n || /[\s.]/.test(o)) throw new Error("illegal type: " + o);
    n[o] = [];
  }
  return new Ko(n);
}
function Ko(e) {
  this._ = e;
}
function gh(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var o = "", r = n.indexOf(".");
    if (r >= 0 && (o = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: o };
  });
}
Ko.prototype = Er.prototype = {
  constructor: Ko,
  on: function(e, t) {
    var n = this._, o = gh(e + "", n), r, s = -1, a = o.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = o[s]).type) && (r = yh(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = o[s]).type) n[r] = Js(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Js(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ko(e);
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
function yh(e, t) {
  for (var n = 0, o = e.length, r; n < o; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Js(e, t, n) {
  for (var o = 0, r = e.length; o < r; ++o)
    if (e[o].name === t) {
      e[o] = hh, e = e.slice(0, o).concat(e.slice(o + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Ci = "http://www.w3.org/1999/xhtml";
const Qs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Ci,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Cr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Qs.hasOwnProperty(t) ? { space: Qs[t], local: e } : e;
}
function mh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Ci && t.documentElement.namespaceURI === Ci ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function xh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function dl(e) {
  var t = Cr(e);
  return (t.local ? xh : mh)(t);
}
function wh() {
}
function es(e) {
  return e == null ? wh : function() {
    return this.querySelector(e);
  };
}
function vh(e) {
  typeof e != "function" && (e = es(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Fe(o, this._parents);
}
function bh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Nh() {
  return [];
}
function fl(e) {
  return e == null ? Nh : function() {
    return this.querySelectorAll(e);
  };
}
function jh(e) {
  return function() {
    return bh(e.apply(this, arguments));
  };
}
function Sh(e) {
  typeof e == "function" ? e = jh(e) : e = fl(e);
  for (var t = this._groups, n = t.length, o = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (o.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Fe(o, r);
}
function pl(e) {
  return function() {
    return this.matches(e);
  };
}
function hl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Eh = Array.prototype.find;
function Ch(e) {
  return function() {
    return Eh.call(this.children, e);
  };
}
function Ih() {
  return this.firstElementChild;
}
function kh(e) {
  return this.select(e == null ? Ih : Ch(typeof e == "function" ? e : hl(e)));
}
var Ah = Array.prototype.filter;
function _h() {
  return Array.from(this.children);
}
function Dh(e) {
  return function() {
    return Ah.call(this.children, e);
  };
}
function Th(e) {
  return this.selectAll(e == null ? _h : Dh(typeof e == "function" ? e : hl(e)));
}
function $h(e) {
  typeof e != "function" && (e = pl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Fe(o, this._parents);
}
function gl(e) {
  return new Array(e.length);
}
function Mh() {
  return new Fe(this._enter || this._groups.map(gl), this._parents);
}
function cr(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
cr.prototype = {
  constructor: cr,
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
function Ph(e) {
  return function() {
    return e;
  };
}
function Rh(e, t, n, o, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], o[a] = c) : n[a] = new cr(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function zh(e, t, n, o, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, p = s.length, f = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (f[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < p; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (o[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new cr(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(f[c]) === u && (r[c] = u);
}
function Lh(e) {
  return e.__data__;
}
function Vh(e, t) {
  if (!arguments.length) return Array.from(this, Lh);
  var n = t ? zh : Rh, o = this._parents, r = this._groups;
  typeof e != "function" && (e = Ph(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = o[l], p = r[l], f = p.length, h = Oh(e.call(d, d && d.__data__, l, o)), g = h.length, b = c[l] = new Array(g), w = a[l] = new Array(g), m = u[l] = new Array(f);
    n(d, p, b, w, m, h, t);
    for (var j = 0, y = 0, x, N; j < g; ++j)
      if (x = b[j]) {
        for (j >= y && (y = j + 1); !(N = w[y]) && ++y < g; ) ;
        x._next = N || null;
      }
  }
  return a = new Fe(a, o), a._enter = c, a._exit = u, a;
}
function Oh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Hh() {
  return new Fe(this._exit || this._groups.map(gl), this._parents);
}
function Wh(e, t, n) {
  var o = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (o = e(o), o && (o = o.selection())) : o = o.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), o && r ? o.merge(r).order() : r;
}
function Fh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, o = t._groups, r = n.length, s = o.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = o[u], p = l.length, f = c[u] = new Array(p), h, g = 0; g < p; ++g)
      (h = l[g] || d[g]) && (f[g] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Fe(c, this._parents);
}
function Bh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var o = e[t], r = o.length - 1, s = o[r], a; --r >= 0; )
      (a = o[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Kh(e) {
  e || (e = Uh);
  function t(p, f) {
    return p && f ? e(p.__data__, f.__data__) : !p - !f;
  }
  for (var n = this._groups, o = n.length, r = new Array(o), s = 0; s < o; ++s) {
    for (var a = n[s], c = a.length, u = r[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Fe(r, this._parents).order();
}
function Uh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Xh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function qh() {
  return Array.from(this);
}
function Yh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length; r < s; ++r) {
      var a = o[r];
      if (a) return a;
    }
  return null;
}
function Zh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Gh() {
  return !this.node();
}
function Jh(e) {
  for (var t = this._groups, n = 0, o = t.length; n < o; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Qh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function eg(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function tg(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function ng(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function og(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function rg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ig(e, t) {
  var n = Cr(e);
  if (arguments.length < 2) {
    var o = this.node();
    return n.local ? o.getAttributeNS(n.space, n.local) : o.getAttribute(n);
  }
  return this.each((t == null ? n.local ? eg : Qh : typeof t == "function" ? n.local ? rg : og : n.local ? ng : tg)(n, t));
}
function yl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function sg(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function ag(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function cg(e, t, n) {
  return function() {
    var o = t.apply(this, arguments);
    o == null ? this.style.removeProperty(e) : this.style.setProperty(e, o, n);
  };
}
function lg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? sg : typeof t == "function" ? cg : ag)(e, t, n ?? "")) : on(this.node(), e);
}
function on(e, t) {
  return e.style.getPropertyValue(t) || yl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ug(e) {
  return function() {
    delete this[e];
  };
}
function dg(e, t) {
  return function() {
    this[e] = t;
  };
}
function fg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function pg(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ug : typeof t == "function" ? fg : dg)(e, t)) : this.node()[e];
}
function ml(e) {
  return e.trim().split(/^|\s+/);
}
function ts(e) {
  return e.classList || new xl(e);
}
function xl(e) {
  this._node = e, this._names = ml(e.getAttribute("class") || "");
}
xl.prototype = {
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
function wl(e, t) {
  for (var n = ts(e), o = -1, r = t.length; ++o < r; ) n.add(t[o]);
}
function vl(e, t) {
  for (var n = ts(e), o = -1, r = t.length; ++o < r; ) n.remove(t[o]);
}
function hg(e) {
  return function() {
    wl(this, e);
  };
}
function gg(e) {
  return function() {
    vl(this, e);
  };
}
function yg(e, t) {
  return function() {
    (t.apply(this, arguments) ? wl : vl)(this, e);
  };
}
function mg(e, t) {
  var n = ml(e + "");
  if (arguments.length < 2) {
    for (var o = ts(this.node()), r = -1, s = n.length; ++r < s; ) if (!o.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? yg : t ? hg : gg)(n, t));
}
function xg() {
  this.textContent = "";
}
function wg(e) {
  return function() {
    this.textContent = e;
  };
}
function vg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function bg(e) {
  return arguments.length ? this.each(e == null ? xg : (typeof e == "function" ? vg : wg)(e)) : this.node().textContent;
}
function Ng() {
  this.innerHTML = "";
}
function jg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function Sg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Eg(e) {
  return arguments.length ? this.each(e == null ? Ng : (typeof e == "function" ? Sg : jg)(e)) : this.node().innerHTML;
}
function Cg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Ig() {
  return this.each(Cg);
}
function kg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Ag() {
  return this.each(kg);
}
function _g(e) {
  var t = typeof e == "function" ? e : dl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Dg() {
  return null;
}
function Tg(e, t) {
  var n = typeof e == "function" ? e : dl(e), o = t == null ? Dg : typeof t == "function" ? t : es(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), o.apply(this, arguments) || null);
  });
}
function $g() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Mg() {
  return this.each($g);
}
function Pg() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Rg() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function zg(e) {
  return this.select(e ? Rg : Pg);
}
function Lg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Vg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function Og(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", o = t.indexOf(".");
    return o >= 0 && (n = t.slice(o + 1), t = t.slice(0, o)), { type: t, name: n };
  });
}
function Hg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, o = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++o] = s;
      ++o ? t.length = o : delete this.__on;
    }
  };
}
function Wg(e, t, n) {
  return function() {
    var o = this.__on, r, s = Vg(t);
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
function Fg(e, t, n) {
  var o = Og(e + ""), r, s = o.length, a;
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
  for (c = t ? Wg : Hg, r = 0; r < s; ++r) this.each(c(o[r], t, n));
  return this;
}
function bl(e, t, n) {
  var o = yl(e), r = o.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = o.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Bg(e, t) {
  return function() {
    return bl(this, e, t);
  };
}
function Kg(e, t) {
  return function() {
    return bl(this, e, t.apply(this, arguments));
  };
}
function Ug(e, t) {
  return this.each((typeof t == "function" ? Kg : Bg)(e, t));
}
function* Xg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var o = e[t], r = 0, s = o.length, a; r < s; ++r)
      (a = o[r]) && (yield a);
}
var Nl = [null];
function Fe(e, t) {
  this._groups = e, this._parents = t;
}
function io() {
  return new Fe([[document.documentElement]], Nl);
}
function qg() {
  return this;
}
Fe.prototype = io.prototype = {
  constructor: Fe,
  select: vh,
  selectAll: Sh,
  selectChild: kh,
  selectChildren: Th,
  filter: $h,
  data: Vh,
  enter: Mh,
  exit: Hh,
  join: Wh,
  merge: Fh,
  selection: qg,
  order: Bh,
  sort: Kh,
  call: Xh,
  nodes: qh,
  node: Yh,
  size: Zh,
  empty: Gh,
  each: Jh,
  attr: ig,
  style: lg,
  property: pg,
  classed: mg,
  text: bg,
  html: Eg,
  raise: Ig,
  lower: Ag,
  append: _g,
  insert: Tg,
  remove: Mg,
  clone: zg,
  datum: Lg,
  on: Fg,
  dispatch: Ug,
  [Symbol.iterator]: Xg
};
function We(e) {
  return typeof e == "string" ? new Fe([[document.querySelector(e)]], [document.documentElement]) : new Fe([[e]], Nl);
}
function Yg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function qe(e, t) {
  if (e = Yg(e), t === void 0 && (t = e.currentTarget), t) {
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
const Zg = { passive: !1 }, Xn = { capture: !0, passive: !1 };
function si(e) {
  e.stopImmediatePropagation();
}
function Jt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function jl(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", Jt, Xn);
  "onselectstart" in t ? n.on("selectstart.drag", Jt, Xn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Sl(e, t) {
  var n = e.document.documentElement, o = We(e).on("dragstart.drag", null);
  t && (o.on("click.drag", Jt, Xn), setTimeout(function() {
    o.on("click.drag", null);
  }, 0)), "onselectstart" in n ? o.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Do = (e) => () => e;
function Ii(e, {
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
Ii.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Gg(e) {
  return !e.ctrlKey && !e.button;
}
function Jg() {
  return this.parentNode;
}
function Qg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function ey() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function El() {
  var e = Gg, t = Jg, n = Qg, o = ey, r = {}, s = Er("start", "drag", "end"), a = 0, c, u, l, d, p = 0;
  function f(x) {
    x.on("mousedown.drag", h).filter(o).on("touchstart.drag", w).on("touchmove.drag", m, Zg).on("touchend.drag touchcancel.drag", j).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(x, N) {
    if (!(d || !e.call(this, x, N))) {
      var v = y(this, t.call(this, x, N), x, N, "mouse");
      v && (We(x.view).on("mousemove.drag", g, Xn).on("mouseup.drag", b, Xn), jl(x.view), si(x), l = !1, c = x.clientX, u = x.clientY, v("start", x));
    }
  }
  function g(x) {
    if (Jt(x), !l) {
      var N = x.clientX - c, v = x.clientY - u;
      l = N * N + v * v > p;
    }
    r.mouse("drag", x);
  }
  function b(x) {
    We(x.view).on("mousemove.drag mouseup.drag", null), Sl(x.view, l), Jt(x), r.mouse("end", x);
  }
  function w(x, N) {
    if (e.call(this, x, N)) {
      var v = x.changedTouches, S = t.call(this, x, N), k = v.length, M, R;
      for (M = 0; M < k; ++M)
        (R = y(this, S, x, N, v[M].identifier, v[M])) && (si(x), R("start", x, v[M]));
    }
  }
  function m(x) {
    var N = x.changedTouches, v = N.length, S, k;
    for (S = 0; S < v; ++S)
      (k = r[N[S].identifier]) && (Jt(x), k("drag", x, N[S]));
  }
  function j(x) {
    var N = x.changedTouches, v = N.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < v; ++S)
      (k = r[N[S].identifier]) && (si(x), k("end", x, N[S]));
  }
  function y(x, N, v, S, k, M) {
    var R = s.copy(), T = qe(M || v, N), A, z, E;
    if ((E = n.call(x, new Ii("beforestart", {
      sourceEvent: v,
      target: f,
      identifier: k,
      active: a,
      x: T[0],
      y: T[1],
      dx: 0,
      dy: 0,
      dispatch: R
    }), S)) != null)
      return A = E.x - T[0] || 0, z = E.y - T[1] || 0, function D(I, $, L) {
        var P = T, K;
        switch (I) {
          case "start":
            r[k] = D, K = a++;
            break;
          case "end":
            delete r[k], --a;
          // falls through
          case "drag":
            T = qe(L || $, N), K = a;
            break;
        }
        R.call(
          I,
          x,
          new Ii(I, {
            sourceEvent: $,
            subject: E,
            target: f,
            identifier: k,
            active: K,
            x: T[0] + A,
            y: T[1] + z,
            dx: T[0] - P[0],
            dy: T[1] - P[1],
            dispatch: R
          }),
          S
        );
      };
  }
  return f.filter = function(x) {
    return arguments.length ? (e = typeof x == "function" ? x : Do(!!x), f) : e;
  }, f.container = function(x) {
    return arguments.length ? (t = typeof x == "function" ? x : Do(x), f) : t;
  }, f.subject = function(x) {
    return arguments.length ? (n = typeof x == "function" ? x : Do(x), f) : n;
  }, f.touchable = function(x) {
    return arguments.length ? (o = typeof x == "function" ? x : Do(!!x), f) : o;
  }, f.on = function() {
    var x = s.on.apply(s, arguments);
    return x === s ? f : x;
  }, f.clickDistance = function(x) {
    return arguments.length ? (p = (x = +x) * x, f) : Math.sqrt(p);
  }, f;
}
function ns(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Cl(e, t) {
  var n = Object.create(e.prototype);
  for (var o in t) n[o] = t[o];
  return n;
}
function so() {
}
var qn = 0.7, lr = 1 / qn, Qt = "\\s*([+-]?\\d+)\\s*", Yn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", rt = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", ty = /^#([0-9a-f]{3,8})$/, ny = new RegExp(`^rgb\\(${Qt},${Qt},${Qt}\\)$`), oy = new RegExp(`^rgb\\(${rt},${rt},${rt}\\)$`), ry = new RegExp(`^rgba\\(${Qt},${Qt},${Qt},${Yn}\\)$`), iy = new RegExp(`^rgba\\(${rt},${rt},${rt},${Yn}\\)$`), sy = new RegExp(`^hsl\\(${Yn},${rt},${rt}\\)$`), ay = new RegExp(`^hsla\\(${Yn},${rt},${rt},${Yn}\\)$`), ea = {
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
ns(so, $t, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: ta,
  // Deprecated! Use color.formatHex.
  formatHex: ta,
  formatHex8: cy,
  formatHsl: ly,
  formatRgb: na,
  toString: na
});
function ta() {
  return this.rgb().formatHex();
}
function cy() {
  return this.rgb().formatHex8();
}
function ly() {
  return Il(this).formatHsl();
}
function na() {
  return this.rgb().formatRgb();
}
function $t(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = ty.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? oa(t) : n === 3 ? new $e(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? To(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? To(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = ny.exec(e)) ? new $e(t[1], t[2], t[3], 1) : (t = oy.exec(e)) ? new $e(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = ry.exec(e)) ? To(t[1], t[2], t[3], t[4]) : (t = iy.exec(e)) ? To(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = sy.exec(e)) ? sa(t[1], t[2] / 100, t[3] / 100, 1) : (t = ay.exec(e)) ? sa(t[1], t[2] / 100, t[3] / 100, t[4]) : ea.hasOwnProperty(e) ? oa(ea[e]) : e === "transparent" ? new $e(NaN, NaN, NaN, 0) : null;
}
function oa(e) {
  return new $e(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function To(e, t, n, o) {
  return o <= 0 && (e = t = n = NaN), new $e(e, t, n, o);
}
function uy(e) {
  return e instanceof so || (e = $t(e)), e ? (e = e.rgb(), new $e(e.r, e.g, e.b, e.opacity)) : new $e();
}
function ki(e, t, n, o) {
  return arguments.length === 1 ? uy(e) : new $e(e, t, n, o ?? 1);
}
function $e(e, t, n, o) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +o;
}
ns($e, ki, Cl(so, {
  brighter(e) {
    return e = e == null ? lr : Math.pow(lr, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? qn : Math.pow(qn, e), new $e(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new $e(Dt(this.r), Dt(this.g), Dt(this.b), ur(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: ra,
  // Deprecated! Use color.formatHex.
  formatHex: ra,
  formatHex8: dy,
  formatRgb: ia,
  toString: ia
}));
function ra() {
  return `#${At(this.r)}${At(this.g)}${At(this.b)}`;
}
function dy() {
  return `#${At(this.r)}${At(this.g)}${At(this.b)}${At((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ia() {
  const e = ur(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Dt(this.r)}, ${Dt(this.g)}, ${Dt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function ur(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Dt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function At(e) {
  return e = Dt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function sa(e, t, n, o) {
  return o <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ye(e, t, n, o);
}
function Il(e) {
  if (e instanceof Ye) return new Ye(e.h, e.s, e.l, e.opacity);
  if (e instanceof so || (e = $t(e)), !e) return new Ye();
  if (e instanceof Ye) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, o = e.b / 255, r = Math.min(t, n, o), s = Math.max(t, n, o), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - o) / c + (n < o) * 6 : n === s ? a = (o - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ye(a, c, u, e.opacity);
}
function fy(e, t, n, o) {
  return arguments.length === 1 ? Il(e) : new Ye(e, t, n, o ?? 1);
}
function Ye(e, t, n, o) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +o;
}
ns(Ye, fy, Cl(so, {
  brighter(e) {
    return e = e == null ? lr : Math.pow(lr, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? qn : Math.pow(qn, e), new Ye(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, o = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - o;
    return new $e(
      ai(e >= 240 ? e - 240 : e + 120, r, o),
      ai(e, r, o),
      ai(e < 120 ? e + 240 : e - 120, r, o),
      this.opacity
    );
  },
  clamp() {
    return new Ye(aa(this.h), $o(this.s), $o(this.l), ur(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = ur(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${aa(this.h)}, ${$o(this.s) * 100}%, ${$o(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function aa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function $o(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function ai(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const os = (e) => () => e;
function py(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function hy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(o) {
    return Math.pow(e + o * t, n);
  };
}
function gy(e) {
  return (e = +e) == 1 ? kl : function(t, n) {
    return n - t ? hy(t, n, e) : os(isNaN(t) ? n : t);
  };
}
function kl(e, t) {
  var n = t - e;
  return n ? py(e, n) : os(isNaN(e) ? t : e);
}
const dr = (function e(t) {
  var n = gy(t);
  function o(r, s) {
    var a = n((r = ki(r)).r, (s = ki(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = kl(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return o.gamma = e, o;
})(1);
function yy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, o = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) o[r] = e[r] * (1 - s) + t[r] * s;
    return o;
  };
}
function my(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function xy(e, t) {
  var n = t ? t.length : 0, o = e ? Math.min(n, e.length) : 0, r = new Array(o), s = new Array(n), a;
  for (a = 0; a < o; ++a) r[a] = Fn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < o; ++a) s[a] = r[a](c);
    return s;
  };
}
function wy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(o) {
    return n.setTime(e * (1 - o) + t * o), n;
  };
}
function ot(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function vy(e, t) {
  var n = {}, o = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = Fn(e[r], t[r]) : o[r] = t[r];
  return function(s) {
    for (r in n) o[r] = n[r](s);
    return o;
  };
}
var Ai = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, ci = new RegExp(Ai.source, "g");
function by(e) {
  return function() {
    return e;
  };
}
function Ny(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Al(e, t) {
  var n = Ai.lastIndex = ci.lastIndex = 0, o, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (o = Ai.exec(e)) && (r = ci.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (o = o[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: ot(o, r) })), n = ci.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Ny(u[0].x) : by(t) : (t = u.length, function(l) {
    for (var d = 0, p; d < t; ++d) c[(p = u[d]).i] = p.x(l);
    return c.join("");
  });
}
function Fn(e, t) {
  var n = typeof t, o;
  return t == null || n === "boolean" ? os(t) : (n === "number" ? ot : n === "string" ? (o = $t(t)) ? (t = o, dr) : Al : t instanceof $t ? dr : t instanceof Date ? wy : my(t) ? yy : Array.isArray(t) ? xy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? vy : ot)(e, t);
}
var ca = 180 / Math.PI, _i = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function _l(e, t, n, o, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * o) && (n -= e * u, o -= t * u), (c = Math.sqrt(n * n + o * o)) && (n /= c, o /= c, u /= c), e * o < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * ca,
    skewX: Math.atan(u) * ca,
    scaleX: a,
    scaleY: c
  };
}
var Mo;
function jy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? _i : _l(t.a, t.b, t.c, t.d, t.e, t.f);
}
function Sy(e) {
  return e == null || (Mo || (Mo = document.createElementNS("http://www.w3.org/2000/svg", "g")), Mo.setAttribute("transform", e), !(e = Mo.transform.baseVal.consolidate())) ? _i : (e = e.matrix, _l(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Dl(e, t, n, o) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, p, f, h, g) {
    if (l !== p || d !== f) {
      var b = h.push("translate(", null, t, null, n);
      g.push({ i: b - 4, x: ot(l, p) }, { i: b - 2, x: ot(d, f) });
    } else (p || f) && h.push("translate(" + p + t + f + n);
  }
  function a(l, d, p, f) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), f.push({ i: p.push(r(p) + "rotate(", null, o) - 2, x: ot(l, d) })) : d && p.push(r(p) + "rotate(" + d + o);
  }
  function c(l, d, p, f) {
    l !== d ? f.push({ i: p.push(r(p) + "skewX(", null, o) - 2, x: ot(l, d) }) : d && p.push(r(p) + "skewX(" + d + o);
  }
  function u(l, d, p, f, h, g) {
    if (l !== p || d !== f) {
      var b = h.push(r(h) + "scale(", null, ",", null, ")");
      g.push({ i: b - 4, x: ot(l, p) }, { i: b - 2, x: ot(d, f) });
    } else (p !== 1 || f !== 1) && h.push(r(h) + "scale(" + p + "," + f + ")");
  }
  return function(l, d) {
    var p = [], f = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, p, f), a(l.rotate, d.rotate, p, f), c(l.skewX, d.skewX, p, f), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, p, f), l = d = null, function(h) {
      for (var g = -1, b = f.length, w; ++g < b; ) p[(w = f[g]).i] = w.x(h);
      return p.join("");
    };
  };
}
var Ey = Dl(jy, "px, ", "px)", "deg)"), Cy = Dl(Sy, ", ", ")", ")"), Iy = 1e-12;
function la(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function ky(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ay(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Uo = (function e(t, n, o) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], p = a[1], f = a[2], h = d - c, g = p - u, b = h * h + g * g, w, m;
    if (b < Iy)
      m = Math.log(f / l) / t, w = function(S) {
        return [
          c + S * h,
          u + S * g,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var j = Math.sqrt(b), y = (f * f - l * l + o * b) / (2 * l * n * j), x = (f * f - l * l - o * b) / (2 * f * n * j), N = Math.log(Math.sqrt(y * y + 1) - y), v = Math.log(Math.sqrt(x * x + 1) - x);
      m = (v - N) / t, w = function(S) {
        var k = S * m, M = la(N), R = l / (n * j) * (M * Ay(t * k + N) - ky(N));
        return [
          c + R * h,
          u + R * g,
          l * M / la(t * k + N)
        ];
      };
    }
    return w.duration = m * 1e3 * t / Math.SQRT2, w;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var rn = 0, Tn = 0, An = 0, Tl = 1e3, fr, $n, pr = 0, Mt = 0, Ir = 0, Zn = typeof performance == "object" && performance.now ? performance : Date, $l = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function rs() {
  return Mt || ($l(_y), Mt = Zn.now() + Ir);
}
function _y() {
  Mt = 0;
}
function hr() {
  this._call = this._time = this._next = null;
}
hr.prototype = Ml.prototype = {
  constructor: hr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? rs() : +n) + (t == null ? 0 : +t), !this._next && $n !== this && ($n ? $n._next = this : fr = this, $n = this), this._call = e, this._time = n, Di();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Di());
  }
};
function Ml(e, t, n) {
  var o = new hr();
  return o.restart(e, t, n), o;
}
function Dy() {
  rs(), ++rn;
  for (var e = fr, t; e; )
    (t = Mt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --rn;
}
function ua() {
  Mt = (pr = Zn.now()) + Ir, rn = Tn = 0;
  try {
    Dy();
  } finally {
    rn = 0, $y(), Mt = 0;
  }
}
function Ty() {
  var e = Zn.now(), t = e - pr;
  t > Tl && (Ir -= t, pr = e);
}
function $y() {
  for (var e, t = fr, n, o = 1 / 0; t; )
    t._call ? (o > t._time && (o = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : fr = n);
  $n = e, Di(o);
}
function Di(e) {
  if (!rn) {
    Tn && (Tn = clearTimeout(Tn));
    var t = e - Mt;
    t > 24 ? (e < 1 / 0 && (Tn = setTimeout(ua, e - Zn.now() - Ir)), An && (An = clearInterval(An))) : (An || (pr = Zn.now(), An = setInterval(Ty, Tl)), rn = 1, $l(ua));
  }
}
function da(e, t, n) {
  var o = new hr();
  return t = t == null ? 0 : +t, o.restart((r) => {
    o.stop(), e(r + t);
  }, t, n), o;
}
var My = Er("start", "end", "cancel", "interrupt"), Py = [], Pl = 0, fa = 1, Ti = 2, Xo = 3, pa = 4, $i = 5, qo = 6;
function kr(e, t, n, o, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  Ry(e, n, {
    name: t,
    index: o,
    // For context during callback.
    group: r,
    // For context during callback.
    on: My,
    tween: Py,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Pl
  });
}
function is(e, t) {
  var n = Qe(e, t);
  if (n.state > Pl) throw new Error("too late; already scheduled");
  return n;
}
function at(e, t) {
  var n = Qe(e, t);
  if (n.state > Xo) throw new Error("too late; already running");
  return n;
}
function Qe(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function Ry(e, t, n) {
  var o = e.__transition, r;
  o[t] = n, n.timer = Ml(s, 0, n.time);
  function s(l) {
    n.state = fa, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, p, f, h;
    if (n.state !== fa) return u();
    for (d in o)
      if (h = o[d], h.name === n.name) {
        if (h.state === Xo) return da(a);
        h.state === pa ? (h.state = qo, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete o[d]) : +d < t && (h.state = qo, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete o[d]);
      }
    if (da(function() {
      n.state === Xo && (n.state = pa, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Ti, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Ti) {
      for (n.state = Xo, r = new Array(f = n.tween.length), d = 0, p = -1; d < f; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++p] = h);
      r.length = p + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = $i, 1), p = -1, f = r.length; ++p < f; )
      r[p].call(e, d);
    n.state === $i && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = qo, n.timer.stop(), delete o[t];
    for (var l in o) return;
    delete e.__transition;
  }
}
function Yo(e, t) {
  var n = e.__transition, o, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((o = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = o.state > Ti && o.state < $i, o.state = qo, o.timer.stop(), o.on.call(r ? "interrupt" : "cancel", e, e.__data__, o.index, o.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function zy(e) {
  return this.each(function() {
    Yo(this, e);
  });
}
function Ly(e, t) {
  var n, o;
  return function() {
    var r = at(this, e), s = r.tween;
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
function Vy(e, t, n) {
  var o, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = at(this, e), a = s.tween;
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
function Oy(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var o = Qe(this.node(), n).tween, r = 0, s = o.length, a; r < s; ++r)
      if ((a = o[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? Ly : Vy)(n, e, t));
}
function ss(e, t, n) {
  var o = e._id;
  return e.each(function() {
    var r = at(this, o);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Qe(r, o).value[t];
  };
}
function Rl(e, t) {
  var n;
  return (typeof t == "number" ? ot : t instanceof $t ? dr : (n = $t(t)) ? (t = n, dr) : Al)(e, t);
}
function Hy(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Wy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Fy(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function By(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Ky(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Uy(e, t, n) {
  var o, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c)));
  };
}
function Xy(e, t) {
  var n = Cr(e), o = n === "transform" ? Cy : Rl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Uy : Ky)(n, o, ss(this, "attr." + e, t)) : t == null ? (n.local ? Wy : Hy)(n) : (n.local ? By : Fy)(n, o, t));
}
function qy(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Yy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Zy(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && Yy(e, s)), n;
  }
  return r._value = t, r;
}
function Gy(e, t) {
  var n, o;
  function r() {
    var s = t.apply(this, arguments);
    return s !== o && (n = (o = s) && qy(e, s)), n;
  }
  return r._value = t, r;
}
function Jy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var o = Cr(e);
  return this.tween(n, (o.local ? Zy : Gy)(o, t));
}
function Qy(e, t) {
  return function() {
    is(this, e).delay = +t.apply(this, arguments);
  };
}
function em(e, t) {
  return t = +t, function() {
    is(this, e).delay = t;
  };
}
function tm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Qy : em)(t, e)) : Qe(this.node(), t).delay;
}
function nm(e, t) {
  return function() {
    at(this, e).duration = +t.apply(this, arguments);
  };
}
function om(e, t) {
  return t = +t, function() {
    at(this, e).duration = t;
  };
}
function rm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? nm : om)(t, e)) : Qe(this.node(), t).duration;
}
function im(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    at(this, e).ease = t;
  };
}
function sm(e) {
  var t = this._id;
  return arguments.length ? this.each(im(t, e)) : Qe(this.node(), t).ease;
}
function am(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    at(this, e).ease = n;
  };
}
function cm(e) {
  if (typeof e != "function") throw new Error();
  return this.each(am(this._id, e));
}
function lm(e) {
  typeof e != "function" && (e = pl(e));
  for (var t = this._groups, n = t.length, o = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = o[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new dt(o, this._parents, this._name, this._id);
}
function um(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, o = t.length, r = n.length, s = Math.min(o, r), a = new Array(o), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, p = a[c] = new Array(d), f, h = 0; h < d; ++h)
      (f = u[h] || l[h]) && (p[h] = f);
  for (; c < o; ++c)
    a[c] = t[c];
  return new dt(a, this._parents, this._name, this._id);
}
function dm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function fm(e, t, n) {
  var o, r, s = dm(t) ? is : at;
  return function() {
    var a = s(this, e), c = a.on;
    c !== o && (r = (o = c).copy()).on(t, n), a.on = r;
  };
}
function pm(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Qe(this.node(), n).on.on(e) : this.each(fm(n, e, t));
}
function hm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function gm() {
  return this.on("end.remove", hm(this._id));
}
function ym(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = es(e));
  for (var o = this._groups, r = o.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = o[a], u = c.length, l = s[a] = new Array(u), d, p, f = 0; f < u; ++f)
      (d = c[f]) && (p = e.call(d, d.__data__, f, c)) && ("__data__" in d && (p.__data__ = d.__data__), l[f] = p, kr(l[f], t, n, f, l, Qe(d, n)));
  return new dt(s, this._parents, t, n);
}
function mm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = fl(e));
  for (var o = this._groups, r = o.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = o[c], l = u.length, d, p = 0; p < l; ++p)
      if (d = u[p]) {
        for (var f = e.call(d, d.__data__, p, u), h, g = Qe(d, n), b = 0, w = f.length; b < w; ++b)
          (h = f[b]) && kr(h, t, n, b, f, g);
        s.push(f), a.push(d);
      }
  return new dt(s, a, t, n);
}
var xm = io.prototype.constructor;
function wm() {
  return new xm(this._groups, this._parents);
}
function vm(e, t) {
  var n, o, r;
  return function() {
    var s = on(this, e), a = (this.style.removeProperty(e), on(this, e));
    return s === a ? null : s === n && a === o ? r : r = t(n = s, o = a);
  };
}
function zl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function bm(e, t, n) {
  var o, r = n + "", s;
  return function() {
    var a = on(this, e);
    return a === r ? null : a === o ? s : s = t(o = a, n);
  };
}
function Nm(e, t, n) {
  var o, r, s;
  return function() {
    var a = on(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), on(this, e))), a === u ? null : a === o && u === r ? s : (r = u, s = t(o = a, c));
  };
}
function jm(e, t) {
  var n, o, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = at(this, e), l = u.on, d = u.value[s] == null ? c || (c = zl(t)) : void 0;
    (l !== n || r !== d) && (o = (n = l).copy()).on(a, r = d), u.on = o;
  };
}
function Sm(e, t, n) {
  var o = (e += "") == "transform" ? Ey : Rl;
  return t == null ? this.styleTween(e, vm(e, o)).on("end.style." + e, zl(e)) : typeof t == "function" ? this.styleTween(e, Nm(e, o, ss(this, "style." + e, t))).each(jm(this._id, e)) : this.styleTween(e, bm(e, o, t), n).on("end.style." + e, null);
}
function Em(e, t, n) {
  return function(o) {
    this.style.setProperty(e, t.call(this, o), n);
  };
}
function Cm(e, t, n) {
  var o, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (o = (r = a) && Em(e, a, n)), o;
  }
  return s._value = t, s;
}
function Im(e, t, n) {
  var o = "style." + (e += "");
  if (arguments.length < 2) return (o = this.tween(o)) && o._value;
  if (t == null) return this.tween(o, null);
  if (typeof t != "function") throw new Error();
  return this.tween(o, Cm(e, t, n ?? ""));
}
function km(e) {
  return function() {
    this.textContent = e;
  };
}
function Am(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function _m(e) {
  return this.tween("text", typeof e == "function" ? Am(ss(this, "text", e)) : km(e == null ? "" : e + ""));
}
function Dm(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Tm(e) {
  var t, n;
  function o() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Dm(r)), t;
  }
  return o._value = e, o;
}
function $m(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Tm(e));
}
function Mm() {
  for (var e = this._name, t = this._id, n = Ll(), o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Qe(u, t);
        kr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new dt(o, this._parents, e, n);
}
function Pm() {
  var e, t, n = this, o = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = at(this, o), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var Rm = 0;
function dt(e, t, n, o) {
  this._groups = e, this._parents = t, this._name = n, this._id = o;
}
function Ll() {
  return ++Rm;
}
var ct = io.prototype;
dt.prototype = {
  constructor: dt,
  select: ym,
  selectAll: mm,
  selectChild: ct.selectChild,
  selectChildren: ct.selectChildren,
  filter: lm,
  merge: um,
  selection: wm,
  transition: Mm,
  call: ct.call,
  nodes: ct.nodes,
  node: ct.node,
  size: ct.size,
  empty: ct.empty,
  each: ct.each,
  on: pm,
  attr: Xy,
  attrTween: Jy,
  style: Sm,
  styleTween: Im,
  text: _m,
  textTween: $m,
  remove: gm,
  tween: Oy,
  delay: tm,
  duration: rm,
  ease: sm,
  easeVarying: cm,
  end: Pm,
  [Symbol.iterator]: ct[Symbol.iterator]
};
function zm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Lm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: zm
};
function Vm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function Om(e) {
  var t, n;
  e instanceof dt ? (t = e._id, e = e._name) : (t = Ll(), (n = Lm).time = rs(), e = e == null ? null : e + "");
  for (var o = this._groups, r = o.length, s = 0; s < r; ++s)
    for (var a = o[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && kr(u, e, t, l, a, n || Vm(u, t));
  return new dt(o, this._parents, e, t);
}
io.prototype.interrupt = zy;
io.prototype.transition = Om;
const Po = (e) => () => e;
function Hm(e, {
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
var Ar = new lt(1, 0, 0);
Vl.prototype = lt.prototype;
function Vl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Ar;
  return e.__zoom;
}
function li(e) {
  e.stopImmediatePropagation();
}
function _n(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Wm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Fm() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ha() {
  return this.__zoom || Ar;
}
function Bm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Km() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Um(e, t, n) {
  var o = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > o ? (o + r) / 2 : Math.min(0, o) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Ol() {
  var e = Wm, t = Fm, n = Um, o = Bm, r = Km, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Uo, l = Er("start", "zoom", "end"), d, p, f, h = 500, g = 150, b = 0, w = 10;
  function m(E) {
    E.property("__zoom", ha).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", M).on("dblclick.zoom", R).filter(r).on("touchstart.zoom", T).on("touchmove.zoom", A).on("touchend.zoom touchcancel.zoom", z).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(E, D, I, $) {
    var L = E.selection ? E.selection() : E;
    L.property("__zoom", ha), E !== L ? N(E, D, I, $) : L.interrupt().each(function() {
      v(this, arguments).event($).start().zoom(null, typeof D == "function" ? D.apply(this, arguments) : D).end();
    });
  }, m.scaleBy = function(E, D, I, $) {
    m.scaleTo(E, function() {
      var L = this.__zoom.k, P = typeof D == "function" ? D.apply(this, arguments) : D;
      return L * P;
    }, I, $);
  }, m.scaleTo = function(E, D, I, $) {
    m.transform(E, function() {
      var L = t.apply(this, arguments), P = this.__zoom, K = I == null ? x(L) : typeof I == "function" ? I.apply(this, arguments) : I, F = P.invert(K), B = typeof D == "function" ? D.apply(this, arguments) : D;
      return n(y(j(P, B), K, F), L, a);
    }, I, $);
  }, m.translateBy = function(E, D, I, $) {
    m.transform(E, function() {
      return n(this.__zoom.translate(
        typeof D == "function" ? D.apply(this, arguments) : D,
        typeof I == "function" ? I.apply(this, arguments) : I
      ), t.apply(this, arguments), a);
    }, null, $);
  }, m.translateTo = function(E, D, I, $, L) {
    m.transform(E, function() {
      var P = t.apply(this, arguments), K = this.__zoom, F = $ == null ? x(P) : typeof $ == "function" ? $.apply(this, arguments) : $;
      return n(Ar.translate(F[0], F[1]).scale(K.k).translate(
        typeof D == "function" ? -D.apply(this, arguments) : -D,
        typeof I == "function" ? -I.apply(this, arguments) : -I
      ), P, a);
    }, $, L);
  };
  function j(E, D) {
    return D = Math.max(s[0], Math.min(s[1], D)), D === E.k ? E : new lt(D, E.x, E.y);
  }
  function y(E, D, I) {
    var $ = D[0] - I[0] * E.k, L = D[1] - I[1] * E.k;
    return $ === E.x && L === E.y ? E : new lt(E.k, $, L);
  }
  function x(E) {
    return [(+E[0][0] + +E[1][0]) / 2, (+E[0][1] + +E[1][1]) / 2];
  }
  function N(E, D, I, $) {
    E.on("start.zoom", function() {
      v(this, arguments).event($).start();
    }).on("interrupt.zoom end.zoom", function() {
      v(this, arguments).event($).end();
    }).tween("zoom", function() {
      var L = this, P = arguments, K = v(L, P).event($), F = t.apply(L, P), B = I == null ? x(F) : typeof I == "function" ? I.apply(L, P) : I, q = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), G = L.__zoom, X = typeof D == "function" ? D.apply(L, P) : D, le = u(G.invert(B).concat(q / G.k), X.invert(B).concat(q / X.k));
      return function(Z) {
        if (Z === 1) Z = X;
        else {
          var O = le(Z), Y = q / O[2];
          Z = new lt(Y, B[0] - O[0] * Y, B[1] - O[1] * Y);
        }
        K.zoom(null, Z);
      };
    });
  }
  function v(E, D, I) {
    return !I && E.__zooming || new S(E, D);
  }
  function S(E, D) {
    this.that = E, this.args = D, this.active = 0, this.sourceEvent = null, this.extent = t.apply(E, D), this.taps = 0;
  }
  S.prototype = {
    event: function(E) {
      return E && (this.sourceEvent = E), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(E, D) {
      return this.mouse && E !== "mouse" && (this.mouse[1] = D.invert(this.mouse[0])), this.touch0 && E !== "touch" && (this.touch0[1] = D.invert(this.touch0[0])), this.touch1 && E !== "touch" && (this.touch1[1] = D.invert(this.touch1[0])), this.that.__zoom = D, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(E) {
      var D = We(this.that).datum();
      l.call(
        E,
        this.that,
        new Hm(E, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        D
      );
    }
  };
  function k(E, ...D) {
    if (!e.apply(this, arguments)) return;
    var I = v(this, D).event(E), $ = this.__zoom, L = Math.max(s[0], Math.min(s[1], $.k * Math.pow(2, o.apply(this, arguments)))), P = qe(E);
    if (I.wheel)
      (I.mouse[0][0] !== P[0] || I.mouse[0][1] !== P[1]) && (I.mouse[1] = $.invert(I.mouse[0] = P)), clearTimeout(I.wheel);
    else {
      if ($.k === L) return;
      I.mouse = [P, $.invert(P)], Yo(this), I.start();
    }
    _n(E), I.wheel = setTimeout(K, g), I.zoom("mouse", n(y(j($, L), I.mouse[0], I.mouse[1]), I.extent, a));
    function K() {
      I.wheel = null, I.end();
    }
  }
  function M(E, ...D) {
    if (f || !e.apply(this, arguments)) return;
    var I = E.currentTarget, $ = v(this, D, !0).event(E), L = We(E.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", q, !0), P = qe(E, I), K = E.clientX, F = E.clientY;
    jl(E.view), li(E), $.mouse = [P, this.__zoom.invert(P)], Yo(this), $.start();
    function B(G) {
      if (_n(G), !$.moved) {
        var X = G.clientX - K, le = G.clientY - F;
        $.moved = X * X + le * le > b;
      }
      $.event(G).zoom("mouse", n(y($.that.__zoom, $.mouse[0] = qe(G, I), $.mouse[1]), $.extent, a));
    }
    function q(G) {
      L.on("mousemove.zoom mouseup.zoom", null), Sl(G.view, $.moved), _n(G), $.event(G).end();
    }
  }
  function R(E, ...D) {
    if (e.apply(this, arguments)) {
      var I = this.__zoom, $ = qe(E.changedTouches ? E.changedTouches[0] : E, this), L = I.invert($), P = I.k * (E.shiftKey ? 0.5 : 2), K = n(y(j(I, P), $, L), t.apply(this, D), a);
      _n(E), c > 0 ? We(this).transition().duration(c).call(N, K, $, E) : We(this).call(m.transform, K, $, E);
    }
  }
  function T(E, ...D) {
    if (e.apply(this, arguments)) {
      var I = E.touches, $ = I.length, L = v(this, D, E.changedTouches.length === $).event(E), P, K, F, B;
      for (li(E), K = 0; K < $; ++K)
        F = I[K], B = qe(F, this), B = [B, this.__zoom.invert(B), F.identifier], L.touch0 ? !L.touch1 && L.touch0[2] !== B[2] && (L.touch1 = B, L.taps = 0) : (L.touch0 = B, P = !0, L.taps = 1 + !!d);
      d && (d = clearTimeout(d)), P && (L.taps < 2 && (p = B[0], d = setTimeout(function() {
        d = null;
      }, h)), Yo(this), L.start());
    }
  }
  function A(E, ...D) {
    if (this.__zooming) {
      var I = v(this, D).event(E), $ = E.changedTouches, L = $.length, P, K, F, B;
      for (_n(E), P = 0; P < L; ++P)
        K = $[P], F = qe(K, this), I.touch0 && I.touch0[2] === K.identifier ? I.touch0[0] = F : I.touch1 && I.touch1[2] === K.identifier && (I.touch1[0] = F);
      if (K = I.that.__zoom, I.touch1) {
        var q = I.touch0[0], G = I.touch0[1], X = I.touch1[0], le = I.touch1[1], Z = (Z = X[0] - q[0]) * Z + (Z = X[1] - q[1]) * Z, O = (O = le[0] - G[0]) * O + (O = le[1] - G[1]) * O;
        K = j(K, Math.sqrt(Z / O)), F = [(q[0] + X[0]) / 2, (q[1] + X[1]) / 2], B = [(G[0] + le[0]) / 2, (G[1] + le[1]) / 2];
      } else if (I.touch0) F = I.touch0[0], B = I.touch0[1];
      else return;
      I.zoom("touch", n(y(K, F, B), I.extent, a));
    }
  }
  function z(E, ...D) {
    if (this.__zooming) {
      var I = v(this, D).event(E), $ = E.changedTouches, L = $.length, P, K;
      for (li(E), f && clearTimeout(f), f = setTimeout(function() {
        f = null;
      }, h), P = 0; P < L; ++P)
        K = $[P], I.touch0 && I.touch0[2] === K.identifier ? delete I.touch0 : I.touch1 && I.touch1[2] === K.identifier && delete I.touch1;
      if (I.touch1 && !I.touch0 && (I.touch0 = I.touch1, delete I.touch1), I.touch0) I.touch0[1] = this.__zoom.invert(I.touch0[0]);
      else if (I.end(), I.taps === 2 && (K = qe(K, this), Math.hypot(p[0] - K[0], p[1] - K[1]) < w)) {
        var F = We(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(E) {
    return arguments.length ? (o = typeof E == "function" ? E : Po(+E), m) : o;
  }, m.filter = function(E) {
    return arguments.length ? (e = typeof E == "function" ? E : Po(!!E), m) : e;
  }, m.touchable = function(E) {
    return arguments.length ? (r = typeof E == "function" ? E : Po(!!E), m) : r;
  }, m.extent = function(E) {
    return arguments.length ? (t = typeof E == "function" ? E : Po([[+E[0][0], +E[0][1]], [+E[1][0], +E[1][1]]]), m) : t;
  }, m.scaleExtent = function(E) {
    return arguments.length ? (s[0] = +E[0], s[1] = +E[1], m) : [s[0], s[1]];
  }, m.translateExtent = function(E) {
    return arguments.length ? (a[0][0] = +E[0][0], a[1][0] = +E[1][0], a[0][1] = +E[0][1], a[1][1] = +E[1][1], m) : [[a[0][0], a[0][1]], [a[1][0], a[1][1]]];
  }, m.constrain = function(E) {
    return arguments.length ? (n = E, m) : n;
  }, m.duration = function(E) {
    return arguments.length ? (c = +E, m) : c;
  }, m.interpolate = function(E) {
    return arguments.length ? (u = E, m) : u;
  }, m.on = function() {
    var E = l.on.apply(l, arguments);
    return E === l ? m : E;
  }, m.clickDistance = function(E) {
    return arguments.length ? (b = (E = +E) * E, m) : Math.sqrt(b);
  }, m.tapDistance = function(E) {
    return arguments.length ? (w = +E, m) : w;
  }, m;
}
const Be = {
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
}, Gn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Hl = ["Enter", " ", "Escape"], Wl = {
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
var sn;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(sn || (sn = {}));
var Tt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Tt || (Tt = {}));
var Jn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Jn || (Jn = {}));
const Fl = {
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
var vt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(vt || (vt = {}));
var gr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(gr || (gr = {}));
var ne;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ne || (ne = {}));
const ga = {
  [ne.Left]: ne.Right,
  [ne.Right]: ne.Left,
  [ne.Top]: ne.Bottom,
  [ne.Bottom]: ne.Top
};
function Bl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Kl = (e) => "id" in e && "source" in e && "target" in e, Xm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), as = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ao = (e, t = [0, 0]) => {
  const { width: n, height: o } = ft(e), r = e.origin ?? t, s = n * r[0], a = o * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, qm = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((o, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : as(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? yr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return _r(o, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Dr(n);
}, co = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, o = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = _r(n, yr(r)), o = !0);
  }), o ? Dr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, cs = (e, t, [n, o, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...hn(t, [n, o, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: p = !0, hidden: f = !1 } = l;
    if (a && !p || f)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, g = d.height ?? l.height ?? l.initialHeight ?? null, b = Qn(c, cn(l)), w = (h ?? 0) * (g ?? 0), m = s && b > 0;
    (!l.internals.handleBounds || m || b >= w || l.dragging) && u.push(l);
  }
  return u;
}, Ym = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((o) => {
    n.add(o.id);
  }), t.filter((o) => n.has(o.source) || n.has(o.target));
};
function Zm(e, t) {
  const n = /* @__PURE__ */ new Map(), o = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!o || o.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function Gm({ nodes: e, width: t, height: n, panZoom: o, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Zm(e, a), u = co(c), l = us(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await o.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Ul({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: o = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? o;
  let p = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Be.error005());
    else {
      const h = c.measured.width, g = c.measured.height;
      h && g && (p = [
        [u, l],
        [u + h, l + g]
      ]);
    }
  else c && Rt(a.extent) && (p = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const f = Rt(p) ? Pt(t, p, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Be.error015()), {
    position: {
      x: f.x - u + (a.measured.width ?? 0) * d[0],
      y: f.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: f
  };
}
async function Jm({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: o, onBeforeDelete: r }) {
  const s = new Set(e.map((f) => f.id)), a = [];
  for (const f of n) {
    if (f.deletable === !1)
      continue;
    const h = s.has(f.id), g = !h && f.parentId && a.find((b) => b.id === f.parentId);
    (h || g) && a.push(f);
  }
  const c = new Set(t.map((f) => f.id)), u = o.filter((f) => f.deletable !== !1), d = Ym(a, u);
  for (const f of u)
    c.has(f.id) && !d.find((g) => g.id === f.id) && d.push(f);
  if (!r)
    return {
      edges: d,
      nodes: a
    };
  const p = await r({
    nodes: a,
    edges: d
  });
  return typeof p == "boolean" ? p ? { edges: d, nodes: a } : { edges: [], nodes: [] } : p;
}
const an = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Pt = (e = { x: 0, y: 0 }, t, n) => ({
  x: an(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: an(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Xl(e, t, n) {
  const { width: o, height: r } = ft(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Pt(e, [
    [s, a],
    [s + o, a + r]
  ], t);
}
const ya = (e, t, n) => e < t ? an(Math.abs(e - t), 1, t) / t : e > n ? -an(Math.abs(e - n), 1, t) / t : 0, ls = (e, t, n = 15, o = 40) => {
  const r = ya(e.x, o, t.width - o) * n, s = ya(e.y, o, t.height - o) * n;
  return [r, s];
}, _r = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Mi = ({ x: e, y: t, width: n, height: o }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + o
}), Dr = ({ x: e, y: t, x2: n, y2: o }) => ({
  x: e,
  y: t,
  width: n - e,
  height: o - t
}), cn = (e, t = [0, 0]) => {
  const { x: n, y: o } = as(e) ? e.internals.positionAbsolute : ao(e, t);
  return {
    x: n,
    y: o,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, yr = (e, t = [0, 0]) => {
  const { x: n, y: o } = as(e) ? e.internals.positionAbsolute : ao(e, t);
  return {
    x: n,
    y: o,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: o + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, ql = (e, t) => Dr(_r(Mi(e), Mi(t))), Qn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), o = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * o);
}, ma = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), Yl = (e, t) => (n, o) => {
}, lo = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), hn = ({ x: e, y: t }, [n, o, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - o) / r
  };
  return s ? lo(c, a) : c;
}, ln = ({ x: e, y: t }, [n, o, r]) => ({
  x: e * r + n,
  y: t * r + o
});
function qt(e, t) {
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
function Qm(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const o = qt(e, n), r = qt(e, t);
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
    const o = qt(e.top ?? e.y ?? 0, n), r = qt(e.bottom ?? e.y ?? 0, n), s = qt(e.left ?? e.x ?? 0, t), a = qt(e.right ?? e.x ?? 0, t);
    return { top: o, right: a, bottom: r, left: s, x: s + a, y: o + r };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function ex(e, t, n, o, r, s) {
  const { x: a, y: c } = ln(e, [t, n, o]), { x: u, y: l } = ln({ x: e.x + e.width, y: e.y + e.height }, [t, n, o]), d = r - u, p = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(p)
  };
}
const us = (e, t, n, o, r, s) => {
  const a = Qm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = an(l, o, r), p = e.x + e.width / 2, f = e.y + e.height / 2, h = t / 2 - p * d, g = n / 2 - f * d, b = ex(e, h, g, d, t, n), w = {
    left: Math.min(b.left - a.left, 0),
    top: Math.min(b.top - a.top, 0),
    right: Math.min(b.right - a.right, 0),
    bottom: Math.min(b.bottom - a.bottom, 0)
  };
  return {
    x: h - w.left + w.right,
    y: g - w.top + w.bottom,
    zoom: d
  };
}, eo = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Rt(e) {
  return e != null && e !== "parent";
}
function ft(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Zl(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Gl(e, t = { width: 0, height: 0 }, n, o, r) {
  const s = { ...e }, a = o.get(n);
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
function tx() {
  let e, t;
  return { promise: new Promise((o, r) => {
    e = o, t = r;
  }), resolve: e, reject: t };
}
function nx(e) {
  return { ...Wl, ...e || {} };
}
function Bn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: o, containerBounds: r }) {
  const { x: s, y: a } = Ge(e), c = hn({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, o), { x: u, y: l } = n ? lo(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const ds = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), Jl = (e) => e?.getRootNode?.() || window?.document, ox = ["INPUT", "SELECT", "TEXTAREA"];
function Ql(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ox.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const eu = (e) => "clientX" in e, Ge = (e, t) => {
  const n = eu(e), o = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: o - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, wa = (e, t, n, o, r) => {
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
      ...ds(a)
    };
  });
};
function tu({ sourceX: e, sourceY: t, targetX: n, targetY: o, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + o * 0.125, d = Math.abs(u - e), p = Math.abs(l - t);
  return [u, l, d, p];
}
function Ro(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function va({ pos: e, x1: t, y1: n, x2: o, y2: r, c: s }) {
  switch (e) {
    case ne.Left:
      return [t - Ro(t - o, s), n];
    case ne.Right:
      return [t + Ro(o - t, s), n];
    case ne.Top:
      return [t, n - Ro(n - r, s)];
    case ne.Bottom:
      return [t, n + Ro(r - n, s)];
  }
}
function nu({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, curvature: a = 0.25 }) {
  const [c, u] = va({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r,
    c: a
  }), [l, d] = va({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [p, f, h, g] = tu({
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
    p,
    f,
    h,
    g
  ];
}
function ou({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(o - t) / 2, c = o < t ? o + a : o - a;
  return [s, c, r, a];
}
function rx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: o = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return o;
  const a = r && n ? o + 1e3 : o, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function ix({ sourceNode: e, targetNode: t, width: n, height: o, transform: r }) {
  const s = _r(yr(e), yr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: o / r[2]
  };
  return Qn(a, Dr(s)) > 0;
}
const ru = ({ source: e, sourceHandle: t, target: n, targetHandle: o }) => `xy-edge__${e}${t || ""}-${n}${o || ""}`, sx = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), ax = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Be.error006()), t;
  const o = n.getEdgeId || ru;
  let r;
  return Kl(e) ? r = { ...e } : r = {
    ...e,
    id: o(e)
  }, sx(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, cx = (e, t, n, o = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return o.onError?.("006", Be.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return o.onError?.("007", Be.error007(r)), n;
  const c = o.getEdgeId || ru, u = {
    ...s,
    id: o.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function iu({ sourceX: e, sourceY: t, targetX: n, targetY: o }) {
  const [r, s, a, c] = ou({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: o
  });
  return [`M ${e},${t}L ${n},${o}`, r, s, a, c];
}
const ba = {
  [ne.Left]: { x: -1, y: 0 },
  [ne.Right]: { x: 1, y: 0 },
  [ne.Top]: { x: 0, y: -1 },
  [ne.Bottom]: { x: 0, y: 1 }
}, lx = ({ source: e, sourcePosition: t = ne.Bottom, target: n }) => t === ne.Left || t === ne.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, Na = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function ux({ source: e, sourcePosition: t = ne.Bottom, target: n, targetPosition: o = ne.Top, center: r, offset: s, stepPosition: a }) {
  const c = ba[t], u = ba[o], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, p = lx({
    source: l,
    sourcePosition: t,
    target: d
  }), f = p.x !== 0 ? "x" : "y", h = p[f];
  let g = [], b, w;
  const m = { x: 0, y: 0 }, j = { x: 0, y: 0 }, [, , y, x] = ou({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[f] * u[f] === -1) {
    f === "x" ? (b = r.x ?? l.x + (d.x - l.x) * a, w = r.y ?? (l.y + d.y) / 2) : (b = r.x ?? (l.x + d.x) / 2, w = r.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: b, y: l.y },
      { x: b, y: d.y }
    ], M = [
      { x: l.x, y: w },
      { x: d.x, y: w }
    ];
    c[f] === h ? g = f === "x" ? k : M : g = f === "x" ? M : k;
  } else {
    const k = [{ x: l.x, y: d.y }], M = [{ x: d.x, y: l.y }];
    if (f === "x" ? g = c.x === h ? M : k : g = c.y === h ? k : M, t === o) {
      const E = Math.abs(e[f] - n[f]);
      if (E <= s) {
        const D = Math.min(s - 1, s - E);
        c[f] === h ? m[f] = (l[f] > e[f] ? -1 : 1) * D : j[f] = (d[f] > n[f] ? -1 : 1) * D;
      }
    }
    if (t !== o) {
      const E = f === "x" ? "y" : "x", D = c[f] === u[E], I = l[E] > d[E], $ = l[E] < d[E];
      (c[f] === 1 && (!D && I || D && $) || c[f] !== 1 && (!D && $ || D && I)) && (g = f === "x" ? k : M);
    }
    const R = { x: l.x + m.x, y: l.y + m.y }, T = { x: d.x + j.x, y: d.y + j.y }, A = Math.max(Math.abs(R.x - g[0].x), Math.abs(T.x - g[0].x)), z = Math.max(Math.abs(R.y - g[0].y), Math.abs(T.y - g[0].y));
    A >= z ? (b = (R.x + T.x) / 2, w = g[0].y) : (b = g[0].x, w = (R.y + T.y) / 2);
  }
  const N = { x: l.x + m.x, y: l.y + m.y }, v = { x: d.x + j.x, y: d.y + j.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...N.x !== g[0].x || N.y !== g[0].y ? [N] : [],
    ...g,
    ...v.x !== g[g.length - 1].x || v.y !== g[g.length - 1].y ? [v] : [],
    n
  ], b, w, y, x];
}
function dx(e, t, n, o) {
  const r = Math.min(Na(e, t) / 2, Na(t, n) / 2, o), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function mr({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [p, f, h, g, b] = ux({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: o, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let w = `M${p[0].x} ${p[0].y}`;
  for (let m = 1; m < p.length - 1; m++)
    w += dx(p[m - 1], p[m], p[m + 1], a);
  return w += `L${p[p.length - 1].x} ${p[p.length - 1].y}`, [w, f, h, g, b];
}
function ja(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function fx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ja(t) || !ja(n))
    return null;
  const o = t.internals.handleBounds || Sa(t.handles), r = n.internals.handleBounds || Sa(n.handles), s = Ea(o?.source ?? [], e.sourceHandle), a = Ea(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === sn.Strict ? r?.target ?? [] : (r?.target ?? []).concat(r?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Be.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ne.Bottom, u = a?.position || ne.Top, l = zt(t, s, c), d = zt(n, a, u);
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
  for (const o of e)
    o.width = o.width ?? 1, o.height = o.height ?? 1, o.type === "source" ? t.push(o) : o.type === "target" && n.push(o);
  return {
    source: t,
    target: n
  };
}
function zt(e, t, n = ne.Left, o = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? ft(e);
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
function Ea(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Pi(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((o) => `${o}=${e[o]}`).join("&")}` : "";
}
function px(e, { id: t, defaultColor: n, defaultMarkerStart: o, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || o, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Pi(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const su = 1e3, hx = 10, fs = {
  nodeOrigin: [0, 0],
  nodeExtent: Gn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, gx = {
  ...fs,
  checkEquality: !0
};
function ps(e, t) {
  const n = { ...e };
  for (const o in t)
    t[o] !== void 0 && (n[o] = t[o]);
  return n;
}
function yx(e, t, n) {
  const o = ps(fs, n);
  for (const r of e.values())
    if (r.parentId)
      gs(r, e, t, o);
    else {
      const s = ao(r, o.nodeOrigin), a = Rt(r.extent) ? r.extent : o.nodeExtent, c = Pt(s, a, ft(r));
      r.internals.positionAbsolute = c;
    }
}
function mx(e, t) {
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
function hs(e) {
  return e === "manual";
}
function Ri(e, t, n, o = {}) {
  const r = ps(gx, o), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !hs(r.zIndexMode) ? su : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let p = a.get(d.id);
    if (r.checkEquality && d === p?.internals.userNode)
      t.set(d.id, p);
    else {
      const f = ao(d, r.nodeOrigin), h = Rt(d.extent) ? d.extent : r.nodeExtent, g = Pt(f, h, ft(d));
      p = {
        ...r.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: g,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: mx(d, p),
          z: au(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, p);
    }
    (p.measured === void 0 || p.measured.width === void 0 || p.measured.height === void 0) && !p.hidden && (u = !1), d.parentId && gs(p, t, n, o, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function xx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function gs(e, t, n, o, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = ps(fs, o), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  xx(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * hx), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const p = s && !hs(u) ? su : 0, { x: f, y: h, z: g } = wx(e, d, a, c, p, u), { positionAbsolute: b } = e.internals, w = f !== b.x || h !== b.y;
  (w || g !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: w ? { x: f, y: h } : b,
      z: g
    }
  });
}
function au(e, t, n) {
  const o = Ze(e.zIndex) ? e.zIndex : 0;
  return hs(n) ? o : o + (e.selected ? t : 0);
}
function wx(e, t, n, o, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = ft(e), l = ao(e, n), d = Rt(e.extent) ? Pt(l, e.extent, u) : l;
  let p = Pt({ x: a + d.x, y: c + d.y }, o, u);
  e.extent === "parent" && (p = Xl(p, u, t));
  const f = au(e, r, s), h = t.internals.z ?? 0;
  return {
    x: p.x,
    y: p.y,
    z: h >= f ? h + 1 : f
  };
}
function ys(e, t, n, o = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? cn(c), l = ql(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = ft(c), p = c.origin ?? o, f = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, g = Math.max(d.width, Math.round(a.width)), b = Math.max(d.height, Math.round(a.height)), w = (g - d.width) * p[0], m = (b - d.height) * p[1];
    (f > 0 || h > 0 || w || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - f + w,
        y: c.position.y - h + m
      }
    }), n.get(u)?.forEach((j) => {
      e.some((y) => y.id === j.id) || r.push({
        id: j.id,
        type: "position",
        position: {
          x: j.position.x + f,
          y: j.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || f || h) && r.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: g + (f ? p[0] * f - w : 0),
        height: b + (h ? p[1] * h - m : 0)
      }
    });
  }), r;
}
function vx(e, t, n, o, r, s, a) {
  const c = o?.querySelector(".xyflow__viewport");
  let u = !1;
  if (!c)
    return { changes: [], updatedInternals: u };
  const l = [], d = window.getComputedStyle(c), { m22: p } = new window.DOMMatrixReadOnly(d.transform), f = [];
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
    const b = ds(h.nodeElement), w = g.measured.width !== b.width || g.measured.height !== b.height;
    if (!!(b.width && b.height && (w || !g.internals.handleBounds || h.force))) {
      const j = h.nodeElement.getBoundingClientRect(), y = Rt(g.extent) ? g.extent : s;
      let { positionAbsolute: x } = g.internals;
      g.parentId && g.extent === "parent" ? x = Xl(x, b, t.get(g.parentId)) : y && (x = Pt(x, y, b));
      const N = {
        ...g,
        measured: b,
        internals: {
          ...g.internals,
          positionAbsolute: x,
          handleBounds: {
            source: wa("source", h.nodeElement, j, p, g.id),
            target: wa("target", h.nodeElement, j, p, g.id)
          }
        }
      };
      t.set(g.id, N), g.parentId && gs(N, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, w && (l.push({
        id: g.id,
        type: "dimensions",
        dimensions: b
      }), g.expandParent && g.parentId && f.push({
        id: g.id,
        parentId: g.parentId,
        rect: cn(N, r)
      }));
    }
  }
  if (f.length > 0) {
    const h = ys(f, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function bx({ delta: e, panZoom: t, transform: n, translateExtent: o, width: r, height: s }) {
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
function Ca(e, t, n, o, r, s) {
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
function cu(e, t, n) {
  e.clear(), t.clear();
  for (const o of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = o, u = { edgeId: o.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Ca("source", u, d, e, r, a), Ca("target", u, l, e, s, c), t.set(o.id, o);
  }
}
function lu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : lu(n, t) : !1;
}
function Ia(e, t, n) {
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
function Nx(e, t, n, o) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === o) && (!a.parentId || !lu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function ui({ nodeId: e, dragItems: t, nodeLookup: n, dragging: o = !0 }) {
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
function jx({ dragItems: e, snapGrid: t, x: n, y: o }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: o - r.distance.y
  }, a = lo(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function Sx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: o, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, p = !1, f = null, h = !1, g = !1, b = null;
  function w({ noDragClassName: j, handleSelector: y, domNode: x, isSelectable: N, nodeId: v, nodeClickDistance: S = 0 }) {
    f = We(x);
    function k({ x: A, y: z }) {
      const { nodeLookup: E, nodeExtent: D, snapGrid: I, snapToGrid: $, nodeOrigin: L, onNodeDrag: P, onSelectionDrag: K, onError: F, updateNodePositions: B } = t();
      s = { x: A, y: z };
      let q = !1;
      const G = c.size > 1, X = G && D ? Mi(co(c)) : null, le = G && $ ? jx({
        dragItems: c,
        snapGrid: I,
        x: A,
        y: z
      }) : null;
      for (const [Z, O] of c) {
        if (!E.has(Z))
          continue;
        let Y = { x: A - O.distance.x, y: z - O.distance.y };
        $ && (Y = le ? {
          x: Math.round(Y.x + le.x),
          y: Math.round(Y.y + le.y)
        } : lo(Y, I));
        let ae = null;
        if (G && D && !O.extent && X) {
          const { positionAbsolute: oe } = O.internals, de = oe.x - X.x + D[0][0], H = oe.x + O.measured.width - X.x2 + D[1][0], ee = oe.y - X.y + D[0][1], ge = oe.y + O.measured.height - X.y2 + D[1][1];
          ae = [
            [de, ee],
            [H, ge]
          ];
        }
        const { position: ce, positionAbsolute: te } = Ul({
          nodeId: Z,
          nextPosition: Y,
          nodeLookup: E,
          nodeExtent: ae || D,
          nodeOrigin: L,
          onError: F
        });
        q = q || O.position.x !== ce.x || O.position.y !== ce.y, O.position = ce, O.internals.positionAbsolute = te;
      }
      if (g = g || q, !!q && (B(c, !0), b && (o || P || !v && K))) {
        const [Z, O] = ui({
          nodeId: v,
          dragItems: c,
          nodeLookup: E
        });
        o?.(b, c, Z, O), P?.(b, Z, O), v || K?.(b, O);
      }
    }
    async function M() {
      if (!d)
        return;
      const { transform: A, panBy: z, autoPanSpeed: E, autoPanOnNodeDrag: D } = t();
      if (!D) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [I, $] = ls(l, d, E);
      (I !== 0 || $ !== 0) && (s.x = (s.x ?? 0) - I / A[2], s.y = (s.y ?? 0) - $ / A[2], await z({ x: I, y: $ }) && k(s)), a = requestAnimationFrame(M);
    }
    function R(A) {
      const { nodeLookup: z, multiSelectionActive: E, nodesDraggable: D, transform: I, snapGrid: $, snapToGrid: L, selectNodesOnDrag: P, onNodeDragStart: K, onSelectionDragStart: F, unselectNodesAndEdges: B } = t();
      p = !0, (!P || !N) && !E && v && (z.get(v)?.selected || B()), N && P && v && e?.(v);
      const q = Bn(A.sourceEvent, { transform: I, snapGrid: $, snapToGrid: L, containerBounds: d });
      if (s = q, c = Nx(z, D, q, v), c.size > 0 && (n || K || !v && F)) {
        const [G, X] = ui({
          nodeId: v,
          dragItems: c,
          nodeLookup: z
        });
        n?.(A.sourceEvent, c, G, X), K?.(A.sourceEvent, G, X), v || F?.(A.sourceEvent, X);
      }
    }
    const T = El().clickDistance(S).on("start", (A) => {
      const { domNode: z, nodeDragThreshold: E, transform: D, snapGrid: I, snapToGrid: $ } = t();
      d = z?.getBoundingClientRect() || null, h = !1, g = !1, b = A.sourceEvent, E === 0 && R(A), s = Bn(A.sourceEvent, { transform: D, snapGrid: I, snapToGrid: $, containerBounds: d }), l = Ge(A.sourceEvent, d);
    }).on("drag", (A) => {
      const { autoPanOnNodeDrag: z, transform: E, snapGrid: D, snapToGrid: I, nodeDragThreshold: $, nodeLookup: L } = t(), P = Bn(A.sourceEvent, { transform: E, snapGrid: D, snapToGrid: I, containerBounds: d });
      if (b = A.sourceEvent, (A.sourceEvent.type === "touchmove" && A.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      v && !L.has(v)) && (h = !0), !h) {
        if (!u && z && p && (u = !0, M()), !p) {
          const K = Ge(A.sourceEvent, d), F = K.x - l.x, B = K.y - l.y;
          Math.sqrt(F * F + B * B) > $ && R(A);
        }
        (s.x !== P.xSnapped || s.y !== P.ySnapped) && c && p && (l = Ge(A.sourceEvent, d), k(P));
      }
    }).on("end", (A) => {
      if (!p || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, p = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: z, updateNodePositions: E, onNodeDragStop: D, onSelectionDragStop: I } = t();
        if (g && (E(c, !1), g = !1), r || D || !v && I) {
          const [$, L] = ui({
            nodeId: v,
            dragItems: c,
            nodeLookup: z,
            dragging: !1
          });
          r?.(A.sourceEvent, c, $, L), D?.(A.sourceEvent, $, L), v || I?.(A.sourceEvent, L);
        }
      }
    }).filter((A) => {
      const z = A.target;
      return !A.button && (!j || !Ia(z, `.${j}`, x)) && (!y || Ia(z, y, x));
    });
    f.call(T);
  }
  function m() {
    f?.on(".drag", null);
  }
  return {
    update: w,
    destroy: m
  };
}
function Ex(e, t, n) {
  const o = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Qn(r, cn(s)) > 0 && o.push(s);
  return o;
}
const Cx = 250;
function Ix(e, t, n, o) {
  let r = [], s = 1 / 0;
  const a = Ex(e, n, t + Cx);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (o.nodeId === l.nodeId && o.type === l.type && o.id === l.id)
        continue;
      const { x: d, y: p } = zt(c, l, l.position, !0), f = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(p - e.y, 2));
      f > t || (f < s ? (r = [{ ...l, x: d, y: p }], s = f) : f === s && r.push({ ...l, x: d, y: p }));
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
function uu(e, t, n, o, r, s = !1) {
  const a = o.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...zt(a, u, u.position, !0) } : u;
}
function du(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function kx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const fu = () => !0;
function Ax(e, { connectionMode: t, connectionRadius: n, handleId: o, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: p, panBy: f, cancelConnection: h, onConnectStart: g, onConnect: b, onConnectEnd: w, isValidConnection: m = fu, onReconnectEnd: j, updateConnection: y, getTransform: x, getFromHandle: N, autoPanSpeed: v, dragThreshold: S = 1, handleDomNode: k }) {
  const M = Jl(e.target);
  let R = 0, T;
  const { x: A, y: z } = Ge(e), E = du(s, k), D = c?.getBoundingClientRect();
  let I = !1;
  if (!D || !E)
    return;
  const $ = uu(r, E, o, u, t);
  if (!$)
    return;
  let L = Ge(e, D), P = !1, K = null, F = !1, B = null;
  function q() {
    if (!d || !D)
      return;
    const [ce, te] = ls(L, D, v);
    f({ x: ce, y: te }), R = requestAnimationFrame(q);
  }
  const G = {
    ...$,
    nodeId: r,
    type: E,
    position: $.position
  }, X = u.get(r);
  let Z = {
    inProgress: !0,
    isValid: null,
    from: zt(X, G, ne.Left, !0),
    fromHandle: G,
    fromPosition: G.position,
    fromNode: X,
    to: L,
    toHandle: null,
    toPosition: ga[G.position],
    toNode: null,
    pointer: L
  };
  function O() {
    I = !0, y(Z), g?.(e, { nodeId: r, handleId: o, handleType: E });
  }
  S === 0 && O();
  function Y(ce) {
    if (!I) {
      const { x: ge, y: be } = Ge(ce), Ke = ge - A, Ue = be - z;
      if (!(Ke * Ke + Ue * Ue > S * S))
        return;
      O();
    }
    if (!N() || !G) {
      ae(ce);
      return;
    }
    const te = x();
    L = Ge(ce, D), T = Ix(hn(L, te, !1, [1, 1]), n, u, G), P || (q(), P = !0);
    const oe = pu(ce, {
      handle: T,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: o,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: M,
      lib: l,
      flowId: p,
      nodeLookup: u
    });
    B = oe.handleDomNode, K = oe.connection, F = kx(!!T, oe.isValid);
    const de = u.get(r), H = de ? zt(de, G, ne.Left, !0) : Z.from, ee = {
      ...Z,
      from: H,
      isValid: F,
      to: oe.toHandle && F ? ln({ x: oe.toHandle.x, y: oe.toHandle.y }, te) : L,
      toHandle: oe.toHandle,
      toPosition: F && oe.toHandle ? oe.toHandle.position : ga[G.position],
      toNode: oe.toHandle ? u.get(oe.toHandle.nodeId) : null,
      pointer: L
    };
    y(ee), Z = ee;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (I) {
        (T || B) && K && F && b?.(K);
        const { inProgress: te, ...oe } = Z, de = {
          ...oe,
          toPosition: Z.toHandle ? Z.toPosition : null
        };
        w?.(ce, de), s && j?.(ce, de);
      }
      h(), cancelAnimationFrame(R), P = !1, F = !1, K = null, B = null, M.removeEventListener("mousemove", Y), M.removeEventListener("mouseup", ae), M.removeEventListener("touchmove", Y), M.removeEventListener("touchend", ae);
    }
  }
  M.addEventListener("mousemove", Y), M.addEventListener("mouseup", ae), M.addEventListener("touchmove", Y), M.addEventListener("touchend", ae);
}
function pu(e, { handle: t, connectionMode: n, fromNodeId: o, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = fu, nodeLookup: d }) {
  const p = s === "target", f = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: g } = Ge(e), b = a.elementFromPoint(h, g), w = b?.classList.contains(`${c}-flow__handle`) ? b : f, m = {
    handleDomNode: w,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (w) {
    const j = du(void 0, w), y = w.getAttribute("data-nodeid"), x = w.getAttribute("data-handleid"), N = w.classList.contains("connectable"), v = w.classList.contains("connectableend");
    if (!y || !j)
      return m;
    const S = {
      source: p ? y : o,
      sourceHandle: p ? x : r,
      target: p ? o : y,
      targetHandle: p ? r : x
    };
    m.connection = S;
    const M = N && v && (n === sn.Strict ? p && j === "source" || !p && j === "target" : y !== o || x !== r);
    m.isValid = M && l(S), m.toHandle = uu(y, j, x, d, n, !0);
  }
  return m;
}
const zi = {
  onPointerDown: Ax,
  isValid: pu
};
function _x({ domNode: e, panZoom: t, getTransform: n, getViewScale: o }) {
  const r = We(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: p = !0, zoomable: f = !0, inversePan: h = !1 }) {
    const g = (y) => {
      if (y.sourceEvent.type !== "wheel" || !t)
        return;
      const x = n(), N = y.sourceEvent.ctrlKey && eo() ? 10 : 1, v = -y.sourceEvent.deltaY * (y.sourceEvent.deltaMode === 1 ? 0.05 : y.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = x[2] * Math.pow(2, v * N);
      t.scaleTo(S);
    };
    let b = [0, 0];
    const w = (y) => {
      (y.sourceEvent.type === "mousedown" || y.sourceEvent.type === "touchstart") && (b = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ]);
    }, m = (y) => {
      const x = n();
      if (y.sourceEvent.type !== "mousemove" && y.sourceEvent.type !== "touchmove" || !t)
        return;
      const N = [
        y.sourceEvent.clientX ?? y.sourceEvent.touches[0].clientX,
        y.sourceEvent.clientY ?? y.sourceEvent.touches[0].clientY
      ], v = [N[0] - b[0], N[1] - b[1]];
      b = N;
      const S = o() * Math.max(x[2], Math.log(x[2])) * (h ? -1 : 1), k = {
        x: x[0] - v[0] * S,
        y: x[1] - v[1] * S
      }, M = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: x[2]
      }, M, c);
    }, j = Ol().on("start", w).on("zoom", p ? m : null).on("zoom.wheel", f ? g : null);
    r.call(j, {});
  }
  function a() {
    r.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: qe
  };
}
const Tr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), di = ({ x: e, y: t, zoom: n }) => Ar.translate(e, t).scale(n), Zt = (e, t) => e.target.closest(`.${t}`), hu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Dx = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, fi = (e, t = 0, n = Dx, o = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || o(), r ? e.transition().duration(t).ease(n).on("end", o) : e;
}, gu = (e) => {
  const t = e.ctrlKey && eo() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Tx({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: o, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Zt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const p = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const w = qe(d), m = gu(d), j = p * Math.pow(2, m);
      o.scaleTo(n, j, w, d);
      return;
    }
    const f = d.deltaMode === 1 ? 20 : 1;
    let h = r === Tt.Vertical ? 0 : d.deltaX * f, g = r === Tt.Horizontal ? 0 : d.deltaY * f;
    !eo() && d.shiftKey && r !== Tt.Vertical && (h = d.deltaY * f, g = 0), o.translateBy(
      n,
      -(h / p) * s,
      -(g / p) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const b = Tr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, b), e.panScrollTimeout = setTimeout(() => {
      l?.(d, b), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, b));
  };
}
function $x({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(o, r) {
    const s = o.type === "wheel", a = !t && s && !o.ctrlKey, c = Zt(o, e);
    if (o.ctrlKey && s && c && o.preventDefault(), a || c)
      return null;
    o.preventDefault(), n.call(this, o, r);
  };
}
function Mx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (o) => {
    if (o.sourceEvent?.internal)
      return;
    const r = Tr(o.transform);
    e.mouseButton = o.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, o.sourceEvent?.type === "mousedown" && t(!0), n && n?.(o.sourceEvent, r);
  };
}
function Px({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: o, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && hu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || o([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, Tr(s.transform));
  };
}
function Rx({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: o, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && hu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, o(!1), r)) {
      const c = Tr(a.transform);
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
function zx({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: o, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (p) => {
    const f = e || t, h = n && p.ctrlKey, g = p.type === "wheel";
    if (p.button === 1 && p.type === "mousedown" && (Zt(p, `${l}-flow__node`) || Zt(p, `${l}-flow__edge`)))
      return !0;
    if (!o && !f && !r && !s && !n || a || d && !g || Zt(p, c) && g || Zt(p, u) && (!g || r && g && !e) || !n && p.ctrlKey && g)
      return !1;
    if (!n && p.type === "touchstart" && p.touches?.length > 1)
      return p.preventDefault(), !1;
    if (!f && !r && !h && g || !o && (p.type === "mousedown" || p.type === "touchstart") || Array.isArray(o) && !o.includes(p.button) && p.type === "mousedown")
      return !1;
    const b = Array.isArray(o) && o.includes(p.button) || !p.button || p.button <= 1;
    return (!p.ctrlKey || g) && b;
  };
}
function Lx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: o, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), p = Ol().scaleExtent([t, n]).translateExtent(o), f = We(e).call(p);
  j({
    x: r.x,
    y: r.y,
    zoom: an(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], o);
  const h = f.on("wheel.zoom"), g = f.on("dblclick.zoom");
  p.wheelDelta(gu);
  async function b(T, A) {
    return f ? new Promise((z) => {
      p?.interpolate(A?.interpolate === "linear" ? Fn : Uo).transform(fi(f, A?.duration, A?.ease, () => z(!0)), T);
    }) : !1;
  }
  function w({ noWheelClassName: T, noPanClassName: A, onPaneContextMenu: z, userSelectionActive: E, panOnScroll: D, panOnDrag: I, panOnScrollMode: $, panOnScrollSpeed: L, preventScrolling: P, zoomOnPinch: K, zoomOnScroll: F, zoomOnDoubleClick: B, zoomActivationKeyPressed: q, lib: G, onTransformChange: X, connectionInProgress: le, paneClickDistance: Z, selectionOnDrag: O }) {
    E && !l.isZoomingOrPanning && m();
    const Y = D && !q && !E;
    p.clickDistance(O ? 1 / 0 : !Ze(Z) || Z < 0 ? 0 : Z);
    const ae = Y ? Tx({
      zoomPanValues: l,
      noWheelClassName: T,
      d3Selection: f,
      d3Zoom: p,
      panOnScrollMode: $,
      panOnScrollSpeed: L,
      zoomOnPinch: K,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : $x({
      noWheelClassName: T,
      preventScrolling: P,
      d3ZoomHandler: h
    });
    f.on("wheel.zoom", ae, { passive: !1 });
    const ce = Mx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    p.on("start", ce);
    const te = Px({
      zoomPanValues: l,
      panOnDrag: I,
      onPaneContextMenu: !!z,
      onPanZoom: s,
      onTransformChange: X
    });
    p.on("zoom", te);
    const oe = Rx({
      zoomPanValues: l,
      panOnDrag: I,
      panOnScroll: D,
      onPaneContextMenu: z,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    p.on("end", oe);
    const de = zx({
      zoomActivationKeyPressed: q,
      panOnDrag: I,
      zoomOnScroll: F,
      panOnScroll: D,
      zoomOnDoubleClick: B,
      zoomOnPinch: K,
      userSelectionActive: E,
      noPanClassName: A,
      noWheelClassName: T,
      lib: G,
      connectionInProgress: le
    });
    p.filter(de), B ? f.on("dblclick.zoom", g) : f.on("dblclick.zoom", null);
  }
  function m() {
    p.on("zoom", null);
  }
  async function j(T, A, z) {
    const E = di(T), D = p?.constrain()(E, A, z);
    return D && await b(D), D;
  }
  async function y(T, A) {
    const z = di(T);
    return await b(z, A), z;
  }
  function x(T) {
    if (f) {
      const A = di(T), z = f.property("__zoom");
      (z.k !== T.zoom || z.x !== T.x || z.y !== T.y) && p?.transform(f, A, null, { sync: !0 });
    }
  }
  function N() {
    const T = f ? Vl(f.node()) : { x: 0, y: 0, k: 1 };
    return { x: T.x, y: T.y, zoom: T.k };
  }
  async function v(T, A) {
    return f ? new Promise((z) => {
      p?.interpolate(A?.interpolate === "linear" ? Fn : Uo).scaleTo(fi(f, A?.duration, A?.ease, () => z(!0)), T);
    }) : !1;
  }
  async function S(T, A) {
    return f ? new Promise((z) => {
      p?.interpolate(A?.interpolate === "linear" ? Fn : Uo).scaleBy(fi(f, A?.duration, A?.ease, () => z(!0)), T);
    }) : !1;
  }
  function k(T) {
    p?.scaleExtent(T);
  }
  function M(T) {
    p?.translateExtent(T);
  }
  function R(T) {
    const A = !Ze(T) || T < 0 ? 0 : T;
    p?.clickDistance(A);
  }
  return {
    update: w,
    destroy: m,
    setViewport: y,
    setViewportConstrained: j,
    getViewport: N,
    scaleTo: v,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: M,
    syncViewport: x,
    setClickDistance: R
  };
}
var un;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(un || (un = {}));
function Vx({ width: e, prevWidth: t, height: n, prevHeight: o, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - o, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function ka(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), o = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: o,
    affectsY: r
  };
}
function xt(e, t) {
  return Math.max(0, t - e);
}
function wt(e, t) {
  return Math.max(0, e - t);
}
function zo(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function Aa(e, t) {
  return e ? !t : t;
}
function Ox(e, t, n, o, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: p } = t, f = d && p, { xSnapped: h, ySnapped: g } = n, { minWidth: b, maxWidth: w, minHeight: m, maxHeight: j } = o, { x: y, y: x, width: N, height: v, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), M = Math.floor(p ? g - e.pointerY : 0);
  const R = N + (u ? -k : k), T = v + (l ? -M : M), A = -s[0] * N, z = -s[1] * v;
  let E = zo(R, b, w), D = zo(T, m, j);
  if (a) {
    let L = 0, P = 0;
    u && k < 0 ? L = xt(y + k + A, a[0][0]) : !u && k > 0 && (L = wt(y + R + A, a[1][0])), l && M < 0 ? P = xt(x + M + z, a[0][1]) : !l && M > 0 && (P = wt(x + T + z, a[1][1])), E = Math.max(E, L), D = Math.max(D, P);
  }
  if (c) {
    let L = 0, P = 0;
    u && k > 0 ? L = wt(y + k, c[0][0]) : !u && k < 0 && (L = xt(y + R, c[1][0])), l && M > 0 ? P = wt(x + M, c[0][1]) : !l && M < 0 && (P = xt(x + T, c[1][1])), E = Math.max(E, L), D = Math.max(D, P);
  }
  if (r) {
    if (d) {
      const L = zo(R / S, m, j) * S;
      if (E = Math.max(E, L), a) {
        let P = 0;
        !u && !l || u && !l && f ? P = wt(x + z + R / S, a[1][1]) * S : P = xt(x + z + (u ? k : -k) / S, a[0][1]) * S, E = Math.max(E, P);
      }
      if (c) {
        let P = 0;
        !u && !l || u && !l && f ? P = xt(x + R / S, c[1][1]) * S : P = wt(x + (u ? k : -k) / S, c[0][1]) * S, E = Math.max(E, P);
      }
    }
    if (p) {
      const L = zo(T * S, b, w) / S;
      if (D = Math.max(D, L), a) {
        let P = 0;
        !u && !l || l && !u && f ? P = wt(y + T * S + A, a[1][0]) / S : P = xt(y + (l ? M : -M) * S + A, a[0][0]) / S, D = Math.max(D, P);
      }
      if (c) {
        let P = 0;
        !u && !l || l && !u && f ? P = xt(y + T * S, c[1][0]) / S : P = wt(y + (l ? M : -M) * S, c[0][0]) / S, D = Math.max(D, P);
      }
    }
  }
  M = M + (M < 0 ? D : -D), k = k + (k < 0 ? E : -E), r && (f ? R > T * S ? M = (Aa(u, l) ? -k : k) / S : k = (Aa(u, l) ? -M : M) * S : d ? (M = k / S, l = u) : (k = M * S, u = l));
  const I = u ? y + k : y, $ = l ? x + M : x;
  return {
    width: N + (u ? -k : k),
    height: v + (l ? -M : M),
    x: s[0] * k * (u ? -1 : 1) + I,
    y: s[1] * M * (l ? -1 : 1) + $
  };
}
const yu = { width: 0, height: 0, x: 0, y: 0 }, Hx = {
  ...yu,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Wx(e, t, n) {
  const o = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [o - c, r - u],
    [o + s - c, r + a - u]
  ];
}
function Fx({ domNode: e, nodeId: t, getStoreItems: n, onChange: o, onEnd: r }) {
  const s = We(e);
  let a = {
    controlDirection: ka("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: p, resizeDirection: f, onResizeStart: h, onResize: g, onResizeEnd: b, shouldResize: w }) {
    let m = { ...yu }, j = { ...Hx };
    a = {
      boundaries: d,
      resizeDirection: f,
      keepAspectRatio: p,
      controlDirection: ka(l)
    };
    let y, x = null, N = [], v, S, k, M = !1;
    const R = El().on("start", (T) => {
      const { nodeLookup: A, transform: z, snapGrid: E, snapToGrid: D, nodeOrigin: I, paneDomNode: $ } = n();
      if (y = A.get(t), !y)
        return;
      x = $?.getBoundingClientRect() ?? null;
      const { xSnapped: L, ySnapped: P } = Bn(T.sourceEvent, {
        transform: z,
        snapGrid: E,
        snapToGrid: D,
        containerBounds: x
      });
      m = {
        width: y.measured.width ?? 0,
        height: y.measured.height ?? 0,
        x: y.position.x ?? 0,
        y: y.position.y ?? 0
      }, j = {
        ...m,
        pointerX: L,
        pointerY: P,
        aspectRatio: m.width / m.height
      }, v = void 0, S = Rt(y.extent) ? y.extent : void 0, y.parentId && (y.extent === "parent" || y.expandParent) && (v = A.get(y.parentId)), v && y.extent === "parent" && (S = [
        [0, 0],
        [v.measured.width, v.measured.height]
      ]), N = [], k = void 0;
      for (const [K, F] of A)
        if (F.parentId === t && (N.push({
          id: K,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const B = Wx(F, y, F.origin ?? I);
          k ? k = [
            [Math.min(B[0][0], k[0][0]), Math.min(B[0][1], k[0][1])],
            [Math.max(B[1][0], k[1][0]), Math.max(B[1][1], k[1][1])]
          ] : k = B;
        }
      h?.(T, { ...m });
    }).on("drag", (T) => {
      const { transform: A, snapGrid: z, snapToGrid: E, nodeOrigin: D } = n(), I = Bn(T.sourceEvent, {
        transform: A,
        snapGrid: z,
        snapToGrid: E,
        containerBounds: x
      }), $ = [];
      if (!y)
        return;
      const { x: L, y: P, width: K, height: F } = m, B = {}, q = y.origin ?? D, { width: G, height: X, x: le, y: Z } = Ox(j, a.controlDirection, I, a.boundaries, a.keepAspectRatio, q, S, k), O = G !== K, Y = X !== F, ae = le !== L && O, ce = Z !== P && Y;
      if (!ae && !ce && !O && !Y)
        return;
      if ((ae || ce || q[0] === 1 || q[1] === 1) && (B.x = ae ? le : m.x, B.y = ce ? Z : m.y, m.x = B.x, m.y = B.y, N.length > 0)) {
        const H = le - L, ee = Z - P;
        for (const ge of N)
          ge.position = {
            x: ge.position.x - H + q[0] * (G - K),
            y: ge.position.y - ee + q[1] * (X - F)
          }, $.push(ge);
      }
      if ((O || Y) && (B.width = O && (!a.resizeDirection || a.resizeDirection === "horizontal") ? G : m.width, B.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? X : m.height, m.width = B.width, m.height = B.height), v && y.expandParent) {
        const H = q[0] * (B.width ?? 0);
        B.x && B.x < H && (m.x = H, j.x = j.x - (B.x - H));
        const ee = q[1] * (B.height ?? 0);
        B.y && B.y < ee && (m.y = ee, j.y = j.y - (B.y - ee));
      }
      const te = Vx({
        width: m.width,
        prevWidth: K,
        height: m.height,
        prevHeight: F,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), oe = { ...m, direction: te };
      w?.(T, oe) !== !1 && (M = !0, g?.(T, oe), o(B, $));
    }).on("end", (T) => {
      M && (b?.(T, { ...m }), r?.({ ...m }), M = !1);
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
var pi = { exports: {} }, hi = {}, gi = { exports: {} }, yi = {};
var _a;
function Bx() {
  if (_a) return yi;
  _a = 1;
  var e = it;
  function t(p, f) {
    return p === f && (p !== 0 || 1 / p === 1 / f) || p !== p && f !== f;
  }
  var n = typeof Object.is == "function" ? Object.is : t, o = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(p, f) {
    var h = f(), g = o({ inst: { value: h, getSnapshot: f } }), b = g[0].inst, w = g[1];
    return s(
      function() {
        b.value = h, b.getSnapshot = f, u(b) && w({ inst: b });
      },
      [p, h, f]
    ), r(
      function() {
        return u(b) && w({ inst: b }), p(function() {
          u(b) && w({ inst: b });
        });
      },
      [p]
    ), a(h), h;
  }
  function u(p) {
    var f = p.getSnapshot;
    p = p.value;
    try {
      var h = f();
      return !n(p, h);
    } catch {
      return !0;
    }
  }
  function l(p, f) {
    return f();
  }
  var d = typeof window > "u" || typeof window.document > "u" || typeof window.document.createElement > "u" ? l : c;
  return yi.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, yi;
}
var Da;
function Kx() {
  return Da || (Da = 1, gi.exports = Bx()), gi.exports;
}
var Ta;
function Ux() {
  if (Ta) return hi;
  Ta = 1;
  var e = it, t = Kx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var o = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return hi.useSyncExternalStoreWithSelector = function(l, d, p, f, h) {
    var g = s(null);
    if (g.current === null) {
      var b = { hasValue: !1, value: null };
      g.current = b;
    } else b = g.current;
    g = c(
      function() {
        function m(v) {
          if (!j) {
            if (j = !0, y = v, v = f(v), h !== void 0 && b.hasValue) {
              var S = b.value;
              if (h(S, v))
                return x = S;
            }
            return x = v;
          }
          if (S = x, o(y, v)) return S;
          var k = f(v);
          return h !== void 0 && h(S, k) ? (y = v, S) : (y = v, x = k);
        }
        var j = !1, y, x, N = p === void 0 ? null : p;
        return [
          function() {
            return m(d());
          },
          N === null ? void 0 : function() {
            return m(N());
          }
        ];
      },
      [d, p, f, h]
    );
    var w = r(l, g[0], g[1]);
    return a(
      function() {
        b.hasValue = !0, b.value = w;
      },
      [w]
    ), u(w), w;
  }, hi;
}
var $a;
function Xx() {
  return $a || ($a = 1, pi.exports = Ux()), pi.exports;
}
var qx = Xx();
const Yx = /* @__PURE__ */ bf(qx), Zx = {}, Ma = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), o = (d, p) => {
    const f = typeof d == "function" ? d(t) : d;
    if (!Object.is(f, t)) {
      const h = t;
      t = p ?? (typeof f != "object" || f === null) ? f : Object.assign({}, t, f), n.forEach((g) => g(t, h));
    }
  }, r = () => t, u = { setState: o, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Zx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(o, r, u);
  return u;
}, Gx = (e) => e ? Ma(e) : Ma, { useDebugValue: Jx } = it, { useSyncExternalStoreWithSelector: Qx } = Yx, ew = (e) => e;
function mu(e, t = ew, n) {
  const o = Qx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Jx(o), o;
}
const Pa = (e, t) => {
  const n = Gx(e), o = (r, s = t) => mu(n, r, s);
  return Object.assign(o, n), o;
}, tw = (e, t) => e ? Pa(e, t) : Pa;
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
var mi = { exports: {} }, Ae = {};
var Ra;
function nw() {
  if (Ra) return Ae;
  Ra = 1;
  var e = it;
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
    var p = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: r,
      key: p == null ? null : "" + p,
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
  return Ae.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = o, Ae.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, Ae.flushSync = function(u) {
    var l = a.T, d = o.p;
    try {
      if (a.T = null, o.p = 2, u) return u();
    } finally {
      a.T = l, o.p = d, o.d.f();
    }
  }, Ae.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, o.d.C(u, l));
  }, Ae.prefetchDNS = function(u) {
    typeof u == "string" && o.d.D(u);
  }, Ae.preinit = function(u, l) {
    if (typeof u == "string" && l && typeof l.as == "string") {
      var d = l.as, p = c(d, l.crossOrigin), f = typeof l.integrity == "string" ? l.integrity : void 0, h = typeof l.fetchPriority == "string" ? l.fetchPriority : void 0;
      d === "style" ? o.d.S(
        u,
        typeof l.precedence == "string" ? l.precedence : void 0,
        {
          crossOrigin: p,
          integrity: f,
          fetchPriority: h
        }
      ) : d === "script" && o.d.X(u, {
        crossOrigin: p,
        integrity: f,
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
          o.d.M(u, {
            crossOrigin: d,
            integrity: typeof l.integrity == "string" ? l.integrity : void 0,
            nonce: typeof l.nonce == "string" ? l.nonce : void 0
          });
        }
      } else l == null && o.d.M(u);
  }, Ae.preload = function(u, l) {
    if (typeof u == "string" && typeof l == "object" && l !== null && typeof l.as == "string") {
      var d = l.as, p = c(d, l.crossOrigin);
      o.d.L(u, d, {
        crossOrigin: p,
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
        o.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else o.d.m(u);
  }, Ae.requestFormReset = function(u) {
    o.d.r(u);
  }, Ae.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, Ae.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, Ae.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, Ae.version = "19.2.7", Ae;
}
var za;
function ow() {
  if (za) return mi.exports;
  za = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), mi.exports = nw(), mi.exports;
}
var rw = ow();
const $r = Ui(null), iw = $r.Provider, xu = Be.error001("react");
function pe(e, t) {
  const n = no($r);
  if (n === null)
    throw new Error(xu);
  return mu(n, e, t);
}
function xe() {
  const e = no($r);
  if (e === null)
    throw new Error(xu);
  return ue(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const La = { display: "none" }, sw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, wu = "react-flow__node-desc", vu = "react-flow__edge-desc", aw = "react-flow__aria-live", cw = (e) => e.ariaLiveMessage, lw = (e) => e.ariaLabelConfig;
function uw({ rfId: e }) {
  const t = pe(cw);
  return i.jsx("div", { id: `${aw}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: sw, children: t });
}
function dw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(lw);
  return i.jsxs(i.Fragment, { children: [i.jsx("div", { id: `${wu}-${e}`, style: La, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), i.jsx("div", { id: `${vu}-${e}`, style: La, children: n["edge.a11yDescription.default"] }), !t && i.jsx(uw, { rfId: e })] });
}
const Mr = Rc(({ position: e = "top-left", children: t, className: n, style: o, ...r }, s) => {
  const a = `${e}`.split("-");
  return i.jsx("div", { className: Se(["react-flow__panel", n, ...a]), style: o, ref: s, ...r, children: t });
});
Mr.displayName = "Panel";
function fw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : i.jsx(Mr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: i.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const pw = (e) => {
  const t = [], n = [];
  for (const [, o] of e.nodeLookup)
    o.selected && t.push(o.internals.userNode);
  for (const [, o] of e.edgeLookup)
    o.selected && n.push(o);
  return { selectedNodes: t, selectedEdges: n };
}, Lo = (e) => e.id;
function hw(e, t) {
  return me(e.selectedNodes.map(Lo), t.selectedNodes.map(Lo)) && me(e.selectedEdges.map(Lo), t.selectedEdges.map(Lo));
}
function gw({ onSelectionChange: e }) {
  const t = xe(), { selectedNodes: n, selectedEdges: o } = pe(pw, hw);
  return Q(() => {
    const r = { nodes: n, edges: o };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, o, e]), null;
}
const yw = (e) => !!e.onSelectionChangeHandlers;
function mw({ onSelectionChange: e }) {
  const t = pe(yw);
  return e || t ? i.jsx(gw, { onSelectionChange: e }) : null;
}
const bu = [0, 0], xw = { x: 0, y: 0, zoom: 1 }, ww = [
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
], Va = [...ww, "rfId"], vw = (e) => ({
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
  translateExtent: Gn,
  nodeOrigin: bu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function bw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: o, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(vw, me), l = xe();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Oa, c();
  }), []);
  const d = re(Oa);
  return Q(
    () => {
      for (const p of Va) {
        const f = e[p], h = d.current[p];
        f !== h && (typeof e[p] > "u" || (p === "nodes" ? t(f) : p === "edges" ? n(f) : p === "minZoom" ? o(f) : p === "maxZoom" ? r(f) : p === "translateExtent" ? s(f) : p === "nodeExtent" ? a(f) : p === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: nx(f) }) : p === "fitView" ? l.setState({ fitViewQueued: f }) : p === "fitViewOptions" ? l.setState({ fitViewOptions: f }) : l.setState({ [p]: f })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Va.map((p) => e[p])
  ), null;
}
function Ha() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Nw(e) {
  const [t, n] = U(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const o = Ha(), r = () => n(o?.matches ? "dark" : "light");
    return r(), o?.addEventListener("change", r), () => {
      o?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Ha()?.matches ? "dark" : "light";
}
const Wa = typeof document < "u" ? document : null;
function to(e = null, t = { target: Wa, actInsideInputWithModifier: !0 }) {
  const [n, o] = U(!1), r = re(!1), s = re(/* @__PURE__ */ new Set([])), [a, c] = ue(() => {
    if (e !== null) {
      const l = (Array.isArray(e) ? e : [e]).filter((p) => typeof p == "string").map((p) => p.replace("+", `
`).replace(`

`, `
+`).split(`
`)), d = l.reduce((p, f) => p.concat(...f), []);
      return [l, d];
    }
    return [[], []];
  }, [e]);
  return Q(() => {
    const u = t?.target ?? Wa, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && Ql(h))
          return !1;
        const b = Ba(h.code, c);
        if (s.current.add(h[b]), Fa(a, s.current, !1)) {
          const w = h.composedPath?.()?.[0] || h.target, m = w?.nodeName === "BUTTON" || w?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && h.preventDefault(), o(!0);
        }
      }, p = (h) => {
        const g = Ba(h.code, c);
        Fa(a, s.current, !0) ? (o(!1), s.current.clear()) : s.current.delete(h[g]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, f = () => {
        s.current.clear(), o(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", p), window.addEventListener("blur", f), window.addEventListener("contextmenu", f), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", p), window.removeEventListener("blur", f), window.removeEventListener("contextmenu", f);
      };
    }
  }, [e, o]), n;
}
function Fa(e, t, n) {
  return e.filter((o) => n || o.length === t.size).some((o) => o.every((r) => t.has(r)));
}
function Ba(e, t) {
  return t.includes(e) ? "code" : "key";
}
const jw = () => {
  const e = xe();
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
      const { width: o, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = us(t, o, r, s, a, n?.padding ?? 0.1);
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
      }, d = n.snapGrid ?? r, p = n.snapToGrid ?? s;
      return hn(l, o, p, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: o } = e.getState();
      if (!o)
        return t;
      const { x: r, y: s } = o.getBoundingClientRect(), a = ln(t, n);
      return {
        x: a.x + r,
        y: a.y + s
      };
    }
  }), []);
};
function Nu(e, t) {
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
      Sw(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function Sw(e, t) {
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
function ju(e, t) {
  return Nu(e, t);
}
function Su(e, t) {
  return Nu(e, t);
}
function kt(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Gt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const o = [];
  for (const [r, s] of e) {
    const a = t.has(r);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), o.push(kt(s.id, a)));
  }
  return o;
}
function Ka({ items: e = [], lookup: t }) {
  const n = [], o = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    o.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ua(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Eu = Yl();
function Cu(e, t, n = {}) {
  return ax(e, t, {
    ...n,
    onError: n.onError ?? Eu
  });
}
function Ew(e, t, n, o = { shouldReplaceId: !0 }) {
  return cx(e, t, n, {
    ...o,
    onError: o.onError ?? Eu
  });
}
const Xa = (e) => Xm(e), Cw = (e) => Kl(e);
function Iu(e) {
  return Rc(e);
}
const Iw = typeof window < "u" ? nf : Q;
function qa(e) {
  const [t, n] = U(BigInt(0)), [o] = U(() => kw(() => n((r) => r + BigInt(1))));
  return Iw(() => {
    const r = o.get();
    r.length && (e(r), o.reset());
  }, [t]), o;
}
function kw(e) {
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
const ku = Ui(null);
function Aw({ children: e }) {
  const t = xe(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: p, nodeLookup: f, fitViewQueued: h, onNodesChangeMiddlewareMap: g } = t.getState();
    let b = u;
    for (const m of c)
      b = typeof m == "function" ? m(b) : m;
    let w = Ka({
      items: b,
      lookup: f
    });
    for (const m of g.values())
      w = m(w);
    d && l(b), w.length > 0 ? p?.(w) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: j, setNodes: y } = t.getState();
      m && y(j);
    });
  }, []), o = qa(n), r = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: p, edgeLookup: f } = t.getState();
    let h = u;
    for (const g of c)
      h = typeof g == "function" ? g(h) : g;
    d ? l(h) : p && p(Ka({
      items: h,
      lookup: f
    }));
  }, []), s = qa(r), a = ue(() => ({ nodeQueue: o, edgeQueue: s }), []);
  return i.jsx(ku.Provider, { value: a, children: e });
}
function _w() {
  const e = no(ku);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Dw = (e) => !!e.panZoom;
function ms() {
  const e = jw(), t = xe(), n = _w(), o = pe(Dw), r = ue(() => {
    const s = (p) => t.getState().nodeLookup.get(p), a = (p) => {
      n.nodeQueue.push(p);
    }, c = (p) => {
      n.edgeQueue.push(p);
    }, u = (p) => {
      const { nodeLookup: f, nodeOrigin: h } = t.getState(), g = Xa(p) ? p : f.get(p.id), b = g.parentId ? Gl(g.position, g.measured, g.parentId, f, h) : g.position, w = {
        ...g,
        position: b,
        width: g.measured?.width ?? g.width,
        height: g.measured?.height ?? g.height
      };
      return cn(w);
    }, l = (p, f, h = { replace: !1 }) => {
      a((g) => g.map((b) => {
        if (b.id === p) {
          const w = typeof f == "function" ? f(b) : f;
          return h.replace && Xa(w) ? w : { ...b, ...w };
        }
        return b;
      }));
    }, d = (p, f, h = { replace: !1 }) => {
      c((g) => g.map((b) => {
        if (b.id === p) {
          const w = typeof f == "function" ? f(b) : f;
          return h.replace && Cw(w) ? w : { ...b, ...w };
        }
        return b;
      }));
    };
    return {
      getNodes: () => t.getState().nodes.map((p) => ({ ...p })),
      getNode: (p) => s(p)?.internals.userNode,
      getInternalNode: s,
      getEdges: () => {
        const { edges: p = [] } = t.getState();
        return p.map((f) => ({ ...f }));
      },
      getEdge: (p) => t.getState().edgeLookup.get(p),
      setNodes: a,
      setEdges: c,
      addNodes: (p) => {
        const f = Array.isArray(p) ? p : [p];
        n.nodeQueue.push((h) => [...h, ...f]);
      },
      addEdges: (p) => {
        const f = Array.isArray(p) ? p : [p];
        n.edgeQueue.push((h) => [...h, ...f]);
      },
      toObject: () => {
        const { nodes: p = [], edges: f = [], transform: h } = t.getState(), [g, b, w] = h;
        return {
          nodes: p.map((m) => ({ ...m })),
          edges: f.map((m) => ({ ...m })),
          viewport: {
            x: g,
            y: b,
            zoom: w
          }
        };
      },
      deleteElements: async ({ nodes: p = [], edges: f = [] }) => {
        const { nodes: h, edges: g, onNodesDelete: b, onEdgesDelete: w, triggerNodeChanges: m, triggerEdgeChanges: j, onDelete: y, onBeforeDelete: x } = t.getState(), { nodes: N, edges: v } = await Jm({
          nodesToRemove: p,
          edgesToRemove: f,
          nodes: h,
          edges: g,
          onBeforeDelete: x
        }), S = v.length > 0, k = N.length > 0;
        if (S) {
          const M = v.map(Ua);
          w?.(v), j(M);
        }
        if (k) {
          const M = N.map(Ua);
          b?.(N), m(M);
        }
        return (k || S) && y?.({ nodes: N, edges: v }), { deletedNodes: N, deletedEdges: v };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (p, f = !0, h) => {
        const g = ma(p), b = g ? p : u(p), w = h !== void 0;
        return b ? (h || t.getState().nodes).filter((m) => {
          const j = t.getState().nodeLookup.get(m.id);
          if (j && !g && (m.id === p.id || !j.internals.positionAbsolute))
            return !1;
          const y = cn(w ? m : j), x = Qn(y, b);
          return f && x > 0 || x >= y.width * y.height || x >= b.width * b.height;
        }) : [];
      },
      isNodeIntersecting: (p, f, h = !0) => {
        const b = ma(p) ? p : u(p);
        if (!b)
          return !1;
        const w = Qn(b, f);
        return h && w > 0 || w >= f.width * f.height || w >= b.width * b.height;
      },
      updateNode: l,
      updateNodeData: (p, f, h = { replace: !1 }) => {
        l(p, (g) => {
          const b = typeof f == "function" ? f(g) : f;
          return h.replace ? { ...g, data: b } : { ...g, data: { ...g.data, ...b } };
        }, h);
      },
      updateEdge: d,
      updateEdgeData: (p, f, h = { replace: !1 }) => {
        d(p, (g) => {
          const b = typeof f == "function" ? f(g) : f;
          return h.replace ? { ...g, data: b } : { ...g, data: { ...g.data, ...b } };
        }, h);
      },
      getNodesBounds: (p) => {
        const { nodeLookup: f, nodeOrigin: h } = t.getState();
        return qm(p, { nodeLookup: f, nodeOrigin: h });
      },
      getHandleConnections: ({ type: p, id: f, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${p}${f ? `-${f}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: p, handleId: f, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${p ? f ? `-${p}-${f}` : `-${p}` : ""}`)?.values() ?? []),
      fitView: async (p) => {
        const f = t.getState().fitViewResolver ?? tx();
        return t.setState({ fitViewQueued: !0, fitViewOptions: p, fitViewResolver: f }), n.nodeQueue.push((h) => [...h]), f.promise;
      }
    };
  }, []);
  return ue(() => ({
    ...r,
    ...e,
    viewportInitialized: o
  }), [o]);
}
const Ya = (e) => e.selected, Tw = typeof window < "u" ? window : void 0;
function $w({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = xe(), { deleteElements: o } = ms(), r = to(e, { actInsideInputWithModifier: !1 }), s = to(t, { target: Tw });
  Q(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      o({ nodes: c.filter(Ya), edges: a.filter(Ya) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Mw(e) {
  const t = xe();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const o = ds(e.current);
      (o.height === 0 || o.width === 0) && t.getState().onError?.("004", Be.error004()), t.setState({ width: o.width || 500, height: o.height || 500 });
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
const Pr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Pw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function Rw({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: o = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Tt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: p, zoomActivationKeyCode: f, preventScrolling: h = !0, children: g, noWheelClassName: b, noPanClassName: w, onViewportChange: m, isControlledViewport: j, paneClickDistance: y, selectionOnDrag: x }) {
  const N = xe(), v = re(null), { userSelectionActive: S, lib: k, connectionInProgress: M } = pe(Pw, me), R = to(f), T = re();
  Mw(v);
  const A = se((z) => {
    m?.({ x: z[0], y: z[1], zoom: z[2] }), j || N.setState({ transform: z });
  }, [m, j]);
  return Q(() => {
    if (v.current) {
      T.current = Lx({
        domNode: v.current,
        minZoom: d,
        maxZoom: p,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (I) => N.setState(($) => $.paneDragging === I ? $ : { paneDragging: I }),
        onPanZoomStart: (I, $) => {
          const { onViewportChangeStart: L, onMoveStart: P } = N.getState();
          P?.(I, $), L?.($);
        },
        onPanZoom: (I, $) => {
          const { onViewportChange: L, onMove: P } = N.getState();
          P?.(I, $), L?.($);
        },
        onPanZoomEnd: (I, $) => {
          const { onViewportChangeEnd: L, onMoveEnd: P } = N.getState();
          P?.(I, $), L?.($);
        }
      });
      const { x: z, y: E, zoom: D } = T.current.getViewport();
      return N.setState({
        panZoom: T.current,
        transform: [z, E, D],
        domNode: v.current.closest(".react-flow")
      }), () => {
        T.current?.destroy();
      };
    }
  }, []), Q(() => {
    T.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: o,
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: R,
      preventScrolling: h,
      noPanClassName: w,
      userSelectionActive: S,
      noWheelClassName: b,
      lib: k,
      onTransformChange: A,
      connectionInProgress: M,
      selectionOnDrag: x,
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
    R,
    h,
    w,
    S,
    b,
    k,
    A,
    M,
    x,
    y
  ]), i.jsx("div", { className: "react-flow__renderer", ref: v, style: Pr, children: g });
}
const zw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Lw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(zw, me);
  return e && t ? i.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const xi = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Vw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function Ow({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Jn.Full, panOnDrag: o, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: p, onPaneMouseEnter: f, onPaneMouseMove: h, onPaneMouseLeave: g, children: b }) {
  const w = re(0), m = xe(), { userSelectionActive: j, elementsSelectable: y, dragging: x, connectionInProgress: N, panBy: v, autoPanSpeed: S } = pe(Vw, me), k = y && (e || j), M = re(null), R = re(), T = re(/* @__PURE__ */ new Set()), A = re(/* @__PURE__ */ new Set()), z = re(!1), E = re({ x: 0, y: 0 }), D = re(!1), I = (O) => {
    if (z.current || N) {
      z.current = !1;
      return;
    }
    l?.(O), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, $ = (O) => {
    if (Array.isArray(o) && o?.includes(2)) {
      O.preventDefault();
      return;
    }
    d?.(O);
  }, L = p ? (O) => p(O) : void 0, P = (O) => {
    z.current && (O.stopPropagation(), z.current = !1);
  }, K = (O) => {
    const { domNode: Y, transform: ae } = m.getState();
    if (R.current = Y?.getBoundingClientRect(), !R.current)
      return;
    const ce = O.target === M.current;
    if (!ce && !!O.target.closest(".nokey") || !e || !(a && ce || t) || O.button !== 0 || !O.isPrimary)
      return;
    O.target?.setPointerCapture?.(O.pointerId), z.current = !1;
    const { x: de, y: H } = Ge(O.nativeEvent, R.current), ee = hn({ x: de, y: H }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ee.x,
        startY: ee.y,
        x: de,
        y: H
      }
    }), ce || (O.stopPropagation(), O.preventDefault());
  };
  function F(O, Y) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: te, edgeLookup: oe, connectionLookup: de, triggerNodeChanges: H, triggerEdgeChanges: ee, defaultEdgeOptions: ge } = m.getState(), be = { x: ae.startX, y: ae.startY }, { x: Ke, y: Ue } = ln(be, ce), Pe = {
      startX: be.x,
      startY: be.y,
      x: O < Ke ? O : Ke,
      y: Y < Ue ? Y : Ue,
      width: Math.abs(O - Ke),
      height: Math.abs(Y - Ue)
    }, Xe = T.current, et = A.current;
    T.current = new Set(cs(te, Pe, ce, n === Jn.Partial, !0).map((ke) => ke.id)), A.current = /* @__PURE__ */ new Set();
    const Ce = ge?.selectable ?? !0;
    for (const ke of T.current) {
      const Re = de.get(ke);
      if (Re)
        for (const { edgeId: ze } of Re.values()) {
          const tt = oe.get(ze);
          tt && (tt.selectable ?? Ce) && A.current.add(ze);
        }
    }
    if (!xa(Xe, T.current)) {
      const ke = Gt(te, T.current, !0);
      H(ke);
    }
    if (!xa(et, A.current)) {
      const ke = Gt(oe, A.current);
      ee(ke);
    }
    m.setState({
      userSelectionRect: Pe,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!r || !R.current)
      return;
    const [O, Y] = ls(E.current, R.current, S);
    v({ x: O, y: Y }).then((ae) => {
      if (!z.current || !ae) {
        w.current = requestAnimationFrame(B);
        return;
      }
      const { x: ce, y: te } = E.current;
      F(ce, te), w.current = requestAnimationFrame(B);
    });
  }
  const q = () => {
    cancelAnimationFrame(w.current), w.current = 0, D.current = !1;
  };
  Q(() => () => q(), []);
  const G = (O) => {
    const { userSelectionRect: Y, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!R.current || !Y)
      return;
    const { x: te, y: oe } = Ge(O.nativeEvent, R.current);
    E.current = { x: te, y: oe };
    const de = ln({ x: Y.startX, y: Y.startY }, ae);
    if (!z.current) {
      const H = t ? 0 : s;
      if (Math.hypot(te - de.x, oe - de.y) <= H)
        return;
      ce(), c?.(O);
    }
    z.current = !0, D.current || (B(), D.current = !0), F(te, oe);
  }, X = (O) => {
    O.button === 0 && (O.target?.releasePointerCapture?.(O.pointerId), !j && O.target === M.current && m.getState().userSelectionRect && I?.(O), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), z.current && (u?.(O), m.setState({
      nodesSelectionActive: T.current.size > 0
    })), q());
  }, le = (O) => {
    O.target?.releasePointerCapture?.(O.pointerId), q();
  }, Z = o === !0 || Array.isArray(o) && o.includes(0);
  return i.jsxs("div", { className: Se(["react-flow__pane", { draggable: Z, dragging: x, selection: e }]), onClick: k ? void 0 : xi(I, M), onContextMenu: xi($, M), onWheel: xi(L, M), onPointerEnter: k ? void 0 : f, onPointerMove: k ? G : h, onPointerUp: k ? X : void 0, onPointerCancel: k ? le : void 0, onPointerDownCapture: k ? K : void 0, onClickCapture: k ? P : void 0, onPointerLeave: g, ref: M, style: Pr, children: [b, i.jsx(Lw, {})] });
}
function Li({ id: e, store: t, unselect: n = !1, nodeRef: o }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Be.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => o?.current?.blur())) : r([e]);
}
function Au({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: o, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = xe(), [u, l] = U(!1), d = re();
  return Q(() => {
    d.current = Sx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (p) => {
        Li({
          id: p,
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
const Hw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function _u() {
  const e = xe();
  return se((n) => {
    const { nodeExtent: o, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), p = /* @__PURE__ */ new Map(), f = Hw(a), h = r ? s[0] : 5, g = r ? s[1] : 5, b = n.direction.x * h * n.factor, w = n.direction.y * g * n.factor;
    for (const [, m] of l) {
      if (!f(m))
        continue;
      let j = {
        x: m.internals.positionAbsolute.x + b,
        y: m.internals.positionAbsolute.y + w
      };
      r && (j = lo(j, s));
      const { position: y, positionAbsolute: x } = Ul({
        nodeId: m.id,
        nextPosition: j,
        nodeLookup: l,
        nodeExtent: o,
        nodeOrigin: d,
        onError: c
      });
      m.position = y, m.internals.positionAbsolute = x, p.set(m.id, m);
    }
    u(p);
  }, []);
}
const xs = Ui(null), Ww = xs.Provider;
xs.Consumer;
const Du = () => no(xs), Fw = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Bw = (e, t, n) => (o) => {
  const { connectionClickStartHandle: r, connectionMode: s, connection: a } = o, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: r?.nodeId === e && r?.id === t && r?.type === n,
    isPossibleEndHandle: s === sn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!r,
    valid: d && l
  };
};
function Kw({ type: e = "source", position: t = ne.Top, isValidConnection: n, isConnectable: o = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: p, ...f }, h) {
  const g = a || null, b = e === "target", w = xe(), m = Du(), { connectOnClick: j, noPanClassName: y, rfId: x } = pe(Fw, me), { connectingFrom: N, connectingTo: v, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: M, clickConnectionInProcess: R, valid: T } = pe(Bw(m, g, e), me);
  m || w.getState().onError?.("010", Be.error010());
  const A = (D) => {
    const { defaultEdgeOptions: I, onConnect: $, hasDefaultEdges: L } = w.getState(), P = {
      ...I,
      ...D
    };
    if (L) {
      const { edges: K, setEdges: F, onError: B } = w.getState();
      F(Cu(P, K, { onError: B }));
    }
    $?.(P), c?.(P);
  }, z = (D) => {
    if (!m)
      return;
    const I = eu(D.nativeEvent);
    if (r && (I && D.button === 0 || !I)) {
      const $ = w.getState();
      zi.onPointerDown(D.nativeEvent, {
        handleDomNode: D.currentTarget,
        autoPanOnConnect: $.autoPanOnConnect,
        connectionMode: $.connectionMode,
        connectionRadius: $.connectionRadius,
        domNode: $.domNode,
        nodeLookup: $.nodeLookup,
        lib: $.lib,
        isTarget: b,
        handleId: g,
        nodeId: m,
        flowId: $.rfId,
        panBy: $.panBy,
        cancelConnection: $.cancelConnection,
        onConnectStart: $.onConnectStart,
        onConnectEnd: (...L) => w.getState().onConnectEnd?.(...L),
        updateConnection: $.updateConnection,
        onConnect: A,
        isValidConnection: n || ((...L) => w.getState().isValidConnection?.(...L) ?? !0),
        getTransform: () => w.getState().transform,
        getFromHandle: () => w.getState().connection.fromHandle,
        autoPanSpeed: $.autoPanSpeed,
        dragThreshold: $.connectionDragThreshold
      });
    }
    I ? d?.(D) : p?.(D);
  }, E = (D) => {
    const { onClickConnectStart: I, onClickConnectEnd: $, connectionClickStartHandle: L, connectionMode: P, isValidConnection: K, lib: F, rfId: B, nodeLookup: q, connection: G } = w.getState();
    if (!m || !L && !r)
      return;
    if (!L) {
      I?.(D.nativeEvent, { nodeId: m, handleId: g, handleType: e }), w.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: g } });
      return;
    }
    const X = Jl(D.target), le = n || K, { connection: Z, isValid: O } = zi.isValid(D.nativeEvent, {
      handle: {
        nodeId: m,
        id: g,
        type: e
      },
      connectionMode: P,
      fromNodeId: L.nodeId,
      fromHandleId: L.id || null,
      fromType: L.type,
      isValidConnection: le,
      flowId: B,
      doc: X,
      lib: F,
      nodeLookup: q
    });
    O && Z && A(Z);
    const Y = structuredClone(G);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, $?.(D, Y), w.setState({ connectionClickStartHandle: null });
  };
  return i.jsx("div", { "data-handleid": g, "data-nodeid": m, "data-handlepos": t, "data-id": `${x}-${m}-${g}-${e}`, className: Se([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    y,
    l,
    {
      source: !b,
      target: b,
      connectable: o,
      connectablestart: r,
      connectableend: s,
      clickconnecting: S,
      connectingfrom: N,
      connectingto: v,
      valid: T,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: o && (!M || k) && (M || R ? s : r)
    }
  ]), onMouseDown: z, onTouchStart: z, onClick: j ? E : void 0, ref: h, ...f, children: u });
}
const dn = Ne(Iu(Kw));
function Uw({ data: e, isConnectable: t, sourcePosition: n = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [e?.label, i.jsx(dn, { type: "source", position: n, isConnectable: t })] });
}
function Xw({ data: e, isConnectable: t, targetPosition: n = ne.Top, sourcePosition: o = ne.Bottom }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(dn, { type: "target", position: n, isConnectable: t }), e?.label, i.jsx(dn, { type: "source", position: o, isConnectable: t })] });
}
function qw() {
  return null;
}
function Yw({ data: e, isConnectable: t, targetPosition: n = ne.Top }) {
  return i.jsxs(i.Fragment, { children: [i.jsx(dn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const xr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Za = {
  input: Uw,
  default: Xw,
  output: Yw,
  group: qw
};
function Zw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Gw = (e) => {
  const { width: t, height: n, x: o, y: r } = co(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${o}px,${r}px)`
  };
};
function Jw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const o = xe(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(Gw, me), u = _u(), l = re(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (Au({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const p = e ? (h) => {
    const g = o.getState().nodes.filter((b) => b.selected);
    e(h, g);
  } : void 0, f = (h) => {
    Object.prototype.hasOwnProperty.call(xr, h.key) && (h.preventDefault(), u({
      direction: xr[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return i.jsx("div", { className: Se(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: i.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: p, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : f, style: {
    width: r,
    height: s
  } }) });
}
const Ga = typeof window < "u" ? window : void 0, Qw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Tu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: p, onSelectionStart: f, onSelectionEnd: h, multiSelectionKeyCode: g, panActivationKeyCode: b, zoomActivationKeyCode: w, elementsSelectable: m, zoomOnScroll: j, zoomOnPinch: y, panOnScroll: x, panOnScrollSpeed: N, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: M, defaultViewport: R, translateExtent: T, minZoom: A, maxZoom: z, preventScrolling: E, onSelectionContextMenu: D, noWheelClassName: I, noPanClassName: $, disableKeyboardA11y: L, onViewportChange: P, isControlledViewport: K }) {
  const { nodesSelectionActive: F, userSelectionActive: B } = pe(Qw, me), q = to(l, { target: Ga }), G = to(b, { target: Ga }), X = G || k, le = G || x, Z = d && X !== !0, O = q || B || Z;
  return $w({ deleteKeyCode: u, multiSelectionKeyCode: g }), i.jsx(Rw, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: j, zoomOnPinch: y, panOnScroll: le, panOnScrollSpeed: N, panOnScrollMode: v, zoomOnDoubleClick: S, panOnDrag: !q && X, defaultViewport: R, translateExtent: T, minZoom: A, maxZoom: z, zoomActivationKeyCode: w, preventScrolling: E, noWheelClassName: I, noPanClassName: $, onViewportChange: P, isControlledViewport: K, paneClickDistance: c, selectionOnDrag: Z, children: i.jsxs(Ow, { onSelectionStart: f, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: o, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: X, autoPanOnSelection: M, isSelecting: !!O, selectionMode: p, selectionKeyPressed: q, paneClickDistance: c, selectionOnDrag: Z, children: [e, F && i.jsx(Jw, { onSelectionContextMenu: D, noPanClassName: $, disableKeyboardA11y: L })] }) });
}
Tu.displayName = "FlowRenderer";
const ev = Ne(Tu), tv = (e) => (t) => e ? cs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function nv(e) {
  return pe(se(tv(e), [e]), me);
}
const ov = (e) => e.updateNodeInternals;
function rv() {
  const e = pe(ov), [t] = U(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function iv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: o }) {
  const r = xe(), s = re(null), a = re(null), c = re(e.sourcePosition), u = re(e.targetPosition), l = re(t), d = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && o?.unobserve(a.current), o?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), Q(() => () => {
    a.current && (o?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const p = l.current !== t, f = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (p || f || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function sv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: o, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: p, noDragClassName: f, noPanClassName: h, disableKeyboardA11y: g, rfId: b, nodeTypes: w, nodeClickDistance: m, onError: j }) {
  const { node: y, internals: x, isParent: N } = pe((O) => {
    const Y = O.nodeLookup.get(e), ae = O.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ae
    };
  }, me);
  let v = y.type || "default", S = w?.[v] || Za[v];
  S === void 0 && (j?.("003", Be.error003(v)), v = "default", S = w?.default || Za.default);
  const k = !!(y.draggable || c && typeof y.draggable > "u"), M = !!(y.selectable || u && typeof y.selectable > "u"), R = !!(y.connectable || l && typeof y.connectable > "u"), T = !!(y.focusable || d && typeof y.focusable > "u"), A = xe(), z = Zl(y), E = iv({ node: y, nodeType: v, hasDimensions: z, resizeObserver: p }), D = Au({
    nodeRef: E,
    disabled: y.hidden || !k,
    noDragClassName: f,
    handleSelector: y.dragHandle,
    nodeId: e,
    isSelectable: M,
    nodeClickDistance: m
  }), I = _u();
  if (y.hidden)
    return null;
  const $ = ft(y), L = Zw(y), P = M || k || t || n || o || r, K = n ? (O) => n(O, { ...x.userNode }) : void 0, F = o ? (O) => o(O, { ...x.userNode }) : void 0, B = r ? (O) => r(O, { ...x.userNode }) : void 0, q = s ? (O) => s(O, { ...x.userNode }) : void 0, G = a ? (O) => a(O, { ...x.userNode }) : void 0, X = (O) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ae } = A.getState();
    M && (!Y || !k || ae > 0) && Li({
      id: e,
      store: A,
      nodeRef: E
    }), t && t(O, { ...x.userNode });
  }, le = (O) => {
    if (!(Ql(O.nativeEvent) || g)) {
      if (Hl.includes(O.key) && M) {
        const Y = O.key === "Escape";
        Li({
          id: e,
          store: A,
          unselect: Y,
          nodeRef: E
        });
      } else if (k && y.selected && Object.prototype.hasOwnProperty.call(xr, O.key)) {
        O.preventDefault();
        const { ariaLabelConfig: Y } = A.getState();
        A.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: O.key.replace("Arrow", "").toLowerCase(),
            x: ~~x.positionAbsolute.x,
            y: ~~x.positionAbsolute.y
          })
        }), I({
          direction: xr[O.key],
          factor: O.shiftKey ? 4 : 1
        });
      }
    }
  }, Z = () => {
    if (g || !E.current?.matches(":focus-visible"))
      return;
    const { transform: O, width: Y, height: ae, autoPanOnNodeFocus: ce, setCenter: te } = A.getState();
    if (!ce)
      return;
    cs(/* @__PURE__ */ new Map([[e, y]]), { x: 0, y: 0, width: Y, height: ae }, O, !0).length > 0 || te(y.position.x + $.width / 2, y.position.y + $.height / 2, {
      zoom: O[2]
    });
  };
  return i.jsx("div", { className: Se([
    "react-flow__node",
    `react-flow__node-${v}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: k
    },
    y.className,
    {
      selected: y.selected,
      selectable: M,
      parent: N,
      draggable: k,
      dragging: D
    }
  ]), ref: E, style: {
    zIndex: x.z,
    transform: `translate(${x.positionAbsolute.x}px,${x.positionAbsolute.y}px)`,
    pointerEvents: P ? "all" : "none",
    visibility: z ? "visible" : "hidden",
    ...y.style,
    ...L
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: K, onMouseMove: F, onMouseLeave: B, onContextMenu: q, onClick: X, onDoubleClick: G, onKeyDown: T ? le : void 0, tabIndex: T ? 0 : void 0, onFocus: T ? Z : void 0, role: y.ariaRole ?? (T ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": g ? void 0 : `${wu}-${b}`, "aria-label": y.ariaLabel, ...y.domAttributes, children: i.jsx(Ww, { value: e, children: i.jsx(S, { id: e, data: y.data, type: v, positionAbsoluteX: x.positionAbsolute.x, positionAbsoluteY: x.positionAbsolute.y, selected: y.selected ?? !1, selectable: M, draggable: k, deletable: y.deletable ?? !0, isConnectable: R, sourcePosition: y.sourcePosition, targetPosition: y.targetPosition, dragging: D, dragHandle: y.dragHandle, zIndex: x.z, parentId: y.parentId, ...$ }) }) });
}
var av = Ne(sv);
const cv = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function $u(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, onError: s } = pe(cv, me), a = nv(e.onlyRenderVisibleElements), c = rv();
  return i.jsx("div", { className: "react-flow__nodes", style: Pr, children: a.map((u) => (
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
    i.jsx(av, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: o, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
$u.displayName = "NodeRenderer";
const lv = Ne($u);
function uv(e) {
  return pe(se((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const o = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && ix({
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
const dv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return i.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, fv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return i.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Ja = {
  [gr.Arrow]: dv,
  [gr.ArrowClosed]: fv
};
function pv(e) {
  const t = xe();
  return ue(() => Object.prototype.hasOwnProperty.call(Ja, e) ? Ja[e] : (t.getState().onError?.("009", Be.error009(e)), null), [e]);
}
const hv = ({ id: e, type: t, color: n, width: o = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = pv(t);
  return u ? i.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${o}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: i.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Mu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), o = pe((s) => s.defaultEdgeOptions), r = ue(() => px(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: o?.markerStart,
    defaultMarkerEnd: o?.markerEnd
  }), [n, o, t, e]);
  return r.length ? i.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: i.jsx("defs", { children: r.map((s) => i.jsx(hv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Mu.displayName = "MarkerDefinitions";
var gv = Ne(Mu);
function Pu({ x: e, y: t, label: n, labelStyle: o, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [p, f] = U({ x: 1, y: 0, width: 0, height: 0 }), h = Se(["react-flow__edge-textwrapper", l]), g = re(null);
  return Q(() => {
    if (g.current) {
      const b = g.current.getBBox();
      f({
        x: b.x,
        y: b.y,
        width: b.width,
        height: b.height
      });
    }
  }, [n]), n ? i.jsxs("g", { transform: `translate(${e - p.width / 2} ${t - p.height / 2})`, className: h, visibility: p.width ? "visible" : "hidden", ...d, children: [r && i.jsx("rect", { width: p.width + 2 * a[0], x: -a[0], y: -a[1], height: p.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), i.jsx("text", { className: "react-flow__edge-text", y: p.height / 2, dy: "0.3em", ref: g, style: o, children: n }), u] }) : null;
}
Pu.displayName = "EdgeText";
const yv = Ne(Pu);
function uo({ path: e, labelX: t, labelY: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return i.jsxs(i.Fragment, { children: [i.jsx("path", { ...d, d: e, fill: "none", className: Se(["react-flow__edge-path", d.className]) }), l ? i.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, o && Ze(t) && Ze(n) ? i.jsx(yv, { x: t, y: n, label: o, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Qa({ pos: e, x1: t, y1: n, x2: o, y2: r }) {
  return e === ne.Left || e === ne.Right ? [0.5 * (t + o), n] : [t, 0.5 * (n + r)];
}
function Ru({ sourceX: e, sourceY: t, sourcePosition: n = ne.Bottom, targetX: o, targetY: r, targetPosition: s = ne.Top }) {
  const [a, c] = Qa({
    pos: n,
    x1: e,
    y1: t,
    x2: o,
    y2: r
  }), [u, l] = Qa({
    pos: s,
    x1: o,
    y1: r,
    x2: e,
    y2: t
  }), [d, p, f, h] = tu({
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
    p,
    f,
    h
  ];
}
function zu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: p, labelBgPadding: f, labelBgBorderRadius: h, style: g, markerEnd: b, markerStart: w, interactionWidth: m }) => {
    const [j, y, x] = Ru({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), N = e.isInternal ? void 0 : t;
    return i.jsx(uo, { id: N, path: j, labelX: y, labelY: x, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: p, labelBgPadding: f, labelBgBorderRadius: h, style: g, markerEnd: b, markerStart: w, interactionWidth: m });
  });
}
const mv = zu({ isInternal: !1 }), Lu = zu({ isInternal: !0 });
mv.displayName = "SimpleBezierEdge";
Lu.displayName = "SimpleBezierEdgeInternal";
function Vu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: p, style: f, sourcePosition: h = ne.Bottom, targetPosition: g = ne.Top, markerEnd: b, markerStart: w, pathOptions: m, interactionWidth: j }) => {
    const [y, x, N] = mr({
      sourceX: n,
      sourceY: o,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: g,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(uo, { id: v, path: y, labelX: x, labelY: N, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: p, style: f, markerEnd: b, markerStart: w, interactionWidth: j });
  });
}
const Ou = Vu({ isInternal: !1 }), Hu = Vu({ isInternal: !0 });
Ou.displayName = "SmoothStepEdge";
Hu.displayName = "SmoothStepEdgeInternal";
function Wu(e) {
  return Ne(({ id: t, ...n }) => {
    const o = e.isInternal ? void 0 : t;
    return i.jsx(Ou, { ...n, id: o, pathOptions: ue(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const xv = Wu({ isInternal: !1 }), Fu = Wu({ isInternal: !0 });
xv.displayName = "StepEdge";
Fu.displayName = "StepEdgeInternal";
function Bu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: p, style: f, markerEnd: h, markerStart: g, interactionWidth: b }) => {
    const [w, m, j] = iu({ sourceX: n, sourceY: o, targetX: r, targetY: s }), y = e.isInternal ? void 0 : t;
    return i.jsx(uo, { id: y, path: w, labelX: m, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: p, style: f, markerEnd: h, markerStart: g, interactionWidth: b });
  });
}
const wv = Bu({ isInternal: !1 }), Ku = Bu({ isInternal: !0 });
wv.displayName = "StraightEdge";
Ku.displayName = "StraightEdgeInternal";
function Uu(e) {
  return Ne(({ id: t, sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a = ne.Bottom, targetPosition: c = ne.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: p, labelBgPadding: f, labelBgBorderRadius: h, style: g, markerEnd: b, markerStart: w, pathOptions: m, interactionWidth: j }) => {
    const [y, x, N] = nu({
      sourceX: n,
      sourceY: o,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), v = e.isInternal ? void 0 : t;
    return i.jsx(uo, { id: v, path: y, labelX: x, labelY: N, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: p, labelBgPadding: f, labelBgBorderRadius: h, style: g, markerEnd: b, markerStart: w, interactionWidth: j });
  });
}
const vv = Uu({ isInternal: !1 }), Xu = Uu({ isInternal: !0 });
vv.displayName = "BezierEdge";
Xu.displayName = "BezierEdgeInternal";
const ec = {
  default: Xu,
  straight: Ku,
  step: Fu,
  smoothstep: Hu,
  simplebezier: Lu
}, tc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, bv = (e, t, n) => n === ne.Left ? e - t : n === ne.Right ? e + t : e, Nv = (e, t, n) => n === ne.Top ? e - t : n === ne.Bottom ? e + t : e, nc = "react-flow__edgeupdater";
function oc({ position: e, centerX: t, centerY: n, radius: o = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return i.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: Se([nc, `${nc}-${c}`]), cx: bv(t, o, e), cy: Nv(n, o, e), r: o, stroke: "transparent", fill: "transparent" });
}
function jv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: o, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: p, setReconnecting: f, setUpdateHover: h }) {
  const g = xe(), b = (x, N) => {
    if (x.button !== 0)
      return;
    const { autoPanOnConnect: v, domNode: S, connectionMode: k, connectionRadius: M, lib: R, onConnectStart: T, cancelConnection: A, nodeLookup: z, rfId: E, panBy: D, updateConnection: I } = g.getState(), $ = N.type === "target", L = (F, B) => {
      f(!1), p?.(F, n, N.type, B);
    }, P = (F) => l?.(n, F), K = (F, B) => {
      f(!0), d?.(x, n, N.type), T?.(F, B);
    };
    zi.onPointerDown(x.nativeEvent, {
      autoPanOnConnect: v,
      connectionMode: k,
      connectionRadius: M,
      domNode: S,
      handleId: N.id,
      nodeId: N.nodeId,
      nodeLookup: z,
      isTarget: $,
      edgeUpdaterType: N.type,
      lib: R,
      flowId: E,
      cancelConnection: A,
      panBy: D,
      isValidConnection: (...F) => g.getState().isValidConnection?.(...F) ?? !0,
      onConnect: P,
      onConnectStart: K,
      onConnectEnd: (...F) => g.getState().onConnectEnd?.(...F),
      onReconnectEnd: L,
      updateConnection: I,
      getTransform: () => g.getState().transform,
      getFromHandle: () => g.getState().connection.fromHandle,
      dragThreshold: g.getState().connectionDragThreshold,
      handleDomNode: x.currentTarget
    });
  }, w = (x) => b(x, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (x) => b(x, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), j = () => h(!0), y = () => h(!1);
  return i.jsxs(i.Fragment, { children: [(e === !0 || e === "source") && i.jsx(oc, { position: c, centerX: o, centerY: r, radius: t, onMouseDown: w, onMouseEnter: j, onMouseOut: y, type: "source" }), (e === !0 || e === "target") && i.jsx(oc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: j, onMouseOut: y, type: "target" })] });
}
function Sv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: o, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: p, onReconnectStart: f, onReconnectEnd: h, rfId: g, edgeTypes: b, noPanClassName: w, onError: m, disableKeyboardA11y: j }) {
  let y = pe((te) => te.edgeLookup.get(e));
  const x = pe((te) => te.defaultEdgeOptions);
  y = x ? { ...x, ...y } : y;
  let N = y.type || "default", v = b?.[N] || ec[N];
  v === void 0 && (m?.("011", Be.error011(N)), N = "default", v = b?.default || ec.default);
  const S = !!(y.focusable || t && typeof y.focusable > "u"), k = typeof p < "u" && (y.reconnectable || n && typeof y.reconnectable > "u"), M = !!(y.selectable || o && typeof y.selectable > "u"), R = re(null), [T, A] = U(!1), [z, E] = U(!1), D = xe(), { zIndex: I, sourceX: $, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B } = pe(se((te) => {
    const oe = te.nodeLookup.get(y.source), de = te.nodeLookup.get(y.target);
    if (!oe || !de)
      return {
        zIndex: y.zIndex,
        ...tc
      };
    const H = fx({
      id: e,
      sourceNode: oe,
      targetNode: de,
      sourceHandle: y.sourceHandle || null,
      targetHandle: y.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: m
    });
    return {
      zIndex: rx({
        selected: y.selected,
        zIndex: y.zIndex,
        sourceNode: oe,
        targetNode: de,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...H || tc
    };
  }, [y.source, y.target, y.sourceHandle, y.targetHandle, y.selected, y.zIndex]), me), q = ue(() => y.markerStart ? `url('#${Pi(y.markerStart, g)}')` : void 0, [y.markerStart, g]), G = ue(() => y.markerEnd ? `url('#${Pi(y.markerEnd, g)}')` : void 0, [y.markerEnd, g]);
  if (y.hidden || $ === null || L === null || P === null || K === null)
    return null;
  const X = (te) => {
    const { addSelectedEdges: oe, unselectNodesAndEdges: de, multiSelectionActive: H } = D.getState();
    M && (D.setState({ nodesSelectionActive: !1 }), y.selected && H ? (de({ nodes: [], edges: [y] }), R.current?.blur()) : oe([e])), r && r(te, y);
  }, le = s ? (te) => {
    s(te, { ...y });
  } : void 0, Z = a ? (te) => {
    a(te, { ...y });
  } : void 0, O = c ? (te) => {
    c(te, { ...y });
  } : void 0, Y = u ? (te) => {
    u(te, { ...y });
  } : void 0, ae = l ? (te) => {
    l(te, { ...y });
  } : void 0, ce = (te) => {
    if (!j && Hl.includes(te.key) && M) {
      const { unselectNodesAndEdges: oe, addSelectedEdges: de } = D.getState();
      te.key === "Escape" ? (R.current?.blur(), oe({ edges: [y] })) : de([e]);
    }
  };
  return i.jsx("svg", { style: { zIndex: I }, children: i.jsxs("g", { className: Se([
    "react-flow__edge",
    `react-flow__edge-${N}`,
    y.className,
    w,
    {
      selected: y.selected,
      animated: y.animated,
      inactive: !M && !r,
      updating: T,
      selectable: M
    }
  ]), onClick: X, onDoubleClick: le, onContextMenu: Z, onMouseEnter: O, onMouseMove: Y, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: y.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": y.ariaLabel === null ? void 0 : y.ariaLabel || `Edge from ${y.source} to ${y.target}`, "aria-describedby": S ? `${vu}-${g}` : void 0, ref: R, ...y.domAttributes, children: [!z && i.jsx(v, { id: e, source: y.source, target: y.target, type: y.type, selected: y.selected, animated: y.animated, selectable: M, deletable: y.deletable ?? !0, label: y.label, labelStyle: y.labelStyle, labelShowBg: y.labelShowBg, labelBgStyle: y.labelBgStyle, labelBgPadding: y.labelBgPadding, labelBgBorderRadius: y.labelBgBorderRadius, sourceX: $, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B, data: y.data, style: y.style, sourceHandleId: y.sourceHandle, targetHandleId: y.targetHandle, markerStart: q, markerEnd: G, pathOptions: "pathOptions" in y ? y.pathOptions : void 0, interactionWidth: y.interactionWidth }), k && i.jsx(jv, { edge: y, isReconnectable: k, reconnectRadius: d, onReconnect: p, onReconnectStart: f, onReconnectEnd: h, sourceX: $, sourceY: L, targetX: P, targetY: K, sourcePosition: F, targetPosition: B, setUpdateHover: A, setReconnecting: E })] }) });
}
var Ev = Ne(Sv);
const Cv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function qu({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: o, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: p, onEdgeDoubleClick: f, onReconnectStart: h, onReconnectEnd: g, disableKeyboardA11y: b }) {
  const { edgesFocusable: w, edgesReconnectable: m, elementsSelectable: j, onError: y } = pe(Cv, me), x = uv(t);
  return i.jsxs("div", { className: "react-flow__edges", children: [i.jsx(gv, { defaultColor: e, rfId: n }), x.map((N) => i.jsx(Ev, { id: N, edgesFocusable: w, edgesReconnectable: m, elementsSelectable: j, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: p, onDoubleClick: f, onReconnectStart: h, onReconnectEnd: g, rfId: n, onError: y, edgeTypes: o, disableKeyboardA11y: b }, N))] });
}
qu.displayName = "EdgeRenderer";
const Iv = Ne(qu), kv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Av({ children: e }) {
  const t = pe(kv);
  return i.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function _v(e) {
  const t = ms(), n = re(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Dv = (e) => e.panZoom?.syncViewport;
function Tv(e) {
  const t = pe(Dv), n = xe();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function $v(e) {
  return e.connection.inProgress ? { ...e.connection, to: hn(e.connection.to, e.transform) } : { ...e.connection };
}
function Mv(e) {
  return $v;
}
function Pv(e) {
  const t = Mv();
  return pe(t, me);
}
const Rv = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function zv({ containerStyle: e, style: t, type: n, component: o }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe(Rv, me);
  return !(s && r && u) ? null : i.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: i.jsx("g", { className: Se(["react-flow__connection", Bl(c)]), children: i.jsx(Yu, { style: t, type: n, CustomComponent: o, isValid: c }) }) });
}
const Yu = ({ style: e, type: t = vt.Bezier, CustomComponent: n, isValid: o }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: p, toPosition: f, pointer: h } = Pv();
  if (!r)
    return;
  if (n)
    return i.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: f, connectionStatus: Bl(o), toNode: d, toHandle: p, pointer: h });
  let g = "";
  const b = {
    sourceX: s.x,
    sourceY: s.y,
    sourcePosition: u,
    targetX: l.x,
    targetY: l.y,
    targetPosition: f
  };
  switch (t) {
    case vt.Bezier:
      [g] = nu(b);
      break;
    case vt.SimpleBezier:
      [g] = Ru(b);
      break;
    case vt.Step:
      [g] = mr({
        ...b,
        borderRadius: 0
      });
      break;
    case vt.SmoothStep:
      [g] = mr(b);
      break;
    default:
      [g] = iu(b);
  }
  return i.jsx("path", { d: g, fill: "none", className: "react-flow__connection-path", style: e });
};
Yu.displayName = "ConnectionLine";
const Lv = {};
function rc(e = Lv) {
  re(e), xe(), Q(() => {
  }, [e]);
}
function Vv() {
  xe(), re(!1), Q(() => {
  }, []);
}
function Zu({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: o, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: p, onSelectionStart: f, onSelectionEnd: h, connectionLineType: g, connectionLineStyle: b, connectionLineComponent: w, connectionLineContainerStyle: m, selectionKeyCode: j, selectionOnDrag: y, selectionMode: x, multiSelectionKeyCode: N, panActivationKeyCode: v, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: M, elementsSelectable: R, defaultViewport: T, translateExtent: A, minZoom: z, maxZoom: E, preventScrolling: D, defaultMarkerColor: I, zoomOnScroll: $, zoomOnPinch: L, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: F, zoomOnDoubleClick: B, panOnDrag: q, autoPanOnSelection: G, onPaneClick: X, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: O, onPaneScroll: Y, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: te, onEdgeContextMenu: oe, onEdgeMouseEnter: de, onEdgeMouseMove: H, onEdgeMouseLeave: ee, reconnectRadius: ge, onReconnect: be, onReconnectStart: Ke, onReconnectEnd: Ue, noDragClassName: Pe, noWheelClassName: Xe, noPanClassName: et, disableKeyboardA11y: Ce, nodeExtent: ke, rfId: Re, viewport: ze, onViewportChange: tt }) {
  return rc(e), rc(t), Vv(), _v(n), Tv(ze), i.jsx(ev, { onPaneClick: X, onPaneMouseEnter: le, onPaneMouseMove: Z, onPaneMouseLeave: O, onPaneContextMenu: ae, onPaneScroll: Y, paneClickDistance: ce, deleteKeyCode: k, selectionKeyCode: j, selectionOnDrag: y, selectionMode: x, onSelectionStart: f, onSelectionEnd: h, multiSelectionKeyCode: N, panActivationKeyCode: v, zoomActivationKeyCode: S, elementsSelectable: R, zoomOnScroll: $, zoomOnPinch: L, zoomOnDoubleClick: B, panOnScroll: P, panOnScrollSpeed: K, panOnScrollMode: F, panOnDrag: q, autoPanOnSelection: G, defaultViewport: T, translateExtent: A, minZoom: z, maxZoom: E, onSelectionContextMenu: p, preventScrolling: D, noDragClassName: Pe, noWheelClassName: Xe, noPanClassName: et, disableKeyboardA11y: Ce, onViewportChange: tt, isControlledViewport: !!ze, children: i.jsxs(Av, { children: [i.jsx(Iv, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: be, onReconnectStart: Ke, onReconnectEnd: Ue, onlyRenderVisibleElements: M, onEdgeContextMenu: oe, onEdgeMouseEnter: de, onEdgeMouseMove: H, onEdgeMouseLeave: ee, reconnectRadius: ge, defaultMarkerColor: I, noPanClassName: et, disableKeyboardA11y: Ce, rfId: Re }), i.jsx(zv, { style: b, type: g, component: w, containerStyle: m }), i.jsx("div", { className: "react-flow__edgelabel-renderer" }), i.jsx(lv, { nodeTypes: e, onNodeClick: o, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: te, onlyRenderVisibleElements: M, noPanClassName: et, noDragClassName: Pe, disableKeyboardA11y: Ce, nodeExtent: ke, rfId: Re }), i.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Zu.displayName = "GraphView";
const Ov = Ne(Zu), Hv = Yl(), ic = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: p, zIndexMode: f = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), g = /* @__PURE__ */ new Map(), b = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), m = o ?? t ?? [], j = n ?? e ?? [], y = d ?? [0, 0], x = p ?? Gn;
  cu(b, w, m);
  const { nodesInitialized: N } = Ri(j, h, g, {
    nodeOrigin: y,
    nodeExtent: x,
    zIndexMode: f
  });
  let v = [0, 0, 1];
  if (a && r && s) {
    const S = co(h, {
      filter: (T) => !!((T.width || T.initialWidth) && (T.height || T.initialHeight))
    }), { x: k, y: M, zoom: R } = us(S, r, s, u, l, c?.padding ?? 0.1);
    v = [k, M, R];
  }
  return {
    rfId: "1",
    width: r ?? 0,
    height: s ?? 0,
    transform: v,
    nodes: j,
    nodesInitialized: N,
    nodeLookup: h,
    parentLookup: g,
    edges: m,
    edgeLookup: w,
    connectionLookup: b,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: o !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Gn,
    nodeExtent: x,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: sn.Strict,
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
    connection: { ...Fl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Hv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Wl,
    zIndexMode: f,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Wv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: p, zIndexMode: f }) => tw((h, g) => {
  async function b() {
    const { nodeLookup: w, panZoom: m, fitViewOptions: j, fitViewResolver: y, width: x, height: N, minZoom: v, maxZoom: S } = g();
    m && (await Gm({
      nodes: w,
      width: x,
      height: N,
      panZoom: m,
      minZoom: v,
      maxZoom: S
    }, j), y?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...ic({
      nodes: e,
      edges: t,
      width: r,
      height: s,
      fitView: a,
      fitViewOptions: c,
      minZoom: u,
      maxZoom: l,
      nodeOrigin: d,
      nodeExtent: p,
      defaultNodes: n,
      defaultEdges: o,
      zIndexMode: f
    }),
    setNodes: (w) => {
      const { nodeLookup: m, parentLookup: j, nodeOrigin: y, elevateNodesOnSelect: x, fitViewQueued: N, zIndexMode: v, nodesSelectionActive: S } = g(), { nodesInitialized: k, hasSelectedNodes: M } = Ri(w, m, j, {
        nodeOrigin: y,
        nodeExtent: p,
        elevateNodesOnSelect: x,
        checkEquality: !0,
        zIndexMode: v
      }), R = S && M;
      N && k ? (b(), h({
        nodes: w,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: R
      })) : h({ nodes: w, nodesInitialized: k, nodesSelectionActive: R });
    },
    setEdges: (w) => {
      const { connectionLookup: m, edgeLookup: j } = g();
      cu(m, j, w), h({ edges: w });
    },
    setDefaultNodesAndEdges: (w, m) => {
      if (w) {
        const { setNodes: j } = g();
        j(w), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: j } = g();
        j(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (w) => {
      const { triggerNodeChanges: m, nodeLookup: j, parentLookup: y, domNode: x, nodeOrigin: N, nodeExtent: v, debug: S, fitViewQueued: k, zIndexMode: M } = g(), { changes: R, updatedInternals: T } = vx(w, j, y, x, N, v, M);
      T && (yx(j, y, { nodeOrigin: N, nodeExtent: v, zIndexMode: M }), k ? (b(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), R?.length > 0 && (S && console.log("React Flow: trigger node changes", R), m?.(R)));
    },
    updateNodePositions: (w, m = !1) => {
      const j = [];
      let y = [];
      const { nodeLookup: x, triggerNodeChanges: N, connection: v, updateConnection: S, onNodesChangeMiddlewareMap: k } = g();
      for (const [M, R] of w) {
        const T = x.get(M), A = !!(T?.expandParent && T?.parentId && R?.position), z = {
          id: M,
          type: "position",
          position: A ? {
            x: Math.max(0, R.position.x),
            y: Math.max(0, R.position.y)
          } : R.position,
          dragging: m
        };
        if (T && v.inProgress && v.fromNode.id === T.id) {
          const E = zt(T, v.fromHandle, ne.Left, !0);
          S({ ...v, from: E });
        }
        A && T.parentId && j.push({
          id: M,
          parentId: T.parentId,
          rect: {
            ...R.internals.positionAbsolute,
            width: R.measured.width ?? 0,
            height: R.measured.height ?? 0
          }
        }), y.push(z);
      }
      if (j.length > 0) {
        const { parentLookup: M, nodeOrigin: R } = g(), T = ys(j, x, M, R);
        y.push(...T);
      }
      for (const M of k.values())
        y = M(y);
      N(y);
    },
    triggerNodeChanges: (w) => {
      const { onNodesChange: m, setNodes: j, nodes: y, hasDefaultNodes: x, debug: N } = g();
      if (w?.length) {
        if (x) {
          const v = ju(w, y);
          j(v);
        }
        N && console.log("React Flow: trigger node changes", w), m?.(w);
      }
    },
    triggerEdgeChanges: (w) => {
      const { onEdgesChange: m, setEdges: j, edges: y, hasDefaultEdges: x, debug: N } = g();
      if (w?.length) {
        if (x) {
          const v = Su(w, y);
          j(v);
        }
        N && console.log("React Flow: trigger edge changes", w), m?.(w);
      }
    },
    addSelectedNodes: (w) => {
      const { multiSelectionActive: m, edgeLookup: j, nodeLookup: y, triggerNodeChanges: x, triggerEdgeChanges: N } = g();
      if (m) {
        const v = w.map((S) => kt(S, !0));
        x(v);
        return;
      }
      x(Gt(y, /* @__PURE__ */ new Set([...w]), !0)), N(Gt(j));
    },
    addSelectedEdges: (w) => {
      const { multiSelectionActive: m, edgeLookup: j, nodeLookup: y, triggerNodeChanges: x, triggerEdgeChanges: N } = g();
      if (m) {
        const v = w.map((S) => kt(S, !0));
        N(v);
        return;
      }
      N(Gt(j, /* @__PURE__ */ new Set([...w]))), x(Gt(y, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: w, edges: m } = {}) => {
      const { edges: j, nodes: y, nodeLookup: x, triggerNodeChanges: N, triggerEdgeChanges: v } = g(), S = w || y, k = m || j, M = [];
      for (const T of S) {
        if (!T.selected)
          continue;
        const A = x.get(T.id);
        A && (A.selected = !1), M.push(kt(T.id, !1));
      }
      const R = [];
      for (const T of k)
        T.selected && R.push(kt(T.id, !1));
      N(M), v(R);
    },
    setMinZoom: (w) => {
      const { panZoom: m, maxZoom: j } = g();
      m?.setScaleExtent([w, j]), h({ minZoom: w });
    },
    setMaxZoom: (w) => {
      const { panZoom: m, minZoom: j } = g();
      m?.setScaleExtent([j, w]), h({ maxZoom: w });
    },
    setTranslateExtent: (w) => {
      g().panZoom?.setTranslateExtent(w), h({ translateExtent: w });
    },
    resetSelectedElements: () => {
      const { edges: w, nodes: m, triggerNodeChanges: j, triggerEdgeChanges: y, elementsSelectable: x } = g();
      if (!x)
        return;
      const N = m.reduce((S, k) => k.selected ? [...S, kt(k.id, !1)] : S, []), v = w.reduce((S, k) => k.selected ? [...S, kt(k.id, !1)] : S, []);
      j(N), y(v);
    },
    setNodeExtent: (w) => {
      const { nodes: m, nodeLookup: j, parentLookup: y, nodeOrigin: x, elevateNodesOnSelect: N, nodeExtent: v, zIndexMode: S } = g();
      w[0][0] === v[0][0] && w[0][1] === v[0][1] && w[1][0] === v[1][0] && w[1][1] === v[1][1] || (Ri(m, j, y, {
        nodeOrigin: x,
        nodeExtent: w,
        elevateNodesOnSelect: N,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: w }));
    },
    panBy: (w) => {
      const { transform: m, width: j, height: y, panZoom: x, translateExtent: N } = g();
      return bx({ delta: w, panZoom: x, transform: m, translateExtent: N, width: j, height: y });
    },
    setCenter: async (w, m, j) => {
      const { width: y, height: x, maxZoom: N, panZoom: v } = g();
      if (!v)
        return !1;
      const S = typeof j?.zoom < "u" ? j.zoom : N;
      return await v.setViewport({
        x: y / 2 - w * S,
        y: x / 2 - m * S,
        zoom: S
      }, { duration: j?.duration, ease: j?.ease, interpolate: j?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Fl }
      });
    },
    updateConnection: (w) => {
      h({ connection: w });
    },
    reset: () => h({ ...ic() })
  };
}, Object.is);
function Fv({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: o, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: p, zIndexMode: f, children: h }) {
  const [g] = U(() => Wv({
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
    nodeExtent: p,
    zIndexMode: f
  }));
  return i.jsx(iw, { value: g, children: i.jsx(Aw, { children: h }) });
}
function Bv({ children: e, nodes: t, edges: n, defaultNodes: o, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: p, nodeExtent: f, zIndexMode: h }) {
  return no($r) ? i.jsx(i.Fragment, { children: e }) : i.jsx(Fv, { initialNodes: t, initialEdges: n, defaultNodes: o, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: p, nodeExtent: f, zIndexMode: h, children: e });
}
const Kv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Uv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: o, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: p, onMoveEnd: f, onConnect: h, onConnectStart: g, onConnectEnd: b, onClickConnectStart: w, onClickConnectEnd: m, onNodeMouseEnter: j, onNodeMouseMove: y, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: v, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: M, onNodesDelete: R, onEdgesDelete: T, onDelete: A, onSelectionChange: z, onSelectionDragStart: E, onSelectionDrag: D, onSelectionDragStop: I, onSelectionContextMenu: $, onSelectionStart: L, onSelectionEnd: P, onBeforeDelete: K, connectionMode: F, connectionLineType: B = vt.Bezier, connectionLineStyle: q, connectionLineComponent: G, connectionLineContainerStyle: X, deleteKeyCode: le = "Backspace", selectionKeyCode: Z = "Shift", selectionOnDrag: O = !1, selectionMode: Y = Jn.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = eo() ? "Meta" : "Control", zoomActivationKeyCode: te = eo() ? "Meta" : "Control", snapToGrid: oe, snapGrid: de, onlyRenderVisibleElements: H = !1, selectNodesOnDrag: ee, nodesDraggable: ge, autoPanOnNodeFocus: be, nodesConnectable: Ke, nodesFocusable: Ue, nodeOrigin: Pe = bu, edgesFocusable: Xe, edgesReconnectable: et, elementsSelectable: Ce = !0, defaultViewport: ke = xw, minZoom: Re = 0.5, maxZoom: ze = 2, translateExtent: tt = Gn, preventScrolling: gn = !0, nodeExtent: pt, defaultMarkerColor: ht = "#b1b1b7", zoomOnScroll: St = !0, zoomOnPinch: Lr = !0, panOnScroll: Vr = !1, panOnScrollSpeed: Ht = 0.5, panOnScrollMode: Wt = Tt.Free, zoomOnDoubleClick: nt = !0, panOnDrag: Ft = !0, onPaneClick: ho, onPaneMouseEnter: go, onPaneMouseMove: Or, onPaneMouseLeave: Hr, onPaneScroll: yo, onPaneContextMenu: mo, paneClickDistance: xo = 1, nodeClickDistance: wo = 0, children: vo, onReconnect: yn, onReconnectStart: mn, onReconnectEnd: Wr, onEdgeContextMenu: Fr, onEdgeDoubleClick: Et, onEdgeMouseEnter: De, onEdgeMouseMove: xn, onEdgeMouseLeave: bo, reconnectRadius: Le = 10, onNodesChange: No, onEdgesChange: ye, noDragClassName: Ve = "nodrag", noWheelClassName: Bt = "nowheel", noPanClassName: wn = "nopan", fitView: he, fitViewOptions: gt, connectOnClick: jo, attributionPosition: vn, proOptions: bn, defaultEdgeOptions: Ie, elevateNodesOnSelect: Ct = !0, elevateEdgesOnSelect: yt = !1, disableKeyboardA11y: So = !1, autoPanOnConnect: Eo, autoPanOnNodeDrag: Co, autoPanOnSelection: Nn = !0, autoPanSpeed: Br, connectionRadius: jn, isValidConnection: Kt, onError: _e, style: Sn, id: Ut, nodeDragThreshold: En, connectionDragThreshold: Xt, viewport: Kr, onViewportChange: Ur, width: Xr, height: qr, colorMode: Io = "light", debug: ko, onScroll: Cn, ariaLabelConfig: Yr, zIndexMode: It = "basic", ...Zr }, Gr) {
  const mt = Ut || "1", Jr = Nw(Io), Qr = se((Ao) => {
    Ao.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Cn?.(Ao);
  }, [Cn]);
  return i.jsx("div", { "data-testid": "rf__wrapper", ...Zr, onScroll: Qr, style: { ...Sn, ...Kv }, ref: Gr, className: Se(["react-flow", r, Jr]), id: Ut, role: "application", children: i.jsxs(Bv, { nodes: e, edges: t, width: Xr, height: qr, fitView: he, fitViewOptions: gt, minZoom: Re, maxZoom: ze, nodeOrigin: Pe, nodeExtent: pt, zIndexMode: It, children: [i.jsx(bw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: o, onConnect: h, onConnectStart: g, onConnectEnd: b, onClickConnectStart: w, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: be, nodesConnectable: Ke, nodesFocusable: Ue, edgesFocusable: Xe, edgesReconnectable: et, elementsSelectable: Ce, elevateNodesOnSelect: Ct, elevateEdgesOnSelect: yt, minZoom: Re, maxZoom: ze, nodeExtent: pt, onNodesChange: No, onEdgesChange: ye, snapToGrid: oe, snapGrid: de, connectionMode: F, translateExtent: tt, connectOnClick: jo, defaultEdgeOptions: Ie, fitView: he, fitViewOptions: gt, onNodesDelete: R, onEdgesDelete: T, onDelete: A, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: M, onSelectionDrag: D, onSelectionDragStart: E, onSelectionDragStop: I, onMove: d, onMoveStart: p, onMoveEnd: f, noPanClassName: wn, nodeOrigin: Pe, rfId: mt, autoPanOnConnect: Eo, autoPanOnNodeDrag: Co, autoPanSpeed: Br, onError: _e, connectionRadius: jn, isValidConnection: Kt, selectNodesOnDrag: ee, nodeDragThreshold: En, connectionDragThreshold: Xt, onBeforeDelete: K, debug: ko, ariaLabelConfig: Yr, zIndexMode: It }), i.jsx(Ov, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: j, onNodeMouseMove: y, onNodeMouseLeave: x, onNodeContextMenu: N, onNodeDoubleClick: v, nodeTypes: s, edgeTypes: a, connectionLineType: B, connectionLineStyle: q, connectionLineComponent: G, connectionLineContainerStyle: X, selectionKeyCode: Z, selectionOnDrag: O, selectionMode: Y, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: te, onlyRenderVisibleElements: H, defaultViewport: ke, translateExtent: tt, minZoom: Re, maxZoom: ze, preventScrolling: gn, zoomOnScroll: St, zoomOnPinch: Lr, zoomOnDoubleClick: nt, panOnScroll: Vr, panOnScrollSpeed: Ht, panOnScrollMode: Wt, panOnDrag: Ft, autoPanOnSelection: Nn, onPaneClick: ho, onPaneMouseEnter: go, onPaneMouseMove: Or, onPaneMouseLeave: Hr, onPaneScroll: yo, onPaneContextMenu: mo, paneClickDistance: xo, nodeClickDistance: wo, onSelectionContextMenu: $, onSelectionStart: L, onSelectionEnd: P, onReconnect: yn, onReconnectStart: mn, onReconnectEnd: Wr, onEdgeContextMenu: Fr, onEdgeDoubleClick: Et, onEdgeMouseEnter: De, onEdgeMouseMove: xn, onEdgeMouseLeave: bo, reconnectRadius: Le, defaultMarkerColor: ht, noDragClassName: Ve, noWheelClassName: Bt, noPanClassName: wn, rfId: mt, disableKeyboardA11y: So, nodeExtent: pt, viewport: Kr, onViewportChange: Ur }), i.jsx(mw, { onSelectionChange: z }), vo, i.jsx(fw, { proOptions: bn, position: vn }), i.jsx(dw, { rfId: mt, disableKeyboardA11y: So })] }) });
}
var Gu = Iu(Uv);
const Xv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function qv({ children: e }) {
  const t = pe(Xv);
  return t ? rw.createPortal(e, t) : null;
}
function Yv({ dimensions: e, lineWidth: t, variant: n, className: o }) {
  return i.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Se(["react-flow__background-pattern", n, o]) });
}
function Zv({ radius: e, className: t }) {
  return i.jsx("circle", { cx: e, cy: e, r: e, className: Se(["react-flow__background-pattern", "dots", t]) });
}
var bt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(bt || (bt = {}));
const Gv = {
  [bt.Dots]: 1,
  [bt.Lines]: 1,
  [bt.Cross]: 6
}, Jv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Ju({
  id: e,
  variant: t = bt.Dots,
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
  const p = re(null), { transform: f, patternId: h } = pe(Jv, me), g = o || Gv[t], b = t === bt.Dots, w = t === bt.Cross, m = Array.isArray(n) ? n : [n, n], j = [m[0] * f[2] || 1, m[1] * f[2] || 1], y = g * f[2], x = Array.isArray(s) ? s : [s, s], N = w ? [y, y] : j, v = [
    x[0] * f[2] || 1 + N[0] / 2,
    x[1] * f[2] || 1 + N[1] / 2
  ], S = `${h}${e || ""}`;
  return i.jsxs("svg", { className: Se(["react-flow__background", l]), style: {
    ...u,
    ...Pr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: p, "data-testid": "rf__background", children: [i.jsx("pattern", { id: S, x: f[0] % j[0], y: f[1] % j[1], width: j[0], height: j[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${v[0]},-${v[1]})`, children: b ? i.jsx(Zv, { radius: y / 2, className: d }) : i.jsx(Yv, { dimensions: N, lineWidth: r, variant: t, className: d }) }), i.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
Ju.displayName = "Background";
const Qu = Ne(Ju);
function Qv() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: i.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function e0() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: i.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function t0() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: i.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function n0() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function o0() {
  return i.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: i.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Vo({ children: e, className: t, ...n }) {
  return i.jsx("button", { type: "button", className: Se(["react-flow__controls-button", t]), ...n, children: e });
}
const r0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ed({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: o = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: p = "bottom-left", orientation: f = "vertical", "aria-label": h }) {
  const g = xe(), { isInteractive: b, minZoomReached: w, maxZoomReached: m, ariaLabelConfig: j } = pe(r0, me), { zoomIn: y, zoomOut: x, fitView: N } = ms(), v = () => {
    y(), s?.();
  }, S = () => {
    x(), a?.();
  }, k = () => {
    N(r), c?.();
  }, M = () => {
    g.setState({
      nodesDraggable: !b,
      nodesConnectable: !b,
      elementsSelectable: !b
    }), u?.(!b);
  }, R = f === "horizontal" ? "horizontal" : "vertical";
  return i.jsxs(Mr, { className: Se(["react-flow__controls", R, l]), position: p, style: e, "data-testid": "rf__controls", "aria-label": h ?? j["controls.ariaLabel"], children: [t && i.jsxs(i.Fragment, { children: [i.jsx(Vo, { onClick: v, className: "react-flow__controls-zoomin", title: j["controls.zoomIn.ariaLabel"], "aria-label": j["controls.zoomIn.ariaLabel"], disabled: m, children: i.jsx(Qv, {}) }), i.jsx(Vo, { onClick: S, className: "react-flow__controls-zoomout", title: j["controls.zoomOut.ariaLabel"], "aria-label": j["controls.zoomOut.ariaLabel"], disabled: w, children: i.jsx(e0, {}) })] }), n && i.jsx(Vo, { className: "react-flow__controls-fitview", onClick: k, title: j["controls.fitView.ariaLabel"], "aria-label": j["controls.fitView.ariaLabel"], children: i.jsx(t0, {}) }), o && i.jsx(Vo, { className: "react-flow__controls-interactive", onClick: M, title: j["controls.interactive.ariaLabel"], "aria-label": j["controls.interactive.ariaLabel"], children: b ? i.jsx(o0, {}) : i.jsx(n0, {}) }), d] });
}
ed.displayName = "Controls";
const td = Ne(ed);
function i0({ id: e, x: t, y: n, width: o, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: p, selected: f, onClick: h }) {
  const { background: g, backgroundColor: b } = s || {}, w = a || g || b;
  return i.jsx("rect", { className: Se(["react-flow__minimap-node", { selected: f }, l]), x: t, y: n, rx: d, ry: d, width: o, height: r, style: {
    fill: w,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: p, onClick: h ? (m) => h(m, e) : void 0 });
}
const s0 = Ne(i0), a0 = (e) => e.nodes.map((t) => t.id), wi = (e) => e instanceof Function ? e : () => e;
function c0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: o = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = s0,
  onClick: a
}) {
  const c = pe(a0, me), u = wi(t), l = wi(e), d = wi(n), p = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return i.jsx(i.Fragment, { children: c.map((f) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    i.jsx(u0, { id: f, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: o, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: p }, f)
  )) });
}
function l0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: o, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: p, width: f, height: h } = pe((g) => {
    const b = g.nodeLookup.get(e);
    if (!b)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const w = b.internals.userNode, { x: m, y: j } = b.internals.positionAbsolute, { width: y, height: x } = ft(w);
    return {
      node: w,
      x: m,
      y: j,
      width: y,
      height: x
    };
  }, me);
  return !l || l.hidden || !Zl(l) ? null : i.jsx(c, { x: d, y: p, width: f, height: h, style: l.style, selected: !!l.selected, className: o(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const u0 = Ne(l0);
var d0 = Ne(c0);
const f0 = 200, p0 = 150, h0 = (e) => !e.hidden, g0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? ql(co(e.nodeLookup, { filter: h0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, y0 = "react-flow__minimap-desc";
function nd({
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
  maskStrokeWidth: p,
  position: f = "bottom-right",
  onClick: h,
  onNodeClick: g,
  pannable: b = !1,
  zoomable: w = !1,
  ariaLabel: m,
  inversePan: j,
  zoomStep: y = 1,
  offsetScale: x = 5
}) {
  const N = xe(), v = re(null), { boundingRect: S, viewBB: k, rfId: M, panZoom: R, translateExtent: T, flowWidth: A, flowHeight: z, ariaLabelConfig: E } = pe(g0, me), D = e?.width ?? f0, I = e?.height ?? p0, $ = S.width / D, L = S.height / I, P = Math.max($, L), K = P * D, F = P * I, B = x * P, q = S.x - (K - S.width) / 2 - B, G = S.y - (F - S.height) / 2 - B, X = K + B * 2, le = F + B * 2, Z = `${y0}-${M}`, O = re(0), Y = re();
  O.current = P, Q(() => {
    if (v.current && R)
      return Y.current = _x({
        domNode: v.current,
        panZoom: R,
        getTransform: () => N.getState().transform,
        getViewScale: () => O.current
      }), () => {
        Y.current?.destroy();
      };
  }, [R]), Q(() => {
    Y.current?.update({
      translateExtent: T,
      width: A,
      height: z,
      inversePan: j,
      pannable: b,
      zoomStep: y,
      zoomable: w
    });
  }, [b, w, j, y, T, A, z]);
  const ae = h ? (oe) => {
    const [de, H] = Y.current?.pointer(oe) || [0, 0];
    h(oe, { x: de, y: H });
  } : void 0, ce = g ? se((oe, de) => {
    const H = N.getState().nodeLookup.get(de).internals.userNode;
    g(oe, H);
  }, []) : void 0, te = m ?? E["minimap.ariaLabel"];
  return i.jsx(Mr, { position: f, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof p == "number" ? p * P : void 0,
    "--xy-minimap-node-background-color-props": typeof o == "string" ? o : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Se(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: i.jsxs("svg", { width: D, height: I, viewBox: `${q} ${G} ${X} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": Z, ref: v, onClick: ae, children: [te && i.jsx("title", { id: Z, children: te }), i.jsx(d0, { onClick: ce, nodeColor: o, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), i.jsx("path", { className: "react-flow__minimap-mask", d: `M${q - B},${G - B}h${X + B * 2}v${le + B * 2}h${-X - B * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
nd.displayName = "MiniMap";
const od = Ne(nd), m0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, x0 = {
  [un.Line]: "right",
  [un.Handle]: "bottom-right"
};
function w0({ nodeId: e, position: t, variant: n = un.Handle, className: o, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: p = !1, resizeDirection: f, autoScale: h = !0, shouldResize: g, onResizeStart: b, onResize: w, onResizeEnd: m }) {
  const j = Du(), y = typeof e == "string" ? e : j, x = xe(), N = re(null), v = n === un.Handle, S = pe(se(m0(v && h), [v, h]), me), k = re(null), M = t ?? x0[n];
  Q(() => {
    if (!(!N.current || !y))
      return k.current || (k.current = Fx({
        domNode: N.current,
        nodeId: y,
        getStoreItems: () => {
          const { nodeLookup: T, transform: A, snapGrid: z, snapToGrid: E, nodeOrigin: D, domNode: I } = x.getState();
          return {
            nodeLookup: T,
            transform: A,
            snapGrid: z,
            snapToGrid: E,
            nodeOrigin: D,
            paneDomNode: I
          };
        },
        onChange: (T, A) => {
          const { triggerNodeChanges: z, nodeLookup: E, parentLookup: D, nodeOrigin: I } = x.getState(), $ = [], L = { x: T.x, y: T.y }, P = E.get(y);
          if (P && P.expandParent && P.parentId) {
            const K = P.origin ?? I, F = T.width ?? P.measured.width ?? 0, B = T.height ?? P.measured.height ?? 0, q = {
              id: P.id,
              parentId: P.parentId,
              rect: {
                width: F,
                height: B,
                ...Gl({
                  x: T.x ?? P.position.x,
                  y: T.y ?? P.position.y
                }, { width: F, height: B }, P.parentId, E, K)
              }
            }, G = ys([q], E, D, I);
            $.push(...G), L.x = T.x ? Math.max(K[0] * F, T.x) : void 0, L.y = T.y ? Math.max(K[1] * B, T.y) : void 0;
          }
          if (L.x !== void 0 && L.y !== void 0) {
            const K = {
              id: y,
              type: "position",
              position: { ...L }
            };
            $.push(K);
          }
          if (T.width !== void 0 && T.height !== void 0) {
            const F = {
              id: y,
              type: "dimensions",
              resizing: !0,
              setAttributes: f ? f === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: T.width,
                height: T.height
              }
            };
            $.push(F);
          }
          for (const K of A) {
            const F = {
              ...K,
              type: "position"
            };
            $.push(F);
          }
          z($);
        },
        onEnd: ({ width: T, height: A }) => {
          const z = {
            id: y,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: T,
              height: A
            }
          };
          x.getState().triggerNodeChanges([z]);
        }
      })), k.current.update({
        controlPosition: M,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: p,
        resizeDirection: f,
        onResizeStart: b,
        onResize: w,
        onResizeEnd: m,
        shouldResize: g
      }), () => {
        k.current?.destroy();
      };
  }, [
    M,
    c,
    u,
    l,
    d,
    p,
    b,
    w,
    m,
    g
  ]);
  const R = M.split("-");
  return i.jsx("div", { className: Se(["react-flow__resize-control", "nodrag", ...R, n, o]), ref: N, style: {
    ...r,
    scale: S,
    ...a && { [v ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Ne(w0);
const v0 = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function rd(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ws(e) {
  return rd(e.name);
}
function b0(e, t) {
  const n = ws(t), o = e[n];
  return t.isWrapped === !1 ? o ?? t.defaultValue ?? "" : sd(o, t);
}
function id(e, t) {
  return sd(e[ws(t)], t);
}
function N0(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function j0(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function sc(e, t, n) {
  return {
    ...e,
    [ws(t)]: n
  };
}
function S0(e, t) {
  return t.isWrapped === !1 ? b0(e, t) : id(e, t).expression.value;
}
function sd(e, t) {
  return T0(e) ? {
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
const E0 = /* @__PURE__ */ new Set([
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
function C0(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const o = t.indexOf("`");
  if (o < 0) return null;
  const r = t.slice(0, o), s = (r.split(".").pop() ?? r).toLowerCase();
  return E0.has(s) ? { elementTypeName: I0(t.slice(o)) } : null;
}
function I0(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function k0(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function A0(e) {
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
function _0(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function D0(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function vi(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const o = [...e], [r] = o.splice(t, 1);
  return o.splice(n, 0, r), o;
}
function T0(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function ad(e) {
  return vs(e?.trim() ?? "") || e;
}
function vs(e) {
  if (!e) return "";
  const t = $0(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${vs(n[1])}${n[2]}`;
  const o = t.indexOf("`");
  if (o >= 0) {
    const r = ac(t.slice(0, o)), s = M0(t.slice(o));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return ac(t);
}
function $0(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const o = e[n];
    if (o === "[") t++;
    else if (o === "]") t--;
    else if (o === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function ac(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function M0(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = cc(e, t);
  return n == null ? [] : P0(n).map((o) => {
    const r = o.trim(), s = r.startsWith("[") ? cc(r, 0) ?? r : r;
    return vs(s);
  }).filter(Boolean);
}
function cc(e, t) {
  let n = 0;
  for (let o = t; o < e.length; o++)
    if (e[o] === "[") n++;
    else if (e[o] === "]" && --n === 0) return e.slice(t + 1, o);
  return null;
}
function P0(e) {
  const t = [];
  let n = 0, o = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(o, r)), o = r + 1);
  }
  return t.push(e.slice(o)), t.map((r) => r.trim()).filter(Boolean);
}
const cd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), R0 = "Variable";
function z0({
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
  const l = t.inputs.filter((f) => f.isBrowsable !== !1).sort((f, h) => (f.order ?? 0) - (h.order ?? 0) || f.name.localeCompare(h.name));
  if (l.length === 0)
    return /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose editable properties." });
  const d = K0(l), p = r.length > 0 ? r : v0;
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((f) => /* @__PURE__ */ i.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ i.jsx("h4", { children: f.category }) : null,
      f.inputs.map((h) => /* @__PURE__ */ i.jsx(
        L0,
        {
          activity: e,
          input: h,
          editors: n,
          expressionEditors: o,
          expressionDescriptors: p,
          visibleVariables: a,
          scopeStatus: c,
          onChange: u
        },
        h.name
      ))
    ] }, f.category))
  ] });
}
function L0({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: o,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Wi(n, t, l), p = d?.component, f = t.isWrapped !== !1 ? id(e, t) : null, h = f?.expression.type ?? "Literal", g = S0(e, t), b = h.toLowerCase(), m = f && (b === "literal" || b === "object") && !k0(t) ? C0(t.typeName) : null, j = m ? Wi(n, t, { ...l, scope: "collection" }) : void 0, y = f ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, x = y ? ud(o, y) : null, N = x?.surfaces.inline, v = x && y ? dd(x, y, g) : [], S = m != null, k = !!(f && !S && U0(t, d?.id)), M = !!(f && !S && X0(t, d?.id)), [R, T] = U(!1), A = (I) => {
    const $ = f ? N0(f, I) : I;
    c(sc(e, t, $));
  }, z = (I) => {
    f && c(sc(e, t, j0(f, I)));
  }, E = m ? j ? Vi(j.component, t, g, u, { ...l, scope: "collection" }, A) : /* @__PURE__ */ i.jsx(
    O0,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: g,
      editors: n,
      context: l,
      disabled: u,
      onChange: A
    }
  ) : null, D = h === R0 && f ? /* @__PURE__ */ i.jsx(
    F0,
    {
      value: g,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: A
    }
  ) : E ?? (N && y ? /* @__PURE__ */ i.jsx(
    N,
    {
      descriptor: t,
      syntax: h,
      value: g,
      disabled: u,
      context: y,
      onChange: A
    }
  ) : Vi(p, t, g, u, l, A));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ i.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ i.jsx("span", { children: ad(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ i.jsx("p", { children: t.description }) : null,
    f && !k ? /* @__PURE__ */ i.jsx(
      Oi,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: z
      }
    ) : null,
    k ? /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-expression-editor", children: [
        D,
        Fi(v)
      ] }),
      /* @__PURE__ */ i.jsx(
        Oi,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: z
        }
      ),
      M ? /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => T(!0),
          children: /* @__PURE__ */ i.jsx(er, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      D,
      Fi(v)
    ] }),
    M && !k ? /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => T(!0),
        children: [
          /* @__PURE__ */ i.jsx(er, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    R ? /* @__PURE__ */ i.jsx(
      H0,
      {
        input: t,
        value: g,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: o,
        disabled: u,
        onChange: A,
        onSyntaxChange: z,
        onClose: () => T(!1)
      }
    ) : null
  ] });
}
function V0(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function O0({
  input: e,
  elementTypeName: t,
  value: n,
  editors: o,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = A0(n), u = D0(e, t), l = { ...r, scope: "element" }, d = Wi(o, u, l)?.component, p = e.displayName || e.name, f = (N, v) => a(c.map((S, k) => k === N ? v : S)), [h, g] = U(null), [b, w] = U(null), m = () => {
    g(null), w(null);
  }, j = (N) => (v) => {
    g(N), v.dataTransfer.effectAllowed = "move", v.dataTransfer.setData("text/plain", String(N));
  }, y = (N) => (v) => {
    h !== null && (v.preventDefault(), v.dataTransfer.dropEffect = "move", b !== N && w(N));
  }, x = (N) => (v) => {
    v.preventDefault(), h !== null && h !== N && a(vi(c, h, N)), m();
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-collection-items", children: c.map((N, v) => /* @__PURE__ */ i.jsxs(
      "li",
      {
        className: V0(v, h, b),
        onDragOver: y(v),
        onDrop: x(v),
        children: [
          /* @__PURE__ */ i.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${p} item ${v + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: j(v),
              onDragEnd: m,
              children: /* @__PURE__ */ i.jsx(Vc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ i.jsx("div", { className: "wf-collection-item-editor", children: Vi(d, u, N, s, l, (S) => f(v, S)) }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} up`,
                disabled: s || v === 0,
                onClick: () => a(vi(c, v, v - 1)),
                children: /* @__PURE__ */ i.jsx(cf, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${p} item ${v + 1} down`,
                disabled: s || v === c.length - 1,
                onClick: () => a(vi(c, v, v + 1)),
                children: /* @__PURE__ */ i.jsx(Oc, { size: 13 })
              }
            ),
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${p} item ${v + 1}`,
                disabled: s,
                onClick: () => a(c.filter((S, k) => k !== v)),
                children: /* @__PURE__ */ i.jsx(Kn, { size: 13 })
              }
            )
          ] })
        ]
      },
      v
    )) }),
    /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-collection-add",
        disabled: s,
        onClick: () => a([...c, _0(t)]),
        children: [
          /* @__PURE__ */ i.jsx(tn, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function H0({
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
  const d = zc(), p = e.displayName || e.name, f = {
    activity: r,
    descriptor: e,
    expressionDescriptors: o,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = ud(s, f), g = h?.surfaces.expanded, b = h ? dd(h, f, t) : [], w = g ? null : B0(s, f);
  return Q(() => {
    const m = (j) => {
      j.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ i.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ i.jsx("h3", { id: d, children: p })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": `Close ${p} editor`, onClick: l, children: /* @__PURE__ */ i.jsx(Hc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ i.jsx(
          Oi,
          {
            label: `${p} expression syntax`,
            value: n,
            descriptors: o,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ i.jsx("span", { children: ad(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ i.jsx("p", { children: e.description }) : null,
      g ? /* @__PURE__ */ i.jsx(
        g,
        {
          descriptor: e,
          syntax: n,
          value: t,
          disabled: a,
          context: f,
          onChange: c
        }
      ) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
        w ? /* @__PURE__ */ i.jsx("p", { className: "wf-expression-editor-hint", children: w }) : null,
        /* @__PURE__ */ i.jsx(
          "textarea",
          {
            "aria-label": `${p} expanded value`,
            value: t == null ? "" : String(t),
            disabled: a,
            spellCheck: !1,
            onChange: (m) => c(m.target.value)
          }
        )
      ] }),
      Fi(b)
    ] }),
    /* @__PURE__ */ i.jsxs("footer", { children: [
      /* @__PURE__ */ i.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Vi(e, t, n, o, r, s) {
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
function Oi({
  label: e,
  value: t,
  descriptors: n,
  disabled: o,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = U(!1), u = zc(), l = n.find((p) => p.type === t), d = [
    "wf-syntax-picker-trigger",
    r === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ i.jsxs("div", { className: r === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (p) => {
    p.currentTarget.contains(p.relatedTarget) || c(!1);
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
        onClick: () => c((p) => !p),
        children: /* @__PURE__ */ i.jsx("span", { children: l?.displayName || l?.type || t })
      }
    ),
    a ? /* @__PURE__ */ i.jsx("div", { id: u, role: "listbox", className: "wf-syntax-picker-menu", "aria-label": e, children: n.map((p) => {
      const f = p.displayName || p.type, h = p.type === t;
      return /* @__PURE__ */ i.jsx(
        "button",
        {
          type: "button",
          role: "option",
          "aria-selected": h,
          className: h ? "selected" : "",
          onClick: () => {
            s(p.type), c(!1);
          },
          children: f
        },
        p.type
      );
    }) }) : null
  ] });
}
const Hi = "::";
function ld(e) {
  return !e || e === ir ? ir : e;
}
function lc(e, t) {
  return `${ld(t)}${Hi}${e}`;
}
function W0(e) {
  const t = e.indexOf(Hi);
  if (t < 0) return null;
  const n = e.slice(t + Hi.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function F0({ value: e, visibleVariables: t, scopeStatus: n, disabled: o, onChange: r }) {
  const s = al(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? lc(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === ld(c.declaringScopeId)
  );
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ i.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: o,
        onChange: (d) => {
          const p = W0(d.target.value);
          p && r(Sp(p.referenceKey, p.scopeId));
        },
        children: [
          /* @__PURE__ */ i.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ i.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const p = lc(d.referenceKey, d.scopeId);
            return /* @__PURE__ */ i.jsxs("option", { value: p, children: [
              d.name,
              d.isWorkflowScope ? " · workflow" : " · container"
            ] }, p);
          })
        ]
      }
    ),
    n === "unavailable" ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "Variable scope information is unavailable (pending backend support). Existing references are preserved." }) : n === "ready" && t.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-variable-picker-note", children: "No variables are visible here. Declare one on the workflow or a container scope." }) : null
  ] });
}
function Wi(e, t, n) {
  return [...e].sort((o, r) => (o.order ?? 500) - (r.order ?? 500)).find((o) => o.supports(t, n));
}
function ud(e, t) {
  return [...e].sort((n, o) => (n.order ?? 500) - (o.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function dd(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function B0(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const o = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${o} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Fi(e) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info";
    return /* @__PURE__ */ i.jsxs("p", { className: `wf-expression-editor-diagnostic ${o}`, children: [
      t.code ? /* @__PURE__ */ i.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function K0(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "General";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return [...t.entries()].map(([n, o]) => ({ category: n, inputs: o }));
}
function U0(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !cd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function X0(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !cd.has(t) && n !== "multiline") return !1;
  const o = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(o) || n === "singleline" || n === "multiline";
}
function q0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: oo(e.state),
    layout: e.layout
  };
}
function Y0(e) {
  return JSON.stringify(
    {
      state: oo(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Z0(e, t) {
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
      state: jr(o.state),
      layout: o.layout ?? t.layout
    }
  };
}
function G0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", o = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(o), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
const J0 = 320, Q0 = 140;
function eb(e, t, n) {
  return n === "sequence" ? tb(e) : nb(e, t);
}
function tb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, o) => {
    t.set(n.id, { x: o * 280, y: 0 });
  }), t;
}
function nb(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const o = new Set(e.map((d) => d.id)), r = t.filter((d) => o.has(d.source) && o.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of r)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let p = !1;
    for (const f of r) {
      const h = (a.get(f.source) ?? 0) + 1;
      h > (a.get(f.target) ?? 0) && h <= e.length && (a.set(f.target, h), p = !0);
    }
    if (!p) break;
  }
  const c = Math.max(0, ...e.filter((d) => s.has(d.id)).map((d) => a.get(d.id) ?? 0)), u = s.size > 0 ? c + 1 : 0, l = /* @__PURE__ */ new Map();
  for (const d of e) {
    const p = s.has(d.id) ? a.get(d.id) ?? 0 : u, f = l.get(p);
    f ? f.push(d.id) : l.set(p, [d.id]);
  }
  for (const [d, p] of l)
    p.forEach((f, h) => {
      n.set(f, { x: d * J0, y: h * Q0 });
    });
  return n;
}
function Je(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function fd(e, t) {
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
function wr(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function bs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ i.jsx(Wc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ i.jsx(qi, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ i.jsx(uf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ i.jsx(nn, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ i.jsx(lf, { size: 15 });
    default:
      return /* @__PURE__ */ i.jsx(br, { size: 15 });
  }
}
function uc({
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
function ob({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: o = "studio",
  minHeight: r = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((f) => !f.uri || f.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, p = ue(
    () => d ? of(d) : null,
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
        p ? /* @__PURE__ */ i.jsx(rf, { fallback: /* @__PURE__ */ i.jsx(
          uc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ i.jsx(
          p,
          {
            document: e,
            readOnly: n,
            theme: o,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ i.jsx(
          uc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ i.jsx(rb, { diagnostics: u })
      ]
    }
  );
}
function rb({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ i.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const o = t.severity ?? "info", r = ib(t);
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
function ib(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const sb = { language: "json", displayName: "JSON" };
function ab({ draft: e, onApply: t }) {
  const n = ue(() => Y0(e), [e]), [o, r] = U(n), [s, a] = U(n), [c, u] = U(null);
  Q(() => {
    r(n), a(n), u(null);
  }, [n]);
  const l = o !== s, d = c ? [{ severity: "error", message: c }] : [], p = () => u(t(o));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ i.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ i.jsx("button", { type: "button", disabled: !l, onClick: () => {
          r(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: !l, onClick: p, children: [
          /* @__PURE__ */ i.jsx(fn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ i.jsx(
      ob,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: o },
        languageAdapter: sb,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (f) => {
          r(f.value), c && u(null);
        }
      }
    ) })
  ] });
}
const cb = ["Single", "Array", "List", "HashSet"];
function pd(e) {
  const [t, n] = U(null), [o, r] = U(null);
  Q(() => {
    let u = !1;
    return Gp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Qp(e).then(
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
      const l = Ws(u);
      return {
        value: l,
        label: Xc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ue(
    () => o && o.length > 0 ? o.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Lf(u.displayName, u.typeName)
    })) : null,
    [o]
  ), c = ue(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Ws(l), p = l.defaultEditor?.trim();
      d && u.set(d, p && p.length > 0 ? p : "text");
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
      const o = kf(n.namePrefix, e.map((r) => On(r, n.nameKeys)));
      t([...e, n.create(o)]);
    },
    update: (o, r) => t(e.map((s, a) => a === o ? n.patch(s, r) : s)),
    remove: (o) => t(e.filter((r, s) => s !== o))
  };
}
function dc({ value: e, options: t, placeholder: n, allowEmpty: o, ariaLabel: r, onChange: s }) {
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
const db = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function fb({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("select", { "aria-label": t, value: e, onChange: (o) => n(o.target.value), children: cb.map((o) => /* @__PURE__ */ i.jsx("option", { value: o, children: db[o] }, o)) });
}
function pb(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function hb({ value: e, editor: t, ariaLabel: n, onChange: o }) {
  const r = pb(t, e);
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
function gb({ title: e, addLabel: t, emptyLabel: n, headers: o, isEmpty: r, onAdd: s, children: a }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ i.jsx("h3", { children: e }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ i.jsx(tn, { size: 14 }),
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
function yb({ label: e, onRemove: t }) {
  return /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ i.jsx(Kn, { size: 14 }) }) });
}
function mb({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ i.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (o) => n(o.target.checked) });
}
function Ns({
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
  columns: p,
  warnings: f,
  onChange: h
}) {
  const { add: g, update: b, remove: w } = ub(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (y) => l(y, lb(t)),
    patch: d
  }), m = ["Name", "Type", "Collection", ...p.default ? ["Default"] : [], ...p.storage ? ["Storage"] : [], ...p.required ? ["Required"] : []], j = r.toLowerCase();
  return /* @__PURE__ */ i.jsx(
    gb,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: g,
      children: e.map((y, x) => {
        const N = On(y, s), v = Uc(y), S = On(y, Yc), k = S ? f?.get(S) : void 0, M = v.collectionKind === "Single" ? o(v.alias) : "text";
        return /* @__PURE__ */ i.jsxs("tr", { children: [
          /* @__PURE__ */ i.jsxs("td", { children: [
            /* @__PURE__ */ i.jsx("input", { type: "text", "aria-label": `${r} name`, value: N, onChange: (R) => b(x, { name: R.target.value }) }),
            k ? /* @__PURE__ */ i.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            dc,
            {
              ariaLabel: `${r} type`,
              value: v.alias,
              options: t,
              placeholder: "Type",
              onChange: (R) => b(x, { type: { alias: R, collectionKind: v.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            fb,
            {
              ariaLabel: `${r} collection kind`,
              value: v.collectionKind,
              onChange: (R) => b(x, { type: { alias: v.alias, collectionKind: R } })
            }
          ) }),
          p.default ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            hb,
            {
              ariaLabel: `${r} default value`,
              value: Tf(y.default),
              editor: M,
              onChange: (R) => b(x, { default: Df(R) })
            }
          ) }) : null,
          p.storage ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            dc,
            {
              ariaLabel: `${r} storage driver`,
              value: On(y, Of),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (R) => b(x, { storageDriverType: R || null })
            }
          ) }) : null,
          p.required ? /* @__PURE__ */ i.jsx("td", { children: /* @__PURE__ */ i.jsx(
            mb,
            {
              ariaLabel: `${r} required`,
              checked: y.isRequired === !0,
              onChange: (R) => b(x, { isRequired: R })
            }
          ) }) : null,
          /* @__PURE__ */ i.jsx(yb, { label: `Remove ${j} ${N || x + 1}`, onRemove: () => w(x) })
        ] }, x);
      })
    }
  );
}
function hd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ i.jsx(
    Ns,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
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
function xb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    Ns,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Input",
      nameKeys: qc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => $f({ name: s, alias: a }),
      patch: (s, a) => Mf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function wb({ items: e, typeOptions: t, storageOptions: n, editorForAlias: o, onChange: r }) {
  return /* @__PURE__ */ i.jsx(
    Ns,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: o,
      namePrefix: "Output",
      nameKeys: qc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Pf({ name: s, alias: a }),
      patch: (s, a) => Rf(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function Zo(e) {
  return (e ?? []).filter(nr);
}
function vb({ context: e, variables: t, title: n, addLabel: o, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = pd(e);
  return /* @__PURE__ */ i.jsx(
    hd,
    {
      items: Zo(t),
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
function bb({ definition: e, definitionId: t, onMetaChange: n }) {
  const o = !!n, [r, s] = U(e?.name ?? ""), [a, c] = U(e?.description ?? "");
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
function Nb({ details: e, draft: t, context: n, onStateChange: o, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = pd(n), u = Zo(t.state.variables), l = Zo(t.state.inputs), d = Zo(t.state.outputs), p = e?.versions ?? [];
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ i.jsx(
      bb,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ i.jsx(
      hd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (f) => o((h) => ({ ...h, variables: f }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      xb,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (f) => o((h) => ({ ...h, inputs: f }))
      }
    ),
    /* @__PURE__ */ i.jsx(
      wb,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (f) => o((h) => ({ ...h, outputs: f }))
      }
    ),
    /* @__PURE__ */ i.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ i.jsx("h3", { children: "Versions" }),
      p.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ i.jsx("ul", { className: "wf-properties-versions", children: p.map((f) => /* @__PURE__ */ i.jsxs("li", { children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          f.version
        ] }),
        /* @__PURE__ */ i.jsx("time", { children: Je(f.createdAt) })
      ] }, f.id)) })
    ] })
  ] });
}
const fc = "elsa-studio:apply-workflow-graph-operation-batch", pc = "elsa-studio:undo-workflow-graph-operation-batch", jb = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function Sb(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const o = Tb(e), r = yd(o.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = Db(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const p = Te(d.activityId) ?? u.temporaryReferences?.[0], f = _b(p ?? Te(d.displayName) ?? Te(d.activityType) ?? "weaver-activity", r), h = Eb(u, f, n);
      a.set(f, h), c.push(f), p && s.set(p, f), o.state.rootActivity && Cb(o.state.rootActivity, h);
      const g = Nt(d.position) ? Bi(d.position, { x: 280, y: 160 }) : null;
      g && (o.layout = hc(o.layout, f, g));
      continue;
    }
    if (l === "set-root") {
      const p = bi(o, d.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown root activity.");
      o.state.rootActivity = p;
      continue;
    }
    if (l === "set-designer-position") {
      const p = Lt(d.activityId, s);
      if (!p || !js(o.state.rootActivity, p)) throw new Error("Weaver batch referenced an unknown activity position.");
      o.layout = hc(o.layout, p, Bi(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const p = bi(o, d.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity property target.");
      Ab(p, Te(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const p = bi(o, d.activityId, s, a);
      if (!p) throw new Error("Weaver batch referenced an unknown activity update target.");
      const f = Nt(d.patch) ? d.patch : d;
      Object.assign(p, f);
      continue;
    }
    if (l === "remove-activity") {
      const p = Lt(d.activityId, s);
      if (!p) throw new Error("Weaver batch referenced an unknown activity remove target.");
      o.state.rootActivity = gd(o.state.rootActivity, p), o.layout = o.layout.filter((f) => f.nodeId !== p);
      continue;
    }
    if (l === "connect-activities") {
      Ib(o, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      kb(o, d, s);
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
function Eb(e, t, n) {
  const o = e.parameters ?? {}, r = Te(o.activityVersionId) ?? Te(o.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Te(o.displayName));
  return s ? Ei(s, t) : {
    nodeId: t,
    activityVersionId: s?.activityVersionId ?? r,
    inputs: [],
    outputs: [],
    ...Te(o.displayName) ? { displayName: Te(o.displayName) } : {},
    designer: { position: Bi(o.position, { x: 280, y: 160 }) }
  };
}
function Cb(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ss(e);
  n && !n.some((o) => o.nodeId === t.nodeId) && n.push(t);
}
function Ib(e, t, n) {
  const o = e.state.rootActivity;
  if (!o?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = o.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Te(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !Nt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Te(t.outcome) ?? Te(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function kb(e, t, n) {
  const o = e.state.rootActivity, r = o?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Te(t.connectionId), a = Lt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Lt(t.targetActivityId ?? t.targetId ?? t.to, n);
  o.structure.payload.connections = r.filter((u) => {
    if (!Nt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = Nt(u.source) ? u.source.nodeId : void 0, d = Nt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function Ab(e, t, n) {
  const o = Nt(n);
  e[rd(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: o ? "Object" : "Literal", value: n }
  };
}
function bi(e, t, n, o) {
  const r = Lt(t, n);
  return r ? js(e.state.rootActivity, r) ?? o.get(r) ?? null : null;
}
function Lt(e, t) {
  const n = Te(e);
  return n ? t.get(n) ?? n : null;
}
function js(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of md(e)) {
    const o = js(n, t);
    if (o) return o;
  }
  return null;
}
function gd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ss(e);
  if (n) {
    const o = n.map((r) => gd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...o);
  }
  return e;
}
function yd(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of md(e)) yd(n, t);
  return t;
}
function md(e) {
  return Ss(e) ?? [];
}
function Ss(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function hc(e, t, n) {
  return [
    ...e.filter((o) => o.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Bi(e, t) {
  const n = Nt(e) ? e : {}, o = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function _b(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let o = n, r = 2;
  for (; t.has(o); )
    o = `${n}-${r}`, r += 1;
  return t.add(o), o;
}
function Db(e) {
  return typeof e == "number" ? jb[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Te(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function Tb(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Nt(e) {
  return typeof e == "object" && e !== null;
}
const gc = "application/x-elsa-activity-version-id", $b = 6, Mb = 1200, Pb = 250, Rb = [10, 25, 50], zb = 10, yc = "elsa-studio-workflow-palette-width", mc = "elsa-studio-workflow-inspector-width", xc = "elsa-studio-workflow-palette-collapsed", wc = "elsa-studio-workflow-inspector-collapsed", xd = "elsa-studio-workflow-side-panel-maximized", Mn = 180, Pn = 460, Lb = 260, Rn = 260, zn = 560, Vb = 320, vc = 42, Oo = 16, wd = it.createContext(null), vd = it.createContext(null);
function Ob(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function bd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Vt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Ot(e, t, n) {
  const o = t.createPrompt(n);
  return o ? (e.dispatchPrompt(o), !0) : !1;
}
function Hb(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const o = e.match(/\{[\s\S]*?\}/);
  o && t.push(o[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = bc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return bc(r, s);
}
function bc(e, t) {
  const n = typeof e == "string" ? Nc(e) : void 0, o = typeof t == "string" ? Nc(t) : void 0;
  return n || o ? { name: n || void 0, description: o || void 0 } : null;
}
function Nc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Wb(e, t) {
  return e.rootActivityVersionId ?? Fb(t, e.rootKind)?.activityVersionId ?? null;
}
function Fb(e, t) {
  return e.find((n) => Bb(n) === t);
}
function Bb(e) {
  return e ? Kb(e) ? "flowchart" : Ub(e) ? "sequence" : null : null;
}
function Ki(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const o = n.category?.trim() || "Uncategorized";
    t.set(o, [...t.get(o) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [o]) => n.localeCompare(o)).map(([n, o]) => ({
    category: n,
    activities: o.sort((r, s) => Ee(r).localeCompare(Ee(s)))
  }));
}
function Kb(e) {
  return Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Ub(e) {
  return Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Xb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function Nd(e) {
  return Jb(e.rootActivityType) || e.rootActivityType;
}
function qb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Yb(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function Zb(e, t) {
  return jc(t) - jc(e);
}
function jc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function jd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Sd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function Gb(e) {
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
function Jb(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Qb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Ho(t, n.typeName, n), Ho(t, n.name, n), Ho(t, n.displayName, n);
    const o = n.typeName.split(".").filter(Boolean).at(-1);
    Ho(t, o, n);
  }
  return t;
}
function eN(e, t, n) {
  const o = t.get(e.activityVersionId);
  return n.get(Ln(o?.activityTypeKey)) ?? n.get(Ln(wr(o?.activityTypeKey))) ?? n.get(Ln(o?.displayName)) ?? n.get(Ln(e.activityVersionId)) ?? null;
}
function Ho(e, t, n) {
  const o = Ln(t);
  o && !e.has(o) && e.set(o, n);
}
function Ln(e) {
  return e?.trim().toLowerCase() ?? "";
}
function Sc(e, t, n, o) {
  const r = Rr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Go(a, n, o) : t;
}
function Ec(e, t) {
  const n = Rr();
  if (!n) return t;
  const o = n.getItem(e);
  return o === "true" ? !0 : o === "false" ? !1 : t;
}
function tN() {
  const e = Rr();
  if (!e) return null;
  const t = e.getItem(xd);
  return t === "palette" || t === "inspector" ? t : null;
}
function Rr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Dn(e, t) {
  const n = Rr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Go(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function Ed(e) {
  switch (nN(e)) {
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
function nN(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function oN(e, t) {
  const n = e.state.rootActivity;
  if (!n) return /* @__PURE__ */ new Set();
  const o = t.find((s) => s.activityVersionId === n.activityVersionId);
  if (Gi(n, o) === "unsupported") return /* @__PURE__ */ new Set([n.nodeId]);
  const r = Hn(n, []);
  return new Set(r?.slot.activities.map((s) => s.nodeId) ?? [n.nodeId]);
}
function Cc(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function Ic(e) {
  return `${Ee(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function kc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function rN(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Cd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function iN(e) {
  const t = Cd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function sN(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function He(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function aN(e) {
  return Ad(He(e));
}
function Id(e, t, n = []) {
  if (!e) return n;
  const o = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: o?.activityTypeKey ?? e.activityVersionId,
    displayName: o ? Ee(o) : void 0
  });
  for (const r of Me(e))
    for (const s of r.activities) Id(s, t, n);
  return n;
}
function kd(e, t = []) {
  if (!e) return t;
  for (const n of rl(e))
    t.push({ source: n.source, target: n.target, sourcePort: n.sourceHandle ?? void 0, targetPort: n.targetHandle ?? void 0 });
  for (const n of Me(e))
    for (const o of n.activities) kd(o, t);
  return t;
}
function Vn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function cN(e) {
  return `${e.id}-${Ad(JSON.stringify(e.state))}`;
}
function Ad(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Es(e) {
  return e.status.toLowerCase() === "rejected";
}
function lN(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function uN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return dN(e, n) ? `Run ${t} was not found.` : n;
}
function dN(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const o = JSON.parse(t);
    return [o.error, o.title, o.detail].some((r) => typeof r == "string" && /not found/i.test(r));
  } catch {
    return /not found/i.test(t);
  }
}
function fo({ status: e, subStatus: t }) {
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const _d = { workflowActivity: fN }, Dd = { workflow: hN };
function fN({ data: e, selected: t }) {
  const n = e, o = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = pN(n), u = it.useContext(vd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ i.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ i.jsx(dn, { type: "target", position: ne.Left }) : null,
        u ? /* @__PURE__ */ i.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${sr(u.state)}`, children: /* @__PURE__ */ i.jsx(Qo, { size: 13 }) }) : null,
        /* @__PURE__ */ i.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: bs(n.icon) }),
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
          o.status ? /* @__PURE__ */ i.jsx(fo, { status: o.status, subStatus: o.subStatus }) : null,
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
          const p = `${(d + 1) / (s.length + 1) * 100}%`;
          return /* @__PURE__ */ i.jsxs(it.Fragment, { children: [
            /* @__PURE__ */ i.jsx("span", { className: "wf-node-port-label", style: { top: p }, children: l.displayName }),
            /* @__PURE__ */ i.jsx(dn, { type: "source", position: ne.Right, id: l.name, style: { top: p } })
          ] }, l.name);
        })
      ]
    }
  );
}
function pN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function hN(e) {
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
    labelStyle: p
  } = e, f = it.useContext(wd), [h, g] = U(!1), [b, w, m] = mr({ sourceX: n, sourceY: o, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), j = f?.highlightedEdgeId === t;
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsx(
      uo,
      {
        id: t,
        path: b,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: j ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: w,
        labelY: m,
        labelStyle: p,
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1)
      }
    ),
    f ? /* @__PURE__ */ i.jsx(qv, { children: /* @__PURE__ */ i.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", j ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${w}px, ${m}px)` },
        onMouseEnter: () => g(!0),
        onMouseLeave: () => g(!1),
        children: [
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (y) => f.requestInsertActivity(t, y.clientX, y.clientY), children: /* @__PURE__ */ i.jsx(tn, { size: 12 }) }),
          /* @__PURE__ */ i.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => f.deleteEdge(t), children: /* @__PURE__ */ i.jsx(Kn, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function gN({ clientX: e, clientY: t, activities: n, onPick: o, onClose: r }) {
  const [s, a] = U(""), [c, u] = U(0), l = re(null), d = re(null), p = ue(() => {
    const j = s.trim().toLowerCase(), y = n.filter(Xb);
    return j ? y.filter((x) => Ee(x).toLowerCase().includes(j) || x.activityTypeKey.toLowerCase().includes(j) || (x.category ?? "").toLowerCase().includes(j) || (x.description ?? "").toLowerCase().includes(j)) : y;
  }, [n, s]), f = ue(() => Ki(p), [p]), h = ue(() => f.flatMap((j) => j.activities), [f]);
  Q(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), Q(() => {
    const j = (x) => {
      l.current?.contains(x.target) || r();
    }, y = (x) => {
      x.key === "Escape" && r();
    };
    return document.addEventListener("mousedown", j, !0), document.addEventListener("keydown", y), () => {
      document.removeEventListener("mousedown", j, !0), document.removeEventListener("keydown", y);
    };
  }, [r]);
  const g = (j) => {
    if (j.key === "ArrowDown")
      j.preventDefault(), u((y) => Math.min(y + 1, h.length - 1));
    else if (j.key === "ArrowUp")
      j.preventDefault(), u((y) => Math.max(y - 1, 0));
    else if (j.key === "Enter") {
      j.preventDefault();
      const y = h[c];
      y && o(y);
    }
  }, b = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), w = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ i.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: b, top: w }, onMouseDown: (j) => j.stopPropagation(), onClick: (j) => j.stopPropagation(), children: [
    /* @__PURE__ */ i.jsx(
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
    /* @__PURE__ */ i.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: f.length === 0 ? /* @__PURE__ */ i.jsx("p", { children: "No matching activities." }) : f.map((j) => /* @__PURE__ */ i.jsxs("section", { children: [
      /* @__PURE__ */ i.jsx("h4", { children: j.category }),
      j.activities.map((y) => {
        m += 1;
        const x = m, N = x === c;
        return /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": N,
            className: N ? "active" : "",
            onMouseEnter: () => u(x),
            onClick: () => o(y),
            children: [
              /* @__PURE__ */ i.jsx("strong", { children: Ee(y) }),
              /* @__PURE__ */ i.jsx("small", { children: y.category || y.activityTypeKey })
            ]
          },
          y.activityVersionId
        );
      })
    ] }, j.category)) })
  ] });
}
function Jo({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: o
}) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, children: t.map((r) => /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": r.id === n,
      className: r.id === n ? "active" : "",
      title: r.title,
      onClick: () => o(r.id),
      children: [
        r.icon ? /* @__PURE__ */ i.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: r.icon }) : null,
        /* @__PURE__ */ i.jsx("span", { children: r.title })
      ]
    },
    r.id
  )) });
}
function Ac(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const yN = "Expressions/UnresolvedVariable";
function mN(e) {
  return String(e.type ?? e.code ?? "");
}
function xN(e) {
  return mN(e) === yN;
}
function wN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, o, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: o === "inputs" || o === "outputs" || o === "variables" ? o : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function vN(e) {
  return (e ?? []).filter(xN).map((t) => ({
    error: t,
    path: wN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function bN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ i.jsx(fn, { size: 14 }),
      " No validation errors"
    ] });
  const o = vN(n), r = new Map(o.map((s) => [s.error, s]));
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ i.jsx(jt, { size: 14 }),
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
          /* @__PURE__ */ i.jsx(df, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function NN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Es(e);
  return /* @__PURE__ */ i.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ i.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ i.jsx(jt, { size: 16 }) : /* @__PURE__ */ i.jsx(fn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function jN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Es(e), o = e.workflowExecutionId;
  return /* @__PURE__ */ i.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ i.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ i.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ i.jsx(fo, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ i.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ i.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ i.jsx(jt, { size: 14 }),
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
        /* @__PURE__ */ i.jsx("dd", { children: _c(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ i.jsx("dd", { children: _c(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ i.jsx("dd", { title: e.expiresAt ? Je(e.expiresAt) : "None", children: e.expiresAt ? Je(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function _c(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function Cs({ rows: e = 5 }) {
  return /* @__PURE__ */ i.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ i.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function Is({ icon: e, title: t, description: n, action: o }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ i.jsx(br, { size: 22 }) }),
    /* @__PURE__ */ i.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ i.jsx("p", { children: n }) : null,
    o ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty-state-action", children: o }) : null
  ] });
}
function po({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ i.jsx(jt, { size: 18 }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ i.jsx("strong", { children: t }),
      /* @__PURE__ */ i.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Td({ status: e, run: t, compact: n = !1 }) {
  const o = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ i.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ i.jsx(fn, { size: n ? 13 : 14 }),
    /* @__PURE__ */ i.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: o, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function en({ value: e, ariaLabel: t, copiedLabel: n, onCopied: o, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await Gb(e), o(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ i.jsx(ff, { size: 12 }) });
}
function SN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) {
  const [r, s] = U("loading"), [a, c] = U(""), [u, l] = U(""), [d, p] = U(null), [f, h] = U([]), g = n?.trim().toLowerCase() ?? "", b = ue(
    () => g ? f.filter((S) => qb(S, g)) : f,
    [g, f]
  ), w = ue(
    () => Array.from(new Set(f.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [f]
  ), m = Vt(t, "weaver.workflows.explain-executable"), j = se(async () => {
    s("loading"), c("");
    try {
      h(await ll(e)), s("ready");
    } catch (S) {
      c(S instanceof Error ? S.message : String(S)), s("failed");
    }
  }, [e]);
  Q(() => {
    j();
  }, [j]);
  const y = async (S) => {
    l(""), p(null), c("");
    try {
      const k = await cl(e, S.artifactId), M = Sd(k);
      p({ artifactId: S.artifactId, workflowExecutionId: M }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, x = (S) => {
    m && Ot(t, m, S) && (c(""), p(null), l(`Sent ${S.artifactId} to Weaver`));
  }, N = (S) => {
    c(""), p(null), l(`Copied ${S}`);
  }, v = (S) => {
    l(""), p(null), c(`Could not copy ${S}.`);
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        j();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ i.jsx(vr, { size: 14 }),
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
        /* @__PURE__ */ i.jsx(Hc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsx(po, { message: a }) : null,
    u ? /* @__PURE__ */ i.jsx(Td, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    r === "ready" && b.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(nn, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    r === "ready" && b.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ i.jsx("span", { children: "Version" }),
        /* @__PURE__ */ i.jsx("span", { children: "Source" }),
        /* @__PURE__ */ i.jsx("span", { children: "Root" }),
        /* @__PURE__ */ i.jsx("span", { children: "Published" }),
        /* @__PURE__ */ i.jsx("span", { children: "Actions" })
      ] }),
      b.map((S) => /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-row", role: "row", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-cell", children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
            /* @__PURE__ */ i.jsx("strong", { title: S.artifactId, children: S.artifactId }),
            /* @__PURE__ */ i.jsx(en, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: N, onCopyFailed: v })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ i.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ i.jsx(en, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: N, onCopyFailed: v })
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ i.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ i.jsx(en, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: N, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ i.jsx(EN, { executable: S, onCopied: N, onCopyFailed: v }),
        /* @__PURE__ */ i.jsx("span", { children: Nd(S) }),
        /* @__PURE__ */ i.jsx("span", { children: Je(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
            y(S);
          }, children: [
            /* @__PURE__ */ i.jsx(nn, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => x(S), children: [
            /* @__PURE__ */ i.jsx(ut, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function EN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const o = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ i.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ i.jsx("span", { className: "wf-source-kind", children: jd(e.sourceKind) }),
    o ? /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
      /* @__PURE__ */ i.jsx("code", { title: o, children: o }),
      /* @__PURE__ */ i.jsx(en, { value: o, ariaLabel: `Copy source ID ${o}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    r ? /* @__PURE__ */ i.jsxs("small", { children: [
      "Version ",
      r
    ] }) : null
  ] });
}
function CN({ context: e, ai: t, definitionId: n, publishedArtifactId: o }) {
  const [r, s] = U("loading"), [a, c] = U(""), [u, l] = U(""), [d, p] = U(null), [f, h] = U([]), g = Vt(t, "weaver.workflows.explain-executable"), b = se(async () => {
    s("loading"), c("");
    try {
      const N = await ll(e);
      h(N.filter((v) => Yb(v, n)).sort(Zb)), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    b();
  }, [b, o]);
  const w = async (N) => {
    l(""), p(null), c("");
    try {
      const v = await cl(e, N.artifactId);
      p({ artifactId: N.artifactId, workflowExecutionId: Sd(v) }), l(`Started ${N.artifactId}`);
    } catch (v) {
      c(v instanceof Error ? v.message : String(v));
    }
  }, m = (N) => {
    g && Ot(t, g, N) && (c(""), p(null), l(`Sent ${N.artifactId} to Weaver`));
  }, j = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, y = (N) => {
    c(""), p(null), l(`Copied ${N}`);
  }, x = (N) => {
    l(""), p(null), c(`Could not copy ${N}.`);
  };
  return /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        f.length,
        " artifact",
        f.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        b();
      }, children: [
        /* @__PURE__ */ i.jsx(Yi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: j, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ i.jsx(jt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ i.jsx(Td, { status: u, run: d, compact: !0 }) : null,
    r === "loading" ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    r === "ready" && f.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    r === "ready" && f.length > 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: f.map((N) => /* @__PURE__ */ i.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": N.artifactId === o ? "true" : void 0, children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            N.artifactVersion
          ] }),
          N.artifactId === o ? /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ i.jsx("span", { children: Je(N.publishedAt ?? N.createdAt) })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ i.jsx("code", { title: N.artifactId, children: N.artifactId }),
          /* @__PURE__ */ i.jsx(en, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: y, onCopyFailed: x })
        ] }),
        /* @__PURE__ */ i.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ i.jsx("code", { title: N.artifactHash, children: N.artifactHash }),
          /* @__PURE__ */ i.jsx(en, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: y, onCopyFailed: x })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ i.jsxs("dd", { children: [
            jd(N.sourceKind),
            " ",
            N.sourceVersion ? `v${N.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ i.jsxs("div", { children: [
          /* @__PURE__ */ i.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ i.jsx("dd", { children: Nd(N) })
        ] })
      ] }),
      /* @__PURE__ */ i.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
          w(N);
        }, children: [
          /* @__PURE__ */ i.jsx(nn, { size: 13 }),
          " Run"
        ] }),
        g ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => m(N), children: [
          /* @__PURE__ */ i.jsx(ut, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, N.artifactId)) }) : null
  ] });
}
function IN() {
  const [e, t] = U(() => Sc(yc, Lb, Mn, Pn)), [n, o] = U(() => Sc(mc, Vb, Rn, zn)), [r, s] = U(() => Ec(xc, !1)), [a, c] = U(() => Ec(wc, !1)), [u, l] = U(tN);
  Q(() => {
    Dn(yc, String(e));
  }, [e]), Q(() => {
    Dn(mc, String(n));
  }, [n]), Q(() => {
    Dn(xc, String(r));
  }, [r]), Q(() => {
    Dn(wc, String(a));
  }, [a]), Q(() => {
    Dn(xd, u);
  }, [u]), Q(() => {
    if (!u) return;
    const y = (x) => {
      x.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [u]);
  const d = se((y) => {
    l((x) => x === y ? null : x), y === "palette" ? s((x) => !x) : c((x) => !x);
  }, []), p = se((y) => {
    y === "palette" ? s(!1) : c(!1), l((x) => x === y ? null : y);
  }, []), f = se((y, x) => {
    l(null), y === "palette" ? (s(!1), t((N) => Go(N + x, Mn, Pn))) : (c(!1), o((N) => Go(N + x, Rn, zn)));
  }, []), h = se((y, x) => {
    x.preventDefault(), l(null), y === "palette" ? s(!1) : c(!1);
    const N = x.clientX, v = y === "palette" ? e : n, S = y === "palette" ? Mn : Rn, k = y === "palette" ? Pn : zn;
    document.body.classList.add("wf-side-panel-resizing");
    const M = (T) => {
      const A = y === "palette" ? T.clientX - N : N - T.clientX, z = Go(v + A, S, k);
      y === "palette" ? t(z) : o(z);
    }, R = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", M), window.removeEventListener("pointerup", R), window.removeEventListener("pointercancel", R);
    };
    window.addEventListener("pointermove", M), window.addEventListener("pointerup", R), window.addEventListener("pointercancel", R);
  }, [n, e]), g = se((y, x) => {
    x.key === "ArrowLeft" ? (x.preventDefault(), f(y, y === "palette" ? -Oo : Oo)) : x.key === "ArrowRight" ? (x.preventDefault(), f(y, y === "palette" ? Oo : -Oo)) : x.key === "Home" ? (x.preventDefault(), y === "palette" ? t(Mn) : o(Rn)) : x.key === "End" && (x.preventDefault(), y === "palette" ? t(Pn) : o(zn));
  }, [f]), b = !r && u !== "inspector", w = !a && u !== "palette", m = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), j = {
    "--wf-palette-width": `${r ? vc : e}px`,
    "--wf-inspector-width": `${a ? vc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: b,
    inspectorExpanded: w,
    editorBodyClassName: m,
    editorBodyStyle: j,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: p,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: g
  };
}
const kN = 50;
function Dc() {
  return { past: [], future: [] };
}
function AN(e) {
  return e.past.length > 0;
}
function _N(e) {
  return e.future.length > 0;
}
function Tc(e, t, n = kN) {
  const o = [...e.past, t];
  return o.length > n && o.splice(0, o.length - n), { past: o, future: [] };
}
function DN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), o = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: o };
}
function TN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), o = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: o };
}
function $N({ draft: e, setDraft: t, setSelectedNodeId: n, setFrames: o }) {
  const r = re(Dc()), s = re(null), a = re(""), c = re(!1), [u, l] = U(0), d = se((m) => {
    r.current = Dc(), s.current = m ? Vn(m) : null, a.current = m ? He(m) : "", c.current = !1, l(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (c.current) {
      c.current = !1;
      return;
    }
    const m = He(e);
    if (m === a.current) return;
    const j = window.setTimeout(() => {
      const y = s.current;
      y && (r.current = Tc(r.current, y), l((x) => x + 1)), s.current = Vn(e), a.current = m;
    }, Pb);
    return () => window.clearTimeout(j);
  }, [e]);
  const p = se(() => {
    if (!e) return;
    const m = He(e);
    if (m === a.current) return;
    const j = s.current;
    j && (r.current = Tc(r.current, j)), s.current = Vn(e), a.current = m;
  }, [e]), f = se((m) => {
    c.current = !0, s.current = Vn(m), a.current = He(m), t(m), n(null), o([]), l((j) => j + 1);
  }, [t, o, n]), h = se(() => {
    if (!e) return;
    p();
    const m = DN(r.current, e);
    m && (r.current = m.history, f(m.snapshot));
  }, [e, p, f]), g = se(() => {
    if (!e) return;
    p();
    const m = TN(r.current, e);
    m && (r.current = m.history, f(m.snapshot));
  }, [e, p, f]), { canUndoNow: b, canRedoNow: w } = ue(() => {
    const m = !!e && !!s.current && He(e) !== a.current;
    return {
      canUndoNow: AN(r.current) || m,
      canRedoNow: _N(r.current) && !m
    };
  }, [e, u]);
  return { resetHistory: d, undo: h, redo: g, canUndoNow: b, canRedoNow: w };
}
function MN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: o,
  expressionEditors: r,
  workflowDesignerPanels: s,
  onBack: a
}) {
  const [c, u] = U(null), l = re(null), d = re(null), p = re({});
  Q(() => {
    l.current = c;
  }, [c]);
  const [f, h] = U(null), [g, b] = U([]), [w, m] = U([]), [j, y] = U(null), [x, N] = U(Bo), [v, S] = U("loading"), [k, M] = U([]), [R, T] = U([]), [A, z] = U([]), [E, D] = U(null), [I, $] = U(null), [L, P] = U(null), [K, F] = U(null), [B, q] = U(""), [G, X] = U(""), [le, Z] = U("idle"), [O, Y] = U(null), [ae, ce] = U(!1), [te, oe] = U(null), [de, H] = U(() => /* @__PURE__ */ new Set()), [ee, ge] = U(""), [be, Ke] = U("activities"), [Ue, Pe] = U("inspector"), [Xe, et] = U("designer"), Ce = re(null), ke = re(null), Re = re(""), ze = re(0), tt = re(Promise.resolve()), gn = re(/* @__PURE__ */ new Map()), pt = re(null), ht = re(null), St = re(!1), {
    paletteWidth: Lr,
    inspectorWidth: Vr,
    paletteCollapsed: Ht,
    inspectorCollapsed: Wt,
    maximizedSidePanel: nt,
    setInspectorCollapsed: Ft,
    paletteExpanded: ho,
    inspectorExpanded: go,
    editorBodyClassName: Or,
    editorBodyStyle: Hr,
    toggleSidePanelCollapsed: yo,
    toggleSidePanelMaximized: mo,
    startSidePanelResize: xo,
    handleSidePanelResizeKeyDown: wo
  } = IN(), { resetHistory: vo, undo: yn, redo: mn, canUndoNow: Wr, canRedoNow: Fr } = $N({ draft: f, setDraft: h, setSelectedNodeId: $, setFrames: M }), Et = f?.state.rootActivity ?? null, De = ue(() => new Map(g.map((C) => [C.activityVersionId, C])), [g]), xn = se(
    (C) => fh([C.activityVersionId, C.activityTypeKey], j),
    [j]
  ), bo = ue(() => Qb(w), [w]), Le = ue(() => Qc(Et, k), [Et, k]), No = Gi(Le, Le ? De.get(Le.activityVersionId) : void 0), ye = !!Le && No === "unsupported", Ve = ue(() => ye ? null : Hn(Et, k), [Et, k, ye]), Bt = ue(() => Ki(g), [g]), wn = ue(() => {
    const C = ee.trim().toLowerCase();
    if (!C) return Bt;
    const _ = g.filter((V) => Ee(V).toLowerCase().includes(C) || V.activityTypeKey.toLowerCase().includes(C) || (V.category ?? "").toLowerCase().includes(C) || (V.description ?? "").toLowerCase().includes(C));
    return Ki(_);
  }, [g, ee, Bt]), he = ue(() => ye && Le?.nodeId === I ? Le : Ve?.slot.activities.find((C) => C.nodeId === I) ?? null, [ye, Ve, Le, I]), gt = ue(
    () => he ? eN(he, De, bo) : null,
    [De, bo, he]
  ), jo = ue(
    () => he ? xn({ activityVersionId: he.activityVersionId, activityTypeKey: De.get(he.activityVersionId)?.activityTypeKey }) : null,
    [xn, De, he]
  ), vn = he ? Me(he) : [], bn = Mp(e, f?.state, I), Ie = No === "flowchart" && Ve?.slot.mode === "flowchart", Ct = !Et || !ye, yt = le !== "idle", So = !!f?.state.rootActivity && !yt, Eo = Vt(n, "weaver.workflows.find-draft-risks"), Co = Vt(n, "weaver.workflows.propose-update");
  Q(() => {
    if (!(!c || !f))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: c.definition.id,
        workflowDefinitionId: c.definition.id,
        workflowVersionId: f.sourceVersionId ?? null,
        draftId: f.id,
        revision: aN(f),
        selectedNodeId: I,
        selectedActivityType: gt?.typeName ?? (he ? De.get(he.activityVersionId)?.activityTypeKey ?? he.activityVersionId : null),
        summary: c.definition.name,
        activities: Id(f.state.rootActivity, De),
        connections: kd(f.state.rootActivity),
        diagnostics: f.validationErrors.map((C) => ({ severity: C.code ?? "warning", message: C.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === c.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [De, c, f, gt, he, I]), Q(() => {
    const C = (V) => {
      const W = V.detail;
      if (!W?.batch || !W.respond) return;
      if (!f || !c) {
        W.respond({ ok: !1, message: "No active workflow draft is open." });
        return;
      }
      const J = W.batch.workflowDefinitionId;
      if (J && J !== "active-draft" && J !== c.definition.id) {
        W.respond({ ok: !1, message: `Batch targets workflow '${J}', but '${c.definition.id}' is active.` });
        return;
      }
      try {
        const fe = Vn(f), ie = Sb(f, W.batch, g), we = `weaver-batch-${Date.now()}`;
        gn.current.set(we, fe), h(ie.draft), M([]), $(ie.finalActivityIds.at(-1) ?? null), oe(null), Y(null), X(ie.summary), q(""), W.respond({ ok: !0, result: { ...ie, undoToken: we } });
      } catch (fe) {
        const ie = fe instanceof Error ? fe.message : String(fe);
        q(ie), W.respond({ ok: !1, message: ie });
      }
    }, _ = (V) => {
      const W = V.detail;
      if (!W?.undoToken || !W.respond) return;
      const J = gn.current.get(W.undoToken);
      if (!J) {
        W.respond({ ok: !1, message: "The Weaver batch undo point is no longer available." });
        return;
      }
      gn.current.delete(W.undoToken), h(J), M([]), $(null), oe(null), Y(null), X("Restored workflow draft before Weaver batch."), q(""), W.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(fc, C), window.addEventListener(pc, _), () => {
      window.removeEventListener(fc, C), window.removeEventListener(pc, _);
    };
  }, [g, c, f]);
  const Nn = se(async () => {
    q(""), S("loading");
    const [C, _, V, W, J] = await Promise.all([
      Tp(e, t),
      Qi(e),
      Yp(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Zp(e).then(
        (ie) => ({ ok: !0, descriptors: ie }),
        () => ({ ok: !1, descriptors: Bo })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      ul(e).then(
        (ie) => ie,
        () => null
      )
    ]), fe = C.draft ?? null;
    u(C), Re.current = fe ? He(fe) : "", vo(fe), h(fe), b(_.activities ?? []), m(V.descriptors), y(J), N(W.descriptors.length > 0 ? W.descriptors : Bo), S(V.ok ? "ready" : "failed"), M([]), $(null);
  }, [e, t, vo]);
  Q(() => {
    Nn().catch((C) => q(C instanceof Error ? C.message : String(C)));
  }, [Nn]), Q(() => {
    H((C) => {
      let _ = !1;
      const V = new Set(C);
      for (const W of Bt)
        V.has(W.category) || (V.add(W.category), _ = !0);
      return _ ? V : C;
    });
  }, [Bt]), Q(() => {
    if (!Le) {
      T([]), z([]);
      return;
    }
    const C = ye ? ji(Le, g, f?.layout ?? []) : Ve ? el(Ve, g, f?.layout ?? []) : { nodes: [], edges: [] };
    T(C.nodes), z(C.edges);
  }, [g, f?.layout, ye, Ve, Le]);
  const Br = (C) => {
    h((_) => _ && { ..._, state: { ..._.state, rootActivity: C } });
  }, jn = se((C, _) => {
    if (f?.state.rootActivity && ye)
      return;
    const V = Ei(C, Ic(C));
    if (!f?.state.rootActivity) {
      Br(V), $(V.nodeId);
      return;
    }
    if (!Ve) {
      if (!Me(V)[0]) {
        X(""), q("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
        return;
      }
      h((J) => {
        if (!J?.state.rootActivity) return J;
        const fe = J.state.rootActivity, ie = Si(V, [], [fe]), we = _ ? [
          ...J.layout.filter((ve) => ve.nodeId !== fe.nodeId),
          {
            nodeId: fe.nodeId,
            x: Math.round(_.x),
            y: Math.round(_.y)
          }
        ] : J.layout;
        return {
          ...J,
          layout: we,
          state: {
            ...J.state,
            rootActivity: ie
          }
        };
      }), $(f.state.rootActivity.nodeId), q(""), X(`Wrapped root in ${Ee(C)}`);
      return;
    }
    h((W) => {
      if (!W?.state.rootActivity) return W;
      const J = Hn(W.state.rootActivity, k);
      if (!J) return W;
      const fe = Si(W.state.rootActivity, k, [...J.slot.activities, V]), ie = _ ? [
        ...W.layout.filter((we) => we.nodeId !== V.nodeId),
        {
          nodeId: V.nodeId,
          x: Math.round(_.x),
          y: Math.round(_.y)
        }
      ] : W.layout;
      return {
        ...W,
        layout: ie,
        state: {
          ...W.state,
          rootActivity: fe
        }
      };
    }), $(V.nodeId);
  }, [f?.state.rootActivity, k, ye, Ve]), Kt = se((C, _) => {
    const V = Ei(C, Ic(C)), W = {
      id: V.nodeId,
      type: "workflowActivity",
      position: _,
      selected: !0,
      data: {
        label: Ee(C),
        activityVersionId: C.activityVersionId,
        activityTypeKey: C.activityTypeKey,
        category: C.category,
        executionType: C.executionType,
        icon: or(C),
        childSlots: Me(V),
        acceptsInbound: String(C.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: il(V, C)
      }
    };
    return { activityNode: V, node: W };
  }, []), _e = se((C, _, V = []) => {
    ye || h((W) => {
      if (!W) return W;
      const J = np(W.layout, C), fe = W.state.rootActivity;
      if (!fe) return { ...W, layout: J };
      const ie = Hn(fe, k);
      if (!ie) return { ...W, layout: J };
      const we = ep(ie, C, _, V), ve = ie.slot.mode === "flowchart" ? tp(we, _) : we;
      return {
        ...W,
        layout: J,
        state: {
          ...W.state,
          rootActivity: tl(fe, k, ve)
        }
      };
    });
  }, [k, ye]), Sn = se((C, _) => {
    if (!Ce.current) return null;
    const V = Ce.current.getBoundingClientRect();
    return E ? E.screenToFlowPosition({ x: C, y: _ }) : {
      x: C - V.left,
      y: _ - V.top
    };
  }, [E]), Ut = se((C, _) => document.elementFromPoint(C, _)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), En = se((C, _, V) => {
    const W = R.find((Oe) => Oe.id === _.source), J = R.find((Oe) => Oe.id === _.target), fe = W && J ? rN(W, J) : W ? kc(W) : V, ie = Kt(C, fe), ve = [...R.map((Oe) => Oe.selected ? { ...Oe, selected: !1 } : Oe), ie.node], In = dp(A, _, ie.node.id);
    T(ve), z(In), $(ie.node.id), _e(ve, In, [ie.activityNode]);
  }, [_e, Kt, A, R]), Xt = se((C, _, V) => {
    if (!Ct || !Ce.current) return !1;
    const W = Ce.current.getBoundingClientRect();
    if (!(_ >= W.left && _ <= W.right && V >= W.top && V <= W.bottom)) return !1;
    const fe = Sn(_, V);
    if (!fe) return !1;
    if (Ie) {
      const ie = Ut(_, V), we = ie ? A.find((ve) => ve.id === ie) : void 0;
      if (we)
        return En(C, we, fe), !0;
    }
    return jn(C, fe), !0;
  }, [jn, Ct, A, Ut, Ie, En, Sn]);
  Q(() => {
    const C = (V) => {
      const W = pt.current;
      if (!W) return;
      Math.hypot(V.clientX - W.startX, V.clientY - W.startY) >= $b && (W.dragging = !0);
    }, _ = (V) => {
      const W = pt.current;
      if (pt.current = null, !W?.dragging || !Ce.current || ht.current) return;
      const J = Ce.current.getBoundingClientRect();
      V.clientX >= J.left && V.clientX <= J.right && V.clientY >= J.top && V.clientY <= J.bottom && (St.current = !0, window.setTimeout(() => {
        St.current = !1;
      }, 0), Xt(W.activity, V.clientX, V.clientY));
    };
    return window.addEventListener("pointermove", C), window.addEventListener("pointerup", _), window.addEventListener("pointercancel", _), () => {
      window.removeEventListener("pointermove", C), window.removeEventListener("pointerup", _), window.removeEventListener("pointercancel", _);
    };
  }, [E, Xt]);
  const Kr = (C, _) => {
    ht.current = { activityVersionId: _.activityVersionId, handledDrop: !1 }, C.dataTransfer.setData(gc, _.activityVersionId), C.dataTransfer.setData("text/plain", _.activityVersionId), C.dataTransfer.effectAllowed = "copy";
  }, Ur = (C, _) => {
    const V = ht.current;
    ht.current = null, !V?.handledDrop && (C.clientX === 0 && C.clientY === 0 || Xt(_, C.clientX, C.clientY) && (St.current = !0, window.setTimeout(() => {
      St.current = !1;
    }, 0)));
  }, Xr = (C, _) => {
    C.button === 0 && (pt.current = {
      activity: _,
      startX: C.clientX,
      startY: C.clientY,
      dragging: !1
    });
  }, qr = (C) => {
    St.current || Ct && jn(C);
  }, Io = (C) => {
    if (!Ct) {
      C.dataTransfer.dropEffect = "none";
      return;
    }
    if (C.preventDefault(), C.dataTransfer.dropEffect = "copy", !Ie) return;
    const _ = Ut(C.clientX, C.clientY);
    F(_);
  }, ko = (C) => {
    if (!Ce.current) return;
    const _ = C.relatedTarget;
    _ && Ce.current.contains(_) || F(null);
  }, Cn = (C) => {
    C.preventDefault(), F(null);
    const _ = C.dataTransfer.getData(gc) || C.dataTransfer.getData("text/plain");
    if (!_ || (C.stopPropagation(), ht.current?.activityVersionId === _ && (ht.current.handledDrop = !0), !Ct)) return;
    const V = De.get(_);
    V && Xt(V, C.clientX, C.clientY);
  }, Yr = () => {
    if (!Ie) return;
    const C = Ce.current?.getBoundingClientRect();
    C && P({
      kind: "fromEmpty",
      clientX: C.left + C.width / 2,
      clientY: C.top + C.height / 2
    });
  }, It = se(async (C, _) => {
    const V = async () => {
      const J = ++ze.current, fe = He(C);
      q("");
      try {
        const ie = await Hp(e, C), we = He(ie);
        return Re.current = we, h((ve) => !ve || ve.id !== ie.id ? ve : He(ve) === fe ? ie : { ...ve, validationErrors: ie.validationErrors }), J === ze.current && X(_), ie;
      } catch (ie) {
        throw J === ze.current && (X(""), q(ie instanceof Error ? ie.message : String(ie))), ie;
      }
    }, W = tt.current.then(V, V);
    return tt.current = W.catch(() => {
    }), W;
  }, [e]);
  Q(() => {
    if (!ae || !f || He(f) === Re.current) return;
    X("Autosaving...");
    const _ = window.setTimeout(() => {
      It(f, "Autosaved").catch(() => {
      });
    }, Mb);
    return () => window.clearTimeout(_);
  }, [ae, f, It]);
  const Zr = se(() => {
    if (!f) return;
    const C = c?.definition.name;
    G0(q0(f, C), C), X("Exported workflow as JSON.");
  }, [f, c]), Gr = se((C) => {
    h((_) => _ && { ..._, state: C(_.state) });
  }, []), mt = se(() => {
    d.current !== null && (window.clearTimeout(d.current), d.current = null);
    const C = p.current;
    p.current = {};
    const _ = l.current?.definition;
    !_ || C.name === void 0 && C.description === void 0 || Op(e, _.id, {
      name: C.name ?? _.name,
      description: C.description ?? _.description ?? null
    }).then((V) => u((W) => W && W.definition.id === V.definition.id ? { ...W, definition: V.definition } : W)).catch(() => X("Couldn't save name/description."));
  }, [e]), Jr = se((C) => {
    u((_) => _ && { ..._, definition: { ..._.definition, ...C } }), p.current = { ...p.current, ...C }, d.current !== null && window.clearTimeout(d.current), d.current = window.setTimeout(mt, 800);
  }, [mt]);
  Q(() => () => {
    mt();
  }, [mt]);
  const Qr = se((C) => {
    if (!f) return "No draft is loaded.";
    const _ = Z0(C, f);
    return _.ok ? (h(_.draft), $(null), M([]), X("Applied workflow JSON."), null) : _.error;
  }, [f]);
  Q(() => {
    const C = (_) => {
      if (Xe !== "designer" || !(_.metaKey || _.ctrlKey)) return;
      const V = _.target;
      if (V && (V.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(V.tagName))) return;
      const W = _.key.toLowerCase();
      W === "z" && !_.shiftKey ? (_.preventDefault(), yn()) : (W === "z" && _.shiftKey || W === "y") && (_.preventDefault(), mn());
    };
    return window.addEventListener("keydown", C), () => window.removeEventListener("keydown", C);
  }, [Xe, yn, mn]);
  const Ao = async () => {
    if (!(!f || yt)) {
      Z("saving"), X("Saving...");
      try {
        await It(f, "Saved");
      } catch {
      } finally {
        Z("idle");
      }
    }
  }, $d = async () => {
    if (!(!f || yt)) {
      Z("promoting"), X("Saving...");
      try {
        await It(f, "Saved"), X("Promoting...");
        const C = await Wp(e, f.id), _ = await Fp(e, C.versionId);
        oe(_.artifactId), X(`Published ${_.artifactVersion}`), await Nn();
      } catch (C) {
        X(""), q(C instanceof Error ? C.message : String(C));
      } finally {
        Z("idle");
      }
    }
  }, Md = async () => {
    if (!f?.state.rootActivity || yt) return;
    const C = f, _ = He(C);
    Y(null), X("Preparing test run...");
    try {
      Z("testRunPreparing"), X("Preparing test run...");
      const V = cN(C);
      Z("testRunStarting"), X("Starting test run...");
      const W = await Bp(e, {
        definitionId: C.definitionId,
        snapshotId: V,
        state: C.state
      });
      Y({ draftSignature: _, view: W }), Pe("runtime"), Ft(!1), X(Es(W) ? "Test run rejected" : "Test run dispatched");
    } catch (V) {
      X(""), q(V instanceof Error ? V.message : String(V));
    } finally {
      Z("idle");
    }
  }, Pd = (C) => {
    const _ = ye ? C.filter((V) => V.type === "select") : C;
    _.length !== 0 && T((V) => ju(_, V));
  }, Rd = (C) => {
    ye || z((_) => Su(C, _));
  }, ei = (C) => !C.source || !C.target || C.source === C.target || !Ie ? !1 : !C.targetHandle, zd = (C) => {
    if (!f?.state.rootActivity || !Ve || !Ie || !ei(C)) return;
    const _ = rr(C.source, C.target, C.sourceHandle ?? "Done", C.targetHandle ?? void 0), V = Cu(_, A);
    z(V), _e(R, V);
  }, Ld = () => {
    _e(R, A);
  }, Vd = !ye && R.length > 0, Od = se(() => {
    if (ye || R.length === 0) return;
    const C = Ve?.slot.mode === "sequence" ? "sequence" : "flowchart", _ = eb(R, A, C), V = R.map((W) => {
      const J = _.get(W.id);
      return J ? { ...W, position: J } : W;
    });
    T(V), _e(V, A), window.requestAnimationFrame(() => E?.fitView({ padding: 0.2 })), X("Rearranged the canvas.");
  }, [A, R, Ve, ye, _e, E]), Hd = (C, _) => {
    if (!_.nodeId || _.handleType === "target") {
      ke.current = null;
      return;
    }
    ke.current = {
      nodeId: _.nodeId,
      handleId: _.handleId ?? null
    };
  }, Wd = (C, _) => {
    const V = sN(ke.current, _);
    if (ke.current = null, !V || !Ie || _.toNode || _.toHandle || iN(C)) return;
    const W = Cd(C);
    P({
      kind: "fromPort",
      sourceNodeId: V.nodeId,
      sourceHandleId: V.handleId,
      clientX: W.x,
      clientY: W.y
    });
  }, Fd = (C, _) => {
    if (!Ie || !ei(_)) return;
    const V = Ew(C, {
      ..._,
      sourceHandle: _.sourceHandle ?? "Done",
      targetHandle: _.targetHandle ?? void 0
    }, A, { shouldReplaceId: !1 });
    z(V), _e(R, V);
  }, Bd = (C) => {
    if (ye || C.length === 0) return;
    const _ = new Set(C.map((J) => J.id)), V = R.filter((J) => !_.has(J.id)), W = A.filter((J) => !_.has(J.source) && !_.has(J.target));
    T(V), z(W), I && _.has(I) && $(null), _e(V, W);
  }, Kd = (C) => {
    if (ye || C.length === 0) return;
    const _ = new Set(C.map((W) => W.id)), V = A.filter((W) => !_.has(W.id));
    z(V), _e(R, V);
  }, ks = se((C) => {
    if (ye) return;
    const _ = A.filter((V) => V.id !== C);
    z(_), _e(R, _);
  }, [_e, A, ye, R]), As = se((C, _, V) => {
    Ie && P({ kind: "spliceEdge", edgeId: C, clientX: _, clientY: V });
  }, [Ie]), Ud = (C) => {
    const _ = L;
    if (!_) return;
    P(null);
    const V = Sn(_.clientX, _.clientY) ?? { x: 0, y: 0 };
    if (_.kind === "fromEmpty") {
      const J = Kt(C, V), ie = [...R.map((we) => we.selected ? { ...we, selected: !1 } : we), J.node];
      T(ie), $(J.node.id), _e(ie, A, [J.activityNode]);
      return;
    }
    if (_.kind === "fromPort") {
      const J = R.find((Oe) => Oe.id === _.sourceNodeId), fe = J ? kc(J) : V, ie = Kt(C, fe), ve = [...R.map((Oe) => Oe.selected ? { ...Oe, selected: !1 } : Oe), ie.node], In = [...A, rr(_.sourceNodeId, ie.node.id, _.sourceHandleId ?? "Done")];
      T(ve), z(In), $(ie.node.id), _e(ve, In, [ie.activityNode]);
      return;
    }
    const W = A.find((J) => J.id === _.edgeId);
    W && En(C, W, V);
  }, Xd = ue(() => ({
    highlightedEdgeId: K,
    deleteEdge: ks,
    requestInsertActivity: As
  }), [ks, K, As]), qd = (C, _, V) => {
    M((W) => [...W, { ownerNodeId: C.nodeId, slotId: _, label: V }]), $(null);
  }, _s = se((C) => {
    h((_) => {
      const V = _?.state.rootActivity;
      return !_ || !V ? _ : {
        ..._,
        state: {
          ..._.state,
          rootActivity: nl(V, C.nodeId, () => C)
        }
      };
    });
  }, []), Yd = se((C) => {
    if (!C) return;
    const _ = f?.state.rootActivity;
    if (!_) return;
    const V = Jf(_, C, (W) => {
      const J = De.get(W.activityVersionId);
      return J ? Ee(J) : W.nodeId;
    });
    V && (et("designer"), M(V), $(C), Ft(!1));
  }, [f?.state.rootActivity, De, Ft]), Zd = (C) => {
    H((_) => {
      const V = new Set(_);
      return V.has(C) ? V.delete(C) : V.add(C), V;
    });
  };
  if (!c || !f)
    return /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: B || "Loading workflow editor..." });
  const _o = O?.draftSignature === He(f) ? O.view : null, Ds = _o && G.startsWith("Test run") ? "" : G, Gd = (C) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(C)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Jd = {
    definition: c.definition,
    draft: f,
    selectedActivity: he,
    selectedActivityDescriptor: gt,
    selectedActivitySlots: vn,
    catalog: g,
    currentScopeOwner: Le,
    frames: k
  }, Ts = s.map((C) => {
    const _ = C.component;
    return {
      id: C.id,
      title: C.title,
      side: C.side,
      order: C.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ i.jsx(_, { context: Jd })
    };
  }), ti = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(br, { size: 15 }),
      render: ef
    },
    ...Ts.filter((C) => C.side === "left")
  ].sort(Ac), ni = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ i.jsx(qi, { size: 15 }),
      render: tf
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ i.jsx(nn, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(jN, { testRun: _o, onOpenRun: Gd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ i.jsx(Fc, { size: 15 }),
      render: () => /* @__PURE__ */ i.jsx(
        CN,
        {
          context: e,
          ai: n,
          definitionId: c.definition.id,
          publishedArtifactId: te
        }
      )
    },
    ...Ts.filter((C) => C.side === "right")
  ].sort(Ac), $s = ti.find((C) => C.id === be) ?? ti[0], Ms = ni.find((C) => C.id === Ue) ?? ni[0], Qd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ i.jsx(mf, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ i.jsx(xf, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ i.jsx(Xi, { size: 14 }), render: () => null }
  ];
  function ef() {
    const C = ee.trim().length > 0;
    return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-body", children: [
      /* @__PURE__ */ i.jsxs("label", { className: "wf-palette-search", children: [
        /* @__PURE__ */ i.jsx(vr, { size: 14, "aria-hidden": "true" }),
        /* @__PURE__ */ i.jsx(
          "input",
          {
            type: "search",
            value: ee,
            placeholder: "Search activities",
            "aria-label": "Search activity palette",
            onChange: (_) => ge(_.target.value)
          }
        )
      ] }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-palette-list", role: "tree", "aria-label": "Available activities", children: wn.length === 0 ? /* @__PURE__ */ i.jsx("p", { className: "wf-muted wf-palette-empty", children: "No matching activities." }) : wn.map((_) => {
        const V = C || de.has(_.category);
        return /* @__PURE__ */ i.jsxs("div", { className: "wf-palette-category", children: [
          /* @__PURE__ */ i.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-category-toggle",
              role: "treeitem",
              "aria-expanded": V,
              onClick: () => Zd(_.category),
              children: [
                V ? /* @__PURE__ */ i.jsx(Oc, { size: 14 }) : /* @__PURE__ */ i.jsx(Yt, { size: 14 }),
                /* @__PURE__ */ i.jsx("span", { children: _.category }),
                /* @__PURE__ */ i.jsx("small", { children: _.activities.length })
              ]
            }
          ),
          V ? /* @__PURE__ */ i.jsx("div", { className: "wf-palette-activities", role: "group", children: _.activities.map((W) => {
            const J = W.description?.trim(), fe = J ? `wf-palette-description-${W.activityVersionId}` : void 0, ie = Ee(W), we = or(W);
            return /* @__PURE__ */ i.jsxs(
              "button",
              {
                type: "button",
                className: "wf-palette-activity",
                role: "treeitem",
                draggable: !0,
                title: J || Ee(W),
                "aria-describedby": fe,
                onClick: () => qr(W),
                onDragStart: (ve) => Kr(ve, W),
                onDragEnd: (ve) => Ur(ve, W),
                onPointerDown: (ve) => Xr(ve, W),
                children: [
                  /* @__PURE__ */ i.jsx("span", { className: "wf-activity-icon", "data-icon": we, "aria-hidden": "true", children: bs(we) }),
                  /* @__PURE__ */ i.jsxs("span", { className: "wf-palette-activity-text", children: [
                    /* @__PURE__ */ i.jsx("strong", { children: ie }),
                    J ? /* @__PURE__ */ i.jsx("small", { id: fe, children: J }) : null
                  ] }),
                  /* @__PURE__ */ i.jsx(Vc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
                ]
              },
              W.activityVersionId
            );
          }) }) : null
        ] }, _.category);
      }) })
    ] });
  }
  function tf() {
    return he ? /* @__PURE__ */ i.jsxs("div", { className: "wf-inspector-content", children: [
      /* @__PURE__ */ i.jsx("h3", { children: R.find((C) => C.id === he.nodeId)?.data.label ?? he.nodeId }),
      /* @__PURE__ */ i.jsxs("dl", { children: [
        /* @__PURE__ */ i.jsx("dt", { children: "Node ID" }),
        /* @__PURE__ */ i.jsx("dd", { children: he.nodeId }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity type" }),
        /* @__PURE__ */ i.jsx("dd", { children: gt?.typeName ?? De.get(he.activityVersionId)?.activityTypeKey ?? "Unknown" }),
        /* @__PURE__ */ i.jsx("dt", { children: "Activity version" }),
        /* @__PURE__ */ i.jsx("dd", { children: he.activityVersionId })
      ] }),
      jo ? /* @__PURE__ */ i.jsxs("div", { className: "wf-availability-notice", children: [
        /* @__PURE__ */ i.jsx(Qo, { size: 14 }),
        /* @__PURE__ */ i.jsxs("span", { children: [
          "No longer available for new use · ",
          sr(jo.state)
        ] })
      ] }) : null,
      /* @__PURE__ */ i.jsx(
        z0,
        {
          activity: he,
          descriptor: gt,
          editors: o,
          expressionEditors: r,
          expressionDescriptors: x,
          descriptorStatus: v,
          visibleVariables: bn.visibleVariables,
          scopeStatus: bn.status,
          onChange: _s
        }
      ),
      Np(he) ? /* @__PURE__ */ i.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ i.jsx(
        vb,
        {
          context: e,
          variables: sl(he),
          title: "Container variables",
          addLabel: "Add container variable",
          emptyLabel: "No container variables declared on this activity.",
          warnings: Cp(bn.shadowingWarnings, he.nodeId),
          onChange: (C) => _s(jp(he, C))
        }
      ) }) : null,
      vn.length > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-slot-list", children: [
        /* @__PURE__ */ i.jsx("span", { children: "Embedded slots" }),
        vn.map((C) => /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => qd(he, C.id, `${R.find((_) => _.id === he.nodeId)?.data.label ?? he.nodeId} / ${C.label}`), children: [
          C.label,
          /* @__PURE__ */ i.jsxs("small", { children: [
            C.activities.length,
            " activit",
            C.activities.length === 1 ? "y" : "ies"
          ] })
        ] }, C.id))
      ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
    ] }) : /* @__PURE__ */ i.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
  }
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", className: "wf-link-button", onClick: a, children: "Definitions" }),
      /* @__PURE__ */ i.jsx(Yt, { size: 14 }),
      /* @__PURE__ */ i.jsx("strong", { children: c.definition.name }),
      /* @__PURE__ */ i.jsx("span", { className: "wf-chip", children: "Draft" }),
      Ds ? /* @__PURE__ */ i.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ i.jsx(fn, { size: 13 }),
        " ",
        Ds
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
              disabled: !Wr,
              onClick: yn,
              children: /* @__PURE__ */ i.jsx(pf, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Fr,
              onClick: mn,
              children: /* @__PURE__ */ i.jsx(hf, { size: 16 })
            }
          ),
          /* @__PURE__ */ i.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !Vd,
              onClick: Od,
              children: /* @__PURE__ */ i.jsx(gf, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ i.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: ae, onChange: (C) => ce(C.target.checked) }),
          /* @__PURE__ */ i.jsx("span", { children: "Autosave" })
        ] }),
        Eo ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Ot(n, Eo, { definition: c.definition, draft: f }), children: [
          /* @__PURE__ */ i.jsx(ut, { size: 15 }),
          " Risks"
        ] }) : null,
        Co ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Ot(n, Co, { definition: c.definition, draft: f }), children: [
          /* @__PURE__ */ i.jsx(ut, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Zr, children: [
          /* @__PURE__ */ i.jsx(yf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: yt, onClick: () => {
          Ao();
        }, children: [
          /* @__PURE__ */ i.jsx(Lc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ i.jsxs("button", { type: "button", disabled: yt, onClick: () => {
          $d();
        }, children: [
          /* @__PURE__ */ i.jsx(Wc, { size: 15 }),
          " Promote"
        ] }),
        _o ? /* @__PURE__ */ i.jsx(
          NN,
          {
            testRun: _o,
            onOpenDetails: () => {
              Pe("runtime"), Ft(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ i.jsxs(
          "button",
          {
            type: "button",
            disabled: !So,
            title: f.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              Md();
            },
            children: [
              /* @__PURE__ */ i.jsx(nn, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    B ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(jt, { size: 16 }),
      " ",
      B
    ] }) : null,
    /* @__PURE__ */ i.jsxs("div", { className: Or, style: Hr, children: [
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            Jo,
            {
              label: "Activities panel tabs",
              tabs: ti,
              activeTabId: $s.id,
              onSelect: Ke
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Ht ? "Expand activities panel" : "Collapse activities panel",
                title: Ht ? "Expand" : "Collapse",
                onClick: () => yo("palette"),
                children: Ht ? /* @__PURE__ */ i.jsx(Yt, { size: 14 }) : /* @__PURE__ */ i.jsx(tr, { size: 14 })
              }
            ),
            Ht ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": nt === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: nt === "palette" ? "Restore" : "Maximize",
                onClick: () => mo("palette"),
                children: nt === "palette" ? /* @__PURE__ */ i.jsx(zs, { size: 14 }) : /* @__PURE__ */ i.jsx(er, { size: 14 })
              }
            )
          ] })
        ] }),
        ho ? $s.render() : null
      ] }),
      ho && !nt ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Mn,
          "aria-valuemax": Pn,
          "aria-valuenow": Lr,
          tabIndex: 0,
          onPointerDown: (C) => xo("palette", C),
          onKeyDown: (C) => wo("palette", C)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ i.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ i.jsx(
          Jo,
          {
            label: "Editor view tabs",
            tabs: Qd,
            activeTabId: Xe,
            onSelect: (C) => et(C)
          }
        ) }),
        Xe === "code" ? /* @__PURE__ */ i.jsx(ab, { draft: f, onApply: Qr }) : Xe === "properties" ? /* @__PURE__ */ i.jsx(Nb, { details: c, draft: f, context: e, onStateChange: Gr, onDefinitionMetaChange: Jr }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
          /* @__PURE__ */ i.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
              M([]), $(null);
            }, children: "Root" }),
            k.map((C, _) => /* @__PURE__ */ i.jsxs(it.Fragment, { children: [
              /* @__PURE__ */ i.jsx(Yt, { size: 13 }),
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
                M(k.slice(0, _ + 1)), $(null);
              }, children: C.label })
            ] }, `${C.ownerNodeId}-${C.slotId}-${_}`))
          ] }),
          /* @__PURE__ */ i.jsxs("div", { className: "wf-canvas", ref: Ce, onDragOver: Io, onDragLeave: ko, onDrop: Cn, children: [
            /* @__PURE__ */ i.jsx(wd.Provider, { value: Xd, children: /* @__PURE__ */ i.jsx(vd.Provider, { value: xn, children: /* @__PURE__ */ i.jsxs(
              Gu,
              {
                nodes: R,
                edges: A,
                nodeTypes: _d,
                edgeTypes: Dd,
                onInit: D,
                onNodesChange: Pd,
                onEdgesChange: Rd,
                onNodesDelete: Bd,
                onEdgesDelete: Kd,
                onConnect: zd,
                onConnectStart: Ie ? Hd : void 0,
                onConnectEnd: Ie ? Wd : void 0,
                onReconnect: Ie ? Fd : void 0,
                isValidConnection: ei,
                onDragOver: Io,
                onDragLeave: ko,
                onDrop: Cn,
                onPaneClick: () => $(null),
                onNodeClick: (C, _) => $(_.id),
                onNodeDragStop: ye ? void 0 : Ld,
                fitView: !0,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: Ie,
                nodesDraggable: !ye,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: ye ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ i.jsx(Qu, { gap: 18, size: 1 }),
                  /* @__PURE__ */ i.jsx(td, {}),
                  /* @__PURE__ */ i.jsx(od, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Ie && R.length === 0 ? /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Yr(), children: [
              /* @__PURE__ */ i.jsx(tn, { size: 15 }),
              " Add activity"
            ] }) : null,
            L ? /* @__PURE__ */ i.jsx(
              gN,
              {
                clientX: L.clientX,
                clientY: L.clientY,
                activities: g,
                onPick: Ud,
                onClose: () => P(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ i.jsx(bN, { draft: f, onRepair: Yd })
        ] })
      ] }),
      go && !nt ? /* @__PURE__ */ i.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Rn,
          "aria-valuemax": zn,
          "aria-valuenow": Vr,
          tabIndex: 0,
          onPointerDown: (C) => xo("inspector", C),
          onKeyDown: (C) => wo("inspector", C)
        }
      ) : /* @__PURE__ */ i.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ i.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ i.jsx(
            Jo,
            {
              label: "Inspector panel tabs",
              tabs: ni,
              activeTabId: Ms.id,
              onSelect: Pe
            }
          ),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Wt ? "Expand inspector panel" : "Collapse inspector panel",
                title: Wt ? "Expand" : "Collapse",
                onClick: () => yo("inspector"),
                children: Wt ? /* @__PURE__ */ i.jsx(tr, { size: 14 }) : /* @__PURE__ */ i.jsx(Yt, { size: 14 })
              }
            ),
            Wt ? null : /* @__PURE__ */ i.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": nt === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: nt === "inspector" ? "Restore" : "Maximize",
                onClick: () => mo("inspector"),
                children: nt === "inspector" ? /* @__PURE__ */ i.jsx(zs, { size: 14 }) : /* @__PURE__ */ i.jsx(er, { size: 14 })
              }
            )
          ] })
        ] }),
        go ? Ms.render() : null
      ] })
    ] })
  ] });
}
function PN({ page: e, pageSize: t, totalCount: n, onPageChange: o, onPageSizeChange: r }) {
  const s = bd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ i.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: Rb.map((u) => /* @__PURE__ */ i.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => o(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ i.jsx(tr, { size: 14 }),
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
        /* @__PURE__ */ i.jsx(Yt, { size: 14 })
      ] })
    ] })
  ] });
}
const RN = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function zN({ draft: e, creating: t, ai: n, suggestMetadataAction: o, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = U(!1), [l, d] = U(""), [p, f] = U(!1), [h, g] = U(null), [b, w] = U(null), m = re(null), j = re(e);
  j.current = e;
  const y = re(r);
  y.current = r;
  const x = se((v) => {
    const S = { ...j.current };
    v.name && (S.name = v.name), v.description && (S.description = v.description), y.current(S), g(null), w(null);
  }, []);
  Q(() => {
    if (o)
      return n.onPromptResult((v) => {
        if (v.requestId !== m.current) return;
        if (m.current = null, f(!1), v.status !== "completed") {
          w(v.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = Hb(v.text);
        if (!S) {
          w("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        v.autoApply ? x(S) : g(S);
      });
  }, [n, o, x]);
  const N = () => {
    if (!o) return;
    const v = o.createPrompt({ draft: j.current, intent: l });
    if (!v) return;
    const S = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    m.current = S, f(!0), g(null), w(null), n.dispatchPrompt({ ...v, requestId: S });
  };
  return /* @__PURE__ */ i.jsx("div", { className: "wf-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ i.jsx("section", { className: "wf-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": "workflow-create-title", children: /* @__PURE__ */ i.jsxs(
    "form",
    {
      onSubmit: (v) => {
        v.preventDefault(), a();
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
              onClick: () => u((v) => !v),
              title: o.description ?? o.label,
              children: [
                /* @__PURE__ */ i.jsx(ut, { size: 13 }),
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
                disabled: p,
                onChange: (v) => d(v.target.value),
                onKeyDown: (v) => {
                  (v.metaKey || v.ctrlKey) && v.key === "Enter" && (v.preventDefault(), N());
                }
              }
            )
          ] }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-ai-action", onClick: N, disabled: p, children: [
            /* @__PURE__ */ i.jsx(ut, { size: 13 }),
            " ",
            p ? "Generating…" : "Generate"
          ] }) }),
          b ? /* @__PURE__ */ i.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: b }) : null,
          h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ i.jsx("p", { children: /* @__PURE__ */ i.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ i.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ i.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => x(h), children: "Apply" }),
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
              onChange: (v) => r({ ...e, name: v.target.value })
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
              onChange: (v) => r({ ...e, description: v.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ i.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ i.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ i.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: RN.map((v) => {
            const S = e.rootKind === v.value;
            return /* @__PURE__ */ i.jsxs("label", { className: "wf-root-card", "data-checked": S || void 0, children: [
              /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "radio",
                  name: "wf-root-kind",
                  "aria-label": v.label,
                  value: v.value,
                  checked: S,
                  onChange: () => r({ ...e, rootKind: v.value, rootActivityVersionId: null })
                }
              ),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-title", children: v.label }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-root-card-hint", children: v.hint })
            ] }, v.value);
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
function LN({ context: e, ai: t, onOpen: n }) {
  const [o, r] = U(""), [s, a] = U("active"), [c, u] = U(1), [l, d] = U(zb), [p, f] = U("loading"), [h, g] = U(""), [b, w] = U(""), [m, j] = U([]), [y, x] = U(0), [N, v] = U(() => /* @__PURE__ */ new Set()), [S, k] = U(null), [M, R] = U(!1), [T, A] = U([]), [z, E] = U("idle"), D = re(null), I = ue(() => m.map((H) => H.id), [m]), $ = Vt(t, "weaver.workflows.suggest-create-metadata"), L = Vt(t, "weaver.workflows.explain-definition"), P = I.filter((H) => N.has(H)).length, K = I.length > 0 && P === I.length, F = se(async () => {
    f("loading"), g("");
    try {
      const H = await Dp(e, { search: o, state: s, page: c, pageSize: l }), ee = typeof H.totalCount == "number", ge = H.totalCount ?? H.definitions.length, be = bd(ge, l);
      if (ge > 0 && c > be) {
        u(be);
        return;
      }
      j(ee ? H.definitions : Ob(H.definitions, c, l)), x(ge), f("ready");
    } catch (H) {
      g(H instanceof Error ? H.message : String(H)), f("failed");
    }
  }, [e, o, s, c, l]);
  Q(() => {
    F();
  }, [F]), Q(() => {
    D.current && (D.current.indeterminate = P > 0 && !K);
  }, [K, P]);
  const B = se(async () => {
    if (!(z === "loading" || z === "ready")) {
      E("loading");
      try {
        const H = await Qi(e);
        A(H.activities ?? []), E("ready");
      } catch (H) {
        E("failed"), g(H instanceof Error ? H.message : String(H));
      }
    }
  }, [z, e]), q = () => {
    g(""), w(""), k({ name: "", description: "", rootKind: "flowchart" }), B();
  }, G = async () => {
    if (S?.name.trim()) {
      R(!0), g(""), w("");
      try {
        const H = await Rp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Wb(S, T)
        });
        k(null), n(H.definition.id);
      } catch (H) {
        g(H instanceof Error ? H.message : String(H));
      } finally {
        R(!1);
      }
    }
  }, X = (H) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(H)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await F();
  }, Z = () => v(/* @__PURE__ */ new Set()), O = (H, ee) => {
    v((ge) => {
      const be = new Set(ge);
      return ee ? be.add(H) : be.delete(H), be;
    });
  }, Y = (H) => {
    v((ee) => {
      const ge = new Set(ee);
      for (const be of I)
        H ? ge.add(be) : ge.delete(be);
      return ge;
    });
  }, ae = (H) => {
    a(H), u(1), Z();
  }, ce = (H) => {
    r(H), u(1), Z();
  }, te = async (H) => {
    if (await Os().confirm({ message: `Delete workflow definition "${H.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      w(""), g("");
      try {
        await zp(e, H.id), O(H.id, !1), w(`Deleted ${H.name}`), await le();
      } catch (ee) {
        g(ee instanceof Error ? ee.message : String(ee));
      }
    }
  }, oe = async (H) => {
    w(""), g("");
    try {
      await Lp(e, H.id), O(H.id, !1), w(`Restored ${H.name}`), await le();
    } catch (ee) {
      g(ee instanceof Error ? ee.message : String(ee));
    }
  }, de = async (H) => {
    if (await Os().confirm({ message: `Permanently delete workflow definition "${H.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      w(""), g("");
      try {
        await Vp(e, H.id), O(H.id, !1), w(`Permanently deleted ${H.name}`), await le();
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
        /* @__PURE__ */ i.jsx(vr, { size: 15 }),
        /* @__PURE__ */ i.jsx("input", { value: o, onChange: (H) => ce(H.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ i.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ i.jsxs("button", { type: "button", title: "Create workflow", onClick: q, children: [
        /* @__PURE__ */ i.jsx(tn, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    p === "failed" ? /* @__PURE__ */ i.jsx(po, { message: h, title: "Couldn't load workflow definitions" }) : null,
    p !== "failed" && h ? /* @__PURE__ */ i.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ i.jsx(jt, { size: 16 }),
      " ",
      h
    ] }) : null,
    b ? /* @__PURE__ */ i.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ i.jsx(fn, { size: 14 }),
      " ",
      b
    ] }) : null,
    N.size > 0 ? /* @__PURE__ */ i.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ i.jsxs("span", { children: [
        N.size,
        " selected"
      ] }),
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: Z, children: "Clear selection" })
    ] }) : null,
    p === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    p === "ready" && m.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(Fc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ i.jsxs("button", { type: "button", className: "wf-link-button", onClick: q, children: [
          /* @__PURE__ */ i.jsx(tn, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    p === "ready" && m.length > 0 ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ i.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ i.jsx(
            "input",
            {
              ref: D,
              type: "checkbox",
              checked: K,
              onChange: (H) => Y(H.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ i.jsx("span", { children: "Name" }),
          /* @__PURE__ */ i.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ i.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ i.jsx("span", { children: "Actions" })
        ] }),
        m.map((H) => /* @__PURE__ */ i.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${H.name}`,
            "aria-selected": N.has(H.id),
            tabIndex: 0,
            onClick: () => n(H.id),
            onKeyDown: (ee) => {
              ee.currentTarget === ee.target && (ee.key !== "Enter" && ee.key !== " " || (ee.preventDefault(), n(H.id)));
            },
            children: [
              /* @__PURE__ */ i.jsx("label", { className: "wf-row-select", onClick: (ee) => ee.stopPropagation(), children: /* @__PURE__ */ i.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: N.has(H.id),
                  onChange: (ee) => O(H.id, ee.target.checked),
                  "aria-label": `Select workflow definition ${H.name}`
                }
              ) }),
              /* @__PURE__ */ i.jsxs("span", { children: [
                /* @__PURE__ */ i.jsx("strong", { children: H.name }),
                /* @__PURE__ */ i.jsx("small", { children: H.description || H.id })
              ] }),
              /* @__PURE__ */ i.jsx("span", { children: H.latestVersion ?? "No version" }),
              /* @__PURE__ */ i.jsx("span", { children: s === "deleted" ? Je(H.deletedAt) : H.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ i.jsx("span", { children: Je(H.lastModifiedAt) }),
              /* @__PURE__ */ i.jsx("span", { className: "wf-row-actions", onClick: (ee) => ee.stopPropagation(), children: s === "active" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), n(H.id);
                }, children: "Open" }),
                /* @__PURE__ */ i.jsx("button", { type: "button", onClick: (ee) => {
                  ee.stopPropagation(), X(H.id);
                }, children: "Artifacts" }),
                L ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Ot(t, L, H), children: [
                  /* @__PURE__ */ i.jsx(ut, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(Kn, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
                /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
                  oe(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(Yi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ i.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  de(H);
                }, children: [
                  /* @__PURE__ */ i.jsx(Kn, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          H.id
        ))
      ] }),
      /* @__PURE__ */ i.jsx(
        PN,
        {
          page: c,
          pageSize: l,
          totalCount: y,
          onPageChange: u,
          onPageSizeChange: (H) => {
            d(H), u(1);
          }
        }
      )
    ] }) : null,
    S ? /* @__PURE__ */ i.jsx(
      zN,
      {
        draft: S,
        creating: M,
        ai: t,
        suggestMetadataAction: $,
        onChange: (H) => k(H),
        onClose: () => k(null),
        onSubmit: G
      }
    ) : null
  ] });
}
function VN({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: o }) {
  const r = ue(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ue(() => HN(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ i.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = or(c), l = c ? Ee(c) : wr(a.activityType) ?? a.activityType, d = wr(a.activityType) ?? a.activityType, p = WN(a.startedAt ?? a.scheduledAt), f = fd(a.startedAt, a.completedAt);
    return /* @__PURE__ */ i.jsx("li", { children: /* @__PURE__ */ i.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => o?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ i.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: bs(u) }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-body", children: [
            /* @__PURE__ */ i.jsx("strong", { title: l, children: l }),
            /* @__PURE__ */ i.jsx("small", { title: d, children: d })
          ] }),
          /* @__PURE__ */ i.jsxs("span", { className: "wf-timeline-meta", children: [
            p ? /* @__PURE__ */ i.jsx("time", { children: p }) : null,
            f ? /* @__PURE__ */ i.jsxs("small", { children: [
              "took ",
              f
            ] }) : null
          ] }),
          /* @__PURE__ */ i.jsx(ON, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function ON({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ i.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function HN(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => $c(t.activity) - $c(n.activity) || t.index - n.index).map((t) => t.activity);
}
function $c(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function WN(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function FN({ context: e }) {
  const [t, n] = U("loading"), [o, r] = U(""), [s, a] = U(""), [c, u] = U(""), [l, d] = U([]), p = se(async () => {
    n("loading"), r("");
    try {
      const h = await Kp(e, {
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
    p();
  }, [p]);
  const f = (h) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(h)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsx("button", { type: "button", onClick: () => {
        p();
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
    t === "failed" ? /* @__PURE__ */ i.jsx(po, { message: o }) : null,
    t === "loading" ? /* @__PURE__ */ i.jsx(Cs, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ i.jsx(
      Is,
      {
        icon: /* @__PURE__ */ i.jsx(br, { size: 22 }),
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
          onClick: () => f(h.workflowExecutionId),
          children: [
            /* @__PURE__ */ i.jsxs("span", { children: [
              /* @__PURE__ */ i.jsx("strong", { children: h.workflowExecutionId }),
              /* @__PURE__ */ i.jsx("small", { children: h.artifactId })
            ] }),
            /* @__PURE__ */ i.jsx("span", { children: Ed(h.runKind) }),
            /* @__PURE__ */ i.jsx("span", { children: /* @__PURE__ */ i.jsx(fo, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ i.jsx("span", { children: Je(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ i.jsx("span", { children: fd(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function BN({ context: e, ai: t, workflowExecutionId: n }) {
  const [o, r] = U("loading"), [s, a] = U(""), [c, u] = U(null), [l, d] = U(null), p = Vt(t, "weaver.workflows.explain-instance"), f = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const g = await Up(e, n), [b, w] = await Promise.all([
        Pp(e, g.instance.definitionVersionId).then(
          (m) => ({ definitionVersion: m, error: "" }),
          (m) => ({ definitionVersion: null, error: m instanceof Error ? m.message : String(m) })
        ),
        Qi(e)
      ]);
      u({
        details: g,
        definitionVersion: b.definitionVersion,
        definitionVersionError: b.error,
        activityCatalog: w.activities
      }), d(null), r("ready");
    } catch (g) {
      u(null), a(uN(g, n)), r("failed");
    }
  }, [e, n]);
  Q(() => {
    f();
  }, [f]);
  const h = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
    /* @__PURE__ */ i.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: h, children: [
        /* @__PURE__ */ i.jsx(tr, { size: 14 }),
        " Runs"
      ] }),
      /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => {
        f();
      }, children: [
        /* @__PURE__ */ i.jsx(Yi, { size: 14 }),
        " Refresh"
      ] }),
      c && p ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Ot(t, p, c.details), children: [
        /* @__PURE__ */ i.jsx(ut, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    o === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    o === "failed" ? /* @__PURE__ */ i.jsx(po, { message: s }) : null,
    o === "ready" && c ? /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-detail-workbench", children: [
      /* @__PURE__ */ i.jsx(
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
      /* @__PURE__ */ i.jsx(
        UN,
        {
          ai: t,
          action: p,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? oN(c.definitionVersion, c.activityCatalog) : void 0
        }
      )
    ] }) : null
  ] });
}
function KN({ definitionVersion: e, definitionVersionError: t, activityCatalog: n, details: o, selectedEvidenceId: r, onSelectEvidence: s }) {
  const a = ue(() => {
    if (!e) return { nodes: [], edges: [] };
    const c = e.state.rootActivity;
    if (!c) return { nodes: [], edges: [] };
    const u = n.find((h) => h.activityVersionId === c.activityVersionId), l = Gi(c, u), d = l === "unsupported" ? null : Hn(c, []), p = l === "unsupported" ? ji(c, n, e.layout) : d ? el(d, n, e.layout) : ji(c, n, e.layout), f = p.nodes.map((h) => ({
      ...h,
      draggable: !1,
      connectable: !1,
      deletable: !1
    }));
    return {
      nodes: Qf(f, o.activities, o.incidents, r),
      edges: p.edges.map((h) => ({ ...h, deletable: !1 }))
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
      /* @__PURE__ */ i.jsx(fo, { status: o.instance.status, subStatus: o.instance.subStatus })
    ] }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ i.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ i.jsx("small", { children: lN(t) }) : null
      ] }),
      e && a.nodes.length === 0 ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      a.nodes.length > 0 ? /* @__PURE__ */ i.jsxs(
        Gu,
        {
          nodes: a.nodes,
          edges: a.edges,
          nodeTypes: _d,
          edgeTypes: Dd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (c, u) => s(u.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ i.jsx(Qu, {}),
            /* @__PURE__ */ i.jsx(od, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ i.jsx(td, {})
          ]
        }
      ) : null
    ] })
  ] });
}
function UN({ ai: e, action: t, summary: n, details: o, state: r, error: s, selectedEvidenceId: a = null, onSelectEvidence: c, graphNodeIds: u, activityCatalog: l = [] }) {
  const [d, p] = U("timeline");
  if (!n)
    return /* @__PURE__ */ i.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const f = o?.incidents.length ?? 0, h = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ i.jsx(qi, { size: 14 }), render: () => null },
    { id: "issues", title: f > 0 ? `Issues (${f})` : "Issues", order: 1, icon: /* @__PURE__ */ i.jsx(jt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 2, icon: /* @__PURE__ */ i.jsx(Xi, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ i.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Workflow run details", children: [
    /* @__PURE__ */ i.jsxs("header", { children: [
      /* @__PURE__ */ i.jsxs("div", { children: [
        /* @__PURE__ */ i.jsx("span", { children: "Workflow Instance ID" }),
        /* @__PURE__ */ i.jsx("h3", { children: n.workflowExecutionId })
      ] }),
      t ? /* @__PURE__ */ i.jsxs("button", { type: "button", onClick: () => Ot(e, t, o ?? n), children: [
        /* @__PURE__ */ i.jsx(ut, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tabs", children: /* @__PURE__ */ i.jsx(Jo, { label: "Workflow run tabs", tabs: h, activeTabId: d, onSelect: (g) => p(g) }) }),
    r === "loading" ? /* @__PURE__ */ i.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
    r === "failed" ? /* @__PURE__ */ i.jsx(po, { message: s }) : null,
    r === "ready" && o ? /* @__PURE__ */ i.jsx("div", { className: "wf-instance-tab-content", children: d === "timeline" ? /* @__PURE__ */ i.jsx(
      VN,
      {
        activities: o.activities,
        activityCatalog: l,
        selectedEvidenceId: a,
        onSelectEvidence: c
      }
    ) : d === "issues" ? /* @__PURE__ */ i.jsxs(i.Fragment, { children: [
      /* @__PURE__ */ i.jsx(XN, { incidents: o.incidents, selectedEvidenceId: a, onSelectEvidence: c }),
      /* @__PURE__ */ i.jsx(qN, { details: o, graphNodeIds: u })
    ] }) : /* @__PURE__ */ i.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ i.jsx("dt", { children: "Status" }),
      /* @__PURE__ */ i.jsx("dd", { children: /* @__PURE__ */ i.jsx(fo, { status: n.status, subStatus: n.subStatus }) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Run Kind" }),
      /* @__PURE__ */ i.jsx("dd", { children: Ed(n.runKind) }),
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
      /* @__PURE__ */ i.jsx("dd", { children: Je(n.createdAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Started" }),
      /* @__PURE__ */ i.jsx("dd", { children: Je(n.startedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Completed" }),
      /* @__PURE__ */ i.jsx("dd", { children: Je(n.completedAt) }),
      /* @__PURE__ */ i.jsx("dt", { children: "Correlation" }),
      /* @__PURE__ */ i.jsx("dd", { children: n.correlationId || "None" })
    ] }) }) : null
  ] });
}
function XN({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
function qN({ details: e, graphNodeIds: t }) {
  if (!t) return null;
  const n = new Map(e.activities.map((s) => [s.activityExecutionId, s])), o = e.activities.filter((s) => !t.has(Cc(s))), r = e.incidents.filter((s) => {
    const a = s.activityExecutionId ? n.get(s.activityExecutionId) : null, c = s.executableNodeId ?? (a ? Cc(a) : "");
    return !c || !t.has(c);
  });
  return o.length === 0 && r.length === 0 ? null : /* @__PURE__ */ i.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ i.jsx("h4", { children: "Unmatched runtime evidence" }),
    /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched-list", children: [
      o.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: wr(s.activityType) ?? s.activityType }),
        /* @__PURE__ */ i.jsx("small", { children: s.activityExecutionId })
      ] }, `activity-${s.activityExecutionId}`)),
      r.map((s) => /* @__PURE__ */ i.jsxs("div", { className: "wf-instance-unmatched", children: [
        /* @__PURE__ */ i.jsx("strong", { children: s.failureType }),
        /* @__PURE__ */ i.jsx("small", { children: s.incidentId })
      ] }, `incident-${s.incidentId}`))
    ] })
  ] });
}
function YN({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: o,
  workflowDesignerPanels: r
}) {
  const [s, a] = U(Mc);
  Q(() => {
    const u = () => a(Mc());
    return window.addEventListener("popstate", u), () => window.removeEventListener("popstate", u);
  }, []);
  const c = (u) => {
    const l = u ? `/workflows/definitions?definition=${encodeURIComponent(u)}` : "/workflows/definitions";
    window.history.pushState({}, "", l), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return s ? /* @__PURE__ */ i.jsx(MN, { context: e, definitionId: s, ai: t, propertyEditors: n, expressionEditors: o, workflowDesignerPanels: r, onBack: () => c(null) }) : /* @__PURE__ */ i.jsx(zr, { title: "Definitions", children: /* @__PURE__ */ i.jsx(LN, { context: e, ai: t, onOpen: c }) });
}
function ZN({ context: e, ai: t }) {
  const [n, o] = U(Pc);
  Q(() => {
    const s = () => o(Pc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), o(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ i.jsx(zr, { title: "Executables", children: /* @__PURE__ */ i.jsx(SN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function GN({ context: e, ai: t }) {
  return /* @__PURE__ */ i.jsx(zr, { title: "Runs", children: /* @__PURE__ */ i.jsx(FN, { context: e, ai: t }) });
}
function JN({ context: e, ai: t }) {
  const n = QN();
  return /* @__PURE__ */ i.jsx(zr, { title: "Run", children: /* @__PURE__ */ i.jsx(BN, { context: e, ai: t, workflowExecutionId: n }) });
}
function zr({ title: e, children: t }) {
  return /* @__PURE__ */ i.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ i.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ i.jsxs("div", { children: [
      /* @__PURE__ */ i.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ i.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Mc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Pc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function QN() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function oj(e) {
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
        component: () => /* @__PURE__ */ i.jsx(YN, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list() })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ i.jsx(ZN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ i.jsx(GN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ i.jsx(JN, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ i.jsx(ph, { context: e.backend })
      }
    ]
  });
}
export {
  iN as isConnectEndOverExistingWorkflowNode,
  oj as register,
  sN as resolveConnectEndSource
};
