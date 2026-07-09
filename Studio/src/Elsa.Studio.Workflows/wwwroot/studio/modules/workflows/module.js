import tt, { useMemo as de, useState as B, useEffect as G, memo as Se, forwardRef as $c, useRef as ne, useCallback as oe, useContext as Wn, createContext as Oo, useLayoutEffect as Xd, lazy as Yd, Suspense as qd, useReducer as Ud, useId as Pc } from "react";
import { ListChecks as Zd, Save as Mc, EyeOff as Is, Shield as As, AlertTriangle as $i, SlidersHorizontal as Ho, Activity as Rc, Search as Gi, Check as an, Boxes as Fn, Zap as Gd, Play as Zt, Terminal as Jd, ListTree as Wo, GitBranch as zc, Plus as Gt, Trash2 as An, AlertCircle as wt, Wrench as Qd, Copy as ef, X as Lc, Sparkles as lt, RotateCcw as Fo, ChevronDown as Vc, ChevronRight as yt, GripVertical as Oc, Maximize2 as _n, ChevronUp as tf, Package as Hc, Undo2 as nf, Redo2 as rf, Network as of, Download as sf, ChevronLeft as Dn, Minimize2 as yo, Workflow as Wc, Code2 as af } from "lucide-react";
import { useQuery as Fc, useQueryClient as cf, useMutation as lf } from "@tanstack/react-query";
import { useTablistKeyboard as uf } from "@elsa-workflows/studio-ui";
function df(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var Gr = { exports: {} }, hn = {};
var _s;
function ff() {
  if (_s) return hn;
  _s = 1;
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
  return hn.Fragment = t, hn.jsx = n, hn.jsxs = n, hn;
}
var Ds;
function pf() {
  return Ds || (Ds = 1, Gr.exports = ff()), Gr.exports;
}
var o = pf();
let Bc;
function hf(e) {
  Bc = e;
}
function Ts() {
  return Bc;
}
const gf = "String", yf = "singleline";
function mf(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function Bo(e, t = "Single") {
  return { alias: (e ?? "").trim() || gf, collectionKind: t };
}
function Kc(e) {
  const t = e.type ?? e.Type;
  if (Pi(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: mf(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Pi(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: $s(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: $s(e) ? "Array" : "Single" };
}
function $s(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Ps(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function Ji() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function xf(e, t) {
  const n = new Set(t);
  let i = 1, r = `${e}${i}`;
  for (; n.has(r); )
    i += 1, r = `${e}${i}`;
  return r;
}
function wf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Bo(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function vf(e, t) {
  return { ...e, ...t };
}
function bf(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function Nf(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function jf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Bo(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: yf,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Sf(e, t) {
  return { ...e, ...t };
}
function Cf(e) {
  return {
    referenceKey: Ji(),
    name: e.name,
    type: Bo(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Ef(e, t) {
  return { ...e, ...t };
}
function kf(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Xc(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : kf(e || t);
}
function If(e, t) {
  return Xc(e, t).replace(/StorageDriver$/, "");
}
function Pi(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function Cn(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Af = ["name", "Name"], Yc = ["name", "Name"], _f = ["storageDriverType", "StorageDriverType"], qc = ["referenceKey", "ReferenceKey"], Df = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function Bn(e) {
  return Uc(e, Mf);
}
function Qi(e) {
  return Uc(e, Rf);
}
function Uc(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = mo(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Ni(e.variables, xo)), Array.isArray(e.inputs) && (n.inputs = Ni(e.inputs, xo)), Array.isArray(e.outputs) && (n.outputs = Ni(e.outputs, (i) => Zc(i, !1))), n;
}
function mo(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !nt(i.payload)) return n;
  let r = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Rs(c) ? (s[a] = mo(c, t), r = !0) : Array.isArray(c) && c.length > 0 && c.every(Rs) && (s[a] = c.map((u) => mo(u, t)), r = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Ni(i.payload.variables, xo), r = !0), r ? { ...n, structure: { ...i, payload: s } } : n;
}
function Ni(e, t) {
  return e.map((n) => nt(n) && !Array.isArray(n) ? t(n) : n);
}
function xo(e) {
  return Zc(e, !0);
}
const Tf = [
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
function Zc(e, t) {
  const n = $f(e, Tf);
  return Cn(e, qc).trim() || (n.referenceKey = Ji()), n.type = Kc(e), t && (n.storageDriverType = Pf(e.storageDriverType ?? e.StorageDriverType)), n;
}
function $f(e, t) {
  const n = new Set(t), i = {};
  for (const [r, s] of Object.entries(e))
    n.has(r) || (i[r] = s);
  return i;
}
function Pf(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (nt(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Mf(e) {
  const t = [], n = {};
  for (const [r, s] of Object.entries(e))
    Df.has(r) || (Of(s) ? t.push({
      referenceKey: zf(r),
      value: Vf(s.expression)
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
function Rf(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!nt(i) || typeof i.referenceKey != "string") continue;
    const r = nt(i.value) ? i.value : {};
    n[Lf(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof r.expressionType == "string" ? r.expressionType : "Literal",
        value: r.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function zf(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Lf(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Vf(e) {
  const t = e.type || "Literal";
  return t === "Variable" && nt(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && nt(e.value) ? { value: Ms(e.value), expressionType: "Object" } : { value: Ms(e.value), expressionType: t };
}
function Ms(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Of(e) {
  if (!nt(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return nt(t) && typeof t.type == "string";
}
function Rs(e) {
  return nt(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function nt(e) {
  return typeof e == "object" && e !== null;
}
const Kn = "elsa.sequence.structure", cn = "elsa.flowchart.structure";
function Gc(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const r of t) {
    const s = Wf(i, r.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Hf(e, t, n = (r) => r.nodeId, i) {
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
function Jc(e, t, n) {
  const i = Me(e, n), r = t.at(-1);
  return r ? i.find((s) => s.id === r.slotId) ?? null : i[0] ?? null;
}
function Tn(e, t, n) {
  const i = Gc(e, t, n);
  if (!i) return null;
  const r = Jc(i, t, n);
  return r ? { owner: i, slot: r } : null;
}
function Qc(e, t, n) {
  for (const i of Me(e, n)) {
    const r = i.activities.find((s) => s.nodeId === t);
    if (r) return { slot: i, child: r };
  }
  return null;
}
function Wf(e, t, n) {
  return Qc(e, t, n)?.child ?? null;
}
function Me(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, r = Ko(el(e, t));
  if (r?.kind === n.kind) return Kf(n, r);
  const s = fp(n), a = xn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: pp(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => xn(c) || Ri(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: gp(c),
    property: c,
    cardinality: xn(u) ? "many" : "single",
    mode: "generic",
    activities: xn(u) ?? (Ri(u) ? [u] : [])
  }));
}
function Ko(e) {
  for (const t of e?.designFacets ?? []) {
    if (!it(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !it(t.payload)) continue;
    const r = Ff(t.payload);
    if (r) return { kind: n, schemaVersion: i, payload: r };
  }
  return null;
}
function Ff(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !it(e.initialPayload)) return null;
  const n = e.slots.map(Bf).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Bf(e) {
  if (!it(e)) return null;
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
function Kf(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, r = e.payload[n.collectionProperty];
      return Array.isArray(r) ? r.flatMap((s, a) => {
        if (!it(s)) return [];
        const c = n.labelProperty ? Ft(s[n.labelProperty]) : void 0;
        return [zs(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [zs(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function zs(e, t, n, i, r, s) {
  const a = r === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${r}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: r === void 0 ? t.displayName : Yf(t, r, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Xf(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: r,
    collectionItemLabel: s
  };
}
function Xf(e, t) {
  return t === "many" ? xn(e) ?? [] : Ri(e) ? [e] : [];
}
function Yf(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function el(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function qf(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, r) => it(i) && Ft(i[t.labelProperty]) === t.collectionItemLabel ? r : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function tl(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), r = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = r.get(a.nodeId) ?? hp(e.slot.mode, c);
    return sl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? cl(e.owner) : op(e.slot, s)
  };
}
function nl(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), r = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [sl(e, i, { x: r.x, y: r.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Uf(e, t, n, i = null) {
  const r = new Map(t.map((c) => [c.activityExecutionId, c])), s = Os(t, (c) => c.authoredActivityId || c.executableNodeId), a = Os(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? r.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = ul(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function il(e, t) {
  if (e?.structure?.kind === cn || tp(t)) return "flowchart";
  if (e?.structure?.kind === Kn || np(t)) return "sequence";
  if (e) {
    const n = Me(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Ls(e, t, n, i) {
  return Xo(e, t, i, (r) => {
    const s = Jc(r, t, i);
    return s ? Xn(r, s, n) : r;
  });
}
function Zf(e, t, n, i) {
  return t.length === 0 ? n : Xo(e, t, i, () => n);
}
function Xo(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [r, ...s] = t, a = Qc(e, r.ownerNodeId, n);
  if (!a) return e;
  const c = Xo(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === r.ownerNodeId ? c : l);
  return Xn(e, a.slot, u);
}
function rl(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const r = Me(e, i);
  if (r.length === 0) return e;
  let s = !1, a = e;
  for (const c of r) {
    const u = c.activities.map((l) => {
      const d = rl(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = Xn(a, c, u));
  }
  return s ? a : e;
}
function Xn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const r = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(r)) return e;
    const s = qf(r, t);
    if (s < 0) return e;
    const a = r[s];
    if (!it(a)) return e;
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
function Gf(e, t, n, i = []) {
  const r = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    r.set(a.nodeId, a);
  const s = t.map((a) => r.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), Xn(e.owner, e.slot, s);
}
function Jf(e, t) {
  return {
    ...e,
    structure: rp(e.structure, t)
  };
}
function Qf(e, t) {
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
function wo(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: al(e)
  };
}
function ol(e, t) {
  if (!e) return null;
  const n = el(e, t), i = e.structure ?? (n ? al(n) : null);
  let r = i === e.structure ? e : { ...e, structure: i };
  const s = Me(r, t);
  for (const a of s) {
    const c = a.activities.map((u) => ol(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (r = Xn(r, a, c));
  }
  return r;
}
function Ee(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? ip(t) : n;
}
function sl(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? Ee(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: er(t),
      childSlots: Me(e, t),
      acceptsInbound: sp(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : ll(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function er(e) {
  if (!e) return "activity";
  const t = ep(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ee(e).toLowerCase(), r = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : r.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function ep(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function tp(e) {
  return !!e && (Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function np(e) {
  return !!e && (Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function ip(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function al(e) {
  const t = Ko(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: up(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: Kn,
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
function rp(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const r of n) {
    if (!it(r)) continue;
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
function op(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function cl(e) {
  if (e.structure?.kind !== cn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const r = n.source, s = n.target;
    if (!r?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(dp) : [];
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
function ll(e, t) {
  const n = Vs(e.cases);
  if (cp(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...ji(t?.designFacets),
    ...ji(t?.ports),
    ...ji(t?.outputs)
  ];
  if (i.length > 0) return lp(i);
  const r = Vs(e.outcomes);
  return r.length > 0 ? r.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function sp(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Mi(e, t, n, i) {
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
function ap(e, t, n) {
  const i = Mi(t.source, n, t.sourceHandle ?? "Done", void 0), r = Mi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, r);
}
function xn(e) {
  return Array.isArray(e) ? e.filter(Ri) : null;
}
function cp(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function ji(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!it(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...ji(n.ports));
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
function lp(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Vs(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Ft(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function up(e) {
  return JSON.parse(JSON.stringify(e));
}
function Os(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const r = t(i);
    r && n.set(r, [...n.get(r) ?? [], i]);
  }
  return n;
}
function ul(e) {
  return [...e].sort((t, n) => Hs(n).localeCompare(Hs(t)))[0];
}
function Hs(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function dp(e) {
  return it(e) && typeof e.x == "number" && typeof e.y == "number";
}
function it(e) {
  return typeof e == "object" && e !== null;
}
function Ri(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function fp(e) {
  return e.kind === Kn ? "sequence" : e.kind === cn ? "flowchart" : "generic";
}
function pp(e) {
  return e.kind === Kn || e.kind === cn, "Activities";
}
function hp(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function gp(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const zi = "workflow", yp = /* @__PURE__ */ new Set([Kn, cn]);
function mp(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (yp.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? Ko(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function dl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Pi) : [];
}
function xp(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function wp(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== zi ? t : zi
  };
}
function fl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return fl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function vp(e, t) {
  if (!e) return "";
  const n = [`workflow:${Ws(e.variables)}`], i = (r) => {
    const s = Me(r, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${r.nodeId}:${Ws(dl(r))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Ws(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function bp(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const je = "/_elsa/workflow-management", Np = "/publishing", En = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"]
};
function jp(e) {
  return Fc({
    queryKey: En.activityAvailabilitySettings,
    queryFn: () => Bp(e)
  });
}
function Sp(e) {
  return Fc({
    queryKey: En.activityAvailabilityDiagnostics,
    queryFn: () => gl(e)
  });
}
function Cp(e) {
  const t = cf();
  return lf({
    mutationFn: (n) => Kp(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: En.activityAvailabilitySettings }), t.invalidateQueries({ queryKey: En.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: En.activities });
    }
  });
}
async function Ep(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${je}/definitions?${n.toString()}`);
}
async function kp(e, t) {
  const n = await e.http.getJson(`${je}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: Qi(n.draft.state) } } : n;
}
async function Ip(e, t, n) {
  const i = await e.http.postJson(
    `${je}/design/scoped-variables/analyze`,
    { state: Bn(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const Jr = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function Ap(e, t, n, i) {
  const r = de(() => vp(t, i), [i, t]), [s, a] = B(() => Jr("loading"));
  return G(() => {
    if (!t) {
      a(Jr("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Ip(e, t, n).then(
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
async function _p(e, t) {
  const n = await e.http.getJson(`${je}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: Qi(n.state) };
}
async function Dp(e, t) {
  return e.http.postJson(`${je}/definitions`, t);
}
async function Tp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}`);
}
async function $p(e, t) {
  await e.http.postJson(`${je}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Pp(e, t) {
  await e.http.deleteJson(`${je}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Mp(e, t, n) {
  return e.http.requestJson(
    `${je}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Rp(e, t) {
  const n = await e.http.putJson(
    `${je}/drafts/${encodeURIComponent(t.id)}`,
    { state: Bn(t.state), layout: t.layout }
  );
  return { ...n, state: Qi(n.state) };
}
async function zp(e, t) {
  return e.http.postJson(`${je}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Lp(e, t) {
  return e.http.postJson(`${je}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Vp(e, t) {
  const n = { ...t, state: Bn(t.state) };
  try {
    return await e.http.postJson(`${Np}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const r = Jp(i);
    if (r) return r;
    throw i;
  }
}
async function pl(e, t) {
  return e.http.postJson(`${je}/executables/${encodeURIComponent(t)}/run`, {});
}
async function hl(e) {
  const t = [`${je}/executables`, "/_demo/workflows/executables"], n = [];
  for (const i of t)
    try {
      const r = await e.http.getJson(i);
      return Op(r);
    } catch (r) {
      n.push(r);
    }
  if (n.length > 0 && n.every(Fs)) return [];
  throw n.find((i) => !Fs(i)) ?? n[n.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Op(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Fs(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Hp(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Wp(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Fp(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function Yo(e) {
  return e.http.getJson(`${je}/activities`);
}
async function Bp(e) {
  return e.http.getJson(`${je}/activities/availability/settings`);
}
async function Kp(e, t) {
  return e.http.putJson(`${je}/activities/availability/settings`, t);
}
async function gl(e) {
  return e.http.getJson(`${je}/activities/availability/diagnostics`);
}
async function Xp(e) {
  const t = await tr(e, [
    `${je}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Bs(t) : Bs(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Yp(e) {
  const t = await tr(e, [
    `${je}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Si;
}
async function qp(e) {
  const t = await tr(e, [
    `${je}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Up(i));
}
function Up(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Zp(e) {
  const t = await tr(e, [
    `${je}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Gp(i));
}
function Gp(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function tr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (r) {
      n = r;
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
function Jp(e) {
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
const Si = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
], Qp = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], eh = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};
function jt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? Qp[e] ?? "Available" : "Available";
}
function Li(e) {
  const t = jt(e);
  return eh[t] ?? t;
}
function th(e) {
  return jt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function yl(e) {
  return jt(e) !== "Available";
}
function nh(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function ih(e) {
  return e === "Only" ? 1 : 0;
}
function Xs(e) {
  const t = e?.rules;
  return {
    mode: nh(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function rh(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function oh(e) {
  return [...e?.items ?? []].filter(rh).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Vi(t).localeCompare(Vi(n)));
}
function sh(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = jt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function Ys(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Vi(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = ch(e?.activityTypeKey);
  return ah(n) || t || e?.activityTypeKey || "Activity";
}
function qs(e) {
  const t = ml(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function Us(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !yl(e.state) ? "" : n;
}
function ah(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function ch(e) {
  const t = ml(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function ml(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function lh(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => yl(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((r) => r && n.has(r)) : !1) ?? null;
}
function uh({ context: e }) {
  const t = jp(e), n = Sp(e), i = Cp(e), r = t.data ?? null, s = n.data ?? null, a = t.isLoading || n.isLoading, c = i.isPending, [u, l] = B(() => Xs(r)), [d, f] = B(""), [p, h] = B(null);
  G(() => {
    l(Xs(r));
  }, [r]);
  const y = de(() => oh(s), [s]), w = de(() => sh(s), [s]), x = s?.sets ?? [], m = de(() => {
    const I = d.trim().toLowerCase();
    return I ? y.filter((R) => [
      Vi(R),
      qs(R),
      Us(R),
      R.activityTypeKey,
      R.category
    ].some((C) => (C ?? "").toLowerCase().includes(I))) : y;
  }, [y, d]), b = new Set(u.activityTypes), g = new Set(u.sets), v = y.filter((I) => jt(I.state) === "BlockedByHostBaseline").length, j = y.filter((I) => jt(I.state) === "HiddenByManagementSettings").length, N = i.error ?? t.error ?? n.error, S = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, k = (I) => l((R) => ({ ...R, mode: I })), D = (I) => l((R) => ({ ...R, activityTypes: Ys(R.activityTypes, I) })), M = (I) => l((R) => ({ ...R, sets: Ys(R.sets, I) })), A = () => {
    h(null), i.mutate(
      {
        scope: r?.scope ?? "host-default",
        mode: ih(u.mode),
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
          /* @__PURE__ */ o.jsx(Zd, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "availability-save", onClick: A, disabled: a || c, children: [
        /* @__PURE__ */ o.jsx(Mc, { size: 15 }),
        c ? "Saving…" : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "availability-body", children: [
      S && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-error", children: S }),
      p && !S && /* @__PURE__ */ o.jsx("div", { className: "availability-banner availability-banner-success", children: p }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: u.mode === "AllExcept" ? "active" : "", onClick: () => k("AllExcept"), disabled: a || c, children: [
          /* @__PURE__ */ o.jsx(Is, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ o.jsx("em", { children: "Show everything except the selected activities" })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", className: u.mode === "Only" ? "active" : "", onClick: () => k("Only"), disabled: a || c, children: [
          /* @__PURE__ */ o.jsx(As, { size: 15 }),
          /* @__PURE__ */ o.jsxs("span", { children: [
            /* @__PURE__ */ o.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ o.jsx("em", { children: "Show only the selected activities" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(As, { size: 14 }),
          " ",
          v,
          " host blocked"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx(Is, { size: 14 }),
          " ",
          j,
          " management hidden"
        ] }),
        /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx($i, { size: 14 }),
          " ",
          w.length,
          " unresolved"
        ] })
      ] }),
      x.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx(Ho, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-set-list", children: x.map((I) => /* @__PURE__ */ o.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ o.jsx("input", { type: "checkbox", checked: g.has(I.name), disabled: a || c, onChange: () => M(I.name) }),
          /* @__PURE__ */ o.jsx("span", { children: I.name }),
          /* @__PURE__ */ o.jsx("code", { children: (I.activityTypeKeys ?? []).length })
        ] }, I.name)) })
      ] }),
      /* @__PURE__ */ o.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ o.jsx(Rc, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-search availability-search", children: [
            /* @__PURE__ */ o.jsx(Gi, { size: 14 }),
            /* @__PURE__ */ o.jsx("input", { type: "search", value: d, placeholder: "Filter activities…", onChange: (I) => f(I.target.value) })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { className: "availability-activity-list", children: [
          a && y.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
          !a && y.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
          !a && y.length > 0 && m.length === 0 && /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "No activities match the filter." }),
          m.map((I) => {
            const C = jt(I.state) === "BlockedByHostBaseline", _ = I.activityTypeKey ?? I.activityDefinitionId ?? "", E = Vi(I), T = qs(I), P = Us(I);
            return /* @__PURE__ */ o.jsxs("label", { className: `availability-activity-option ${C ? "disabled" : ""}`, children: [
              /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: b.has(_),
                  disabled: a || c || C,
                  onChange: () => D(_)
                }
              ),
              /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-main", children: [
                /* @__PURE__ */ o.jsxs("span", { className: "availability-activity-title-line", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: E }),
                  I.category && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-category", children: I.category })
                ] }),
                P && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-description", children: P }),
                T && /* @__PURE__ */ o.jsx("span", { className: "availability-activity-meta", title: I.activityTypeKey ?? void 0, children: /* @__PURE__ */ o.jsx("code", { children: T }) })
              ] }),
              /* @__PURE__ */ o.jsx("em", { className: `availability-state ${th(I.state)}`, children: Li(I.state) })
            ] }, _);
          })
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ o.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ o.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ o.jsx($i, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ o.jsx("div", { className: "availability-unresolved-list", children: w.map((I) => /* @__PURE__ */ o.jsxs("span", { children: [
          /* @__PURE__ */ o.jsx("strong", { children: I.referenceName }),
          /* @__PURE__ */ o.jsx("em", { children: Li(I.state) })
        ] }, `${I.layer}-${I.referenceKind}-${I.referenceName}`)) })
      ] })
    ] })
  ] });
}
function ke(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = ke(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var dh = { value: () => {
} };
function nr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Ci(n);
}
function Ci(e) {
  this._ = e;
}
function fh(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", r = n.indexOf(".");
    if (r >= 0 && (i = n.slice(r + 1), n = n.slice(0, r)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Ci.prototype = nr.prototype = {
  constructor: Ci,
  on: function(e, t) {
    var n = this._, i = fh(e + "", n), r, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((r = (e = i[s]).type) && (r = ph(n[r], e.name))) return r;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (r = (e = i[s]).type) n[r] = Zs(n[r], e.name, t);
      else if (t == null) for (r in n) n[r] = Zs(n[r], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Ci(e);
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
function ph(e, t) {
  for (var n = 0, i = e.length, r; n < i; ++n)
    if ((r = e[n]).name === t)
      return r.value;
}
function Zs(e, t, n) {
  for (var i = 0, r = e.length; i < r; ++i)
    if (e[i].name === t) {
      e[i] = dh, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var vo = "http://www.w3.org/1999/xhtml";
const Gs = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: vo,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function ir(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Gs.hasOwnProperty(t) ? { space: Gs[t], local: e } : e;
}
function hh(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === vo && t.documentElement.namespaceURI === vo ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function gh(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function xl(e) {
  var t = ir(e);
  return (t.local ? gh : hh)(t);
}
function yh() {
}
function qo(e) {
  return e == null ? yh : function() {
    return this.querySelector(e);
  };
}
function mh(e) {
  typeof e != "function" && (e = qo(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(i, this._parents);
}
function xh(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function wh() {
  return [];
}
function wl(e) {
  return e == null ? wh : function() {
    return this.querySelectorAll(e);
  };
}
function vh(e) {
  return function() {
    return xh(e.apply(this, arguments));
  };
}
function bh(e) {
  typeof e == "function" ? e = vh(e) : e = wl(e);
  for (var t = this._groups, n = t.length, i = [], r = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), r.push(u));
  return new Be(i, r);
}
function vl(e) {
  return function() {
    return this.matches(e);
  };
}
function bl(e) {
  return function(t) {
    return t.matches(e);
  };
}
var Nh = Array.prototype.find;
function jh(e) {
  return function() {
    return Nh.call(this.children, e);
  };
}
function Sh() {
  return this.firstElementChild;
}
function Ch(e) {
  return this.select(e == null ? Sh : jh(typeof e == "function" ? e : bl(e)));
}
var Eh = Array.prototype.filter;
function kh() {
  return Array.from(this.children);
}
function Ih(e) {
  return function() {
    return Eh.call(this.children, e);
  };
}
function Ah(e) {
  return this.selectAll(e == null ? kh : Ih(typeof e == "function" ? e : bl(e)));
}
function _h(e) {
  typeof e != "function" && (e = vl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Be(i, this._parents);
}
function Nl(e) {
  return new Array(e.length);
}
function Dh() {
  return new Be(this._enter || this._groups.map(Nl), this._parents);
}
function Oi(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
Oi.prototype = {
  constructor: Oi,
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
function Th(e) {
  return function() {
    return e;
  };
}
function $h(e, t, n, i, r, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new Oi(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (r[a] = c);
}
function Ph(e, t, n, i, r, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? r[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new Oi(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (r[c] = u);
}
function Mh(e) {
  return e.__data__;
}
function Rh(e, t) {
  if (!arguments.length) return Array.from(this, Mh);
  var n = t ? Ph : $h, i = this._parents, r = this._groups;
  typeof e != "function" && (e = Th(e));
  for (var s = r.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = r[l], p = f.length, h = zh(e.call(d, d && d.__data__, l, i)), y = h.length, w = c[l] = new Array(y), x = a[l] = new Array(y), m = u[l] = new Array(p);
    n(d, f, w, x, m, h, t);
    for (var b = 0, g = 0, v, j; b < y; ++b)
      if (v = w[b]) {
        for (b >= g && (g = b + 1); !(j = x[g]) && ++g < y; ) ;
        v._next = j || null;
      }
  }
  return a = new Be(a, i), a._enter = c, a._exit = u, a;
}
function zh(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Lh() {
  return new Be(this._exit || this._groups.map(Nl), this._parents);
}
function Vh(e, t, n) {
  var i = this.enter(), r = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (r = t(r), r && (r = r.selection())), n == null ? s.remove() : n(s), i && r ? i.merge(r).order() : r;
}
function Oh(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, r = n.length, s = i.length, a = Math.min(r, s), c = new Array(r), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, y = 0; y < f; ++y)
      (h = l[y] || d[y]) && (p[y] = h);
  for (; u < r; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function Hh() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], r = i.length - 1, s = i[r], a; --r >= 0; )
      (a = i[r]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function Wh(e) {
  e || (e = Fh);
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
function Fh(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Bh() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Kh() {
  return Array.from(this);
}
function Xh() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length; r < s; ++r) {
      var a = i[r];
      if (a) return a;
    }
  return null;
}
function Yh() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function qh() {
  return !this.node();
}
function Uh(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var r = t[n], s = 0, a = r.length, c; s < a; ++s)
      (c = r[s]) && e.call(c, c.__data__, s, r);
  return this;
}
function Zh(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Gh(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Jh(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Qh(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function eg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function tg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function ng(e, t) {
  var n = ir(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Gh : Zh : typeof t == "function" ? n.local ? tg : eg : n.local ? Qh : Jh)(n, t));
}
function jl(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function ig(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function rg(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function og(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function sg(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? ig : typeof t == "function" ? og : rg)(e, t, n ?? "")) : Jt(this.node(), e);
}
function Jt(e, t) {
  return e.style.getPropertyValue(t) || jl(e).getComputedStyle(e, null).getPropertyValue(t);
}
function ag(e) {
  return function() {
    delete this[e];
  };
}
function cg(e, t) {
  return function() {
    this[e] = t;
  };
}
function lg(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function ug(e, t) {
  return arguments.length > 1 ? this.each((t == null ? ag : typeof t == "function" ? lg : cg)(e, t)) : this.node()[e];
}
function Sl(e) {
  return e.trim().split(/^|\s+/);
}
function Uo(e) {
  return e.classList || new Cl(e);
}
function Cl(e) {
  this._node = e, this._names = Sl(e.getAttribute("class") || "");
}
Cl.prototype = {
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
function El(e, t) {
  for (var n = Uo(e), i = -1, r = t.length; ++i < r; ) n.add(t[i]);
}
function kl(e, t) {
  for (var n = Uo(e), i = -1, r = t.length; ++i < r; ) n.remove(t[i]);
}
function dg(e) {
  return function() {
    El(this, e);
  };
}
function fg(e) {
  return function() {
    kl(this, e);
  };
}
function pg(e, t) {
  return function() {
    (t.apply(this, arguments) ? El : kl)(this, e);
  };
}
function hg(e, t) {
  var n = Sl(e + "");
  if (arguments.length < 2) {
    for (var i = Uo(this.node()), r = -1, s = n.length; ++r < s; ) if (!i.contains(n[r])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? pg : t ? dg : fg)(n, t));
}
function gg() {
  this.textContent = "";
}
function yg(e) {
  return function() {
    this.textContent = e;
  };
}
function mg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function xg(e) {
  return arguments.length ? this.each(e == null ? gg : (typeof e == "function" ? mg : yg)(e)) : this.node().textContent;
}
function wg() {
  this.innerHTML = "";
}
function vg(e) {
  return function() {
    this.innerHTML = e;
  };
}
function bg(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function Ng(e) {
  return arguments.length ? this.each(e == null ? wg : (typeof e == "function" ? bg : vg)(e)) : this.node().innerHTML;
}
function jg() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function Sg() {
  return this.each(jg);
}
function Cg() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function Eg() {
  return this.each(Cg);
}
function kg(e) {
  var t = typeof e == "function" ? e : xl(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function Ig() {
  return null;
}
function Ag(e, t) {
  var n = typeof e == "function" ? e : xl(e), i = t == null ? Ig : typeof t == "function" ? t : qo(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function _g() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function Dg() {
  return this.each(_g);
}
function Tg() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function $g() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function Pg(e) {
  return this.select(e ? $g : Tg);
}
function Mg(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function Rg(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function zg(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Lg(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, r = t.length, s; n < r; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function Vg(e, t, n) {
  return function() {
    var i = this.__on, r, s = Rg(t);
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
function Og(e, t, n) {
  var i = zg(e + ""), r, s = i.length, a;
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
  for (c = t ? Vg : Lg, r = 0; r < s; ++r) this.each(c(i[r], t, n));
  return this;
}
function Il(e, t, n) {
  var i = jl(e), r = i.CustomEvent;
  typeof r == "function" ? r = new r(t, n) : (r = i.document.createEvent("Event"), n ? (r.initEvent(t, n.bubbles, n.cancelable), r.detail = n.detail) : r.initEvent(t, !1, !1)), e.dispatchEvent(r);
}
function Hg(e, t) {
  return function() {
    return Il(this, e, t);
  };
}
function Wg(e, t) {
  return function() {
    return Il(this, e, t.apply(this, arguments));
  };
}
function Fg(e, t) {
  return this.each((typeof t == "function" ? Wg : Hg)(e, t));
}
function* Bg() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], r = 0, s = i.length, a; r < s; ++r)
      (a = i[r]) && (yield a);
}
var Al = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function Yn() {
  return new Be([[document.documentElement]], Al);
}
function Kg() {
  return this;
}
Be.prototype = Yn.prototype = {
  constructor: Be,
  select: mh,
  selectAll: bh,
  selectChild: Ch,
  selectChildren: Ah,
  filter: _h,
  data: Rh,
  enter: Dh,
  exit: Lh,
  join: Vh,
  merge: Oh,
  selection: Kg,
  order: Hh,
  sort: Wh,
  call: Bh,
  nodes: Kh,
  node: Xh,
  size: Yh,
  empty: qh,
  each: Uh,
  attr: ng,
  style: sg,
  property: ug,
  classed: hg,
  text: xg,
  html: Ng,
  raise: Sg,
  lower: Eg,
  append: kg,
  insert: Ag,
  remove: Dg,
  clone: Pg,
  datum: Mg,
  on: Og,
  dispatch: Fg,
  [Symbol.iterator]: Bg
};
function We(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], Al);
}
function Xg(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function qe(e, t) {
  if (e = Xg(e), t === void 0 && (t = e.currentTarget), t) {
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
const Yg = { passive: !1 }, $n = { capture: !0, passive: !1 };
function Qr(e) {
  e.stopImmediatePropagation();
}
function qt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function _l(e) {
  var t = e.document.documentElement, n = We(e).on("dragstart.drag", qt, $n);
  "onselectstart" in t ? n.on("selectstart.drag", qt, $n) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Dl(e, t) {
  var n = e.document.documentElement, i = We(e).on("dragstart.drag", null);
  t && (i.on("click.drag", qt, $n), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const di = (e) => () => e;
function bo(e, {
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
bo.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function qg(e) {
  return !e.ctrlKey && !e.button;
}
function Ug() {
  return this.parentNode;
}
function Zg(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Gg() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Tl() {
  var e = qg, t = Ug, n = Zg, i = Gg, r = {}, s = nr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(v) {
    v.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", m, Yg).on("touchend.drag touchcancel.drag", b).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(v, j) {
    if (!(d || !e.call(this, v, j))) {
      var N = g(this, t.call(this, v, j), v, j, "mouse");
      N && (We(v.view).on("mousemove.drag", y, $n).on("mouseup.drag", w, $n), _l(v.view), Qr(v), l = !1, c = v.clientX, u = v.clientY, N("start", v));
    }
  }
  function y(v) {
    if (qt(v), !l) {
      var j = v.clientX - c, N = v.clientY - u;
      l = j * j + N * N > f;
    }
    r.mouse("drag", v);
  }
  function w(v) {
    We(v.view).on("mousemove.drag mouseup.drag", null), Dl(v.view, l), qt(v), r.mouse("end", v);
  }
  function x(v, j) {
    if (e.call(this, v, j)) {
      var N = v.changedTouches, S = t.call(this, v, j), k = N.length, D, M;
      for (D = 0; D < k; ++D)
        (M = g(this, S, v, j, N[D].identifier, N[D])) && (Qr(v), M("start", v, N[D]));
    }
  }
  function m(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (qt(v), k("drag", v, j[S]));
  }
  function b(v) {
    var j = v.changedTouches, N = j.length, S, k;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), S = 0; S < N; ++S)
      (k = r[j[S].identifier]) && (Qr(v), k("end", v, j[S]));
  }
  function g(v, j, N, S, k, D) {
    var M = s.copy(), A = qe(D || N, j), I, R, C;
    if ((C = n.call(v, new bo("beforestart", {
      sourceEvent: N,
      target: p,
      identifier: k,
      active: a,
      x: A[0],
      y: A[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), S)) != null)
      return I = C.x - A[0] || 0, R = C.y - A[1] || 0, function _(E, T, P) {
        var $ = A, F;
        switch (E) {
          case "start":
            r[k] = _, F = a++;
            break;
          case "end":
            delete r[k], --a;
          // falls through
          case "drag":
            A = qe(P || T, j), F = a;
            break;
        }
        M.call(
          E,
          v,
          new bo(E, {
            sourceEvent: T,
            subject: C,
            target: p,
            identifier: k,
            active: F,
            x: A[0] + I,
            y: A[1] + R,
            dx: A[0] - $[0],
            dy: A[1] - $[1],
            dispatch: M
          }),
          S
        );
      };
  }
  return p.filter = function(v) {
    return arguments.length ? (e = typeof v == "function" ? v : di(!!v), p) : e;
  }, p.container = function(v) {
    return arguments.length ? (t = typeof v == "function" ? v : di(v), p) : t;
  }, p.subject = function(v) {
    return arguments.length ? (n = typeof v == "function" ? v : di(v), p) : n;
  }, p.touchable = function(v) {
    return arguments.length ? (i = typeof v == "function" ? v : di(!!v), p) : i;
  }, p.on = function() {
    var v = s.on.apply(s, arguments);
    return v === s ? p : v;
  }, p.clickDistance = function(v) {
    return arguments.length ? (f = (v = +v) * v, p) : Math.sqrt(f);
  }, p;
}
function Zo(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function $l(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function qn() {
}
var Pn = 0.7, Hi = 1 / Pn, Ut = "\\s*([+-]?\\d+)\\s*", Mn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", et = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Jg = /^#([0-9a-f]{3,8})$/, Qg = new RegExp(`^rgb\\(${Ut},${Ut},${Ut}\\)$`), ey = new RegExp(`^rgb\\(${et},${et},${et}\\)$`), ty = new RegExp(`^rgba\\(${Ut},${Ut},${Ut},${Mn}\\)$`), ny = new RegExp(`^rgba\\(${et},${et},${et},${Mn}\\)$`), iy = new RegExp(`^hsl\\(${Mn},${et},${et}\\)$`), ry = new RegExp(`^hsla\\(${Mn},${et},${et},${Mn}\\)$`), Js = {
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
Zo(qn, kt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Qs,
  // Deprecated! Use color.formatHex.
  formatHex: Qs,
  formatHex8: oy,
  formatHsl: sy,
  formatRgb: ea,
  toString: ea
});
function Qs() {
  return this.rgb().formatHex();
}
function oy() {
  return this.rgb().formatHex8();
}
function sy() {
  return Pl(this).formatHsl();
}
function ea() {
  return this.rgb().formatRgb();
}
function kt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Jg.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? ta(t) : n === 3 ? new Ve(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? fi(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? fi(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Qg.exec(e)) ? new Ve(t[1], t[2], t[3], 1) : (t = ey.exec(e)) ? new Ve(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = ty.exec(e)) ? fi(t[1], t[2], t[3], t[4]) : (t = ny.exec(e)) ? fi(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = iy.exec(e)) ? ra(t[1], t[2] / 100, t[3] / 100, 1) : (t = ry.exec(e)) ? ra(t[1], t[2] / 100, t[3] / 100, t[4]) : Js.hasOwnProperty(e) ? ta(Js[e]) : e === "transparent" ? new Ve(NaN, NaN, NaN, 0) : null;
}
function ta(e) {
  return new Ve(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function fi(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new Ve(e, t, n, i);
}
function ay(e) {
  return e instanceof qn || (e = kt(e)), e ? (e = e.rgb(), new Ve(e.r, e.g, e.b, e.opacity)) : new Ve();
}
function No(e, t, n, i) {
  return arguments.length === 1 ? ay(e) : new Ve(e, t, n, i ?? 1);
}
function Ve(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
Zo(Ve, No, $l(qn, {
  brighter(e) {
    return e = e == null ? Hi : Math.pow(Hi, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Ve(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new Ve(St(this.r), St(this.g), St(this.b), Wi(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: na,
  // Deprecated! Use color.formatHex.
  formatHex: na,
  formatHex8: cy,
  formatRgb: ia,
  toString: ia
}));
function na() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}`;
}
function cy() {
  return `#${Nt(this.r)}${Nt(this.g)}${Nt(this.b)}${Nt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function ia() {
  const e = Wi(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${St(this.r)}, ${St(this.g)}, ${St(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function Wi(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function St(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Nt(e) {
  return e = St(e), (e < 16 ? "0" : "") + e.toString(16);
}
function ra(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ue(e, t, n, i);
}
function Pl(e) {
  if (e instanceof Ue) return new Ue(e.h, e.s, e.l, e.opacity);
  if (e instanceof qn || (e = kt(e)), !e) return new Ue();
  if (e instanceof Ue) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, r = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - r, u = (s + r) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + r : 2 - s - r, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ue(a, c, u, e.opacity);
}
function ly(e, t, n, i) {
  return arguments.length === 1 ? Pl(e) : new Ue(e, t, n, i ?? 1);
}
function Ue(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
Zo(Ue, ly, $l(qn, {
  brighter(e) {
    return e = e == null ? Hi : Math.pow(Hi, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Pn : Math.pow(Pn, e), new Ue(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, r = 2 * n - i;
    return new Ve(
      eo(e >= 240 ? e - 240 : e + 120, r, i),
      eo(e, r, i),
      eo(e < 120 ? e + 240 : e - 120, r, i),
      this.opacity
    );
  },
  clamp() {
    return new Ue(oa(this.h), pi(this.s), pi(this.l), Wi(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = Wi(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${oa(this.h)}, ${pi(this.s) * 100}%, ${pi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function oa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function pi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function eo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const Go = (e) => () => e;
function uy(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function dy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function fy(e) {
  return (e = +e) == 1 ? Ml : function(t, n) {
    return n - t ? dy(t, n, e) : Go(isNaN(t) ? n : t);
  };
}
function Ml(e, t) {
  var n = t - e;
  return n ? uy(e, n) : Go(isNaN(e) ? t : e);
}
const Fi = (function e(t) {
  var n = fy(t);
  function i(r, s) {
    var a = n((r = No(r)).r, (s = No(s)).r), c = n(r.g, s.g), u = n(r.b, s.b), l = Ml(r.opacity, s.opacity);
    return function(d) {
      return r.r = a(d), r.g = c(d), r.b = u(d), r.opacity = l(d), r + "";
    };
  }
  return i.gamma = e, i;
})(1);
function py(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), r;
  return function(s) {
    for (r = 0; r < n; ++r) i[r] = e[r] * (1 - s) + t[r] * s;
    return i;
  };
}
function hy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function gy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, r = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) r[a] = kn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = r[a](c);
    return s;
  };
}
function yy(e, t) {
  var n = /* @__PURE__ */ new Date();
  return e = +e, t = +t, function(i) {
    return n.setTime(e * (1 - i) + t * i), n;
  };
}
function Qe(e, t) {
  return e = +e, t = +t, function(n) {
    return e * (1 - n) + t * n;
  };
}
function my(e, t) {
  var n = {}, i = {}, r;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (r in t)
    r in e ? n[r] = kn(e[r], t[r]) : i[r] = t[r];
  return function(s) {
    for (r in n) i[r] = n[r](s);
    return i;
  };
}
var jo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, to = new RegExp(jo.source, "g");
function xy(e) {
  return function() {
    return e;
  };
}
function wy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Rl(e, t) {
  var n = jo.lastIndex = to.lastIndex = 0, i, r, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = jo.exec(e)) && (r = to.exec(t)); )
    (s = r.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (r = r[0]) ? c[a] ? c[a] += r : c[++a] = r : (c[++a] = null, u.push({ i: a, x: Qe(i, r) })), n = to.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? wy(u[0].x) : xy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function kn(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? Go(t) : (n === "number" ? Qe : n === "string" ? (i = kt(t)) ? (t = i, Fi) : Rl : t instanceof kt ? Fi : t instanceof Date ? yy : hy(t) ? py : Array.isArray(t) ? gy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? my : Qe)(e, t);
}
var sa = 180 / Math.PI, So = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function zl(e, t, n, i, r, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: r,
    translateY: s,
    rotate: Math.atan2(t, e) * sa,
    skewX: Math.atan(u) * sa,
    scaleX: a,
    scaleY: c
  };
}
var hi;
function vy(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? So : zl(t.a, t.b, t.c, t.d, t.e, t.f);
}
function by(e) {
  return e == null || (hi || (hi = document.createElementNS("http://www.w3.org/2000/svg", "g")), hi.setAttribute("transform", e), !(e = hi.transform.baseVal.consolidate())) ? So : (e = e.matrix, zl(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Ll(e, t, n, i) {
  function r(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push("translate(", null, t, null, n);
      y.push({ i: w - 4, x: Qe(l, f) }, { i: w - 2, x: Qe(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(r(f) + "rotate(", null, i) - 2, x: Qe(l, d) })) : d && f.push(r(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(r(f) + "skewX(", null, i) - 2, x: Qe(l, d) }) : d && f.push(r(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, y) {
    if (l !== f || d !== p) {
      var w = h.push(r(h) + "scale(", null, ",", null, ")");
      y.push({ i: w - 4, x: Qe(l, f) }, { i: w - 2, x: Qe(d, p) });
    } else (f !== 1 || p !== 1) && h.push(r(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var y = -1, w = p.length, x; ++y < w; ) f[(x = p[y]).i] = x.x(h);
      return f.join("");
    };
  };
}
var Ny = Ll(vy, "px, ", "px)", "deg)"), jy = Ll(by, ", ", ")", ")"), Sy = 1e-12;
function aa(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function Cy(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function Ey(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ei = (function e(t, n, i) {
  function r(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, y = f - u, w = h * h + y * y, x, m;
    if (w < Sy)
      m = Math.log(p / l) / t, x = function(S) {
        return [
          c + S * h,
          u + S * y,
          l * Math.exp(t * S * m)
        ];
      };
    else {
      var b = Math.sqrt(w), g = (p * p - l * l + i * w) / (2 * l * n * b), v = (p * p - l * l - i * w) / (2 * p * n * b), j = Math.log(Math.sqrt(g * g + 1) - g), N = Math.log(Math.sqrt(v * v + 1) - v);
      m = (N - j) / t, x = function(S) {
        var k = S * m, D = aa(j), M = l / (n * b) * (D * Ey(t * k + j) - Cy(j));
        return [
          c + M * h,
          u + M * y,
          l * D / aa(t * k + j)
        ];
      };
    }
    return x.duration = m * 1e3 * t / Math.SQRT2, x;
  }
  return r.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, r;
})(Math.SQRT2, 2, 4);
var Qt = 0, wn = 0, gn = 0, Vl = 1e3, Bi, vn, Ki = 0, It = 0, rr = 0, Rn = typeof performance == "object" && performance.now ? performance : Date, Ol = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Jo() {
  return It || (Ol(ky), It = Rn.now() + rr);
}
function ky() {
  It = 0;
}
function Xi() {
  this._call = this._time = this._next = null;
}
Xi.prototype = Hl.prototype = {
  constructor: Xi,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Jo() : +n) + (t == null ? 0 : +t), !this._next && vn !== this && (vn ? vn._next = this : Bi = this, vn = this), this._call = e, this._time = n, Co();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, Co());
  }
};
function Hl(e, t, n) {
  var i = new Xi();
  return i.restart(e, t, n), i;
}
function Iy() {
  Jo(), ++Qt;
  for (var e = Bi, t; e; )
    (t = It - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --Qt;
}
function ca() {
  It = (Ki = Rn.now()) + rr, Qt = wn = 0;
  try {
    Iy();
  } finally {
    Qt = 0, _y(), It = 0;
  }
}
function Ay() {
  var e = Rn.now(), t = e - Ki;
  t > Vl && (rr -= t, Ki = e);
}
function _y() {
  for (var e, t = Bi, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : Bi = n);
  vn = e, Co(i);
}
function Co(e) {
  if (!Qt) {
    wn && (wn = clearTimeout(wn));
    var t = e - It;
    t > 24 ? (e < 1 / 0 && (wn = setTimeout(ca, e - Rn.now() - rr)), gn && (gn = clearInterval(gn))) : (gn || (Ki = Rn.now(), gn = setInterval(Ay, Vl)), Qt = 1, Ol(ca));
  }
}
function la(e, t, n) {
  var i = new Xi();
  return t = t == null ? 0 : +t, i.restart((r) => {
    i.stop(), e(r + t);
  }, t, n), i;
}
var Dy = nr("start", "end", "cancel", "interrupt"), Ty = [], Wl = 0, ua = 1, Eo = 2, ki = 3, da = 4, ko = 5, Ii = 6;
function or(e, t, n, i, r, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  $y(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: r,
    // For context during callback.
    on: Dy,
    tween: Ty,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Wl
  });
}
function Qo(e, t) {
  var n = Je(e, t);
  if (n.state > Wl) throw new Error("too late; already scheduled");
  return n;
}
function rt(e, t) {
  var n = Je(e, t);
  if (n.state > ki) throw new Error("too late; already running");
  return n;
}
function Je(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function $y(e, t, n) {
  var i = e.__transition, r;
  i[t] = n, n.timer = Hl(s, 0, n.time);
  function s(l) {
    n.state = ua, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== ua) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === ki) return la(a);
        h.state === da ? (h.state = Ii, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Ii, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (la(function() {
      n.state === ki && (n.state = da, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Eo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Eo) {
      for (n.state = ki, r = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (r[++f] = h);
      r.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = ko, 1), f = -1, p = r.length; ++f < p; )
      r[f].call(e, d);
    n.state === ko && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Ii, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Ai(e, t) {
  var n = e.__transition, i, r, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      r = i.state > Eo && i.state < ko, i.state = Ii, i.timer.stop(), i.on.call(r ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function Py(e) {
  return this.each(function() {
    Ai(this, e);
  });
}
function My(e, t) {
  var n, i;
  return function() {
    var r = rt(this, e), s = r.tween;
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
function Ry(e, t, n) {
  var i, r;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = rt(this, e), a = s.tween;
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
function zy(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = Je(this.node(), n).tween, r = 0, s = i.length, a; r < s; ++r)
      if ((a = i[r]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? My : Ry)(n, e, t));
}
function es(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var r = rt(this, i);
    (r.value || (r.value = {}))[t] = n.apply(this, arguments);
  }), function(r) {
    return Je(r, i).value[t];
  };
}
function Fl(e, t) {
  var n;
  return (typeof t == "number" ? Qe : t instanceof kt ? Fi : (n = kt(t)) ? (t = n, Fi) : Rl)(e, t);
}
function Ly(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Vy(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Oy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Hy(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function Wy(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function Fy(e, t, n) {
  var i, r, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c)));
  };
}
function By(e, t) {
  var n = ir(e), i = n === "transform" ? jy : Fl;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Fy : Wy)(n, i, es(this, "attr." + e, t)) : t == null ? (n.local ? Vy : Ly)(n) : (n.local ? Hy : Oy)(n, i, t));
}
function Ky(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Xy(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function Yy(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Xy(e, s)), n;
  }
  return r._value = t, r;
}
function qy(e, t) {
  var n, i;
  function r() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Ky(e, s)), n;
  }
  return r._value = t, r;
}
function Uy(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = ir(e);
  return this.tween(n, (i.local ? Yy : qy)(i, t));
}
function Zy(e, t) {
  return function() {
    Qo(this, e).delay = +t.apply(this, arguments);
  };
}
function Gy(e, t) {
  return t = +t, function() {
    Qo(this, e).delay = t;
  };
}
function Jy(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Zy : Gy)(t, e)) : Je(this.node(), t).delay;
}
function Qy(e, t) {
  return function() {
    rt(this, e).duration = +t.apply(this, arguments);
  };
}
function em(e, t) {
  return t = +t, function() {
    rt(this, e).duration = t;
  };
}
function tm(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Qy : em)(t, e)) : Je(this.node(), t).duration;
}
function nm(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    rt(this, e).ease = t;
  };
}
function im(e) {
  var t = this._id;
  return arguments.length ? this.each(nm(t, e)) : Je(this.node(), t).ease;
}
function rm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    rt(this, e).ease = n;
  };
}
function om(e) {
  if (typeof e != "function") throw new Error();
  return this.each(rm(this._id, e));
}
function sm(e) {
  typeof e != "function" && (e = vl(e));
  for (var t = this._groups, n = t.length, i = new Array(n), r = 0; r < n; ++r)
    for (var s = t[r], a = s.length, c = i[r] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new ut(i, this._parents, this._name, this._id);
}
function am(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, r = n.length, s = Math.min(i, r), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new ut(a, this._parents, this._name, this._id);
}
function cm(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function lm(e, t, n) {
  var i, r, s = cm(t) ? Qo : rt;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (r = (i = c).copy()).on(t, n), a.on = r;
  };
}
function um(e, t) {
  var n = this._id;
  return arguments.length < 2 ? Je(this.node(), n).on.on(e) : this.each(lm(n, e, t));
}
function dm(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function fm() {
  return this.on("end.remove", dm(this._id));
}
function pm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = qo(e));
  for (var i = this._groups, r = i.length, s = new Array(r), a = 0; a < r; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, or(l[p], t, n, p, l, Je(d, n)));
  return new ut(s, this._parents, t, n);
}
function hm(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = wl(e));
  for (var i = this._groups, r = i.length, s = [], a = [], c = 0; c < r; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, y = Je(d, n), w = 0, x = p.length; w < x; ++w)
          (h = p[w]) && or(h, t, n, w, p, y);
        s.push(p), a.push(d);
      }
  return new ut(s, a, t, n);
}
var gm = Yn.prototype.constructor;
function ym() {
  return new gm(this._groups, this._parents);
}
function mm(e, t) {
  var n, i, r;
  return function() {
    var s = Jt(this, e), a = (this.style.removeProperty(e), Jt(this, e));
    return s === a ? null : s === n && a === i ? r : r = t(n = s, i = a);
  };
}
function Bl(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function xm(e, t, n) {
  var i, r = n + "", s;
  return function() {
    var a = Jt(this, e);
    return a === r ? null : a === i ? s : s = t(i = a, n);
  };
}
function wm(e, t, n) {
  var i, r, s;
  return function() {
    var a = Jt(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), Jt(this, e))), a === u ? null : a === i && u === r ? s : (r = u, s = t(i = a, c));
  };
}
function vm(e, t) {
  var n, i, r, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = rt(this, e), l = u.on, d = u.value[s] == null ? c || (c = Bl(t)) : void 0;
    (l !== n || r !== d) && (i = (n = l).copy()).on(a, r = d), u.on = i;
  };
}
function bm(e, t, n) {
  var i = (e += "") == "transform" ? Ny : Fl;
  return t == null ? this.styleTween(e, mm(e, i)).on("end.style." + e, Bl(e)) : typeof t == "function" ? this.styleTween(e, wm(e, i, es(this, "style." + e, t))).each(vm(this._id, e)) : this.styleTween(e, xm(e, i, t), n).on("end.style." + e, null);
}
function Nm(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function jm(e, t, n) {
  var i, r;
  function s() {
    var a = t.apply(this, arguments);
    return a !== r && (i = (r = a) && Nm(e, a, n)), i;
  }
  return s._value = t, s;
}
function Sm(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, jm(e, t, n ?? ""));
}
function Cm(e) {
  return function() {
    this.textContent = e;
  };
}
function Em(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function km(e) {
  return this.tween("text", typeof e == "function" ? Em(es(this, "text", e)) : Cm(e == null ? "" : e + ""));
}
function Im(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function Am(e) {
  var t, n;
  function i() {
    var r = e.apply(this, arguments);
    return r !== n && (t = (n = r) && Im(r)), t;
  }
  return i._value = e, i;
}
function _m(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, Am(e));
}
function Dm() {
  for (var e = this._name, t = this._id, n = Kl(), i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = Je(u, t);
        or(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new ut(i, this._parents, e, n);
}
function Tm() {
  var e, t, n = this, i = n._id, r = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --r === 0 && s();
    } };
    n.each(function() {
      var l = rt(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), r === 0 && s();
  });
}
var $m = 0;
function ut(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Kl() {
  return ++$m;
}
var at = Yn.prototype;
ut.prototype = {
  constructor: ut,
  select: pm,
  selectAll: hm,
  selectChild: at.selectChild,
  selectChildren: at.selectChildren,
  filter: sm,
  merge: am,
  selection: ym,
  transition: Dm,
  call: at.call,
  nodes: at.nodes,
  node: at.node,
  size: at.size,
  empty: at.empty,
  each: at.each,
  on: um,
  attr: By,
  attrTween: Uy,
  style: bm,
  styleTween: Sm,
  text: km,
  textTween: _m,
  remove: fm,
  tween: zy,
  delay: Jy,
  duration: tm,
  ease: im,
  easeVarying: om,
  end: Tm,
  [Symbol.iterator]: at[Symbol.iterator]
};
function Pm(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var Mm = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: Pm
};
function Rm(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function zm(e) {
  var t, n;
  e instanceof ut ? (t = e._id, e = e._name) : (t = Kl(), (n = Mm).time = Jo(), e = e == null ? null : e + "");
  for (var i = this._groups, r = i.length, s = 0; s < r; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && or(u, e, t, l, a, n || Rm(u, t));
  return new ut(i, this._parents, e, t);
}
Yn.prototype.interrupt = Py;
Yn.prototype.transition = zm;
const gi = (e) => () => e;
function Lm(e, {
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
function ct(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ct.prototype = {
  constructor: ct,
  scale: function(e) {
    return e === 1 ? this : new ct(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ct(this.k, this.x + this.k * e, this.y + this.k * t);
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
var sr = new ct(1, 0, 0);
Xl.prototype = ct.prototype;
function Xl(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return sr;
  return e.__zoom;
}
function no(e) {
  e.stopImmediatePropagation();
}
function yn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Vm(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Om() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function fa() {
  return this.__zoom || sr;
}
function Hm(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function Wm() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Fm(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], r = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    r > i ? (i + r) / 2 : Math.min(0, i) || Math.max(0, r),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Yl() {
  var e = Vm, t = Om, n = Fm, i = Hm, r = Wm, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ei, l = nr("start", "zoom", "end"), d, f, p, h = 500, y = 150, w = 0, x = 10;
  function m(C) {
    C.property("__zoom", fa).on("wheel.zoom", k, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", M).filter(r).on("touchstart.zoom", A).on("touchmove.zoom", I).on("touchend.zoom touchcancel.zoom", R).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  m.transform = function(C, _, E, T) {
    var P = C.selection ? C.selection() : C;
    P.property("__zoom", fa), C !== P ? j(C, _, E, T) : P.interrupt().each(function() {
      N(this, arguments).event(T).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, m.scaleBy = function(C, _, E, T) {
    m.scaleTo(C, function() {
      var P = this.__zoom.k, $ = typeof _ == "function" ? _.apply(this, arguments) : _;
      return P * $;
    }, E, T);
  }, m.scaleTo = function(C, _, E, T) {
    m.transform(C, function() {
      var P = t.apply(this, arguments), $ = this.__zoom, F = E == null ? v(P) : typeof E == "function" ? E.apply(this, arguments) : E, H = $.invert(F), O = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(g(b($, O), F, H), P, a);
    }, E, T);
  }, m.translateBy = function(C, _, E, T) {
    m.transform(C, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof E == "function" ? E.apply(this, arguments) : E
      ), t.apply(this, arguments), a);
    }, null, T);
  }, m.translateTo = function(C, _, E, T, P) {
    m.transform(C, function() {
      var $ = t.apply(this, arguments), F = this.__zoom, H = T == null ? v($) : typeof T == "function" ? T.apply(this, arguments) : T;
      return n(sr.translate(H[0], H[1]).scale(F.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof E == "function" ? -E.apply(this, arguments) : -E
      ), $, a);
    }, T, P);
  };
  function b(C, _) {
    return _ = Math.max(s[0], Math.min(s[1], _)), _ === C.k ? C : new ct(_, C.x, C.y);
  }
  function g(C, _, E) {
    var T = _[0] - E[0] * C.k, P = _[1] - E[1] * C.k;
    return T === C.x && P === C.y ? C : new ct(C.k, T, P);
  }
  function v(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function j(C, _, E, T) {
    C.on("start.zoom", function() {
      N(this, arguments).event(T).start();
    }).on("interrupt.zoom end.zoom", function() {
      N(this, arguments).event(T).end();
    }).tween("zoom", function() {
      var P = this, $ = arguments, F = N(P, $).event(T), H = t.apply(P, $), O = E == null ? v(H) : typeof E == "function" ? E.apply(P, $) : E, X = Math.max(H[1][0] - H[0][0], H[1][1] - H[0][1]), q = P.__zoom, ee = typeof _ == "function" ? _.apply(P, $) : _, le = u(q.invert(O).concat(X / q.k), ee.invert(O).concat(X / ee.k));
      return function(U) {
        if (U === 1) U = ee;
        else {
          var z = le(U), Y = X / z[2];
          U = new ct(Y, O[0] - z[0] * Y, O[1] - z[1] * Y);
        }
        F.zoom(null, U);
      };
    });
  }
  function N(C, _, E) {
    return !E && C.__zooming || new S(C, _);
  }
  function S(C, _) {
    this.that = C, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, _), this.taps = 0;
  }
  S.prototype = {
    event: function(C) {
      return C && (this.sourceEvent = C), this;
    },
    start: function() {
      return ++this.active === 1 && (this.that.__zooming = this, this.emit("start")), this;
    },
    zoom: function(C, _) {
      return this.mouse && C !== "mouse" && (this.mouse[1] = _.invert(this.mouse[0])), this.touch0 && C !== "touch" && (this.touch0[1] = _.invert(this.touch0[0])), this.touch1 && C !== "touch" && (this.touch1[1] = _.invert(this.touch1[0])), this.that.__zoom = _, this.emit("zoom"), this;
    },
    end: function() {
      return --this.active === 0 && (delete this.that.__zooming, this.emit("end")), this;
    },
    emit: function(C) {
      var _ = We(this.that).datum();
      l.call(
        C,
        this.that,
        new Lm(C, {
          sourceEvent: this.sourceEvent,
          target: m,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function k(C, ..._) {
    if (!e.apply(this, arguments)) return;
    var E = N(this, _).event(C), T = this.__zoom, P = Math.max(s[0], Math.min(s[1], T.k * Math.pow(2, i.apply(this, arguments)))), $ = qe(C);
    if (E.wheel)
      (E.mouse[0][0] !== $[0] || E.mouse[0][1] !== $[1]) && (E.mouse[1] = T.invert(E.mouse[0] = $)), clearTimeout(E.wheel);
    else {
      if (T.k === P) return;
      E.mouse = [$, T.invert($)], Ai(this), E.start();
    }
    yn(C), E.wheel = setTimeout(F, y), E.zoom("mouse", n(g(b(T, P), E.mouse[0], E.mouse[1]), E.extent, a));
    function F() {
      E.wheel = null, E.end();
    }
  }
  function D(C, ..._) {
    if (p || !e.apply(this, arguments)) return;
    var E = C.currentTarget, T = N(this, _, !0).event(C), P = We(C.view).on("mousemove.zoom", O, !0).on("mouseup.zoom", X, !0), $ = qe(C, E), F = C.clientX, H = C.clientY;
    _l(C.view), no(C), T.mouse = [$, this.__zoom.invert($)], Ai(this), T.start();
    function O(q) {
      if (yn(q), !T.moved) {
        var ee = q.clientX - F, le = q.clientY - H;
        T.moved = ee * ee + le * le > w;
      }
      T.event(q).zoom("mouse", n(g(T.that.__zoom, T.mouse[0] = qe(q, E), T.mouse[1]), T.extent, a));
    }
    function X(q) {
      P.on("mousemove.zoom mouseup.zoom", null), Dl(q.view, T.moved), yn(q), T.event(q).end();
    }
  }
  function M(C, ..._) {
    if (e.apply(this, arguments)) {
      var E = this.__zoom, T = qe(C.changedTouches ? C.changedTouches[0] : C, this), P = E.invert(T), $ = E.k * (C.shiftKey ? 0.5 : 2), F = n(g(b(E, $), T, P), t.apply(this, _), a);
      yn(C), c > 0 ? We(this).transition().duration(c).call(j, F, T, C) : We(this).call(m.transform, F, T, C);
    }
  }
  function A(C, ..._) {
    if (e.apply(this, arguments)) {
      var E = C.touches, T = E.length, P = N(this, _, C.changedTouches.length === T).event(C), $, F, H, O;
      for (no(C), F = 0; F < T; ++F)
        H = E[F], O = qe(H, this), O = [O, this.__zoom.invert(O), H.identifier], P.touch0 ? !P.touch1 && P.touch0[2] !== O[2] && (P.touch1 = O, P.taps = 0) : (P.touch0 = O, $ = !0, P.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (P.taps < 2 && (f = O[0], d = setTimeout(function() {
        d = null;
      }, h)), Ai(this), P.start());
    }
  }
  function I(C, ..._) {
    if (this.__zooming) {
      var E = N(this, _).event(C), T = C.changedTouches, P = T.length, $, F, H, O;
      for (yn(C), $ = 0; $ < P; ++$)
        F = T[$], H = qe(F, this), E.touch0 && E.touch0[2] === F.identifier ? E.touch0[0] = H : E.touch1 && E.touch1[2] === F.identifier && (E.touch1[0] = H);
      if (F = E.that.__zoom, E.touch1) {
        var X = E.touch0[0], q = E.touch0[1], ee = E.touch1[0], le = E.touch1[1], U = (U = ee[0] - X[0]) * U + (U = ee[1] - X[1]) * U, z = (z = le[0] - q[0]) * z + (z = le[1] - q[1]) * z;
        F = b(F, Math.sqrt(U / z)), H = [(X[0] + ee[0]) / 2, (X[1] + ee[1]) / 2], O = [(q[0] + le[0]) / 2, (q[1] + le[1]) / 2];
      } else if (E.touch0) H = E.touch0[0], O = E.touch0[1];
      else return;
      E.zoom("touch", n(g(F, H, O), E.extent, a));
    }
  }
  function R(C, ..._) {
    if (this.__zooming) {
      var E = N(this, _).event(C), T = C.changedTouches, P = T.length, $, F;
      for (no(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), $ = 0; $ < P; ++$)
        F = T[$], E.touch0 && E.touch0[2] === F.identifier ? delete E.touch0 : E.touch1 && E.touch1[2] === F.identifier && delete E.touch1;
      if (E.touch1 && !E.touch0 && (E.touch0 = E.touch1, delete E.touch1), E.touch0) E.touch0[1] = this.__zoom.invert(E.touch0[0]);
      else if (E.end(), E.taps === 2 && (F = qe(F, this), Math.hypot(f[0] - F[0], f[1] - F[1]) < x)) {
        var H = We(this).on("dblclick.zoom");
        H && H.apply(this, arguments);
      }
    }
  }
  return m.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : gi(+C), m) : i;
  }, m.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : gi(!!C), m) : e;
  }, m.touchable = function(C) {
    return arguments.length ? (r = typeof C == "function" ? C : gi(!!C), m) : r;
  }, m.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : gi([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), m) : t;
  }, m.scaleExtent = function(C) {
    return arguments.length ? (s[0] = +C[0], s[1] = +C[1], m) : [s[0], s[1]];
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
    return arguments.length ? (w = (C = +C) * C, m) : Math.sqrt(w);
  }, m.tapDistance = function(C) {
    return arguments.length ? (x = +C, m) : x;
  }, m;
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
}, zn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], ql = ["Enter", " ", "Escape"], Ul = {
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
var Ct;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Ct || (Ct = {}));
var Ln;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Ln || (Ln = {}));
const Zl = {
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
var gt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(gt || (gt = {}));
var Yi;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(Yi || (Yi = {}));
var ie;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ie || (ie = {}));
const pa = {
  [ie.Left]: ie.Right,
  [ie.Right]: ie.Left,
  [ie.Top]: ie.Bottom,
  [ie.Bottom]: ie.Top
};
function Gl(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Jl = (e) => "id" in e && "source" in e && "target" in e, Bm = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), ts = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), Un = (e, t = [0, 0]) => {
  const { width: n, height: i } = dt(e), r = e.origin ?? t, s = n * r[0], a = i * r[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Km = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, r) => {
    const s = typeof r == "string";
    let a = !t.nodeLookup && !s ? r : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(r) : ts(r) ? r : t.nodeLookup.get(r.id));
    const c = a ? qi(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return ar(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return cr(n);
}, Zn = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((r) => {
    (t.filter === void 0 || t.filter(r)) && (n = ar(n, qi(r)), i = !0);
  }), i ? cr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, ns = (e, t, [n, i, r] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...ln(t, [n, i, r]),
    width: t.width / r,
    height: t.height / r
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, y = d.height ?? l.height ?? l.initialHeight ?? null, w = Vn(c, nn(l)), x = (h ?? 0) * (y ?? 0), m = s && w > 0;
    (!l.internals.handleBounds || m || w >= x || l.dragging) && u.push(l);
  }
  return u;
}, Xm = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function Ym(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((r) => r.id)) : null;
  return e.forEach((r) => {
    r.measured.width && r.measured.height && (t?.includeHiddenNodes || !r.hidden) && (!i || i.has(r.id)) && n.set(r.id, r);
  }), n;
}
async function qm({ nodes: e, width: t, height: n, panZoom: i, minZoom: r, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = Ym(e, a), u = Zn(c), l = rs(u, t, n, a?.minZoom ?? r, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Ql({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: r, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || r;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ke.error005());
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
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Um({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: r }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), y = !h && p.parentId && a.find((w) => w.id === p.parentId);
    (h || y) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Xm(a, u);
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
const tn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), At = (e = { x: 0, y: 0 }, t, n) => ({
  x: tn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: tn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function eu(e, t, n) {
  const { width: i, height: r } = dt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return At(e, [
    [s, a],
    [s + i, a + r]
  ], t);
}
const ha = (e, t, n) => e < t ? tn(Math.abs(e - t), 1, t) / t : e > n ? -tn(Math.abs(e - n), 1, t) / t : 0, is = (e, t, n = 15, i = 40) => {
  const r = ha(e.x, i, t.width - i) * n, s = ha(e.y, i, t.height - i) * n;
  return [r, s];
}, ar = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Io = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), cr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), nn = (e, t = [0, 0]) => {
  const { x: n, y: i } = ts(e) ? e.internals.positionAbsolute : Un(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, qi = (e, t = [0, 0]) => {
  const { x: n, y: i } = ts(e) ? e.internals.positionAbsolute : Un(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, tu = (e, t) => cr(ar(Io(e), Io(t))), Vn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, ga = (e) => Ze(e.width) && Ze(e.height) && Ze(e.x) && Ze(e.y), Ze = (e) => !isNaN(e) && isFinite(e), nu = (e, t) => (n, i) => {
}, Gn = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), ln = ({ x: e, y: t }, [n, i, r], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / r,
    y: (t - i) / r
  };
  return s ? Gn(c, a) : c;
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
function Zm(e, t, n) {
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
function Gm(e, t, n, i, r, s) {
  const { x: a, y: c } = rn(e, [t, n, i]), { x: u, y: l } = rn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = r - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const rs = (e, t, n, i, r, s) => {
  const a = Zm(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = tn(l, i, r), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, y = n / 2 - p * d, w = Gm(e, h, y, d, t, n), x = {
    left: Math.min(w.left - a.left, 0),
    top: Math.min(w.top - a.top, 0),
    right: Math.min(w.right - a.right, 0),
    bottom: Math.min(w.bottom - a.bottom, 0)
  };
  return {
    x: h - x.left + x.right,
    y: y - x.top + x.bottom,
    zoom: d
  };
}, On = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function _t(e) {
  return e != null && e !== "parent";
}
function dt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function iu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function ru(e, t = { width: 0, height: 0 }, n, i, r) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || r;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function ya(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Jm() {
  let e, t;
  return { promise: new Promise((i, r) => {
    e = i, t = r;
  }), resolve: e, reject: t };
}
function Qm(e) {
  return { ...Ul, ...e || {} };
}
function In(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: r }) {
  const { x: s, y: a } = Ge(e), c = ln({ x: s - (r?.left ?? 0), y: a - (r?.top ?? 0) }, i), { x: u, y: l } = n ? Gn(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const os = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), ou = (e) => e?.getRootNode?.() || window?.document, ex = ["INPUT", "SELECT", "TEXTAREA"];
function su(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : ex.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const au = (e) => "clientX" in e, Ge = (e, t) => {
  const n = au(e), i = n ? e.clientX : e.touches?.[0].clientX, r = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: r - (t?.top ?? 0)
  };
}, ma = (e, t, n, i, r) => {
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
      ...os(a)
    };
  });
};
function cu({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: r, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + r * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function yi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function xa({ pos: e, x1: t, y1: n, x2: i, y2: r, c: s }) {
  switch (e) {
    case ie.Left:
      return [t - yi(t - i, s), n];
    case ie.Right:
      return [t + yi(i - t, s), n];
    case ie.Top:
      return [t, n - yi(n - r, s)];
    case ie.Bottom:
      return [t, n + yi(r - n, s)];
  }
}
function lu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top, curvature: a = 0.25 }) {
  const [c, u] = xa({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r,
    c: a
  }), [l, d] = xa({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, y] = cu({
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
function uu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const r = Math.abs(n - e) / 2, s = n < e ? n + r : n - r, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, r, a];
}
function tx({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: r = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = r && n ? i + 1e3 : i, c = Math.max(e.parentId || r && e.selected ? e.internals.z : 0, t.parentId || r && t.selected ? t.internals.z : 0);
  return a + c;
}
function nx({ sourceNode: e, targetNode: t, width: n, height: i, transform: r }) {
  const s = ar(qi(e), qi(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -r[0] / r[2],
    y: -r[1] / r[2],
    width: n / r[2],
    height: i / r[2]
  };
  return Vn(a, cr(s)) > 0;
}
const du = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, ix = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), rx = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || du;
  let r;
  return Jl(e) ? r = { ...e } : r = {
    ...e,
    id: i(e)
  }, ix(r, t) ? t : (r.sourceHandle === null && delete r.sourceHandle, r.targetHandle === null && delete r.targetHandle, t.concat(r));
}, ox = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: r, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(r)), n;
  const c = i.getEdgeId || du, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : r,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== r).concat(u);
};
function fu({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [r, s, a, c] = uu({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, r, s, a, c];
}
const wa = {
  [ie.Left]: { x: -1, y: 0 },
  [ie.Right]: { x: 1, y: 0 },
  [ie.Top]: { x: 0, y: -1 },
  [ie.Bottom]: { x: 0, y: 1 }
}, sx = ({ source: e, sourcePosition: t = ie.Bottom, target: n }) => t === ie.Left || t === ie.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, va = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function ax({ source: e, sourcePosition: t = ie.Bottom, target: n, targetPosition: i = ie.Top, center: r, offset: s, stepPosition: a }) {
  const c = wa[t], u = wa[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = sx({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let y = [], w, x;
  const m = { x: 0, y: 0 }, b = { x: 0, y: 0 }, [, , g, v] = uu({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (w = r.x ?? l.x + (d.x - l.x) * a, x = r.y ?? (l.y + d.y) / 2) : (w = r.x ?? (l.x + d.x) / 2, x = r.y ?? l.y + (d.y - l.y) * a);
    const k = [
      { x: w, y: l.y },
      { x: w, y: d.y }
    ], D = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? y = p === "x" ? k : D : y = p === "x" ? D : k;
  } else {
    const k = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? y = c.x === h ? D : k : y = c.y === h ? k : D, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const _ = Math.min(s - 1, s - C);
        c[p] === h ? m[p] = (l[p] > e[p] ? -1 : 1) * _ : b[p] = (d[p] > n[p] ? -1 : 1) * _;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", _ = c[p] === u[C], E = l[C] > d[C], T = l[C] < d[C];
      (c[p] === 1 && (!_ && E || _ && T) || c[p] !== 1 && (!_ && T || _ && E)) && (y = p === "x" ? k : D);
    }
    const M = { x: l.x + m.x, y: l.y + m.y }, A = { x: d.x + b.x, y: d.y + b.y }, I = Math.max(Math.abs(M.x - y[0].x), Math.abs(A.x - y[0].x)), R = Math.max(Math.abs(M.y - y[0].y), Math.abs(A.y - y[0].y));
    I >= R ? (w = (M.x + A.x) / 2, x = y[0].y) : (w = y[0].x, x = (M.y + A.y) / 2);
  }
  const j = { x: l.x + m.x, y: l.y + m.y }, N = { x: d.x + b.x, y: d.y + b.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...j.x !== y[0].x || j.y !== y[0].y ? [j] : [],
    ...y,
    ...N.x !== y[y.length - 1].x || N.y !== y[y.length - 1].y ? [N] : [],
    n
  ], w, x, g, v];
}
function cx(e, t, n, i) {
  const r = Math.min(va(e, t) / 2, va(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + r * l},${a}Q ${s},${a} ${s},${a + r * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + r * u}Q ${s},${a} ${s + r * c},${a}`;
}
function Ui({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, y, w] = ax({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: r },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let m = 1; m < f.length - 1; m++)
    x += cx(f[m - 1], f[m], f[m + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, y, w];
}
function ba(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function lx(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!ba(t) || !ba(n))
    return null;
  const i = t.internals.handleBounds || Na(t.handles), r = n.internals.handleBounds || Na(n.handles), s = ja(i?.source ?? [], e.sourceHandle), a = ja(
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
  const c = s?.position || ie.Bottom, u = a?.position || ie.Top, l = Dt(t, s, c), d = Dt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function Na(e) {
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
function Dt(e, t, n = ie.Left, i = !1) {
  const r = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? dt(e);
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
function ja(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Ao(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function ux(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: r }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || r].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Ao(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const pu = 1e3, dx = 10, ss = {
  nodeOrigin: [0, 0],
  nodeExtent: zn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, fx = {
  ...ss,
  checkEquality: !0
};
function as(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function px(e, t, n) {
  const i = as(ss, n);
  for (const r of e.values())
    if (r.parentId)
      ls(r, e, t, i);
    else {
      const s = Un(r, i.nodeOrigin), a = _t(r.extent) ? r.extent : i.nodeExtent, c = At(s, a, dt(r));
      r.internals.positionAbsolute = c;
    }
}
function hx(e, t) {
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
function cs(e) {
  return e === "manual";
}
function _o(e, t, n, i = {}) {
  const r = as(fx, i), s = { i: 0 }, a = new Map(t), c = r?.elevateNodesOnSelect && !cs(r.zIndexMode) ? pu : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (r.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = Un(d, r.nodeOrigin), h = _t(d.extent) ? d.extent : r.nodeExtent, y = At(p, h, dt(d));
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
          handleBounds: hx(d, f),
          z: hu(d, c, r.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && ls(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function gx(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function ls(e, t, n, i, r) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = as(ss, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  gx(e, n), r && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++r.i, d.internals.z = d.internals.z + r.i * dx), r && d.internals.rootParentIndex !== void 0 && (r.i = d.internals.rootParentIndex);
  const f = s && !cs(u) ? pu : 0, { x: p, y: h, z: y } = yx(e, d, a, c, f, u), { positionAbsolute: w } = e.internals, x = p !== w.x || h !== w.y;
  (x || y !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : w,
      z: y
    }
  });
}
function hu(e, t, n) {
  const i = Ze(e.zIndex) ? e.zIndex : 0;
  return cs(n) ? i : i + (e.selected ? t : 0);
}
function yx(e, t, n, i, r, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = dt(e), l = Un(e, n), d = _t(e.extent) ? At(l, e.extent, u) : l;
  let f = At({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = eu(f, u, t));
  const p = hu(e, r, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function us(e, t, n, i = [0, 0]) {
  const r = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? nn(c), l = tu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = dt(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, y = Math.max(d.width, Math.round(a.width)), w = Math.max(d.height, Math.round(a.height)), x = (y - d.width) * f[0], m = (w - d.height) * f[1];
    (p > 0 || h > 0 || x || m) && (r.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + m
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
        width: y + (p ? f[0] * p - x : 0),
        height: w + (h ? f[1] * h - m : 0)
      }
    });
  }), r;
}
function mx(e, t, n, i, r, s, a) {
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
    const w = os(h.nodeElement), x = y.measured.width !== w.width || y.measured.height !== w.height;
    if (!!(w.width && w.height && (x || !y.internals.handleBounds || h.force))) {
      const b = h.nodeElement.getBoundingClientRect(), g = _t(y.extent) ? y.extent : s;
      let { positionAbsolute: v } = y.internals;
      y.parentId && y.extent === "parent" ? v = eu(v, w, t.get(y.parentId)) : g && (v = At(v, g, w));
      const j = {
        ...y,
        measured: w,
        internals: {
          ...y.internals,
          positionAbsolute: v,
          handleBounds: {
            source: ma("source", h.nodeElement, b, f, y.id),
            target: ma("target", h.nodeElement, b, f, y.id)
          }
        }
      };
      t.set(y.id, j), y.parentId && ls(j, t, n, { nodeOrigin: r, zIndexMode: a }), u = !0, x && (l.push({
        id: y.id,
        type: "dimensions",
        dimensions: w
      }), y.expandParent && y.parentId && p.push({
        id: y.id,
        parentId: y.parentId,
        rect: nn(j, r)
      }));
    }
  }
  if (p.length > 0) {
    const h = us(p, t, n, r);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function xx({ delta: e, panZoom: t, transform: n, translateExtent: i, width: r, height: s }) {
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
function Sa(e, t, n, i, r, s) {
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
function gu(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: r, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: r, target: s, sourceHandle: a, targetHandle: c }, l = `${r}-${a}--${s}-${c}`, d = `${s}-${c}--${r}-${a}`;
    Sa("source", u, d, e, r, a), Sa("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function yu(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : yu(n, t) : !1;
}
function Ca(e, t, n) {
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
function wx(e, t, n, i) {
  const r = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !yu(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function vx({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const r = e.values().next().value;
  if (!r)
    return null;
  const s = {
    x: n - r.distance.x,
    y: i - r.distance.y
  }, a = Gn(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function bx({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: r }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, y = !1, w = null;
  function x({ noDragClassName: b, handleSelector: g, domNode: v, isSelectable: j, nodeId: N, nodeClickDistance: S = 0 }) {
    p = We(v);
    function k({ x: I, y: R }) {
      const { nodeLookup: C, nodeExtent: _, snapGrid: E, snapToGrid: T, nodeOrigin: P, onNodeDrag: $, onSelectionDrag: F, onError: H, updateNodePositions: O } = t();
      s = { x: I, y: R };
      let X = !1;
      const q = c.size > 1, ee = q && _ ? Io(Zn(c)) : null, le = q && T ? vx({
        dragItems: c,
        snapGrid: E,
        x: I,
        y: R
      }) : null;
      for (const [U, z] of c) {
        if (!C.has(U))
          continue;
        let Y = { x: I - z.distance.x, y: R - z.distance.y };
        T && (Y = le ? {
          x: Math.round(Y.x + le.x),
          y: Math.round(Y.y + le.y)
        } : Gn(Y, E));
        let ae = null;
        if (q && _ && !z.extent && ee) {
          const { positionAbsolute: re } = z.internals, fe = re.x - ee.x + _[0][0], V = re.x + z.measured.width - ee.x2 + _[1][0], Q = re.y - ee.y + _[0][1], ge = re.y + z.measured.height - ee.y2 + _[1][1];
          ae = [
            [fe, Q],
            [V, ge]
          ];
        }
        const { position: ce, positionAbsolute: J } = Ql({
          nodeId: U,
          nextPosition: Y,
          nodeLookup: C,
          nodeExtent: ae || _,
          nodeOrigin: P,
          onError: H
        });
        X = X || z.position.x !== ce.x || z.position.y !== ce.y, z.position = ce, z.internals.positionAbsolute = J;
      }
      if (y = y || X, !!X && (O(c, !0), w && (i || $ || !N && F))) {
        const [U, z] = io({
          nodeId: N,
          dragItems: c,
          nodeLookup: C
        });
        i?.(w, c, U, z), $?.(w, U, z), N || F?.(w, z);
      }
    }
    async function D() {
      if (!d)
        return;
      const { transform: I, panBy: R, autoPanSpeed: C, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [E, T] = is(l, d, C);
      (E !== 0 || T !== 0) && (s.x = (s.x ?? 0) - E / I[2], s.y = (s.y ?? 0) - T / I[2], await R({ x: E, y: T }) && k(s)), a = requestAnimationFrame(D);
    }
    function M(I) {
      const { nodeLookup: R, multiSelectionActive: C, nodesDraggable: _, transform: E, snapGrid: T, snapToGrid: P, selectNodesOnDrag: $, onNodeDragStart: F, onSelectionDragStart: H, unselectNodesAndEdges: O } = t();
      f = !0, (!$ || !j) && !C && N && (R.get(N)?.selected || O()), j && $ && N && e?.(N);
      const X = In(I.sourceEvent, { transform: E, snapGrid: T, snapToGrid: P, containerBounds: d });
      if (s = X, c = wx(R, _, X, N), c.size > 0 && (n || F || !N && H)) {
        const [q, ee] = io({
          nodeId: N,
          dragItems: c,
          nodeLookup: R
        });
        n?.(I.sourceEvent, c, q, ee), F?.(I.sourceEvent, q, ee), N || H?.(I.sourceEvent, ee);
      }
    }
    const A = Tl().clickDistance(S).on("start", (I) => {
      const { domNode: R, nodeDragThreshold: C, transform: _, snapGrid: E, snapToGrid: T } = t();
      d = R?.getBoundingClientRect() || null, h = !1, y = !1, w = I.sourceEvent, C === 0 && M(I), s = In(I.sourceEvent, { transform: _, snapGrid: E, snapToGrid: T, containerBounds: d }), l = Ge(I.sourceEvent, d);
    }).on("drag", (I) => {
      const { autoPanOnNodeDrag: R, transform: C, snapGrid: _, snapToGrid: E, nodeDragThreshold: T, nodeLookup: P } = t(), $ = In(I.sourceEvent, { transform: C, snapGrid: _, snapToGrid: E, containerBounds: d });
      if (w = I.sourceEvent, (I.sourceEvent.type === "touchmove" && I.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      N && !P.has(N)) && (h = !0), !h) {
        if (!u && R && f && (u = !0, D()), !f) {
          const F = Ge(I.sourceEvent, d), H = F.x - l.x, O = F.y - l.y;
          Math.sqrt(H * H + O * O) > T && M(I);
        }
        (s.x !== $.xSnapped || s.y !== $.ySnapped) && c && f && (l = Ge(I.sourceEvent, d), k($));
      }
    }).on("end", (I) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: R, updateNodePositions: C, onNodeDragStop: _, onSelectionDragStop: E } = t();
        if (y && (C(c, !1), y = !1), r || _ || !N && E) {
          const [T, P] = io({
            nodeId: N,
            dragItems: c,
            nodeLookup: R,
            dragging: !1
          });
          r?.(I.sourceEvent, c, T, P), _?.(I.sourceEvent, T, P), N || E?.(I.sourceEvent, P);
        }
      }
    }).filter((I) => {
      const R = I.target;
      return !I.button && (!b || !Ca(R, `.${b}`, v)) && (!g || Ca(R, g, v));
    });
    p.call(A);
  }
  function m() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: m
  };
}
function Nx(e, t, n) {
  const i = [], r = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Vn(r, nn(s)) > 0 && i.push(s);
  return i;
}
const jx = 250;
function Sx(e, t, n, i) {
  let r = [], s = 1 / 0;
  const a = Nx(e, n, t + jx);
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
function mu(e, t, n, i, r, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = r === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Dt(a, u, u.position, !0) } : u;
}
function xu(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function Cx(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const wu = () => !0;
function Ex(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: r, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: y, onConnect: w, onConnectEnd: x, isValidConnection: m = wu, onReconnectEnd: b, updateConnection: g, getTransform: v, getFromHandle: j, autoPanSpeed: N, dragThreshold: S = 1, handleDomNode: k }) {
  const D = ou(e.target);
  let M = 0, A;
  const { x: I, y: R } = Ge(e), C = xu(s, k), _ = c?.getBoundingClientRect();
  let E = !1;
  if (!_ || !C)
    return;
  const T = mu(r, C, i, u, t);
  if (!T)
    return;
  let P = Ge(e, _), $ = !1, F = null, H = !1, O = null;
  function X() {
    if (!d || !_)
      return;
    const [ce, J] = is(P, _, N);
    p({ x: ce, y: J }), M = requestAnimationFrame(X);
  }
  const q = {
    ...T,
    nodeId: r,
    type: C,
    position: T.position
  }, ee = u.get(r);
  let U = {
    inProgress: !0,
    isValid: null,
    from: Dt(ee, q, ie.Left, !0),
    fromHandle: q,
    fromPosition: q.position,
    fromNode: ee,
    to: P,
    toHandle: null,
    toPosition: pa[q.position],
    toNode: null,
    pointer: P
  };
  function z() {
    E = !0, g(U), y?.(e, { nodeId: r, handleId: i, handleType: C });
  }
  S === 0 && z();
  function Y(ce) {
    if (!E) {
      const { x: ge, y: me } = Ge(ce), Re = ge - I, _e = me - R;
      if (!(Re * Re + _e * _e > S * S))
        return;
      z();
    }
    if (!j() || !q) {
      ae(ce);
      return;
    }
    const J = v();
    P = Ge(ce, _), A = Sx(ln(P, J, !1, [1, 1]), n, u, q), $ || (X(), $ = !0);
    const re = vu(ce, {
      handle: A,
      connectionMode: t,
      fromNodeId: r,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: m,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    O = re.handleDomNode, F = re.connection, H = Cx(!!A, re.isValid);
    const fe = u.get(r), V = fe ? Dt(fe, q, ie.Left, !0) : U.from, Q = {
      ...U,
      from: V,
      isValid: H,
      to: re.toHandle && H ? rn({ x: re.toHandle.x, y: re.toHandle.y }, J) : P,
      toHandle: re.toHandle,
      toPosition: H && re.toHandle ? re.toHandle.position : pa[q.position],
      toNode: re.toHandle ? u.get(re.toHandle.nodeId) : null,
      pointer: P
    };
    g(Q), U = Q;
  }
  function ae(ce) {
    if (!("touches" in ce && ce.touches.length > 0)) {
      if (E) {
        (A || O) && F && H && w?.(F);
        const { inProgress: J, ...re } = U, fe = {
          ...re,
          toPosition: U.toHandle ? U.toPosition : null
        };
        x?.(ce, fe), s && b?.(ce, fe);
      }
      h(), cancelAnimationFrame(M), $ = !1, H = !1, F = null, O = null, D.removeEventListener("mousemove", Y), D.removeEventListener("mouseup", ae), D.removeEventListener("touchmove", Y), D.removeEventListener("touchend", ae);
    }
  }
  D.addEventListener("mousemove", Y), D.addEventListener("mouseup", ae), D.addEventListener("touchmove", Y), D.addEventListener("touchend", ae);
}
function vu(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: r, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = wu, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y } = Ge(e), w = a.elementFromPoint(h, y), x = w?.classList.contains(`${c}-flow__handle`) ? w : p, m = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const b = xu(void 0, x), g = x.getAttribute("data-nodeid"), v = x.getAttribute("data-handleid"), j = x.classList.contains("connectable"), N = x.classList.contains("connectableend");
    if (!g || !b)
      return m;
    const S = {
      source: f ? g : i,
      sourceHandle: f ? v : r,
      target: f ? i : g,
      targetHandle: f ? r : v
    };
    m.connection = S;
    const D = j && N && (n === en.Strict ? f && b === "source" || !f && b === "target" : g !== i || v !== r);
    m.isValid = D && l(S), m.toHandle = mu(g, b, v, d, n, !0);
  }
  return m;
}
const Do = {
  onPointerDown: Ex,
  isValid: vu
};
function kx({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const r = We(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const y = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const v = n(), j = g.sourceEvent.ctrlKey && On() ? 10 : 1, N = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, S = v[2] * Math.pow(2, N * j);
      t.scaleTo(S);
    };
    let w = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (w = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, m = (g) => {
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
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: k.x,
        y: k.y,
        zoom: v[2]
      }, D, c);
    }, b = Yl().on("start", x).on("zoom", f ? m : null).on("zoom.wheel", p ? y : null);
    r.call(b, {});
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
const lr = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), ro = ({ x: e, y: t, zoom: n }) => sr.translate(e, t).scale(n), Bt = (e, t) => e.target.closest(`.${t}`), bu = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), Ix = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, oo = (e, t = 0, n = Ix, i = () => {
}) => {
  const r = typeof t == "number" && t > 0;
  return r || i(), r ? e.transition().duration(t).ease(n).on("end", i) : e;
}, Nu = (e) => {
  const t = e.ctrlKey && On() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function Ax({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: r, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Bt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = qe(d), m = Nu(d), b = f * Math.pow(2, m);
      i.scaleTo(n, b, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = r === Ct.Vertical ? 0 : d.deltaX * p, y = r === Ct.Horizontal ? 0 : d.deltaY * p;
    !On() && d.shiftKey && r !== Ct.Vertical && (h = d.deltaY * p, y = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(y / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const w = lr(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, w), e.panScrollTimeout = setTimeout(() => {
      l?.(d, w), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, w));
  };
}
function _x({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, r) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Bt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, r);
  };
}
function Dx({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const r = lr(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = r, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, r);
  };
}
function Tx({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: r }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && bu(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), r && !s.sourceEvent?.internal && r?.(s.sourceEvent, lr(s.transform));
  };
}
function $x({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: r, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && bu(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), r)) {
      const c = lr(a.transform);
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
function Px({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: r, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
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
function Mx({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: r, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Yl().scaleExtent([t, n]).translateExtent(i), p = We(e).call(f);
  b({
    x: r.x,
    y: r.y,
    zoom: tn(r.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), y = p.on("dblclick.zoom");
  f.wheelDelta(Nu);
  async function w(A, I) {
    return p ? new Promise((R) => {
      f?.interpolate(I?.interpolate === "linear" ? kn : Ei).transform(oo(p, I?.duration, I?.ease, () => R(!0)), A);
    }) : !1;
  }
  function x({ noWheelClassName: A, noPanClassName: I, onPaneContextMenu: R, userSelectionActive: C, panOnScroll: _, panOnDrag: E, panOnScrollMode: T, panOnScrollSpeed: P, preventScrolling: $, zoomOnPinch: F, zoomOnScroll: H, zoomOnDoubleClick: O, zoomActivationKeyPressed: X, lib: q, onTransformChange: ee, connectionInProgress: le, paneClickDistance: U, selectionOnDrag: z }) {
    C && !l.isZoomingOrPanning && m();
    const Y = _ && !X && !C;
    f.clickDistance(z ? 1 / 0 : !Ze(U) || U < 0 ? 0 : U);
    const ae = Y ? Ax({
      zoomPanValues: l,
      noWheelClassName: A,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: T,
      panOnScrollSpeed: P,
      zoomOnPinch: F,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : _x({
      noWheelClassName: A,
      preventScrolling: $,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", ae, { passive: !1 });
    const ce = Dx({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", ce);
    const J = Tx({
      zoomPanValues: l,
      panOnDrag: E,
      onPaneContextMenu: !!R,
      onPanZoom: s,
      onTransformChange: ee
    });
    f.on("zoom", J);
    const re = $x({
      zoomPanValues: l,
      panOnDrag: E,
      panOnScroll: _,
      onPaneContextMenu: R,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", re);
    const fe = Px({
      zoomActivationKeyPressed: X,
      panOnDrag: E,
      zoomOnScroll: H,
      panOnScroll: _,
      zoomOnDoubleClick: O,
      zoomOnPinch: F,
      userSelectionActive: C,
      noPanClassName: I,
      noWheelClassName: A,
      lib: q,
      connectionInProgress: le
    });
    f.filter(fe), O ? p.on("dblclick.zoom", y) : p.on("dblclick.zoom", null);
  }
  function m() {
    f.on("zoom", null);
  }
  async function b(A, I, R) {
    const C = ro(A), _ = f?.constrain()(C, I, R);
    return _ && await w(_), _;
  }
  async function g(A, I) {
    const R = ro(A);
    return await w(R, I), R;
  }
  function v(A) {
    if (p) {
      const I = ro(A), R = p.property("__zoom");
      (R.k !== A.zoom || R.x !== A.x || R.y !== A.y) && f?.transform(p, I, null, { sync: !0 });
    }
  }
  function j() {
    const A = p ? Xl(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: A.x, y: A.y, zoom: A.k };
  }
  async function N(A, I) {
    return p ? new Promise((R) => {
      f?.interpolate(I?.interpolate === "linear" ? kn : Ei).scaleTo(oo(p, I?.duration, I?.ease, () => R(!0)), A);
    }) : !1;
  }
  async function S(A, I) {
    return p ? new Promise((R) => {
      f?.interpolate(I?.interpolate === "linear" ? kn : Ei).scaleBy(oo(p, I?.duration, I?.ease, () => R(!0)), A);
    }) : !1;
  }
  function k(A) {
    f?.scaleExtent(A);
  }
  function D(A) {
    f?.translateExtent(A);
  }
  function M(A) {
    const I = !Ze(A) || A < 0 ? 0 : A;
    f?.clickDistance(I);
  }
  return {
    update: x,
    destroy: m,
    setViewport: g,
    setViewportConstrained: b,
    getViewport: j,
    scaleTo: N,
    scaleBy: S,
    setScaleExtent: k,
    setTranslateExtent: D,
    syncViewport: v,
    setClickDistance: M
  };
}
var on;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(on || (on = {}));
function Rx({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: r, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && r && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function Ea(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), r = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: r
  };
}
function pt(e, t) {
  return Math.max(0, t - e);
}
function ht(e, t) {
  return Math.max(0, e - t);
}
function mi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function ka(e, t) {
  return e ? !t : t;
}
function zx(e, t, n, i, r, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: y } = n, { minWidth: w, maxWidth: x, minHeight: m, maxHeight: b } = i, { x: g, y: v, width: j, height: N, aspectRatio: S } = e;
  let k = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? y - e.pointerY : 0);
  const M = j + (u ? -k : k), A = N + (l ? -D : D), I = -s[0] * j, R = -s[1] * N;
  let C = mi(M, w, x), _ = mi(A, m, b);
  if (a) {
    let P = 0, $ = 0;
    u && k < 0 ? P = pt(g + k + I, a[0][0]) : !u && k > 0 && (P = ht(g + M + I, a[1][0])), l && D < 0 ? $ = pt(v + D + R, a[0][1]) : !l && D > 0 && ($ = ht(v + A + R, a[1][1])), C = Math.max(C, P), _ = Math.max(_, $);
  }
  if (c) {
    let P = 0, $ = 0;
    u && k > 0 ? P = ht(g + k, c[0][0]) : !u && k < 0 && (P = pt(g + M, c[1][0])), l && D > 0 ? $ = ht(v + D, c[0][1]) : !l && D < 0 && ($ = pt(v + A, c[1][1])), C = Math.max(C, P), _ = Math.max(_, $);
  }
  if (r) {
    if (d) {
      const P = mi(M / S, m, b) * S;
      if (C = Math.max(C, P), a) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = ht(v + R + M / S, a[1][1]) * S : $ = pt(v + R + (u ? k : -k) / S, a[0][1]) * S, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = pt(v + M / S, c[1][1]) * S : $ = ht(v + (u ? k : -k) / S, c[0][1]) * S, C = Math.max(C, $);
      }
    }
    if (f) {
      const P = mi(A * S, w, x) / S;
      if (_ = Math.max(_, P), a) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = ht(g + A * S + I, a[1][0]) / S : $ = pt(g + (l ? D : -D) * S + I, a[0][0]) / S, _ = Math.max(_, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = pt(g + A * S, c[1][0]) / S : $ = ht(g + (l ? D : -D) * S, c[0][0]) / S, _ = Math.max(_, $);
      }
    }
  }
  D = D + (D < 0 ? _ : -_), k = k + (k < 0 ? C : -C), r && (p ? M > A * S ? D = (ka(u, l) ? -k : k) / S : k = (ka(u, l) ? -D : D) * S : d ? (D = k / S, l = u) : (k = D * S, u = l));
  const E = u ? g + k : g, T = l ? v + D : v;
  return {
    width: j + (u ? -k : k),
    height: N + (l ? -D : D),
    x: s[0] * k * (u ? -1 : 1) + E,
    y: s[1] * D * (l ? -1 : 1) + T
  };
}
const ju = { width: 0, height: 0, x: 0, y: 0 }, Lx = {
  ...ju,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Vx(e, t, n) {
  const i = t.position.x + e.position.x, r = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, r - u],
    [i + s - c, r + a - u]
  ];
}
function Ox({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: r }) {
  const s = We(e);
  let a = {
    controlDirection: Ea("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: y, onResizeEnd: w, shouldResize: x }) {
    let m = { ...ju }, b = { ...Lx };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: Ea(l)
    };
    let g, v = null, j = [], N, S, k, D = !1;
    const M = Tl().on("start", (A) => {
      const { nodeLookup: I, transform: R, snapGrid: C, snapToGrid: _, nodeOrigin: E, paneDomNode: T } = n();
      if (g = I.get(t), !g)
        return;
      v = T?.getBoundingClientRect() ?? null;
      const { xSnapped: P, ySnapped: $ } = In(A.sourceEvent, {
        transform: R,
        snapGrid: C,
        snapToGrid: _,
        containerBounds: v
      });
      m = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, b = {
        ...m,
        pointerX: P,
        pointerY: $,
        aspectRatio: m.width / m.height
      }, N = void 0, S = _t(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (N = I.get(g.parentId)), N && g.extent === "parent" && (S = [
        [0, 0],
        [N.measured.width, N.measured.height]
      ]), j = [], k = void 0;
      for (const [F, H] of I)
        if (H.parentId === t && (j.push({
          id: F,
          position: { ...H.position },
          extent: H.extent
        }), H.extent === "parent" || H.expandParent)) {
          const O = Vx(H, g, H.origin ?? E);
          k ? k = [
            [Math.min(O[0][0], k[0][0]), Math.min(O[0][1], k[0][1])],
            [Math.max(O[1][0], k[1][0]), Math.max(O[1][1], k[1][1])]
          ] : k = O;
        }
      h?.(A, { ...m });
    }).on("drag", (A) => {
      const { transform: I, snapGrid: R, snapToGrid: C, nodeOrigin: _ } = n(), E = In(A.sourceEvent, {
        transform: I,
        snapGrid: R,
        snapToGrid: C,
        containerBounds: v
      }), T = [];
      if (!g)
        return;
      const { x: P, y: $, width: F, height: H } = m, O = {}, X = g.origin ?? _, { width: q, height: ee, x: le, y: U } = zx(b, a.controlDirection, E, a.boundaries, a.keepAspectRatio, X, S, k), z = q !== F, Y = ee !== H, ae = le !== P && z, ce = U !== $ && Y;
      if (!ae && !ce && !z && !Y)
        return;
      if ((ae || ce || X[0] === 1 || X[1] === 1) && (O.x = ae ? le : m.x, O.y = ce ? U : m.y, m.x = O.x, m.y = O.y, j.length > 0)) {
        const V = le - P, Q = U - $;
        for (const ge of j)
          ge.position = {
            x: ge.position.x - V + X[0] * (q - F),
            y: ge.position.y - Q + X[1] * (ee - H)
          }, T.push(ge);
      }
      if ((z || Y) && (O.width = z && (!a.resizeDirection || a.resizeDirection === "horizontal") ? q : m.width, O.height = Y && (!a.resizeDirection || a.resizeDirection === "vertical") ? ee : m.height, m.width = O.width, m.height = O.height), N && g.expandParent) {
        const V = X[0] * (O.width ?? 0);
        O.x && O.x < V && (m.x = V, b.x = b.x - (O.x - V));
        const Q = X[1] * (O.height ?? 0);
        O.y && O.y < Q && (m.y = Q, b.y = b.y - (O.y - Q));
      }
      const J = Rx({
        width: m.width,
        prevWidth: F,
        height: m.height,
        prevHeight: H,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), re = { ...m, direction: J };
      x?.(A, re) !== !1 && (D = !0, y?.(A, re), i(O, T));
    }).on("end", (A) => {
      D && (w?.(A, { ...m }), r?.({ ...m }), D = !1);
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
var Ia;
function Hx() {
  if (Ia) return lo;
  Ia = 1;
  var e = tt;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, r = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), y = i({ inst: { value: h, getSnapshot: p } }), w = y[0].inst, x = y[1];
    return s(
      function() {
        w.value = h, w.getSnapshot = p, u(w) && x({ inst: w });
      },
      [f, h, p]
    ), r(
      function() {
        return u(w) && x({ inst: w }), f(function() {
          u(w) && x({ inst: w });
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
var Aa;
function Wx() {
  return Aa || (Aa = 1, co.exports = Hx()), co.exports;
}
var _a;
function Fx() {
  if (_a) return ao;
  _a = 1;
  var e = tt, t = Wx();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, r = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return ao.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var y = s(null);
    if (y.current === null) {
      var w = { hasValue: !1, value: null };
      y.current = w;
    } else w = y.current;
    y = c(
      function() {
        function m(N) {
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
            return m(d());
          },
          j === null ? void 0 : function() {
            return m(j());
          }
        ];
      },
      [d, f, p, h]
    );
    var x = r(l, y[0], y[1]);
    return a(
      function() {
        w.hasValue = !0, w.value = x;
      },
      [x]
    ), u(x), x;
  }, ao;
}
var Da;
function Bx() {
  return Da || (Da = 1, so.exports = Fx()), so.exports;
}
var Kx = Bx();
const Xx = /* @__PURE__ */ df(Kx), Yx = {}, Ta = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((y) => y(t, h));
    }
  }, r = () => t, u = { setState: i, getState: r, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (Yx ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, r, u);
  return u;
}, qx = (e) => e ? Ta(e) : Ta, { useDebugValue: Ux } = tt, { useSyncExternalStoreWithSelector: Zx } = Xx, Gx = (e) => e;
function Su(e, t = Gx, n) {
  const i = Zx(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Ux(i), i;
}
const $a = (e, t) => {
  const n = qx(e), i = (r, s = t) => Su(n, r, s);
  return Object.assign(i, n), i;
}, Jx = (e, t) => e ? $a(e, t) : $a;
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
var uo = { exports: {} }, De = {};
var Pa;
function Qx() {
  if (Pa) return De;
  Pa = 1;
  var e = tt;
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
  return De.__DOM_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE = i, De.createPortal = function(u, l) {
    var d = 2 < arguments.length && arguments[2] !== void 0 ? arguments[2] : null;
    if (!l || l.nodeType !== 1 && l.nodeType !== 9 && l.nodeType !== 11)
      throw Error(t(299));
    return s(u, l, null, d);
  }, De.flushSync = function(u) {
    var l = a.T, d = i.p;
    try {
      if (a.T = null, i.p = 2, u) return u();
    } finally {
      a.T = l, i.p = d, i.d.f();
    }
  }, De.preconnect = function(u, l) {
    typeof u == "string" && (l ? (l = l.crossOrigin, l = typeof l == "string" ? l === "use-credentials" ? l : "" : void 0) : l = null, i.d.C(u, l));
  }, De.prefetchDNS = function(u) {
    typeof u == "string" && i.d.D(u);
  }, De.preinit = function(u, l) {
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
  }, De.preinitModule = function(u, l) {
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
  }, De.preload = function(u, l) {
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
  }, De.preloadModule = function(u, l) {
    if (typeof u == "string")
      if (l) {
        var d = c(l.as, l.crossOrigin);
        i.d.m(u, {
          as: typeof l.as == "string" && l.as !== "script" ? l.as : void 0,
          crossOrigin: d,
          integrity: typeof l.integrity == "string" ? l.integrity : void 0
        });
      } else i.d.m(u);
  }, De.requestFormReset = function(u) {
    i.d.r(u);
  }, De.unstable_batchedUpdates = function(u, l) {
    return u(l);
  }, De.useFormState = function(u, l, d) {
    return a.H.useFormState(u, l, d);
  }, De.useFormStatus = function() {
    return a.H.useHostTransitionStatus();
  }, De.version = "19.2.7", De;
}
var Ma;
function ew() {
  if (Ma) return uo.exports;
  Ma = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), uo.exports = Qx(), uo.exports;
}
var tw = ew();
const ur = Oo(null), nw = ur.Provider, Cu = Ke.error001("react");
function pe(e, t) {
  const n = Wn(ur);
  if (n === null)
    throw new Error(Cu);
  return Su(n, e, t);
}
function ve() {
  const e = Wn(ur);
  if (e === null)
    throw new Error(Cu);
  return de(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Ra = { display: "none" }, iw = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Eu = "react-flow__node-desc", ku = "react-flow__edge-desc", rw = "react-flow__aria-live", ow = (e) => e.ariaLiveMessage, sw = (e) => e.ariaLabelConfig;
function aw({ rfId: e }) {
  const t = pe(ow);
  return o.jsx("div", { id: `${rw}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: iw, children: t });
}
function cw({ rfId: e, disableKeyboardA11y: t }) {
  const n = pe(sw);
  return o.jsxs(o.Fragment, { children: [o.jsx("div", { id: `${Eu}-${e}`, style: Ra, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), o.jsx("div", { id: `${ku}-${e}`, style: Ra, children: n["edge.a11yDescription.default"] }), !t && o.jsx(aw, { rfId: e })] });
}
const dr = $c(({ position: e = "top-left", children: t, className: n, style: i, ...r }, s) => {
  const a = `${e}`.split("-");
  return o.jsx("div", { className: ke(["react-flow__panel", n, ...a]), style: i, ref: s, ...r, children: t });
});
dr.displayName = "Panel";
function lw({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : o.jsx(dr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: o.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const uw = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, xi = (e) => e.id;
function dw(e, t) {
  return we(e.selectedNodes.map(xi), t.selectedNodes.map(xi)) && we(e.selectedEdges.map(xi), t.selectedEdges.map(xi));
}
function fw({ onSelectionChange: e }) {
  const t = ve(), { selectedNodes: n, selectedEdges: i } = pe(uw, dw);
  return G(() => {
    const r = { nodes: n, edges: i };
    e?.(r), t.getState().onSelectionChangeHandlers.forEach((s) => s(r));
  }, [n, i, e]), null;
}
const pw = (e) => !!e.onSelectionChangeHandlers;
function hw({ onSelectionChange: e }) {
  const t = pe(pw);
  return e || t ? o.jsx(fw, { onSelectionChange: e }) : null;
}
const Iu = [0, 0], gw = { x: 0, y: 0, zoom: 1 }, yw = [
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
], za = [...yw, "rfId"], mw = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), La = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: zn,
  nodeOrigin: Iu,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function xw(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: r, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = pe(mw, we), l = ve();
  G(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = La, c();
  }), []);
  const d = ne(La);
  return G(
    () => {
      for (const f of za) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? r(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Qm(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    za.map((f) => e[f])
  ), null;
}
function Va() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function ww(e) {
  const [t, n] = B(e === "system" ? null : e);
  return G(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Va(), r = () => n(i?.matches ? "dark" : "light");
    return r(), i?.addEventListener("change", r), () => {
      i?.removeEventListener("change", r);
    };
  }, [e]), t !== null ? t : Va()?.matches ? "dark" : "light";
}
const Oa = typeof document < "u" ? document : null;
function Hn(e = null, t = { target: Oa, actInsideInputWithModifier: !0 }) {
  const [n, i] = B(!1), r = ne(!1), s = ne(/* @__PURE__ */ new Set([])), [a, c] = de(() => {
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
    const u = t?.target ?? Oa, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (r.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!r.current || r.current && !l) && su(h))
          return !1;
        const w = Wa(h.code, c);
        if (s.current.add(h[w]), Ha(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, m = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (r.current || !m) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const y = Wa(h.code, c);
        Ha(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[y]), h.key === "Meta" && s.current.clear(), r.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Ha(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((r) => t.has(r)));
}
function Wa(e, t) {
  return t.includes(e) ? "code" : "key";
}
const vw = () => {
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
      const { width: i, height: r, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = rs(t, i, r, s, a, n?.padding ?? 0.1);
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
function Au(e, t) {
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
      bw(u, c);
    n.push(c);
  }
  return r.length && r.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function bw(e, t) {
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
function _u(e, t) {
  return Au(e, t);
}
function Du(e, t) {
  return Au(e, t);
}
function bt(e, t) {
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
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(bt(s.id, a)));
  }
  return i;
}
function Fa({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((r) => [r.id, r]));
  for (const [r, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: r });
  }
  for (const [r] of t)
    i.get(r) === void 0 && n.push({ id: r, type: "remove" });
  return n;
}
function Ba(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Tu = nu();
function $u(e, t, n = {}) {
  return rx(e, t, {
    ...n,
    onError: n.onError ?? Tu
  });
}
function Nw(e, t, n, i = { shouldReplaceId: !0 }) {
  return ox(e, t, n, {
    ...i,
    onError: i.onError ?? Tu
  });
}
const Ka = (e) => Bm(e), jw = (e) => Jl(e);
function Pu(e) {
  return $c(e);
}
const Sw = typeof window < "u" ? Xd : G;
function Xa(e) {
  const [t, n] = B(BigInt(0)), [i] = B(() => Cw(() => n((r) => r + BigInt(1))));
  return Sw(() => {
    const r = i.get();
    r.length && (e(r), i.reset());
  }, [t]), i;
}
function Cw(e) {
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
const Mu = Oo(null);
function Ew({ children: e }) {
  const t = ve(), n = oe((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: y } = t.getState();
    let w = u;
    for (const m of c)
      w = typeof m == "function" ? m(w) : m;
    let x = Fa({
      items: w,
      lookup: p
    });
    for (const m of y.values())
      x = m(x);
    d && l(w), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: m, nodes: b, setNodes: g } = t.getState();
      m && g(b);
    });
  }, []), i = Xa(n), r = oe((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const y of c)
      h = typeof y == "function" ? y(h) : y;
    d ? l(h) : f && f(Fa({
      items: h,
      lookup: p
    }));
  }, []), s = Xa(r), a = de(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return o.jsx(Mu.Provider, { value: a, children: e });
}
function kw() {
  const e = Wn(Mu);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const Iw = (e) => !!e.panZoom;
function ds() {
  const e = vw(), t = ve(), n = kw(), i = pe(Iw), r = de(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), y = Ka(f) ? f : p.get(f.id), w = y.parentId ? ru(y.position, y.measured, y.parentId, p, h) : y.position, x = {
        ...y,
        position: w,
        width: y.measured?.width ?? y.width,
        height: y.measured?.height ?? y.height
      };
      return nn(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && Ka(x) ? x : { ...w, ...x };
        }
        return w;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((y) => y.map((w) => {
        if (w.id === f) {
          const x = typeof p == "function" ? p(w) : p;
          return h.replace && jw(x) ? x : { ...w, ...x };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [y, w, x] = h;
        return {
          nodes: f.map((m) => ({ ...m })),
          edges: p.map((m) => ({ ...m })),
          viewport: {
            x: y,
            y: w,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: y, onNodesDelete: w, onEdgesDelete: x, triggerNodeChanges: m, triggerEdgeChanges: b, onDelete: g, onBeforeDelete: v } = t.getState(), { nodes: j, edges: N } = await Um({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: y,
          onBeforeDelete: v
        }), S = N.length > 0, k = j.length > 0;
        if (S) {
          const D = N.map(Ba);
          x?.(N), b(D);
        }
        if (k) {
          const D = j.map(Ba);
          w?.(j), m(D);
        }
        return (k || S) && g?.({ nodes: j, edges: N }), { deletedNodes: j, deletedEdges: N };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const y = ga(f), w = y ? f : u(f), x = h !== void 0;
        return w ? (h || t.getState().nodes).filter((m) => {
          const b = t.getState().nodeLookup.get(m.id);
          if (b && !y && (m.id === f.id || !b.internals.positionAbsolute))
            return !1;
          const g = nn(x ? m : b), v = Vn(g, w);
          return p && v > 0 || v >= g.width * g.height || v >= w.width * w.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const w = ga(f) ? f : u(f);
        if (!w)
          return !1;
        const x = Vn(w, p);
        return h && x > 0 || x >= p.width * p.height || x >= w.width * w.height;
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
        return Km(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Jm();
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
const Ya = (e) => e.selected, Aw = typeof window < "u" ? window : void 0;
function _w({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = ve(), { deleteElements: i } = ds(), r = Hn(e, { actInsideInputWithModifier: !1 }), s = Hn(t, { target: Aw });
  G(() => {
    if (r) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Ya), edges: a.filter(Ya) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [r]), G(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function Dw(e) {
  const t = ve();
  G(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = os(e.current);
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
const fr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, Tw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function $w({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: r = 0.5, panOnScrollMode: s = Ct.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: y, noWheelClassName: w, noPanClassName: x, onViewportChange: m, isControlledViewport: b, paneClickDistance: g, selectionOnDrag: v }) {
  const j = ve(), N = ne(null), { userSelectionActive: S, lib: k, connectionInProgress: D } = pe(Tw, we), M = Hn(p), A = ne();
  Dw(N);
  const I = oe((R) => {
    m?.({ x: R[0], y: R[1], zoom: R[2] }), b || j.setState({ transform: R });
  }, [m, b]);
  return G(() => {
    if (N.current) {
      A.current = Mx({
        domNode: N.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (E) => j.setState((T) => T.paneDragging === E ? T : { paneDragging: E }),
        onPanZoomStart: (E, T) => {
          const { onViewportChangeStart: P, onMoveStart: $ } = j.getState();
          $?.(E, T), P?.(T);
        },
        onPanZoom: (E, T) => {
          const { onViewportChange: P, onMove: $ } = j.getState();
          $?.(E, T), P?.(T);
        },
        onPanZoomEnd: (E, T) => {
          const { onViewportChangeEnd: P, onMoveEnd: $ } = j.getState();
          $?.(E, T), P?.(T);
        }
      });
      const { x: R, y: C, zoom: _ } = A.current.getViewport();
      return j.setState({
        panZoom: A.current,
        transform: [R, C, _],
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
      panOnScroll: i,
      panOnScrollSpeed: r,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: M,
      preventScrolling: h,
      noPanClassName: x,
      userSelectionActive: S,
      noWheelClassName: w,
      lib: k,
      onTransformChange: I,
      connectionInProgress: D,
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
    x,
    S,
    w,
    k,
    I,
    D,
    v,
    g
  ]), o.jsx("div", { className: "react-flow__renderer", ref: N, style: fr, children: y });
}
const Pw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function Mw() {
  const { userSelectionActive: e, userSelectionRect: t } = pe(Pw, we);
  return e && t ? o.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const fo = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, Rw = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function zw({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Ln.Full, panOnDrag: i, autoPanOnSelection: r, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: y, children: w }) {
  const x = ne(0), m = ve(), { userSelectionActive: b, elementsSelectable: g, dragging: v, connectionInProgress: j, panBy: N, autoPanSpeed: S } = pe(Rw, we), k = g && (e || b), D = ne(null), M = ne(), A = ne(/* @__PURE__ */ new Set()), I = ne(/* @__PURE__ */ new Set()), R = ne(!1), C = ne({ x: 0, y: 0 }), _ = ne(!1), E = (z) => {
    if (R.current || j) {
      R.current = !1;
      return;
    }
    l?.(z), m.getState().resetSelectedElements(), m.setState({ nodesSelectionActive: !1 });
  }, T = (z) => {
    if (Array.isArray(i) && i?.includes(2)) {
      z.preventDefault();
      return;
    }
    d?.(z);
  }, P = f ? (z) => f(z) : void 0, $ = (z) => {
    R.current && (z.stopPropagation(), R.current = !1);
  }, F = (z) => {
    const { domNode: Y, transform: ae } = m.getState();
    if (M.current = Y?.getBoundingClientRect(), !M.current)
      return;
    const ce = z.target === D.current;
    if (!ce && !!z.target.closest(".nokey") || !e || !(a && ce || t) || z.button !== 0 || !z.isPrimary)
      return;
    z.target?.setPointerCapture?.(z.pointerId), R.current = !1;
    const { x: fe, y: V } = Ge(z.nativeEvent, M.current), Q = ln({ x: fe, y: V }, ae);
    m.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: Q.x,
        startY: Q.y,
        x: fe,
        y: V
      }
    }), ce || (z.stopPropagation(), z.preventDefault());
  };
  function H(z, Y) {
    const { userSelectionRect: ae } = m.getState();
    if (!ae)
      return;
    const { transform: ce, nodeLookup: J, edgeLookup: re, connectionLookup: fe, triggerNodeChanges: V, triggerEdgeChanges: Q, defaultEdgeOptions: ge } = m.getState(), me = { x: ae.startX, y: ae.startY }, { x: Re, y: _e } = rn(me, ce), Te = {
      startX: me.x,
      startY: me.y,
      x: z < Re ? z : Re,
      y: Y < _e ? Y : _e,
      width: Math.abs(z - Re),
      height: Math.abs(Y - _e)
    }, ot = A.current, Xe = I.current;
    A.current = new Set(ns(J, Te, ce, n === Ln.Partial, !0).map(($e) => $e.id)), I.current = /* @__PURE__ */ new Set();
    const Ye = ge?.selectable ?? !0;
    for (const $e of A.current) {
      const Oe = fe.get($e);
      if (Oe)
        for (const { edgeId: He } of Oe.values()) {
          const Ne = re.get(He);
          Ne && (Ne.selectable ?? Ye) && I.current.add(He);
        }
    }
    if (!ya(ot, A.current)) {
      const $e = Kt(J, A.current, !0);
      V($e);
    }
    if (!ya(Xe, I.current)) {
      const $e = Kt(re, I.current);
      Q($e);
    }
    m.setState({
      userSelectionRect: Te,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function O() {
    if (!r || !M.current)
      return;
    const [z, Y] = is(C.current, M.current, S);
    N({ x: z, y: Y }).then((ae) => {
      if (!R.current || !ae) {
        x.current = requestAnimationFrame(O);
        return;
      }
      const { x: ce, y: J } = C.current;
      H(ce, J), x.current = requestAnimationFrame(O);
    });
  }
  const X = () => {
    cancelAnimationFrame(x.current), x.current = 0, _.current = !1;
  };
  G(() => () => X(), []);
  const q = (z) => {
    const { userSelectionRect: Y, transform: ae, resetSelectedElements: ce } = m.getState();
    if (!M.current || !Y)
      return;
    const { x: J, y: re } = Ge(z.nativeEvent, M.current);
    C.current = { x: J, y: re };
    const fe = rn({ x: Y.startX, y: Y.startY }, ae);
    if (!R.current) {
      const V = t ? 0 : s;
      if (Math.hypot(J - fe.x, re - fe.y) <= V)
        return;
      ce(), c?.(z);
    }
    R.current = !0, _.current || (O(), _.current = !0), H(J, re);
  }, ee = (z) => {
    z.button === 0 && (z.target?.releasePointerCapture?.(z.pointerId), !b && z.target === D.current && m.getState().userSelectionRect && E?.(z), m.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), R.current && (u?.(z), m.setState({
      nodesSelectionActive: A.current.size > 0
    })), X());
  }, le = (z) => {
    z.target?.releasePointerCapture?.(z.pointerId), X();
  }, U = i === !0 || Array.isArray(i) && i.includes(0);
  return o.jsxs("div", { className: ke(["react-flow__pane", { draggable: U, dragging: v, selection: e }]), onClick: k ? void 0 : fo(E, D), onContextMenu: fo(T, D), onWheel: fo(P, D), onPointerEnter: k ? void 0 : p, onPointerMove: k ? q : h, onPointerUp: k ? ee : void 0, onPointerCancel: k ? le : void 0, onPointerDownCapture: k ? F : void 0, onClickCapture: k ? $ : void 0, onPointerLeave: y, ref: D, style: fr, children: [w, o.jsx(Mw, {})] });
}
function To({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: r, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : r([e]);
}
function Ru({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: r, isSelectable: s, nodeClickDistance: a }) {
  const c = ve(), [u, l] = B(!1), d = ne();
  return G(() => {
    d.current = bx({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        To({
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
const Lw = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function zu() {
  const e = ve();
  return oe((n) => {
    const { nodeExtent: i, snapToGrid: r, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Lw(a), h = r ? s[0] : 5, y = r ? s[1] : 5, w = n.direction.x * h * n.factor, x = n.direction.y * y * n.factor;
    for (const [, m] of l) {
      if (!p(m))
        continue;
      let b = {
        x: m.internals.positionAbsolute.x + w,
        y: m.internals.positionAbsolute.y + x
      };
      r && (b = Gn(b, s));
      const { position: g, positionAbsolute: v } = Ql({
        nodeId: m.id,
        nextPosition: b,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      m.position = g, m.internals.positionAbsolute = v, f.set(m.id, m);
    }
    u(f);
  }, []);
}
const fs = Oo(null), Vw = fs.Provider;
fs.Consumer;
const Lu = () => Wn(fs), Ow = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Hw = (e, t, n) => (i) => {
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
function Ww({ type: e = "source", position: t = ie.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: r = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const y = a || null, w = e === "target", x = ve(), m = Lu(), { connectOnClick: b, noPanClassName: g, rfId: v } = pe(Ow, we), { connectingFrom: j, connectingTo: N, clickConnecting: S, isPossibleEndHandle: k, connectionInProcess: D, clickConnectionInProcess: M, valid: A } = pe(Hw(m, y, e), we);
  m || x.getState().onError?.("010", Ke.error010());
  const I = (_) => {
    const { defaultEdgeOptions: E, onConnect: T, hasDefaultEdges: P } = x.getState(), $ = {
      ...E,
      ..._
    };
    if (P) {
      const { edges: F, setEdges: H, onError: O } = x.getState();
      H($u($, F, { onError: O }));
    }
    T?.($), c?.($);
  }, R = (_) => {
    if (!m)
      return;
    const E = au(_.nativeEvent);
    if (r && (E && _.button === 0 || !E)) {
      const T = x.getState();
      Do.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: T.autoPanOnConnect,
        connectionMode: T.connectionMode,
        connectionRadius: T.connectionRadius,
        domNode: T.domNode,
        nodeLookup: T.nodeLookup,
        lib: T.lib,
        isTarget: w,
        handleId: y,
        nodeId: m,
        flowId: T.rfId,
        panBy: T.panBy,
        cancelConnection: T.cancelConnection,
        onConnectStart: T.onConnectStart,
        onConnectEnd: (...P) => x.getState().onConnectEnd?.(...P),
        updateConnection: T.updateConnection,
        onConnect: I,
        isValidConnection: n || ((...P) => x.getState().isValidConnection?.(...P) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: T.autoPanSpeed,
        dragThreshold: T.connectionDragThreshold
      });
    }
    E ? d?.(_) : f?.(_);
  }, C = (_) => {
    const { onClickConnectStart: E, onClickConnectEnd: T, connectionClickStartHandle: P, connectionMode: $, isValidConnection: F, lib: H, rfId: O, nodeLookup: X, connection: q } = x.getState();
    if (!m || !P && !r)
      return;
    if (!P) {
      E?.(_.nativeEvent, { nodeId: m, handleId: y, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: m, type: e, id: y } });
      return;
    }
    const ee = ou(_.target), le = n || F, { connection: U, isValid: z } = Do.isValid(_.nativeEvent, {
      handle: {
        nodeId: m,
        id: y,
        type: e
      },
      connectionMode: $,
      fromNodeId: P.nodeId,
      fromHandleId: P.id || null,
      fromType: P.type,
      isValidConnection: le,
      flowId: O,
      doc: ee,
      lib: H,
      nodeLookup: X
    });
    z && U && I(U);
    const Y = structuredClone(q);
    delete Y.inProgress, Y.toPosition = Y.toHandle ? Y.toHandle.position : null, T?.(_, Y), x.setState({ connectionClickStartHandle: null });
  };
  return o.jsx("div", { "data-handleid": y, "data-nodeid": m, "data-handlepos": t, "data-id": `${v}-${m}-${y}-${e}`, className: ke([
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
      valid: A,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!D || k) && (D || M ? s : r)
    }
  ]), onMouseDown: R, onTouchStart: R, onClick: b ? C : void 0, ref: h, ...p, children: u });
}
const sn = Se(Pu(Ww));
function Fw({ data: e, isConnectable: t, sourcePosition: n = ie.Bottom }) {
  return o.jsxs(o.Fragment, { children: [e?.label, o.jsx(sn, { type: "source", position: n, isConnectable: t })] });
}
function Bw({ data: e, isConnectable: t, targetPosition: n = ie.Top, sourcePosition: i = ie.Bottom }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label, o.jsx(sn, { type: "source", position: i, isConnectable: t })] });
}
function Kw() {
  return null;
}
function Xw({ data: e, isConnectable: t, targetPosition: n = ie.Top }) {
  return o.jsxs(o.Fragment, { children: [o.jsx(sn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const Zi = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, qa = {
  input: Fw,
  default: Bw,
  output: Xw,
  group: Kw
};
function Yw(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const qw = (e) => {
  const { width: t, height: n, x: i, y: r } = Zn(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ze(t) ? t : null,
    height: Ze(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${r}px)`
  };
};
function Uw({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = ve(), { width: r, height: s, transformString: a, userSelectionActive: c } = pe(qw, we), u = zu(), l = ne(null);
  G(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && r !== null && s !== null;
  if (Ru({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const y = i.getState().nodes.filter((w) => w.selected);
    e(h, y);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(Zi, h.key) && (h.preventDefault(), u({
      direction: Zi[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return o.jsx("div", { className: ke(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: o.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: r,
    height: s
  } }) });
}
const Ua = typeof window < "u" ? window : void 0, Zw = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Vu({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: y, panActivationKeyCode: w, zoomActivationKeyCode: x, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: v, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: k, autoPanOnSelection: D, defaultViewport: M, translateExtent: A, minZoom: I, maxZoom: R, preventScrolling: C, onSelectionContextMenu: _, noWheelClassName: E, noPanClassName: T, disableKeyboardA11y: P, onViewportChange: $, isControlledViewport: F }) {
  const { nodesSelectionActive: H, userSelectionActive: O } = pe(Zw, we), X = Hn(l, { target: Ua }), q = Hn(w, { target: Ua }), ee = q || k, le = q || v, U = d && ee !== !0, z = X || O || U;
  return _w({ deleteKeyCode: u, multiSelectionKeyCode: y }), o.jsx($w, { onPaneContextMenu: s, elementsSelectable: m, zoomOnScroll: b, zoomOnPinch: g, panOnScroll: le, panOnScrollSpeed: j, panOnScrollMode: N, zoomOnDoubleClick: S, panOnDrag: !X && ee, defaultViewport: M, translateExtent: A, minZoom: I, maxZoom: R, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: E, noPanClassName: T, onViewportChange: $, isControlledViewport: F, paneClickDistance: c, selectionOnDrag: U, children: o.jsxs(zw, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: r, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: ee, autoPanOnSelection: D, isSelecting: !!z, selectionMode: f, selectionKeyPressed: X, paneClickDistance: c, selectionOnDrag: U, children: [e, H && o.jsx(Uw, { onSelectionContextMenu: _, noPanClassName: T, disableKeyboardA11y: P })] }) });
}
Vu.displayName = "FlowRenderer";
const Gw = Se(Vu), Jw = (e) => (t) => e ? ns(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Qw(e) {
  return pe(oe(Jw(e), [e]), we);
}
const ev = (e) => e.updateNodeInternals;
function tv() {
  const e = pe(ev), [t] = B(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
  return G(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function nv({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const r = ve(), s = ne(null), a = ne(null), c = ne(e.sourcePosition), u = ne(e.targetPosition), l = ne(t), d = n && !!e.internals.handleBounds;
  return G(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), G(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), G(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, r.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function iv({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: r, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: y, rfId: w, nodeTypes: x, nodeClickDistance: m, onError: b }) {
  const { node: g, internals: v, isParent: j } = pe((z) => {
    const Y = z.nodeLookup.get(e), ae = z.parentLookup.has(e);
    return {
      node: Y,
      internals: Y.internals,
      isParent: ae
    };
  }, we);
  let N = g.type || "default", S = x?.[N] || qa[N];
  S === void 0 && (b?.("003", Ke.error003(N)), N = "default", S = x?.default || qa.default);
  const k = !!(g.draggable || c && typeof g.draggable > "u"), D = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), A = !!(g.focusable || d && typeof g.focusable > "u"), I = ve(), R = iu(g), C = nv({ node: g, nodeType: N, hasDimensions: R, resizeObserver: f }), _ = Ru({
    nodeRef: C,
    disabled: g.hidden || !k,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: m
  }), E = zu();
  if (g.hidden)
    return null;
  const T = dt(g), P = Yw(g), $ = D || k || t || n || i || r, F = n ? (z) => n(z, { ...v.userNode }) : void 0, H = i ? (z) => i(z, { ...v.userNode }) : void 0, O = r ? (z) => r(z, { ...v.userNode }) : void 0, X = s ? (z) => s(z, { ...v.userNode }) : void 0, q = a ? (z) => a(z, { ...v.userNode }) : void 0, ee = (z) => {
    const { selectNodesOnDrag: Y, nodeDragThreshold: ae } = I.getState();
    D && (!Y || !k || ae > 0) && To({
      id: e,
      store: I,
      nodeRef: C
    }), t && t(z, { ...v.userNode });
  }, le = (z) => {
    if (!(su(z.nativeEvent) || y)) {
      if (ql.includes(z.key) && D) {
        const Y = z.key === "Escape";
        To({
          id: e,
          store: I,
          unselect: Y,
          nodeRef: C
        });
      } else if (k && g.selected && Object.prototype.hasOwnProperty.call(Zi, z.key)) {
        z.preventDefault();
        const { ariaLabelConfig: Y } = I.getState();
        I.setState({
          ariaLiveMessage: Y["node.a11yDescription.ariaLiveMessage"]({
            direction: z.key.replace("Arrow", "").toLowerCase(),
            x: ~~v.positionAbsolute.x,
            y: ~~v.positionAbsolute.y
          })
        }), E({
          direction: Zi[z.key],
          factor: z.shiftKey ? 4 : 1
        });
      }
    }
  }, U = () => {
    if (y || !C.current?.matches(":focus-visible"))
      return;
    const { transform: z, width: Y, height: ae, autoPanOnNodeFocus: ce, setCenter: J } = I.getState();
    if (!ce)
      return;
    ns(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: Y, height: ae }, z, !0).length > 0 || J(g.position.x + T.width / 2, g.position.y + T.height / 2, {
      zoom: z[2]
    });
  };
  return o.jsx("div", { className: ke([
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
      dragging: _
    }
  ]), ref: C, style: {
    zIndex: v.z,
    transform: `translate(${v.positionAbsolute.x}px,${v.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: R ? "visible" : "hidden",
    ...g.style,
    ...P
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: F, onMouseMove: H, onMouseLeave: O, onContextMenu: X, onClick: ee, onDoubleClick: q, onKeyDown: A ? le : void 0, tabIndex: A ? 0 : void 0, onFocus: A ? U : void 0, role: g.ariaRole ?? (A ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": y ? void 0 : `${Eu}-${w}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: o.jsx(Vw, { value: e, children: o.jsx(S, { id: e, data: g.data, type: N, positionAbsoluteX: v.positionAbsolute.x, positionAbsoluteY: v.positionAbsolute.y, selected: g.selected ?? !1, selectable: D, draggable: k, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: v.z, parentId: g.parentId, ...T }) }) });
}
var rv = Se(iv);
const ov = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ou(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, onError: s } = pe(ov, we), a = Qw(e.onlyRenderVisibleElements), c = tv();
  return o.jsx("div", { className: "react-flow__nodes", style: fr, children: a.map((u) => (
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
    o.jsx(rv, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: r, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Ou.displayName = "NodeRenderer";
const sv = Se(Ou);
function av(e) {
  return pe(oe((n) => {
    if (!e)
      return n.edges.map((r) => r.id);
    const i = [];
    if (n.width && n.height)
      for (const r of n.edges) {
        const s = n.nodeLookup.get(r.source), a = n.nodeLookup.get(r.target);
        s && a && nx({
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
const cv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return o.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, lv = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return o.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Za = {
  [Yi.Arrow]: cv,
  [Yi.ArrowClosed]: lv
};
function uv(e) {
  const t = ve();
  return de(() => Object.prototype.hasOwnProperty.call(Za, e) ? Za[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const dv = ({ id: e, type: t, color: n, width: i = 12.5, height: r = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = uv(t);
  return u ? o.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${r}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: o.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Hu = ({ defaultColor: e, rfId: t }) => {
  const n = pe((s) => s.edges), i = pe((s) => s.defaultEdgeOptions), r = de(() => ux(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return r.length ? o.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: o.jsx("defs", { children: r.map((s) => o.jsx(dv, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Hu.displayName = "MarkerDefinitions";
var fv = Se(Hu);
function Wu({ x: e, y: t, label: n, labelStyle: i, labelShowBg: r = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = B({ x: 1, y: 0, width: 0, height: 0 }), h = ke(["react-flow__edge-textwrapper", l]), y = ne(null);
  return G(() => {
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
Wu.displayName = "EdgeText";
const pv = Se(Wu);
function Jn({ path: e, labelX: t, labelY: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return o.jsxs(o.Fragment, { children: [o.jsx("path", { ...d, d: e, fill: "none", className: ke(["react-flow__edge-path", d.className]) }), l ? o.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ze(t) && Ze(n) ? o.jsx(pv, { x: t, y: n, label: i, labelStyle: r, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Ga({ pos: e, x1: t, y1: n, x2: i, y2: r }) {
  return e === ie.Left || e === ie.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + r)];
}
function Fu({ sourceX: e, sourceY: t, sourcePosition: n = ie.Bottom, targetX: i, targetY: r, targetPosition: s = ie.Top }) {
  const [a, c] = Ga({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: r
  }), [u, l] = Ga({
    pos: s,
    x1: i,
    y1: r,
    x2: e,
    y2: t
  }), [d, f, p, h] = cu({
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
function Bu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m }) => {
    const [b, g, v] = Fu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c
    }), j = e.isInternal ? void 0 : t;
    return o.jsx(Jn, { id: j, path: b, labelX: g, labelY: v, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: m });
  });
}
const hv = Bu({ isInternal: !1 }), Ku = Bu({ isInternal: !0 });
hv.displayName = "SimpleBezierEdge";
Ku.displayName = "SimpleBezierEdgeInternal";
function Xu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ie.Bottom, targetPosition: y = ie.Top, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = Ui({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: r,
      targetY: s,
      targetPosition: y,
      borderRadius: m?.borderRadius,
      offset: m?.offset,
      stepPosition: m?.stepPosition
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(Jn, { id: N, path: g, labelX: v, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const Yu = Xu({ isInternal: !1 }), qu = Xu({ isInternal: !0 });
Yu.displayName = "SmoothStepEdge";
qu.displayName = "SmoothStepEdgeInternal";
function Uu(e) {
  return Se(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return o.jsx(Yu, { ...n, id: i, pathOptions: de(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const gv = Uu({ isInternal: !1 }), Zu = Uu({ isInternal: !0 });
gv.displayName = "StepEdge";
Zu.displayName = "StepEdgeInternal";
function Gu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w }) => {
    const [x, m, b] = fu({ sourceX: n, sourceY: i, targetX: r, targetY: s }), g = e.isInternal ? void 0 : t;
    return o.jsx(Jn, { id: g, path: x, labelX: m, labelY: b, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: y, interactionWidth: w });
  });
}
const yv = Gu({ isInternal: !1 }), Ju = Gu({ isInternal: !0 });
yv.displayName = "StraightEdge";
Ju.displayName = "StraightEdgeInternal";
function Qu(e) {
  return Se(({ id: t, sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a = ie.Bottom, targetPosition: c = ie.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, pathOptions: m, interactionWidth: b }) => {
    const [g, v, j] = lu({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: r,
      targetY: s,
      targetPosition: c,
      curvature: m?.curvature
    }), N = e.isInternal ? void 0 : t;
    return o.jsx(Jn, { id: N, path: g, labelX: v, labelY: j, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: y, markerEnd: w, markerStart: x, interactionWidth: b });
  });
}
const mv = Qu({ isInternal: !1 }), ed = Qu({ isInternal: !0 });
mv.displayName = "BezierEdge";
ed.displayName = "BezierEdgeInternal";
const Ja = {
  default: ed,
  straight: Ju,
  step: Zu,
  smoothstep: qu,
  simplebezier: Ku
}, Qa = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, xv = (e, t, n) => n === ie.Left ? e - t : n === ie.Right ? e + t : e, wv = (e, t, n) => n === ie.Top ? e - t : n === ie.Bottom ? e + t : e, ec = "react-flow__edgeupdater";
function tc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: r, onMouseEnter: s, onMouseOut: a, type: c }) {
  return o.jsx("circle", { onMouseDown: r, onMouseEnter: s, onMouseOut: a, className: ke([ec, `${ec}-${c}`]), cx: xv(t, i, e), cy: wv(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function vv({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: r, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const y = ve(), w = (v, j) => {
    if (v.button !== 0)
      return;
    const { autoPanOnConnect: N, domNode: S, connectionMode: k, connectionRadius: D, lib: M, onConnectStart: A, cancelConnection: I, nodeLookup: R, rfId: C, panBy: _, updateConnection: E } = y.getState(), T = j.type === "target", P = (H, O) => {
      p(!1), f?.(H, n, j.type, O);
    }, $ = (H) => l?.(n, H), F = (H, O) => {
      p(!0), d?.(v, n, j.type), A?.(H, O);
    };
    Do.onPointerDown(v.nativeEvent, {
      autoPanOnConnect: N,
      connectionMode: k,
      connectionRadius: D,
      domNode: S,
      handleId: j.id,
      nodeId: j.nodeId,
      nodeLookup: R,
      isTarget: T,
      edgeUpdaterType: j.type,
      lib: M,
      flowId: C,
      cancelConnection: I,
      panBy: _,
      isValidConnection: (...H) => y.getState().isValidConnection?.(...H) ?? !0,
      onConnect: $,
      onConnectStart: F,
      onConnectEnd: (...H) => y.getState().onConnectEnd?.(...H),
      onReconnectEnd: P,
      updateConnection: E,
      getTransform: () => y.getState().transform,
      getFromHandle: () => y.getState().connection.fromHandle,
      dragThreshold: y.getState().connectionDragThreshold,
      handleDomNode: v.currentTarget
    });
  }, x = (v) => w(v, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), m = (v) => w(v, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), b = () => h(!0), g = () => h(!1);
  return o.jsxs(o.Fragment, { children: [(e === !0 || e === "source") && o.jsx(tc, { position: c, centerX: i, centerY: r, radius: t, onMouseDown: x, onMouseEnter: b, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && o.jsx(tc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: m, onMouseEnter: b, onMouseOut: g, type: "target" })] });
}
function bv({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: r, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: y, edgeTypes: w, noPanClassName: x, onError: m, disableKeyboardA11y: b }) {
  let g = pe((J) => J.edgeLookup.get(e));
  const v = pe((J) => J.defaultEdgeOptions);
  g = v ? { ...v, ...g } : g;
  let j = g.type || "default", N = w?.[j] || Ja[j];
  N === void 0 && (m?.("011", Ke.error011(j)), j = "default", N = w?.default || Ja.default);
  const S = !!(g.focusable || t && typeof g.focusable > "u"), k = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), D = !!(g.selectable || i && typeof g.selectable > "u"), M = ne(null), [A, I] = B(!1), [R, C] = B(!1), _ = ve(), { zIndex: E, sourceX: T, sourceY: P, targetX: $, targetY: F, sourcePosition: H, targetPosition: O } = pe(oe((J) => {
    const re = J.nodeLookup.get(g.source), fe = J.nodeLookup.get(g.target);
    if (!re || !fe)
      return {
        zIndex: g.zIndex,
        ...Qa
      };
    const V = lx({
      id: e,
      sourceNode: re,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: J.connectionMode,
      onError: m
    });
    return {
      zIndex: tx({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: re,
        targetNode: fe,
        elevateOnSelect: J.elevateEdgesOnSelect,
        zIndexMode: J.zIndexMode
      }),
      ...V || Qa
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), we), X = de(() => g.markerStart ? `url('#${Ao(g.markerStart, y)}')` : void 0, [g.markerStart, y]), q = de(() => g.markerEnd ? `url('#${Ao(g.markerEnd, y)}')` : void 0, [g.markerEnd, y]);
  if (g.hidden || T === null || P === null || $ === null || F === null)
    return null;
  const ee = (J) => {
    const { addSelectedEdges: re, unselectNodesAndEdges: fe, multiSelectionActive: V } = _.getState();
    D && (_.setState({ nodesSelectionActive: !1 }), g.selected && V ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : re([e])), r && r(J, g);
  }, le = s ? (J) => {
    s(J, { ...g });
  } : void 0, U = a ? (J) => {
    a(J, { ...g });
  } : void 0, z = c ? (J) => {
    c(J, { ...g });
  } : void 0, Y = u ? (J) => {
    u(J, { ...g });
  } : void 0, ae = l ? (J) => {
    l(J, { ...g });
  } : void 0, ce = (J) => {
    if (!b && ql.includes(J.key) && D) {
      const { unselectNodesAndEdges: re, addSelectedEdges: fe } = _.getState();
      J.key === "Escape" ? (M.current?.blur(), re({ edges: [g] })) : fe([e]);
    }
  };
  return o.jsx("svg", { style: { zIndex: E }, children: o.jsxs("g", { className: ke([
    "react-flow__edge",
    `react-flow__edge-${j}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !D && !r,
      updating: A,
      selectable: D
    }
  ]), onClick: ee, onDoubleClick: le, onContextMenu: U, onMouseEnter: z, onMouseMove: Y, onMouseLeave: ae, onKeyDown: S ? ce : void 0, tabIndex: S ? 0 : void 0, role: g.ariaRole ?? (S ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": S ? `${ku}-${y}` : void 0, ref: M, ...g.domAttributes, children: [!R && o.jsx(N, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: D, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: T, sourceY: P, targetX: $, targetY: F, sourcePosition: H, targetPosition: O, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: X, markerEnd: q, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), k && o.jsx(vv, { edge: g, isReconnectable: k, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: T, sourceY: P, targetX: $, targetY: F, sourcePosition: H, targetPosition: O, setUpdateHover: I, setReconnecting: C })] }) });
}
var Nv = Se(bv);
const jv = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function td({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: r, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, disableKeyboardA11y: w }) {
  const { edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, onError: g } = pe(jv, we), v = av(t);
  return o.jsxs("div", { className: "react-flow__edges", children: [o.jsx(fv, { defaultColor: e, rfId: n }), v.map((j) => o.jsx(Nv, { id: j, edgesFocusable: x, edgesReconnectable: m, elementsSelectable: b, noPanClassName: r, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: y, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: w }, j))] });
}
td.displayName = "EdgeRenderer";
const Sv = Se(td), Cv = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function Ev({ children: e }) {
  const t = pe(Cv);
  return o.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function kv(e) {
  const t = ds(), n = ne(!1);
  G(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const Iv = (e) => e.panZoom?.syncViewport;
function Av(e) {
  const t = pe(Iv), n = ve();
  return G(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function _v(e) {
  return e.connection.inProgress ? { ...e.connection, to: ln(e.connection.to, e.transform) } : { ...e.connection };
}
function Dv(e) {
  return _v;
}
function Tv(e) {
  const t = Dv();
  return pe(t, we);
}
const $v = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function Pv({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: r, width: s, height: a, isValid: c, inProgress: u } = pe($v, we);
  return !(s && r && u) ? null : o.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: o.jsx("g", { className: ke(["react-flow__connection", Gl(c)]), children: o.jsx(nd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const nd = ({ style: e, type: t = gt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: r, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = Tv();
  if (!r)
    return;
  if (n)
    return o.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Gl(i), toNode: d, toHandle: f, pointer: h });
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
    case gt.Bezier:
      [y] = lu(w);
      break;
    case gt.SimpleBezier:
      [y] = Fu(w);
      break;
    case gt.Step:
      [y] = Ui({
        ...w,
        borderRadius: 0
      });
      break;
    case gt.SmoothStep:
      [y] = Ui(w);
      break;
    default:
      [y] = fu(w);
  }
  return o.jsx("path", { d: y, fill: "none", className: "react-flow__connection-path", style: e });
};
nd.displayName = "ConnectionLine";
const Mv = {};
function nc(e = Mv) {
  ne(e), ve(), G(() => {
  }, [e]);
}
function Rv() {
  ve(), ne(!1), G(() => {
  }, []);
}
function id({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: r, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: y, connectionLineStyle: w, connectionLineComponent: x, connectionLineContainerStyle: m, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, deleteKeyCode: k, onlyRenderVisibleElements: D, elementsSelectable: M, defaultViewport: A, translateExtent: I, minZoom: R, maxZoom: C, preventScrolling: _, defaultMarkerColor: E, zoomOnScroll: T, zoomOnPinch: P, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: H, zoomOnDoubleClick: O, panOnDrag: X, autoPanOnSelection: q, onPaneClick: ee, onPaneMouseEnter: le, onPaneMouseMove: U, onPaneMouseLeave: z, onPaneScroll: Y, onPaneContextMenu: ae, paneClickDistance: ce, nodeClickDistance: J, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: ge, onReconnect: me, onReconnectStart: Re, onReconnectEnd: _e, noDragClassName: Te, noWheelClassName: ot, noPanClassName: Xe, disableKeyboardA11y: Ye, nodeExtent: $e, rfId: Oe, viewport: He, onViewportChange: Ne }) {
  return nc(e), nc(t), Rv(), kv(n), Av(He), o.jsx(Gw, { onPaneClick: ee, onPaneMouseEnter: le, onPaneMouseMove: U, onPaneMouseLeave: z, onPaneContextMenu: ae, onPaneScroll: Y, paneClickDistance: ce, deleteKeyCode: k, selectionKeyCode: b, selectionOnDrag: g, selectionMode: v, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: j, panActivationKeyCode: N, zoomActivationKeyCode: S, elementsSelectable: M, zoomOnScroll: T, zoomOnPinch: P, zoomOnDoubleClick: O, panOnScroll: $, panOnScrollSpeed: F, panOnScrollMode: H, panOnDrag: X, autoPanOnSelection: q, defaultViewport: A, translateExtent: I, minZoom: R, maxZoom: C, onSelectionContextMenu: f, preventScrolling: _, noDragClassName: Te, noWheelClassName: ot, noPanClassName: Xe, disableKeyboardA11y: Ye, onViewportChange: Ne, isControlledViewport: !!He, children: o.jsxs(Ev, { children: [o.jsx(Sv, { edgeTypes: t, onEdgeClick: r, onEdgeDoubleClick: a, onReconnect: me, onReconnectStart: Re, onReconnectEnd: _e, onlyRenderVisibleElements: D, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: V, onEdgeMouseLeave: Q, reconnectRadius: ge, defaultMarkerColor: E, noPanClassName: Xe, disableKeyboardA11y: Ye, rfId: Oe }), o.jsx(Pv, { style: w, type: y, component: x, containerStyle: m }), o.jsx("div", { className: "react-flow__edgelabel-renderer" }), o.jsx(sv, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: J, onlyRenderVisibleElements: D, noPanClassName: Xe, noDragClassName: Te, disableKeyboardA11y: Ye, nodeExtent: $e, rfId: Oe }), o.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
id.displayName = "GraphView";
const zv = Se(id), Lv = nu(), ic = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), y = /* @__PURE__ */ new Map(), w = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), m = i ?? t ?? [], b = n ?? e ?? [], g = d ?? [0, 0], v = f ?? zn;
  gu(w, x, m);
  const { nodesInitialized: j } = _o(b, h, y, {
    nodeOrigin: g,
    nodeExtent: v,
    zIndexMode: p
  });
  let N = [0, 0, 1];
  if (a && r && s) {
    const S = Zn(h, {
      filter: (A) => !!((A.width || A.initialWidth) && (A.height || A.initialHeight))
    }), { x: k, y: D, zoom: M } = rs(S, r, s, u, l, c?.padding ?? 0.1);
    N = [k, D, M];
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
    edges: m,
    edgeLookup: x,
    connectionLookup: w,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: zn,
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
    connection: { ...Zl },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: Lv,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Ul,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, Vv = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: r, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Jx((h, y) => {
  async function w() {
    const { nodeLookup: x, panZoom: m, fitViewOptions: b, fitViewResolver: g, width: v, height: j, minZoom: N, maxZoom: S } = y();
    m && (await qm({
      nodes: x,
      width: v,
      height: j,
      panZoom: m,
      minZoom: N,
      maxZoom: S
    }, b), g?.resolve(!0), h({ fitViewResolver: null }));
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
      nodeExtent: f,
      defaultNodes: n,
      defaultEdges: i,
      zIndexMode: p
    }),
    setNodes: (x) => {
      const { nodeLookup: m, parentLookup: b, nodeOrigin: g, elevateNodesOnSelect: v, fitViewQueued: j, zIndexMode: N, nodesSelectionActive: S } = y(), { nodesInitialized: k, hasSelectedNodes: D } = _o(x, m, b, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: v,
        checkEquality: !0,
        zIndexMode: N
      }), M = S && D;
      j && k ? (w(), h({
        nodes: x,
        nodesInitialized: k,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: x, nodesInitialized: k, nodesSelectionActive: M });
    },
    setEdges: (x) => {
      const { connectionLookup: m, edgeLookup: b } = y();
      gu(m, b, x), h({ edges: x });
    },
    setDefaultNodesAndEdges: (x, m) => {
      if (x) {
        const { setNodes: b } = y();
        b(x), h({ hasDefaultNodes: !0 });
      }
      if (m) {
        const { setEdges: b } = y();
        b(m), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: m, nodeLookup: b, parentLookup: g, domNode: v, nodeOrigin: j, nodeExtent: N, debug: S, fitViewQueued: k, zIndexMode: D } = y(), { changes: M, updatedInternals: A } = mx(x, b, g, v, j, N, D);
      A && (px(b, g, { nodeOrigin: j, nodeExtent: N, zIndexMode: D }), k ? (w(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (S && console.log("React Flow: trigger node changes", M), m?.(M)));
    },
    updateNodePositions: (x, m = !1) => {
      const b = [];
      let g = [];
      const { nodeLookup: v, triggerNodeChanges: j, connection: N, updateConnection: S, onNodesChangeMiddlewareMap: k } = y();
      for (const [D, M] of x) {
        const A = v.get(D), I = !!(A?.expandParent && A?.parentId && M?.position), R = {
          id: D,
          type: "position",
          position: I ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: m
        };
        if (A && N.inProgress && N.fromNode.id === A.id) {
          const C = Dt(A, N.fromHandle, ie.Left, !0);
          S({ ...N, from: C });
        }
        I && A.parentId && b.push({
          id: D,
          parentId: A.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(R);
      }
      if (b.length > 0) {
        const { parentLookup: D, nodeOrigin: M } = y(), A = us(b, v, D, M);
        g.push(...A);
      }
      for (const D of k.values())
        g = D(g);
      j(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: m, setNodes: b, nodes: g, hasDefaultNodes: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = _u(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger node changes", x), m?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: m, setEdges: b, edges: g, hasDefaultEdges: v, debug: j } = y();
      if (x?.length) {
        if (v) {
          const N = Du(x, g);
          b(N);
        }
        j && console.log("React Flow: trigger edge changes", x), m?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => bt(S, !0));
        v(N);
        return;
      }
      v(Kt(g, /* @__PURE__ */ new Set([...x]), !0)), j(Kt(b));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: m, edgeLookup: b, nodeLookup: g, triggerNodeChanges: v, triggerEdgeChanges: j } = y();
      if (m) {
        const N = x.map((S) => bt(S, !0));
        j(N);
        return;
      }
      j(Kt(b, /* @__PURE__ */ new Set([...x]))), v(Kt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: m } = {}) => {
      const { edges: b, nodes: g, nodeLookup: v, triggerNodeChanges: j, triggerEdgeChanges: N } = y(), S = x || g, k = m || b, D = [];
      for (const A of S) {
        if (!A.selected)
          continue;
        const I = v.get(A.id);
        I && (I.selected = !1), D.push(bt(A.id, !1));
      }
      const M = [];
      for (const A of k)
        A.selected && M.push(bt(A.id, !1));
      j(D), N(M);
    },
    setMinZoom: (x) => {
      const { panZoom: m, maxZoom: b } = y();
      m?.setScaleExtent([x, b]), h({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: m, minZoom: b } = y();
      m?.setScaleExtent([b, x]), h({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      y().panZoom?.setTranslateExtent(x), h({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: m, triggerNodeChanges: b, triggerEdgeChanges: g, elementsSelectable: v } = y();
      if (!v)
        return;
      const j = m.reduce((S, k) => k.selected ? [...S, bt(k.id, !1)] : S, []), N = x.reduce((S, k) => k.selected ? [...S, bt(k.id, !1)] : S, []);
      b(j), g(N);
    },
    setNodeExtent: (x) => {
      const { nodes: m, nodeLookup: b, parentLookup: g, nodeOrigin: v, elevateNodesOnSelect: j, nodeExtent: N, zIndexMode: S } = y();
      x[0][0] === N[0][0] && x[0][1] === N[0][1] && x[1][0] === N[1][0] && x[1][1] === N[1][1] || (_o(m, b, g, {
        nodeOrigin: v,
        nodeExtent: x,
        elevateNodesOnSelect: j,
        checkEquality: !1,
        zIndexMode: S
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: m, width: b, height: g, panZoom: v, translateExtent: j } = y();
      return xx({ delta: x, panZoom: v, transform: m, translateExtent: j, width: b, height: g });
    },
    setCenter: async (x, m, b) => {
      const { width: g, height: v, maxZoom: j, panZoom: N } = y();
      if (!N)
        return !1;
      const S = typeof b?.zoom < "u" ? b.zoom : j;
      return await N.setViewport({
        x: g / 2 - x * S,
        y: v / 2 - m * S,
        zoom: S
      }, { duration: b?.duration, ease: b?.ease, interpolate: b?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Zl }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...ic() })
  };
}, Object.is);
function Ov({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: r, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [y] = B(() => Vv({
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
  return o.jsx(nw, { value: y, children: o.jsx(Ew, { children: h }) });
}
function Hv({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: r, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Wn(ur) ? o.jsx(o.Fragment, { children: e }) : o.jsx(Ov, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: r, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const Wv = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function Fv({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: r, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: D, onNodesDelete: M, onEdgesDelete: A, onDelete: I, onSelectionChange: R, onSelectionDragStart: C, onSelectionDrag: _, onSelectionDragStop: E, onSelectionContextMenu: T, onSelectionStart: P, onSelectionEnd: $, onBeforeDelete: F, connectionMode: H, connectionLineType: O = gt.Bezier, connectionLineStyle: X, connectionLineComponent: q, connectionLineContainerStyle: ee, deleteKeyCode: le = "Backspace", selectionKeyCode: U = "Shift", selectionOnDrag: z = !1, selectionMode: Y = Ln.Full, panActivationKeyCode: ae = "Space", multiSelectionKeyCode: ce = On() ? "Meta" : "Control", zoomActivationKeyCode: J = On() ? "Meta" : "Control", snapToGrid: re, snapGrid: fe, onlyRenderVisibleElements: V = !1, selectNodesOnDrag: Q, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Re, nodesFocusable: _e, nodeOrigin: Te = Iu, edgesFocusable: ot, edgesReconnectable: Xe, elementsSelectable: Ye = !0, defaultViewport: $e = gw, minZoom: Oe = 0.5, maxZoom: He = 2, translateExtent: Ne = zn, preventScrolling: Rt = !0, nodeExtent: Ae, defaultMarkerColor: dn = "#b1b1b7", zoomOnScroll: L = !0, zoomOnPinch: W = !0, panOnScroll: K = !1, panOnScrollSpeed: te = 0.5, panOnScrollMode: se = Ct.Free, zoomOnDoubleClick: he = !0, panOnDrag: ye = !0, onPaneClick: Ie, onPaneMouseEnter: Ce, onPaneMouseMove: ze, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: gr, paneClickDistance: ei = 1, nodeClickDistance: yr = 0, children: mr, onReconnect: ft, onReconnectStart: xr, onReconnectEnd: Lt, onEdgeContextMenu: wr, onEdgeDoubleClick: Vt, onEdgeMouseEnter: vr, onEdgeMouseMove: ti, onEdgeMouseLeave: ni, reconnectRadius: br = 10, onNodesChange: Ot, onEdgesChange: Nr, noDragClassName: jr = "nodrag", noWheelClassName: Sr = "nowheel", noPanClassName: Ht = "nopan", fitView: ii, fitViewOptions: ri, connectOnClick: Cr, attributionPosition: Er, proOptions: kr, defaultEdgeOptions: Ir, elevateNodesOnSelect: Ar = !0, elevateEdgesOnSelect: _r = !1, disableKeyboardA11y: oi = !1, autoPanOnConnect: Dr, autoPanOnNodeDrag: Tr, autoPanOnSelection: $r = !0, autoPanSpeed: Pr, connectionRadius: Mr, isValidConnection: si, onError: ai, style: ci, id: fn, nodeDragThreshold: Rr, connectionDragThreshold: zr, viewport: Lr, onViewportChange: Vr, width: Or, height: Hr, colorMode: Wr = "light", debug: Fr, onScroll: li, ariaLabelConfig: Br, zIndexMode: ui = "basic", ...Kr }, Xr) {
  const pn = fn || "1", Yr = ww(Wr), qr = oe((vt) => {
    vt.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), li?.(vt);
  }, [li]);
  return o.jsx("div", { "data-testid": "rf__wrapper", ...Kr, onScroll: qr, style: { ...ci, ...Wv }, ref: Xr, className: ke(["react-flow", r, Yr]), id: fn, role: "application", children: o.jsxs(Hv, { nodes: e, edges: t, width: Or, height: Hr, fitView: ii, fitViewOptions: ri, minZoom: Oe, maxZoom: He, nodeOrigin: Te, nodeExtent: Ae, zIndexMode: ui, children: [o.jsx(xw, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: y, onConnectEnd: w, onClickConnectStart: x, onClickConnectEnd: m, nodesDraggable: ge, autoPanOnNodeFocus: me, nodesConnectable: Re, nodesFocusable: _e, edgesFocusable: ot, edgesReconnectable: Xe, elementsSelectable: Ye, elevateNodesOnSelect: Ar, elevateEdgesOnSelect: _r, minZoom: Oe, maxZoom: He, nodeExtent: Ae, onNodesChange: Ot, onEdgesChange: Nr, snapToGrid: re, snapGrid: fe, connectionMode: H, translateExtent: Ne, connectOnClick: Cr, defaultEdgeOptions: Ir, fitView: ii, fitViewOptions: ri, onNodesDelete: M, onEdgesDelete: A, onDelete: I, onNodeDragStart: S, onNodeDrag: k, onNodeDragStop: D, onSelectionDrag: _, onSelectionDragStart: C, onSelectionDragStop: E, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: Ht, nodeOrigin: Te, rfId: pn, autoPanOnConnect: Dr, autoPanOnNodeDrag: Tr, autoPanSpeed: Pr, onError: ai, connectionRadius: Mr, isValidConnection: si, selectNodesOnDrag: Q, nodeDragThreshold: Rr, connectionDragThreshold: zr, onBeforeDelete: F, debug: Fr, ariaLabelConfig: Br, zIndexMode: ui }), o.jsx(zv, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: b, onNodeMouseMove: g, onNodeMouseLeave: v, onNodeContextMenu: j, onNodeDoubleClick: N, nodeTypes: s, edgeTypes: a, connectionLineType: O, connectionLineStyle: X, connectionLineComponent: q, connectionLineContainerStyle: ee, selectionKeyCode: U, selectionOnDrag: z, selectionMode: Y, deleteKeyCode: le, multiSelectionKeyCode: ce, panActivationKeyCode: ae, zoomActivationKeyCode: J, onlyRenderVisibleElements: V, defaultViewport: $e, translateExtent: Ne, minZoom: Oe, maxZoom: He, preventScrolling: Rt, zoomOnScroll: L, zoomOnPinch: W, zoomOnDoubleClick: he, panOnScroll: K, panOnScrollSpeed: te, panOnScrollMode: se, panOnDrag: ye, autoPanOnSelection: $r, onPaneClick: Ie, onPaneMouseEnter: Ce, onPaneMouseMove: ze, onPaneMouseLeave: xe, onPaneScroll: zt, onPaneContextMenu: gr, paneClickDistance: ei, nodeClickDistance: yr, onSelectionContextMenu: T, onSelectionStart: P, onSelectionEnd: $, onReconnect: ft, onReconnectStart: xr, onReconnectEnd: Lt, onEdgeContextMenu: wr, onEdgeDoubleClick: Vt, onEdgeMouseEnter: vr, onEdgeMouseMove: ti, onEdgeMouseLeave: ni, reconnectRadius: br, defaultMarkerColor: dn, noDragClassName: jr, noWheelClassName: Sr, noPanClassName: Ht, rfId: pn, disableKeyboardA11y: oi, nodeExtent: Ae, viewport: Lr, onViewportChange: Vr }), o.jsx(hw, { onSelectionChange: R }), mr, o.jsx(lw, { proOptions: kr, position: Er }), o.jsx(cw, { rfId: pn, disableKeyboardA11y: oi })] }) });
}
var rd = Pu(Fv);
const Bv = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function Kv({ children: e }) {
  const t = pe(Bv);
  return t ? tw.createPortal(e, t) : null;
}
function Xv({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return o.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: ke(["react-flow__background-pattern", n, i]) });
}
function Yv({ radius: e, className: t }) {
  return o.jsx("circle", { cx: e, cy: e, r: e, className: ke(["react-flow__background-pattern", "dots", t]) });
}
var mt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(mt || (mt = {}));
const qv = {
  [mt.Dots]: 1,
  [mt.Lines]: 1,
  [mt.Cross]: 6
}, Uv = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function od({
  id: e,
  variant: t = mt.Dots,
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
  const f = ne(null), { transform: p, patternId: h } = pe(Uv, we), y = i || qv[t], w = t === mt.Dots, x = t === mt.Cross, m = Array.isArray(n) ? n : [n, n], b = [m[0] * p[2] || 1, m[1] * p[2] || 1], g = y * p[2], v = Array.isArray(s) ? s : [s, s], j = x ? [g, g] : b, N = [
    v[0] * p[2] || 1 + j[0] / 2,
    v[1] * p[2] || 1 + j[1] / 2
  ], S = `${h}${e || ""}`;
  return o.jsxs("svg", { className: ke(["react-flow__background", l]), style: {
    ...u,
    ...fr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [o.jsx("pattern", { id: S, x: p[0] % b[0], y: p[1] % b[1], width: b[0], height: b[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${N[0]},-${N[1]})`, children: w ? o.jsx(Yv, { radius: g / 2, className: d }) : o.jsx(Xv, { dimensions: j, lineWidth: r, variant: t, className: d }) }), o.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${S})` })] });
}
od.displayName = "Background";
const sd = Se(od);
function Zv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: o.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function Gv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: o.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function Jv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: o.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function Qv() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function e0() {
  return o.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: o.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function wi({ children: e, className: t, ...n }) {
  return o.jsx("button", { type: "button", className: ke(["react-flow__controls-button", t]), ...n, children: e });
}
const t0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ad({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: r, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const y = ve(), { isInteractive: w, minZoomReached: x, maxZoomReached: m, ariaLabelConfig: b } = pe(t0, we), { zoomIn: g, zoomOut: v, fitView: j } = ds(), N = () => {
    g(), s?.();
  }, S = () => {
    v(), a?.();
  }, k = () => {
    j(r), c?.();
  }, D = () => {
    y.setState({
      nodesDraggable: !w,
      nodesConnectable: !w,
      elementsSelectable: !w
    }), u?.(!w);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return o.jsxs(dr, { className: ke(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? b["controls.ariaLabel"], children: [t && o.jsxs(o.Fragment, { children: [o.jsx(wi, { onClick: N, className: "react-flow__controls-zoomin", title: b["controls.zoomIn.ariaLabel"], "aria-label": b["controls.zoomIn.ariaLabel"], disabled: m, children: o.jsx(Zv, {}) }), o.jsx(wi, { onClick: S, className: "react-flow__controls-zoomout", title: b["controls.zoomOut.ariaLabel"], "aria-label": b["controls.zoomOut.ariaLabel"], disabled: x, children: o.jsx(Gv, {}) })] }), n && o.jsx(wi, { className: "react-flow__controls-fitview", onClick: k, title: b["controls.fitView.ariaLabel"], "aria-label": b["controls.fitView.ariaLabel"], children: o.jsx(Jv, {}) }), i && o.jsx(wi, { className: "react-flow__controls-interactive", onClick: D, title: b["controls.interactive.ariaLabel"], "aria-label": b["controls.interactive.ariaLabel"], children: w ? o.jsx(e0, {}) : o.jsx(Qv, {}) }), d] });
}
ad.displayName = "Controls";
const cd = Se(ad);
function n0({ id: e, x: t, y: n, width: i, height: r, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: y, backgroundColor: w } = s || {}, x = a || y || w;
  return o.jsx("rect", { className: ke(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: r, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (m) => h(m, e) : void 0 });
}
const i0 = Se(n0), r0 = (e) => e.nodes.map((t) => t.id), po = (e) => e instanceof Function ? e : () => e;
function o0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: r,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = i0,
  onClick: a
}) {
  const c = pe(r0, we), u = po(t), l = po(e), d = po(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return o.jsx(o.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    o.jsx(a0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: r, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function s0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: r, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = pe((y) => {
    const w = y.nodeLookup.get(e);
    if (!w)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = w.internals.userNode, { x: m, y: b } = w.internals.positionAbsolute, { width: g, height: v } = dt(x);
    return {
      node: x,
      x: m,
      y: b,
      width: g,
      height: v
    };
  }, we);
  return !l || l.hidden || !iu(l) ? null : o.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: r, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const a0 = Se(s0);
var c0 = Se(o0);
const l0 = 200, u0 = 150, d0 = (e) => !e.hidden, f0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? tu(Zn(e.nodeLookup, { filter: d0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, p0 = "react-flow__minimap-desc";
function ld({
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
  zoomable: x = !1,
  ariaLabel: m,
  inversePan: b,
  zoomStep: g = 1,
  offsetScale: v = 5
}) {
  const j = ve(), N = ne(null), { boundingRect: S, viewBB: k, rfId: D, panZoom: M, translateExtent: A, flowWidth: I, flowHeight: R, ariaLabelConfig: C } = pe(f0, we), _ = e?.width ?? l0, E = e?.height ?? u0, T = S.width / _, P = S.height / E, $ = Math.max(T, P), F = $ * _, H = $ * E, O = v * $, X = S.x - (F - S.width) / 2 - O, q = S.y - (H - S.height) / 2 - O, ee = F + O * 2, le = H + O * 2, U = `${p0}-${D}`, z = ne(0), Y = ne();
  z.current = $, G(() => {
    if (N.current && M)
      return Y.current = kx({
        domNode: N.current,
        panZoom: M,
        getTransform: () => j.getState().transform,
        getViewScale: () => z.current
      }), () => {
        Y.current?.destroy();
      };
  }, [M]), G(() => {
    Y.current?.update({
      translateExtent: A,
      width: I,
      height: R,
      inversePan: b,
      pannable: w,
      zoomStep: g,
      zoomable: x
    });
  }, [w, x, b, g, A, I, R]);
  const ae = h ? (re) => {
    const [fe, V] = Y.current?.pointer(re) || [0, 0];
    h(re, { x: fe, y: V });
  } : void 0, ce = y ? oe((re, fe) => {
    const V = j.getState().nodeLookup.get(fe).internals.userNode;
    y(re, V);
  }, []) : void 0, J = m ?? C["minimap.ariaLabel"];
  return o.jsx(dr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: ke(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: o.jsxs("svg", { width: _, height: E, viewBox: `${X} ${q} ${ee} ${le}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": U, ref: N, onClick: ae, children: [J && o.jsx("title", { id: U, children: J }), o.jsx(c0, { onClick: ce, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: r, nodeStrokeWidth: a, nodeComponent: c }), o.jsx("path", { className: "react-flow__minimap-mask", d: `M${X - O},${q - O}h${ee + O * 2}v${le + O * 2}h${-ee - O * 2}z
        M${k.x},${k.y}h${k.width}v${k.height}h${-k.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
ld.displayName = "MiniMap";
const ud = Se(ld), h0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, g0 = {
  [on.Line]: "right",
  [on.Handle]: "bottom-right"
};
function y0({ nodeId: e, position: t, variant: n = on.Handle, className: i, style: r = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: y, onResizeStart: w, onResize: x, onResizeEnd: m }) {
  const b = Lu(), g = typeof e == "string" ? e : b, v = ve(), j = ne(null), N = n === on.Handle, S = pe(oe(h0(N && h), [N, h]), we), k = ne(null), D = t ?? g0[n];
  G(() => {
    if (!(!j.current || !g))
      return k.current || (k.current = Ox({
        domNode: j.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: A, transform: I, snapGrid: R, snapToGrid: C, nodeOrigin: _, domNode: E } = v.getState();
          return {
            nodeLookup: A,
            transform: I,
            snapGrid: R,
            snapToGrid: C,
            nodeOrigin: _,
            paneDomNode: E
          };
        },
        onChange: (A, I) => {
          const { triggerNodeChanges: R, nodeLookup: C, parentLookup: _, nodeOrigin: E } = v.getState(), T = [], P = { x: A.x, y: A.y }, $ = C.get(g);
          if ($ && $.expandParent && $.parentId) {
            const F = $.origin ?? E, H = A.width ?? $.measured.width ?? 0, O = A.height ?? $.measured.height ?? 0, X = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: H,
                height: O,
                ...ru({
                  x: A.x ?? $.position.x,
                  y: A.y ?? $.position.y
                }, { width: H, height: O }, $.parentId, C, F)
              }
            }, q = us([X], C, _, E);
            T.push(...q), P.x = A.x ? Math.max(F[0] * H, A.x) : void 0, P.y = A.y ? Math.max(F[1] * O, A.y) : void 0;
          }
          if (P.x !== void 0 && P.y !== void 0) {
            const F = {
              id: g,
              type: "position",
              position: { ...P }
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
          for (const F of I) {
            const H = {
              ...F,
              type: "position"
            };
            T.push(H);
          }
          R(T);
        },
        onEnd: ({ width: A, height: I }) => {
          const R = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: A,
              height: I
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
        onResizeStart: w,
        onResize: x,
        onResizeEnd: m,
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
    w,
    x,
    m,
    y
  ]);
  const M = D.split("-");
  return o.jsx("div", { className: ke(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: j, style: {
    ...r,
    scale: S,
    ...a && { [N ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
Se(y0);
function m0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: Bn(e.state),
    layout: e.layout
  };
}
function x0(e) {
  return JSON.stringify(
    {
      state: Bn(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function w0(e, t) {
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
      state: Qi(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function v0(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), r = URL.createObjectURL(i), s = document.createElement("a");
  s.href = r, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(r);
}
function rc({
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
function b0({
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
    () => d ? Yd(d) : null,
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
        f ? /* @__PURE__ */ o.jsx(qd, { fallback: /* @__PURE__ */ o.jsx(
          rc,
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
          rc,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ o.jsx(N0, { diagnostics: u })
      ]
    }
  );
}
function N0({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", r = j0(t);
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
function j0(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const S0 = { language: "json", displayName: "JSON" };
function C0({ draft: e, onApply: t }) {
  const n = de(() => x0(e), [e]), [i, r] = B(n), [s, a] = B(n), [c, u] = B(null);
  G(() => {
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
      b0,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: S0,
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
function Pe(e) {
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
function Tt(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function hs(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ o.jsx(zc, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ o.jsx(Wo, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ o.jsx(Jd, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ o.jsx(Zt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ o.jsx(Gd, { size: 15 });
    default:
      return /* @__PURE__ */ o.jsx(Fn, { size: 15 });
  }
}
const E0 = ["Single", "Array", "List", "HashSet"];
function dd(e) {
  const [t, n] = B(null), [i, r] = B(null);
  G(() => {
    let u = !1;
    return qp(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Zp(e).then(
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
      const l = Ps(u);
      return {
        value: l,
        label: Xc(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = de(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: If(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = de(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Ps(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function k0(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function I0(e, t, n) {
  return {
    add: () => {
      const i = xf(n.namePrefix, e.map((r) => Cn(r, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, r) => t(e.map((s, a) => a === i ? n.patch(s, r) : s)),
    remove: (i) => t(e.filter((r, s) => s !== i))
  };
}
function oc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: r, onChange: s }) {
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
const A0 = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function _0({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: E0.map((i) => /* @__PURE__ */ o.jsx("option", { value: i, children: A0[i] }, i)) });
}
function D0(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function T0({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const r = D0(t, e);
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
function $0({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: r, onAdd: s, children: a }) {
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
function P0({ label: e, onRemove: t }) {
  return /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ o.jsx(An, { size: 14 }) }) });
}
function M0({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ o.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function gs({
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
  const { add: y, update: w, remove: x } = I0(e, h, {
    namePrefix: r,
    nameKeys: s,
    create: (g) => l(g, k0(t)),
    patch: d
  }), m = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], b = r.toLowerCase();
  return /* @__PURE__ */ o.jsx(
    $0,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: m,
      isEmpty: e.length === 0,
      onAdd: y,
      children: e.map((g, v) => {
        const j = Cn(g, s), N = Kc(g), S = Cn(g, qc), k = S ? p?.get(S) : void 0, D = N.collectionKind === "Single" ? i(N.alias) : "text";
        return /* @__PURE__ */ o.jsxs("tr", { children: [
          /* @__PURE__ */ o.jsxs("td", { children: [
            /* @__PURE__ */ o.jsx("input", { type: "text", "aria-label": `${r} name`, value: j, onChange: (M) => w(v, { name: M.target.value }) }),
            k ? /* @__PURE__ */ o.jsx("span", { className: "wf-properties-warning", role: "note", title: k, children: k }) : null
          ] }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            oc,
            {
              ariaLabel: `${r} type`,
              value: N.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => w(v, { type: { alias: M, collectionKind: N.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            _0,
            {
              ariaLabel: `${r} collection kind`,
              value: N.collectionKind,
              onChange: (M) => w(v, { type: { alias: N.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            T0,
            {
              ariaLabel: `${r} default value`,
              value: Nf(g.default),
              editor: D,
              onChange: (M) => w(v, { default: bf(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            oc,
            {
              ariaLabel: `${r} storage driver`,
              value: Cn(g, _f),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => w(v, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ o.jsx("td", { children: /* @__PURE__ */ o.jsx(
            M0,
            {
              ariaLabel: `${r} required`,
              checked: g.isRequired === !0,
              onChange: (M) => w(v, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ o.jsx(P0, { label: `Remove ${b} ${j || v + 1}`, onRemove: () => x(v) })
        ] }, v);
      })
    }
  );
}
function fd({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: r = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ o.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Af,
      title: r,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => wf({ name: l, alias: d }),
      patch: (l, d) => vf(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function R0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: Yc,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => jf({ name: s, alias: a }),
      patch: (s, a) => Sf(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: r
    }
  );
}
function z0({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: r }) {
  return /* @__PURE__ */ o.jsx(
    gs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: Yc,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Cf({ name: s, alias: a }),
      patch: (s, a) => Ef(s, a),
      columns: { default: !1, storage: !1 },
      onChange: r
    }
  );
}
function _i(e) {
  return (e ?? []).filter(Pi);
}
function L0({ context: e, variables: t, title: n, addLabel: i, emptyLabel: r, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = dd(e);
  return /* @__PURE__ */ o.jsx(
    fd,
    {
      items: _i(t),
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
function V0({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [r, s] = B(e?.name ?? ""), [a, c] = B(e?.description ?? "");
  G(() => {
    s(e?.name ?? "");
  }, [e?.name]), G(() => {
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
function O0({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: r }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = dd(n), u = _i(t.state.variables), l = _i(t.state.inputs), d = _i(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ o.jsx(
      V0,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: r
      }
    ),
    /* @__PURE__ */ o.jsx(
      fd,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      R0,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ o.jsx(
      z0,
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
        /* @__PURE__ */ o.jsx("time", { children: Pe(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const sc = "application/x-elsa-activity-version-id", H0 = 6, W0 = 1200, F0 = 250, B0 = [10, 25, 50], K0 = 10, ac = "elsa-studio-workflow-palette-width", cc = "elsa-studio-workflow-inspector-width", lc = "elsa-studio-workflow-palette-collapsed", uc = "elsa-studio-workflow-inspector-collapsed", pd = "elsa-studio-workflow-side-panel-maximized", bn = 180, Nn = 460, X0 = 260, Xt = 260, Yt = 560, Y0 = 320, dc = 42, vi = 16, hd = tt.createContext(null), gd = tt.createContext(null);
function q0(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function yd(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function $t(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Pt(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function U0(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = fc(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const r = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return fc(r, s);
}
function fc(e, t) {
  const n = typeof e == "string" ? pc(e) : void 0, i = typeof t == "string" ? pc(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function pc(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Z0(e, t) {
  return e.rootActivityVersionId ?? md(t, e.rootKind)?.activityVersionId ?? null;
}
function md(e, t) {
  return e.find((n) => G0(n) === t);
}
function G0(e) {
  return e ? J0(e) ? "flowchart" : Q0(e) ? "sequence" : null : null;
}
function $o(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "Uncategorized";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return Array.from(t.entries()).sort(([n], [i]) => n.localeCompare(i)).map(([n, i]) => ({
    category: n,
    activities: i.sort((r, s) => Ee(r).localeCompare(Ee(s)))
  }));
}
function J0(e) {
  return Ee(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Q0(e) {
  return Ee(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function eb(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function xd(e) {
  return ob(e.rootActivityType) || e.rootActivityType;
}
function tb(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function nb(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function ib(e, t) {
  return hc(t) - hc(e);
}
function hc(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function wd(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function vd(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
async function rb(e) {
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
function ob(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function sb(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    bi(t, n.typeName, n), bi(t, n.name, n), bi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    bi(t, i, n);
  }
  return t;
}
function ab(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(jn(i?.activityTypeKey)) ?? n.get(jn(Tt(i?.activityTypeKey))) ?? n.get(jn(i?.displayName)) ?? n.get(jn(e.activityVersionId)) ?? null;
}
function bi(e, t, n) {
  const i = jn(t);
  i && !e.has(i) && e.set(i, n);
}
function jn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function gc(e, t, n, i) {
  const r = pr();
  if (!r) return t;
  const s = r.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Di(a, n, i) : t;
}
function yc(e, t) {
  const n = pr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function cb() {
  const e = pr();
  if (!e) return null;
  const t = e.getItem(pd);
  return t === "palette" || t === "inspector" ? t : null;
}
function pr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function mn(e, t) {
  const n = pr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Di(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function bd(e) {
  switch (lb(e)) {
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
function lb(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function ub(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function mc(e) {
  return `${Ee(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function xc(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function db(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function Nd(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function fb(e) {
  const t = Nd(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function pb(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Fe(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function hb(e) {
  return Cd(Fe(e));
}
function gb(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ee(n) : Tt(e.activityVersionId) ?? e.activityVersionId;
}
function yb(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? gb(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function jd(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ee(i) : void 0
  });
  for (const r of Me(e, t))
    for (const s of r.activities) jd(s, t, n);
  return n;
}
function Sd(e, t, n = []) {
  if (!e) return n;
  for (const i of cl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Me(e, t))
    for (const r of i.activities) Sd(r, t, n);
  return n;
}
function Sn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function mb(e) {
  return `${e.id}-${Cd(JSON.stringify(e.state))}`;
}
function Cd(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ys(e) {
  return e.status.toLowerCase() === "rejected";
}
function xb(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function wb(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return vb(e, n) ? `Run ${t} was not found.` : n;
}
function vb(e, t) {
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
const Ed = { workflowActivity: bb }, kd = { workflow: jb };
function bb({ data: e, selected: t }) {
  const n = e, i = n.runtime, r = !n.suppressFlowPorts, s = r ? n.sourcePorts.length > 0 ? n.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], a = Nb(n), u = tt.useContext(gd)?.({ activityVersionId: n.activityVersionId, activityTypeKey: n.activityTypeKey }) ?? null;
  return /* @__PURE__ */ o.jsxs(
    "div",
    {
      className: ["wf-node", t ? "selected" : "", i ? "wf-node-runtime" : "", i?.hasBlockingIncident ? "faulted" : "", u ? "wf-node-unavailable" : ""].filter(Boolean).join(" "),
      "data-icon": n.icon ?? "activity",
      children: [
        r && n.acceptsInbound ? /* @__PURE__ */ o.jsx(sn, { type: "target", position: ie.Left }) : null,
        u ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Li(u.state)}`, children: /* @__PURE__ */ o.jsx($i, { size: 13 }) }) : null,
        /* @__PURE__ */ o.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: hs(n.icon) }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ o.jsx("strong", { children: n.label }),
            a ? /* @__PURE__ */ o.jsx("small", { children: a }) : null
          ] })
        ] }),
        n.childSlots.length > 0 ? n.onEnterSlot ? /* @__PURE__ */ o.jsx("span", { className: "wf-node-slot-list", children: n.childSlots.map((l) => /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-node-slot-badge",
            onClick: (d) => {
              d.stopPropagation(), n.onEnterSlot?.(l);
            },
            children: l.label
          },
          l.id
        )) }) : /* @__PURE__ */ o.jsxs("span", { className: "wf-node-slot-badge", children: [
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
          return /* @__PURE__ */ o.jsxs(tt.Fragment, { children: [
            /* @__PURE__ */ o.jsx("span", { className: "wf-node-port-label", style: { top: f }, children: l.displayName }),
            /* @__PURE__ */ o.jsx(sn, { type: "source", position: ie.Right, id: l.name, style: { top: f } })
          ] }, l.name);
        })
      ]
    }
  );
}
function Nb(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((r) => !!r).join(" · ");
}
function jb(e) {
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
  } = e, p = tt.useContext(hd), [h, y] = B(!1), [w, x, m] = Ui({ sourceX: n, sourceY: i, targetX: r, targetY: s, sourcePosition: a, targetPosition: c }), b = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx(
      Jn,
      {
        id: t,
        path: w,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: b ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: m,
        labelStyle: f,
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1)
      }
    ),
    p ? /* @__PURE__ */ o.jsx(Kv, { children: /* @__PURE__ */ o.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", b ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${m}px)` },
        onMouseEnter: () => y(!0),
        onMouseLeave: () => y(!1),
        children: [
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ o.jsx(Gt, { size: 12 }) }),
          /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ o.jsx(An, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function Sb({ clientX: e, clientY: t, activities: n, onPick: i, onClose: r }) {
  const [s, a] = B(""), [c, u] = B(0), l = ne(null), d = ne(null), f = de(() => {
    const b = s.trim().toLowerCase(), g = n.filter(eb);
    return b ? g.filter((v) => Ee(v).toLowerCase().includes(b) || v.activityTypeKey.toLowerCase().includes(b) || (v.category ?? "").toLowerCase().includes(b) || (v.description ?? "").toLowerCase().includes(b)) : g;
  }, [n, s]), p = de(() => $o(f), [f]), h = de(() => p.flatMap((b) => b.activities), [p]);
  G(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), G(() => {
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
  }, w = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let m = -1;
  return /* @__PURE__ */ o.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: w, top: x }, onMouseDown: (b) => b.stopPropagation(), onClick: (b) => b.stopPropagation(), children: [
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
        m += 1;
        const v = m, j = v === c;
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
              /* @__PURE__ */ o.jsx("strong", { children: Ee(g) }),
              /* @__PURE__ */ o.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, b.category)) })
  ] });
}
function Ti({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const r = uf(t.map((s) => s.id), n, i);
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
function wc(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
const Cb = "Expressions/UnresolvedVariable";
function Eb(e) {
  return String(e.type ?? e.code ?? "");
}
function kb(e) {
  return Eb(e) === Cb;
}
function Ib(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...r] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: r.length > 0 ? r.join("/") : null
  };
}
function Ab(e) {
  return (e ?? []).filter(kb).map((t) => ({
    error: t,
    path: Ib(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function _b({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
      " No validation errors"
    ] });
  const i = Ab(n), r = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ o.jsx(wt, { size: 14 }),
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
          /* @__PURE__ */ o.jsx(Qd, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function Db({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ys(e);
  return /* @__PURE__ */ o.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ o.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ o.jsx(wt, { size: 16 }) : /* @__PURE__ */ o.jsx(an, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function Tb({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = ys(e), i = e.workflowExecutionId;
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
      /* @__PURE__ */ o.jsx(wt, { size: 14 }),
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
        /* @__PURE__ */ o.jsx("dd", { children: vc(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: vc(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ o.jsx("dd", { title: e.expiresAt ? Pe(e.expiresAt) : "None", children: e.expiresAt ? Pe(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function vc(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ms({ rows: e = 5 }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ o.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function xs({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ o.jsx(Fn, { size: 22 }) }),
    /* @__PURE__ */ o.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ o.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function Qn({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ o.jsx(wt, { size: 18 }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ o.jsx("strong", { children: t }),
      /* @__PURE__ */ o.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function Id({ status: e, run: t, compact: n = !1 }) {
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
function Et({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: r }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await rb(e), i(n);
    } catch {
      r(n);
    }
  };
  return /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ o.jsx(ef, { size: 12 }) });
}
function $b({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = n?.trim().toLowerCase() ?? "", w = de(
    () => y ? p.filter((S) => tb(S, y)) : p,
    [y, p]
  ), x = de(
    () => Array.from(new Set(p.flatMap((S) => [
      S.definitionId,
      S.definitionVersionId,
      S.sourceId
    ]).filter((S) => !!S))).sort((S, k) => S.localeCompare(k)),
    [p]
  ), m = $t(t, "weaver.workflows.explain-executable"), b = oe(async () => {
    s("loading"), c("");
    try {
      h(await hl(e)), s("ready");
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
      const k = await pl(e, S.artifactId), D = vd(k);
      f({ artifactId: S.artifactId, workflowExecutionId: D }), l(`Started ${S.artifactId}`);
    } catch (k) {
      c(k instanceof Error ? k.message : String(k));
    }
  }, v = (S) => {
    m && Pt(t, m, S) && (c(""), f(null), l(`Sent ${S.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(Gi, { size: 14 }),
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
        /* @__PURE__ */ o.jsx(Lc, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsx(Qn, { message: a }) : null,
    u ? /* @__PURE__ */ o.jsx(Id, { status: u, run: d }) : null,
    r === "loading" ? /* @__PURE__ */ o.jsx(ms, {}) : null,
    r === "ready" && w.length === 0 ? /* @__PURE__ */ o.jsx(
      xs,
      {
        icon: /* @__PURE__ */ o.jsx(Zt, { size: 22 }),
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
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactId, ariaLabel: `Copy artifact ID ${S.artifactId}`, copiedLabel: "artifact ID", onCopied: j, onCopyFailed: N })
          ] }),
          /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
            /* @__PURE__ */ o.jsx("small", { title: S.artifactHash, children: S.artifactHash }),
            /* @__PURE__ */ o.jsx(Et, { value: S.artifactHash, ariaLabel: `Copy artifact hash ${S.artifactHash}`, copiedLabel: "artifact hash", onCopied: j, onCopyFailed: N })
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-version-cell", children: [
          /* @__PURE__ */ o.jsx("span", { children: S.artifactVersion }),
          /* @__PURE__ */ o.jsx(Et, { value: S.artifactVersion, ariaLabel: `Copy artifact version ${S.artifactVersion}`, copiedLabel: "artifact version", onCopied: j, onCopyFailed: N })
        ] }),
        /* @__PURE__ */ o.jsx(Pb, { executable: S, onCopied: j, onCopyFailed: N }),
        /* @__PURE__ */ o.jsx("span", { children: xd(S) }),
        /* @__PURE__ */ o.jsx("span", { children: Pe(S.publishedAt ?? S.createdAt) }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-row-actions", children: [
          /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
            g(S);
          }, children: [
            /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
            " Run"
          ] }),
          m ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => v(S), children: [
            /* @__PURE__ */ o.jsx(lt, { size: 13 }),
            " Explain"
          ] }) : null
        ] })
      ] }, S.artifactId))
    ] }) : null
  ] });
}
function Pb({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, r = e.sourceVersion;
  return /* @__PURE__ */ o.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-source-kind", children: wd(e.sourceKind) }),
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
function Mb({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [r, s] = B("loading"), [a, c] = B(""), [u, l] = B(""), [d, f] = B(null), [p, h] = B([]), y = $t(t, "weaver.workflows.explain-executable"), w = oe(async () => {
    s("loading"), c("");
    try {
      const j = await hl(e);
      h(j.filter((N) => nb(N, n)).sort(ib)), s("ready");
    } catch (j) {
      c(j instanceof Error ? j.message : String(j)), h([]), s("failed");
    }
  }, [e, n]);
  G(() => {
    w();
  }, [w, i]);
  const x = async (j) => {
    l(""), f(null), c("");
    try {
      const N = await pl(e, j.artifactId);
      f({ artifactId: j.artifactId, workflowExecutionId: vd(N) }), l(`Started ${j.artifactId}`);
    } catch (N) {
      c(N instanceof Error ? N.message : String(N));
    }
  }, m = (j) => {
    y && Pt(t, y, j) && (c(""), f(null), l(`Sent ${j.artifactId} to Weaver`));
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
        /* @__PURE__ */ o.jsx(Fo, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: b, children: "Open list" })
    ] }),
    r === "failed" ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ o.jsx(wt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ o.jsx(Id, { status: u, run: d, compact: !0 }) : null,
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
        /* @__PURE__ */ o.jsx("span", { children: Pe(j.publishedAt ?? j.createdAt) })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactId, children: j.artifactId }),
          /* @__PURE__ */ o.jsx(Et, { value: j.artifactId, ariaLabel: `Copy artifact ID ${j.artifactId}`, copiedLabel: "artifact ID", onCopied: g, onCopyFailed: v })
        ] }),
        /* @__PURE__ */ o.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ o.jsx("code", { title: j.artifactHash, children: j.artifactHash }),
          /* @__PURE__ */ o.jsx(Et, { value: j.artifactHash, ariaLabel: `Copy artifact hash ${j.artifactHash}`, copiedLabel: "artifact hash", onCopied: g, onCopyFailed: v })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("dl", { children: [
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ o.jsxs("dd", { children: [
            wd(j.sourceKind),
            " ",
            j.sourceVersion ? `v${j.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ o.jsxs("div", { children: [
          /* @__PURE__ */ o.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ o.jsx("dd", { children: xd(j) })
        ] })
      ] }),
      /* @__PURE__ */ o.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
          x(j);
        }, children: [
          /* @__PURE__ */ o.jsx(Zt, { size: 13 }),
          " Run"
        ] }),
        y ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => m(j), children: [
          /* @__PURE__ */ o.jsx(lt, { size: 13 }),
          " Explain"
        ] }) : null
      ] })
    ] }, j.artifactId)) }) : null
  ] });
}
function Ad() {
  const [e, t] = B(() => gc(ac, X0, bn, Nn)), [n, i] = B(() => gc(cc, Y0, Xt, Yt)), [r, s] = B(() => yc(lc, !1)), [a, c] = B(() => yc(uc, !1)), [u, l] = B(cb);
  G(() => {
    mn(ac, String(e));
  }, [e]), G(() => {
    mn(cc, String(n));
  }, [n]), G(() => {
    mn(lc, String(r));
  }, [r]), G(() => {
    mn(uc, String(a));
  }, [a]), G(() => {
    mn(pd, u);
  }, [u]), G(() => {
    if (!u) return;
    const g = (v) => {
      v.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = oe((g) => {
    l((v) => v === g ? null : v), g === "palette" ? s((v) => !v) : c((v) => !v);
  }, []), f = oe((g) => {
    g === "palette" ? s(!1) : c(!1), l((v) => v === g ? null : g);
  }, []), p = oe((g, v) => {
    l(null), g === "palette" ? (s(!1), t((j) => Di(j + v, bn, Nn))) : (c(!1), i((j) => Di(j + v, Xt, Yt)));
  }, []), h = oe((g, v) => {
    v.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const j = v.clientX, N = g === "palette" ? e : n, S = g === "palette" ? bn : Xt, k = g === "palette" ? Nn : Yt;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (A) => {
      const I = g === "palette" ? A.clientX - j : j - A.clientX, R = Di(N + I, S, k);
      g === "palette" ? t(R) : i(R);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), y = oe((g, v) => {
    v.key === "ArrowLeft" ? (v.preventDefault(), p(g, g === "palette" ? -vi : vi)) : v.key === "ArrowRight" ? (v.preventDefault(), p(g, g === "palette" ? vi : -vi)) : v.key === "Home" ? (v.preventDefault(), g === "palette" ? t(bn) : i(Xt)) : v.key === "End" && (v.preventDefault(), g === "palette" ? t(Nn) : i(Yt));
  }, [p]), w = !r && u !== "inspector", x = !a && u !== "palette", m = [
    "wf-editor-body",
    r ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), b = {
    "--wf-palette-width": `${r ? dc : e}px`,
    "--wf-inspector-width": `${a ? dc : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: r,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: w,
    inspectorExpanded: x,
    editorBodyClassName: m,
    editorBodyStyle: b,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: y
  };
}
const Rb = 50;
function bc() {
  return { past: [], future: [] };
}
function zb(e) {
  return e.past.length > 0;
}
function Lb(e) {
  return e.future.length > 0;
}
function Nc(e, t, n = Rb) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function Vb(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function Ob(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function Hb({ draft: e, restoreDraft: t }) {
  const n = ne(bc()), i = ne(null), r = ne(""), s = ne(!1), [a, c] = B(0), u = oe((w) => {
    n.current = bc(), i.current = w ? Sn(w) : null, r.current = w ? Fe(w) : "", s.current = !1, c(0);
  }, []);
  G(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const w = Fe(e);
    if (w === r.current) return;
    const x = window.setTimeout(() => {
      const m = i.current;
      m && (n.current = Nc(n.current, m), c((b) => b + 1)), i.current = Sn(e), r.current = w;
    }, F0);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = oe(() => {
    if (!e) return;
    const w = Fe(e);
    if (w === r.current) return;
    const x = i.current;
    x && (n.current = Nc(n.current, x)), i.current = Sn(e), r.current = w;
  }, [e]), d = oe((w) => {
    s.current = !0, i.current = Sn(w), r.current = Fe(w), t(w), c((x) => x + 1);
  }, [t]), f = oe(() => {
    if (!e) return;
    l();
    const w = Vb(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), p = oe(() => {
    if (!e) return;
    l();
    const w = Ob(n.current, e);
    w && (n.current = w.history, d(w.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: y } = de(() => {
    const w = !!e && !!i.current && Fe(e) !== r.current;
    return {
      canUndoNow: zb(n.current) || w,
      canRedoNow: Lb(n.current) && !w
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: y };
}
const Wb = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function Fb(e, t) {
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
function Bb() {
  const [e, t] = Ud(Fb, Wb), n = de(() => ({
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
const Kb = 320, Xb = 140;
function Yb(e, t, n) {
  return n === "sequence" ? qb(e) : Ub(e, t);
}
function qb(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function Ub(e, t) {
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
      n.set(p, { x: d * Kb, y: h * Xb });
    });
  return n;
}
function Zb(e, t, n, i, r) {
  if (!e) return { kind: "becomeRoot" };
  const s = Tn(e, t, r);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Me(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const Gb = "root";
function Jb(e) {
  return e.length === 0 ? Gb : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function Qb(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function eN({
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
  const [x, m] = B([]), [b, g] = B([]), [v, j] = B(null), [N, S] = B(null), [k, D] = B(null), M = ne(null), A = ne(null), I = ne(/* @__PURE__ */ new Map()), R = ne(/* @__PURE__ */ new Set()), C = ne(null), _ = ne([]), E = ne(null), T = ne(null), P = ne(!1), $ = de(() => Jb(i), [i]);
  G(() => () => {
    v && I.current.set($, v.getViewport());
  }, [v, $]), G(() => {
    if (C.current = $, !n) {
      _.current = [], m([]), g([]);
      return;
    }
    const L = a ? nl(n, r, e?.layout ?? []) : t ? tl(t, r, e?.layout ?? []) : { nodes: [], edges: [] };
    _.current = L.nodes.map((W) => W.id), m(L.nodes), g(L.edges);
  }, [r, e?.layout, a, t, n, $]), G(() => {
    if (!v || C.current !== $ || !Qb(x, _.current)) return;
    C.current = null;
    const L = I.current.get($), W = R.current.has($);
    R.current.add($), window.requestAnimationFrame(() => {
      L ? v.setViewport(L) : !W && x.length > 0 && v.fitView({ padding: 0.2 });
    });
  }, [x, v, $]);
  const F = oe((L, W, K) => K ? [
    ...L.filter((te) => te.nodeId !== W),
    { nodeId: W, x: Math.round(K.x), y: Math.round(K.y) }
  ] : L, []), H = oe((L, W) => {
    if (e?.state.rootActivity && a)
      return;
    const K = wo(L, mc(L)), te = Zb(e?.state.rootActivity, i, K, L, s);
    if (te.kind === "becomeRoot") {
      f(
        ({ draft: se }) => se ? { ...se, state: { ...se.state, rootActivity: K } } : null,
        K.nodeId
      );
      return;
    }
    if (te.kind === "leafError") {
      y(""), w("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root.");
      return;
    }
    if (te.kind === "staleFrames") {
      y(""), w("This slot could not be resolved — returning to the workflow root."), h();
      return;
    }
    if (te.kind === "wrapRoot") {
      f(({ draft: se }) => {
        const he = se?.state.rootActivity;
        return he ? {
          ...se,
          layout: F(se.layout, he.nodeId, W),
          state: { ...se.state, rootActivity: Ls(K, [], [he], L) }
        } : null;
      }, e?.state.rootActivity?.nodeId ?? null), w(""), y(`Wrapped root in ${Ee(L)}`);
      return;
    }
    f(({ draft: se, frames: he }) => {
      if (!se?.state.rootActivity) return null;
      const ye = Tn(se.state.rootActivity, he, s);
      if (!ye) return null;
      const Ie = ye.slot.cardinality === "single" ? [K] : [...ye.slot.activities, K], Ce = Ls(se.state.rootActivity, he, Ie, s);
      return {
        ...se,
        layout: F(se.layout, K.nodeId, W),
        state: { ...se.state, rootActivity: Ce }
      };
    }, K.nodeId), te.replacedActivity && (w(""), y(`Replaced ${te.slot.label} content`));
  }, [s, e?.state.rootActivity, i, a, f, h, F, w, y]), O = oe((L, W) => {
    const K = wo(L, mc(L)), te = {
      id: K.nodeId,
      type: "workflowActivity",
      position: W,
      selected: !0,
      data: {
        label: Ee(L),
        activityVersionId: L.activityVersionId,
        activityTypeKey: L.activityTypeKey,
        category: L.category,
        executionType: L.executionType,
        icon: er(L),
        childSlots: Me(K, L),
        acceptsInbound: String(L.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: ll(K, L)
      }
    };
    return { activityNode: K, node: te };
  }, []), X = oe((L, W, K = []) => {
    a || d(({ draft: te, frames: se }) => {
      if (!te) return null;
      const he = Qf(te.layout, L), ye = te.state.rootActivity;
      if (!ye) return { ...te, layout: he };
      const Ie = Tn(ye, se, s);
      if (!Ie) return { ...te, layout: he };
      const Ce = Gf(Ie, L, W, K), ze = Ie.slot.mode === "flowchart" ? Jf(Ce, W) : Ce;
      return {
        ...te,
        layout: he,
        state: {
          ...te.state,
          rootActivity: Zf(ye, se, ze, s)
        }
      };
    });
  }, [s, a, d]), q = oe((L, W) => {
    if (!M.current) return null;
    const K = M.current.getBoundingClientRect();
    return v ? v.screenToFlowPosition({ x: L, y: W }) : {
      x: L - K.left,
      y: W - K.top
    };
  }, [v]), ee = oe((L, W) => document.elementFromPoint(L, W)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), le = oe((L, W, K) => {
    const te = x.find((xe) => xe.id === W.source), se = x.find((xe) => xe.id === W.target), he = te && se ? db(te, se) : te ? xc(te) : K, ye = O(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), ye.node], ze = ap(b, W, ye.node.id);
    m(Ce), g(ze), p(ye.node.id), X(Ce, ze, [ye.activityNode]);
  }, [X, O, b, x, p]), U = oe((L, W, K) => {
    if (!u || !M.current) return !1;
    const te = M.current.getBoundingClientRect();
    if (!(W >= te.left && W <= te.right && K >= te.top && K <= te.bottom)) return !1;
    const he = q(W, K);
    if (!he) return !1;
    if (c) {
      const ye = ee(W, K), Ie = ye ? b.find((Ce) => Ce.id === ye) : void 0;
      if (Ie)
        return le(L, Ie, he), !0;
    }
    return H(L, he), !0;
  }, [H, u, b, ee, c, le, q]);
  G(() => {
    const L = (K) => {
      const te = E.current;
      if (!te) return;
      Math.hypot(K.clientX - te.startX, K.clientY - te.startY) >= H0 && (te.dragging = !0);
    }, W = (K) => {
      const te = E.current;
      if (E.current = null, !te?.dragging || !M.current || T.current) return;
      const se = M.current.getBoundingClientRect();
      K.clientX >= se.left && K.clientX <= se.right && K.clientY >= se.top && K.clientY <= se.bottom && (P.current = !0, window.setTimeout(() => {
        P.current = !1;
      }, 0), U(te.activity, K.clientX, K.clientY));
    };
    return window.addEventListener("pointermove", L), window.addEventListener("pointerup", W), window.addEventListener("pointercancel", W), () => {
      window.removeEventListener("pointermove", L), window.removeEventListener("pointerup", W), window.removeEventListener("pointercancel", W);
    };
  }, [v, U]);
  const z = (L, W) => {
    T.current = { activityVersionId: W.activityVersionId, handledDrop: !1 }, L.dataTransfer.setData(sc, W.activityVersionId), L.dataTransfer.setData("text/plain", W.activityVersionId), L.dataTransfer.effectAllowed = "copy";
  }, Y = (L, W) => {
    const K = T.current;
    T.current = null, !K?.handledDrop && (L.clientX === 0 && L.clientY === 0 || U(W, L.clientX, L.clientY) && (P.current = !0, window.setTimeout(() => {
      P.current = !1;
    }, 0)));
  }, ae = (L, W) => {
    L.button === 0 && (E.current = {
      activity: W,
      startX: L.clientX,
      startY: L.clientY,
      dragging: !1
    });
  }, ce = (L) => {
    P.current || u && H(L);
  }, J = (L) => {
    if (!u) {
      L.dataTransfer.dropEffect = "none";
      return;
    }
    if (L.preventDefault(), L.dataTransfer.dropEffect = "copy", !c) return;
    const W = ee(L.clientX, L.clientY);
    D(W);
  }, re = (L) => {
    if (!M.current) return;
    const W = L.relatedTarget;
    W && M.current.contains(W) || D(null);
  }, fe = (L) => {
    L.preventDefault(), D(null);
    const W = L.dataTransfer.getData(sc) || L.dataTransfer.getData("text/plain");
    if (!W || (L.stopPropagation(), T.current?.activityVersionId === W && (T.current.handledDrop = !0), !u)) return;
    const K = s.get(W);
    K && U(K, L.clientX, L.clientY);
  }, V = (L) => {
    if (!u) return;
    if (L) {
      S({ kind: "fromEmpty", clientX: L.clientX, clientY: L.clientY });
      return;
    }
    const W = M.current?.getBoundingClientRect();
    W && S({
      kind: "fromEmpty",
      clientX: W.left + W.width / 2,
      clientY: W.top + W.height / 2
    });
  }, Q = (L) => {
    const W = a ? L.filter((K) => K.type === "select") : L;
    W.length !== 0 && m((K) => _u(W, K));
  }, ge = (L) => {
    a || g((W) => Du(L, W));
  }, me = (L) => !L.source || !L.target || L.source === L.target || !c ? !1 : !L.targetHandle, Re = (L) => {
    if (!e?.state.rootActivity || !t || !c || !me(L)) return;
    const W = Mi(L.source, L.target, L.sourceHandle ?? "Done", L.targetHandle ?? void 0), K = $u(W, b);
    g(K), X(x, K);
  }, _e = () => {
    X(x, b);
  }, Te = !a && x.length > 0, ot = oe(() => {
    if (a || x.length === 0) return;
    const L = t?.slot.mode === "sequence" ? "sequence" : "flowchart", W = Yb(x, b, L), K = x.map((te) => {
      const se = W.get(te.id);
      return se ? { ...te, position: se } : te;
    });
    m(K), X(K, b), window.requestAnimationFrame(() => v?.fitView({ padding: 0.2 })), y("Rearranged the canvas.");
  }, [b, x, t, a, X, v, y]), Xe = (L, W) => {
    if (!W.nodeId || W.handleType === "target") {
      A.current = null;
      return;
    }
    A.current = {
      nodeId: W.nodeId,
      handleId: W.handleId ?? null
    };
  }, Ye = (L, W) => {
    const K = pb(A.current, W);
    if (A.current = null, !K || !c || W.toNode || W.toHandle || fb(L)) return;
    const te = Nd(L);
    S({
      kind: "fromPort",
      sourceNodeId: K.nodeId,
      sourceHandleId: K.handleId,
      clientX: te.x,
      clientY: te.y
    });
  }, $e = (L, W) => {
    if (!c || !me(W)) return;
    const K = Nw(L, {
      ...W,
      sourceHandle: W.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: W.targetHandle ?? null
    }, b, { shouldReplaceId: !1 });
    g(K), X(x, K);
  }, Oe = (L) => {
    if (a || L.length === 0) return;
    const W = new Set(L.map((se) => se.id)), K = x.filter((se) => !W.has(se.id)), te = b.filter((se) => !W.has(se.source) && !W.has(se.target));
    m(K), g(te), l && W.has(l) && p(null), X(K, te);
  }, He = (L) => {
    if (a || L.length === 0) return;
    const W = new Set(L.map((te) => te.id)), K = b.filter((te) => !W.has(te.id));
    g(K), X(x, K);
  }, Ne = oe((L) => {
    if (a) return;
    const W = b.filter((K) => K.id !== L);
    g(W), X(x, W);
  }, [X, b, a, x]), Rt = oe((L, W, K) => {
    c && S({ kind: "spliceEdge", edgeId: L, clientX: W, clientY: K });
  }, [c]), Ae = (L) => {
    const W = N;
    if (!W) return;
    S(null);
    const K = q(W.clientX, W.clientY) ?? { x: 0, y: 0 };
    if (W.kind === "fromEmpty") {
      const se = O(L, K), ye = [...x.map((Ie) => Ie.selected ? { ...Ie, selected: !1 } : Ie), se.node];
      m(ye), p(se.node.id), X(ye, b, [se.activityNode]);
      return;
    }
    if (W.kind === "fromPort") {
      const se = x.find((xe) => xe.id === W.sourceNodeId), he = se ? xc(se) : K, ye = O(L, he), Ce = [...x.map((xe) => xe.selected ? { ...xe, selected: !1 } : xe), ye.node], ze = [...b, Mi(W.sourceNodeId, ye.node.id, W.sourceHandleId ?? "Done")];
      m(Ce), g(ze), p(ye.node.id), X(Ce, ze, [ye.activityNode]);
      return;
    }
    const te = b.find((se) => se.id === W.edgeId);
    te && le(L, te, K);
  }, dn = de(() => ({
    highlightedEdgeId: k,
    deleteEdge: Ne,
    requestInsertActivity: Rt
  }), [Ne, k, Rt]);
  return {
    nodes: x,
    edges: b,
    canvasRef: M,
    setReactFlowInstance: j,
    connectMenu: N,
    setConnectMenu: S,
    edgeActions: dn,
    onNodesChange: Q,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: He,
    isValidConnection: me,
    onConnect: Re,
    onConnectStart: Xe,
    onConnectEnd: Ye,
    onReconnect: $e,
    commitLayout: _e,
    canAutoLayout: Te,
    autoLayout: ot,
    onCanvasDragOver: J,
    onCanvasDragLeave: re,
    onCanvasDrop: fe,
    openEmptyConnectMenu: V,
    onConnectMenuPick: Ae,
    addActivity: H,
    onPaletteClick: ce,
    onPaletteDragStart: z,
    onPaletteDragEnd: Y,
    onPalettePointerDown: ae
  };
}
const tN = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function _d(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function ws(e) {
  return _d(e.name);
}
function nN(e, t) {
  const n = ws(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : Td(i, t);
}
function Dd(e, t) {
  return Td(e[ws(t)], t);
}
function iN(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function rN(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function jc(e, t, n) {
  return {
    ...e,
    [ws(t)]: n
  };
}
function oN(e, t) {
  return t.isWrapped === !1 ? nN(e, t) : Dd(e, t).expression.value;
}
function Td(e, t) {
  return pN(e) ? {
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
const sN = /* @__PURE__ */ new Set([
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
function aN(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const r = t.slice(0, i), s = (r.split(".").pop() ?? r).toLowerCase();
  return sN.has(s) ? { elementTypeName: cN(t.slice(i)) } : null;
}
function cN(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function lN(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function uN(e) {
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
function dN(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function fN(e, t) {
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
function pN(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function $d(e) {
  return vs(e?.trim() ?? "") || e;
}
function vs(e) {
  if (!e) return "";
  const t = hN(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${vs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const r = Sc(t.slice(0, i)), s = gN(t.slice(i));
    return s.length > 0 ? `${r}<${s.join(", ")}>` : r;
  }
  return Sc(t);
}
function hN(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function Sc(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function gN(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Cc(e, t);
  return n == null ? [] : yN(n).map((i) => {
    const r = i.trim(), s = r.startsWith("[") ? Cc(r, 0) ?? r : r;
    return vs(s);
  }).filter(Boolean);
}
function Cc(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function yN(e) {
  const t = [];
  let n = 0, i = 0;
  for (let r = 0; r < e.length; r++) {
    const s = e[r];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, r)), i = r + 1);
  }
  return t.push(e.slice(i)), t.map((r) => r.trim()).filter(Boolean);
}
const Ec = "elsa-studio:apply-workflow-graph-operation-batch", kc = "elsa-studio:undo-workflow-graph-operation-batch", mN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function xN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = EN(e), r = Md(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = CN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = SN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", r), h = wN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && vN(i.state.rootActivity, h);
      const y = xt(d.position) ? Po(d.position, { x: 280, y: 160 }) : null;
      y && (i.layout = Ic(i.layout, p, y));
      continue;
    }
    if (l === "set-root") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Mt(d.activityId, s);
      if (!f || !bs(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = Ic(i.layout, f, Po(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      jN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = go(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = xt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Mt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Pd(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      bN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      NN(i, d, s);
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
function wN(e, t, n) {
  const i = e.parameters ?? {}, r = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === r || a.activityTypeKey === r || a.displayName === Le(i.displayName));
  return s ? wo(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: r,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: Po(i.position, { x: 280, y: 160 }) }
  };
}
function vN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = Ns(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function bN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const r = Mt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Mt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!r || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${r}-${s}`;
  a.connections = [
    ...c.filter((l) => !xt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: r, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function NN(e, t, n) {
  const i = e.state.rootActivity, r = i?.structure?.payload.connections;
  if (!Array.isArray(r)) return;
  const s = Le(t.connectionId), a = Mt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Mt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = r.filter((u) => {
    if (!xt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = xt(u.source) ? u.source.nodeId : void 0, d = xt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function jN(e, t, n) {
  const i = xt(n);
  e[_d(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function go(e, t, n, i) {
  const r = Mt(t, n);
  return r ? bs(e.state.rootActivity, r) ?? i.get(r) ?? null : null;
}
function Mt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function bs(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Rd(e)) {
    const i = bs(n, t);
    if (i) return i;
  }
  return null;
}
function Pd(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = Ns(e);
  if (n) {
    const i = n.map((r) => Pd(r, t)).filter((r) => !!r);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Md(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Rd(e)) Md(n, t);
  return t;
}
function Rd(e) {
  return Ns(e) ?? [];
}
function Ns(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function Ic(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function Po(e, t) {
  const n = xt(e) ? e : {}, i = Number(n.x), r = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(r) ? Math.max(40, Math.round(r)) : t.y
  };
}
function SN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, r = 2;
  for (; t.has(i); )
    i = `${n}-${r}`, r += 1;
  return t.add(i), i;
}
function CN(e) {
  return typeof e == "number" ? mN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function EN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function xt(e) {
  return typeof e == "object" && e !== null;
}
function kN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: r,
  setError: s
}) {
  const a = ne(/* @__PURE__ */ new Map());
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
        const p = Sn(e), h = xN(e, d.batch, n), y = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(Ec, c), window.addEventListener(kc, u), () => {
      window.removeEventListener(Ec, c), window.removeEventListener(kc, u);
    };
  }, [n, t, e, i, r, s]);
}
function IN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: r, setError: s }) {
  const [a, c] = B(n), u = ne(""), l = ne(0), d = ne(Promise.resolve()), f = oe((h) => {
    u.current = h ? Fe(h) : "";
  }, []), p = oe(async (h, y) => {
    const w = async () => {
      const m = ++l.current, b = Fe(h);
      s("");
      try {
        const g = await Rp(e, h), v = Fe(g);
        return u.current = v, i(({ draft: j }) => !j || j.id !== g.id ? null : Fe(j) === b ? g : { ...j, validationErrors: g.validationErrors }), m === l.current && r(y), g;
      } catch (g) {
        throw m === l.current && (r(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(w, w);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, r, s]);
  return G(() => {
    if (!a || !t || Fe(t) === u.current) return;
    r("Autosaving...");
    const y = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, W0);
    return () => window.clearTimeout(y);
  }, [a, t, p, r]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function AN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: r, setError: s }) {
  const [a, c] = B(null), [u, l] = B([]), [d, f] = B([]), [p, h] = B(null), [y, w] = B(Si), [x, m] = B("loading"), b = oe(async () => {
    s(""), m("loading");
    const [g, v, j, N, S] = await Promise.all([
      kp(e, t),
      Yo(e),
      Xp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Yp(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: Si })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      gl(e).then(
        (D) => D,
        () => null
      )
    ]), k = g.draft ?? null;
    c(g), r(k), n(k), i(k), l(v.activities ?? []), f(j.descriptors), h(S), w(N.descriptors.length > 0 ? N.descriptors : Si), m(j.ok ? "ready" : "failed");
  }, [e, t, n, i, r, s]);
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
function _N({ context: e, details: t, setDetails: n, setStatus: i }) {
  const r = ne(null), s = ne(null), a = ne({});
  G(() => {
    r.current = t;
  }, [t]);
  const c = oe(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = r.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Mp(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = oe((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return G(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function DN({
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
  const y = oe(() => {
    if (!t) return;
    const b = n?.definition.name;
    v0(m0(t, b), b), d("Exported workflow as JSON.");
  }, [t, n, d]), w = oe(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await r(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, r, l, d]), x = oe(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await r(t, "Saved"), d("Promoting...");
        const b = await zp(e, t.id), g = await Lp(e, b.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (b) {
        d(""), f(b instanceof Error ? b.message : String(b));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, r, s, u, l, d, f]), m = oe(async () => {
    if (!t?.state.rootActivity || i) return;
    const b = t, g = Fe(b);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const v = mb(b);
      l("testRunStarting"), d("Starting test run...");
      const j = await Vp(e, {
        definitionId: b.definitionId,
        snapshotId: v,
        state: b.state
      });
      a({ draftSignature: g, view: j }), p("runtime"), h(!1), d(ys(j) ? "Test run rejected" : "Test run dispatched");
    } catch (v) {
      d(""), f(v instanceof Error ? v.message : String(v));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: y, save: w, promoteAndPublish: x, run: m };
}
function TN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: r,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = de(() => new Map(r.map((S) => [S.activityVersionId, S])), [r]), l = oe(
    (S) => lh([S.activityVersionId, S.activityTypeKey], a),
    [a]
  ), d = de(() => sb(s), [s]), f = de(() => Gc(c, n, u), [c, n, u]), p = il(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", y = de(() => h ? null : Tn(c, n, u), [c, n, u, h]), w = de(() => h && f?.nodeId === i ? f : y?.slot.activities.find((S) => S.nodeId === i) ?? null, [h, y, f, i]), x = de(
    () => w ? ab(w, u, d) : null,
    [u, d, w]
  ), m = de(
    () => w ? l({ activityVersionId: w.activityVersionId, activityTypeKey: u.get(w.activityVersionId)?.activityTypeKey }) : null,
    [l, u, w]
  ), b = w ? Me(w, u) : [], g = w ? mp(w, u.get(w.activityVersionId)) : !1, v = Ap(e, t?.state, i, u), j = !h && y?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: y,
    selectedNode: w,
    selectedDescriptor: x,
    selectedNodeAvailability: m,
    selectedSlots: b,
    selectedSupportsScopedVariables: g,
    scopedVariableAnalysis: v,
    isFlowchartDesigner: j,
    canAddActivitiesToCanvas: !c || !h
  };
}
function $N({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: r,
  catalogByVersion: s
}) {
  G(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: hb(t),
        selectedNodeId: i,
        selectedActivityType: r?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: jd(t.state.rootActivity, s),
        connections: Sd(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, r, n, i]);
}
function PN({
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
      /* @__PURE__ */ o.jsx(Gi, { size: 14, "aria-hidden": "true" }),
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
              f ? /* @__PURE__ */ o.jsx(Vc, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 }),
              /* @__PURE__ */ o.jsx("span", { children: d.category }),
              /* @__PURE__ */ o.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ o.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), y = h ? `wf-palette-description-${p.activityVersionId}` : void 0, w = Ee(p), x = er(p);
          return /* @__PURE__ */ o.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ee(p),
              "aria-describedby": y,
              onClick: () => s(p),
              onDragStart: (m) => a(m, p),
              onDragEnd: (m) => c(m, p),
              onPointerDown: (m) => u(m, p),
              children: [
                /* @__PURE__ */ o.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: hs(x) }),
                /* @__PURE__ */ o.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ o.jsx("strong", { children: w }),
                  h ? /* @__PURE__ */ o.jsx("small", { id: y, children: h }) : null
                ] }),
                /* @__PURE__ */ o.jsx(Oc, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const zd = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), MN = "Variable";
function RN({
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
  const d = BN(l), f = r.length > 0 ? r : tN;
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ o.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ o.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ o.jsx(
        zN,
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
function zN({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: r,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: r, readOnly: u }, d = Lo(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? Dd(e, t) : null, h = p?.expression.type ?? "Literal", y = oN(e, t), w = h.toLowerCase(), m = p && (w === "literal" || w === "object") && !lN(t) ? aN(t.typeName) : null, b = m ? Lo(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: r,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, v = g ? Vd(i, g) : null, j = v?.surfaces.inline, N = v && g ? Od(v, g, y) : [], S = m != null, k = !!(p && !S && KN(t, d?.id)), D = !!(p && !S && XN(t, d?.id)), [M, A] = B(!1), I = (E) => {
    const T = p ? iN(p, E) : E;
    c(jc(e, t, T));
  }, R = (E) => {
    p && c(jc(e, t, rN(p, E)));
  }, C = m ? b ? Mo(b.component, t, y, u, { ...l, scope: "collection" }, I) : /* @__PURE__ */ o.jsx(
    VN,
    {
      input: t,
      elementTypeName: m.elementTypeName,
      value: y,
      editors: n,
      context: l,
      disabled: u,
      onChange: I
    }
  ) : null, _ = h === MN && p ? /* @__PURE__ */ o.jsx(
    WN,
    {
      value: y,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: I
    }
  ) : C ?? (j && g ? /* @__PURE__ */ o.jsx(
    j,
    {
      descriptor: t,
      syntax: h,
      value: y,
      disabled: u,
      context: g,
      onChange: I
    }
  ) : Mo(f, t, y, u, l, I));
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ o.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ o.jsx("span", { children: $d(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ o.jsx("p", { children: t.description }) : null,
    p && !k ? /* @__PURE__ */ o.jsx(
      Ro,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: r,
        disabled: u,
        onChange: R
      }
    ) : null,
    k ? /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-expression-editor", children: [
        _,
        Vo(N)
      ] }),
      /* @__PURE__ */ o.jsx(
        Ro,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: r,
          disabled: u,
          variant: "inline",
          onChange: R
        }
      ),
      D ? /* @__PURE__ */ o.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => A(!0),
          children: /* @__PURE__ */ o.jsx(_n, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      _,
      Vo(N)
    ] }),
    D && !k ? /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => A(!0),
        children: [
          /* @__PURE__ */ o.jsx(_n, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ o.jsx(
      ON,
      {
        input: t,
        value: y,
        syntax: h,
        descriptors: r,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: I,
        onSyntaxChange: R,
        onClose: () => A(!1)
      }
    ) : null
  ] });
}
function LN(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function VN({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: r,
  disabled: s,
  onChange: a
}) {
  const c = uN(n), u = fN(e, t), l = { ...r, scope: "element" }, d = Lo(i, u, l)?.component, f = e.displayName || e.name, p = (j, N) => a(c.map((S, k) => k === j ? N : S)), [h, y] = B(null), [w, x] = B(null), m = () => {
    y(null), x(null);
  }, b = (j) => (N) => {
    y(j), N.dataTransfer.effectAllowed = "move", N.dataTransfer.setData("text/plain", String(j));
  }, g = (j) => (N) => {
    h !== null && (N.preventDefault(), N.dataTransfer.dropEffect = "move", w !== j && x(j));
  }, v = (j) => (N) => {
    N.preventDefault(), h !== null && h !== j && a(ho(c, h, j)), m();
  };
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ o.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ o.jsx("ul", { className: "wf-collection-items", children: c.map((j, N) => /* @__PURE__ */ o.jsxs(
      "li",
      {
        className: LN(N, h, w),
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
              onDragEnd: m,
              children: /* @__PURE__ */ o.jsx(Oc, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ o.jsx("div", { className: "wf-collection-item-editor", children: Mo(d, u, j, s, l, (S) => p(N, S)) }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${N + 1} up`,
                disabled: s || N === 0,
                onClick: () => a(ho(c, N, N - 1)),
                children: /* @__PURE__ */ o.jsx(tf, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(Vc, { size: 13 })
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
                children: /* @__PURE__ */ o.jsx(An, { size: 13 })
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
        onClick: () => a([...c, dN(t)]),
        children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function ON({
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
  const d = Pc(), f = e.displayName || e.name, p = {
    activity: r,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Vd(s, p), y = h?.surfaces.expanded, w = h ? Od(h, p, t) : [], x = y ? null : FN(s, p);
  return G(() => {
    const m = (b) => {
      b.key === "Escape" && l();
    };
    return window.addEventListener("keydown", m), () => window.removeEventListener("keydown", m);
  }, [l]), /* @__PURE__ */ o.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ o.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("div", { children: [
        /* @__PURE__ */ o.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ o.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ o.jsx(Lc, { size: 16 }) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ o.jsx(
          Ro,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ o.jsx("span", { children: $d(e.typeName) })
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
        x ? /* @__PURE__ */ o.jsx("p", { className: "wf-expression-editor-hint", children: x }) : null,
        /* @__PURE__ */ o.jsx(
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
      Vo(w)
    ] }),
    /* @__PURE__ */ o.jsxs("footer", { children: [
      /* @__PURE__ */ o.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function Mo(e, t, n, i, r, s) {
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
function Ro({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: r = "block",
  onChange: s
}) {
  const [a, c] = B(!1), u = Pc(), l = n.find((f) => f.type === t), d = [
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
const zo = "::";
function Ld(e) {
  return !e || e === zi ? zi : e;
}
function Ac(e, t) {
  return `${Ld(t)}${zo}${e}`;
}
function HN(e) {
  const t = e.indexOf(zo);
  if (t < 0) return null;
  const n = e.slice(t + zo.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function WN({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: r }) {
  const s = fl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? Ac(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Ld(c.declaringScopeId)
  );
  return /* @__PURE__ */ o.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ o.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = HN(d.target.value);
          f && r(wp(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ o.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ o.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = Ac(d.referenceKey, d.scopeId);
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
function Lo(e, t, n) {
  return [...e].sort((i, r) => (i.order ?? 500) - (r.order ?? 500)).find((i) => i.supports(t, n));
}
function Vd(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function Od(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function FN(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", r = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return r ? `${s} ${r}` : s;
}
function Vo(e) {
  return e.length === 0 ? null : /* @__PURE__ */ o.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ o.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ o.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function BN(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function KN(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !zd.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function XN(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !zd.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function YN({
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
      /* @__PURE__ */ o.jsx($i, { size: 14 }),
      /* @__PURE__ */ o.jsxs("span", { children: [
        "No longer available for new use · ",
        Li(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ o.jsx(
      RN,
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
      L0,
      {
        context: e,
        variables: dl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: bp(h.shadowingWarnings, t.nodeId),
        onChange: (x) => y(xp(t, x))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ o.jsx("span", { children: "Embedded slots" }),
      a.map((x) => /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => w(t, x.id, `${n} / ${x.label}`), children: [
        x.label,
        /* @__PURE__ */ o.jsx("small", { children: yb(x, c) })
      ] }, x.id))
    ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." })
  ] }) : /* @__PURE__ */ o.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Hd = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Wd({ label: e, hint: t }) {
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ o.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function qN({ value: e, onChange: t }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Hd.map((n) => {
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
      /* @__PURE__ */ o.jsx(Wd, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function UN({ onPick: e }) {
  return /* @__PURE__ */ o.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Hd.map((t) => /* @__PURE__ */ o.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ o.jsx(Wd, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function ZN({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const r = (s) => {
    const a = md(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ o.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ o.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ o.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ o.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ o.jsx(UN, { onPick: r }),
    /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ o.jsx(Fn, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ o.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function GN({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: r,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = Bb(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: y,
    replaceDraftByBatch: w,
    editDraft: x,
    editDraftAndSelect: m,
    select: b,
    navigateToScope: g,
    resetToRoot: v,
    enterSlot: j,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k
  } = u, [D, M] = B(""), [A, I] = B(""), [R, C] = B("idle"), [_, E] = B(() => /* @__PURE__ */ new Set()), [T, P] = B(""), [$, F] = B("activities"), [H, O] = B("inspector"), [X, q] = B("designer"), {
    paletteWidth: ee,
    inspectorWidth: le,
    paletteCollapsed: U,
    inspectorCollapsed: z,
    maximizedSidePanel: Y,
    setInspectorCollapsed: ae,
    paletteExpanded: ce,
    inspectorExpanded: J,
    editorBodyClassName: re,
    editorBodyStyle: fe,
    toggleSidePanelCollapsed: V,
    toggleSidePanelMaximized: Q,
    startSidePanelResize: ge,
    handleSidePanelResizeKeyDown: me
  } = Ad(), { resetHistory: Re, undo: _e, redo: Te, canUndoNow: ot, canRedoNow: Xe } = Hb({ draft: l, restoreDraft: y }), { saveDraft: Ye, autosaveEnabled: $e, setAutosaveEnabled: Oe, markSaved: He } = IN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: I, setError: M }), {
    details: Ne,
    setDetails: Rt,
    catalog: Ae,
    activityDescriptors: dn,
    availabilityDiagnostics: L,
    expressionDescriptors: W,
    descriptorStatus: K,
    reload: te
  } = AN({ context: e, definitionId: t, resetHistory: Re, loadDraft: y, markSaved: He, setError: M }), { updateDefinitionMeta: se } = _N({ context: e, details: Ne, setDetails: Rt, setStatus: I }), {
    catalogByVersion: he,
    availabilityLookup: ye,
    scopeOwner: Ie,
    isUnsupportedDesigner: Ce,
    scope: ze,
    selectedNode: xe,
    selectedDescriptor: zt,
    selectedNodeAvailability: gr,
    selectedSlots: ei,
    selectedSupportsScopedVariables: yr,
    scopedVariableAnalysis: mr,
    isFlowchartDesigner: ft,
    canAddActivitiesToCanvas: xr
  } = TN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: Ae, activityDescriptors: dn, availabilityDiagnostics: L }), Lt = de(() => $o(Ae), [Ae]), wr = de(() => {
    const Z = T.trim().toLowerCase();
    if (!Z) return Lt;
    const ue = Ae.filter((be) => Ee(be).toLowerCase().includes(Z) || be.activityTypeKey.toLowerCase().includes(Z) || (be.category ?? "").toLowerCase().includes(Z) || (be.description ?? "").toLowerCase().includes(Z));
    return $o(ue);
  }, [Ae, T, Lt]), Vt = R !== "idle", vr = !!l?.state.rootActivity && !Vt, ti = $t(n, "weaver.workflows.find-draft-risks"), ni = $t(n, "weaver.workflows.propose-update"), br = eN({
    draft: l,
    scope: ze,
    scopeOwner: Ie,
    frames: d,
    catalog: Ae,
    catalogByVersion: he,
    isUnsupportedDesigner: Ce,
    isFlowchartDesigner: ft,
    canAddActivitiesToCanvas: xr,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: m,
    select: b,
    resetToRoot: v,
    setStatus: I,
    setError: M
  }), {
    nodes: Ot,
    edges: Nr,
    canvasRef: jr,
    setReactFlowInstance: Sr,
    connectMenu: Ht,
    setConnectMenu: ii,
    edgeActions: ri,
    onNodesChange: Cr,
    onEdgesChange: Er,
    onNodesDelete: kr,
    onEdgesDelete: Ir,
    isValidConnection: Ar,
    onConnect: _r,
    onConnectStart: oi,
    onConnectEnd: Dr,
    onReconnect: Tr,
    commitLayout: $r,
    canAutoLayout: Pr,
    autoLayout: Mr,
    onCanvasDragOver: si,
    onCanvasDragLeave: ai,
    onCanvasDrop: ci,
    openEmptyConnectMenu: fn,
    onConnectMenuPick: Rr,
    addActivity: zr,
    onPaletteClick: Lr,
    onPaletteDragStart: Vr,
    onPaletteDragEnd: Or,
    onPalettePointerDown: Hr
  } = br, Wr = !Ce && d.length > 0 && !!ze && ze.slot.activities.length === 0 && Ot.length === 0;
  kN({ draft: l, details: Ne, catalog: Ae, replaceDraftByBatch: w, setStatus: I, setError: M }), G(() => {
    !l?.state.rootActivity || Ae.length === 0 || x(({ draft: Z }) => {
      if (!Z?.state.rootActivity) return null;
      const ue = ol(Z.state.rootActivity, he);
      return !ue || ue === Z.state.rootActivity ? null : {
        ...Z,
        state: {
          ...Z.state,
          rootActivity: ue
        }
      };
    });
  }, [Ae.length, he, l?.state.rootActivity, x]), $N({ details: Ne, draft: l, selectedNode: xe, selectedNodeId: f, selectedDescriptor: zt, catalogByVersion: he }), G(() => {
    E((Z) => {
      let ue = !1;
      const be = new Set(Z);
      for (const st of Lt)
        be.has(st.category) || (be.add(st.category), ue = !0);
      return ue ? be : Z;
    });
  }, [Lt]);
  const { exportJson: Fr, save: li, promoteAndPublish: Br, run: ui } = DN({
    context: e,
    draft: l,
    details: Ne,
    busy: Vt,
    saveDraft: Ye,
    reload: te,
    startTestRun: N,
    clearTestRun: S,
    setPublishedArtifact: k,
    setOperation: C,
    setStatus: I,
    setError: M,
    setActiveRightPanelId: O,
    setInspectorCollapsed: ae
  }), Kr = oe((Z) => {
    x(({ draft: ue }) => ue ? { ...ue, state: Z(ue.state) } : null);
  }, [x]), Xr = oe((Z) => {
    if (!l) return "No draft is loaded.";
    const ue = w0(Z, l);
    return ue.ok ? (y(ue.draft), I("Applied workflow JSON."), null) : ue.error;
  }, [l, y]);
  G(() => {
    const Z = (ue) => {
      if (X !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const be = ue.target;
      if (be && (be.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(be.tagName))) return;
      const st = ue.key.toLowerCase();
      st === "z" && !ue.shiftKey ? (ue.preventDefault(), _e()) : (st === "z" && ue.shiftKey || st === "y") && (ue.preventDefault(), Te());
    };
    return window.addEventListener("keydown", Z), () => window.removeEventListener("keydown", Z);
  }, [X, _e, Te]);
  const pn = oe((Z) => {
    x(({ draft: ue }) => {
      const be = ue?.state.rootActivity;
      return !ue || !be ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: rl(be, Z.nodeId, () => Z, he)
        }
      };
    });
  }, [he, x]), Yr = oe((Z) => {
    if (!Z) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const be = Hf(ue, Z, (st) => {
      const ks = he.get(st.activityVersionId);
      return ks ? Ee(ks) : st.nodeId;
    }, he);
    be && (q("designer"), g(be, Z), ae(!1));
  }, [l?.state.rootActivity, he, ae, g]), qr = (Z) => {
    E((ue) => {
      const be = new Set(ue);
      return be.has(Z) ? be.delete(Z) : be.add(Z), be;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: D || "Loading workflow editor..." });
  const vt = p?.draftSignature === Fe(l) ? p.view : null, js = vt && A.startsWith("Test run") ? "" : A, Fd = (Z) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(Z)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Bd = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: xe,
    selectedActivityDescriptor: zt,
    selectedActivitySlots: ei,
    catalog: Ae,
    currentScopeOwner: Ie,
    frames: d
  }, Ss = s.map((Z) => {
    const ue = Z.component;
    return {
      id: Z.id,
      title: Z.title,
      side: Z.side,
      order: Z.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ o.jsx(ue, { context: Bd })
    };
  }), Ur = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Fn, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        PN,
        {
          paletteSearch: T,
          onSearchChange: P,
          groups: wr,
          expandedCategories: _,
          onToggleCategory: qr,
          onActivityClick: Lr,
          onActivityDragStart: Vr,
          onActivityDragEnd: Or,
          onActivityPointerDown: Hr
        }
      )
    },
    ...Ss.filter((Z) => Z.side === "left")
  ].sort(wc), Zr = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ o.jsx(Wo, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        YN,
        {
          context: e,
          selectedNode: xe,
          selectedNodeLabel: xe ? Ot.find((Z) => Z.id === xe.nodeId)?.data.label ?? xe.nodeId : "",
          selectedActivityType: xe ? zt?.typeName ?? he.get(xe.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: zt,
          selectedNodeAvailability: gr,
          selectedSlots: ei,
          catalogByVersion: he,
          selectedSupportsScopedVariables: yr,
          propertyEditors: i,
          expressionEditors: r,
          expressionDescriptors: W,
          descriptorStatus: K,
          scopedVariableAnalysis: mr,
          onSelectedActivityChange: pn,
          onEnterSlot: j
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(Tb, { testRun: vt, onOpenRun: Fd })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ o.jsx(Hc, { size: 15 }),
      render: () => /* @__PURE__ */ o.jsx(
        Mb,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...Ss.filter((Z) => Z.side === "right")
  ].sort(wc), Cs = Ur.find((Z) => Z.id === $) ?? Ur[0], Es = Zr.find((Z) => Z.id === H) ?? Zr[0], Kd = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ o.jsx(Wc, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ o.jsx(af, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ o.jsx(Ho, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ o.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ o.jsx(yt, { size: 14 }),
      /* @__PURE__ */ o.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-chip", children: "Draft" }),
      js ? /* @__PURE__ */ o.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ o.jsx(an, { size: 13 }),
        " ",
        js
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
              disabled: !ot,
              onClick: _e,
              children: /* @__PURE__ */ o.jsx(nf, { size: 16 })
            }
          ),
          /* @__PURE__ */ o.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !Xe,
              onClick: Te,
              children: /* @__PURE__ */ o.jsx(rf, { size: 16 })
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
              children: /* @__PURE__ */ o.jsx(of, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ o.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ o.jsx("input", { className: "wf-autosave-switch-input", type: "checkbox", checked: $e, onChange: (Z) => Oe(Z.target.checked) }),
          /* @__PURE__ */ o.jsx("span", { children: "Autosave" })
        ] }),
        ti ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Pt(n, ti, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(lt, { size: 15 }),
          " Risks"
        ] }) : null,
        ni ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Pt(n, ni, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ o.jsx(lt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: Fr, children: [
          /* @__PURE__ */ o.jsx(sf, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Vt, onClick: () => {
          li();
        }, children: [
          /* @__PURE__ */ o.jsx(Mc, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ o.jsxs("button", { type: "button", disabled: Vt, onClick: () => {
          Br();
        }, children: [
          /* @__PURE__ */ o.jsx(zc, { size: 15 }),
          " Promote"
        ] }),
        vt ? /* @__PURE__ */ o.jsx(
          Db,
          {
            testRun: vt,
            onOpenDetails: () => {
              O("runtime"), ae(!1);
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
              ui();
            },
            children: [
              /* @__PURE__ */ o.jsx(Zt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    D ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(wt, { size: 16 }),
      " ",
      D
    ] }) : null,
    /* @__PURE__ */ o.jsxs("div", { className: re, style: fe, children: [
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ti,
            {
              label: "Activities panel tabs",
              tabs: Ur,
              activeTabId: Cs.id,
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
                children: U ? /* @__PURE__ */ o.jsx(yt, { size: 14 }) : /* @__PURE__ */ o.jsx(Dn, { size: 14 })
              }
            ),
            U ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Y === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: Y === "palette" ? "Restore" : "Maximize",
                onClick: () => Q("palette"),
                children: Y === "palette" ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(_n, { size: 14 })
              }
            )
          ] })
        ] }),
        ce ? Cs.render() : null
      ] }),
      ce && !Y ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": bn,
          "aria-valuemax": Nn,
          "aria-valuenow": ee,
          tabIndex: 0,
          onPointerDown: (Z) => ge("palette", Z),
          onKeyDown: (Z) => me("palette", Z)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ o.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ o.jsx(
          Ti,
          {
            label: "Editor view tabs",
            tabs: Kd,
            activeTabId: X,
            onSelect: (Z) => q(Z)
          }
        ) }),
        X === "code" ? /* @__PURE__ */ o.jsx(C0, { draft: l, onApply: Xr }) : X === "properties" ? /* @__PURE__ */ o.jsx(O0, { details: Ne, draft: l, context: e, onStateChange: Kr, onDefinitionMetaChange: se }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
          /* @__PURE__ */ o.jsxs("div", { className: "wf-breadcrumb", children: [
            /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => v(), children: "Root" }),
            d.map((Z, ue) => /* @__PURE__ */ o.jsxs(tt.Fragment, { children: [
              /* @__PURE__ */ o.jsx(yt, { size: 13 }),
              /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => g(d.slice(0, ue + 1), null), children: Z.label })
            ] }, `${Z.ownerNodeId}-${Z.slotId}-${ue}`))
          ] }),
          /* @__PURE__ */ o.jsxs("div", { className: "wf-canvas", ref: jr, onDragOver: si, onDragLeave: ai, onDrop: ci, children: [
            /* @__PURE__ */ o.jsx(hd.Provider, { value: ri, children: /* @__PURE__ */ o.jsx(gd.Provider, { value: ye, children: /* @__PURE__ */ o.jsxs(
              rd,
              {
                nodes: Ot,
                edges: Nr,
                nodeTypes: Ed,
                edgeTypes: kd,
                onInit: Sr,
                onNodesChange: Cr,
                onEdgesChange: Er,
                onNodesDelete: kr,
                onEdgesDelete: Ir,
                onConnect: _r,
                onConnectStart: ft ? oi : void 0,
                onConnectEnd: ft ? Dr : void 0,
                onReconnect: ft ? Tr : void 0,
                isValidConnection: Ar,
                onDragOver: si,
                onDragLeave: ai,
                onDrop: ci,
                onPaneClick: () => b(null),
                onNodeClick: (Z, ue) => b(ue.id),
                onNodeDragStop: Ce ? void 0 : $r,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: ft,
                nodesDraggable: !Ce,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: Ce ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ o.jsx(sd, { gap: 18, size: 1 }),
                  /* @__PURE__ */ o.jsx(cd, {}),
                  /* @__PURE__ */ o.jsx(ud, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }),
            Wr ? /* @__PURE__ */ o.jsx(
              ZN,
              {
                slotLabel: ze?.slot.label ?? "this slot",
                catalog: Ae,
                onPickActivity: zr,
                onBrowseAll: fn
              }
            ) : ft && Ot.length === 0 ? /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => fn(), children: [
              /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
              " Add activity"
            ] }) : null,
            Ht ? /* @__PURE__ */ o.jsx(
              Sb,
              {
                clientX: Ht.clientX,
                clientY: Ht.clientY,
                activities: Ae,
                onPick: Rr,
                onClose: () => ii(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ o.jsx(_b, { draft: l, onRepair: Yr })
        ] })
      ] }),
      J && !Y ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": Yt,
          "aria-valuenow": le,
          tabIndex: 0,
          onPointerDown: (Z) => ge("inspector", Z),
          onKeyDown: (Z) => me("inspector", Z)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ o.jsx(
            Ti,
            {
              label: "Inspector panel tabs",
              tabs: Zr,
              activeTabId: Es.id,
              onSelect: O
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
                onClick: () => V("inspector"),
                children: z ? /* @__PURE__ */ o.jsx(Dn, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 })
              }
            ),
            z ? null : /* @__PURE__ */ o.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": Y === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: Y === "inspector" ? "Restore" : "Maximize",
                onClick: () => Q("inspector"),
                children: Y === "inspector" ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(_n, { size: 14 })
              }
            )
          ] })
        ] }),
        J ? Es.render() : null
      ] })
    ] })
  ] });
}
function JN({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: r }) {
  const s = yd(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ o.jsx("select", { value: t, onChange: (u) => r(Number(u.target.value)), children: B0.map((u) => /* @__PURE__ */ o.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ o.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ o.jsx(Dn, { size: 14 }),
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
function QN({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: r, onClose: s, onSubmit: a }) {
  const [c, u] = B(!1), [l, d] = B(""), [f, p] = B(!1), [h, y] = B(null), [w, x] = B(null), m = ne(null), b = ne(e);
  b.current = e;
  const g = ne(r);
  g.current = r;
  const v = oe((N) => {
    const S = { ...b.current };
    N.name && (S.name = N.name), N.description && (S.description = N.description), g.current(S), y(null), x(null);
  }, []);
  G(() => {
    if (i)
      return n.onPromptResult((N) => {
        if (N.requestId !== m.current) return;
        if (m.current = null, p(!1), N.status !== "completed") {
          x(N.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const S = U0(N.text);
        if (!S) {
          x("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
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
    m.current = S, p(!0), y(null), x(null), n.dispatchPrompt({ ...N, requestId: S });
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
                /* @__PURE__ */ o.jsx(lt, { size: 13 }),
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
            /* @__PURE__ */ o.jsx(lt, { size: 13 }),
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
            qN,
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
function ej({ context: e, ai: t, onOpen: n }) {
  const [i, r] = B(""), [s, a] = B("active"), [c, u] = B(1), [l, d] = B(K0), [f, p] = B("loading"), [h, y] = B(""), [w, x] = B(""), [m, b] = B([]), [g, v] = B(0), [j, N] = B(() => /* @__PURE__ */ new Set()), [S, k] = B(null), [D, M] = B(!1), [A, I] = B([]), [R, C] = B("idle"), _ = ne(null), E = de(() => m.map((V) => V.id), [m]), T = $t(t, "weaver.workflows.suggest-create-metadata"), P = $t(t, "weaver.workflows.explain-definition"), $ = E.filter((V) => j.has(V)).length, F = E.length > 0 && $ === E.length, H = oe(async () => {
    p("loading"), y("");
    try {
      const V = await Ep(e, { search: i, state: s, page: c, pageSize: l }), Q = typeof V.totalCount == "number", ge = V.totalCount ?? V.definitions.length, me = yd(ge, l);
      if (ge > 0 && c > me) {
        u(me);
        return;
      }
      b(Q ? V.definitions : q0(V.definitions, c, l)), v(ge), p("ready");
    } catch (V) {
      y(V instanceof Error ? V.message : String(V)), p("failed");
    }
  }, [e, i, s, c, l]);
  G(() => {
    H();
  }, [H]), G(() => {
    _.current && (_.current.indeterminate = $ > 0 && !F);
  }, [F, $]);
  const O = oe(async () => {
    if (!(R === "loading" || R === "ready")) {
      C("loading");
      try {
        const V = await Yo(e);
        I(V.activities ?? []), C("ready");
      } catch (V) {
        C("failed"), y(V instanceof Error ? V.message : String(V));
      }
    }
  }, [R, e]), X = () => {
    y(""), x(""), k({ name: "", description: "", rootKind: "flowchart" }), O();
  }, q = async () => {
    if (S?.name.trim()) {
      M(!0), y(""), x("");
      try {
        const V = await Dp(e, {
          name: S.name.trim(),
          description: S.description.trim() || null,
          rootKind: S.rootKind,
          rootActivityVersionId: Z0(S, A)
        });
        k(null), n(V.definition.id);
      } catch (V) {
        y(V instanceof Error ? V.message : String(V));
      } finally {
        M(!1);
      }
    }
  }, ee = (V) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(V)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, le = async () => {
    if (m.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await H();
  }, U = () => N(/* @__PURE__ */ new Set()), z = (V, Q) => {
    N((ge) => {
      const me = new Set(ge);
      return Q ? me.add(V) : me.delete(V), me;
    });
  }, Y = (V) => {
    N((Q) => {
      const ge = new Set(Q);
      for (const me of E)
        V ? ge.add(me) : ge.delete(me);
      return ge;
    });
  }, ae = (V) => {
    a(V), u(1), U();
  }, ce = (V) => {
    r(V), u(1), U();
  }, J = async (V) => {
    if (await Ts().confirm({ message: `Delete workflow definition "${V.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), y("");
      try {
        await Tp(e, V.id), z(V.id, !1), x(`Deleted ${V.name}`), await le();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
      }
    }
  }, re = async (V) => {
    x(""), y("");
    try {
      await $p(e, V.id), z(V.id, !1), x(`Restored ${V.name}`), await le();
    } catch (Q) {
      y(Q instanceof Error ? Q.message : String(Q));
    }
  }, fe = async (V) => {
    if (await Ts().confirm({ message: `Permanently delete workflow definition "${V.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), y("");
      try {
        await Pp(e, V.id), z(V.id, !1), x(`Permanently deleted ${V.name}`), await le();
      } catch (Q) {
        y(Q instanceof Error ? Q.message : String(Q));
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
        /* @__PURE__ */ o.jsx(Gi, { size: 15 }),
        /* @__PURE__ */ o.jsx("input", { value: i, onChange: (V) => ce(V.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ o.jsx("button", { type: "button", onClick: () => {
        H();
      }, children: "Refresh" }),
      /* @__PURE__ */ o.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ o.jsxs("button", { type: "button", title: "Create workflow", onClick: X, children: [
        /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ o.jsx(Qn, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ o.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ o.jsx(wt, { size: 16 }),
      " ",
      h
    ] }) : null,
    w ? /* @__PURE__ */ o.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ o.jsx(an, { size: 14 }),
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
    f === "loading" ? /* @__PURE__ */ o.jsx(ms, {}) : null,
    f === "ready" && m.length === 0 ? /* @__PURE__ */ o.jsx(
      xs,
      {
        icon: /* @__PURE__ */ o.jsx(Hc, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ o.jsxs("button", { type: "button", className: "wf-link-button", onClick: X, children: [
          /* @__PURE__ */ o.jsx(Gt, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && m.length > 0 ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
      /* @__PURE__ */ o.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ o.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ o.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: F,
              onChange: (V) => Y(V.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ o.jsx("span", { children: "Name" }),
          /* @__PURE__ */ o.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ o.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ o.jsx("span", { children: "Actions" })
        ] }),
        m.map((V) => /* @__PURE__ */ o.jsxs(
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
              /* @__PURE__ */ o.jsx("label", { className: "wf-row-select", onClick: (Q) => Q.stopPropagation(), children: /* @__PURE__ */ o.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: j.has(V.id),
                  onChange: (Q) => z(V.id, Q.target.checked),
                  "aria-label": `Select workflow definition ${V.name}`
                }
              ) }),
              /* @__PURE__ */ o.jsxs("span", { children: [
                /* @__PURE__ */ o.jsx("strong", { children: V.name }),
                /* @__PURE__ */ o.jsx("small", { children: V.description || V.id })
              ] }),
              /* @__PURE__ */ o.jsx("span", { children: V.latestVersion ?? "No version" }),
              /* @__PURE__ */ o.jsx("span", { children: s === "deleted" ? Pe(V.deletedAt) : V.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ o.jsx("span", { children: Pe(V.lastModifiedAt) }),
              /* @__PURE__ */ o.jsx("span", { className: "wf-row-actions", onClick: (Q) => Q.stopPropagation(), children: s === "active" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), n(V.id);
                }, children: "Open" }),
                /* @__PURE__ */ o.jsx("button", { type: "button", onClick: (Q) => {
                  Q.stopPropagation(), ee(V.id);
                }, children: "Artifacts" }),
                P ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Pt(t, P, V), children: [
                  /* @__PURE__ */ o.jsx(lt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  J(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(An, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
                /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
                  re(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(Fo, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ o.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(V);
                }, children: [
                  /* @__PURE__ */ o.jsx(An, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          V.id
        ))
      ] }),
      /* @__PURE__ */ o.jsx(
        JN,
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
      QN,
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
function tj({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const r = de(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = de(() => ij(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ o.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = r.get(a.activityType), u = er(c), l = c ? Ee(c) : Tt(a.activityType) ?? a.activityType, d = Tt(a.activityType) ?? a.activityType, f = rj(a.startedAt ?? a.scheduledAt), p = ps(a.startedAt, a.completedAt);
    return /* @__PURE__ */ o.jsx("li", { children: /* @__PURE__ */ o.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ o.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: hs(u) }),
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
          /* @__PURE__ */ o.jsx(nj, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function nj({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ o.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function ij(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => _c(t.activity) - _c(n.activity) || t.index - n.index).map((t) => t.activity);
}
function _c(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function rj(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function oj({ context: e }) {
  const [t, n] = B("loading"), [i, r] = B(""), [s, a] = B(""), [c, u] = B(""), [l, d] = B([]), f = oe(async () => {
    n("loading"), r("");
    try {
      const h = await Hp(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      r(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  G(() => {
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
    t === "failed" ? /* @__PURE__ */ o.jsx(Qn, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ o.jsx(ms, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ o.jsx(
      xs,
      {
        icon: /* @__PURE__ */ o.jsx(Fn, { size: 22 }),
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
            /* @__PURE__ */ o.jsx("span", { children: bd(h.runKind) }),
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
            /* @__PURE__ */ o.jsx("span", { children: Pe(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ o.jsx("span", { children: ps(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function sj({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, r] = B("loading"), [s, a] = B(""), [c, u] = B(null), [l, d] = B(null), [f, p] = B([]), {
    inspectorWidth: h,
    inspectorCollapsed: y,
    maximizedSidePanel: w,
    inspectorExpanded: x,
    editorBodyStyle: m,
    toggleSidePanelCollapsed: b,
    toggleSidePanelMaximized: g,
    startSidePanelResize: v,
    handleSidePanelResizeKeyDown: j
  } = Ad(), N = $t(t, "weaver.workflows.explain-instance"), S = oe(async () => {
    if (!n) {
      a("No workflow execution id was provided."), r("failed");
      return;
    }
    r("loading"), a("");
    try {
      const A = await Wp(e, n), [I, R] = await Promise.all([
        _p(e, A.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        Yo(e)
      ]);
      u({
        details: A,
        definitionVersion: I.definitionVersion,
        definitionVersionError: I.error,
        activityCatalog: R.activities
      }), d(null), p([]), r("ready");
    } catch (A) {
      u(null), a(wb(A, n)), r("failed");
    }
  }, [e, n]);
  G(() => {
    S();
  }, [S]);
  const k = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, D = () => {
    const A = c?.details.instance.definitionId;
    A && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(A)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    y ? "inspector-collapsed" : "",
    w === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: k, children: [
        /* @__PURE__ */ o.jsx(Dn, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: D, children: [
        /* @__PURE__ */ o.jsx(Wc, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => {
        S();
      }, children: [
        /* @__PURE__ */ o.jsx(Fo, { size: 14 }),
        " Refresh"
      ] }),
      c && N ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Pt(t, N, c.details), children: [
        /* @__PURE__ */ o.jsx(lt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ o.jsx(Qn, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ o.jsxs("div", { className: M, style: m, children: [
      /* @__PURE__ */ o.jsx(
        aj,
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
      x && !w ? /* @__PURE__ */ o.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Xt,
          "aria-valuemax": Yt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (A) => v("inspector", A),
          onKeyDown: (A) => j("inspector", A)
        }
      ) : /* @__PURE__ */ o.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ o.jsx(
        uj,
        {
          context: e,
          ai: t,
          action: N ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? lj(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: y,
          expanded: x,
          maximized: w === "inspector",
          onToggleCollapsed: () => b("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function aj({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: r,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = cj(a), l = de(() => {
    if (!e) return { nodes: [], edges: [] };
    const d = e.state.rootActivity;
    if (!d) return { nodes: [], edges: [] };
    const f = Tn(d, a, n), p = f?.owner ?? d, h = n.find((m) => m.activityVersionId === p.activityVersionId), w = il(p, h) === "unsupported" || !f ? nl(p, n, e.layout) : tl(f, n, e.layout), x = w.nodes.map((m) => ({
      ...m,
      draggable: !1,
      connectable: !1,
      deletable: !1,
      data: {
        ...m.data,
        onEnterSlot: (b) => {
          c([...a, {
            ownerNodeId: m.id,
            slotId: b.id,
            label: `${m.data.label} / ${b.label}`
          }]);
        }
      }
    }));
    return {
      nodes: Uf(x, i.activities, i.incidents, r),
      edges: w.edges.map((m) => ({ ...m, deletable: !1 }))
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
      /* @__PURE__ */ o.jsx(un, { status: i.instance.status, subStatus: i.instance.subStatus })
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
        t ? /* @__PURE__ */ o.jsx("small", { children: xb(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ o.jsxs(
        rd,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: Ed,
          edgeTypes: kd,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ o.jsx(sd, {}),
            /* @__PURE__ */ o.jsx(ud, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ o.jsx(cd, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function cj(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function lj(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (r) => {
    if (r) {
      n.add(r.nodeId);
      for (const s of Me(r, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function uj({
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
  maximized: y = !1,
  onToggleCollapsed: w,
  onToggleMaximized: x
}) {
  const [m, b] = B("timeline");
  if (!i)
    return /* @__PURE__ */ o.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = r?.incidents.length ?? 0, v = dj(r?.activities ?? [], c), j = (S) => {
    u?.(S), b("activity");
  }, N = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ o.jsx(Wo, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ o.jsx(Rc, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ o.jsx(wt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ o.jsx(Ho, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ o.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ o.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ o.jsx(Ti, { label: "Run details tabs", tabs: N, activeTabId: m, onSelect: (S) => b(S) }),
      /* @__PURE__ */ o.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: w,
            children: p ? /* @__PURE__ */ o.jsx(Dn, { size: 14 }) : /* @__PURE__ */ o.jsx(yt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ o.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": y ? "Restore run details panel" : "Maximize run details panel",
            title: y ? "Restore" : "Maximize",
            onClick: x,
            children: y ? /* @__PURE__ */ o.jsx(yo, { size: 14 }) : /* @__PURE__ */ o.jsx(_n, { size: 14 })
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
        n ? /* @__PURE__ */ o.jsxs("button", { type: "button", onClick: () => Pt(t, n, r ?? i), children: [
          /* @__PURE__ */ o.jsx(lt, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ o.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ o.jsx(Qn, { message: a }) : null,
      s === "ready" && r ? /* @__PURE__ */ o.jsx("div", { className: "wf-instance-tab-content", children: m === "timeline" ? /* @__PURE__ */ o.jsx(
        tj,
        {
          activities: r.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: j
        }
      ) : m === "activity" ? /* @__PURE__ */ o.jsx(fj, { context: e, activity: v, activityCatalog: f }) : m === "issues" ? /* @__PURE__ */ o.jsxs(o.Fragment, { children: [
        /* @__PURE__ */ o.jsx(vj, { incidents: r.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ o.jsx(Cj, { details: r, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ o.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ o.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ o.jsx("dd", { children: /* @__PURE__ */ o.jsx(un, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ o.jsx("dd", { children: bd(i.runKind) }),
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
        /* @__PURE__ */ o.jsx("dd", { children: Pe(i.createdAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Pe(i.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Pe(i.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ o.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function dj(e, t) {
  if (!t) return null;
  const n = e.find((r) => r.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((r) => r.executableNodeId === t || r.authoredActivityId === t);
  return i.length > 0 ? ul(i) : null;
}
function fj({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, r = t?.workflowExecutionId ?? null, [s, a] = B({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (G(() => {
    if (!i || !r) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Fp(e, r, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || Tt(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
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
          Tt(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ o.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ o.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ o.jsx("dd", { children: Pe(t.startedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ o.jsx("dd", { children: Pe(t.completedAt) }),
        /* @__PURE__ */ o.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ o.jsx("dd", { children: ps(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ o.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ o.jsx("dd", { children: l }),
        /* @__PURE__ */ o.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ o.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ o.jsx(pj, { state: s })
  ] });
}
function pj({ state: e }) {
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
    /* @__PURE__ */ o.jsx("div", { className: "wf-runtime-input-list", children: t.map((n) => /* @__PURE__ */ o.jsx(hj, { snapshot: n }, `${n.name}:${n.capturedAt}:${n.captureMode}`)) })
  ] });
}
function hj({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.captureMode === "Payload";
  return /* @__PURE__ */ o.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ o.jsxs("header", { children: [
      /* @__PURE__ */ o.jsxs("span", { children: [
        /* @__PURE__ */ o.jsx("strong", { children: e.name }),
        /* @__PURE__ */ o.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ o.jsx("span", { className: "wf-runtime-capture-mode", children: yj(e.captureMode) })
    ] }),
    n ? /* @__PURE__ */ o.jsx(gj, { payload: e.payload }) : /* @__PURE__ */ o.jsx("p", { children: e.captureReason || "The runtime capture policy did not include this input value." }),
    e.isSensitive ? /* @__PURE__ */ o.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
function gj({ payload: e }) {
  const t = xj(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ o.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ o.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ o.jsx("summary", { children: mj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] });
}
function yj(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function mj(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function xj(e) {
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
const wj = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function vj({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  const [i, r] = B("");
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
              value: Ej(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => r(`Copied ${a}`),
              onCopyFailed: (a) => r(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ o.jsx(bj, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ o.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function bj({ incident: e }) {
  const t = Nj(e);
  return t ? /* @__PURE__ */ o.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ o.jsx("summary", { children: Sj(t) }),
    /* @__PURE__ */ o.jsx("pre", { children: t })
  ] }) : null;
}
function Nj(e) {
  const t = jj(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of wj) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function jj(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function Sj(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function Cj({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((r) => {
    const s = ub(r);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ o.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ o.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ o.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((r) => /* @__PURE__ */ o.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ o.jsx("strong", { children: Tt(r.activityType) ?? r.activityType }),
      /* @__PURE__ */ o.jsx("small", { children: r.activityExecutionId })
    ] }, `activity-${r.activityExecutionId}`)) })
  ] });
}
function Ej(e) {
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
    `Created: ${Pe(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${Pe(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function kj({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: r,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = B(Dc);
  G(() => {
    const l = () => c(Dc());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ o.jsx(GN, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: r, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ o.jsx(hr, { title: "Definitions", children: /* @__PURE__ */ o.jsx(ej, { context: e, ai: t, onOpen: u }) });
}
function Ij({ context: e, ai: t }) {
  const [n, i] = B(Tc);
  G(() => {
    const s = () => i(Tc());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const r = oe((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ o.jsx(hr, { title: "Executables", children: /* @__PURE__ */ o.jsx($b, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: r }) });
}
function Aj({ context: e }) {
  return /* @__PURE__ */ o.jsx(hr, { title: "Runs", children: /* @__PURE__ */ o.jsx(oj, { context: e }) });
}
function _j({ context: e, ai: t }) {
  const n = Dj();
  return /* @__PURE__ */ o.jsx(hr, { title: "Run", children: /* @__PURE__ */ o.jsx(sj, { context: e, ai: t, workflowExecutionId: n }) });
}
function hr({ title: e, children: t }) {
  return /* @__PURE__ */ o.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ o.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ o.jsxs("div", { children: [
      /* @__PURE__ */ o.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ o.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Dc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Tc() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Dj() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function Rj(e) {
  hf(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ o.jsx(kj, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ o.jsx(Ij, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ o.jsx(Aj, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ o.jsx(_j, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ o.jsx(uh, { context: e.backend })
      }
    ]
  });
}
export {
  fb as isConnectEndOverExistingWorkflowNode,
  Rj as register,
  pb as resolveConnectEndSource
};
