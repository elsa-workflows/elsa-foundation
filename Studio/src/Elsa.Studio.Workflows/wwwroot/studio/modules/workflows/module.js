import Ke, { useMemo as ie, useState as O, useEffect as Q, memo as ke, forwardRef as El, useRef as ae, useCallback as se, useContext as ii, createContext as cs, useLayoutEffect as qf, lazy as Xf, Suspense as Uf, useReducer as Yf, useId as Al } from "react";
import { Boxes as ri, Zap as Zf, Play as Mt, Terminal as Jf, ListTree as yr, GitBranch as _l, ListChecks as Gf, Save as ls, EyeOff as mo, Shield as xa, AlertTriangle as Bn, SlidersHorizontal as us, Activity as Dl, Search as xr, X as ds, DatabaseZap as wa, ShieldCheck as Qf, Check as Xt, Plus as on, Trash2 as Lt, ChevronRight as pt, AlertCircle as kt, Wrench as ep, Copy as tp, ChevronDown as fs, ScanSearch as $l, Sparkles as rt, RotateCcw as oi, GripVertical as Tl, Maximize2 as sn, ChevronUp as np, Repeat2 as ip, Package as Rl, Undo2 as rp, Redo2 as op, Network as sp, Download as ap, ChevronLeft as zt, Minimize2 as er, Workflow as ps, Code2 as cp, Fingerprint as lp } from "lucide-react";
import { useQuery as wr, useQueryClient as Pl, useMutation as Ml } from "@tanstack/react-query";
import { useTablistKeyboard as up } from "@elsa-workflows/studio-ui";
function dp(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var yo = { exports: {} }, kn = {};
var va;
function fp() {
  if (va) return kn;
  va = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(i, o, s) {
    var a = null;
    if (s !== void 0 && (a = "" + s), o.key !== void 0 && (a = "" + o.key), "key" in o) {
      s = {};
      for (var c in o)
        c !== "key" && (s[c] = o[c]);
    } else s = o;
    return o = s.ref, {
      $$typeof: e,
      type: i,
      key: a,
      ref: o !== void 0 ? o : null,
      props: s
    };
  }
  return kn.Fragment = t, kn.jsx = n, kn.jsxs = n, kn;
}
var ba;
function pp() {
  return ba || (ba = 1, yo.exports = fp()), yo.exports;
}
var r = pp();
let Ll;
function hp(e) {
  Ll = e;
}
function tr() {
  return Ll;
}
const gp = "String", mp = "singleline";
function yp(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function hs(e, t = "Single") {
  return { alias: (e ?? "").trim() || gp, collectionKind: t };
}
function gs(e) {
  const t = e.type ?? e.Type;
  if (Kn(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: yp(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return Kn(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: ja(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: ja(e) ? "Array" : "Single" };
}
function ja(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function Na(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function vr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function xp(e, t) {
  const n = new Set(t);
  let i = 1, o = `${e}${i}`;
  for (; n.has(o); )
    i += 1, o = `${e}${i}`;
  return o;
}
function wp(e) {
  return {
    referenceKey: vr(),
    name: e.name,
    type: hs(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function vp(e, t) {
  return { ...e, ...t };
}
function bp(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function jp(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function Np(e) {
  return {
    referenceKey: vr(),
    name: e.name,
    type: hs(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: mp,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function Sp(e, t) {
  return { ...e, ...t };
}
function Cp(e) {
  return {
    referenceKey: vr(),
    name: e.name,
    type: hs(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function Ip(e, t) {
  return { ...e, ...t };
}
function kp(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function zl(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : kp(e || t);
}
function Ep(e, t) {
  return zl(e, t).replace(/StorageDriver$/, "");
}
function Kn(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function dt(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Ap = ["name", "Name"], ms = ["name", "Name"], _p = ["storageDriverType", "StorageDriverType"], Vl = ["referenceKey", "ReferenceKey"], Dp = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function si(e) {
  return Ol(e, Pp);
}
function br(e) {
  return Ol(e, Mp);
}
function Ol(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Lo(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Bi(e.variables, zo)), Array.isArray(e.inputs) && (n.inputs = Bi(e.inputs, zo)), Array.isArray(e.outputs) && (n.outputs = Bi(e.outputs, (i) => Hl(i, !1))), n;
}
function Lo(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !ot(i.payload)) return n;
  let o = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Ca(c) ? (s[a] = Lo(c, t), o = !0) : Array.isArray(c) && c.length > 0 && c.every(Ca) && (s[a] = c.map((u) => Lo(u, t)), o = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Bi(i.payload.variables, zo), o = !0), o ? { ...n, structure: { ...i, payload: s } } : n;
}
function Bi(e, t) {
  return e.map((n) => ot(n) && !Array.isArray(n) ? t(n) : n);
}
function zo(e) {
  return Hl(e, !0);
}
const $p = [
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
function Hl(e, t) {
  const n = Tp(e, $p);
  return dt(e, Vl).trim() || (n.referenceKey = vr()), n.type = gs(e), t && (n.storageDriverType = Rp(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Tp(e, t) {
  const n = new Set(t), i = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (i[o] = s);
  return i;
}
function Rp(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (ot(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Pp(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    Dp.has(o) || (Op(s) ? t.push({
      referenceKey: Lp(o),
      value: Vp(s.expression)
    }) : n[o] = s);
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
function Mp(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!ot(i) || typeof i.referenceKey != "string") continue;
    const o = ot(i.value) ? i.value : {};
    n[zp(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Lp(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function zp(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Vp(e) {
  const t = e.type || "Literal";
  return t === "Variable" && ot(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && ot(e.value) ? { value: Sa(e.value), expressionType: "Object" } : { value: Sa(e.value), expressionType: t };
}
function Sa(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Op(e) {
  if (!ot(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return ot(t) && typeof t.type == "string";
}
function Ca(e) {
  return ot(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function ot(e) {
  return typeof e == "object" && e !== null;
}
const ai = "elsa.sequence.structure", mn = "elsa.flowchart.structure";
function Wl(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const o of t) {
    const s = Fp(i, o.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Hp(e, t, n) {
  const i = (o, s) => {
    const a = Te(o, n);
    for (const c of a)
      if (c.activities.some((u) => u.nodeId === t)) return { hops: s, targetSlot: c };
    for (const c of a)
      for (const u of c.activities) {
        const l = i(u, [...s, { parent: o, slot: c, child: u }]);
        if (l) return l;
      }
    return null;
  };
  return i(e, []);
}
function Fl(e, t) {
  if (e.cardinality !== "single" || e.activities.length !== 1) return null;
  const n = e.activities[0], i = Te(n, t)[0];
  return i && i.mode !== "generic" ? { child: n, childPrimary: i } : null;
}
function Wp(e, t, n = (o) => o.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = Hp(e, t, i);
  if (!o) return null;
  const { hops: s, targetSlot: a } = o;
  if (s.length === 0)
    return a.id === Te(e, i)[0]?.id ? [] : null;
  const c = [];
  for (let u = 0; u < s.length; ) {
    const l = s[u], d = s[u + 1];
    if (d && Fl(d.slot, i)) {
      const f = s[u + 2]?.slot ?? a;
      c.push({ ownerNodeId: l.child.nodeId, slotId: d.slot.id, label: "" }), c.push({
        ownerNodeId: d.child.nodeId,
        slotId: f.id,
        // Descending lands on the child's primary slot, labeled after the slot that was entered
        // ("For Each / Body"); a non-primary viewed slot is the retarget case, labeled after the
        // container itself.
        label: f.id === Te(d.child, i)[0]?.id ? vt(n(d.parent), d.slot) : vt(n(d.child), f)
      }), u += 2;
    } else {
      const f = d?.slot ?? a;
      c.push({
        ownerNodeId: l.child.nodeId,
        slotId: f.id,
        label: vt(n(l.child), f)
      }), u += 1;
    }
  }
  return c;
}
function On(e, t, n, i, o, s) {
  const a = i.cardinality === "single" && i.activities.length === 1 ? i.activities[0] : null, c = Fl(i, s), u = t?.nodeId === n;
  if (u && e.length === 0)
    return t && Te(t, s)[0]?.id !== i.id ? null : c ? { frames: [{ ownerNodeId: c.child.nodeId, slotId: c.childPrimary.id, label: o }], selectedNodeId: null } : { frames: [], selectedNodeId: a?.nodeId ?? null };
  const l = u ? e.slice(0, -1) : e;
  return c ? {
    frames: [
      ...l,
      { ownerNodeId: n, slotId: i.id, label: "" },
      { ownerNodeId: c.child.nodeId, slotId: c.childPrimary.id, label: o }
    ],
    selectedNodeId: null
  } : {
    frames: [...l, { ownerNodeId: n, slotId: i.id, label: o }],
    selectedNodeId: a?.nodeId ?? null
  };
}
function vt(e, t) {
  return `${e} / ${t.label}`;
}
function Bl(e, t, n) {
  const i = Te(e, n), o = t.at(-1);
  return o ? i.find((s) => s.id === o.slotId) ?? null : i[0] ?? null;
}
function an(e, t, n) {
  const i = Wl(e, t, n);
  if (!i) return null;
  const o = Bl(i, t, n);
  return o ? { owner: i, slot: o } : null;
}
function Kl(e, t, n) {
  for (const i of Te(e, n)) {
    const o = i.activities.find((s) => s.nodeId === t);
    if (o) return { slot: i, child: o };
  }
  return null;
}
function Fp(e, t, n) {
  return Kl(e, t, n)?.child ?? null;
}
function Te(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, o = jr(ql(e, t));
  if (o?.kind === n.kind) return qp(n, o);
  const s = ph(n), a = Dn(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: hh(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => Dn(c) || rr(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: mh(c),
    property: c,
    cardinality: Dn(u) ? "many" : "single",
    mode: "generic",
    activities: Dn(u) ?? (rr(u) ? [u] : [])
  }));
}
function jr(e) {
  for (const t of e?.designFacets ?? []) {
    if (!st(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !st(t.payload)) continue;
    const o = Bp(t.payload);
    if (o) return { kind: n, schemaVersion: i, payload: o };
  }
  return null;
}
function Bp(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !st(e.initialPayload)) return null;
  const n = e.slots.map(Kp).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Kp(e) {
  if (!st(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", o = e.cardinality;
  return !t || !n || !i || o !== "single" && o !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: o,
    collectionProperty: Qt(e.collectionProperty),
    childProperty: Qt(e.childProperty),
    labelProperty: Qt(e.labelProperty),
    slotNameTemplate: Qt(e.slotNameTemplate)
  };
}
function qp(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, o = e.payload[n.collectionProperty];
      return Array.isArray(o) ? o.flatMap((s, a) => {
        if (!st(s)) return [];
        const c = n.labelProperty ? Qt(s[n.labelProperty]) : void 0;
        return [Ia(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [Ia(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Ia(e, t, n, i, o, s) {
  const a = o === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${o}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: o === void 0 ? t.displayName : Up(t, o, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Xp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: o,
    collectionItemLabel: s
  };
}
function Xp(e, t) {
  return t === "many" ? Dn(e) ?? [] : rr(e) ? [e] : [];
}
function Up(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function ql(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Yp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, o) => st(i) && Qt(i[t.labelProperty]) === t.collectionItemLabel ? o : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function ys(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = o.get(a.nodeId) ?? gh(e.slot.mode, c);
    return Ul(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? Zl(e.owner) : sh(e.slot, s)
  };
}
function xs(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Ul(e, i, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Zp(e, t, n, i = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = Aa(t, (c) => c.authoredActivityId || c.executableNodeId), a = Aa(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Gl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
function ws(e, t) {
  if (e?.structure?.kind === mn || nh(t)) return "flowchart";
  if (e?.structure?.kind === ai || ih(t)) return "sequence";
  if (e) {
    const n = Te(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function ka(e, t, n, i) {
  return vs(e, t, i, (o) => {
    const s = Bl(o, t, i);
    return s ? yn(o, s, n) : o;
  });
}
function Jp(e, t, n, i) {
  return t.length === 0 ? n : vs(e, t, i, () => n);
}
function vs(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [o, ...s] = t, a = Kl(e, o.ownerNodeId, n);
  if (!a) return e;
  const c = vs(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === o.ownerNodeId ? c : l);
  return yn(e, a.slot, u);
}
function Vo(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const o = Te(e, i);
  if (o.length === 0) return e;
  let s = !1, a = e;
  for (const c of o) {
    const u = c.activities.map((l) => {
      const d = Vo(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = yn(a, c, u));
  }
  return s ? a : e;
}
function yn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const o = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(o)) return e;
    const s = Yp(o, t);
    if (s < 0) return e;
    const a = o[s];
    if (!st(a)) return e;
    const c = [...o];
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
function Gp(e, t, n, i = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), yn(e.owner, e.slot, s);
}
function Qp(e, t) {
  return {
    ...e,
    structure: oh(e.structure, t)
  };
}
function eh(e, t) {
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
function nr(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: Yl(e)
  };
}
function Xl(e, t) {
  if (!e) return null;
  const n = ql(e, t), i = e.structure ?? (n ? Yl(n) : null);
  let o = i === e.structure ? e : { ...e, structure: i };
  const s = Te(o, t);
  for (const a of s) {
    const c = a.activities.map((u) => Xl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (o = yn(o, a, c));
  }
  return o;
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? rh(t) : n;
}
function Ul(e, t, n, i = {}) {
  return {
    id: e.nodeId,
    type: "workflowActivity",
    position: n,
    connectable: i.connectable,
    deletable: i.deletable,
    draggable: i.draggable,
    data: {
      label: t ? Ie(t) : e.activityVersionId,
      activityVersionId: e.activityVersionId,
      activityTypeKey: t?.activityTypeKey,
      category: t?.category,
      executionType: t?.executionType,
      icon: xn(t),
      childSlots: Te(e, t),
      acceptsInbound: ah(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : Jl(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function xn(e) {
  if (!e) return "activity";
  const t = th(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ie(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function th(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function nh(e) {
  return !!e && (Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function ih(e) {
  return !!e && (Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function rh(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function Yl(e) {
  const t = jr(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: dh(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: ai,
    schemaVersion: "1.0.0",
    payload: { activities: [] }
  } : e.activityTypeKey.endsWith(".Flowchart") || e.displayName === "Flowchart" ? {
    kind: mn,
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
function oh(e, t) {
  if (!e) return e ?? null;
  const n = Array.isArray(e.payload.connections) ? e.payload.connections : [], i = /* @__PURE__ */ new Map();
  for (const o of n) {
    if (!st(o)) continue;
    const s = o.id;
    typeof s == "string" && i.set(s, o);
  }
  return {
    ...e,
    payload: {
      ...e.payload,
      connections: t.map((o) => {
        const s = i.get(o.id) ?? {}, a = o.data?.vertices, { vertices: c, ...u } = s;
        return {
          ...u,
          id: o.id,
          source: { nodeId: o.source, port: o.sourceHandle ?? "Done" },
          target: o.targetHandle ? { nodeId: o.target, port: o.targetHandle } : { nodeId: o.target },
          ...a?.length ? { vertices: a.map((l) => ({ x: Math.round(l.x), y: Math.round(l.y) })) } : {}
        };
      })
    }
  };
}
function sh(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Zl(e) {
  if (e.structure?.kind !== mn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(fh) : [];
    return {
      id: typeof n.id == "string" ? String(n.id) : `flow-${i}-${o.nodeId}-${s.nodeId}`,
      source: o.nodeId,
      target: s.nodeId,
      sourceHandle: o.port,
      targetHandle: s.port && s.port !== "Done" ? s.port : void 0,
      type: "workflow",
      label: o.port && o.port !== "Done" ? o.port : void 0,
      data: a.length ? { vertices: a } : void 0
    };
  }).filter((n) => n !== null) : [];
}
function Jl(e, t) {
  const n = Ea(e.cases);
  if (lh(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Ki(t?.designFacets),
    ...Ki(t?.ports),
    ...Ki(t?.outputs)
  ];
  if (i.length > 0) return uh(i);
  const o = Ea(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function ah(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function ir(e, t, n, i) {
  const o = n ?? "Done";
  return {
    id: `flow-${e}-${t}-${o}-${crypto.randomUUID().slice(0, 8)}`,
    source: e,
    target: t,
    sourceHandle: o,
    targetHandle: i ?? void 0,
    type: "workflow",
    label: o !== "Done" ? o : void 0
  };
}
function ch(e, t, n) {
  const i = ir(t.source, n, t.sourceHandle ?? "Done", void 0), o = ir(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, o);
}
function Dn(e) {
  return Array.isArray(e) ? e.filter(rr) : null;
}
function lh(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Ki(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!st(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Ki(n.ports));
      continue;
    }
    const i = typeof n.type == "string" ? n.type : typeof n.portType == "string" ? n.portType : "", o = n.isBrowsable !== !1 && n.browsable !== !1, s = typeof n.name == "string" ? n.name : typeof n.id == "string" ? n.id : "";
    if (o && i.toLowerCase() === "flow" && s) {
      const a = typeof n.displayName == "string" ? n.displayName : s;
      t.push({ name: s, displayName: a });
    }
  }
  return t;
}
function uh(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function Ea(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Qt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function dh(e) {
  return JSON.parse(JSON.stringify(e));
}
function Aa(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i);
    o && n.set(o, [...n.get(o) ?? [], i]);
  }
  return n;
}
function Gl(e) {
  return [...e].sort((t, n) => _a(n).localeCompare(_a(t)))[0];
}
function _a(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function fh(e) {
  return st(e) && typeof e.x == "number" && typeof e.y == "number";
}
function st(e) {
  return typeof e == "object" && e !== null;
}
function rr(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function ph(e) {
  return e.kind === ai ? "sequence" : e.kind === mn ? "flowchart" : "generic";
}
function hh(e) {
  return e.kind === ai || e.kind === mn, "Activities";
}
function gh(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function mh(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const or = "workflow", yh = /* @__PURE__ */ new Set([ai, mn]);
function xh(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (yh.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? jr(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function Ql(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(Kn) : [];
}
function wh(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function vh(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== or ? t : or
  };
}
function eu(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return eu(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function bh(e, t) {
  if (!e) return "";
  const n = [`workflow:${Da(e.variables)}`], i = (o) => {
    const s = Te(o, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${o.nodeId}:${Da(Ql(o))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function Da(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function jh(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const xe = "/_elsa/workflow-management", Nh = "/publishing", bt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"],
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"]
};
function Sh(e) {
  return wr({
    queryKey: bt.activities,
    queryFn: () => ci(e),
    staleTime: 6e4
  });
}
function Ch(e) {
  return wr({
    queryKey: bt.activityAvailabilitySettings,
    queryFn: () => Zh(e)
  });
}
function Ih(e) {
  return wr({
    queryKey: bt.activityAvailabilityDiagnostics,
    queryFn: () => iu(e)
  });
}
function kh(e) {
  const t = Pl();
  return Ml({
    mutationFn: (n) => Jh(e, n),
    onSuccess: (n) => {
      t.setQueryData(bt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: bt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: bt.activities });
    }
  });
}
function Eh(e) {
  return wr({
    queryKey: bt.runtimeDiagnosticsSettings,
    queryFn: () => Gh(e)
  });
}
function Ah(e) {
  const t = Pl();
  return Ml({
    mutationFn: (n) => Qh(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: bt.runtimeDiagnosticsSettings });
    }
  });
}
async function _h(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${xe}/definitions?${n.toString()}`);
}
async function tu(e, t) {
  const n = await e.http.getJson(`${xe}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: br(n.draft.state) } } : n;
}
async function Dh(e, t, n) {
  const i = await e.http.postJson(
    `${xe}/design/scoped-variables/analyze`,
    { state: si(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const xo = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function $h(e, t, n, i) {
  const o = ie(() => bh(t, i), [i, t]), [s, a] = O(() => xo("loading"));
  return Q(() => {
    if (!t) {
      a(xo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Dh(e, t, n).then(
      (u) => {
        c || a({ ...u, status: "ready" });
      },
      () => {
        c || a(xo("unavailable"));
      }
    ), () => {
      c = !0;
    };
  }, [e, n, o]), s;
}
async function Th(e, t) {
  const n = await e.http.getJson(`${xe}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: br(n.state) };
}
async function Rh(e, t) {
  return e.http.postJson(`${xe}/definitions`, t);
}
async function Ph(e, t) {
  await e.http.deleteJson(`${xe}/definitions/${encodeURIComponent(t)}`);
}
async function Mh(e, t) {
  await e.http.postJson(`${xe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function Lh(e, t) {
  await e.http.deleteJson(`${xe}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function zh(e, t, n) {
  return e.http.requestJson(
    `${xe}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Vh(e, t) {
  const n = await e.http.putJson(
    `${xe}/drafts/${encodeURIComponent(t.id)}`,
    { state: si(t.state), layout: t.layout }
  );
  return { ...n, state: br(n.state) };
}
async function Oh(e, t) {
  return e.http.postJson(`${xe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Hh(e, t) {
  return e.http.postJson(`${xe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Wh(e, t) {
  const n = { ...t, state: si(t.state) };
  try {
    return await e.http.postJson(`${Nh}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const o = sg(i);
    if (o) return o;
    throw i;
  }
}
async function bs(e, t) {
  return e.http.postJson(`${xe}/executables/${encodeURIComponent(t)}/run`, {});
}
function Fh(e) {
  const t = new URLSearchParams();
  e.scope && e.scope !== "published" && t.set("scope", e.scope), e.includeRetired && t.set("includeRetired", "true");
  const n = t.toString();
  return n ? `?${n}` : "";
}
async function Bh(e, t, n) {
  const i = n ? `?ref=${encodeURIComponent(n)}` : "";
  return e.http.getJson(`${xe}/executables/${encodeURIComponent(t)}${i}`);
}
async function nu(e, t, n) {
  const i = n ? `?definitionId=${encodeURIComponent(n)}` : "";
  await e.http.deleteJson(`${xe}/executables/${encodeURIComponent(t)}${i}`);
}
async function Kh(e, t, n) {
  await e.http.postJson(`${xe}/executables/${encodeURIComponent(t)}/restore`, {});
}
async function js(e, t = {}) {
  const n = Fh(t), i = [`${xe}/executables${n}`, `/_demo/workflows/executables${n}`], o = [];
  for (const s of i)
    try {
      const a = await e.http.getJson(s);
      return qh(a);
    } catch (a) {
      o.push(a);
    }
  if (o.length > 0 && o.every($a)) return [];
  throw o.find((s) => !$a(s)) ?? o[o.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function qh(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function $a(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Xh(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Uh(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Yh(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function ci(e) {
  return e.http.getJson(`${xe}/activities`);
}
async function Zh(e) {
  return e.http.getJson(`${xe}/activities/availability/settings`);
}
async function Jh(e, t) {
  return e.http.putJson(`${xe}/activities/availability/settings`, t);
}
async function iu(e) {
  return e.http.getJson(`${xe}/activities/availability/diagnostics`);
}
async function Gh(e) {
  return e.http.getJson(`${xe}/runtime-diagnostics/settings`);
}
async function Qh(e, t) {
  return e.http.putJson(`${xe}/runtime-diagnostics/settings`, t);
}
async function eg(e) {
  const t = await Nr(e, [
    `${xe}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? Ta(t) : Ta(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function tg(e) {
  const t = await Nr(e, [
    `${xe}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : qi;
}
async function ng(e) {
  const t = await Nr(e, [
    `${xe}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => ig(i));
}
function ig(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function rg(e) {
  const t = await Nr(e, [
    `${xe}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => og(i));
}
function og(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function Nr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (o) {
      n = o;
    }
  throw n;
}
function Ta(e) {
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
function sg(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ra(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ra(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ra(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const qi = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function ru(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i)?.trim() || "Uncategorized", s = n.get(o);
    s ? s.push(i) : n.set(o, [i]);
  }
  return [...n.entries()].map(([i, o]) => ({ category: i, items: o })).sort((i, o) => i.category.localeCompare(o.category));
}
const ag = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], cg = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, ou = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, lg = Object.keys(ou);
function ug(e) {
  const t = typeof e == "number" ? lg[e] : e;
  return t && ou[t] || t?.toString() || "";
}
function Vt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? ag[e] ?? "Available" : "Available";
}
function qn(e) {
  const t = Vt(e);
  return cg[t] ?? t;
}
function su(e) {
  return Vt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function au(e) {
  return Vt(e) !== "Available";
}
function cu(e) {
  return Vt(e.state) === "BlockedByHostBaseline";
}
function dg(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function fg(e) {
  return e === "Only" ? 1 : 0;
}
function Oo(e) {
  const t = e?.rules;
  return {
    mode: dg(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function pg(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function $n(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function hg(e) {
  return [...e?.items ?? []].filter(pg).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Xn(t).localeCompare(Xn(n)));
}
function gg(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Vt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function mg(e) {
  return ru(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function yg(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function wo(e, t, n, i) {
  const o = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === o ? e : { ...e, [t]: vg(e[t], n) };
}
function xg(e, t, n) {
  const i = new Set(n.activityTypes), o = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (o.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = $n(c);
    if (cu(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function wg(e, t) {
  const n = Oo(t), i = (o) => [...o].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function vg(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Xn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = jg(e?.activityTypeKey);
  return bg(n) || t || e?.activityTypeKey || "Activity";
}
function Pa(e) {
  const t = lu(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function Ma(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !au(e.state) ? "" : n;
}
function bg(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function jg(e) {
  const t = lu(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function lu(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function La(e) {
  return (e ?? []).flatMap((t) => {
    if (!Kn(t)) return [];
    const n = (dt(t, ["displayName", "DisplayName"]) || dt(t, ms)).trim();
    if (!n) return [];
    const i = (gs(t).alias || dt(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: dt(t, ["description", "Description"]).trim() }];
  });
}
function Ng(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => au(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
const Sg = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function uu(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function Ns(e) {
  return uu(e.name);
}
function Cg(e, t) {
  const n = Ns(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : fu(i, t);
}
function du(e, t) {
  return fu(e[Ns(t)], t);
}
function Ig(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function kg(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function za(e, t, n) {
  return {
    ...e,
    [Ns(t)]: n
  };
}
function Eg(e, t) {
  return t.isWrapped === !1 ? Cg(e, t) : du(e, t).expression.value;
}
function fu(e, t) {
  return Mg(e) ? {
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
const Ag = /* @__PURE__ */ new Set([
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
function _g(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const o = t.slice(0, i), s = (o.split(".").pop() ?? o).toLowerCase();
  return Ag.has(s) ? { elementTypeName: Dg(t.slice(i)) } : null;
}
function Dg(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function $g(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Tg(e) {
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
function Rg(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function Pg(e, t) {
  return {
    ...e,
    typeName: t ?? "System.String",
    isWrapped: !1,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}
function vo(e, t, n) {
  if (t === n || t < 0 || n < 0 || t >= e.length || n >= e.length) return e;
  const i = [...e], [o] = i.splice(t, 1);
  return i.splice(n, 0, o), i;
}
function Mg(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Ss(e) {
  return Cs(e?.trim() ?? "") || e;
}
function Cs(e) {
  if (!e) return "";
  const t = Lg(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${Cs(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const o = Va(t.slice(0, i)), s = zg(t.slice(i));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return Va(t);
}
function Lg(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function Va(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function zg(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Oa(e, t);
  return n == null ? [] : Vg(n).map((i) => {
    const o = i.trim(), s = o.startsWith("[") ? Oa(o, 0) ?? o : o;
    return Cs(s);
  }).filter(Boolean);
}
function Oa(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Vg(e) {
  const t = [];
  let n = 0, i = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, o)), i = o + 1);
  }
  return t.push(e.slice(i)), t.map((o) => o.trim()).filter(Boolean);
}
function $e(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function Is(e, t) {
  if (!e || !t) return "";
  const n = Date.parse(e), i = Date.parse(t);
  if (Number.isNaN(n) || Number.isNaN(i) || i < n) return "";
  const o = Math.round((i - n) / 1e3);
  if (o < 60) return `${o}s`;
  const s = Math.floor(o / 60), a = o % 60;
  if (s < 60) return a ? `${s}m ${a}s` : `${s}m`;
  const c = Math.floor(s / 60), u = s % 60;
  return u ? `${c}h ${u}m` : `${c}h`;
}
function Ot(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function Sr(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(_l, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(yr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(Jf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Mt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(Zf, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(ri, { size: 15 });
  }
}
function Ha(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Og(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), o = n(t), s = Math.max(i.length, o.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (o[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Hg(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function pu({ icon: e }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: Sr(e) });
}
function Wg({ context: e }) {
  const t = Ch(e), n = Ih(e), i = Sh(e), o = kh(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = o.isPending, [l, d] = O(() => Oo(s)), [f, p] = O(""), [h, m] = O(!1), [v, x] = O(null), [y, j] = O(null);
  Q(() => {
    d(Oo(s));
  }, [s]);
  const g = ie(() => hg(a), [a]), w = ie(() => gg(a), [a]), S = ie(() => a?.sets ?? [], [a]), b = ie(
    () => xg(g, S, l),
    [g, S, l]
  ), I = (H) => b.get($n(H)) ?? { enabled: !0, lockedBy: null }, A = ie(() => {
    const H = /* @__PURE__ */ new Map();
    for (const U of i.data?.activities ?? []) {
      if (!U.activityTypeKey) continue;
      const G = H.get(U.activityTypeKey);
      (!G || Og(U.version, G.version) > 0) && H.set(U.activityTypeKey, U);
    }
    return H;
  }, [i.data]), D = (H) => A.get(H.activityTypeKey ?? ""), M = ie(() => {
    const H = f.trim().toLowerCase(), U = h ? g.filter((G) => !(b.get($n(G))?.enabled ?? !0)) : g;
    return H ? U.filter((G) => [
      Xn(G),
      Pa(G),
      Ma(G),
      G.activityTypeKey,
      G.category
    ].some((X) => (X ?? "").toLowerCase().includes(H))) : U;
  }, [g, f, h, b]), k = ie(() => mg(M), [M]), R = y ? g.find((H) => Ha(H) === y) ?? null : null, L = g.filter(cu).length, C = g.filter((H) => Vt(H.state) === "HiddenByManagementSettings").length, _ = ie(() => wg(l, s), [l, s]), N = o.error ?? t.error ?? n.error, E = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, T = (H) => {
    x(null), d(H);
  }, $ = (H) => T((U) => ({ ...U, mode: H })), z = (H, U) => T((G) => wo(G, "activityTypes", H, U)), F = (H, U) => T((G) => H.reduce((X, P) => wo(X, "activityTypes", P, U), G)), B = (H, U) => T((G) => wo(G, "sets", H, U)), Z = () => {
    x(null), o.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: fg(l.mode),
        rules: { activityTypes: l.activityTypes, sets: l.sets }
      },
      { onSuccess: () => x("Activity availability saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page availability-page", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(Gf, { size: 18 }),
          " Activity availability"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control which activities can be added to new workflows. Host baseline rules always take precedence." })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-actions", children: [
        _ && !u && /* @__PURE__ */ r.jsx("span", { className: "wf-chip availability-dirty", children: "Unsaved changes" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: "availability-save", onClick: Z, disabled: c || u || !_, children: [
          /* @__PURE__ */ r.jsx(ls, { size: 15 }),
          u ? "Saving…" : "Save"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-body", children: [
      E && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-error", children: E }),
      v && !E && /* @__PURE__ */ r.jsx("div", { className: "availability-banner availability-banner-success", children: v }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-mode", role: "group", "aria-label": "Activity availability mode", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: l.mode === "AllExcept" ? "active" : "", onClick: () => $("AllExcept"), disabled: c || u, children: [
          /* @__PURE__ */ r.jsx(mo, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "All except" }),
            /* @__PURE__ */ r.jsx("em", { children: "Activities are available unless you turn them off" })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", className: l.mode === "Only" ? "active" : "", onClick: () => $("Only"), disabled: c || u, children: [
          /* @__PURE__ */ r.jsx(xa, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ r.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(xa, { size: 14 }),
          " ",
          L,
          " host blocked"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(mo, { size: 14 }),
          " ",
          C,
          " management hidden"
        ] }),
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(Bn, { size: 14 }),
          " ",
          w.length,
          " unresolved"
        ] })
      ] }),
      S.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(us, { size: 14 }),
          " Sets"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-set-list", children: S.map((H) => /* @__PURE__ */ r.jsxs("label", { className: "availability-set-option", children: [
          /* @__PURE__ */ r.jsx(
            "input",
            {
              type: "checkbox",
              role: "switch",
              className: "wf-switch-input",
              "aria-label": `Activities in the ${H.name} set available in new workflows`,
              checked: yg(l, "sets", H.name),
              disabled: c || u,
              onChange: (U) => B(H.name, U.target.checked)
            }
          ),
          /* @__PURE__ */ r.jsx("span", { children: H.name }),
          /* @__PURE__ */ r.jsx("code", { children: (H.activityTypeKeys ?? []).length })
        ] }, H.name)) })
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "availability-section availability-section-grow", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "availability-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Dl, { size: 14 }),
            " Activities"
          ] }),
          /* @__PURE__ */ r.jsxs("div", { className: "availability-section-tools", children: [
            /* @__PURE__ */ r.jsxs(
              "button",
              {
                type: "button",
                className: `availability-filter-toggle${h ? " active" : ""}`,
                "aria-pressed": h,
                title: "Show only activities that are turned off or blocked",
                onClick: () => m((H) => !H),
                children: [
                  /* @__PURE__ */ r.jsx(mo, { size: 13 }),
                  "Hidden only"
                ]
              }
            ),
            /* @__PURE__ */ r.jsxs("div", { className: "wf-search availability-search", children: [
              /* @__PURE__ */ r.jsx(xr, { size: 14 }),
              /* @__PURE__ */ r.jsx("input", { type: "search", value: f, placeholder: "Filter activities…", onChange: (H) => p(H.target.value) })
            ] })
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-browser", children: [
          /* @__PURE__ */ r.jsxs("div", { className: "availability-activity-list", children: [
            c && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading availability…" }),
            !c && g.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No availability diagnostics reported." }),
            !c && g.length > 0 && M.length === 0 && /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: h && !f.trim() ? "No hidden activities — everything is turned on." : "No activities match the filter." }),
            k.map((H) => {
              const U = H.entries.filter((q) => I(q).lockedBy === null), G = U.filter((q) => I(q).enabled).length, X = U.length > 0 && G === U.length, P = G > 0 && !X;
              return /* @__PURE__ */ r.jsxs("div", { className: "availability-group", children: [
                /* @__PURE__ */ r.jsxs("div", { className: "availability-group-header", children: [
                  /* @__PURE__ */ r.jsxs("span", { className: "availability-group-title", children: [
                    H.category,
                    /* @__PURE__ */ r.jsx("small", { children: H.entries.length })
                  ] }),
                  U.length > 0 && /* @__PURE__ */ r.jsx(
                    "input",
                    {
                      type: "checkbox",
                      role: "switch",
                      className: "wf-switch-input",
                      "aria-label": `Toggle the ${U.length} listed ${H.category} activities`,
                      checked: X,
                      ref: (q) => {
                        q && (q.indeterminate = P);
                      },
                      disabled: c || u,
                      onChange: () => F(U.map($n), !X)
                    }
                  )
                ] }),
                H.entries.map((q) => {
                  const de = $n(q), le = Ha(q), te = Vt(q.state), re = I(q), fe = re.lockedBy !== null, W = xn(D(q)), ne = Xn(q), ge = Pa(q), ye = Ma(q), Ee = y === le;
                  return /* @__PURE__ */ r.jsxs("div", { className: `availability-activity-row${Ee ? " selected" : ""}${fe ? " disabled" : ""}`, children: [
                    /* @__PURE__ */ r.jsxs(
                      "button",
                      {
                        type: "button",
                        className: "availability-activity-open",
                        "aria-expanded": Ee,
                        title: "Show activity details",
                        onClick: () => j(Ee ? null : le),
                        children: [
                          /* @__PURE__ */ r.jsx(pu, { icon: W }),
                          /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-main", children: [
                            /* @__PURE__ */ r.jsxs("span", { className: "availability-activity-title-line", children: [
                              /* @__PURE__ */ r.jsx("strong", { children: ne }),
                              ge && /* @__PURE__ */ r.jsx("code", { title: q.activityTypeKey ?? void 0, children: ge })
                            ] }),
                            ye && /* @__PURE__ */ r.jsx("span", { className: "availability-activity-description", title: ye, children: ye })
                          ] })
                        ]
                      }
                    ),
                    te !== "Available" && /* @__PURE__ */ r.jsx("em", { className: `availability-state ${su(q.state)}`, children: qn(q.state) }),
                    /* @__PURE__ */ r.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${ne} available in new workflows`,
                        title: Hg(re),
                        checked: re.enabled,
                        disabled: c || u || fe,
                        onChange: (Re) => z(de, Re.target.checked)
                      }
                    )
                  ] }, le);
                })
              ] }, H.category);
            })
          ] }),
          R && /* @__PURE__ */ r.jsx(
            Fg,
            {
              entry: R,
              catalogItem: D(R),
              onClose: () => j(null)
            }
          )
        ] })
      ] }),
      w.length > 0 && /* @__PURE__ */ r.jsxs("section", { className: "availability-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Bn, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: w.map((H) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: H.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: qn(H.state) })
        ] }, `${H.layer}-${H.referenceKind}-${H.referenceName}`)) })
      ] })
    ] })
  ] });
}
function Fg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Xn(e), o = e.description?.trim() || t?.description?.trim(), s = ie(() => La(t?.inputs), [t]), a = ie(() => La(t?.outputs), [t]), c = ie(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", ug(e.layer)]
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ r.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ r.jsx(pu, { icon: xn(t) }),
      /* @__PURE__ */ r.jsx("h4", { children: i }),
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ r.jsx(ds, { size: 14 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ r.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ r.jsx("em", { className: `availability-state ${su(e.state)}`, children: qn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ r.jsx("span", { children: e.reason })
      ] }),
      o && /* @__PURE__ */ r.jsx("p", { className: "availability-details-description", children: o }),
      /* @__PURE__ */ r.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
        /* @__PURE__ */ r.jsx("dt", { children: l }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ r.jsx(Wa, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ r.jsx(Wa, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ r.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ r.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ r.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ r.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function Wa({ title: e, items: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ r.jsx("h5", { children: e }),
    /* @__PURE__ */ r.jsx("ul", { children: t.map((n) => /* @__PURE__ */ r.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ r.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ r.jsx("code", { children: Ss(n.typeName) })
    ] }, n.name)) })
  ] });
}
const hu = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"], Fa = new Map(hu.map((e, t) => [e, t])), Bg = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];
function Kg({ context: e }) {
  const t = Eh(e), n = Ah(e), i = t.data ?? null, o = t.isLoading, s = n.isPending, a = i?.permissions.canManage ?? !1, [c, u] = O(() => Ba(i)), [l, d] = O(null);
  Q(() => {
    u(Ba(i));
  }, [i]);
  const f = n.error ?? t.error, p = f instanceof Error ? f.message : f ? "Runtime diagnostics settings could not be loaded." : null, h = i?.hostPolicy.limitationReasons ?? [], m = i?.effective.limitationReasons ?? [], v = ie(() => hu.filter((g) => qg(g, i)), [i]), x = (g) => u((w) => ({ ...w, defaultLevel: g })), y = (g, w) => u((S) => {
    const b = { ...S.subjectOverrides };
    return w === "inherit" ? delete b[g] : b[g] = w, { ...S, subjectOverrides: b };
  }), j = () => {
    d(null), n.mutate(
      {
        scope: i?.requested.scope ?? "host-default",
        defaultLevel: c.defaultLevel,
        subjectOverrides: c.subjectOverrides
      },
      { onSuccess: () => d("Runtime diagnostics saved.") }
    );
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page runtime-diagnostics-page", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-header", children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-kicker", children: "Workflows" }),
        /* @__PURE__ */ r.jsxs("h2", { children: [
          /* @__PURE__ */ r.jsx(wa, { size: 18 }),
          " Runtime diagnostics"
        ] }),
        /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Control future runtime value evidence capture. Existing workflow runs keep their captured evidence." })
      ] }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: j, disabled: o || s || !a, children: [
        /* @__PURE__ */ r.jsx(ls, { size: 15 }),
        s ? "Saving..." : "Save"
      ] }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "runtime-diagnostics-body", children: [
      p && /* @__PURE__ */ r.jsx("div", { className: "runtime-diagnostics-banner error", children: p }),
      l && !p && /* @__PURE__ */ r.jsx("div", { className: "runtime-diagnostics-banner success", children: l }),
      /* @__PURE__ */ r.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "runtime-diagnostics-section-head", children: [
          /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
            /* @__PURE__ */ r.jsx(Qf, { size: 14 }),
            " Default capture"
          ] }),
          i && /* @__PURE__ */ r.jsxs("span", { className: "runtime-diagnostics-effective", children: [
            "Effective: ",
            Tn(i.effective.defaultLevel)
          ] })
        ] }),
        /* @__PURE__ */ r.jsx(
          Ug,
          {
            value: c.defaultLevel,
            allowedLevels: v,
            disabled: o || s || !a,
            onChange: x
          }
        )
      ] }),
      /* @__PURE__ */ r.jsxs("section", { className: "runtime-diagnostics-section runtime-diagnostics-section-grow", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(wa, { size: 14 }),
          " Subject overrides"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "runtime-diagnostics-subjects", children: Bg.map((g) => {
          const w = c.subjectOverrides[g.id] ?? "inherit", S = i ? Xg(i, g.id) : null;
          return /* @__PURE__ */ r.jsxs("label", { className: "runtime-diagnostics-subject", children: [
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: g.label }),
              /* @__PURE__ */ r.jsx("em", { children: g.detail })
            ] }),
            /* @__PURE__ */ r.jsxs("span", { className: "runtime-diagnostics-subject-controls", children: [
              S && /* @__PURE__ */ r.jsxs("code", { children: [
                "Effective: ",
                Tn(S)
              ] }),
              /* @__PURE__ */ r.jsxs(
                "select",
                {
                  value: w,
                  disabled: o || s || !a,
                  onChange: (b) => y(g.id, b.target.value),
                  children: [
                    /* @__PURE__ */ r.jsx("option", { value: "inherit", children: "Inherit default" }),
                    v.map((b) => /* @__PURE__ */ r.jsx("option", { value: b, children: Tn(b) }, b))
                  ]
                }
              )
            ] })
          ] }, g.id);
        }) })
      ] }),
      (h.length > 0 || m.length > 0 || !a) && /* @__PURE__ */ r.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Bn, { size: 14 }),
          " Policy"
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "runtime-diagnostics-policy", children: [
          !a && /* @__PURE__ */ r.jsx("span", { children: "Current user can view settings but cannot change them." }),
          i && /* @__PURE__ */ r.jsxs("span", { children: [
            "Host maximum: ",
            Tn(i.hostPolicy.maximumLevel)
          ] }),
          m.map((g) => /* @__PURE__ */ r.jsx("span", { children: g }, `effective-${g}`)),
          h.map((g) => /* @__PURE__ */ r.jsx("span", { children: g }, `host-${g}`))
        ] })
      ] })
    ] })
  ] });
}
function Ba(e) {
  return {
    defaultLevel: e?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...e?.requested.subjectOverrides ?? {} }
  };
}
function qg(e, t) {
  return t ? e === "Payload" && !t.permissions.canEnableFullPayloads ? !1 : (Fa.get(e) ?? 0) <= (Fa.get(t.hostPolicy.maximumLevel) ?? 0) : e !== "Payload";
}
function Xg(e, t) {
  return e.effective.subjectOverrides?.[t] ?? e.effective.defaultLevel;
}
function Tn(e) {
  return e === "DiagnosticSnapshot" ? "Diagnostic snapshot" : e;
}
function Ug({
  value: e,
  allowedLevels: t,
  disabled: n,
  onChange: i
}) {
  return /* @__PURE__ */ r.jsx("div", { className: "runtime-diagnostics-levels", role: "group", "aria-label": "Default runtime diagnostics level", children: t.map((o) => /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: e === o ? "active" : "",
      disabled: n,
      onClick: () => i(o),
      children: [
        /* @__PURE__ */ r.jsx("strong", { children: Tn(o) }),
        /* @__PURE__ */ r.jsx("em", { children: Yg(o) })
      ]
    },
    o
  )) });
}
function Yg(e) {
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
function Ae(e) {
  if (typeof e == "string" || typeof e == "number") return "" + e;
  let t = "";
  if (Array.isArray(e))
    for (let n = 0, i; n < e.length; n++)
      (i = Ae(e[n])) !== "" && (t += (t && " ") + i);
  else
    for (let n in e)
      e[n] && (t += (t && " ") + n);
  return t;
}
var Zg = { value: () => {
} };
function Cr() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Xi(n);
}
function Xi(e) {
  this._ = e;
}
function Jg(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", o = n.indexOf(".");
    if (o >= 0 && (i = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Xi.prototype = Cr.prototype = {
  constructor: Xi,
  on: function(e, t) {
    var n = this._, i = Jg(e + "", n), o, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = i[s]).type) && (o = Gg(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = i[s]).type) n[o] = Ka(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = Ka(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Xi(e);
  },
  call: function(e, t) {
    if ((o = arguments.length - 2) > 0) for (var n = new Array(o), i = 0, o, s; i < o; ++i) n[i] = arguments[i + 2];
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (s = this._[e], i = 0, o = s.length; i < o; ++i) s[i].value.apply(t, n);
  },
  apply: function(e, t, n) {
    if (!this._.hasOwnProperty(e)) throw new Error("unknown type: " + e);
    for (var i = this._[e], o = 0, s = i.length; o < s; ++o) i[o].value.apply(t, n);
  }
};
function Gg(e, t) {
  for (var n = 0, i = e.length, o; n < i; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function Ka(e, t, n) {
  for (var i = 0, o = e.length; i < o; ++i)
    if (e[i].name === t) {
      e[i] = Zg, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Ho = "http://www.w3.org/1999/xhtml";
const qa = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Ho,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function Ir(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), qa.hasOwnProperty(t) ? { space: qa[t], local: e } : e;
}
function Qg(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Ho && t.documentElement.namespaceURI === Ho ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function em(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function gu(e) {
  var t = Ir(e);
  return (t.local ? em : Qg)(t);
}
function tm() {
}
function ks(e) {
  return e == null ? tm : function() {
    return this.querySelector(e);
  };
}
function nm(e) {
  typeof e != "function" && (e = ks(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new qe(i, this._parents);
}
function im(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function rm() {
  return [];
}
function mu(e) {
  return e == null ? rm : function() {
    return this.querySelectorAll(e);
  };
}
function om(e) {
  return function() {
    return im(e.apply(this, arguments));
  };
}
function sm(e) {
  typeof e == "function" ? e = om(e) : e = mu(e);
  for (var t = this._groups, n = t.length, i = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new qe(i, o);
}
function yu(e) {
  return function() {
    return this.matches(e);
  };
}
function xu(e) {
  return function(t) {
    return t.matches(e);
  };
}
var am = Array.prototype.find;
function cm(e) {
  return function() {
    return am.call(this.children, e);
  };
}
function lm() {
  return this.firstElementChild;
}
function um(e) {
  return this.select(e == null ? lm : cm(typeof e == "function" ? e : xu(e)));
}
var dm = Array.prototype.filter;
function fm() {
  return Array.from(this.children);
}
function pm(e) {
  return function() {
    return dm.call(this.children, e);
  };
}
function hm(e) {
  return this.selectAll(e == null ? fm : pm(typeof e == "function" ? e : xu(e)));
}
function gm(e) {
  typeof e != "function" && (e = yu(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new qe(i, this._parents);
}
function wu(e) {
  return new Array(e.length);
}
function mm() {
  return new qe(this._enter || this._groups.map(wu), this._parents);
}
function sr(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
sr.prototype = {
  constructor: sr,
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
function ym(e) {
  return function() {
    return e;
  };
}
function xm(e, t, n, i, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new sr(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function wm(e, t, n, i, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new sr(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (o[c] = u);
}
function vm(e) {
  return e.__data__;
}
function bm(e, t) {
  if (!arguments.length) return Array.from(this, vm);
  var n = t ? wm : xm, i = this._parents, o = this._groups;
  typeof e != "function" && (e = ym(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = o[l], p = f.length, h = jm(e.call(d, d && d.__data__, l, i)), m = h.length, v = c[l] = new Array(m), x = a[l] = new Array(m), y = u[l] = new Array(p);
    n(d, f, v, x, y, h, t);
    for (var j = 0, g = 0, w, S; j < m; ++j)
      if (w = v[j]) {
        for (j >= g && (g = j + 1); !(S = x[g]) && ++g < m; ) ;
        w._next = S || null;
      }
  }
  return a = new qe(a, i), a._enter = c, a._exit = u, a;
}
function jm(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function Nm() {
  return new qe(this._exit || this._groups.map(wu), this._parents);
}
function Sm(e, t, n) {
  var i = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), i && o ? i.merge(o).order() : o;
}
function Cm(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, o = n.length, s = i.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, m = 0; m < f; ++m)
      (h = l[m] || d[m]) && (p[m] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new qe(c, this._parents);
}
function Im() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], o = i.length - 1, s = i[o], a; --o >= 0; )
      (a = i[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function km(e) {
  e || (e = Em);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, o = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new qe(o, this._parents).order();
}
function Em(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Am() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function _m() {
  return Array.from(this);
}
function Dm() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length; o < s; ++o) {
      var a = i[o];
      if (a) return a;
    }
  return null;
}
function $m() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Tm() {
  return !this.node();
}
function Rm(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function Pm(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Mm(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Lm(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function zm(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Vm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Om(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Hm(e, t) {
  var n = Ir(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Mm : Pm : typeof t == "function" ? n.local ? Om : Vm : n.local ? zm : Lm)(n, t));
}
function vu(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Wm(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Fm(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Bm(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Km(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Wm : typeof t == "function" ? Bm : Fm)(e, t, n ?? "")) : cn(this.node(), e);
}
function cn(e, t) {
  return e.style.getPropertyValue(t) || vu(e).getComputedStyle(e, null).getPropertyValue(t);
}
function qm(e) {
  return function() {
    delete this[e];
  };
}
function Xm(e, t) {
  return function() {
    this[e] = t;
  };
}
function Um(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Ym(e, t) {
  return arguments.length > 1 ? this.each((t == null ? qm : typeof t == "function" ? Um : Xm)(e, t)) : this.node()[e];
}
function bu(e) {
  return e.trim().split(/^|\s+/);
}
function Es(e) {
  return e.classList || new ju(e);
}
function ju(e) {
  this._node = e, this._names = bu(e.getAttribute("class") || "");
}
ju.prototype = {
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
function Nu(e, t) {
  for (var n = Es(e), i = -1, o = t.length; ++i < o; ) n.add(t[i]);
}
function Su(e, t) {
  for (var n = Es(e), i = -1, o = t.length; ++i < o; ) n.remove(t[i]);
}
function Zm(e) {
  return function() {
    Nu(this, e);
  };
}
function Jm(e) {
  return function() {
    Su(this, e);
  };
}
function Gm(e, t) {
  return function() {
    (t.apply(this, arguments) ? Nu : Su)(this, e);
  };
}
function Qm(e, t) {
  var n = bu(e + "");
  if (arguments.length < 2) {
    for (var i = Es(this.node()), o = -1, s = n.length; ++o < s; ) if (!i.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Gm : t ? Zm : Jm)(n, t));
}
function ey() {
  this.textContent = "";
}
function ty(e) {
  return function() {
    this.textContent = e;
  };
}
function ny(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function iy(e) {
  return arguments.length ? this.each(e == null ? ey : (typeof e == "function" ? ny : ty)(e)) : this.node().textContent;
}
function ry() {
  this.innerHTML = "";
}
function oy(e) {
  return function() {
    this.innerHTML = e;
  };
}
function sy(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function ay(e) {
  return arguments.length ? this.each(e == null ? ry : (typeof e == "function" ? sy : oy)(e)) : this.node().innerHTML;
}
function cy() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function ly() {
  return this.each(cy);
}
function uy() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function dy() {
  return this.each(uy);
}
function fy(e) {
  var t = typeof e == "function" ? e : gu(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function py() {
  return null;
}
function hy(e, t) {
  var n = typeof e == "function" ? e : gu(e), i = t == null ? py : typeof t == "function" ? t : ks(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function gy() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function my() {
  return this.each(gy);
}
function yy() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function xy() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function wy(e) {
  return this.select(e ? xy : yy);
}
function vy(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function by(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function jy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function Ny(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function Sy(e, t, n) {
  return function() {
    var i = this.__on, o, s = by(t);
    if (i) {
      for (var a = 0, c = i.length; a < c; ++a)
        if ((o = i[a]).type === e.type && o.name === e.name) {
          this.removeEventListener(o.type, o.listener, o.options), this.addEventListener(o.type, o.listener = s, o.options = n), o.value = t;
          return;
        }
    }
    this.addEventListener(e.type, s, n), o = { type: e.type, name: e.name, value: t, listener: s, options: n }, i ? i.push(o) : this.__on = [o];
  };
}
function Cy(e, t, n) {
  var i = jy(e + ""), o, s = i.length, a;
  if (arguments.length < 2) {
    var c = this.node().__on;
    if (c) {
      for (var u = 0, l = c.length, d; u < l; ++u)
        for (o = 0, d = c[u]; o < s; ++o)
          if ((a = i[o]).type === d.type && a.name === d.name)
            return d.value;
    }
    return;
  }
  for (c = t ? Sy : Ny, o = 0; o < s; ++o) this.each(c(i[o], t, n));
  return this;
}
function Cu(e, t, n) {
  var i = vu(e), o = i.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = i.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function Iy(e, t) {
  return function() {
    return Cu(this, e, t);
  };
}
function ky(e, t) {
  return function() {
    return Cu(this, e, t.apply(this, arguments));
  };
}
function Ey(e, t) {
  return this.each((typeof t == "function" ? ky : Iy)(e, t));
}
function* Ay() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length, a; o < s; ++o)
      (a = i[o]) && (yield a);
}
var Iu = [null];
function qe(e, t) {
  this._groups = e, this._parents = t;
}
function li() {
  return new qe([[document.documentElement]], Iu);
}
function _y() {
  return this;
}
qe.prototype = li.prototype = {
  constructor: qe,
  select: nm,
  selectAll: sm,
  selectChild: um,
  selectChildren: hm,
  filter: gm,
  data: bm,
  enter: mm,
  exit: Nm,
  join: Sm,
  merge: Cm,
  selection: _y,
  order: Im,
  sort: km,
  call: Am,
  nodes: _m,
  node: Dm,
  size: $m,
  empty: Tm,
  each: Rm,
  attr: Hm,
  style: Km,
  property: Ym,
  classed: Qm,
  text: iy,
  html: ay,
  raise: ly,
  lower: dy,
  append: fy,
  insert: hy,
  remove: my,
  clone: wy,
  datum: vy,
  on: Cy,
  dispatch: Ey,
  [Symbol.iterator]: Ay
};
function Fe(e) {
  return typeof e == "string" ? new qe([[document.querySelector(e)]], [document.documentElement]) : new qe([[e]], Iu);
}
function Dy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Je(e, t) {
  if (e = Dy(e), t === void 0 && (t = e.currentTarget), t) {
    var n = t.ownerSVGElement || t;
    if (n.createSVGPoint) {
      var i = n.createSVGPoint();
      return i.x = e.clientX, i.y = e.clientY, i = i.matrixTransform(t.getScreenCTM().inverse()), [i.x, i.y];
    }
    if (t.getBoundingClientRect) {
      var o = t.getBoundingClientRect();
      return [e.clientX - o.left - t.clientLeft, e.clientY - o.top - t.clientTop];
    }
  }
  return [e.pageX, e.pageY];
}
const $y = { passive: !1 }, Un = { capture: !0, passive: !1 };
function bo(e) {
  e.stopImmediatePropagation();
}
function nn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ku(e) {
  var t = e.document.documentElement, n = Fe(e).on("dragstart.drag", nn, Un);
  "onselectstart" in t ? n.on("selectstart.drag", nn, Un) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Eu(e, t) {
  var n = e.document.documentElement, i = Fe(e).on("dragstart.drag", null);
  t && (i.on("click.drag", nn, Un), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Ti = (e) => () => e;
function Wo(e, {
  sourceEvent: t,
  subject: n,
  target: i,
  identifier: o,
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
    identifier: { value: o, enumerable: !0, configurable: !0 },
    active: { value: s, enumerable: !0, configurable: !0 },
    x: { value: a, enumerable: !0, configurable: !0 },
    y: { value: c, enumerable: !0, configurable: !0 },
    dx: { value: u, enumerable: !0, configurable: !0 },
    dy: { value: l, enumerable: !0, configurable: !0 },
    _: { value: d }
  });
}
Wo.prototype.on = function() {
  var e = this._.on.apply(this._, arguments);
  return e === this._ ? this : e;
};
function Ty(e) {
  return !e.ctrlKey && !e.button;
}
function Ry() {
  return this.parentNode;
}
function Py(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function My() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Au() {
  var e = Ty, t = Ry, n = Py, i = My, o = {}, s = Cr("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(w) {
    w.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", y, $y).on("touchend.drag touchcancel.drag", j).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(w, S) {
    if (!(d || !e.call(this, w, S))) {
      var b = g(this, t.call(this, w, S), w, S, "mouse");
      b && (Fe(w.view).on("mousemove.drag", m, Un).on("mouseup.drag", v, Un), ku(w.view), bo(w), l = !1, c = w.clientX, u = w.clientY, b("start", w));
    }
  }
  function m(w) {
    if (nn(w), !l) {
      var S = w.clientX - c, b = w.clientY - u;
      l = S * S + b * b > f;
    }
    o.mouse("drag", w);
  }
  function v(w) {
    Fe(w.view).on("mousemove.drag mouseup.drag", null), Eu(w.view, l), nn(w), o.mouse("end", w);
  }
  function x(w, S) {
    if (e.call(this, w, S)) {
      var b = w.changedTouches, I = t.call(this, w, S), A = b.length, D, M;
      for (D = 0; D < A; ++D)
        (M = g(this, I, w, S, b[D].identifier, b[D])) && (bo(w), M("start", w, b[D]));
    }
  }
  function y(w) {
    var S = w.changedTouches, b = S.length, I, A;
    for (I = 0; I < b; ++I)
      (A = o[S[I].identifier]) && (nn(w), A("drag", w, S[I]));
  }
  function j(w) {
    var S = w.changedTouches, b = S.length, I, A;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), I = 0; I < b; ++I)
      (A = o[S[I].identifier]) && (bo(w), A("end", w, S[I]));
  }
  function g(w, S, b, I, A, D) {
    var M = s.copy(), k = Je(D || b, S), R, L, C;
    if ((C = n.call(w, new Wo("beforestart", {
      sourceEvent: b,
      target: p,
      identifier: A,
      active: a,
      x: k[0],
      y: k[1],
      dx: 0,
      dy: 0,
      dispatch: M
    }), I)) != null)
      return R = C.x - k[0] || 0, L = C.y - k[1] || 0, function _(N, E, T) {
        var $ = k, z;
        switch (N) {
          case "start":
            o[A] = _, z = a++;
            break;
          case "end":
            delete o[A], --a;
          // falls through
          case "drag":
            k = Je(T || E, S), z = a;
            break;
        }
        M.call(
          N,
          w,
          new Wo(N, {
            sourceEvent: E,
            subject: C,
            target: p,
            identifier: A,
            active: z,
            x: k[0] + R,
            y: k[1] + L,
            dx: k[0] - $[0],
            dy: k[1] - $[1],
            dispatch: M
          }),
          I
        );
      };
  }
  return p.filter = function(w) {
    return arguments.length ? (e = typeof w == "function" ? w : Ti(!!w), p) : e;
  }, p.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : Ti(w), p) : t;
  }, p.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : Ti(w), p) : n;
  }, p.touchable = function(w) {
    return arguments.length ? (i = typeof w == "function" ? w : Ti(!!w), p) : i;
  }, p.on = function() {
    var w = s.on.apply(s, arguments);
    return w === s ? p : w;
  }, p.clickDistance = function(w) {
    return arguments.length ? (f = (w = +w) * w, p) : Math.sqrt(f);
  }, p;
}
function As(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function _u(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function ui() {
}
var Yn = 0.7, ar = 1 / Yn, rn = "\\s*([+-]?\\d+)\\s*", Zn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", it = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", Ly = /^#([0-9a-f]{3,8})$/, zy = new RegExp(`^rgb\\(${rn},${rn},${rn}\\)$`), Vy = new RegExp(`^rgb\\(${it},${it},${it}\\)$`), Oy = new RegExp(`^rgba\\(${rn},${rn},${rn},${Zn}\\)$`), Hy = new RegExp(`^rgba\\(${it},${it},${it},${Zn}\\)$`), Wy = new RegExp(`^hsl\\(${Zn},${it},${it}\\)$`), Fy = new RegExp(`^hsla\\(${Zn},${it},${it},${Zn}\\)$`), Xa = {
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
As(ui, Ht, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Ua,
  // Deprecated! Use color.formatHex.
  formatHex: Ua,
  formatHex8: By,
  formatHsl: Ky,
  formatRgb: Ya,
  toString: Ya
});
function Ua() {
  return this.rgb().formatHex();
}
function By() {
  return this.rgb().formatHex8();
}
function Ky() {
  return Du(this).formatHsl();
}
function Ya() {
  return this.rgb().formatRgb();
}
function Ht(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = Ly.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Za(t) : n === 3 ? new ze(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? Ri(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? Ri(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = zy.exec(e)) ? new ze(t[1], t[2], t[3], 1) : (t = Vy.exec(e)) ? new ze(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Oy.exec(e)) ? Ri(t[1], t[2], t[3], t[4]) : (t = Hy.exec(e)) ? Ri(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Wy.exec(e)) ? Qa(t[1], t[2] / 100, t[3] / 100, 1) : (t = Fy.exec(e)) ? Qa(t[1], t[2] / 100, t[3] / 100, t[4]) : Xa.hasOwnProperty(e) ? Za(Xa[e]) : e === "transparent" ? new ze(NaN, NaN, NaN, 0) : null;
}
function Za(e) {
  return new ze(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function Ri(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new ze(e, t, n, i);
}
function qy(e) {
  return e instanceof ui || (e = Ht(e)), e ? (e = e.rgb(), new ze(e.r, e.g, e.b, e.opacity)) : new ze();
}
function Fo(e, t, n, i) {
  return arguments.length === 1 ? qy(e) : new ze(e, t, n, i ?? 1);
}
function ze(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
As(ze, Fo, _u(ui, {
  brighter(e) {
    return e = e == null ? ar : Math.pow(ar, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Yn : Math.pow(Yn, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ze(Rt(this.r), Rt(this.g), Rt(this.b), cr(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Ja,
  // Deprecated! Use color.formatHex.
  formatHex: Ja,
  formatHex8: Xy,
  formatRgb: Ga,
  toString: Ga
}));
function Ja() {
  return `#${Dt(this.r)}${Dt(this.g)}${Dt(this.b)}`;
}
function Xy() {
  return `#${Dt(this.r)}${Dt(this.g)}${Dt(this.b)}${Dt((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ga() {
  const e = cr(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${Rt(this.r)}, ${Rt(this.g)}, ${Rt(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function cr(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function Rt(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function Dt(e) {
  return e = Rt(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Qa(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Ge(e, t, n, i);
}
function Du(e) {
  if (e instanceof Ge) return new Ge(e.h, e.s, e.l, e.opacity);
  if (e instanceof ui || (e = Ht(e)), !e) return new Ge();
  if (e instanceof Ge) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, o = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Ge(a, c, u, e.opacity);
}
function Uy(e, t, n, i) {
  return arguments.length === 1 ? Du(e) : new Ge(e, t, n, i ?? 1);
}
function Ge(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
As(Ge, Uy, _u(ui, {
  brighter(e) {
    return e = e == null ? ar : Math.pow(ar, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Yn : Math.pow(Yn, e), new Ge(this.h, this.s, this.l * e, this.opacity);
  },
  rgb() {
    var e = this.h % 360 + (this.h < 0) * 360, t = isNaN(e) || isNaN(this.s) ? 0 : this.s, n = this.l, i = n + (n < 0.5 ? n : 1 - n) * t, o = 2 * n - i;
    return new ze(
      jo(e >= 240 ? e - 240 : e + 120, o, i),
      jo(e, o, i),
      jo(e < 120 ? e + 240 : e - 120, o, i),
      this.opacity
    );
  },
  clamp() {
    return new Ge(ec(this.h), Pi(this.s), Pi(this.l), cr(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = cr(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${ec(this.h)}, ${Pi(this.s) * 100}%, ${Pi(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function ec(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Pi(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function jo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const _s = (e) => () => e;
function Yy(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Zy(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function Jy(e) {
  return (e = +e) == 1 ? $u : function(t, n) {
    return n - t ? Zy(t, n, e) : _s(isNaN(t) ? n : t);
  };
}
function $u(e, t) {
  var n = t - e;
  return n ? Yy(e, n) : _s(isNaN(e) ? t : e);
}
const lr = (function e(t) {
  var n = Jy(t);
  function i(o, s) {
    var a = n((o = Fo(o)).r, (s = Fo(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = $u(o.opacity, s.opacity);
    return function(d) {
      return o.r = a(d), o.g = c(d), o.b = u(d), o.opacity = l(d), o + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Gy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) i[o] = e[o] * (1 - s) + t[o] * s;
    return i;
  };
}
function Qy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function ex(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, o = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) o[a] = Hn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = o[a](c);
    return s;
  };
}
function tx(e, t) {
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
function nx(e, t) {
  var n = {}, i = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = Hn(e[o], t[o]) : i[o] = t[o];
  return function(s) {
    for (o in n) i[o] = n[o](s);
    return i;
  };
}
var Bo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, No = new RegExp(Bo.source, "g");
function ix(e) {
  return function() {
    return e;
  };
}
function rx(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Tu(e, t) {
  var n = Bo.lastIndex = No.lastIndex = 0, i, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = Bo.exec(e)) && (o = No.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: nt(i, o) })), n = No.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? rx(u[0].x) : ix(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Hn(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? _s(t) : (n === "number" ? nt : n === "string" ? (i = Ht(t)) ? (t = i, lr) : Tu : t instanceof Ht ? lr : t instanceof Date ? tx : Qy(t) ? Gy : Array.isArray(t) ? ex : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? nx : nt)(e, t);
}
var tc = 180 / Math.PI, Ko = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function Ru(e, t, n, i, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * tc,
    skewX: Math.atan(u) * tc,
    scaleX: a,
    scaleY: c
  };
}
var Mi;
function ox(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ko : Ru(t.a, t.b, t.c, t.d, t.e, t.f);
}
function sx(e) {
  return e == null || (Mi || (Mi = document.createElementNS("http://www.w3.org/2000/svg", "g")), Mi.setAttribute("transform", e), !(e = Mi.transform.baseVal.consolidate())) ? Ko : (e = e.matrix, Ru(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Pu(e, t, n, i) {
  function o(l) {
    return l.length ? l.pop() + " " : "";
  }
  function s(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var v = h.push("translate(", null, t, null, n);
      m.push({ i: v - 4, x: nt(l, f) }, { i: v - 2, x: nt(d, p) });
    } else (f || p) && h.push("translate(" + f + t + p + n);
  }
  function a(l, d, f, p) {
    l !== d ? (l - d > 180 ? d += 360 : d - l > 180 && (l += 360), p.push({ i: f.push(o(f) + "rotate(", null, i) - 2, x: nt(l, d) })) : d && f.push(o(f) + "rotate(" + d + i);
  }
  function c(l, d, f, p) {
    l !== d ? p.push({ i: f.push(o(f) + "skewX(", null, i) - 2, x: nt(l, d) }) : d && f.push(o(f) + "skewX(" + d + i);
  }
  function u(l, d, f, p, h, m) {
    if (l !== f || d !== p) {
      var v = h.push(o(h) + "scale(", null, ",", null, ")");
      m.push({ i: v - 4, x: nt(l, f) }, { i: v - 2, x: nt(d, p) });
    } else (f !== 1 || p !== 1) && h.push(o(h) + "scale(" + f + "," + p + ")");
  }
  return function(l, d) {
    var f = [], p = [];
    return l = e(l), d = e(d), s(l.translateX, l.translateY, d.translateX, d.translateY, f, p), a(l.rotate, d.rotate, f, p), c(l.skewX, d.skewX, f, p), u(l.scaleX, l.scaleY, d.scaleX, d.scaleY, f, p), l = d = null, function(h) {
      for (var m = -1, v = p.length, x; ++m < v; ) f[(x = p[m]).i] = x.x(h);
      return f.join("");
    };
  };
}
var ax = Pu(ox, "px, ", "px)", "deg)"), cx = Pu(sx, ", ", ")", ")"), lx = 1e-12;
function nc(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function ux(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function dx(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Ui = (function e(t, n, i) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, m = f - u, v = h * h + m * m, x, y;
    if (v < lx)
      y = Math.log(p / l) / t, x = function(I) {
        return [
          c + I * h,
          u + I * m,
          l * Math.exp(t * I * y)
        ];
      };
    else {
      var j = Math.sqrt(v), g = (p * p - l * l + i * v) / (2 * l * n * j), w = (p * p - l * l - i * v) / (2 * p * n * j), S = Math.log(Math.sqrt(g * g + 1) - g), b = Math.log(Math.sqrt(w * w + 1) - w);
      y = (b - S) / t, x = function(I) {
        var A = I * y, D = nc(S), M = l / (n * j) * (D * dx(t * A + S) - ux(S));
        return [
          c + M * h,
          u + M * m,
          l * D / nc(t * A + S)
        ];
      };
    }
    return x.duration = y * 1e3 * t / Math.SQRT2, x;
  }
  return o.rho = function(s) {
    var a = Math.max(1e-3, +s), c = a * a, u = c * c;
    return e(a, c, u);
  }, o;
})(Math.SQRT2, 2, 4);
var ln = 0, Rn = 0, En = 0, Mu = 1e3, ur, Pn, dr = 0, Wt = 0, kr = 0, Jn = typeof performance == "object" && performance.now ? performance : Date, Lu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function Ds() {
  return Wt || (Lu(fx), Wt = Jn.now() + kr);
}
function fx() {
  Wt = 0;
}
function fr() {
  this._call = this._time = this._next = null;
}
fr.prototype = zu.prototype = {
  constructor: fr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? Ds() : +n) + (t == null ? 0 : +t), !this._next && Pn !== this && (Pn ? Pn._next = this : ur = this, Pn = this), this._call = e, this._time = n, qo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, qo());
  }
};
function zu(e, t, n) {
  var i = new fr();
  return i.restart(e, t, n), i;
}
function px() {
  Ds(), ++ln;
  for (var e = ur, t; e; )
    (t = Wt - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --ln;
}
function ic() {
  Wt = (dr = Jn.now()) + kr, ln = Rn = 0;
  try {
    px();
  } finally {
    ln = 0, gx(), Wt = 0;
  }
}
function hx() {
  var e = Jn.now(), t = e - dr;
  t > Mu && (kr -= t, dr = e);
}
function gx() {
  for (var e, t = ur, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : ur = n);
  Pn = e, qo(i);
}
function qo(e) {
  if (!ln) {
    Rn && (Rn = clearTimeout(Rn));
    var t = e - Wt;
    t > 24 ? (e < 1 / 0 && (Rn = setTimeout(ic, e - Jn.now() - kr)), En && (En = clearInterval(En))) : (En || (dr = Jn.now(), En = setInterval(hx, Mu)), ln = 1, Lu(ic));
  }
}
function rc(e, t, n) {
  var i = new fr();
  return t = t == null ? 0 : +t, i.restart((o) => {
    i.stop(), e(o + t);
  }, t, n), i;
}
var mx = Cr("start", "end", "cancel", "interrupt"), yx = [], Vu = 0, oc = 1, Xo = 2, Yi = 3, sc = 4, Uo = 5, Zi = 6;
function Er(e, t, n, i, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  xx(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: o,
    // For context during callback.
    on: mx,
    tween: yx,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Vu
  });
}
function $s(e, t) {
  var n = tt(e, t);
  if (n.state > Vu) throw new Error("too late; already scheduled");
  return n;
}
function at(e, t) {
  var n = tt(e, t);
  if (n.state > Yi) throw new Error("too late; already running");
  return n;
}
function tt(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function xx(e, t, n) {
  var i = e.__transition, o;
  i[t] = n, n.timer = zu(s, 0, n.time);
  function s(l) {
    n.state = oc, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== oc) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Yi) return rc(a);
        h.state === sc ? (h.state = Zi, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = Zi, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (rc(function() {
      n.state === Yi && (n.state = sc, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Xo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Xo) {
      for (n.state = Yi, o = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
        (h = n.tween[d].value.call(e, e.__data__, n.index, n.group)) && (o[++f] = h);
      o.length = f + 1;
    }
  }
  function c(l) {
    for (var d = l < n.duration ? n.ease.call(null, l / n.duration) : (n.timer.restart(u), n.state = Uo, 1), f = -1, p = o.length; ++f < p; )
      o[f].call(e, d);
    n.state === Uo && (n.on.call("end", e, e.__data__, n.index, n.group), u());
  }
  function u() {
    n.state = Zi, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Ji(e, t) {
  var n = e.__transition, i, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = i.state > Xo && i.state < Uo, i.state = Zi, i.timer.stop(), i.on.call(o ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function wx(e) {
  return this.each(function() {
    Ji(this, e);
  });
}
function vx(e, t) {
  var n, i;
  return function() {
    var o = at(this, e), s = o.tween;
    if (s !== n) {
      i = n = s;
      for (var a = 0, c = i.length; a < c; ++a)
        if (i[a].name === t) {
          i = i.slice(), i.splice(a, 1);
          break;
        }
    }
    o.tween = i;
  };
}
function bx(e, t, n) {
  var i, o;
  if (typeof n != "function") throw new Error();
  return function() {
    var s = at(this, e), a = s.tween;
    if (a !== i) {
      o = (i = a).slice();
      for (var c = { name: t, value: n }, u = 0, l = o.length; u < l; ++u)
        if (o[u].name === t) {
          o[u] = c;
          break;
        }
      u === l && o.push(c);
    }
    s.tween = o;
  };
}
function jx(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = tt(this.node(), n).tween, o = 0, s = i.length, a; o < s; ++o)
      if ((a = i[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? vx : bx)(n, e, t));
}
function Ts(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var o = at(this, i);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return tt(o, i).value[t];
  };
}
function Ou(e, t) {
  var n;
  return (typeof t == "number" ? nt : t instanceof Ht ? lr : (n = Ht(t)) ? (t = n, lr) : Tu)(e, t);
}
function Nx(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Sx(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function Cx(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Ix(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function kx(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Ex(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Ax(e, t) {
  var n = Ir(e), i = n === "transform" ? cx : Ou;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Ex : kx)(n, i, Ts(this, "attr." + e, t)) : t == null ? (n.local ? Sx : Nx)(n) : (n.local ? Ix : Cx)(n, i, t));
}
function _x(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Dx(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function $x(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Dx(e, s)), n;
  }
  return o._value = t, o;
}
function Tx(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && _x(e, s)), n;
  }
  return o._value = t, o;
}
function Rx(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = Ir(e);
  return this.tween(n, (i.local ? $x : Tx)(i, t));
}
function Px(e, t) {
  return function() {
    $s(this, e).delay = +t.apply(this, arguments);
  };
}
function Mx(e, t) {
  return t = +t, function() {
    $s(this, e).delay = t;
  };
}
function Lx(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Px : Mx)(t, e)) : tt(this.node(), t).delay;
}
function zx(e, t) {
  return function() {
    at(this, e).duration = +t.apply(this, arguments);
  };
}
function Vx(e, t) {
  return t = +t, function() {
    at(this, e).duration = t;
  };
}
function Ox(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? zx : Vx)(t, e)) : tt(this.node(), t).duration;
}
function Hx(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    at(this, e).ease = t;
  };
}
function Wx(e) {
  var t = this._id;
  return arguments.length ? this.each(Hx(t, e)) : tt(this.node(), t).ease;
}
function Fx(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    at(this, e).ease = n;
  };
}
function Bx(e) {
  if (typeof e != "function") throw new Error();
  return this.each(Fx(this._id, e));
}
function Kx(e) {
  typeof e != "function" && (e = yu(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new gt(i, this._parents, this._name, this._id);
}
function qx(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, o = n.length, s = Math.min(i, o), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new gt(a, this._parents, this._name, this._id);
}
function Xx(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Ux(e, t, n) {
  var i, o, s = Xx(t) ? $s : at;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (o = (i = c).copy()).on(t, n), a.on = o;
  };
}
function Yx(e, t) {
  var n = this._id;
  return arguments.length < 2 ? tt(this.node(), n).on.on(e) : this.each(Ux(n, e, t));
}
function Zx(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function Jx() {
  return this.on("end.remove", Zx(this._id));
}
function Gx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = ks(e));
  for (var i = this._groups, o = i.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, Er(l[p], t, n, p, l, tt(d, n)));
  return new gt(s, this._parents, t, n);
}
function Qx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = mu(e));
  for (var i = this._groups, o = i.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, m = tt(d, n), v = 0, x = p.length; v < x; ++v)
          (h = p[v]) && Er(h, t, n, v, p, m);
        s.push(p), a.push(d);
      }
  return new gt(s, a, t, n);
}
var ew = li.prototype.constructor;
function tw() {
  return new ew(this._groups, this._parents);
}
function nw(e, t) {
  var n, i, o;
  return function() {
    var s = cn(this, e), a = (this.style.removeProperty(e), cn(this, e));
    return s === a ? null : s === n && a === i ? o : o = t(n = s, i = a);
  };
}
function Hu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function iw(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = cn(this, e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function rw(e, t, n) {
  var i, o, s;
  return function() {
    var a = cn(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), cn(this, e))), a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c));
  };
}
function ow(e, t) {
  var n, i, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = at(this, e), l = u.on, d = u.value[s] == null ? c || (c = Hu(t)) : void 0;
    (l !== n || o !== d) && (i = (n = l).copy()).on(a, o = d), u.on = i;
  };
}
function sw(e, t, n) {
  var i = (e += "") == "transform" ? ax : Ou;
  return t == null ? this.styleTween(e, nw(e, i)).on("end.style." + e, Hu(e)) : typeof t == "function" ? this.styleTween(e, rw(e, i, Ts(this, "style." + e, t))).each(ow(this._id, e)) : this.styleTween(e, iw(e, i, t), n).on("end.style." + e, null);
}
function aw(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function cw(e, t, n) {
  var i, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (i = (o = a) && aw(e, a, n)), i;
  }
  return s._value = t, s;
}
function lw(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, cw(e, t, n ?? ""));
}
function uw(e) {
  return function() {
    this.textContent = e;
  };
}
function dw(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function fw(e) {
  return this.tween("text", typeof e == "function" ? dw(Ts(this, "text", e)) : uw(e == null ? "" : e + ""));
}
function pw(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function hw(e) {
  var t, n;
  function i() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && pw(o)), t;
  }
  return i._value = e, i;
}
function gw(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, hw(e));
}
function mw() {
  for (var e = this._name, t = this._id, n = Wu(), i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = tt(u, t);
        Er(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new gt(i, this._parents, e, n);
}
function yw() {
  var e, t, n = this, i = n._id, o = n.size();
  return new Promise(function(s, a) {
    var c = { value: a }, u = { value: function() {
      --o === 0 && s();
    } };
    n.each(function() {
      var l = at(this, i), d = l.on;
      d !== e && (t = (e = d).copy(), t._.cancel.push(c), t._.interrupt.push(c), t._.end.push(u)), l.on = t;
    }), o === 0 && s();
  });
}
var xw = 0;
function gt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Wu() {
  return ++xw;
}
var ut = li.prototype;
gt.prototype = {
  constructor: gt,
  select: Gx,
  selectAll: Qx,
  selectChild: ut.selectChild,
  selectChildren: ut.selectChildren,
  filter: Kx,
  merge: qx,
  selection: tw,
  transition: mw,
  call: ut.call,
  nodes: ut.nodes,
  node: ut.node,
  size: ut.size,
  empty: ut.empty,
  each: ut.each,
  on: Yx,
  attr: Ax,
  attrTween: Rx,
  style: sw,
  styleTween: lw,
  text: fw,
  textTween: gw,
  remove: Jx,
  tween: jx,
  delay: Lx,
  duration: Ox,
  ease: Wx,
  easeVarying: Bx,
  end: yw,
  [Symbol.iterator]: ut[Symbol.iterator]
};
function ww(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var vw = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: ww
};
function bw(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function jw(e) {
  var t, n;
  e instanceof gt ? (t = e._id, e = e._name) : (t = Wu(), (n = vw).time = Ds(), e = e == null ? null : e + "");
  for (var i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Er(u, e, t, l, a, n || bw(u, t));
  return new gt(i, this._parents, e, t);
}
li.prototype.interrupt = wx;
li.prototype.transition = jw;
const Li = (e) => () => e;
function Nw(e, {
  sourceEvent: t,
  target: n,
  transform: i,
  dispatch: o
}) {
  Object.defineProperties(this, {
    type: { value: e, enumerable: !0, configurable: !0 },
    sourceEvent: { value: t, enumerable: !0, configurable: !0 },
    target: { value: n, enumerable: !0, configurable: !0 },
    transform: { value: i, enumerable: !0, configurable: !0 },
    _: { value: o }
  });
}
function ft(e, t, n) {
  this.k = e, this.x = t, this.y = n;
}
ft.prototype = {
  constructor: ft,
  scale: function(e) {
    return e === 1 ? this : new ft(this.k * e, this.x, this.y);
  },
  translate: function(e, t) {
    return e === 0 & t === 0 ? this : new ft(this.k, this.x + this.k * e, this.y + this.k * t);
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
var Ar = new ft(1, 0, 0);
Fu.prototype = ft.prototype;
function Fu(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Ar;
  return e.__zoom;
}
function So(e) {
  e.stopImmediatePropagation();
}
function An(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Sw(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function Cw() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function ac() {
  return this.__zoom || Ar;
}
function Iw(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function kw() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Ew(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > i ? (i + o) / 2 : Math.min(0, i) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Bu() {
  var e = Sw, t = Cw, n = Ew, i = Iw, o = kw, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Ui, l = Cr("start", "zoom", "end"), d, f, p, h = 500, m = 150, v = 0, x = 10;
  function y(C) {
    C.property("__zoom", ac).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", M).filter(o).on("touchstart.zoom", k).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(C, _, N, E) {
    var T = C.selection ? C.selection() : C;
    T.property("__zoom", ac), C !== T ? S(C, _, N, E) : T.interrupt().each(function() {
      b(this, arguments).event(E).start().zoom(null, typeof _ == "function" ? _.apply(this, arguments) : _).end();
    });
  }, y.scaleBy = function(C, _, N, E) {
    y.scaleTo(C, function() {
      var T = this.__zoom.k, $ = typeof _ == "function" ? _.apply(this, arguments) : _;
      return T * $;
    }, N, E);
  }, y.scaleTo = function(C, _, N, E) {
    y.transform(C, function() {
      var T = t.apply(this, arguments), $ = this.__zoom, z = N == null ? w(T) : typeof N == "function" ? N.apply(this, arguments) : N, F = $.invert(z), B = typeof _ == "function" ? _.apply(this, arguments) : _;
      return n(g(j($, B), z, F), T, a);
    }, N, E);
  }, y.translateBy = function(C, _, N, E) {
    y.transform(C, function() {
      return n(this.__zoom.translate(
        typeof _ == "function" ? _.apply(this, arguments) : _,
        typeof N == "function" ? N.apply(this, arguments) : N
      ), t.apply(this, arguments), a);
    }, null, E);
  }, y.translateTo = function(C, _, N, E, T) {
    y.transform(C, function() {
      var $ = t.apply(this, arguments), z = this.__zoom, F = E == null ? w($) : typeof E == "function" ? E.apply(this, arguments) : E;
      return n(Ar.translate(F[0], F[1]).scale(z.k).translate(
        typeof _ == "function" ? -_.apply(this, arguments) : -_,
        typeof N == "function" ? -N.apply(this, arguments) : -N
      ), $, a);
    }, E, T);
  };
  function j(C, _) {
    return _ = Math.max(s[0], Math.min(s[1], _)), _ === C.k ? C : new ft(_, C.x, C.y);
  }
  function g(C, _, N) {
    var E = _[0] - N[0] * C.k, T = _[1] - N[1] * C.k;
    return E === C.x && T === C.y ? C : new ft(C.k, E, T);
  }
  function w(C) {
    return [(+C[0][0] + +C[1][0]) / 2, (+C[0][1] + +C[1][1]) / 2];
  }
  function S(C, _, N, E) {
    C.on("start.zoom", function() {
      b(this, arguments).event(E).start();
    }).on("interrupt.zoom end.zoom", function() {
      b(this, arguments).event(E).end();
    }).tween("zoom", function() {
      var T = this, $ = arguments, z = b(T, $).event(E), F = t.apply(T, $), B = N == null ? w(F) : typeof N == "function" ? N.apply(T, $) : N, Z = Math.max(F[1][0] - F[0][0], F[1][1] - F[0][1]), H = T.__zoom, U = typeof _ == "function" ? _.apply(T, $) : _, G = u(H.invert(B).concat(Z / H.k), U.invert(B).concat(Z / U.k));
      return function(X) {
        if (X === 1) X = U;
        else {
          var P = G(X), q = Z / P[2];
          X = new ft(q, B[0] - P[0] * q, B[1] - P[1] * q);
        }
        z.zoom(null, X);
      };
    });
  }
  function b(C, _, N) {
    return !N && C.__zooming || new I(C, _);
  }
  function I(C, _) {
    this.that = C, this.args = _, this.active = 0, this.sourceEvent = null, this.extent = t.apply(C, _), this.taps = 0;
  }
  I.prototype = {
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
      var _ = Fe(this.that).datum();
      l.call(
        C,
        this.that,
        new Nw(C, {
          sourceEvent: this.sourceEvent,
          target: y,
          transform: this.that.__zoom,
          dispatch: l
        }),
        _
      );
    }
  };
  function A(C, ..._) {
    if (!e.apply(this, arguments)) return;
    var N = b(this, _).event(C), E = this.__zoom, T = Math.max(s[0], Math.min(s[1], E.k * Math.pow(2, i.apply(this, arguments)))), $ = Je(C);
    if (N.wheel)
      (N.mouse[0][0] !== $[0] || N.mouse[0][1] !== $[1]) && (N.mouse[1] = E.invert(N.mouse[0] = $)), clearTimeout(N.wheel);
    else {
      if (E.k === T) return;
      N.mouse = [$, E.invert($)], Ji(this), N.start();
    }
    An(C), N.wheel = setTimeout(z, m), N.zoom("mouse", n(g(j(E, T), N.mouse[0], N.mouse[1]), N.extent, a));
    function z() {
      N.wheel = null, N.end();
    }
  }
  function D(C, ..._) {
    if (p || !e.apply(this, arguments)) return;
    var N = C.currentTarget, E = b(this, _, !0).event(C), T = Fe(C.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", Z, !0), $ = Je(C, N), z = C.clientX, F = C.clientY;
    ku(C.view), So(C), E.mouse = [$, this.__zoom.invert($)], Ji(this), E.start();
    function B(H) {
      if (An(H), !E.moved) {
        var U = H.clientX - z, G = H.clientY - F;
        E.moved = U * U + G * G > v;
      }
      E.event(H).zoom("mouse", n(g(E.that.__zoom, E.mouse[0] = Je(H, N), E.mouse[1]), E.extent, a));
    }
    function Z(H) {
      T.on("mousemove.zoom mouseup.zoom", null), Eu(H.view, E.moved), An(H), E.event(H).end();
    }
  }
  function M(C, ..._) {
    if (e.apply(this, arguments)) {
      var N = this.__zoom, E = Je(C.changedTouches ? C.changedTouches[0] : C, this), T = N.invert(E), $ = N.k * (C.shiftKey ? 0.5 : 2), z = n(g(j(N, $), E, T), t.apply(this, _), a);
      An(C), c > 0 ? Fe(this).transition().duration(c).call(S, z, E, C) : Fe(this).call(y.transform, z, E, C);
    }
  }
  function k(C, ..._) {
    if (e.apply(this, arguments)) {
      var N = C.touches, E = N.length, T = b(this, _, C.changedTouches.length === E).event(C), $, z, F, B;
      for (So(C), z = 0; z < E; ++z)
        F = N[z], B = Je(F, this), B = [B, this.__zoom.invert(B), F.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== B[2] && (T.touch1 = B, T.taps = 0) : (T.touch0 = B, $ = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (T.taps < 2 && (f = B[0], d = setTimeout(function() {
        d = null;
      }, h)), Ji(this), T.start());
    }
  }
  function R(C, ..._) {
    if (this.__zooming) {
      var N = b(this, _).event(C), E = C.changedTouches, T = E.length, $, z, F, B;
      for (An(C), $ = 0; $ < T; ++$)
        z = E[$], F = Je(z, this), N.touch0 && N.touch0[2] === z.identifier ? N.touch0[0] = F : N.touch1 && N.touch1[2] === z.identifier && (N.touch1[0] = F);
      if (z = N.that.__zoom, N.touch1) {
        var Z = N.touch0[0], H = N.touch0[1], U = N.touch1[0], G = N.touch1[1], X = (X = U[0] - Z[0]) * X + (X = U[1] - Z[1]) * X, P = (P = G[0] - H[0]) * P + (P = G[1] - H[1]) * P;
        z = j(z, Math.sqrt(X / P)), F = [(Z[0] + U[0]) / 2, (Z[1] + U[1]) / 2], B = [(H[0] + G[0]) / 2, (H[1] + G[1]) / 2];
      } else if (N.touch0) F = N.touch0[0], B = N.touch0[1];
      else return;
      N.zoom("touch", n(g(z, F, B), N.extent, a));
    }
  }
  function L(C, ..._) {
    if (this.__zooming) {
      var N = b(this, _).event(C), E = C.changedTouches, T = E.length, $, z;
      for (So(C), p && clearTimeout(p), p = setTimeout(function() {
        p = null;
      }, h), $ = 0; $ < T; ++$)
        z = E[$], N.touch0 && N.touch0[2] === z.identifier ? delete N.touch0 : N.touch1 && N.touch1[2] === z.identifier && delete N.touch1;
      if (N.touch1 && !N.touch0 && (N.touch0 = N.touch1, delete N.touch1), N.touch0) N.touch0[1] = this.__zoom.invert(N.touch0[0]);
      else if (N.end(), N.taps === 2 && (z = Je(z, this), Math.hypot(f[0] - z[0], f[1] - z[1]) < x)) {
        var F = Fe(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : Li(+C), y) : i;
  }, y.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : Li(!!C), y) : e;
  }, y.touchable = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : Li(!!C), y) : o;
  }, y.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : Li([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), y) : t;
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
    return arguments.length ? (x = +C, y) : x;
  }, y;
}
const Xe = {
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
}, Gn = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Ku = ["Enter", " ", "Escape"], qu = {
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
var un;
(function(e) {
  e.Strict = "strict", e.Loose = "loose";
})(un || (un = {}));
var Pt;
(function(e) {
  e.Free = "free", e.Vertical = "vertical", e.Horizontal = "horizontal";
})(Pt || (Pt = {}));
var Qn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Qn || (Qn = {}));
const Xu = {
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
var wt;
(function(e) {
  e.Bezier = "default", e.Straight = "straight", e.Step = "step", e.SmoothStep = "smoothstep", e.SimpleBezier = "simplebezier";
})(wt || (wt = {}));
var pr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(pr || (pr = {}));
var ce;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ce || (ce = {}));
const cc = {
  [ce.Left]: ce.Right,
  [ce.Right]: ce.Left,
  [ce.Top]: ce.Bottom,
  [ce.Bottom]: ce.Top
};
function Uu(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Yu = (e) => "id" in e && "source" in e && "target" in e, Aw = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Rs = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), di = (e, t = [0, 0]) => {
  const { width: n, height: i } = mt(e), o = e.origin ?? t, s = n * o[0], a = i * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, _w = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : Rs(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? hr(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return _r(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return Dr(n);
}, fi = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = _r(n, hr(o)), i = !0);
  }), i ? Dr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Ps = (e, t, [n, i, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...wn(t, [n, i, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, m = d.height ?? l.height ?? l.initialHeight ?? null, v = ei(c, fn(l)), x = (h ?? 0) * (m ?? 0), y = s && v > 0;
    (!l.internals.handleBounds || y || v >= x || l.dragging) && u.push(l);
  }
  return u;
}, Dw = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function $w(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!i || i.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function Tw({ nodes: e, width: t, height: n, panZoom: i, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = $w(e, a), u = fi(c), l = Ls(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Zu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || o;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Xe.error005());
    else {
      const h = c.measured.width, m = c.measured.height;
      h && m && (f = [
        [u, l],
        [u + h, l + m]
      ]);
    }
  else c && Bt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Bt(f) ? Ft(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Xe.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Rw({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: o }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), m = !h && p.parentId && a.find((v) => v.id === p.parentId);
    (h || m) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Dw(a, u);
  for (const p of u)
    c.has(p.id) && !d.find((m) => m.id === p.id) && d.push(p);
  if (!o)
    return {
      edges: d,
      nodes: a
    };
  const f = await o({
    nodes: a,
    edges: d
  });
  return typeof f == "boolean" ? f ? { edges: d, nodes: a } : { edges: [], nodes: [] } : f;
}
const dn = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ft = (e = { x: 0, y: 0 }, t, n) => ({
  x: dn(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: dn(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Ju(e, t, n) {
  const { width: i, height: o } = mt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Ft(e, [
    [s, a],
    [s + i, a + o]
  ], t);
}
const lc = (e, t, n) => e < t ? dn(Math.abs(e - t), 1, t) / t : e > n ? -dn(Math.abs(e - n), 1, t) / t : 0, Ms = (e, t, n = 15, i = 40) => {
  const o = lc(e.x, i, t.width - i) * n, s = lc(e.y, i, t.height - i) * n;
  return [o, s];
}, _r = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Yo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), Dr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), fn = (e, t = [0, 0]) => {
  const { x: n, y: i } = Rs(e) ? e.internals.positionAbsolute : di(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, hr = (e, t = [0, 0]) => {
  const { x: n, y: i } = Rs(e) ? e.internals.positionAbsolute : di(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Gu = (e, t) => Dr(_r(Yo(e), Yo(t))), ei = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, uc = (e) => Qe(e.width) && Qe(e.height) && Qe(e.x) && Qe(e.y), Qe = (e) => !isNaN(e) && isFinite(e), Qu = (e, t) => (n, i) => {
}, pi = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), wn = ({ x: e, y: t }, [n, i, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - i) / o
  };
  return s ? pi(c, a) : c;
}, pn = ({ x: e, y: t }, [n, i, o]) => ({
  x: e * o + n,
  y: t * o + i
});
function Gt(e, t) {
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
function Pw(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Gt(e, n), o = Gt(e, t);
    return {
      top: i,
      right: o,
      bottom: i,
      left: o,
      x: o * 2,
      y: i * 2
    };
  }
  if (typeof e == "object") {
    const i = Gt(e.top ?? e.y ?? 0, n), o = Gt(e.bottom ?? e.y ?? 0, n), s = Gt(e.left ?? e.x ?? 0, t), a = Gt(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: o, left: s, x: s + a, y: i + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Mw(e, t, n, i, o, s) {
  const { x: a, y: c } = pn(e, [t, n, i]), { x: u, y: l } = pn({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = o - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Ls = (e, t, n, i, o, s) => {
  const a = Pw(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = dn(l, i, o), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, m = n / 2 - p * d, v = Mw(e, h, m, d, t, n), x = {
    left: Math.min(v.left - a.left, 0),
    top: Math.min(v.top - a.top, 0),
    right: Math.min(v.right - a.right, 0),
    bottom: Math.min(v.bottom - a.bottom, 0)
  };
  return {
    x: h - x.left + x.right,
    y: m - x.top + x.bottom,
    zoom: d
  };
}, ti = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Bt(e) {
  return e != null && e !== "parent";
}
function mt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function ed(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function td(e, t = { width: 0, height: 0 }, n, i, o) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function dc(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function Lw() {
  let e, t;
  return { promise: new Promise((i, o) => {
    e = i, t = o;
  }), resolve: e, reject: t };
}
function zw(e) {
  return { ...qu, ...e || {} };
}
function Wn(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: o }) {
  const { x: s, y: a } = et(e), c = wn({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, i), { x: u, y: l } = n ? pi(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const zs = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), nd = (e) => e?.getRootNode?.() || window?.document, Vw = ["INPUT", "SELECT", "TEXTAREA"];
function id(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Vw.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const rd = (e) => "clientX" in e, et = (e, t) => {
  const n = rd(e), i = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, fc = (e, t, n, i, o) => {
  const s = t.querySelectorAll(`.${e}`);
  return !s || !s.length ? null : Array.from(s).map((a) => {
    const c = a.getBoundingClientRect();
    return {
      id: a.getAttribute("data-handleid"),
      type: e,
      nodeId: o,
      position: a.getAttribute("data-handlepos"),
      x: (c.left - n.left) / i,
      y: (c.top - n.top) / i,
      ...zs(a)
    };
  });
};
function od({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function zi(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function pc({ pos: e, x1: t, y1: n, x2: i, y2: o, c: s }) {
  switch (e) {
    case ce.Left:
      return [t - zi(t - i, s), n];
    case ce.Right:
      return [t + zi(i - t, s), n];
    case ce.Top:
      return [t, n - zi(n - o, s)];
    case ce.Bottom:
      return [t, n + zi(o - n, s)];
  }
}
function sd({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top, curvature: a = 0.25 }) {
  const [c, u] = pc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o,
    c: a
  }), [l, d] = pc({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, m] = od({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: o,
    sourceControlX: c,
    sourceControlY: u,
    targetControlX: l,
    targetControlY: d
  });
  return [
    `M${e},${t} C${c},${u} ${l},${d} ${i},${o}`,
    f,
    p,
    h,
    m
  ];
}
function ad({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, o, a];
}
function Ow({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = o && n ? i + 1e3 : i, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function Hw({ sourceNode: e, targetNode: t, width: n, height: i, transform: o }) {
  const s = _r(hr(e), hr(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: i / o[2]
  };
  return ei(a, Dr(s)) > 0;
}
const cd = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, Ww = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), Fw = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Xe.error006()), t;
  const i = n.getEdgeId || cd;
  let o;
  return Yu(e) ? o = { ...e } : o = {
    ...e,
    id: i(e)
  }, Ww(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, Bw = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Xe.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Xe.error007(o)), n;
  const c = i.getEdgeId || cd, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function ld({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [o, s, a, c] = ad({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, o, s, a, c];
}
const hc = {
  [ce.Left]: { x: -1, y: 0 },
  [ce.Right]: { x: 1, y: 0 },
  [ce.Top]: { x: 0, y: -1 },
  [ce.Bottom]: { x: 0, y: 1 }
}, Kw = ({ source: e, sourcePosition: t = ce.Bottom, target: n }) => t === ce.Left || t === ce.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, gc = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function qw({ source: e, sourcePosition: t = ce.Bottom, target: n, targetPosition: i = ce.Top, center: o, offset: s, stepPosition: a }) {
  const c = hc[t], u = hc[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Kw({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let m = [], v, x;
  const y = { x: 0, y: 0 }, j = { x: 0, y: 0 }, [, , g, w] = ad({
    sourceX: e.x,
    sourceY: e.y,
    targetX: n.x,
    targetY: n.y
  });
  if (c[p] * u[p] === -1) {
    p === "x" ? (v = o.x ?? l.x + (d.x - l.x) * a, x = o.y ?? (l.y + d.y) / 2) : (v = o.x ?? (l.x + d.x) / 2, x = o.y ?? l.y + (d.y - l.y) * a);
    const A = [
      { x: v, y: l.y },
      { x: v, y: d.y }
    ], D = [
      { x: l.x, y: x },
      { x: d.x, y: x }
    ];
    c[p] === h ? m = p === "x" ? A : D : m = p === "x" ? D : A;
  } else {
    const A = [{ x: l.x, y: d.y }], D = [{ x: d.x, y: l.y }];
    if (p === "x" ? m = c.x === h ? D : A : m = c.y === h ? A : D, t === i) {
      const C = Math.abs(e[p] - n[p]);
      if (C <= s) {
        const _ = Math.min(s - 1, s - C);
        c[p] === h ? y[p] = (l[p] > e[p] ? -1 : 1) * _ : j[p] = (d[p] > n[p] ? -1 : 1) * _;
      }
    }
    if (t !== i) {
      const C = p === "x" ? "y" : "x", _ = c[p] === u[C], N = l[C] > d[C], E = l[C] < d[C];
      (c[p] === 1 && (!_ && N || _ && E) || c[p] !== 1 && (!_ && E || _ && N)) && (m = p === "x" ? A : D);
    }
    const M = { x: l.x + y.x, y: l.y + y.y }, k = { x: d.x + j.x, y: d.y + j.y }, R = Math.max(Math.abs(M.x - m[0].x), Math.abs(k.x - m[0].x)), L = Math.max(Math.abs(M.y - m[0].y), Math.abs(k.y - m[0].y));
    R >= L ? (v = (M.x + k.x) / 2, x = m[0].y) : (v = m[0].x, x = (M.y + k.y) / 2);
  }
  const S = { x: l.x + y.x, y: l.y + y.y }, b = { x: d.x + j.x, y: d.y + j.y };
  return [[
    e,
    // we only want to add the gapped source/target if they are different from the first/last point to avoid duplicates which can cause issues with the bends
    ...S.x !== m[0].x || S.y !== m[0].y ? [S] : [],
    ...m,
    ...b.x !== m[m.length - 1].x || b.y !== m[m.length - 1].y ? [b] : [],
    n
  ], v, x, g, w];
}
function Xw(e, t, n, i) {
  const o = Math.min(gc(e, t) / 2, gc(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function gr({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, m, v] = qw({
    source: { x: e, y: t },
    sourcePosition: n,
    target: { x: i, y: o },
    targetPosition: s,
    center: { x: c, y: u },
    offset: l,
    stepPosition: d
  });
  let x = `M${f[0].x} ${f[0].y}`;
  for (let y = 1; y < f.length - 1; y++)
    x += Xw(f[y - 1], f[y], f[y + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, m, v];
}
function mc(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Uw(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!mc(t) || !mc(n))
    return null;
  const i = t.internals.handleBounds || yc(t.handles), o = n.internals.handleBounds || yc(n.handles), s = xc(i?.source ?? [], e.sourceHandle), a = xc(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === un.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Xe.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ce.Bottom, u = a?.position || ce.Top, l = Kt(t, s, c), d = Kt(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function yc(e) {
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
function Kt(e, t, n = ce.Left, i = !1) {
  const o = (t?.x ?? 0) + e.internals.positionAbsolute.x, s = (t?.y ?? 0) + e.internals.positionAbsolute.y, { width: a, height: c } = t ?? mt(e);
  if (i)
    return { x: o + a / 2, y: s + c / 2 };
  switch (t?.position ?? n) {
    case ce.Top:
      return { x: o + a / 2, y: s };
    case ce.Right:
      return { x: o + a, y: s + c / 2 };
    case ce.Bottom:
      return { x: o + a / 2, y: s + c };
    case ce.Left:
      return { x: o, y: s + c / 2 };
  }
}
function xc(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Zo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function Yw(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Zo(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const ud = 1e3, Zw = 10, Vs = {
  nodeOrigin: [0, 0],
  nodeExtent: Gn,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, Jw = {
  ...Vs,
  checkEquality: !0
};
function Os(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Gw(e, t, n) {
  const i = Os(Vs, n);
  for (const o of e.values())
    if (o.parentId)
      Ws(o, e, t, i);
    else {
      const s = di(o, i.nodeOrigin), a = Bt(o.extent) ? o.extent : i.nodeExtent, c = Ft(s, a, mt(o));
      o.internals.positionAbsolute = c;
    }
}
function Qw(e, t) {
  if (!e.handles)
    return e.measured ? t?.internals.handleBounds : void 0;
  const n = [], i = [];
  for (const o of e.handles) {
    const s = {
      id: o.id,
      width: o.width ?? 1,
      height: o.height ?? 1,
      nodeId: e.id,
      x: o.x,
      y: o.y,
      position: o.position,
      type: o.type
    };
    o.type === "source" ? n.push(s) : o.type === "target" && i.push(s);
  }
  return {
    source: n,
    target: i
  };
}
function Hs(e) {
  return e === "manual";
}
function Jo(e, t, n, i = {}) {
  const o = Os(Jw, i), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !Hs(o.zIndexMode) ? ud : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (o.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = di(d, o.nodeOrigin), h = Bt(d.extent) ? d.extent : o.nodeExtent, m = Ft(p, h, mt(d));
      f = {
        ...o.defaults,
        ...d,
        measured: {
          width: d.measured?.width,
          height: d.measured?.height
        },
        internals: {
          positionAbsolute: m,
          // if user re-initializes the node or removes `measured` for whatever reason, we reset the handleBounds so that the node gets re-measured
          handleBounds: Qw(d, f),
          z: dd(d, c, o.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Ws(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function ev(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Ws(e, t, n, i, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Os(Vs, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  ev(e, n), o && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++o.i, d.internals.z = d.internals.z + o.i * Zw), o && d.internals.rootParentIndex !== void 0 && (o.i = d.internals.rootParentIndex);
  const f = s && !Hs(u) ? ud : 0, { x: p, y: h, z: m } = tv(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, x = p !== v.x || h !== v.y;
  (x || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : v,
      z: m
    }
  });
}
function dd(e, t, n) {
  const i = Qe(e.zIndex) ? e.zIndex : 0;
  return Hs(n) ? i : i + (e.selected ? t : 0);
}
function tv(e, t, n, i, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = mt(e), l = di(e, n), d = Bt(e.extent) ? Ft(l, e.extent, u) : l;
  let f = Ft({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Ju(f, u, t));
  const p = dd(e, o, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function Fs(e, t, n, i = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? fn(c), l = Gu(u, a.rect);
    s.set(a.parentId, { expandedRect: l, parent: c });
  }
  return s.size > 0 && s.forEach(({ expandedRect: a, parent: c }, u) => {
    const l = c.internals.positionAbsolute, d = mt(c), f = c.origin ?? i, p = a.x < l.x ? Math.round(Math.abs(l.x - a.x)) : 0, h = a.y < l.y ? Math.round(Math.abs(l.y - a.y)) : 0, m = Math.max(d.width, Math.round(a.width)), v = Math.max(d.height, Math.round(a.height)), x = (m - d.width) * f[0], y = (v - d.height) * f[1];
    (p > 0 || h > 0 || x || y) && (o.push({
      id: u,
      type: "position",
      position: {
        x: c.position.x - p + x,
        y: c.position.y - h + y
      }
    }), n.get(u)?.forEach((j) => {
      e.some((g) => g.id === j.id) || o.push({
        id: j.id,
        type: "position",
        position: {
          x: j.position.x + p,
          y: j.position.y + h
        }
      });
    })), (d.width < a.width || d.height < a.height || p || h) && o.push({
      id: u,
      type: "dimensions",
      setAttributes: !0,
      dimensions: {
        width: m + (p ? f[0] * p - x : 0),
        height: v + (h ? f[1] * h - y : 0)
      }
    });
  }), o;
}
function nv(e, t, n, i, o, s, a) {
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
    const v = zs(h.nodeElement), x = m.measured.width !== v.width || m.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !m.internals.handleBounds || h.force))) {
      const j = h.nodeElement.getBoundingClientRect(), g = Bt(m.extent) ? m.extent : s;
      let { positionAbsolute: w } = m.internals;
      m.parentId && m.extent === "parent" ? w = Ju(w, v, t.get(m.parentId)) : g && (w = Ft(w, g, v));
      const S = {
        ...m,
        measured: v,
        internals: {
          ...m.internals,
          positionAbsolute: w,
          handleBounds: {
            source: fc("source", h.nodeElement, j, f, m.id),
            target: fc("target", h.nodeElement, j, f, m.id)
          }
        }
      };
      t.set(m.id, S), m.parentId && Ws(S, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, x && (l.push({
        id: m.id,
        type: "dimensions",
        dimensions: v
      }), m.expandParent && m.parentId && p.push({
        id: m.id,
        parentId: m.parentId,
        rect: fn(S, o)
      }));
    }
  }
  if (p.length > 0) {
    const h = Fs(p, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function iv({ delta: e, panZoom: t, transform: n, translateExtent: i, width: o, height: s }) {
  if (!t || !e.x && !e.y)
    return !1;
  const a = await t.setViewportConstrained({
    x: n[0] + e.x,
    y: n[1] + e.y,
    zoom: n[2]
  }, [
    [0, 0],
    [o, s]
  ], i);
  return !!a && (a.x !== n[0] || a.y !== n[1] || a.k !== n[2]);
}
function wc(e, t, n, i, o, s) {
  let a = o;
  const c = i.get(a) || /* @__PURE__ */ new Map();
  i.set(a, c.set(n, t)), a = `${o}-${e}`;
  const u = i.get(a) || /* @__PURE__ */ new Map();
  if (i.set(a, u.set(n, t)), s) {
    a = `${o}-${e}-${s}`;
    const l = i.get(a) || /* @__PURE__ */ new Map();
    i.set(a, l.set(n, t));
  }
}
function fd(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, d = `${s}-${c}--${o}-${a}`;
    wc("source", u, d, e, o, a), wc("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function pd(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : pd(n, t) : !1;
}
function vc(e, t, n) {
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
function rv(e, t, n, i) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !pd(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
      const c = e.get(s);
      c && o.set(s, {
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
  return o;
}
function Co({ nodeId: e, dragItems: t, nodeLookup: n, dragging: i = !0 }) {
  const o = [];
  for (const [a, c] of t) {
    const u = n.get(a)?.internals.userNode;
    u && o.push({
      ...u,
      position: c.position,
      dragging: i
    });
  }
  if (!e)
    return [o[0], o];
  const s = n.get(e)?.internals.userNode;
  return [
    s ? {
      ...s,
      position: t.get(e)?.position || s.position,
      dragging: i
    } : o[0],
    o
  ];
}
function ov({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: i - o.distance.y
  }, a = pi(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function sv({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, m = !1, v = null;
  function x({ noDragClassName: j, handleSelector: g, domNode: w, isSelectable: S, nodeId: b, nodeClickDistance: I = 0 }) {
    p = Fe(w);
    function A({ x: R, y: L }) {
      const { nodeLookup: C, nodeExtent: _, snapGrid: N, snapToGrid: E, nodeOrigin: T, onNodeDrag: $, onSelectionDrag: z, onError: F, updateNodePositions: B } = t();
      s = { x: R, y: L };
      let Z = !1;
      const H = c.size > 1, U = H && _ ? Yo(fi(c)) : null, G = H && E ? ov({
        dragItems: c,
        snapGrid: N,
        x: R,
        y: L
      }) : null;
      for (const [X, P] of c) {
        if (!C.has(X))
          continue;
        let q = { x: R - P.distance.x, y: L - P.distance.y };
        E && (q = G ? {
          x: Math.round(q.x + G.x),
          y: Math.round(q.y + G.y)
        } : pi(q, N));
        let de = null;
        if (H && _ && !P.extent && U) {
          const { positionAbsolute: re } = P.internals, fe = re.x - U.x + _[0][0], W = re.x + P.measured.width - U.x2 + _[1][0], ne = re.y - U.y + _[0][1], ge = re.y + P.measured.height - U.y2 + _[1][1];
          de = [
            [fe, ne],
            [W, ge]
          ];
        }
        const { position: le, positionAbsolute: te } = Zu({
          nodeId: X,
          nextPosition: q,
          nodeLookup: C,
          nodeExtent: de || _,
          nodeOrigin: T,
          onError: F
        });
        Z = Z || P.position.x !== le.x || P.position.y !== le.y, P.position = le, P.internals.positionAbsolute = te;
      }
      if (m = m || Z, !!Z && (B(c, !0), v && (i || $ || !b && z))) {
        const [X, P] = Co({
          nodeId: b,
          dragItems: c,
          nodeLookup: C
        });
        i?.(v, c, X, P), $?.(v, X, P), b || z?.(v, P);
      }
    }
    async function D() {
      if (!d)
        return;
      const { transform: R, panBy: L, autoPanSpeed: C, autoPanOnNodeDrag: _ } = t();
      if (!_) {
        u = !1, cancelAnimationFrame(a);
        return;
      }
      const [N, E] = Ms(l, d, C);
      (N !== 0 || E !== 0) && (s.x = (s.x ?? 0) - N / R[2], s.y = (s.y ?? 0) - E / R[2], await L({ x: N, y: E }) && A(s)), a = requestAnimationFrame(D);
    }
    function M(R) {
      const { nodeLookup: L, multiSelectionActive: C, nodesDraggable: _, transform: N, snapGrid: E, snapToGrid: T, selectNodesOnDrag: $, onNodeDragStart: z, onSelectionDragStart: F, unselectNodesAndEdges: B } = t();
      f = !0, (!$ || !S) && !C && b && (L.get(b)?.selected || B()), S && $ && b && e?.(b);
      const Z = Wn(R.sourceEvent, { transform: N, snapGrid: E, snapToGrid: T, containerBounds: d });
      if (s = Z, c = rv(L, _, Z, b), c.size > 0 && (n || z || !b && F)) {
        const [H, U] = Co({
          nodeId: b,
          dragItems: c,
          nodeLookup: L
        });
        n?.(R.sourceEvent, c, H, U), z?.(R.sourceEvent, H, U), b || F?.(R.sourceEvent, U);
      }
    }
    const k = Au().clickDistance(I).on("start", (R) => {
      const { domNode: L, nodeDragThreshold: C, transform: _, snapGrid: N, snapToGrid: E } = t();
      d = L?.getBoundingClientRect() || null, h = !1, m = !1, v = R.sourceEvent, C === 0 && M(R), s = Wn(R.sourceEvent, { transform: _, snapGrid: N, snapToGrid: E, containerBounds: d }), l = et(R.sourceEvent, d);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: L, transform: C, snapGrid: _, snapToGrid: N, nodeDragThreshold: E, nodeLookup: T } = t(), $ = Wn(R.sourceEvent, { transform: C, snapGrid: _, snapToGrid: N, containerBounds: d });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      b && !T.has(b)) && (h = !0), !h) {
        if (!u && L && f && (u = !0, D()), !f) {
          const z = et(R.sourceEvent, d), F = z.x - l.x, B = z.y - l.y;
          Math.sqrt(F * F + B * B) > E && M(R);
        }
        (s.x !== $.xSnapped || s.y !== $.ySnapped) && c && f && (l = et(R.sourceEvent, d), A($));
      }
    }).on("end", (R) => {
      if (!f || h) {
        h && c.size > 0 && t().updateNodePositions(c, !1);
        return;
      }
      if (u = !1, f = !1, cancelAnimationFrame(a), c.size > 0) {
        const { nodeLookup: L, updateNodePositions: C, onNodeDragStop: _, onSelectionDragStop: N } = t();
        if (m && (C(c, !1), m = !1), o || _ || !b && N) {
          const [E, T] = Co({
            nodeId: b,
            dragItems: c,
            nodeLookup: L,
            dragging: !1
          });
          o?.(R.sourceEvent, c, E, T), _?.(R.sourceEvent, E, T), b || N?.(R.sourceEvent, T);
        }
      }
    }).filter((R) => {
      const L = R.target;
      return !R.button && (!j || !vc(L, `.${j}`, w)) && (!g || vc(L, g, w));
    });
    p.call(k);
  }
  function y() {
    p?.on(".drag", null);
  }
  return {
    update: x,
    destroy: y
  };
}
function av(e, t, n) {
  const i = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    ei(o, fn(s)) > 0 && i.push(s);
  return i;
}
const cv = 250;
function lv(e, t, n, i) {
  let o = [], s = 1 / 0;
  const a = av(e, n, t + cv);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Kt(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
      p > t || (p < s ? (o = [{ ...l, x: d, y: f }], s = p) : p === s && o.push({ ...l, x: d, y: f }));
    }
  }
  if (!o.length)
    return null;
  if (o.length > 1) {
    const c = i.type === "source" ? "target" : "source";
    return o.find((u) => u.type === c) ?? o[0];
  }
  return o[0];
}
function hd(e, t, n, i, o, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Kt(a, u, u.position, !0) } : u;
}
function gd(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function uv(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const md = () => !0;
function dv(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: m, onConnect: v, onConnectEnd: x, isValidConnection: y = md, onReconnectEnd: j, updateConnection: g, getTransform: w, getFromHandle: S, autoPanSpeed: b, dragThreshold: I = 1, handleDomNode: A }) {
  const D = nd(e.target);
  let M = 0, k;
  const { x: R, y: L } = et(e), C = gd(s, A), _ = c?.getBoundingClientRect();
  let N = !1;
  if (!_ || !C)
    return;
  const E = hd(o, C, i, u, t);
  if (!E)
    return;
  let T = et(e, _), $ = !1, z = null, F = !1, B = null;
  function Z() {
    if (!d || !_)
      return;
    const [le, te] = Ms(T, _, b);
    p({ x: le, y: te }), M = requestAnimationFrame(Z);
  }
  const H = {
    ...E,
    nodeId: o,
    type: C,
    position: E.position
  }, U = u.get(o);
  let X = {
    inProgress: !0,
    isValid: null,
    from: Kt(U, H, ce.Left, !0),
    fromHandle: H,
    fromPosition: H.position,
    fromNode: U,
    to: T,
    toHandle: null,
    toPosition: cc[H.position],
    toNode: null,
    pointer: T
  };
  function P() {
    N = !0, g(X), m?.(e, { nodeId: o, handleId: i, handleType: C });
  }
  I === 0 && P();
  function q(le) {
    if (!N) {
      const { x: ge, y: ye } = et(le), Ee = ge - R, Re = ye - L;
      if (!(Ee * Ee + Re * Re > I * I))
        return;
      P();
    }
    if (!S() || !H) {
      de(le);
      return;
    }
    const te = w();
    T = et(le, _), k = lv(wn(T, te, !1, [1, 1]), n, u, H), $ || (Z(), $ = !0);
    const re = yd(le, {
      handle: k,
      connectionMode: t,
      fromNodeId: o,
      fromHandleId: i,
      fromType: a ? "target" : "source",
      isValidConnection: y,
      doc: D,
      lib: l,
      flowId: f,
      nodeLookup: u
    });
    B = re.handleDomNode, z = re.connection, F = uv(!!k, re.isValid);
    const fe = u.get(o), W = fe ? Kt(fe, H, ce.Left, !0) : X.from, ne = {
      ...X,
      from: W,
      isValid: F,
      to: re.toHandle && F ? pn({ x: re.toHandle.x, y: re.toHandle.y }, te) : T,
      toHandle: re.toHandle,
      toPosition: F && re.toHandle ? re.toHandle.position : cc[H.position],
      toNode: re.toHandle ? u.get(re.toHandle.nodeId) : null,
      pointer: T
    };
    g(ne), X = ne;
  }
  function de(le) {
    if (!("touches" in le && le.touches.length > 0)) {
      if (N) {
        (k || B) && z && F && v?.(z);
        const { inProgress: te, ...re } = X, fe = {
          ...re,
          toPosition: X.toHandle ? X.toPosition : null
        };
        x?.(le, fe), s && j?.(le, fe);
      }
      h(), cancelAnimationFrame(M), $ = !1, F = !1, z = null, B = null, D.removeEventListener("mousemove", q), D.removeEventListener("mouseup", de), D.removeEventListener("touchmove", q), D.removeEventListener("touchend", de);
    }
  }
  D.addEventListener("mousemove", q), D.addEventListener("mouseup", de), D.addEventListener("touchmove", q), D.addEventListener("touchend", de);
}
function yd(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = md, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: m } = et(e), v = a.elementFromPoint(h, m), x = v?.classList.contains(`${c}-flow__handle`) ? v : p, y = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const j = gd(void 0, x), g = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), S = x.classList.contains("connectable"), b = x.classList.contains("connectableend");
    if (!g || !j)
      return y;
    const I = {
      source: f ? g : i,
      sourceHandle: f ? w : o,
      target: f ? i : g,
      targetHandle: f ? o : w
    };
    y.connection = I;
    const D = S && b && (n === un.Strict ? f && j === "source" || !f && j === "target" : g !== i || w !== o);
    y.isValid = D && l(I), y.toHandle = hd(g, j, w, d, n, !0);
  }
  return y;
}
const Go = {
  onPointerDown: dv,
  isValid: yd
};
function fv({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const o = Fe(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), S = g.sourceEvent.ctrlKey && ti() ? 10 : 1, b = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, I = w[2] * Math.pow(2, b * S);
      t.scaleTo(I);
    };
    let v = [0, 0];
    const x = (g) => {
      (g.sourceEvent.type === "mousedown" || g.sourceEvent.type === "touchstart") && (v = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ]);
    }, y = (g) => {
      const w = n();
      if (g.sourceEvent.type !== "mousemove" && g.sourceEvent.type !== "touchmove" || !t)
        return;
      const S = [
        g.sourceEvent.clientX ?? g.sourceEvent.touches[0].clientX,
        g.sourceEvent.clientY ?? g.sourceEvent.touches[0].clientY
      ], b = [S[0] - v[0], S[1] - v[1]];
      v = S;
      const I = i() * Math.max(w[2], Math.log(w[2])) * (h ? -1 : 1), A = {
        x: w[0] - b[0] * I,
        y: w[1] - b[1] * I
      }, D = [
        [0, 0],
        [u, l]
      ];
      t.setViewportConstrained({
        x: A.x,
        y: A.y,
        zoom: w[2]
      }, D, c);
    }, j = Bu().on("start", x).on("zoom", f ? y : null).on("zoom.wheel", p ? m : null);
    o.call(j, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Je
  };
}
const $r = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Io = ({ x: e, y: t, zoom: n }) => Ar.translate(e, t).scale(n), en = (e, t) => e.target.closest(`.${t}`), xd = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), pv = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, ko = (e, t = 0, n = pv, i = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || i(), o ? e.transition().duration(t).ease(n).on("end", i) : e;
}, wd = (e) => {
  const t = e.ctrlKey && ti() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function hv({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (en(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = Je(d), y = wd(d), j = f * Math.pow(2, y);
      i.scaleTo(n, j, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = o === Pt.Vertical ? 0 : d.deltaX * p, m = o === Pt.Horizontal ? 0 : d.deltaY * p;
    !ti() && d.shiftKey && o !== Pt.Vertical && (h = d.deltaY * p, m = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(m / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = $r(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function gv({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, o) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = en(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, o);
  };
}
function mv({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const o = $r(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, o);
  };
}
function yv({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && xd(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, $r(s.transform));
  };
}
function xv({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && xd(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), o)) {
      const c = $r(a.transform);
      e.prevViewport = c, clearTimeout(e.timerId), e.timerId = setTimeout(
        () => {
          o?.(a.sourceEvent, c);
        },
        // we need a setTimeout for panOnScroll to suppress multiple end events fired during scroll
        n ? 150 : 0
      );
    }
  };
}
function wv({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (en(f, `${l}-flow__node`) || en(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !o && !s && !n || a || d && !m || en(f, c) && m || en(f, u) && (!m || o && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !o && !h && m || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && v;
  };
}
function vv({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Bu().scaleExtent([t, n]).translateExtent(i), p = Fe(e).call(f);
  j({
    x: o.x,
    y: o.y,
    zoom: dn(o.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), m = p.on("dblclick.zoom");
  f.wheelDelta(wd);
  async function v(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Hn : Ui).transform(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  function x({ noWheelClassName: k, noPanClassName: R, onPaneContextMenu: L, userSelectionActive: C, panOnScroll: _, panOnDrag: N, panOnScrollMode: E, panOnScrollSpeed: T, preventScrolling: $, zoomOnPinch: z, zoomOnScroll: F, zoomOnDoubleClick: B, zoomActivationKeyPressed: Z, lib: H, onTransformChange: U, connectionInProgress: G, paneClickDistance: X, selectionOnDrag: P }) {
    C && !l.isZoomingOrPanning && y();
    const q = _ && !Z && !C;
    f.clickDistance(P ? 1 / 0 : !Qe(X) || X < 0 ? 0 : X);
    const de = q ? hv({
      zoomPanValues: l,
      noWheelClassName: k,
      d3Selection: p,
      d3Zoom: f,
      panOnScrollMode: E,
      panOnScrollSpeed: T,
      zoomOnPinch: z,
      onPanZoomStart: a,
      onPanZoom: s,
      onPanZoomEnd: c
    }) : gv({
      noWheelClassName: k,
      preventScrolling: $,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", de, { passive: !1 });
    const le = mv({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", le);
    const te = yv({
      zoomPanValues: l,
      panOnDrag: N,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: U
    });
    f.on("zoom", te);
    const re = xv({
      zoomPanValues: l,
      panOnDrag: N,
      panOnScroll: _,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", re);
    const fe = wv({
      zoomActivationKeyPressed: Z,
      panOnDrag: N,
      zoomOnScroll: F,
      panOnScroll: _,
      zoomOnDoubleClick: B,
      zoomOnPinch: z,
      userSelectionActive: C,
      noPanClassName: R,
      noWheelClassName: k,
      lib: H,
      connectionInProgress: G
    });
    f.filter(fe), B ? p.on("dblclick.zoom", m) : p.on("dblclick.zoom", null);
  }
  function y() {
    f.on("zoom", null);
  }
  async function j(k, R, L) {
    const C = Io(k), _ = f?.constrain()(C, R, L);
    return _ && await v(_), _;
  }
  async function g(k, R) {
    const L = Io(k);
    return await v(L, R), L;
  }
  function w(k) {
    if (p) {
      const R = Io(k), L = p.property("__zoom");
      (L.k !== k.zoom || L.x !== k.x || L.y !== k.y) && f?.transform(p, R, null, { sync: !0 });
    }
  }
  function S() {
    const k = p ? Fu(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function b(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Hn : Ui).scaleTo(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  async function I(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Hn : Ui).scaleBy(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  function A(k) {
    f?.scaleExtent(k);
  }
  function D(k) {
    f?.translateExtent(k);
  }
  function M(k) {
    const R = !Qe(k) || k < 0 ? 0 : k;
    f?.clickDistance(R);
  }
  return {
    update: x,
    destroy: y,
    setViewport: g,
    setViewportConstrained: j,
    getViewport: S,
    scaleTo: b,
    scaleBy: I,
    setScaleExtent: A,
    setTranslateExtent: D,
    syncViewport: w,
    setClickDistance: M
  };
}
var hn;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(hn || (hn = {}));
function bv({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function bc(e) {
  const t = e.includes("right") || e.includes("left"), n = e.includes("bottom") || e.includes("top"), i = e.includes("left"), o = e.includes("top");
  return {
    isHorizontal: t,
    isVertical: n,
    affectsX: i,
    affectsY: o
  };
}
function yt(e, t) {
  return Math.max(0, t - e);
}
function xt(e, t) {
  return Math.max(0, e - t);
}
function Vi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function jc(e, t) {
  return e ? !t : t;
}
function jv(e, t, n, i, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: m } = n, { minWidth: v, maxWidth: x, minHeight: y, maxHeight: j } = i, { x: g, y: w, width: S, height: b, aspectRatio: I } = e;
  let A = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? m - e.pointerY : 0);
  const M = S + (u ? -A : A), k = b + (l ? -D : D), R = -s[0] * S, L = -s[1] * b;
  let C = Vi(M, v, x), _ = Vi(k, y, j);
  if (a) {
    let T = 0, $ = 0;
    u && A < 0 ? T = yt(g + A + R, a[0][0]) : !u && A > 0 && (T = xt(g + M + R, a[1][0])), l && D < 0 ? $ = yt(w + D + L, a[0][1]) : !l && D > 0 && ($ = xt(w + k + L, a[1][1])), C = Math.max(C, T), _ = Math.max(_, $);
  }
  if (c) {
    let T = 0, $ = 0;
    u && A > 0 ? T = xt(g + A, c[0][0]) : !u && A < 0 && (T = yt(g + M, c[1][0])), l && D > 0 ? $ = xt(w + D, c[0][1]) : !l && D < 0 && ($ = yt(w + k, c[1][1])), C = Math.max(C, T), _ = Math.max(_, $);
  }
  if (o) {
    if (d) {
      const T = Vi(M / I, y, j) * I;
      if (C = Math.max(C, T), a) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = xt(w + L + M / I, a[1][1]) * I : $ = yt(w + L + (u ? A : -A) / I, a[0][1]) * I, C = Math.max(C, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || u && !l && p ? $ = yt(w + M / I, c[1][1]) * I : $ = xt(w + (u ? A : -A) / I, c[0][1]) * I, C = Math.max(C, $);
      }
    }
    if (f) {
      const T = Vi(k * I, v, x) / I;
      if (_ = Math.max(_, T), a) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = xt(g + k * I + R, a[1][0]) / I : $ = yt(g + (l ? D : -D) * I + R, a[0][0]) / I, _ = Math.max(_, $);
      }
      if (c) {
        let $ = 0;
        !u && !l || l && !u && p ? $ = yt(g + k * I, c[1][0]) / I : $ = xt(g + (l ? D : -D) * I, c[0][0]) / I, _ = Math.max(_, $);
      }
    }
  }
  D = D + (D < 0 ? _ : -_), A = A + (A < 0 ? C : -C), o && (p ? M > k * I ? D = (jc(u, l) ? -A : A) / I : A = (jc(u, l) ? -D : D) * I : d ? (D = A / I, l = u) : (A = D * I, u = l));
  const N = u ? g + A : g, E = l ? w + D : w;
  return {
    width: S + (u ? -A : A),
    height: b + (l ? -D : D),
    x: s[0] * A * (u ? -1 : 1) + N,
    y: s[1] * D * (l ? -1 : 1) + E
  };
}
const vd = { width: 0, height: 0, x: 0, y: 0 }, Nv = {
  ...vd,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function Sv(e, t, n) {
  const i = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, o - u],
    [i + s - c, o + a - u]
  ];
}
function Cv({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: o }) {
  const s = Fe(e);
  let a = {
    controlDirection: bc("bottom-right"),
    boundaries: {
      minWidth: 0,
      minHeight: 0,
      maxWidth: Number.MAX_VALUE,
      maxHeight: Number.MAX_VALUE
    },
    resizeDirection: void 0,
    keepAspectRatio: !1
  };
  function c({ controlPosition: l, boundaries: d, keepAspectRatio: f, resizeDirection: p, onResizeStart: h, onResize: m, onResizeEnd: v, shouldResize: x }) {
    let y = { ...vd }, j = { ...Nv };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: bc(l)
    };
    let g, w = null, S = [], b, I, A, D = !1;
    const M = Au().on("start", (k) => {
      const { nodeLookup: R, transform: L, snapGrid: C, snapToGrid: _, nodeOrigin: N, paneDomNode: E } = n();
      if (g = R.get(t), !g)
        return;
      w = E?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: $ } = Wn(k.sourceEvent, {
        transform: L,
        snapGrid: C,
        snapToGrid: _,
        containerBounds: w
      });
      y = {
        width: g.measured.width ?? 0,
        height: g.measured.height ?? 0,
        x: g.position.x ?? 0,
        y: g.position.y ?? 0
      }, j = {
        ...y,
        pointerX: T,
        pointerY: $,
        aspectRatio: y.width / y.height
      }, b = void 0, I = Bt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (b = R.get(g.parentId)), b && g.extent === "parent" && (I = [
        [0, 0],
        [b.measured.width, b.measured.height]
      ]), S = [], A = void 0;
      for (const [z, F] of R)
        if (F.parentId === t && (S.push({
          id: z,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const B = Sv(F, g, F.origin ?? N);
          A ? A = [
            [Math.min(B[0][0], A[0][0]), Math.min(B[0][1], A[0][1])],
            [Math.max(B[1][0], A[1][0]), Math.max(B[1][1], A[1][1])]
          ] : A = B;
        }
      h?.(k, { ...y });
    }).on("drag", (k) => {
      const { transform: R, snapGrid: L, snapToGrid: C, nodeOrigin: _ } = n(), N = Wn(k.sourceEvent, {
        transform: R,
        snapGrid: L,
        snapToGrid: C,
        containerBounds: w
      }), E = [];
      if (!g)
        return;
      const { x: T, y: $, width: z, height: F } = y, B = {}, Z = g.origin ?? _, { width: H, height: U, x: G, y: X } = jv(j, a.controlDirection, N, a.boundaries, a.keepAspectRatio, Z, I, A), P = H !== z, q = U !== F, de = G !== T && P, le = X !== $ && q;
      if (!de && !le && !P && !q)
        return;
      if ((de || le || Z[0] === 1 || Z[1] === 1) && (B.x = de ? G : y.x, B.y = le ? X : y.y, y.x = B.x, y.y = B.y, S.length > 0)) {
        const W = G - T, ne = X - $;
        for (const ge of S)
          ge.position = {
            x: ge.position.x - W + Z[0] * (H - z),
            y: ge.position.y - ne + Z[1] * (U - F)
          }, E.push(ge);
      }
      if ((P || q) && (B.width = P && (!a.resizeDirection || a.resizeDirection === "horizontal") ? H : y.width, B.height = q && (!a.resizeDirection || a.resizeDirection === "vertical") ? U : y.height, y.width = B.width, y.height = B.height), b && g.expandParent) {
        const W = Z[0] * (B.width ?? 0);
        B.x && B.x < W && (y.x = W, j.x = j.x - (B.x - W));
        const ne = Z[1] * (B.height ?? 0);
        B.y && B.y < ne && (y.y = ne, j.y = j.y - (B.y - ne));
      }
      const te = bv({
        width: y.width,
        prevWidth: z,
        height: y.height,
        prevHeight: F,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), re = { ...y, direction: te };
      x?.(k, re) !== !1 && (D = !0, m?.(k, re), i(B, E));
    }).on("end", (k) => {
      D && (v?.(k, { ...y }), o?.({ ...y }), D = !1);
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
var Eo = { exports: {} }, Ao = {}, _o = { exports: {} }, Do = {};
var Nc;
function Iv() {
  if (Nc) return Do;
  Nc = 1;
  var e = Ke;
  function t(f, p) {
    return f === p && (f !== 0 || 1 / f === 1 / p) || f !== f && p !== p;
  }
  var n = typeof Object.is == "function" ? Object.is : t, i = e.useState, o = e.useEffect, s = e.useLayoutEffect, a = e.useDebugValue;
  function c(f, p) {
    var h = p(), m = i({ inst: { value: h, getSnapshot: p } }), v = m[0].inst, x = m[1];
    return s(
      function() {
        v.value = h, v.getSnapshot = p, u(v) && x({ inst: v });
      },
      [f, h, p]
    ), o(
      function() {
        return u(v) && x({ inst: v }), f(function() {
          u(v) && x({ inst: v });
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
  return Do.useSyncExternalStore = e.useSyncExternalStore !== void 0 ? e.useSyncExternalStore : d, Do;
}
var Sc;
function kv() {
  return Sc || (Sc = 1, _o.exports = Iv()), _o.exports;
}
var Cc;
function Ev() {
  if (Cc) return Ao;
  Cc = 1;
  var e = Ke, t = kv();
  function n(l, d) {
    return l === d && (l !== 0 || 1 / l === 1 / d) || l !== l && d !== d;
  }
  var i = typeof Object.is == "function" ? Object.is : n, o = t.useSyncExternalStore, s = e.useRef, a = e.useEffect, c = e.useMemo, u = e.useDebugValue;
  return Ao.useSyncExternalStoreWithSelector = function(l, d, f, p, h) {
    var m = s(null);
    if (m.current === null) {
      var v = { hasValue: !1, value: null };
      m.current = v;
    } else v = m.current;
    m = c(
      function() {
        function y(b) {
          if (!j) {
            if (j = !0, g = b, b = p(b), h !== void 0 && v.hasValue) {
              var I = v.value;
              if (h(I, b))
                return w = I;
            }
            return w = b;
          }
          if (I = w, i(g, b)) return I;
          var A = p(b);
          return h !== void 0 && h(I, A) ? (g = b, I) : (g = b, w = A);
        }
        var j = !1, g, w, S = f === void 0 ? null : f;
        return [
          function() {
            return y(d());
          },
          S === null ? void 0 : function() {
            return y(S());
          }
        ];
      },
      [d, f, p, h]
    );
    var x = o(l, m[0], m[1]);
    return a(
      function() {
        v.hasValue = !0, v.value = x;
      },
      [x]
    ), u(x), x;
  }, Ao;
}
var Ic;
function Av() {
  return Ic || (Ic = 1, Eo.exports = Ev()), Eo.exports;
}
var _v = Av();
const Dv = /* @__PURE__ */ dp(_v), $v = {}, kc = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((m) => m(t, h));
    }
  }, o = () => t, u = { setState: i, getState: o, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    ($v ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, o, u);
  return u;
}, Tv = (e) => e ? kc(e) : kc, { useDebugValue: Rv } = Ke, { useSyncExternalStoreWithSelector: Pv } = Dv, Mv = (e) => e;
function bd(e, t = Mv, n) {
  const i = Pv(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Rv(i), i;
}
const Ec = (e, t) => {
  const n = Tv(e), i = (o, s = t) => bd(n, o, s);
  return Object.assign(i, n), i;
}, Lv = (e, t) => e ? Ec(e, t) : Ec;
function je(e, t) {
  if (Object.is(e, t))
    return !0;
  if (typeof e != "object" || e === null || typeof t != "object" || t === null)
    return !1;
  if (e instanceof Map && t instanceof Map) {
    if (e.size !== t.size) return !1;
    for (const [i, o] of e)
      if (!Object.is(o, t.get(i)))
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
var $o = { exports: {} }, Pe = {};
var Ac;
function zv() {
  if (Ac) return Pe;
  Ac = 1;
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
  }, o = /* @__PURE__ */ Symbol.for("react.portal");
  function s(u, l, d) {
    var f = 3 < arguments.length && arguments[3] !== void 0 ? arguments[3] : null;
    return {
      $$typeof: o,
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
var _c;
function Vv() {
  if (_c) return $o.exports;
  _c = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), $o.exports = zv(), $o.exports;
}
var Ov = Vv();
const Tr = cs(null), Hv = Tr.Provider, jd = Xe.error001("react");
function he(e, t) {
  const n = ii(Tr);
  if (n === null)
    throw new Error(jd);
  return bd(n, e, t);
}
function Ne() {
  const e = ii(Tr);
  if (e === null)
    throw new Error(jd);
  return ie(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const Dc = { display: "none" }, Wv = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, Nd = "react-flow__node-desc", Sd = "react-flow__edge-desc", Fv = "react-flow__aria-live", Bv = (e) => e.ariaLiveMessage, Kv = (e) => e.ariaLabelConfig;
function qv({ rfId: e }) {
  const t = he(Bv);
  return r.jsx("div", { id: `${Fv}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Wv, children: t });
}
function Xv({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Kv);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${Nd}-${e}`, style: Dc, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${Sd}-${e}`, style: Dc, children: n["edge.a11yDescription.default"] }), !t && r.jsx(qv, { rfId: e })] });
}
const Rr = El(({ position: e = "top-left", children: t, className: n, style: i, ...o }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ae(["react-flow__panel", n, ...a]), style: i, ref: s, ...o, children: t });
});
Rr.displayName = "Panel";
function Uv({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(Rr, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Yv = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, Oi = (e) => e.id;
function Zv(e, t) {
  return je(e.selectedNodes.map(Oi), t.selectedNodes.map(Oi)) && je(e.selectedEdges.map(Oi), t.selectedEdges.map(Oi));
}
function Jv({ onSelectionChange: e }) {
  const t = Ne(), { selectedNodes: n, selectedEdges: i } = he(Yv, Zv);
  return Q(() => {
    const o = { nodes: n, edges: i };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, i, e]), null;
}
const Gv = (e) => !!e.onSelectionChangeHandlers;
function Qv({ onSelectionChange: e }) {
  const t = he(Gv);
  return e || t ? r.jsx(Jv, { onSelectionChange: e }) : null;
}
const Cd = [0, 0], eb = { x: 0, y: 0, zoom: 1 }, tb = [
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
], $c = [...tb, "rfId"], nb = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), Tc = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Gn,
  nodeOrigin: Cd,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function ib(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(nb, je), l = Ne();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = Tc, c();
  }), []);
  const d = ae(Tc);
  return Q(
    () => {
      for (const f of $c) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? o(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: zw(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    $c.map((f) => e[f])
  ), null;
}
function Rc() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function rb(e) {
  const [t, n] = O(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Rc(), o = () => n(i?.matches ? "dark" : "light");
    return o(), i?.addEventListener("change", o), () => {
      i?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : Rc()?.matches ? "dark" : "light";
}
const Pc = typeof document < "u" ? document : null;
function ni(e = null, t = { target: Pc, actInsideInputWithModifier: !0 }) {
  const [n, i] = O(!1), o = ae(!1), s = ae(/* @__PURE__ */ new Set([])), [a, c] = ie(() => {
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
    const u = t?.target ?? Pc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && id(h))
          return !1;
        const v = Lc(h.code, c);
        if (s.current.add(h[v]), Mc(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, y = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !y) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const m = Lc(h.code, c);
        Mc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[m]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Mc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((o) => t.has(o)));
}
function Lc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const ob = () => {
  const e = Ne();
  return ie(() => ({
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
      const { transform: [i, o, s], panZoom: a } = e.getState();
      return a ? (await a.setViewport({
        x: t.x ?? i,
        y: t.y ?? o,
        zoom: t.zoom ?? s
      }, n), !0) : !1;
    },
    getViewport: () => {
      const [t, n, i] = e.getState().transform;
      return { x: t, y: n, zoom: i };
    },
    setCenter: async (t, n, i) => e.getState().setCenter(t, n, i),
    fitBounds: async (t, n) => {
      const { width: i, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = Ls(t, i, o, s, a, n?.padding ?? 0.1);
      return c ? (await c.setViewport(u, {
        duration: n?.duration,
        ease: n?.ease,
        interpolate: n?.interpolate
      }), !0) : !1;
    },
    screenToFlowPosition: (t, n = {}) => {
      const { transform: i, snapGrid: o, snapToGrid: s, domNode: a } = e.getState();
      if (!a)
        return t;
      const { x: c, y: u } = a.getBoundingClientRect(), l = {
        x: t.x - c,
        y: t.y - u
      }, d = n.snapGrid ?? o, f = n.snapToGrid ?? s;
      return wn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: o, y: s } = i.getBoundingClientRect(), a = pn(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function Id(e, t) {
  const n = [], i = /* @__PURE__ */ new Map(), o = [];
  for (const s of e)
    if (s.type === "add") {
      o.push(s);
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
      sb(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function sb(e, t) {
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
function kd(e, t) {
  return Id(e, t);
}
function Ed(e, t) {
  return Id(e, t);
}
function _t(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function tn(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(_t(s.id, a)));
  }
  return i;
}
function zc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    i.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function Vc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const Ad = Qu();
function _d(e, t, n = {}) {
  return Fw(e, t, {
    ...n,
    onError: n.onError ?? Ad
  });
}
function ab(e, t, n, i = { shouldReplaceId: !0 }) {
  return Bw(e, t, n, {
    ...i,
    onError: i.onError ?? Ad
  });
}
const Oc = (e) => Aw(e), cb = (e) => Yu(e);
function Dd(e) {
  return El(e);
}
const lb = typeof window < "u" ? qf : Q;
function Hc(e) {
  const [t, n] = O(BigInt(0)), [i] = O(() => ub(() => n((o) => o + BigInt(1))));
  return lb(() => {
    const o = i.get();
    o.length && (e(o), i.reset());
  }, [t]), i;
}
function ub(e) {
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
const $d = cs(null);
function db({ children: e }) {
  const t = Ne(), n = se((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: m } = t.getState();
    let v = u;
    for (const y of c)
      v = typeof y == "function" ? y(v) : y;
    let x = zc({
      items: v,
      lookup: p
    });
    for (const y of m.values())
      x = y(x);
    d && l(v), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: j, setNodes: g } = t.getState();
      y && g(j);
    });
  }, []), i = Hc(n), o = se((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const m of c)
      h = typeof m == "function" ? m(h) : m;
    d ? l(h) : f && f(zc({
      items: h,
      lookup: p
    }));
  }, []), s = Hc(o), a = ie(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return r.jsx($d.Provider, { value: a, children: e });
}
function fb() {
  const e = ii($d);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const pb = (e) => !!e.panZoom;
function Bs() {
  const e = ob(), t = Ne(), n = fb(), i = he(pb), o = ie(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), m = Oc(f) ? f : p.get(f.id), v = m.parentId ? td(m.position, m.measured, m.parentId, p, h) : m.position, x = {
        ...m,
        position: v,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return fn(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && Oc(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && cb(x) ? x : { ...v, ...x };
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
        const { nodes: f = [], edges: p = [], transform: h } = t.getState(), [m, v, x] = h;
        return {
          nodes: f.map((y) => ({ ...y })),
          edges: p.map((y) => ({ ...y })),
          viewport: {
            x: m,
            y: v,
            zoom: x
          }
        };
      },
      deleteElements: async ({ nodes: f = [], edges: p = [] }) => {
        const { nodes: h, edges: m, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: y, triggerEdgeChanges: j, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: S, edges: b } = await Rw({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: m,
          onBeforeDelete: w
        }), I = b.length > 0, A = S.length > 0;
        if (I) {
          const D = b.map(Vc);
          x?.(b), j(D);
        }
        if (A) {
          const D = S.map(Vc);
          v?.(S), y(D);
        }
        return (A || I) && g?.({ nodes: S, edges: b }), { deletedNodes: S, deletedEdges: b };
      },
      /**
       * Partial is defined as "the 2 nodes/areas are intersecting partially".
       * If a is contained in b or b is contained in a, they are both
       * considered fully intersecting.
       */
      getIntersectingNodes: (f, p = !0, h) => {
        const m = uc(f), v = m ? f : u(f), x = h !== void 0;
        return v ? (h || t.getState().nodes).filter((y) => {
          const j = t.getState().nodeLookup.get(y.id);
          if (j && !m && (y.id === f.id || !j.internals.positionAbsolute))
            return !1;
          const g = fn(x ? y : j), w = ei(g, v);
          return p && w > 0 || w >= g.width * g.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const v = uc(f) ? f : u(f);
        if (!v)
          return !1;
        const x = ei(v, p);
        return h && x > 0 || x >= p.width * p.height || x >= v.width * v.height;
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
        return _w(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? Lw();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return ie(() => ({
    ...o,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const Wc = (e) => e.selected, hb = typeof window < "u" ? window : void 0;
function gb({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = Ne(), { deleteElements: i } = Bs(), o = ni(e, { actInsideInputWithModifier: !1 }), s = ni(t, { target: hb });
  Q(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Wc), edges: a.filter(Wc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function mb(e) {
  const t = Ne();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = zs(e.current);
      (i.height === 0 || i.width === 0) && t.getState().onError?.("004", Xe.error004()), t.setState({ width: i.width || 500, height: i.height || 500 });
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
const Pr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, yb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function xb({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = Pt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: m, noWheelClassName: v, noPanClassName: x, onViewportChange: y, isControlledViewport: j, paneClickDistance: g, selectionOnDrag: w }) {
  const S = Ne(), b = ae(null), { userSelectionActive: I, lib: A, connectionInProgress: D } = he(yb, je), M = ni(p), k = ae();
  mb(b);
  const R = se((L) => {
    y?.({ x: L[0], y: L[1], zoom: L[2] }), j || S.setState({ transform: L });
  }, [y, j]);
  return Q(() => {
    if (b.current) {
      k.current = vv({
        domNode: b.current,
        minZoom: d,
        maxZoom: f,
        translateExtent: l,
        viewport: u,
        onDraggingChange: (N) => S.setState((E) => E.paneDragging === N ? E : { paneDragging: N }),
        onPanZoomStart: (N, E) => {
          const { onViewportChangeStart: T, onMoveStart: $ } = S.getState();
          $?.(N, E), T?.(E);
        },
        onPanZoom: (N, E) => {
          const { onViewportChange: T, onMove: $ } = S.getState();
          $?.(N, E), T?.(E);
        },
        onPanZoomEnd: (N, E) => {
          const { onViewportChangeEnd: T, onMoveEnd: $ } = S.getState();
          $?.(N, E), T?.(E);
        }
      });
      const { x: L, y: C, zoom: _ } = k.current.getViewport();
      return S.setState({
        panZoom: k.current,
        transform: [L, C, _],
        domNode: b.current.closest(".react-flow")
      }), () => {
        k.current?.destroy();
      };
    }
  }, []), Q(() => {
    k.current?.update({
      onPaneContextMenu: e,
      zoomOnScroll: t,
      zoomOnPinch: n,
      panOnScroll: i,
      panOnScrollSpeed: o,
      panOnScrollMode: s,
      zoomOnDoubleClick: a,
      panOnDrag: c,
      zoomActivationKeyPressed: M,
      preventScrolling: h,
      noPanClassName: x,
      userSelectionActive: I,
      noWheelClassName: v,
      lib: A,
      onTransformChange: R,
      connectionInProgress: D,
      selectionOnDrag: w,
      paneClickDistance: g
    });
  }, [
    e,
    t,
    n,
    i,
    o,
    s,
    a,
    c,
    M,
    h,
    x,
    I,
    v,
    A,
    R,
    D,
    w,
    g
  ]), r.jsx("div", { className: "react-flow__renderer", ref: b, style: Pr, children: m });
}
const wb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function vb() {
  const { userSelectionActive: e, userSelectionRect: t } = he(wb, je);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const To = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, bb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function jb({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Qn.Full, panOnDrag: i, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: m, children: v }) {
  const x = ae(0), y = Ne(), { userSelectionActive: j, elementsSelectable: g, dragging: w, connectionInProgress: S, panBy: b, autoPanSpeed: I } = he(bb, je), A = g && (e || j), D = ae(null), M = ae(), k = ae(/* @__PURE__ */ new Set()), R = ae(/* @__PURE__ */ new Set()), L = ae(!1), C = ae({ x: 0, y: 0 }), _ = ae(!1), N = (P) => {
    if (L.current || S) {
      L.current = !1;
      return;
    }
    l?.(P), y.getState().resetSelectedElements(), y.setState({ nodesSelectionActive: !1 });
  }, E = (P) => {
    if (Array.isArray(i) && i?.includes(2)) {
      P.preventDefault();
      return;
    }
    d?.(P);
  }, T = f ? (P) => f(P) : void 0, $ = (P) => {
    L.current && (P.stopPropagation(), L.current = !1);
  }, z = (P) => {
    const { domNode: q, transform: de } = y.getState();
    if (M.current = q?.getBoundingClientRect(), !M.current)
      return;
    const le = P.target === D.current;
    if (!le && !!P.target.closest(".nokey") || !e || !(a && le || t) || P.button !== 0 || !P.isPrimary)
      return;
    P.target?.setPointerCapture?.(P.pointerId), L.current = !1;
    const { x: fe, y: W } = et(P.nativeEvent, M.current), ne = wn({ x: fe, y: W }, de);
    y.setState({
      userSelectionRect: {
        width: 0,
        height: 0,
        startX: ne.x,
        startY: ne.y,
        x: fe,
        y: W
      }
    }), le || (P.stopPropagation(), P.preventDefault());
  };
  function F(P, q) {
    const { userSelectionRect: de } = y.getState();
    if (!de)
      return;
    const { transform: le, nodeLookup: te, edgeLookup: re, connectionLookup: fe, triggerNodeChanges: W, triggerEdgeChanges: ne, defaultEdgeOptions: ge } = y.getState(), ye = { x: de.startX, y: de.startY }, { x: Ee, y: Re } = pn(ye, le), Ve = {
      startX: ye.x,
      startY: ye.y,
      x: P < Ee ? P : Ee,
      y: q < Re ? q : Re,
      width: Math.abs(P - Ee),
      height: Math.abs(q - Re)
    }, ct = k.current, Ye = R.current;
    k.current = new Set(Ps(te, Ve, le, n === Qn.Partial, !0).map((Me) => Me.id)), R.current = /* @__PURE__ */ new Set();
    const Ze = ge?.selectable ?? !0;
    for (const Me of k.current) {
      const Oe = fe.get(Me);
      if (Oe)
        for (const { edgeId: we } of Oe.values()) {
          const Ue = re.get(we);
          Ue && (Ue.selectable ?? Ze) && R.current.add(we);
        }
    }
    if (!dc(ct, k.current)) {
      const Me = tn(te, k.current, !0);
      W(Me);
    }
    if (!dc(Ye, R.current)) {
      const Me = tn(re, R.current);
      ne(Me);
    }
    y.setState({
      userSelectionRect: Ve,
      userSelectionActive: !0,
      nodesSelectionActive: !1
    });
  }
  function B() {
    if (!o || !M.current)
      return;
    const [P, q] = Ms(C.current, M.current, I);
    b({ x: P, y: q }).then((de) => {
      if (!L.current || !de) {
        x.current = requestAnimationFrame(B);
        return;
      }
      const { x: le, y: te } = C.current;
      F(le, te), x.current = requestAnimationFrame(B);
    });
  }
  const Z = () => {
    cancelAnimationFrame(x.current), x.current = 0, _.current = !1;
  };
  Q(() => () => Z(), []);
  const H = (P) => {
    const { userSelectionRect: q, transform: de, resetSelectedElements: le } = y.getState();
    if (!M.current || !q)
      return;
    const { x: te, y: re } = et(P.nativeEvent, M.current);
    C.current = { x: te, y: re };
    const fe = pn({ x: q.startX, y: q.startY }, de);
    if (!L.current) {
      const W = t ? 0 : s;
      if (Math.hypot(te - fe.x, re - fe.y) <= W)
        return;
      le(), c?.(P);
    }
    L.current = !0, _.current || (B(), _.current = !0), F(te, re);
  }, U = (P) => {
    P.button === 0 && (P.target?.releasePointerCapture?.(P.pointerId), !j && P.target === D.current && y.getState().userSelectionRect && N?.(P), y.setState({
      userSelectionActive: !1,
      userSelectionRect: null
    }), L.current && (u?.(P), y.setState({
      nodesSelectionActive: k.current.size > 0
    })), Z());
  }, G = (P) => {
    P.target?.releasePointerCapture?.(P.pointerId), Z();
  }, X = i === !0 || Array.isArray(i) && i.includes(0);
  return r.jsxs("div", { className: Ae(["react-flow__pane", { draggable: X, dragging: w, selection: e }]), onClick: A ? void 0 : To(N, D), onContextMenu: To(E, D), onWheel: To(T, D), onPointerEnter: A ? void 0 : p, onPointerMove: A ? H : h, onPointerUp: A ? U : void 0, onPointerCancel: A ? G : void 0, onPointerDownCapture: A ? z : void 0, onClickCapture: A ? $ : void 0, onPointerLeave: m, ref: D, style: Pr, children: [v, r.jsx(vb, {})] });
}
function Qo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Xe.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : o([e]);
}
function Td({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = Ne(), [u, l] = O(!1), d = ae();
  return Q(() => {
    d.current = sv({
      getStoreItems: () => c.getState(),
      onNodeMouseDown: (f) => {
        Qo({
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
        nodeId: o,
        nodeClickDistance: a
      }), () => {
        d.current?.destroy();
      };
  }, [n, i, t, s, e, o, a]), u;
}
const Nb = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function Rd() {
  const e = Ne();
  return se((n) => {
    const { nodeExtent: i, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = Nb(a), h = o ? s[0] : 5, m = o ? s[1] : 5, v = n.direction.x * h * n.factor, x = n.direction.y * m * n.factor;
    for (const [, y] of l) {
      if (!p(y))
        continue;
      let j = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + x
      };
      o && (j = pi(j, s));
      const { position: g, positionAbsolute: w } = Zu({
        nodeId: y.id,
        nextPosition: j,
        nodeLookup: l,
        nodeExtent: i,
        nodeOrigin: d,
        onError: c
      });
      y.position = g, y.internals.positionAbsolute = w, f.set(y.id, y);
    }
    u(f);
  }, []);
}
const Ks = cs(null), Sb = Ks.Provider;
Ks.Consumer;
const Pd = () => ii(Ks), Cb = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), Ib = (e, t, n) => (i) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === un.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: d && l
  };
};
function kb({ type: e = "source", position: t = ce.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const m = a || null, v = e === "target", x = Ne(), y = Pd(), { connectOnClick: j, noPanClassName: g, rfId: w } = he(Cb, je), { connectingFrom: S, connectingTo: b, clickConnecting: I, isPossibleEndHandle: A, connectionInProcess: D, clickConnectionInProcess: M, valid: k } = he(Ib(y, m, e), je);
  y || x.getState().onError?.("010", Xe.error010());
  const R = (_) => {
    const { defaultEdgeOptions: N, onConnect: E, hasDefaultEdges: T } = x.getState(), $ = {
      ...N,
      ..._
    };
    if (T) {
      const { edges: z, setEdges: F, onError: B } = x.getState();
      F(_d($, z, { onError: B }));
    }
    E?.($), c?.($);
  }, L = (_) => {
    if (!y)
      return;
    const N = rd(_.nativeEvent);
    if (o && (N && _.button === 0 || !N)) {
      const E = x.getState();
      Go.onPointerDown(_.nativeEvent, {
        handleDomNode: _.currentTarget,
        autoPanOnConnect: E.autoPanOnConnect,
        connectionMode: E.connectionMode,
        connectionRadius: E.connectionRadius,
        domNode: E.domNode,
        nodeLookup: E.nodeLookup,
        lib: E.lib,
        isTarget: v,
        handleId: m,
        nodeId: y,
        flowId: E.rfId,
        panBy: E.panBy,
        cancelConnection: E.cancelConnection,
        onConnectStart: E.onConnectStart,
        onConnectEnd: (...T) => x.getState().onConnectEnd?.(...T),
        updateConnection: E.updateConnection,
        onConnect: R,
        isValidConnection: n || ((...T) => x.getState().isValidConnection?.(...T) ?? !0),
        getTransform: () => x.getState().transform,
        getFromHandle: () => x.getState().connection.fromHandle,
        autoPanSpeed: E.autoPanSpeed,
        dragThreshold: E.connectionDragThreshold
      });
    }
    N ? d?.(_) : f?.(_);
  }, C = (_) => {
    const { onClickConnectStart: N, onClickConnectEnd: E, connectionClickStartHandle: T, connectionMode: $, isValidConnection: z, lib: F, rfId: B, nodeLookup: Z, connection: H } = x.getState();
    if (!y || !T && !o)
      return;
    if (!T) {
      N?.(_.nativeEvent, { nodeId: y, handleId: m, handleType: e }), x.setState({ connectionClickStartHandle: { nodeId: y, type: e, id: m } });
      return;
    }
    const U = nd(_.target), G = n || z, { connection: X, isValid: P } = Go.isValid(_.nativeEvent, {
      handle: {
        nodeId: y,
        id: m,
        type: e
      },
      connectionMode: $,
      fromNodeId: T.nodeId,
      fromHandleId: T.id || null,
      fromType: T.type,
      isValidConnection: G,
      flowId: B,
      doc: U,
      lib: F,
      nodeLookup: Z
    });
    P && X && R(X);
    const q = structuredClone(H);
    delete q.inProgress, q.toPosition = q.toHandle ? q.toHandle.position : null, E?.(_, q), x.setState({ connectionClickStartHandle: null });
  };
  return r.jsx("div", { "data-handleid": m, "data-nodeid": y, "data-handlepos": t, "data-id": `${w}-${y}-${m}-${e}`, className: Ae([
    "react-flow__handle",
    `react-flow__handle-${t}`,
    "nodrag",
    g,
    l,
    {
      source: !v,
      target: v,
      connectable: i,
      connectablestart: o,
      connectableend: s,
      clickconnecting: I,
      connectingfrom: S,
      connectingto: b,
      valid: k,
      /*
       * shows where you can start a connection from
       * and where you can end it while connecting
       */
      connectionindicator: i && (!D || A) && (D || M ? s : o)
    }
  ]), onMouseDown: L, onTouchStart: L, onClick: j ? C : void 0, ref: h, ...p, children: u });
}
const gn = ke(Dd(kb));
function Eb({ data: e, isConnectable: t, sourcePosition: n = ce.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(gn, { type: "source", position: n, isConnectable: t })] });
}
function Ab({ data: e, isConnectable: t, targetPosition: n = ce.Top, sourcePosition: i = ce.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(gn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(gn, { type: "source", position: i, isConnectable: t })] });
}
function _b() {
  return null;
}
function Db({ data: e, isConnectable: t, targetPosition: n = ce.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(gn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const mr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Fc = {
  input: Eb,
  default: Ab,
  output: Db,
  group: _b
};
function $b(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Tb = (e) => {
  const { width: t, height: n, x: i, y: o } = fi(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Qe(t) ? t : null,
    height: Qe(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${o}px)`
  };
};
function Rb({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = Ne(), { width: o, height: s, transformString: a, userSelectionActive: c } = he(Tb, je), u = Rd(), l = ae(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && o !== null && s !== null;
  if (Td({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const m = i.getState().nodes.filter((v) => v.selected);
    e(h, m);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(mr, h.key) && (h.preventDefault(), u({
      direction: mr[h.key],
      factor: h.shiftKey ? 4 : 1
    }));
  };
  return r.jsx("div", { className: Ae(["react-flow__nodesselection", "react-flow__container", t]), style: {
    transform: a
  }, children: r.jsx("div", { ref: l, className: "react-flow__nodesselection-rect", onContextMenu: f, tabIndex: n ? void 0 : -1, onKeyDown: n ? void 0 : p, style: {
    width: o,
    height: s
  } }) });
}
const Bc = typeof window < "u" ? window : void 0, Pb = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Md({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: m, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: y, zoomOnScroll: j, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: S, panOnScrollMode: b, zoomOnDoubleClick: I, panOnDrag: A, autoPanOnSelection: D, defaultViewport: M, translateExtent: k, minZoom: R, maxZoom: L, preventScrolling: C, onSelectionContextMenu: _, noWheelClassName: N, noPanClassName: E, disableKeyboardA11y: T, onViewportChange: $, isControlledViewport: z }) {
  const { nodesSelectionActive: F, userSelectionActive: B } = he(Pb, je), Z = ni(l, { target: Bc }), H = ni(v, { target: Bc }), U = H || A, G = H || w, X = d && U !== !0, P = Z || B || X;
  return gb({ deleteKeyCode: u, multiSelectionKeyCode: m }), r.jsx(xb, { onPaneContextMenu: s, elementsSelectable: y, zoomOnScroll: j, zoomOnPinch: g, panOnScroll: G, panOnScrollSpeed: S, panOnScrollMode: b, zoomOnDoubleClick: I, panOnDrag: !Z && U, defaultViewport: M, translateExtent: k, minZoom: R, maxZoom: L, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: N, noPanClassName: E, onViewportChange: $, isControlledViewport: z, paneClickDistance: c, selectionOnDrag: X, children: r.jsxs(jb, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: U, autoPanOnSelection: D, isSelecting: !!P, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: X, children: [e, F && r.jsx(Rb, { onSelectionContextMenu: _, noPanClassName: E, disableKeyboardA11y: T })] }) });
}
Md.displayName = "FlowRenderer";
const Mb = ke(Md), Lb = (e) => (t) => e ? Ps(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function zb(e) {
  return he(se(Lb(e), [e]), je);
}
const Vb = (e) => e.updateNodeInternals;
function Ob() {
  const e = he(Vb), [t] = O(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
    const i = /* @__PURE__ */ new Map();
    n.forEach((o) => {
      const s = o.target.getAttribute("data-id");
      i.set(s, {
        id: s,
        nodeElement: o.target,
        force: !0
      });
    }), e(i);
  }));
  return Q(() => () => {
    t?.disconnect();
  }, [t]), t;
}
function Hb({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const o = Ne(), s = ae(null), a = ae(null), c = ae(e.sourcePosition), u = ae(e.targetPosition), l = ae(t), d = n && !!e.internals.handleBounds;
  return Q(() => {
    s.current && !e.hidden && (!d || a.current !== s.current) && (a.current && i?.unobserve(a.current), i?.observe(s.current), a.current = s.current);
  }, [d, e.hidden]), Q(() => () => {
    a.current && (i?.unobserve(a.current), a.current = null);
  }, []), Q(() => {
    if (s.current) {
      const f = l.current !== t, p = c.current !== e.sourcePosition, h = u.current !== e.targetPosition;
      (f || p || h) && (l.current = t, c.current = e.sourcePosition, u.current = e.targetPosition, o.getState().updateNodeInternals(/* @__PURE__ */ new Map([[e.id, { id: e.id, nodeElement: s.current, force: !0 }]])));
    }
  }, [e.id, t, e.sourcePosition, e.targetPosition]), s;
}
function Wb({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: m, rfId: v, nodeTypes: x, nodeClickDistance: y, onError: j }) {
  const { node: g, internals: w, isParent: S } = he((P) => {
    const q = P.nodeLookup.get(e), de = P.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: de
    };
  }, je);
  let b = g.type || "default", I = x?.[b] || Fc[b];
  I === void 0 && (j?.("003", Xe.error003(b)), b = "default", I = x?.default || Fc.default);
  const A = !!(g.draggable || c && typeof g.draggable > "u"), D = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), k = !!(g.focusable || d && typeof g.focusable > "u"), R = Ne(), L = ed(g), C = Hb({ node: g, nodeType: b, hasDimensions: L, resizeObserver: f }), _ = Td({
    nodeRef: C,
    disabled: g.hidden || !A,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: y
  }), N = Rd();
  if (g.hidden)
    return null;
  const E = mt(g), T = $b(g), $ = D || A || t || n || i || o, z = n ? (P) => n(P, { ...w.userNode }) : void 0, F = i ? (P) => i(P, { ...w.userNode }) : void 0, B = o ? (P) => o(P, { ...w.userNode }) : void 0, Z = s ? (P) => s(P, { ...w.userNode }) : void 0, H = a ? (P) => a(P, { ...w.userNode }) : void 0, U = (P) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: de } = R.getState();
    D && (!q || !A || de > 0) && Qo({
      id: e,
      store: R,
      nodeRef: C
    }), t && t(P, { ...w.userNode });
  }, G = (P) => {
    if (!(id(P.nativeEvent) || m)) {
      if (Ku.includes(P.key) && D) {
        const q = P.key === "Escape";
        Qo({
          id: e,
          store: R,
          unselect: q,
          nodeRef: C
        });
      } else if (A && g.selected && Object.prototype.hasOwnProperty.call(mr, P.key)) {
        P.preventDefault();
        const { ariaLabelConfig: q } = R.getState();
        R.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: P.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), N({
          direction: mr[P.key],
          factor: P.shiftKey ? 4 : 1
        });
      }
    }
  }, X = () => {
    if (m || !C.current?.matches(":focus-visible"))
      return;
    const { transform: P, width: q, height: de, autoPanOnNodeFocus: le, setCenter: te } = R.getState();
    if (!le)
      return;
    Ps(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: q, height: de }, P, !0).length > 0 || te(g.position.x + E.width / 2, g.position.y + E.height / 2, {
      zoom: P[2]
    });
  };
  return r.jsx("div", { className: Ae([
    "react-flow__node",
    `react-flow__node-${b}`,
    {
      // this is overwritable by passing `nopan` as a class name
      [h]: A
    },
    g.className,
    {
      selected: g.selected,
      selectable: D,
      parent: S,
      draggable: A,
      dragging: _
    }
  ]), ref: C, style: {
    zIndex: w.z,
    transform: `translate(${w.positionAbsolute.x}px,${w.positionAbsolute.y}px)`,
    pointerEvents: $ ? "all" : "none",
    visibility: L ? "visible" : "hidden",
    ...g.style,
    ...T
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: z, onMouseMove: F, onMouseLeave: B, onContextMenu: Z, onClick: U, onDoubleClick: H, onKeyDown: k ? G : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? X : void 0, role: g.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${Nd}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: r.jsx(Sb, { value: e, children: r.jsx(I, { id: e, data: g.data, type: b, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: D, draggable: A, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...E }) }) });
}
var Fb = ke(Wb);
const Bb = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Ld(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, onError: s } = he(Bb, je), a = zb(e.onlyRenderVisibleElements), c = Ob();
  return r.jsx("div", { className: "react-flow__nodes", style: Pr, children: a.map((u) => (
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
    r.jsx(Fb, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Ld.displayName = "NodeRenderer";
const Kb = ke(Ld);
function qb(e) {
  return he(se((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const i = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && Hw({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(o.id);
      }
    return i;
  }, [e]), je);
}
const Xb = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Ub = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Kc = {
  [pr.Arrow]: Xb,
  [pr.ArrowClosed]: Ub
};
function Yb(e) {
  const t = Ne();
  return ie(() => Object.prototype.hasOwnProperty.call(Kc, e) ? Kc[e] : (t.getState().onError?.("009", Xe.error009(e)), null), [e]);
}
const Zb = ({ id: e, type: t, color: n, width: i = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Yb(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, zd = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), o = ie(() => Yw(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return o.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: o.map((s) => r.jsx(Zb, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
zd.displayName = "MarkerDefinitions";
var Jb = ke(zd);
function Vd({ x: e, y: t, label: n, labelStyle: i, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = O({ x: 1, y: 0, width: 0, height: 0 }), h = Ae(["react-flow__edge-textwrapper", l]), m = ae(null);
  return Q(() => {
    if (m.current) {
      const v = m.current.getBBox();
      p({
        x: v.x,
        y: v.y,
        width: v.width,
        height: v.height
      });
    }
  }, [n]), n ? r.jsxs("g", { transform: `translate(${e - f.width / 2} ${t - f.height / 2})`, className: h, visibility: f.width ? "visible" : "hidden", ...d, children: [o && r.jsx("rect", { width: f.width + 2 * a[0], x: -a[0], y: -a[1], height: f.height + 2 * a[1], className: "react-flow__edge-textbg", style: s, rx: c, ry: c }), r.jsx("text", { className: "react-flow__edge-text", y: f.height / 2, dy: "0.3em", ref: m, style: i, children: n }), u] }) : null;
}
Vd.displayName = "EdgeText";
const Gb = ke(Vd);
function hi({ path: e, labelX: t, labelY: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Ae(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Qe(t) && Qe(n) ? r.jsx(Gb, { x: t, y: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function qc({ pos: e, x1: t, y1: n, x2: i, y2: o }) {
  return e === ce.Left || e === ce.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + o)];
}
function Od({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top }) {
  const [a, c] = qc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o
  }), [u, l] = qc({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t
  }), [d, f, p, h] = od({
    sourceX: e,
    sourceY: t,
    targetX: i,
    targetY: o,
    sourceControlX: a,
    sourceControlY: c,
    targetControlX: u,
    targetControlY: l
  });
  return [
    `M${e},${t} C${a},${c} ${u},${l} ${i},${o}`,
    d,
    f,
    p,
    h
  ];
}
function Hd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: y }) => {
    const [j, g, w] = Od({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), S = e.isInternal ? void 0 : t;
    return r.jsx(hi, { id: S, path: j, labelX: g, labelY: w, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: y });
  });
}
const Qb = Hd({ isInternal: !1 }), Wd = Hd({ isInternal: !0 });
Qb.displayName = "SimpleBezierEdge";
Wd.displayName = "SimpleBezierEdgeInternal";
function Fd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ce.Bottom, targetPosition: m = ce.Top, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: j }) => {
    const [g, w, S] = gr({
      sourceX: n,
      sourceY: i,
      sourcePosition: h,
      targetX: o,
      targetY: s,
      targetPosition: m,
      borderRadius: y?.borderRadius,
      offset: y?.offset,
      stepPosition: y?.stepPosition
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(hi, { id: b, path: g, labelX: w, labelY: S, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: v, markerStart: x, interactionWidth: j });
  });
}
const Bd = Fd({ isInternal: !1 }), Kd = Fd({ isInternal: !0 });
Bd.displayName = "SmoothStepEdge";
Kd.displayName = "SmoothStepEdgeInternal";
function qd(e) {
  return ke(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return r.jsx(Bd, { ...n, id: i, pathOptions: ie(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const e0 = qd({ isInternal: !1 }), Xd = qd({ isInternal: !0 });
e0.displayName = "StepEdge";
Xd.displayName = "StepEdgeInternal";
function Ud(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v }) => {
    const [x, y, j] = ld({ sourceX: n, sourceY: i, targetX: o, targetY: s }), g = e.isInternal ? void 0 : t;
    return r.jsx(hi, { id: g, path: x, labelX: y, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v });
  });
}
const t0 = Ud({ isInternal: !1 }), Yd = Ud({ isInternal: !0 });
t0.displayName = "StraightEdge";
Yd.displayName = "StraightEdgeInternal";
function Zd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a = ce.Bottom, targetPosition: c = ce.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: j }) => {
    const [g, w, S] = sd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: y?.curvature
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(hi, { id: b, path: g, labelX: w, labelY: S, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: j });
  });
}
const n0 = Zd({ isInternal: !1 }), Jd = Zd({ isInternal: !0 });
n0.displayName = "BezierEdge";
Jd.displayName = "BezierEdgeInternal";
const Xc = {
  default: Jd,
  straight: Yd,
  step: Xd,
  smoothstep: Kd,
  simplebezier: Wd
}, Uc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, i0 = (e, t, n) => n === ce.Left ? e - t : n === ce.Right ? e + t : e, r0 = (e, t, n) => n === ce.Top ? e - t : n === ce.Bottom ? e + t : e, Yc = "react-flow__edgeupdater";
function Zc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: Ae([Yc, `${Yc}-${c}`]), cx: i0(t, i, e), cy: r0(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function o0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const m = Ne(), v = (w, S) => {
    if (w.button !== 0)
      return;
    const { autoPanOnConnect: b, domNode: I, connectionMode: A, connectionRadius: D, lib: M, onConnectStart: k, cancelConnection: R, nodeLookup: L, rfId: C, panBy: _, updateConnection: N } = m.getState(), E = S.type === "target", T = (F, B) => {
      p(!1), f?.(F, n, S.type, B);
    }, $ = (F) => l?.(n, F), z = (F, B) => {
      p(!0), d?.(w, n, S.type), k?.(F, B);
    };
    Go.onPointerDown(w.nativeEvent, {
      autoPanOnConnect: b,
      connectionMode: A,
      connectionRadius: D,
      domNode: I,
      handleId: S.id,
      nodeId: S.nodeId,
      nodeLookup: L,
      isTarget: E,
      edgeUpdaterType: S.type,
      lib: M,
      flowId: C,
      cancelConnection: R,
      panBy: _,
      isValidConnection: (...F) => m.getState().isValidConnection?.(...F) ?? !0,
      onConnect: $,
      onConnectStart: z,
      onConnectEnd: (...F) => m.getState().onConnectEnd?.(...F),
      onReconnectEnd: T,
      updateConnection: N,
      getTransform: () => m.getState().transform,
      getFromHandle: () => m.getState().connection.fromHandle,
      dragThreshold: m.getState().connectionDragThreshold,
      handleDomNode: w.currentTarget
    });
  }, x = (w) => v(w, { nodeId: n.target, id: n.targetHandle ?? null, type: "target" }), y = (w) => v(w, { nodeId: n.source, id: n.sourceHandle ?? null, type: "source" }), j = () => h(!0), g = () => h(!1);
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Zc, { position: c, centerX: i, centerY: o, radius: t, onMouseDown: x, onMouseEnter: j, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && r.jsx(Zc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: y, onMouseEnter: j, onMouseOut: g, type: "target" })] });
}
function s0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: m, edgeTypes: v, noPanClassName: x, onError: y, disableKeyboardA11y: j }) {
  let g = he((te) => te.edgeLookup.get(e));
  const w = he((te) => te.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let S = g.type || "default", b = v?.[S] || Xc[S];
  b === void 0 && (y?.("011", Xe.error011(S)), S = "default", b = v?.default || Xc.default);
  const I = !!(g.focusable || t && typeof g.focusable > "u"), A = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), D = !!(g.selectable || i && typeof g.selectable > "u"), M = ae(null), [k, R] = O(!1), [L, C] = O(!1), _ = Ne(), { zIndex: N, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B } = he(se((te) => {
    const re = te.nodeLookup.get(g.source), fe = te.nodeLookup.get(g.target);
    if (!re || !fe)
      return {
        zIndex: g.zIndex,
        ...Uc
      };
    const W = Uw({
      id: e,
      sourceNode: re,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: y
    });
    return {
      zIndex: Ow({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: re,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...W || Uc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), je), Z = ie(() => g.markerStart ? `url('#${Zo(g.markerStart, m)}')` : void 0, [g.markerStart, m]), H = ie(() => g.markerEnd ? `url('#${Zo(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || E === null || T === null || $ === null || z === null)
    return null;
  const U = (te) => {
    const { addSelectedEdges: re, unselectNodesAndEdges: fe, multiSelectionActive: W } = _.getState();
    D && (_.setState({ nodesSelectionActive: !1 }), g.selected && W ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : re([e])), o && o(te, g);
  }, G = s ? (te) => {
    s(te, { ...g });
  } : void 0, X = a ? (te) => {
    a(te, { ...g });
  } : void 0, P = c ? (te) => {
    c(te, { ...g });
  } : void 0, q = u ? (te) => {
    u(te, { ...g });
  } : void 0, de = l ? (te) => {
    l(te, { ...g });
  } : void 0, le = (te) => {
    if (!j && Ku.includes(te.key) && D) {
      const { unselectNodesAndEdges: re, addSelectedEdges: fe } = _.getState();
      te.key === "Escape" ? (M.current?.blur(), re({ edges: [g] })) : fe([e]);
    }
  };
  return r.jsx("svg", { style: { zIndex: N }, children: r.jsxs("g", { className: Ae([
    "react-flow__edge",
    `react-flow__edge-${S}`,
    g.className,
    x,
    {
      selected: g.selected,
      animated: g.animated,
      inactive: !D && !o,
      updating: k,
      selectable: D
    }
  ]), onClick: U, onDoubleClick: G, onContextMenu: X, onMouseEnter: P, onMouseMove: q, onMouseLeave: de, onKeyDown: I ? le : void 0, tabIndex: I ? 0 : void 0, role: g.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": I ? `${Sd}-${m}` : void 0, ref: M, ...g.domAttributes, children: [!L && r.jsx(b, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: D, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Z, markerEnd: H, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), A && r.jsx(o0, { edge: g, isReconnectable: A, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B, setUpdateHover: R, setReconnecting: C })] }) });
}
var a0 = ke(s0);
const c0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Gd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: y, elementsSelectable: j, onError: g } = he(c0, je), w = qb(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(Jb, { defaultColor: e, rfId: n }), w.map((S) => r.jsx(a0, { id: S, edgesFocusable: x, edgesReconnectable: y, elementsSelectable: j, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: v }, S))] });
}
Gd.displayName = "EdgeRenderer";
const l0 = ke(Gd), u0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function d0({ children: e }) {
  const t = he(u0);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function f0(e) {
  const t = Bs(), n = ae(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const p0 = (e) => e.panZoom?.syncViewport;
function h0(e) {
  const t = he(p0), n = Ne();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function g0(e) {
  return e.connection.inProgress ? { ...e.connection, to: wn(e.connection.to, e.transform) } : { ...e.connection };
}
function m0(e) {
  return g0;
}
function y0(e) {
  const t = m0();
  return he(t, je);
}
const x0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function w0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = he(x0, je);
  return !(s && o && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ae(["react-flow__connection", Uu(c)]), children: r.jsx(Qd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Qd = ({ style: e, type: t = wt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = y0();
  if (!o)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: Uu(i), toNode: d, toHandle: f, pointer: h });
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
    case wt.Bezier:
      [m] = sd(v);
      break;
    case wt.SimpleBezier:
      [m] = Od(v);
      break;
    case wt.Step:
      [m] = gr({
        ...v,
        borderRadius: 0
      });
      break;
    case wt.SmoothStep:
      [m] = gr(v);
      break;
    default:
      [m] = ld(v);
  }
  return r.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Qd.displayName = "ConnectionLine";
const v0 = {};
function Jc(e = v0) {
  ae(e), Ne(), Q(() => {
  }, [e]);
}
function b0() {
  Ne(), ae(!1), Q(() => {
  }, []);
}
function ef({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: m, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: y, selectionKeyCode: j, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: S, panActivationKeyCode: b, zoomActivationKeyCode: I, deleteKeyCode: A, onlyRenderVisibleElements: D, elementsSelectable: M, defaultViewport: k, translateExtent: R, minZoom: L, maxZoom: C, preventScrolling: _, defaultMarkerColor: N, zoomOnScroll: E, zoomOnPinch: T, panOnScroll: $, panOnScrollSpeed: z, panOnScrollMode: F, zoomOnDoubleClick: B, panOnDrag: Z, autoPanOnSelection: H, onPaneClick: U, onPaneMouseEnter: G, onPaneMouseMove: X, onPaneMouseLeave: P, onPaneScroll: q, onPaneContextMenu: de, paneClickDistance: le, nodeClickDistance: te, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ne, reconnectRadius: ge, onReconnect: ye, onReconnectStart: Ee, onReconnectEnd: Re, noDragClassName: Ve, noWheelClassName: ct, noPanClassName: Ye, disableKeyboardA11y: Ze, nodeExtent: Me, rfId: Oe, viewport: we, onViewportChange: Ue }) {
  return Jc(e), Jc(t), b0(), f0(n), h0(we), r.jsx(Mb, { onPaneClick: U, onPaneMouseEnter: G, onPaneMouseMove: X, onPaneMouseLeave: P, onPaneContextMenu: de, onPaneScroll: q, paneClickDistance: le, deleteKeyCode: A, selectionKeyCode: j, selectionOnDrag: g, selectionMode: w, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: S, panActivationKeyCode: b, zoomActivationKeyCode: I, elementsSelectable: M, zoomOnScroll: E, zoomOnPinch: T, zoomOnDoubleClick: B, panOnScroll: $, panOnScrollSpeed: z, panOnScrollMode: F, panOnDrag: Z, autoPanOnSelection: H, defaultViewport: k, translateExtent: R, minZoom: L, maxZoom: C, onSelectionContextMenu: f, preventScrolling: _, noDragClassName: Ve, noWheelClassName: ct, noPanClassName: Ye, disableKeyboardA11y: Ze, onViewportChange: Ue, isControlledViewport: !!we, children: r.jsxs(d0, { children: [r.jsx(l0, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: Ee, onReconnectEnd: Re, onlyRenderVisibleElements: D, onEdgeContextMenu: re, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ne, reconnectRadius: ge, defaultMarkerColor: N, noPanClassName: Ye, disableKeyboardA11y: Ze, rfId: Oe }), r.jsx(w0, { style: v, type: m, component: x, containerStyle: y }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Kb, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: te, onlyRenderVisibleElements: D, noPanClassName: Ye, noDragClassName: Ve, disableKeyboardA11y: Ze, nodeExtent: Me, rfId: Oe }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
ef.displayName = "GraphView";
const j0 = ke(ef), N0 = Qu(), Gc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), y = i ?? t ?? [], j = n ?? e ?? [], g = d ?? [0, 0], w = f ?? Gn;
  fd(v, x, y);
  const { nodesInitialized: S } = Jo(j, h, m, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: p
  });
  let b = [0, 0, 1];
  if (a && o && s) {
    const I = fi(h, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: A, y: D, zoom: M } = Ls(I, o, s, u, l, c?.padding ?? 0.1);
    b = [A, D, M];
  }
  return {
    rfId: "1",
    width: o ?? 0,
    height: s ?? 0,
    transform: b,
    nodes: j,
    nodesInitialized: S,
    nodeLookup: h,
    parentLookup: m,
    edges: y,
    edgeLookup: x,
    connectionLookup: v,
    onNodesChange: null,
    onEdgesChange: null,
    hasDefaultNodes: n !== void 0,
    hasDefaultEdges: i !== void 0,
    panZoom: null,
    minZoom: u,
    maxZoom: l,
    translateExtent: Gn,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: un.Strict,
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
    connection: { ...Xu },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: N0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: qu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, S0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => Lv((h, m) => {
  async function v() {
    const { nodeLookup: x, panZoom: y, fitViewOptions: j, fitViewResolver: g, width: w, height: S, minZoom: b, maxZoom: I } = m();
    y && (await Tw({
      nodes: x,
      width: w,
      height: S,
      panZoom: y,
      minZoom: b,
      maxZoom: I
    }, j), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Gc({
      nodes: e,
      edges: t,
      width: o,
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
      const { nodeLookup: y, parentLookup: j, nodeOrigin: g, elevateNodesOnSelect: w, fitViewQueued: S, zIndexMode: b, nodesSelectionActive: I } = m(), { nodesInitialized: A, hasSelectedNodes: D } = Jo(x, y, j, {
        nodeOrigin: g,
        nodeExtent: f,
        elevateNodesOnSelect: w,
        checkEquality: !0,
        zIndexMode: b
      }), M = I && D;
      S && A ? (v(), h({
        nodes: x,
        nodesInitialized: A,
        fitViewQueued: !1,
        fitViewOptions: void 0,
        nodesSelectionActive: M
      })) : h({ nodes: x, nodesInitialized: A, nodesSelectionActive: M });
    },
    setEdges: (x) => {
      const { connectionLookup: y, edgeLookup: j } = m();
      fd(y, j, x), h({ edges: x });
    },
    setDefaultNodesAndEdges: (x, y) => {
      if (x) {
        const { setNodes: j } = m();
        j(x), h({ hasDefaultNodes: !0 });
      }
      if (y) {
        const { setEdges: j } = m();
        j(y), h({ hasDefaultEdges: !0 });
      }
    },
    /*
     * Every node gets registered at a ResizeObserver. Whenever a node
     * changes its dimensions, this function is called to measure the
     * new dimensions and update the nodes.
     */
    updateNodeInternals: (x) => {
      const { triggerNodeChanges: y, nodeLookup: j, parentLookup: g, domNode: w, nodeOrigin: S, nodeExtent: b, debug: I, fitViewQueued: A, zIndexMode: D } = m(), { changes: M, updatedInternals: k } = nv(x, j, g, w, S, b, D);
      k && (Gw(j, g, { nodeOrigin: S, nodeExtent: b, zIndexMode: D }), A ? (v(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (I && console.log("React Flow: trigger node changes", M), y?.(M)));
    },
    updateNodePositions: (x, y = !1) => {
      const j = [];
      let g = [];
      const { nodeLookup: w, triggerNodeChanges: S, connection: b, updateConnection: I, onNodesChangeMiddlewareMap: A } = m();
      for (const [D, M] of x) {
        const k = w.get(D), R = !!(k?.expandParent && k?.parentId && M?.position), L = {
          id: D,
          type: "position",
          position: R ? {
            x: Math.max(0, M.position.x),
            y: Math.max(0, M.position.y)
          } : M.position,
          dragging: y
        };
        if (k && b.inProgress && b.fromNode.id === k.id) {
          const C = Kt(k, b.fromHandle, ce.Left, !0);
          I({ ...b, from: C });
        }
        R && k.parentId && j.push({
          id: D,
          parentId: k.parentId,
          rect: {
            ...M.internals.positionAbsolute,
            width: M.measured.width ?? 0,
            height: M.measured.height ?? 0
          }
        }), g.push(L);
      }
      if (j.length > 0) {
        const { parentLookup: D, nodeOrigin: M } = m(), k = Fs(j, w, D, M);
        g.push(...k);
      }
      for (const D of A.values())
        g = D(g);
      S(g);
    },
    triggerNodeChanges: (x) => {
      const { onNodesChange: y, setNodes: j, nodes: g, hasDefaultNodes: w, debug: S } = m();
      if (x?.length) {
        if (w) {
          const b = kd(x, g);
          j(b);
        }
        S && console.log("React Flow: trigger node changes", x), y?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: y, setEdges: j, edges: g, hasDefaultEdges: w, debug: S } = m();
      if (x?.length) {
        if (w) {
          const b = Ed(x, g);
          j(b);
        }
        S && console.log("React Flow: trigger edge changes", x), y?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: y, edgeLookup: j, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: S } = m();
      if (y) {
        const b = x.map((I) => _t(I, !0));
        w(b);
        return;
      }
      w(tn(g, /* @__PURE__ */ new Set([...x]), !0)), S(tn(j));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: y, edgeLookup: j, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: S } = m();
      if (y) {
        const b = x.map((I) => _t(I, !0));
        S(b);
        return;
      }
      S(tn(j, /* @__PURE__ */ new Set([...x]))), w(tn(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: y } = {}) => {
      const { edges: j, nodes: g, nodeLookup: w, triggerNodeChanges: S, triggerEdgeChanges: b } = m(), I = x || g, A = y || j, D = [];
      for (const k of I) {
        if (!k.selected)
          continue;
        const R = w.get(k.id);
        R && (R.selected = !1), D.push(_t(k.id, !1));
      }
      const M = [];
      for (const k of A)
        k.selected && M.push(_t(k.id, !1));
      S(D), b(M);
    },
    setMinZoom: (x) => {
      const { panZoom: y, maxZoom: j } = m();
      y?.setScaleExtent([x, j]), h({ minZoom: x });
    },
    setMaxZoom: (x) => {
      const { panZoom: y, minZoom: j } = m();
      y?.setScaleExtent([j, x]), h({ maxZoom: x });
    },
    setTranslateExtent: (x) => {
      m().panZoom?.setTranslateExtent(x), h({ translateExtent: x });
    },
    resetSelectedElements: () => {
      const { edges: x, nodes: y, triggerNodeChanges: j, triggerEdgeChanges: g, elementsSelectable: w } = m();
      if (!w)
        return;
      const S = y.reduce((I, A) => A.selected ? [...I, _t(A.id, !1)] : I, []), b = x.reduce((I, A) => A.selected ? [...I, _t(A.id, !1)] : I, []);
      j(S), g(b);
    },
    setNodeExtent: (x) => {
      const { nodes: y, nodeLookup: j, parentLookup: g, nodeOrigin: w, elevateNodesOnSelect: S, nodeExtent: b, zIndexMode: I } = m();
      x[0][0] === b[0][0] && x[0][1] === b[0][1] && x[1][0] === b[1][0] && x[1][1] === b[1][1] || (Jo(y, j, g, {
        nodeOrigin: w,
        nodeExtent: x,
        elevateNodesOnSelect: S,
        checkEquality: !1,
        zIndexMode: I
      }), h({ nodeExtent: x }));
    },
    panBy: (x) => {
      const { transform: y, width: j, height: g, panZoom: w, translateExtent: S } = m();
      return iv({ delta: x, panZoom: w, transform: y, translateExtent: S, width: j, height: g });
    },
    setCenter: async (x, y, j) => {
      const { width: g, height: w, maxZoom: S, panZoom: b } = m();
      if (!b)
        return !1;
      const I = typeof j?.zoom < "u" ? j.zoom : S;
      return await b.setViewport({
        x: g / 2 - x * I,
        y: w / 2 - y * I,
        zoom: I
      }, { duration: j?.duration, ease: j?.ease, interpolate: j?.interpolate }), !0;
    },
    cancelConnection: () => {
      h({
        connection: { ...Xu }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...Gc() })
  };
}, Object.is);
function C0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [m] = O(() => S0({
    nodes: e,
    edges: t,
    defaultNodes: n,
    defaultEdges: i,
    width: o,
    height: s,
    fitView: l,
    minZoom: a,
    maxZoom: c,
    fitViewOptions: u,
    nodeOrigin: d,
    nodeExtent: f,
    zIndexMode: p
  }));
  return r.jsx(Hv, { value: m, children: r.jsx(db, { children: h }) });
}
function I0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return ii(Tr) ? r.jsx(r.Fragment, { children: e }) : r.jsx(C0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const k0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function E0({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, onNodeMouseEnter: j, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: S, onNodeDoubleClick: b, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: D, onNodesDelete: M, onEdgesDelete: k, onDelete: R, onSelectionChange: L, onSelectionDragStart: C, onSelectionDrag: _, onSelectionDragStop: N, onSelectionContextMenu: E, onSelectionStart: T, onSelectionEnd: $, onBeforeDelete: z, connectionMode: F, connectionLineType: B = wt.Bezier, connectionLineStyle: Z, connectionLineComponent: H, connectionLineContainerStyle: U, deleteKeyCode: G = "Backspace", selectionKeyCode: X = "Shift", selectionOnDrag: P = !1, selectionMode: q = Qn.Full, panActivationKeyCode: de = "Space", multiSelectionKeyCode: le = ti() ? "Meta" : "Control", zoomActivationKeyCode: te = ti() ? "Meta" : "Control", snapToGrid: re, snapGrid: fe, onlyRenderVisibleElements: W = !1, selectNodesOnDrag: ne, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ee, nodesFocusable: Re, nodeOrigin: Ve = Cd, edgesFocusable: ct, edgesReconnectable: Ye, elementsSelectable: Ze = !0, defaultViewport: Me = eb, minZoom: Oe = 0.5, maxZoom: we = 2, translateExtent: Ue = Gn, preventScrolling: _e = !0, nodeExtent: Et, defaultMarkerColor: bn = "#b1b1b7", zoomOnScroll: V = !0, zoomOnPinch: K = !0, panOnScroll: Y = !1, panOnScrollSpeed: oe = 0.5, panOnScrollMode: ee = Pt.Free, zoomOnDoubleClick: ve = !0, panOnDrag: pe = !0, onPaneClick: be, onPaneMouseEnter: Se, onPaneMouseMove: He, onPaneMouseLeave: De, onPaneScroll: mi, onPaneContextMenu: We, paneClickDistance: Ut = 1, nodeClickDistance: At = 0, children: Lr, onReconnect: jn, onReconnectStart: zr, onReconnectEnd: Vr, onEdgeContextMenu: lt, onEdgeDoubleClick: Or, onEdgeMouseEnter: Yt, onEdgeMouseMove: Hr, onEdgeMouseLeave: Zt, reconnectRadius: Wr = 10, onNodesChange: yi, onEdgesChange: xi, noDragClassName: Fr = "nodrag", noWheelClassName: Jt = "nowheel", noPanClassName: wi = "nopan", fitView: vi, fitViewOptions: bi, connectOnClick: Nn, attributionPosition: Br, proOptions: Kr, defaultEdgeOptions: qr, elevateNodesOnSelect: Xr = !0, elevateEdgesOnSelect: Ur = !1, disableKeyboardA11y: ji = !1, autoPanOnConnect: Yr, autoPanOnNodeDrag: Zr, autoPanOnSelection: Jr = !0, autoPanSpeed: Gr, connectionRadius: Qr, isValidConnection: eo, onError: to, style: no, id: Sn, nodeDragThreshold: Ni, connectionDragThreshold: Si, viewport: Ci, onViewportChange: io, width: Ii, height: ro, colorMode: oo = "light", debug: so, onScroll: ki, ariaLabelConfig: ao, zIndexMode: Ei = "basic", ...Ai }, co) {
  const Cn = Sn || "1", lo = rb(oo), uo = se((_i) => {
    _i.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), ki?.(_i);
  }, [ki]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...Ai, onScroll: uo, style: { ...no, ...k0 }, ref: co, className: Ae(["react-flow", o, lo]), id: Sn, role: "application", children: r.jsxs(I0, { nodes: e, edges: t, width: Ii, height: ro, fitView: vi, fitViewOptions: bi, minZoom: Oe, maxZoom: we, nodeOrigin: Ve, nodeExtent: Et, zIndexMode: Ei, children: [r.jsx(ib, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ee, nodesFocusable: Re, edgesFocusable: ct, edgesReconnectable: Ye, elementsSelectable: Ze, elevateNodesOnSelect: Xr, elevateEdgesOnSelect: Ur, minZoom: Oe, maxZoom: we, nodeExtent: Et, onNodesChange: yi, onEdgesChange: xi, snapToGrid: re, snapGrid: fe, connectionMode: F, translateExtent: Ue, connectOnClick: Nn, defaultEdgeOptions: qr, fitView: vi, fitViewOptions: bi, onNodesDelete: M, onEdgesDelete: k, onDelete: R, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: D, onSelectionDrag: _, onSelectionDragStart: C, onSelectionDragStop: N, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: wi, nodeOrigin: Ve, rfId: Cn, autoPanOnConnect: Yr, autoPanOnNodeDrag: Zr, autoPanSpeed: Gr, onError: to, connectionRadius: Qr, isValidConnection: eo, selectNodesOnDrag: ne, nodeDragThreshold: Ni, connectionDragThreshold: Si, onBeforeDelete: z, debug: so, ariaLabelConfig: ao, zIndexMode: Ei }), r.jsx(j0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: j, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: S, onNodeDoubleClick: b, nodeTypes: s, edgeTypes: a, connectionLineType: B, connectionLineStyle: Z, connectionLineComponent: H, connectionLineContainerStyle: U, selectionKeyCode: X, selectionOnDrag: P, selectionMode: q, deleteKeyCode: G, multiSelectionKeyCode: le, panActivationKeyCode: de, zoomActivationKeyCode: te, onlyRenderVisibleElements: W, defaultViewport: Me, translateExtent: Ue, minZoom: Oe, maxZoom: we, preventScrolling: _e, zoomOnScroll: V, zoomOnPinch: K, zoomOnDoubleClick: ve, panOnScroll: Y, panOnScrollSpeed: oe, panOnScrollMode: ee, panOnDrag: pe, autoPanOnSelection: Jr, onPaneClick: be, onPaneMouseEnter: Se, onPaneMouseMove: He, onPaneMouseLeave: De, onPaneScroll: mi, onPaneContextMenu: We, paneClickDistance: Ut, nodeClickDistance: At, onSelectionContextMenu: E, onSelectionStart: T, onSelectionEnd: $, onReconnect: jn, onReconnectStart: zr, onReconnectEnd: Vr, onEdgeContextMenu: lt, onEdgeDoubleClick: Or, onEdgeMouseEnter: Yt, onEdgeMouseMove: Hr, onEdgeMouseLeave: Zt, reconnectRadius: Wr, defaultMarkerColor: bn, noDragClassName: Fr, noWheelClassName: Jt, noPanClassName: wi, rfId: Cn, disableKeyboardA11y: ji, nodeExtent: Et, viewport: Ci, onViewportChange: io }), r.jsx(Qv, { onSelectionChange: L }), Lr, r.jsx(Uv, { proOptions: Kr, position: Br }), r.jsx(Xv, { rfId: Cn, disableKeyboardA11y: ji })] }) });
}
var qs = Dd(E0);
const A0 = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function _0({ children: e }) {
  const t = he(A0);
  return t ? Ov.createPortal(e, t) : null;
}
function D0({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ae(["react-flow__background-pattern", n, i]) });
}
function $0({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ae(["react-flow__background-pattern", "dots", t]) });
}
var jt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(jt || (jt = {}));
const T0 = {
  [jt.Dots]: 1,
  [jt.Lines]: 1,
  [jt.Cross]: 6
}, R0 = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function tf({
  id: e,
  variant: t = jt.Dots,
  // only used for dots and cross
  gap: n = 20,
  // only used for lines and cross
  size: i,
  lineWidth: o = 1,
  offset: s = 0,
  color: a,
  bgColor: c,
  style: u,
  className: l,
  patternClassName: d
}) {
  const f = ae(null), { transform: p, patternId: h } = he(R0, je), m = i || T0[t], v = t === jt.Dots, x = t === jt.Cross, y = Array.isArray(n) ? n : [n, n], j = [y[0] * p[2] || 1, y[1] * p[2] || 1], g = m * p[2], w = Array.isArray(s) ? s : [s, s], S = x ? [g, g] : j, b = [
    w[0] * p[2] || 1 + S[0] / 2,
    w[1] * p[2] || 1 + S[1] / 2
  ], I = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Ae(["react-flow__background", l]), style: {
    ...u,
    ...Pr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: I, x: p[0] % j[0], y: p[1] % j[1], width: j[0], height: j[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${b[0]},-${b[1]})`, children: v ? r.jsx($0, { radius: g / 2, className: d }) : r.jsx(D0, { dimensions: S, lineWidth: o, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
tf.displayName = "Background";
const Xs = ke(tf);
function P0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function M0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function L0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function z0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function V0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Hi({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ae(["react-flow__controls-button", t]), ...n, children: e });
}
const O0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function nf({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const m = Ne(), { isInteractive: v, minZoomReached: x, maxZoomReached: y, ariaLabelConfig: j } = he(O0, je), { zoomIn: g, zoomOut: w, fitView: S } = Bs(), b = () => {
    g(), s?.();
  }, I = () => {
    w(), a?.();
  }, A = () => {
    S(o), c?.();
  }, D = () => {
    m.setState({
      nodesDraggable: !v,
      nodesConnectable: !v,
      elementsSelectable: !v
    }), u?.(!v);
  }, M = p === "horizontal" ? "horizontal" : "vertical";
  return r.jsxs(Rr, { className: Ae(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? j["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(Hi, { onClick: b, className: "react-flow__controls-zoomin", title: j["controls.zoomIn.ariaLabel"], "aria-label": j["controls.zoomIn.ariaLabel"], disabled: y, children: r.jsx(P0, {}) }), r.jsx(Hi, { onClick: I, className: "react-flow__controls-zoomout", title: j["controls.zoomOut.ariaLabel"], "aria-label": j["controls.zoomOut.ariaLabel"], disabled: x, children: r.jsx(M0, {}) })] }), n && r.jsx(Hi, { className: "react-flow__controls-fitview", onClick: A, title: j["controls.fitView.ariaLabel"], "aria-label": j["controls.fitView.ariaLabel"], children: r.jsx(L0, {}) }), i && r.jsx(Hi, { className: "react-flow__controls-interactive", onClick: D, title: j["controls.interactive.ariaLabel"], "aria-label": j["controls.interactive.ariaLabel"], children: v ? r.jsx(V0, {}) : r.jsx(z0, {}) }), d] });
}
nf.displayName = "Controls";
const Us = ke(nf);
function H0({ id: e, x: t, y: n, width: i, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: m, backgroundColor: v } = s || {}, x = a || m || v;
  return r.jsx("rect", { className: Ae(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: o, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (y) => h(y, e) : void 0 });
}
const W0 = ke(H0), F0 = (e) => e.nodes.map((t) => t.id), Ro = (e) => e instanceof Function ? e : () => e;
function B0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = W0,
  onClick: a
}) {
  const c = he(F0, je), u = Ro(t), l = Ro(e), d = Ro(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(q0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function K0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
  const { node: l, x: d, y: f, width: p, height: h } = he((m) => {
    const v = m.nodeLookup.get(e);
    if (!v)
      return { node: void 0, x: 0, y: 0, width: 0, height: 0 };
    const x = v.internals.userNode, { x: y, y: j } = v.internals.positionAbsolute, { width: g, height: w } = mt(x);
    return {
      node: x,
      x: y,
      y: j,
      width: g,
      height: w
    };
  }, je);
  return !l || l.hidden || !ed(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const q0 = ke(K0);
var X0 = ke(B0);
const U0 = 200, Y0 = 150, Z0 = (e) => !e.hidden, J0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Gu(fi(e.nodeLookup, { filter: Z0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, G0 = "react-flow__minimap-desc";
function rf({
  style: e,
  className: t,
  nodeStrokeColor: n,
  nodeColor: i,
  nodeClassName: o = "",
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
  zoomable: x = !1,
  ariaLabel: y,
  inversePan: j,
  zoomStep: g = 1,
  offsetScale: w = 5
}) {
  const S = Ne(), b = ae(null), { boundingRect: I, viewBB: A, rfId: D, panZoom: M, translateExtent: k, flowWidth: R, flowHeight: L, ariaLabelConfig: C } = he(J0, je), _ = e?.width ?? U0, N = e?.height ?? Y0, E = I.width / _, T = I.height / N, $ = Math.max(E, T), z = $ * _, F = $ * N, B = w * $, Z = I.x - (z - I.width) / 2 - B, H = I.y - (F - I.height) / 2 - B, U = z + B * 2, G = F + B * 2, X = `${G0}-${D}`, P = ae(0), q = ae();
  P.current = $, Q(() => {
    if (b.current && M)
      return q.current = fv({
        domNode: b.current,
        panZoom: M,
        getTransform: () => S.getState().transform,
        getViewScale: () => P.current
      }), () => {
        q.current?.destroy();
      };
  }, [M]), Q(() => {
    q.current?.update({
      translateExtent: k,
      width: R,
      height: L,
      inversePan: j,
      pannable: v,
      zoomStep: g,
      zoomable: x
    });
  }, [v, x, j, g, k, R, L]);
  const de = h ? (re) => {
    const [fe, W] = q.current?.pointer(re) || [0, 0];
    h(re, { x: fe, y: W });
  } : void 0, le = m ? se((re, fe) => {
    const W = S.getState().nodeLookup.get(fe).internals.userNode;
    m(re, W);
  }, []) : void 0, te = y ?? C["minimap.ariaLabel"];
  return r.jsx(Rr, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ae(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: _, height: N, viewBox: `${Z} ${H} ${U} ${G}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": X, ref: b, onClick: de, children: [te && r.jsx("title", { id: X, children: te }), r.jsx(X0, { onClick: le, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - B},${H - B}h${U + B * 2}v${G + B * 2}h${-U - B * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
rf.displayName = "MiniMap";
const Ys = ke(rf), Q0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, ej = {
  [hn.Line]: "right",
  [hn.Handle]: "bottom-right"
};
function tj({ nodeId: e, position: t, variant: n = hn.Handle, className: i, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: m, onResizeStart: v, onResize: x, onResizeEnd: y }) {
  const j = Pd(), g = typeof e == "string" ? e : j, w = Ne(), S = ae(null), b = n === hn.Handle, I = he(se(Q0(b && h), [b, h]), je), A = ae(null), D = t ?? ej[n];
  Q(() => {
    if (!(!S.current || !g))
      return A.current || (A.current = Cv({
        domNode: S.current,
        nodeId: g,
        getStoreItems: () => {
          const { nodeLookup: k, transform: R, snapGrid: L, snapToGrid: C, nodeOrigin: _, domNode: N } = w.getState();
          return {
            nodeLookup: k,
            transform: R,
            snapGrid: L,
            snapToGrid: C,
            nodeOrigin: _,
            paneDomNode: N
          };
        },
        onChange: (k, R) => {
          const { triggerNodeChanges: L, nodeLookup: C, parentLookup: _, nodeOrigin: N } = w.getState(), E = [], T = { x: k.x, y: k.y }, $ = C.get(g);
          if ($ && $.expandParent && $.parentId) {
            const z = $.origin ?? N, F = k.width ?? $.measured.width ?? 0, B = k.height ?? $.measured.height ?? 0, Z = {
              id: $.id,
              parentId: $.parentId,
              rect: {
                width: F,
                height: B,
                ...td({
                  x: k.x ?? $.position.x,
                  y: k.y ?? $.position.y
                }, { width: F, height: B }, $.parentId, C, z)
              }
            }, H = Fs([Z], C, _, N);
            E.push(...H), T.x = k.x ? Math.max(z[0] * F, k.x) : void 0, T.y = k.y ? Math.max(z[1] * B, k.y) : void 0;
          }
          if (T.x !== void 0 && T.y !== void 0) {
            const z = {
              id: g,
              type: "position",
              position: { ...T }
            };
            E.push(z);
          }
          if (k.width !== void 0 && k.height !== void 0) {
            const F = {
              id: g,
              type: "dimensions",
              resizing: !0,
              setAttributes: p ? p === "horizontal" ? "width" : "height" : !0,
              dimensions: {
                width: k.width,
                height: k.height
              }
            };
            E.push(F);
          }
          for (const z of R) {
            const F = {
              ...z,
              type: "position"
            };
            E.push(F);
          }
          L(E);
        },
        onEnd: ({ width: k, height: R }) => {
          const L = {
            id: g,
            type: "dimensions",
            resizing: !1,
            dimensions: {
              width: k,
              height: R
            }
          };
          w.getState().triggerNodeChanges([L]);
        }
      })), A.current.update({
        controlPosition: D,
        boundaries: {
          minWidth: c,
          minHeight: u,
          maxWidth: l,
          maxHeight: d
        },
        keepAspectRatio: f,
        resizeDirection: p,
        onResizeStart: v,
        onResize: x,
        onResizeEnd: y,
        shouldResize: m
      }), () => {
        A.current?.destroy();
      };
  }, [
    D,
    c,
    u,
    l,
    d,
    f,
    v,
    x,
    y,
    m
  ]);
  const M = D.split("-");
  return r.jsx("div", { className: Ae(["react-flow__resize-control", "nodrag", ...M, n, i]), ref: S, style: {
    ...o,
    scale: I,
    ...a && { [b ? "backgroundColor" : "borderColor"]: a }
  }, children: s });
}
ke(tj);
function nj(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: si(e.state),
    layout: e.layout
  };
}
function ij(e) {
  return JSON.stringify(
    {
      state: si(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function rj(e, t) {
  let n;
  try {
    n = JSON.parse(e);
  } catch (o) {
    return { ok: !1, error: o instanceof Error ? o.message : String(o) };
  }
  if (!n || typeof n != "object")
    return { ok: !1, error: "Workflow JSON must be an object with a 'state' property." };
  const i = n;
  return !i.state || typeof i.state != "object" ? { ok: !1, error: "Workflow JSON is missing a valid 'state' object." } : i.layout !== void 0 && !Array.isArray(i.layout) ? { ok: !1, error: "'layout' must be an array when present." } : {
    ok: !0,
    draft: {
      ...t,
      state: br(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function oj(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(i), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
function Qc({
  document: e,
  readOnly: t,
  minHeight: n,
  ariaLabel: i,
  onChange: o
}) {
  const s = (a) => {
    t || o({ ...e, value: a.target.value });
  };
  return /* @__PURE__ */ r.jsx(
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
function sj({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: o = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = ie(
    () => d ? Xf(d) : null,
    [d]
  );
  return /* @__PURE__ */ r.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": i,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ r.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ r.jsx("span", { children: l }),
          /* @__PURE__ */ r.jsx("code", { children: e.uri })
        ] }),
        f ? /* @__PURE__ */ r.jsx(Uf, { fallback: /* @__PURE__ */ r.jsx(
          Qc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ), children: /* @__PURE__ */ r.jsx(
          f,
          {
            document: e,
            readOnly: n,
            theme: i,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ) }) : /* @__PURE__ */ r.jsx(
          Qc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(aj, { diagnostics: u })
      ]
    }
  );
}
function aj({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", o = cj(t);
    return /* @__PURE__ */ r.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${i}`,
        children: [
          t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
          o ? /* @__PURE__ */ r.jsx("small", { children: o }) : null,
          t.message
        ]
      },
      `${t.uri ?? "document"}-${t.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function cj(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const lj = { language: "json", displayName: "JSON" };
function uj({ draft: e, onApply: t }) {
  const n = ie(() => ij(e), [e]), [i, o] = O(n), [s, a] = O(n), [c, u] = O(null);
  Q(() => {
    o(n), a(n), u(null);
  }, [n]);
  const l = i !== s, d = c ? [{ severity: "error", message: c }] : [], f = () => u(t(i));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-code-view-toolbar", children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-muted", children: "Edit the workflow definition as JSON. Changes apply to the draft when you click Apply." }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-code-view-actions", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", disabled: !l, onClick: () => {
          o(s), u(null);
        }, children: "Reset" }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: !l, onClick: f, children: [
          /* @__PURE__ */ r.jsx(Xt, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      sj,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: lj,
        diagnostics: d,
        minHeight: "100%",
        theme: "studio",
        onChange: (p) => {
          o(p.value), c && u(null);
        }
      }
    ) })
  ] });
}
const dj = ["Single", "Array", "List", "HashSet"];
function of(e) {
  const [t, n] = O(null), [i, o] = O(null);
  Q(() => {
    let u = !1;
    return ng(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), rg(e).then(
      (l) => {
        u || o(l);
      },
      () => {
        u || o([]);
      }
    ), () => {
      u = !0;
    };
  }, [e]);
  const s = ie(
    () => t && t.length > 0 ? t.map((u) => {
      const l = Na(u);
      return {
        value: l,
        label: zl(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ie(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Ep(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = ie(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = Na(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function fj(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function pj(e, t, n) {
  return {
    add: () => {
      const i = xp(n.namePrefix, e.map((o) => dt(o, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, o) => t(e.map((s, a) => a === i ? n.patch(s, o) : s)),
    remove: (i) => t(e.filter((o, s) => s !== i))
  };
}
function el({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: o, onChange: s }) {
  if (!t)
    return /* @__PURE__ */ r.jsx(
      "input",
      {
        type: "text",
        "aria-label": o,
        value: e,
        placeholder: n,
        onChange: (l) => s(l.target.value)
      }
    );
  const a = e === "" || t.some((l) => l.value === e), c = Array.from(new Set(t.map((l) => l.group).filter((l) => !!l))), u = c.length > 0;
  return /* @__PURE__ */ r.jsxs("select", { "aria-label": o, value: e, onChange: (l) => s(l.target.value), children: [
    i ? /* @__PURE__ */ r.jsx("option", { value: "", children: n ?? "—" }) : null,
    a ? null : /* @__PURE__ */ r.jsxs("option", { value: e, disabled: !0, children: [
      e,
      " (unresolved)"
    ] }),
    u ? c.map((l) => /* @__PURE__ */ r.jsx("optgroup", { label: l, children: t.filter((d) => d.group === l).map((d) => /* @__PURE__ */ r.jsx("option", { value: d.value, children: d.label }, d.value)) }, l)) : t.map((l) => /* @__PURE__ */ r.jsx("option", { value: l.value, children: l.label }, l.value))
  ] });
}
const hj = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function gj({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: dj.map((i) => /* @__PURE__ */ r.jsx("option", { value: i, children: hj[i] }, i)) });
}
function mj(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function yj({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const o = mj(t, e);
  return o && t === "checkbox" ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: "checkbox",
      "aria-label": n,
      checked: e === "true" || e === "True",
      onChange: (s) => i(s.target.checked ? "true" : "false")
    }
  ) : o && (t === "number" || t === "date") ? /* @__PURE__ */ r.jsx(
    "input",
    {
      type: t,
      "aria-label": n,
      value: e,
      placeholder: "(empty)",
      onChange: (s) => i(s.target.value)
    }
  ) : /* @__PURE__ */ r.jsx(
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
function xj({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(on, { size: 14 }),
        " ",
        t
      ] })
    ] }),
    o ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: n }) : /* @__PURE__ */ r.jsxs("table", { className: "wf-properties-table", children: [
      /* @__PURE__ */ r.jsx("thead", { children: /* @__PURE__ */ r.jsxs("tr", { children: [
        i.map((c) => /* @__PURE__ */ r.jsx("th", { children: c }, c)),
        /* @__PURE__ */ r.jsx("th", { "aria-label": "Actions" })
      ] }) }),
      /* @__PURE__ */ r.jsx("tbody", { children: a })
    ] })
  ] });
}
function wj({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(Lt, { size: 14 }) }) });
}
function vj({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Zs({
  items: e,
  typeOptions: t,
  storageOptions: n,
  editorForAlias: i,
  namePrefix: o,
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
  const { add: m, update: v, remove: x } = pj(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (g) => l(g, fj(t)),
    patch: d
  }), y = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], j = o.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    xj,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: y,
      isEmpty: e.length === 0,
      onAdd: m,
      children: e.map((g, w) => {
        const S = dt(g, s), b = gs(g), I = dt(g, Vl), A = I ? p?.get(I) : void 0, D = b.collectionKind === "Single" ? i(b.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${o} name`, value: S, onChange: (M) => v(w, { name: M.target.value }) }),
            A ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: A, children: A }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            el,
            {
              ariaLabel: `${o} type`,
              value: b.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => v(w, { type: { alias: M, collectionKind: b.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            gj,
            {
              ariaLabel: `${o} collection kind`,
              value: b.collectionKind,
              onChange: (M) => v(w, { type: { alias: b.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            yj,
            {
              ariaLabel: `${o} default value`,
              value: jp(g.default),
              editor: D,
              onChange: (M) => v(w, { default: bp(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            el,
            {
              ariaLabel: `${o} storage driver`,
              value: dt(g, _p),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => v(w, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            vj,
            {
              ariaLabel: `${o} required`,
              checked: g.isRequired === !0,
              onChange: (M) => v(w, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(wj, { label: `Remove ${j} ${S || w + 1}`, onRemove: () => x(w) })
        ] }, w);
      })
    }
  );
}
function sf({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    Zs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Ap,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => wp({ name: l, alias: d }),
      patch: (l, d) => vp(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function bj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Zs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Input",
      nameKeys: ms,
      title: "Inputs",
      addLabel: "Add input",
      emptyLabel: "No inputs defined.",
      create: (s, a) => Np({ name: s, alias: a }),
      patch: (s, a) => Sp(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function jj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Zs,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Output",
      nameKeys: ms,
      title: "Outputs",
      addLabel: "Add output",
      emptyLabel: "No outputs defined.",
      create: (s, a) => Cp({ name: s, alias: a }),
      patch: (s, a) => Ip(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function Gi(e) {
  return (e ?? []).filter(Kn);
}
function Nj({ context: e, variables: t, title: n, addLabel: i, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = of(e);
  return /* @__PURE__ */ r.jsx(
    sf,
    {
      items: Gi(t),
      typeOptions: c,
      storageOptions: u,
      editorForAlias: l,
      title: n,
      addLabel: i,
      emptyLabel: o,
      warnings: s,
      onChange: a
    }
  );
}
function Sj({ definition: e, definitionId: t, onMetaChange: n }) {
  const i = !!n, [o, s] = O(e?.name ?? ""), [a, c] = O(e?.description ?? "");
  Q(() => {
    s(e?.name ?? "");
  }, [e?.name]), Q(() => {
    c(e?.description ?? "");
  }, [e?.description]);
  const u = () => {
    const d = o.trim();
    d && d !== (e?.name ?? "") ? n?.({ name: d }) : d || s(e?.name ?? "");
  }, l = () => {
    a !== (e?.description ?? "") && n?.({ description: a });
  };
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsx("h3", { children: "Information" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-properties-info", children: [
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-name", children: "Name" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: i ? /* @__PURE__ */ r.jsx(
        "input",
        {
          id: "wf-def-name",
          type: "text",
          "aria-label": "Workflow name",
          value: o,
          onChange: (d) => s(d.target.value),
          onBlur: u
        }
      ) : e?.name ?? "—" }),
      /* @__PURE__ */ r.jsx("dt", { children: /* @__PURE__ */ r.jsx("label", { htmlFor: "wf-def-description", children: "Description" }) }),
      /* @__PURE__ */ r.jsx("dd", { children: i ? /* @__PURE__ */ r.jsx(
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
function Cj({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = of(n), u = Gi(t.state.variables), l = Gi(t.state.inputs), d = Gi(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      Sj,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ r.jsx(
      sf,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      bj,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      jj,
      {
        items: d,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, outputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
      /* @__PURE__ */ r.jsx("h3", { children: "Versions" }),
      f.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published versions yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-properties-versions", children: f.map((p) => /* @__PURE__ */ r.jsxs("li", { children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-properties-version-tag", children: [
          "v",
          p.version
        ] }),
        /* @__PURE__ */ r.jsx("time", { children: $e(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const tl = "application/x-elsa-activity-version-id", Ij = 6, kj = 1200, Ej = 250, Aj = [10, 25, 50], _j = 10, nl = "elsa-studio-workflow-palette-width", il = "elsa-studio-workflow-inspector-width", rl = "elsa-studio-workflow-palette-collapsed", ol = "elsa-studio-workflow-inspector-collapsed", af = "elsa-studio-workflow-side-panel-maximized", Mn = 180, Ln = 460, Dj = 260, $t = 260, Tt = 560, $j = 320, sl = 42, Wi = 16, cf = Ke.createContext(null), lf = Ke.createContext(null), uf = Ke.createContext(null);
function Tj(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function df(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function St(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function Ct(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function Rj(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = al(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return al(o, s);
}
function al(e, t) {
  const n = typeof e == "string" ? cl(e) : void 0, i = typeof t == "string" ? cl(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function cl(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function Pj(e, t) {
  return e.rootActivityVersionId ?? ff(t, e.rootKind)?.activityVersionId ?? null;
}
function ff(e, t) {
  return e.find((n) => Mj(n) === t);
}
function Mj(e) {
  return e ? Lj(e) ? "flowchart" : zj(e) ? "sequence" : null : null;
}
function es(e) {
  return ru(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Ie(n).localeCompare(Ie(i)))
  }));
}
function Lj(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function zj(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Vj(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function pf(e) {
  return Xj(e.rootActivityType) || e.rootActivityType;
}
function Oj(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function hf(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function Hj(e, t) {
  return ll(t) - ll(e);
}
function Wj(e, t, n) {
  return e ? t.find(
    (i) => i.artifactId === e && hf(i, n) && !!i.publishedAt
  ) ?? null : null;
}
function ll(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Js(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Gs(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
function Qs(e) {
  const t = e instanceof Error ? e.message : String(e), n = (typeof e == "object" && e && "payload" in e ? e.payload : null) ?? Fj(t), i = n && typeof n == "object" && typeof n.error == "string" ? n.error : null;
  return i ? /no live .* reference/i.test(i) ? /expired/i.test(i) ? `${i} Start a new test run from the definition editor to mint a fresh reference.` : `${i} Restore the executable or republish its definition to run it.` : i : t;
}
function Fj(e) {
  try {
    return JSON.parse(e);
  } catch {
    return null;
  }
}
function gf(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return t === "published" ? "Published" : t === "testrun" || t === "test-run" ? "Test run" : e?.trim() || "Unknown";
}
function Bj(e, t = /* @__PURE__ */ new Date()) {
  return e.deletedAt ? "retired" : e.expiresAt && new Date(e.expiresAt).getTime() <= t.getTime() ? "expired" : "live";
}
function Kj(e, t) {
  return t ? !t.latestVersionId || t.latestVersionId === e.definitionVersionId ? { kind: "current" } : {
    kind: "behind",
    referenceVersion: e.sourceVersion ?? e.artifactVersion ?? null,
    latestVersion: t.latestVersion ?? null
  } : { kind: "absent" };
}
async function qj(e) {
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
function Xj(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Uj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Fi(t, n.typeName, n), Fi(t, n.name, n), Fi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Fi(t, i, n);
  }
  return t;
}
function ul(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(zn(i?.activityTypeKey)) ?? n.get(zn(Ot(i?.activityTypeKey))) ?? n.get(zn(i?.displayName)) ?? n.get(zn(e.activityVersionId)) ?? null;
}
function Fi(e, t, n) {
  const i = zn(t);
  i && !e.has(i) && e.set(i, n);
}
function zn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function dl(e, t, n, i) {
  const o = Mr();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Qi(a, n, i) : t;
}
function fl(e, t) {
  const n = Mr();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Yj() {
  const e = Mr();
  if (!e) return null;
  const t = e.getItem(af);
  return t === "palette" || t === "inspector" ? t : null;
}
function Mr() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function _n(e, t) {
  const n = Mr();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Qi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function mf(e) {
  switch (Zj(e)) {
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
function Zj(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function Jj(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function ts(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function pl(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Gj(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function yf(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Qj(e) {
  const t = yf(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function eN(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function Be(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function tN(e) {
  return vf(Be(e));
}
function nN(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ie(n) : Ot(e.activityVersionId) ?? e.activityVersionId;
}
function iN(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? nN(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function xf(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ie(i) : void 0
  });
  for (const o of Te(e, t))
    for (const s of o.activities) xf(s, t, n);
  return n;
}
function wf(e, t, n = []) {
  if (!e) return n;
  for (const i of Zl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Te(e, t))
    for (const o of i.activities) wf(o, t, n);
  return n;
}
function Vn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function rN(e) {
  return `${e.id}-${vf(JSON.stringify(e.state))}`;
}
function vf(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function ea(e) {
  return e.status.toLowerCase() === "rejected";
}
function oN(e) {
  try {
    const t = JSON.parse(e);
    if (typeof t.error == "string") return t.error;
  } catch {
  }
  return e;
}
function sN(e, t) {
  const n = e instanceof Error ? e.message : String(e);
  return aN(e, n) ? `Run ${t} was not found.` : n;
}
function aN(e, t) {
  if ((typeof e == "object" && e ? e.response?.status ?? e.status : void 0) === 404 || /\b404\b/.test(t)) return !0;
  try {
    const i = JSON.parse(t);
    return [i.error, i.title, i.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
function vn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const ta = { workflowActivity: cN }, na = { workflow: uN };
function cN({ id: e, data: t, selected: n }) {
  const i = t, o = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = lN(i), l = Ke.useContext(lf)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Ke.useContext(uf), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : "", i.ghost ? "wf-node-ghost" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ r.jsx(gn, { type: "target", position: ce.Left }) : null,
        l ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${qn(l.state)}`, children: /* @__PURE__ */ r.jsx(Bn, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: Sr(i.icon) }),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-node-copy", children: [
            /* @__PURE__ */ r.jsx("strong", { children: i.label }),
            i.ghost ? /* @__PURE__ */ r.jsx("small", { className: "wf-node-ghost-note", children: "Not available in this environment" }) : c ? /* @__PURE__ */ r.jsx("small", { children: c }) : null
          ] })
        ] }),
        i.childSlots.length > 0 ? f ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-slot-list", children: i.childSlots.map((p) => /* @__PURE__ */ r.jsx(
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
        )) }) : /* @__PURE__ */ r.jsxs("span", { className: "wf-node-slot-badge", children: [
          i.childSlots.length,
          " slot",
          i.childSlots.length === 1 ? "" : "s"
        ] }) : null,
        o ? /* @__PURE__ */ r.jsxs("div", { className: "wf-node-runtime-strip", children: [
          o.status ? /* @__PURE__ */ r.jsx(vn, { status: o.status, subStatus: o.subStatus }) : null,
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
        a.map((p, h) => {
          const m = `${(h + 1) / (a.length + 1) * 100}%`;
          return /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: m }, children: p.displayName }),
            /* @__PURE__ */ r.jsx(gn, { type: "source", position: ce.Right, id: p.name, style: { top: m } })
          ] }, p.name);
        })
      ]
    }
  );
}
function lN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function uN(e) {
  const {
    id: t,
    sourceX: n,
    sourceY: i,
    targetX: o,
    targetY: s,
    sourcePosition: a,
    targetPosition: c,
    markerEnd: u,
    style: l,
    label: d,
    labelStyle: f
  } = e, p = Ke.useContext(cf), [h, m] = O(!1), [v, x, y] = gr({ sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), j = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      hi,
      {
        id: t,
        path: v,
        markerEnd: u,
        style: {
          ...l,
          strokeWidth: j ? 2.5 : l?.strokeWidth
        },
        label: d,
        labelX: x,
        labelY: y,
        labelStyle: f,
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1)
      }
    ),
    p ? /* @__PURE__ */ r.jsx(_0, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", j ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ r.jsx(on, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(Lt, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function bf({ clientX: e, clientY: t, activities: n, onPick: i, onClose: o }) {
  const [s, a] = O(""), [c, u] = O(0), l = ae(null), d = ae(null), f = ie(() => {
    const j = s.trim().toLowerCase(), g = n.filter(Vj);
    return j ? g.filter((w) => Ie(w).toLowerCase().includes(j) || w.activityTypeKey.toLowerCase().includes(j) || (w.category ?? "").toLowerCase().includes(j) || (w.description ?? "").toLowerCase().includes(j)) : g;
  }, [n, s]), p = ie(() => es(f), [f]), h = ie(() => p.flatMap((j) => j.activities), [p]);
  Q(() => {
    requestAnimationFrame(() => d.current?.focus());
  }, []), Q(() => {
    const j = (w) => {
      l.current?.contains(w.target) || o();
    }, g = (w) => {
      w.key === "Escape" && o();
    };
    return document.addEventListener("mousedown", j, !0), document.addEventListener("keydown", g), () => {
      document.removeEventListener("mousedown", j, !0), document.removeEventListener("keydown", g);
    };
  }, [o]);
  const m = (j) => {
    if (j.key === "ArrowDown")
      j.preventDefault(), u((g) => Math.min(g + 1, h.length - 1));
    else if (j.key === "ArrowUp")
      j.preventDefault(), u((g) => Math.max(g - 1, 0));
    else if (j.key === "Enter") {
      j.preventDefault();
      const g = h[c];
      g && i(g);
    }
  }, v = Math.max(8, Math.min(e + 4, window.innerWidth - 328)), x = Math.max(8, Math.min(t + 4, window.innerHeight - 360));
  let y = -1;
  return /* @__PURE__ */ r.jsxs("div", { ref: l, className: "wf-connect-menu", style: { left: v, top: x }, onMouseDown: (j) => j.stopPropagation(), onClick: (j) => j.stopPropagation(), children: [
    /* @__PURE__ */ r.jsx(
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
        onKeyDown: m
      }
    ),
    /* @__PURE__ */ r.jsx("div", { className: "wf-connect-menu-list", role: "listbox", "aria-label": "Activity picker", children: p.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No matching activities." }) : p.map((j) => /* @__PURE__ */ r.jsxs("section", { children: [
      /* @__PURE__ */ r.jsx("h4", { children: j.category }),
      j.activities.map((g) => {
        y += 1;
        const w = y, S = w === c;
        return /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            role: "option",
            "aria-selected": S,
            className: S ? "active" : "",
            onMouseEnter: () => u(w),
            onClick: () => i(g),
            children: [
              /* @__PURE__ */ r.jsx("strong", { children: Ie(g) }),
              /* @__PURE__ */ r.jsx("small", { children: g.category || g.activityTypeKey })
            ]
          },
          g.activityVersionId
        );
      })
    ] }, j.category)) })
  ] });
}
function Fn({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const o = up(t.map((s) => s.id), n, i);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-panel-tab-list", role: "tablist", "aria-label": e, onKeyDown: o, children: t.map((s) => {
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
        onClick: () => i(s.id),
        children: [
          s.icon ? /* @__PURE__ */ r.jsx("span", { className: "wf-panel-tab-icon", "aria-hidden": "true", children: s.icon }) : null,
          /* @__PURE__ */ r.jsx("span", { children: s.title })
        ]
      },
      s.id
    );
  }) });
}
function hl(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function ia({ frames: e, onNavigate: t, className: n }) {
  return /* @__PURE__ */ r.jsxs("div", { className: n ? `wf-breadcrumb ${n}` : "wf-breadcrumb", children: [
    /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t([]), children: "Root" }),
    e.map((i, o) => i.label ? /* @__PURE__ */ r.jsxs(Ke.Fragment, { children: [
      /* @__PURE__ */ r.jsx(pt, { size: 13 }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(e.slice(0, o + 1)), children: i.label })
    ] }, `${i.ownerNodeId}-${i.slotId}-${o}`) : null)
  ] });
}
const dN = "Expressions/UnresolvedVariable";
function fN(e) {
  return String(e.type ?? e.code ?? "");
}
function pN(e) {
  return fN(e) === dN;
}
function hN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function gN(e) {
  return (e ?? []).filter(pN).map((t) => ({
    error: t,
    path: hN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function mN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(Xt, { size: 14 }),
      " No validation errors"
    ] });
  const i = gN(n), o = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      n.length,
      " validation issue",
      n.length === 1 ? "" : "s",
      i.length > 0 ? /* @__PURE__ */ r.jsxs("span", { className: "wf-validation-variable-count", children: [
        " · ",
        i.length,
        " invalid variable reference",
        i.length === 1 ? "" : "s"
      ] }) : null
    ] }),
    /* @__PURE__ */ r.jsx("ul", { className: "wf-validation-list", children: n.map((s, a) => {
      const c = o.get(s);
      return /* @__PURE__ */ r.jsxs("li", { className: c ? "wf-validation-item repairable" : "wf-validation-item", children: [
        /* @__PURE__ */ r.jsx("span", { className: "wf-validation-message", children: s.message ?? "Validation issue." }),
        c?.path.nodeId ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-validation-repair", onClick: () => t(c.path.nodeId), children: [
          /* @__PURE__ */ r.jsx(ep, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function yN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = ea(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(kt, { size: 16 }) : /* @__PURE__ */ r.jsx(Xt, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function xN({ testRun: e, publishedEquivalent: t, onOpenRun: n }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const i = ea(e), o = e.workflowExecutionId, s = !i && t && t.artifactId === e.artifactId ? t : null;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": i ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: i ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(vn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    i && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      " ",
      e.reason
    ] }) : null,
    s ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-equivalence", children: [
      /* @__PURE__ */ r.jsx(Xt, { size: 14 }),
      " Current draft is behaviorally identical to published v",
      s.artifactVersion,
      "."
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
        /* @__PURE__ */ r.jsx("dd", { title: o ?? "None", children: o ? /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => n(o), children: o }) : "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ r.jsx("dd", { children: gl(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: gl(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? $e(e.expiresAt) : "None", children: e.expiresAt ? $e(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function gl(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ra({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function oa({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(ri, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function It({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(kt, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function sa({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(Xt, { size: n ? 13 : 14 }),
    /* @__PURE__ */ r.jsx("span", { children: e }),
    t?.workflowExecutionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: i, children: [
      "Open Run ",
      t.workflowExecutionId
    ] }) : null
  ] });
}
function ht({ value: e, ariaLabel: t, copiedLabel: n, onCopied: i, onCopyFailed: o }) {
  if (!e) return null;
  const s = async (a) => {
    a.preventDefault(), a.stopPropagation();
    try {
      await qj(e), i(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(tp, { size: 12 }) });
}
function jf({ references: e, activeReferenceId: t, onSelect: n, ariaLabel: i = "Source references" }) {
  return e.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No source references." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-reference-list", "aria-label": i, children: e.map((o) => {
    const s = Bj(o), a = o.sourceReferenceId === t, c = /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("span", { className: "wf-reference-heading", children: [
        /* @__PURE__ */ r.jsxs("strong", { children: [
          "Version ",
          o.artifactVersion
        ] }),
        /* @__PURE__ */ r.jsx("span", { className: `wf-chip wf-reference-scope-${o.scope.toLowerCase()}`, children: gf(o.scope) }),
        s !== "live" ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip wf-reference-retired", children: s === "retired" ? "Retired" : "Expired" }) : null,
        a ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Shown" }) : null
      ] }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-reference-meta", children: [
        /* @__PURE__ */ r.jsx("small", { children: o.publishedAt ? `Published ${$e(o.publishedAt)}` : `Created ${$e(o.createdAt)}` }),
        o.expiresAt ? /* @__PURE__ */ r.jsxs("small", { children: [
          "Expires ",
          $e(o.expiresAt)
        ] }) : null,
        o.deletedReason ? /* @__PURE__ */ r.jsx("small", { children: o.deletedReason }) : null
      ] })
    ] });
    return /* @__PURE__ */ r.jsx("li", { className: "wf-reference-item", "data-active": a ? "true" : void 0, children: n ? /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-reference-select", "aria-label": `Show reference ${o.sourceReferenceId}`, onClick: () => n(o), children: c }) : /* @__PURE__ */ r.jsx("span", { className: "wf-reference-select", children: c }) }, o.sourceReferenceId);
  }) });
}
function Nf(e, t) {
  window.history.pushState({}, "", `/workflows/executables/${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function wN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [o, s] = O("loading"), [a, c] = O(""), [u, l] = O(""), [d, f] = O(null), [p, h] = O([]), [m, v] = O("published"), [x, y] = O(!1), [j, g] = O(/* @__PURE__ */ new Set()), w = n?.trim().toLowerCase() ?? "", S = ie(
    () => w ? p.filter((N) => Oj(N, w)) : p,
    [w, p]
  ), b = ie(
    () => Array.from(new Set(p.flatMap((N) => [
      N.definitionId,
      N.definitionVersionId,
      N.sourceId
    ]).filter((N) => !!N))).sort((N, E) => N.localeCompare(E)),
    [p]
  ), I = St(t, "weaver.workflows.explain-executable"), A = se(async () => {
    s("loading"), c("");
    try {
      h(await js(e, { scope: m, includeRetired: x })), s("ready");
    } catch (N) {
      c(N instanceof Error ? N.message : String(N)), s("failed");
    }
  }, [e, x, m]);
  Q(() => {
    A();
  }, [A]);
  const D = async (N) => {
    l(""), f(null), c("");
    try {
      const E = await bs(e, N.artifactId), T = Gs(E);
      f({ artifactId: N.artifactId, workflowExecutionId: T }), l(`Started ${N.artifactId}`);
    } catch (E) {
      c(Qs(E));
    }
  }, M = (N) => {
    I && Ct(t, I, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, k = async (N) => {
    if (await tr().confirm({ message: `Delete executable ${N.artifactId}? This retires all of its source references; the artifact can no longer be run.`, confirmLabel: "Delete", tone: "danger" })) {
      l(""), f(null), c("");
      try {
        await nu(e, N.artifactId), l(`Deleted ${N.artifactId}`), await A();
      } catch (E) {
        c(E instanceof Error ? E.message : String(E));
      }
    }
  }, R = async (N) => {
    l(""), f(null), c("");
    try {
      await Kh(e, N.artifactId), l(`Restored ${N.artifactId}`), await A();
    } catch (E) {
      c(E instanceof Error ? E.message : String(E));
    }
  }, L = (N) => {
    g((E) => {
      const T = new Set(E);
      return T.has(N) ? T.delete(N) : T.add(N), T;
    });
  }, C = (N) => {
    c(""), f(null), l(`Copied ${N}`);
  }, _ = (N) => {
    l(""), f(null), c(`Could not copy ${N}.`);
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        A();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Scope" }),
        /* @__PURE__ */ r.jsxs("select", { "aria-label": "Executable reference scope", value: m, onChange: (N) => v(N.target.value), children: [
          /* @__PURE__ */ r.jsx("option", { value: "published", children: "Published" }),
          /* @__PURE__ */ r.jsx("option", { value: "test-runs", children: "Test runs" }),
          /* @__PURE__ */ r.jsx("option", { value: "all", children: "All" })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-toolbar-field wf-toolbar-checkbox", children: [
        /* @__PURE__ */ r.jsx(
          "input",
          {
            type: "checkbox",
            "aria-label": "Include retired references",
            checked: x,
            onChange: (N) => y(N.target.checked)
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: "Include retired" })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search wf-executable-definition-filter", children: [
        /* @__PURE__ */ r.jsx(xr, { size: 14 }),
        /* @__PURE__ */ r.jsx(
          "input",
          {
            "aria-label": "Filter executables by workflow definition",
            list: "wf-executable-definition-options",
            placeholder: "Filter by definition ID",
            value: n ?? "",
            onChange: (N) => i(N.currentTarget.value || null)
          }
        )
      ] }),
      /* @__PURE__ */ r.jsx("datalist", { id: "wf-executable-definition-options", children: b.map((N) => /* @__PURE__ */ r.jsx("option", { value: N }, N)) }),
      n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(null), children: [
        /* @__PURE__ */ r.jsx(ds, { size: 13 }),
        " Clear"
      ] }) : null
    ] }),
    o === "failed" ? /* @__PURE__ */ r.jsx(It, { message: a }) : null,
    o === "ready" && a ? /* @__PURE__ */ r.jsx(It, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(sa, { status: u, run: d }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx(ra, {}) : null,
    o === "ready" && S.length === 0 ? /* @__PURE__ */ r.jsx(
      oa,
      {
        icon: /* @__PURE__ */ r.jsx(Mt, { size: 22 }),
        title: "No workflow executables",
        description: n ? "No executables match this definition filter." : "Publish a workflow definition to make it executable."
      }
    ) : null,
    o === "ready" && S.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-grid wf-executable-grid", role: "table", "aria-label": "Workflow executables", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
        /* @__PURE__ */ r.jsx("span", { children: "Artifact" }),
        /* @__PURE__ */ r.jsx("span", { children: "Version" }),
        /* @__PURE__ */ r.jsx("span", { children: "Source" }),
        /* @__PURE__ */ r.jsx("span", { children: "Root" }),
        /* @__PURE__ */ r.jsx("span", { children: "Published" }),
        /* @__PURE__ */ r.jsx("span", { children: "Actions" })
      ] }),
      S.map((N) => {
        const E = N.references ?? [], T = j.has(N.artifactId), $ = !!N.deletedAt;
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-executable-row-group", children: [
          /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-row", role: "row", children: [
            /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-cell", children: [
              /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
                /* @__PURE__ */ r.jsx("strong", { title: N.artifactId, children: N.artifactId }),
                /* @__PURE__ */ r.jsx(ht, { value: N.artifactId, ariaLabel: `Copy artifact ID ${N.artifactId}`, copiedLabel: "artifact ID", onCopied: C, onCopyFailed: _ }),
                $ ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip wf-reference-retired", children: "Retired" }) : null
              ] }),
              /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
                /* @__PURE__ */ r.jsx("small", { title: N.artifactHash, children: N.artifactHash }),
                /* @__PURE__ */ r.jsx(ht, { value: N.artifactHash, ariaLabel: `Copy artifact hash ${N.artifactHash}`, copiedLabel: "artifact hash", onCopied: C, onCopyFailed: _ })
              ] })
            ] }),
            /* @__PURE__ */ r.jsxs("span", { className: "wf-version-cell wf-executable-version-cell", children: [
              /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
                /* @__PURE__ */ r.jsx("span", { children: N.artifactVersion }),
                /* @__PURE__ */ r.jsx(ht, { value: N.artifactVersion, ariaLabel: `Copy artifact version ${N.artifactVersion}`, copiedLabel: "artifact version", onCopied: C, onCopyFailed: _ })
              ] }),
              E.length > 0 ? /* @__PURE__ */ r.jsxs(
                "button",
                {
                  type: "button",
                  className: "wf-link-button wf-reference-toggle",
                  "aria-expanded": T,
                  "aria-label": `${T ? "Hide" : "Show"} references of ${N.artifactId}`,
                  onClick: () => L(N.artifactId),
                  children: [
                    T ? /* @__PURE__ */ r.jsx(fs, { size: 12 }) : /* @__PURE__ */ r.jsx(pt, { size: 12 }),
                    E.length,
                    " reference",
                    E.length === 1 ? "" : "s"
                  ]
                }
              ) : null
            ] }),
            /* @__PURE__ */ r.jsx(vN, { executable: N, onCopied: C, onCopyFailed: _ }),
            /* @__PURE__ */ r.jsx("span", { children: pf(N) }),
            /* @__PURE__ */ r.jsx("span", { children: $e(N.publishedAt ?? N.createdAt) }),
            /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
              /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Inspect executable ${N.artifactId}`, onClick: () => Nf(N.artifactId), children: [
                /* @__PURE__ */ r.jsx($l, { size: 13 }),
                " Inspect"
              ] }),
              /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                D(N);
              }, children: [
                /* @__PURE__ */ r.jsx(Mt, { size: 13 }),
                " Run"
              ] }),
              I ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => M(N), children: [
                /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                " Explain"
              ] }) : null,
              $ ? /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Restore executable ${N.artifactId}`, onClick: () => {
                R(N);
              }, children: [
                /* @__PURE__ */ r.jsx(oi, { size: 13 }),
                " Restore"
              ] }) : /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Delete executable ${N.artifactId}`, onClick: () => {
                k(N);
              }, children: [
                /* @__PURE__ */ r.jsx(Lt, { size: 13 }),
                " Delete"
              ] })
            ] })
          ] }),
          T ? /* @__PURE__ */ r.jsx("div", { className: "wf-executable-references", children: /* @__PURE__ */ r.jsx(jf, { references: E, ariaLabel: `References of ${N.artifactId}` }) }) : null
        ] }, N.artifactId);
      })
    ] }) : null
  ] });
}
function vN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion, s = e.definitionId, a = () => {
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(s)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: Js(e.sourceKind) }),
    i ? /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
      s ? /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", "aria-label": `Open source definition ${s}`, onClick: a, children: /* @__PURE__ */ r.jsx("code", { title: i, children: i }) }) : /* @__PURE__ */ r.jsx("code", { title: i, children: i }),
      /* @__PURE__ */ r.jsx(ht, { value: i, ariaLabel: `Copy source ID ${i}`, copiedLabel: "source ID", onCopied: t, onCopyFailed: n })
    ] }) : null,
    o ? /* @__PURE__ */ r.jsxs("small", { children: [
      "Version ",
      o
    ] }) : null
  ] });
}
function bN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [o, s] = O("loading"), [a, c] = O(""), [u, l] = O(""), [d, f] = O(null), [p, h] = O([]), m = St(t, "weaver.workflows.explain-executable"), v = se(async () => {
    s("loading"), c("");
    try {
      const b = await js(e);
      h(b.filter((I) => hf(I, n)).sort(Hj)), s("ready");
    } catch (b) {
      c(b instanceof Error ? b.message : String(b)), h([]), s("failed");
    }
  }, [e, n]);
  Q(() => {
    v();
  }, [v, i]);
  const x = async (b) => {
    l(""), f(null), c("");
    try {
      const I = await bs(e, b.artifactId);
      f({ artifactId: b.artifactId, workflowExecutionId: Gs(I) }), l(`Started ${b.artifactId}`);
    } catch (I) {
      c(Qs(I));
    }
  }, y = (b) => {
    m && Ct(t, m, b) && (c(""), f(null), l(`Sent ${b.artifactId} to Weaver`));
  }, j = async (b) => {
    if (await tr().confirm({ message: `Delete executable ${b.artifactId} for this workflow? This retires this definition's references to it.`, confirmLabel: "Delete", tone: "danger" })) {
      l(""), f(null), c("");
      try {
        await nu(e, b.artifactId, n), l(`Deleted ${b.artifactId}`), await v();
      } catch (I) {
        c(I instanceof Error ? I.message : String(I));
      }
    }
  }, g = () => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(n)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, w = (b) => {
    c(""), f(null), l(`Copied ${b}`);
  }, S = (b) => {
    l(""), f(null), c(`Could not copy ${b}.`);
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-artifacts-toolbar", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        p.length,
        " artifact",
        p.length === 1 ? "" : "s"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        v();
      }, children: [
        /* @__PURE__ */ r.jsx(oi, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: g, children: "Open list" })
    ] }),
    o === "failed" || a ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(sa, { status: u, run: d, compact: !0 }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Loading artifacts..." }) : null,
    o === "ready" && p.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No published artifacts for this workflow yet." }) : null,
    o === "ready" && p.length > 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-artifact-list", role: "list", "aria-label": "Workflow artifacts", children: p.map((b) => /* @__PURE__ */ r.jsxs("article", { className: "wf-artifact-card", role: "listitem", "data-active": b.artifactId === i ? "true" : void 0, children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-heading", children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsxs("span", { className: "wf-artifact-version", children: [
            "Version ",
            b.artifactVersion
          ] }),
          b.artifactId === i ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Latest publish" }) : null
        ] }),
        /* @__PURE__ */ r.jsx("span", { children: $e(b.publishedAt ?? b.createdAt) })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-artifact-card-values", children: [
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line", children: [
          /* @__PURE__ */ r.jsx("code", { title: b.artifactId, children: b.artifactId }),
          /* @__PURE__ */ r.jsx(ht, { value: b.artifactId, ariaLabel: `Copy artifact ID ${b.artifactId}`, copiedLabel: "artifact ID", onCopied: w, onCopyFailed: S })
        ] }),
        /* @__PURE__ */ r.jsxs("span", { className: "wf-cell-line wf-cell-line-muted", children: [
          /* @__PURE__ */ r.jsx("code", { title: b.artifactHash, children: b.artifactHash }),
          /* @__PURE__ */ r.jsx(ht, { value: b.artifactHash, ariaLabel: `Copy artifact hash ${b.artifactHash}`, copiedLabel: "artifact hash", onCopied: w, onCopyFailed: S })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("dl", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Source" }),
          /* @__PURE__ */ r.jsxs("dd", { children: [
            Js(b.sourceKind),
            " ",
            b.sourceVersion ? `v${b.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: pf(b) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Inspect executable ${b.artifactId}`, onClick: () => Nf(b.artifactId), children: [
          /* @__PURE__ */ r.jsx($l, { size: 13 }),
          " Inspect"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          x(b);
        }, children: [
          /* @__PURE__ */ r.jsx(Mt, { size: 13 }),
          " Run"
        ] }),
        m ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => y(b), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Delete executable ${b.artifactId} for this workflow`, onClick: () => {
          j(b);
        }, children: [
          /* @__PURE__ */ r.jsx(Lt, { size: 13 }),
          " Delete"
        ] })
      ] })
    ] }, b.artifactId)) }) : null
  ] });
}
function aa() {
  const [e, t] = O(() => dl(nl, Dj, Mn, Ln)), [n, i] = O(() => dl(il, $j, $t, Tt)), [o, s] = O(() => fl(rl, !1)), [a, c] = O(() => fl(ol, !1)), [u, l] = O(Yj);
  Q(() => {
    _n(nl, String(e));
  }, [e]), Q(() => {
    _n(il, String(n));
  }, [n]), Q(() => {
    _n(rl, String(o));
  }, [o]), Q(() => {
    _n(ol, String(a));
  }, [a]), Q(() => {
    _n(af, u);
  }, [u]), Q(() => {
    if (!u) return;
    const g = (w) => {
      w.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = se((g) => {
    l((w) => w === g ? null : w), g === "palette" ? s((w) => !w) : c((w) => !w);
  }, []), f = se((g) => {
    g === "palette" ? s(!1) : c(!1), l((w) => w === g ? null : g);
  }, []), p = se((g, w) => {
    l(null), g === "palette" ? (s(!1), t((S) => Qi(S + w, Mn, Ln))) : (c(!1), i((S) => Qi(S + w, $t, Tt)));
  }, []), h = se((g, w) => {
    w.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const S = w.clientX, b = g === "palette" ? e : n, I = g === "palette" ? Mn : $t, A = g === "palette" ? Ln : Tt;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (k) => {
      const R = g === "palette" ? k.clientX - S : S - k.clientX, L = Qi(b + R, I, A);
      g === "palette" ? t(L) : i(L);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), m = se((g, w) => {
    w.key === "ArrowLeft" ? (w.preventDefault(), p(g, g === "palette" ? -Wi : Wi)) : w.key === "ArrowRight" ? (w.preventDefault(), p(g, g === "palette" ? Wi : -Wi)) : w.key === "Home" ? (w.preventDefault(), g === "palette" ? t(Mn) : i($t)) : w.key === "End" && (w.preventDefault(), g === "palette" ? t(Ln) : i(Tt));
  }, [p]), v = !o && u !== "inspector", x = !a && u !== "palette", y = [
    "wf-editor-body",
    o ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), j = {
    "--wf-palette-width": `${o ? sl : e}px`,
    "--wf-inspector-width": `${a ? sl : n}px`
  };
  return {
    paletteWidth: e,
    inspectorWidth: n,
    paletteCollapsed: o,
    inspectorCollapsed: a,
    maximizedSidePanel: u,
    setInspectorCollapsed: c,
    paletteExpanded: v,
    inspectorExpanded: x,
    editorBodyClassName: y,
    editorBodyStyle: j,
    toggleSidePanelCollapsed: d,
    toggleSidePanelMaximized: f,
    startSidePanelResize: h,
    handleSidePanelResizeKeyDown: m
  };
}
const jN = 50;
function ml() {
  return { past: [], future: [] };
}
function NN(e) {
  return e.past.length > 0;
}
function SN(e) {
  return e.future.length > 0;
}
function yl(e, t, n = jN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function CN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function IN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function kN({ draft: e, restoreDraft: t }) {
  const n = ae(ml()), i = ae(null), o = ae(""), s = ae(!1), [a, c] = O(0), u = se((v) => {
    n.current = ml(), i.current = v ? Vn(v) : null, o.current = v ? Be(v) : "", s.current = !1, c(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const v = Be(e);
    if (v === o.current) return;
    const x = window.setTimeout(() => {
      const y = i.current;
      y && (n.current = yl(n.current, y), c((j) => j + 1)), i.current = Vn(e), o.current = v;
    }, Ej);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = se(() => {
    if (!e) return;
    const v = Be(e);
    if (v === o.current) return;
    const x = i.current;
    x && (n.current = yl(n.current, x)), i.current = Vn(e), o.current = v;
  }, [e]), d = se((v) => {
    s.current = !0, i.current = Vn(v), o.current = Be(v), t(v), c((x) => x + 1);
  }, [t]), f = se(() => {
    if (!e) return;
    l();
    const v = CN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), p = se(() => {
    if (!e) return;
    l();
    const v = IN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: m } = ie(() => {
    const v = !!e && !!i.current && Be(e) !== o.current;
    return {
      canUndoNow: NN(n.current) || v,
      canRedoNow: SN(n.current) && !v
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: m };
}
const EN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function AN(e, t) {
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
function _N() {
  const [e, t] = Yf(AN, EN), n = ie(() => ({
    // Load a freshly fetched / applied draft (or clear it). Used by load(), JSON apply, and undo/redo.
    loadDraft(i) {
      t({ type: "draftLoaded", draft: i });
    },
    // Replace the draft from a Weaver batch apply/undo, invalidating scope, selection, run and artifact.
    replaceDraftByBatch(i, o) {
      t({ type: "draftReplacedByBatch", draft: i, selectedNodeId: o });
    },
    // In-place draft edit that keeps scope + selection.
    editDraft(i) {
      t({ type: "draftEdited", recipe: i });
    },
    // Draft edit that also selects a node (add/wrap root, add scoped activity).
    editDraftAndSelect(i, o) {
      t({ type: "draftEditedAndSelected", recipe: i, selectedNodeId: o });
    },
    select(i) {
      t({ type: "selectionChanged", selectedNodeId: i });
    },
    // Jump to an explicit breadcrumb path (breadcrumb clicks, variable-repair navigation).
    navigateToScope(i, o = null) {
      t({ type: "scopeNavigated", frames: i, selectedNodeId: o });
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
const DN = 320, $N = 140;
function TN(e, t, n) {
  return n === "sequence" ? RN(e) : PN(e, t);
}
function RN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function PN(e, t) {
  const n = /* @__PURE__ */ new Map();
  if (e.length === 0) return n;
  const i = new Set(e.map((d) => d.id)), o = t.filter((d) => i.has(d.source) && i.has(d.target)), s = /* @__PURE__ */ new Set();
  for (const d of o)
    s.add(d.source), s.add(d.target);
  const a = /* @__PURE__ */ new Map();
  for (const d of e) a.set(d.id, 0);
  for (let d = 0; d < e.length; d += 1) {
    let f = !1;
    for (const p of o) {
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
      n.set(p, { x: d * DN, y: h * $N });
    });
  return n;
}
function MN(e, t, n, i, o) {
  if (!e) return { kind: "becomeRoot" };
  const s = an(e, t, o);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Te(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const LN = "root";
function zN(e) {
  return e.length === 0 ? LN : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function VN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function ON({
  draft: e,
  scope: t,
  scopeOwner: n,
  frames: i,
  catalog: o,
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
  const [x, y] = O([]), [j, g] = O([]), [w, S] = O(null), [b, I] = O(null), [A, D] = O(null), M = ae(null), k = ae(null), R = ae(/* @__PURE__ */ new Map()), L = ae(/* @__PURE__ */ new Set()), C = ae(null), _ = ae([]), N = ae(null), E = ae(null), T = ae(!1), $ = ie(() => zN(i), [i]);
  Q(() => () => {
    w && R.current.set($, w.getViewport());
  }, [w, $]), Q(() => {
    if (C.current = $, !n) {
      _.current = [], y([]), g([]);
      return;
    }
    const V = a ? xs(n, o, e?.layout ?? []) : t ? ys(t, o, e?.layout ?? []) : { nodes: [], edges: [] };
    _.current = V.nodes.map((K) => K.id), y(V.nodes), g(V.edges);
  }, [o, e?.layout, a, t, n, $]), Q(() => {
    if (!w || C.current !== $ || !VN(x, _.current)) return;
    C.current = null;
    const V = R.current.get($), K = L.current.has($);
    L.current.add($), window.requestAnimationFrame(() => {
      V ? w.setViewport(V) : !K && x.length > 0 && w.fitView({ padding: 0.2 });
    });
  }, [x, w, $]);
  const z = se((V, K, Y) => Y ? [
    ...V.filter((oe) => oe.nodeId !== K),
    { nodeId: K, x: Math.round(Y.x), y: Math.round(Y.y) }
  ] : V, []), F = se((V, K) => {
    if (e?.state.rootActivity && a)
      return null;
    const Y = nr(V, ts(V)), oe = MN(e?.state.rootActivity, i, Y, V, s);
    return oe.kind === "becomeRoot" ? (f(
      ({ draft: ee }) => ee ? { ...ee, state: { ...ee.state, rootActivity: Y } } : null,
      Y.nodeId
    ), null) : oe.kind === "leafError" ? (m(""), v("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root."), null) : oe.kind === "staleFrames" ? (m(""), v("This slot could not be resolved — returning to the workflow root."), h(), null) : oe.kind === "wrapRoot" ? (f(({ draft: ee }) => {
      const ve = ee?.state.rootActivity;
      return ve ? {
        ...ee,
        layout: z(ee.layout, ve.nodeId, K),
        state: { ...ee.state, rootActivity: ka(Y, [], [ve], V) }
      } : null;
    }, e?.state.rootActivity?.nodeId ?? null), v(""), m(`Wrapped root in ${Ie(V)}`), null) : (f(({ draft: ee, frames: ve }) => {
      if (!ee?.state.rootActivity) return null;
      const pe = an(ee.state.rootActivity, ve, s);
      if (!pe) return null;
      const be = pe.slot.cardinality === "single" ? [Y] : [...pe.slot.activities, Y], Se = ka(ee.state.rootActivity, ve, be, s);
      return {
        ...ee,
        layout: z(ee.layout, Y.nodeId, K),
        state: { ...ee.state, rootActivity: Se }
      };
    }, Y.nodeId), oe.replacedActivity && (v(""), m(`Replaced ${oe.slot.label} content`)), Y);
  }, [s, e?.state.rootActivity, i, a, f, h, z, v, m]), B = se((V, K) => {
    const Y = nr(V, ts(V)), oe = {
      id: Y.nodeId,
      type: "workflowActivity",
      position: K,
      selected: !0,
      data: {
        label: Ie(V),
        activityVersionId: V.activityVersionId,
        activityTypeKey: V.activityTypeKey,
        category: V.category,
        executionType: V.executionType,
        icon: xn(V),
        childSlots: Te(Y, V),
        acceptsInbound: String(V.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Jl(Y, V)
      }
    };
    return { activityNode: Y, node: oe };
  }, []), Z = se((V, K, Y = []) => {
    a || d(({ draft: oe, frames: ee }) => {
      if (!oe) return null;
      const ve = eh(oe.layout, V), pe = oe.state.rootActivity;
      if (!pe) return { ...oe, layout: ve };
      const be = an(pe, ee, s);
      if (!be) return { ...oe, layout: ve };
      const Se = Gp(be, V, K, Y), He = be.slot.mode === "flowchart" ? Qp(Se, K) : Se;
      return {
        ...oe,
        layout: ve,
        state: {
          ...oe.state,
          rootActivity: Jp(pe, ee, He, s)
        }
      };
    });
  }, [s, a, d]), H = se((V, K) => {
    if (!M.current) return null;
    const Y = M.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: V, y: K }) : {
      x: V - Y.left,
      y: K - Y.top
    };
  }, [w]), U = se((V, K) => document.elementFromPoint(V, K)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), G = se((V, K, Y) => {
    const oe = x.find((De) => De.id === K.source), ee = x.find((De) => De.id === K.target), ve = oe && ee ? Gj(oe, ee) : oe ? pl(oe) : Y, pe = B(V, ve), Se = [...x.map((De) => De.selected ? { ...De, selected: !1 } : De), pe.node], He = ch(j, K, pe.node.id);
    y(Se), g(He), p(pe.node.id), Z(Se, He, [pe.activityNode]);
  }, [Z, B, j, x, p]), X = se((V, K, Y) => {
    if (!u || !M.current) return !1;
    const oe = M.current.getBoundingClientRect();
    if (!(K >= oe.left && K <= oe.right && Y >= oe.top && Y <= oe.bottom)) return !1;
    const ve = H(K, Y);
    if (!ve) return !1;
    if (c) {
      const pe = U(K, Y), be = pe ? j.find((Se) => Se.id === pe) : void 0;
      if (be)
        return G(V, be, ve), !0;
    }
    return F(V, ve), !0;
  }, [F, u, j, U, c, G, H]);
  Q(() => {
    const V = (Y) => {
      const oe = N.current;
      if (!oe) return;
      Math.hypot(Y.clientX - oe.startX, Y.clientY - oe.startY) >= Ij && (oe.dragging = !0);
    }, K = (Y) => {
      const oe = N.current;
      if (N.current = null, !oe?.dragging || !M.current || E.current) return;
      const ee = M.current.getBoundingClientRect();
      Y.clientX >= ee.left && Y.clientX <= ee.right && Y.clientY >= ee.top && Y.clientY <= ee.bottom && (T.current = !0, window.setTimeout(() => {
        T.current = !1;
      }, 0), X(oe.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", V), window.addEventListener("pointerup", K), window.addEventListener("pointercancel", K), () => {
      window.removeEventListener("pointermove", V), window.removeEventListener("pointerup", K), window.removeEventListener("pointercancel", K);
    };
  }, [w, X]);
  const P = (V, K) => {
    E.current = { activityVersionId: K.activityVersionId, handledDrop: !1 }, V.dataTransfer.setData(tl, K.activityVersionId), V.dataTransfer.setData("text/plain", K.activityVersionId), V.dataTransfer.effectAllowed = "copy";
  }, q = (V, K) => {
    const Y = E.current;
    E.current = null, !Y?.handledDrop && (V.clientX === 0 && V.clientY === 0 || X(K, V.clientX, V.clientY) && (T.current = !0, window.setTimeout(() => {
      T.current = !1;
    }, 0)));
  }, de = (V, K) => {
    V.button === 0 && (N.current = {
      activity: K,
      startX: V.clientX,
      startY: V.clientY,
      dragging: !1
    });
  }, le = (V) => {
    T.current || u && F(V);
  }, te = (V) => {
    if (!u) {
      V.dataTransfer.dropEffect = "none";
      return;
    }
    if (V.preventDefault(), V.dataTransfer.dropEffect = "copy", !c) return;
    const K = U(V.clientX, V.clientY);
    D(K);
  }, re = (V) => {
    if (!M.current) return;
    const K = V.relatedTarget;
    K && M.current.contains(K) || D(null);
  }, fe = (V) => {
    V.preventDefault(), D(null);
    const K = V.dataTransfer.getData(tl) || V.dataTransfer.getData("text/plain");
    if (!K || (V.stopPropagation(), E.current?.activityVersionId === K && (E.current.handledDrop = !0), !u)) return;
    const Y = s.get(K);
    Y && X(Y, V.clientX, V.clientY);
  }, W = (V) => {
    if (!u) return;
    if (V) {
      I({ kind: "fromEmpty", clientX: V.clientX, clientY: V.clientY });
      return;
    }
    const K = M.current?.getBoundingClientRect();
    K && I({
      kind: "fromEmpty",
      clientX: K.left + K.width / 2,
      clientY: K.top + K.height / 2
    });
  }, ne = (V) => {
    const K = a ? V.filter((Y) => Y.type === "select") : V;
    K.length !== 0 && y((Y) => kd(K, Y));
  }, ge = (V) => {
    a || g((K) => Ed(V, K));
  }, ye = (V) => !V.source || !V.target || V.source === V.target || !c ? !1 : !V.targetHandle, Ee = (V) => {
    if (!e?.state.rootActivity || !t || !c || !ye(V)) return;
    const K = ir(V.source, V.target, V.sourceHandle ?? "Done", V.targetHandle ?? void 0), Y = _d(K, j);
    g(Y), Z(x, Y);
  }, Re = () => {
    Z(x, j);
  }, Ve = !a && x.length > 0, ct = se(() => {
    if (a || x.length === 0) return;
    const V = t?.slot.mode === "sequence" ? "sequence" : "flowchart", K = TN(x, j, V), Y = x.map((oe) => {
      const ee = K.get(oe.id);
      return ee ? { ...oe, position: ee } : oe;
    });
    y(Y), Z(Y, j), window.requestAnimationFrame(() => w?.fitView({ padding: 0.2 })), m("Rearranged the canvas.");
  }, [j, x, t, a, Z, w, m]), Ye = (V, K) => {
    if (!K.nodeId || K.handleType === "target") {
      k.current = null;
      return;
    }
    k.current = {
      nodeId: K.nodeId,
      handleId: K.handleId ?? null
    };
  }, Ze = (V, K) => {
    const Y = eN(k.current, K);
    if (k.current = null, !Y || !c || K.toNode || K.toHandle || Qj(V)) return;
    const oe = yf(V);
    I({
      kind: "fromPort",
      sourceNodeId: Y.nodeId,
      sourceHandleId: Y.handleId,
      clientX: oe.x,
      clientY: oe.y
    });
  }, Me = (V, K) => {
    if (!c || !ye(K)) return;
    const Y = ab(V, {
      ...K,
      sourceHandle: K.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: K.targetHandle ?? null
    }, j, { shouldReplaceId: !1 });
    g(Y), Z(x, Y);
  }, Oe = (V) => {
    if (a || V.length === 0) return;
    const K = new Set(V.map((ee) => ee.id)), Y = x.filter((ee) => !K.has(ee.id)), oe = j.filter((ee) => !K.has(ee.source) && !K.has(ee.target));
    y(Y), g(oe), l && K.has(l) && p(null), Z(Y, oe);
  }, we = (V) => {
    if (a || V.length === 0) return;
    const K = new Set(V.map((oe) => oe.id)), Y = j.filter((oe) => !K.has(oe.id));
    g(Y), Z(x, Y);
  }, Ue = se((V) => {
    if (a) return;
    const K = j.filter((Y) => Y.id !== V);
    g(K), Z(x, K);
  }, [Z, j, a, x]), _e = se((V, K, Y) => {
    c && I({ kind: "spliceEdge", edgeId: V, clientX: K, clientY: Y });
  }, [c]), Et = (V) => {
    const K = b;
    if (!K) return;
    I(null);
    const Y = H(K.clientX, K.clientY) ?? { x: 0, y: 0 };
    if (K.kind === "fromEmpty") {
      const ee = B(V, Y), pe = [...x.map((be) => be.selected ? { ...be, selected: !1 } : be), ee.node];
      y(pe), p(ee.node.id), Z(pe, j, [ee.activityNode]);
      return;
    }
    if (K.kind === "fromPort") {
      const ee = x.find((De) => De.id === K.sourceNodeId), ve = ee ? pl(ee) : Y, pe = B(V, ve), Se = [...x.map((De) => De.selected ? { ...De, selected: !1 } : De), pe.node], He = [...j, ir(K.sourceNodeId, pe.node.id, K.sourceHandleId ?? "Done")];
      y(Se), g(He), p(pe.node.id), Z(Se, He, [pe.activityNode]);
      return;
    }
    const oe = j.find((ee) => ee.id === K.edgeId);
    oe && G(V, oe, Y);
  }, bn = ie(() => ({
    highlightedEdgeId: A,
    deleteEdge: Ue,
    requestInsertActivity: _e
  }), [Ue, A, _e]);
  return {
    nodes: x,
    edges: j,
    canvasRef: M,
    setReactFlowInstance: S,
    connectMenu: b,
    setConnectMenu: I,
    edgeActions: bn,
    onNodesChange: ne,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: we,
    isValidConnection: ye,
    onConnect: Ee,
    onConnectStart: Ye,
    onConnectEnd: Ze,
    onReconnect: Me,
    commitLayout: Re,
    canAutoLayout: Ve,
    autoLayout: ct,
    onCanvasDragOver: te,
    onCanvasDragLeave: re,
    onCanvasDrop: fe,
    openEmptyConnectMenu: W,
    onConnectMenuPick: Et,
    addActivity: F,
    onPaletteClick: le,
    onPaletteDragStart: P,
    onPaletteDragEnd: q,
    onPalettePointerDown: de
  };
}
const xl = "elsa-studio:apply-workflow-graph-operation-batch", wl = "elsa-studio:undo-workflow-graph-operation-batch", HN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function WN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = ZN(e), o = Cf(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = YN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = UN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", o), h = FN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && BN(i.state.rootActivity, h);
      const m = Nt(d.position) ? ns(d.position, { x: 280, y: 160 }) : null;
      m && (i.layout = vl(i.layout, p, m));
      continue;
    }
    if (l === "set-root") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = qt(d.activityId, s);
      if (!f || !ca(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = vl(i.layout, f, ns(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      XN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = Nt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = qt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = Sf(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      KN(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      qN(i, d, s);
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
function FN(e, t, n) {
  const i = e.parameters ?? {}, o = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Le(i.displayName));
  return s ? nr(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: o,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: ns(i.position, { x: 280, y: 160 }) }
  };
}
function BN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = la(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function KN(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = qt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = qt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !Nt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function qN(e, t, n) {
  const i = e.state.rootActivity, o = i?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Le(t.connectionId), a = qt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = qt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = o.filter((u) => {
    if (!Nt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = Nt(u.source) ? u.source.nodeId : void 0, d = Nt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function XN(e, t, n) {
  const i = Nt(n);
  e[uu(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function Po(e, t, n, i) {
  const o = qt(t, n);
  return o ? ca(e.state.rootActivity, o) ?? i.get(o) ?? null : null;
}
function qt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function ca(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of If(e)) {
    const i = ca(n, t);
    if (i) return i;
  }
  return null;
}
function Sf(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = la(e);
  if (n) {
    const i = n.map((o) => Sf(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function Cf(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of If(e)) Cf(n, t);
  return t;
}
function If(e) {
  return la(e) ?? [];
}
function la(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function vl(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function ns(e, t) {
  const n = Nt(e) ? e : {}, i = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function UN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, o = 2;
  for (; t.has(i); )
    i = `${n}-${o}`, o += 1;
  return t.add(i), i;
}
function YN(e) {
  return typeof e == "number" ? HN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function ZN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Nt(e) {
  return typeof e == "object" && e !== null;
}
function JN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: o,
  setError: s
}) {
  const a = ae(/* @__PURE__ */ new Map());
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
        const p = Vn(e), h = WN(e, d.batch, n), m = `weaver-batch-${Date.now()}`;
        a.current.set(m, p), i(h.draft, h.finalActivityIds.at(-1) ?? null), o(h.summary), s(""), d.respond({ ok: !0, result: { ...h, undoToken: m } });
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
      a.current.delete(d.undoToken), i(f, null), o("Restored workflow draft before Weaver batch."), s(""), d.respond({ ok: !0, summary: "Restored workflow draft before Weaver batch." });
    };
    return window.addEventListener(xl, c), window.addEventListener(wl, u), () => {
      window.removeEventListener(xl, c), window.removeEventListener(wl, u);
    };
  }, [n, t, e, i, o, s]);
}
function GN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: o, setError: s }) {
  const [a, c] = O(n), u = ae(""), l = ae(0), d = ae(Promise.resolve()), f = se((h) => {
    u.current = h ? Be(h) : "";
  }, []), p = se(async (h, m) => {
    const v = async () => {
      const y = ++l.current, j = Be(h);
      s("");
      try {
        const g = await Vh(e, h), w = Be(g);
        return u.current = w, i(({ draft: S }) => !S || S.id !== g.id ? null : Be(S) === j ? g : { ...S, validationErrors: g.validationErrors }), y === l.current && o(m), g;
      } catch (g) {
        throw y === l.current && (o(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(v, v);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, o, s]);
  return Q(() => {
    if (!a || !t || Be(t) === u.current) return;
    o("Autosaving...");
    const m = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, kj);
    return () => window.clearTimeout(m);
  }, [a, t, p, o]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function QN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: o, setError: s }) {
  const [a, c] = O(null), [u, l] = O([]), [d, f] = O([]), [p, h] = O(null), [m, v] = O(qi), [x, y] = O("loading"), j = se(async () => {
    s(""), y("loading");
    const [g, w, S, b, I] = await Promise.all([
      tu(e, t),
      ci(e),
      eg(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      tg(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: qi })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      iu(e).then(
        (D) => D,
        () => null
      )
    ]), A = g.draft ?? null;
    c(g), o(A), n(A), i(A), l(w.activities ?? []), f(S.descriptors), h(I), v(b.descriptors.length > 0 ? b.descriptors : qi), y(S.ok ? "ready" : "failed");
  }, [e, t, n, i, o, s]);
  return Q(() => {
    j().catch((g) => s(g instanceof Error ? g.message : String(g)));
  }, [j, s]), {
    details: a,
    setDetails: c,
    catalog: u,
    activityDescriptors: d,
    availabilityDiagnostics: p,
    expressionDescriptors: m,
    descriptorStatus: x,
    reload: j
  };
}
function eS({ context: e, details: t, setDetails: n, setStatus: i }) {
  const o = ae(null), s = ae(null), a = ae({});
  Q(() => {
    o.current = t;
  }, [t]);
  const c = se(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = o.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || zh(e, d.id, {
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
function tS({
  context: e,
  draft: t,
  details: n,
  busy: i,
  saveDraft: o,
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
    const j = n?.definition.name;
    oj(nj(t, j), j), d("Exported workflow as JSON.");
  }, [t, n, d]), v = se(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await o(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, o, l, d]), x = se(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await o(t, "Saved"), d("Promoting...");
        const j = await Oh(e, t.id), g = await Hh(e, j.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (j) {
        d(""), f(j instanceof Error ? j.message : String(j));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, o, s, u, l, d, f]), y = se(async () => {
    if (!t?.state.rootActivity || i) return;
    const j = t, g = Be(j);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const w = rN(j);
      l("testRunStarting"), d("Starting test run...");
      const S = await Wh(e, {
        definitionId: j.definitionId,
        snapshotId: w,
        state: j.state
      });
      a({ draftSignature: g, view: S }), p("runtime"), h(!1), d(ea(S) ? "Test run rejected" : "Test run dispatched");
    } catch (w) {
      d(""), f(w instanceof Error ? w.message : String(w));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: m, save: v, promoteAndPublish: x, run: y };
}
function nS(e, t, n) {
  const i = n?.artifactId ?? null, [o, s] = O(null);
  return Q(() => {
    if (s(null), !i) return;
    let a = !1;
    return js(e).then(
      (c) => {
        a || s(Wj(i, c, t));
      },
      () => {
        a || s(null);
      }
    ), () => {
      a = !0;
    };
  }, [e, t, i]), o;
}
function iS({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: o,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ie(() => new Map(o.map((k) => [k.activityVersionId, k])), [o]), l = se(
    (k) => Ng([k.activityVersionId, k.activityTypeKey], a),
    [a]
  ), d = ie(() => Uj(s), [s]), f = ie(() => Wl(c, n, u), [c, n, u]), p = ws(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", m = ie(() => h ? null : an(c, n, u), [c, n, u, h]), v = ie(() => h && f?.nodeId === i ? f : m?.slot.activities.find((k) => k.nodeId === i) ?? null, [h, m, f, i]), x = ie(
    () => v ? ul(v, u, d) : null,
    [u, d, v]
  ), y = ie(() => v ? Te(v, u) : [], [u, v]), j = v ?? f, g = !v && !!f, w = ie(
    () => j ? ul(j, u, d) : null,
    [u, d, j]
  ), S = ie(
    () => j ? l({ activityVersionId: j.activityVersionId, activityTypeKey: u.get(j.activityVersionId)?.activityTypeKey }) : null,
    [l, u, j]
  ), b = ie(() => j ? Te(j, u) : [], [u, j]), I = j ? xh(j, u.get(j.activityVersionId)) : !1, A = $h(e, t?.state, v?.nodeId ?? null, u), D = !h && m?.slot.mode === "flowchart";
  return {
    catalogByVersion: u,
    availabilityLookup: l,
    scopeOwner: f,
    isUnsupportedDesigner: h,
    scope: m,
    selectedNode: v,
    selectedDescriptor: x,
    selectedSlots: y,
    inspectedNode: j,
    inspectedIsScopeOwner: g,
    inspectedDescriptor: w,
    inspectedNodeAvailability: S,
    inspectedSlots: b,
    inspectedSupportsScopedVariables: I,
    scopedVariableAnalysis: A,
    isFlowchartDesigner: D,
    canAddActivitiesToCanvas: !c || !h
  };
}
function bl(e, t, n) {
  return t?.typeName ?? (e ? n.get(e.activityVersionId)?.activityTypeKey ?? e.activityVersionId : null);
}
function rS({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: o,
  inspectedNode: s,
  inspectedDescriptor: a,
  inspectedIsScopeOwner: c,
  catalogByVersion: u
}) {
  const l = ie(() => t ? xf(t.state.rootActivity, u) : [], [u, t]), d = ie(() => t ? wf(t.state.rootActivity, u) : [], [u, t]), f = ie(
    () => (t?.validationErrors ?? []).map((p) => ({ severity: p.code ?? "warning", message: p.message ?? "Workflow validation issue." })),
    [t]
  );
  Q(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: tN(t),
        selectedNodeId: i,
        selectedActivityType: bl(n, o, u),
        inspectedNodeId: s?.nodeId ?? null,
        inspectedActivityType: bl(s, a, u),
        inspectedIsScopeOwner: c,
        summary: e.definition.name,
        activities: l,
        connections: d,
        diagnostics: f
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [l, u, d, e, f, t, a, c, s, o, n, i]);
}
function oS({
  paletteSearch: e,
  onSearchChange: t,
  groups: n,
  expandedCategories: i,
  onToggleCategory: o,
  onActivityClick: s,
  onActivityDragStart: a,
  onActivityDragEnd: c,
  onActivityPointerDown: u
}) {
  const l = e.trim().length > 0;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-body", children: [
    /* @__PURE__ */ r.jsxs("label", { className: "wf-palette-search", children: [
      /* @__PURE__ */ r.jsx(xr, { size: 14, "aria-hidden": "true" }),
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
      const f = l || i.has(d.category);
      return /* @__PURE__ */ r.jsxs("div", { className: "wf-palette-category", children: [
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            className: "wf-palette-category-toggle",
            role: "treeitem",
            "aria-expanded": f,
            onClick: () => o(d.category),
            children: [
              f ? /* @__PURE__ */ r.jsx(fs, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 }),
              /* @__PURE__ */ r.jsx("span", { children: d.category }),
              /* @__PURE__ */ r.jsx("small", { children: d.activities.length })
            ]
          }
        ),
        f ? /* @__PURE__ */ r.jsx("div", { className: "wf-palette-activities", role: "group", children: d.activities.map((p) => {
          const h = p.description?.trim(), m = h ? `wf-palette-description-${p.activityVersionId}` : void 0, v = Ie(p), x = xn(p);
          return /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-palette-activity",
              role: "treeitem",
              draggable: !0,
              title: h || Ie(p),
              "aria-describedby": m,
              onClick: () => s(p),
              onDragStart: (y) => a(y, p),
              onDragEnd: (y) => c(y, p),
              onPointerDown: (y) => u(y, p),
              children: [
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: Sr(x) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: v }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: m, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Tl, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const kf = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), sS = "Variable";
function aS({
  activity: e,
  descriptor: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
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
  const d = gS(l), f = o.length > 0 ? o : Sg;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        cS,
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
function cS({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, d = ss(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? du(e, t) : null, h = p?.expression.type ?? "Literal", m = Eg(e, t), v = h.toLowerCase(), y = p && (v === "literal" || v === "object") && !$g(t) ? _g(t.typeName) : null, j = y ? ss(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, w = g ? Af(i, g) : null, S = w?.surfaces.inline, b = w && g ? _f(w, g, m) : [], I = y != null, A = !!(p && !I && mS(t, d?.id)), D = !!(p && !I && yS(t, d?.id)), [M, k] = O(!1), R = (N) => {
    const E = p ? Ig(p, N) : N;
    c(za(e, t, E));
  }, L = (N) => {
    p && c(za(e, t, kg(p, N)));
  }, C = y ? j ? is(j.component, t, m, u, { ...l, scope: "collection" }, R) : /* @__PURE__ */ r.jsx(
    uS,
    {
      input: t,
      elementTypeName: y.elementTypeName,
      value: m,
      editors: n,
      context: l,
      disabled: u,
      onChange: R
    }
  ) : null, _ = h === sS && p ? /* @__PURE__ */ r.jsx(
    pS,
    {
      value: m,
      visibleVariables: s,
      scopeStatus: a,
      disabled: u,
      onChange: R
    }
  ) : C ?? (S && g ? /* @__PURE__ */ r.jsx(
    S,
    {
      descriptor: t,
      syntax: h,
      value: m,
      disabled: u,
      context: g,
      onChange: R
    }
  ) : is(f, t, m, u, l, R));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-row-header", children: [
      /* @__PURE__ */ r.jsx("label", { children: t.displayName || t.name }),
      /* @__PURE__ */ r.jsx("span", { children: Ss(t.typeName) })
    ] }),
    t.description ? /* @__PURE__ */ r.jsx("p", { children: t.description }) : null,
    p && !A ? /* @__PURE__ */ r.jsx(
      rs,
      {
        label: `${t.displayName || t.name} expression syntax`,
        value: h,
        descriptors: o,
        disabled: u,
        onChange: L
      }
    ) : null,
    A ? /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-field", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-expression-editor", children: [
        _,
        as(b)
      ] }),
      /* @__PURE__ */ r.jsx(
        rs,
        {
          label: `${t.displayName || t.name} expression syntax`,
          value: h,
          descriptors: o,
          disabled: u,
          variant: "inline",
          onChange: L
        }
      ),
      D ? /* @__PURE__ */ r.jsx(
        "button",
        {
          type: "button",
          className: "wf-expression-expand-button",
          "aria-label": `Open expanded ${t.displayName || t.name} editor`,
          title: "Open expanded editor",
          onClick: () => k(!0),
          children: /* @__PURE__ */ r.jsx(sn, { size: 13 })
        }
      ) : null
    ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      _,
      as(b)
    ] }),
    D && !A ? /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-property-expand-row",
        "aria-label": `Open expanded ${t.displayName || t.name} editor`,
        onClick: () => k(!0),
        children: [
          /* @__PURE__ */ r.jsx(sn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ r.jsx(
      dS,
      {
        input: t,
        value: m,
        syntax: h,
        descriptors: o,
        activity: e,
        expressionEditors: i,
        disabled: u,
        onChange: R,
        onSyntaxChange: L,
        onClose: () => k(!1)
      }
    ) : null
  ] });
}
function lS(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function uS({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = Tg(n), u = Pg(e, t), l = { ...o, scope: "element" }, d = ss(i, u, l)?.component, f = e.displayName || e.name, p = (S, b) => a(c.map((I, A) => A === S ? b : I)), [h, m] = O(null), [v, x] = O(null), y = () => {
    m(null), x(null);
  }, j = (S) => (b) => {
    m(S), b.dataTransfer.effectAllowed = "move", b.dataTransfer.setData("text/plain", String(S));
  }, g = (S) => (b) => {
    h !== null && (b.preventDefault(), b.dataTransfer.dropEffect = "move", v !== S && x(S));
  }, w = (S) => (b) => {
    b.preventDefault(), h !== null && h !== S && a(vo(c, h, S)), y();
  };
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-editor", children: [
    c.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-collection-empty", children: "No items yet." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-collection-items", children: c.map((S, b) => /* @__PURE__ */ r.jsxs(
      "li",
      {
        className: lS(b, h, v),
        onDragOver: g(b),
        onDrop: w(b),
        children: [
          /* @__PURE__ */ r.jsx(
            "span",
            {
              className: "wf-collection-item-handle",
              draggable: !s,
              "aria-label": `Drag ${f} item ${b + 1} to reorder`,
              title: "Drag to reorder",
              onDragStart: j(b),
              onDragEnd: y,
              children: /* @__PURE__ */ r.jsx(Tl, { size: 13, "aria-hidden": "true" })
            }
          ),
          /* @__PURE__ */ r.jsx("div", { className: "wf-collection-item-editor", children: is(d, u, S, s, l, (I) => p(b, I)) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-collection-item-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} up`,
                disabled: s || b === 0,
                onClick: () => a(vo(c, b, b - 1)),
                children: /* @__PURE__ */ r.jsx(np, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button",
                "aria-label": `Move ${f} item ${b + 1} down`,
                disabled: s || b === c.length - 1,
                onClick: () => a(vo(c, b, b + 1)),
                children: /* @__PURE__ */ r.jsx(fs, { size: 13 })
              }
            ),
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-collection-item-button danger",
                "aria-label": `Remove ${f} item ${b + 1}`,
                disabled: s,
                onClick: () => a(c.filter((I, A) => A !== b)),
                children: /* @__PURE__ */ r.jsx(Lt, { size: 13 })
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
        onClick: () => a([...c, Rg(t)]),
        children: [
          /* @__PURE__ */ r.jsx(on, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function dS({
  input: e,
  value: t,
  syntax: n,
  descriptors: i,
  activity: o,
  expressionEditors: s,
  disabled: a,
  onChange: c,
  onSyntaxChange: u,
  onClose: l
}) {
  const d = Al(), f = e.displayName || e.name, p = {
    activity: o,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = Af(s, p), m = h?.surfaces.expanded, v = h ? _f(h, p, t) : [], x = m ? null : hS(s, p);
  return Q(() => {
    const y = (j) => {
      j.key === "Escape" && l();
    };
    return window.addEventListener("keydown", y), () => window.removeEventListener("keydown", y);
  }, [l]), /* @__PURE__ */ r.jsx("div", { className: "wf-property-editor-backdrop", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-property-editor-dialog", role: "dialog", "aria-modal": "true", "aria-labelledby": d, children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Property editor" }),
        /* @__PURE__ */ r.jsx("h3", { id: d, children: f })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": `Close ${f} editor`, onClick: l, children: /* @__PURE__ */ r.jsx(ds, { size: 16 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-body", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-property-editor-toolbar", children: [
        /* @__PURE__ */ r.jsx(
          rs,
          {
            label: `${f} expression syntax`,
            value: n,
            descriptors: i,
            disabled: a,
            onChange: u
          }
        ),
        /* @__PURE__ */ r.jsx("span", { children: Ss(e.typeName) })
      ] }),
      e.description ? /* @__PURE__ */ r.jsx("p", { children: e.description }) : null,
      m ? /* @__PURE__ */ r.jsx(
        m,
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
            onChange: (y) => c(y.target.value)
          }
        )
      ] }),
      as(v)
    ] }),
    /* @__PURE__ */ r.jsxs("footer", { children: [
      /* @__PURE__ */ r.jsx("span", { children: "Changes update the draft immediately." }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: l, children: "Close" })
    ] })
  ] }) });
}
function is(e, t, n, i, o, s) {
  return e ? /* @__PURE__ */ r.jsx(
    e,
    {
      descriptor: t,
      value: n,
      disabled: i,
      context: o,
      onChange: s
    }
  ) : /* @__PURE__ */ r.jsx("input", { type: "text", value: n == null ? "" : String(n), disabled: i, onChange: (a) => s(a.target.value) });
}
function rs({
  label: e,
  value: t,
  descriptors: n,
  disabled: i,
  variant: o = "block",
  onChange: s
}) {
  const [a, c] = O(!1), u = Al(), l = n.find((f) => f.type === t), d = [
    "wf-syntax-picker-trigger",
    o === "inline" ? "inline" : "",
    a ? "open" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs("div", { className: o === "inline" ? "wf-syntax-picker inline" : "wf-syntax-picker", onBlur: (f) => {
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
        disabled: i,
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
const os = "::";
function Ef(e) {
  return !e || e === or ? or : e;
}
function jl(e, t) {
  return `${Ef(t)}${os}${e}`;
}
function fS(e) {
  const t = e.indexOf(os);
  if (t < 0) return null;
  const n = e.slice(t + os.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function pS({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: o }) {
  const s = eu(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? jl(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Ef(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = fS(d.target.value);
          f && o(vh(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = jl(d.referenceKey, d.scopeId);
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
function ss(e, t, n) {
  return [...e].sort((i, o) => (i.order ?? 500) - (o.order ?? 500)).find((i) => i.supports(t, n));
}
function Af(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function _f(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function hS(e, t) {
  if (t.syntax.toLowerCase() === "literal") return null;
  const n = [...e].sort((a, c) => (a.order ?? 500) - (c.order ?? 500)).find((a) => a.supports(t) && a.metadata)?.metadata;
  if (!n) return `No enhanced editor is registered for ${t.syntax}. Using the generic text editor.`;
  const i = n.displayName?.trim() || "enhanced editor", o = n.installHint?.trim(), s = `No ${i} is registered for ${t.syntax}. Using the generic text editor.`;
  return o ? `${s} ${o}` : s;
}
function as(e) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "wf-expression-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info";
    return /* @__PURE__ */ r.jsxs("p", { className: `wf-expression-editor-diagnostic ${i}`, children: [
      t.code ? /* @__PURE__ */ r.jsx("span", { children: t.code }) : null,
      t.message
    ] }, `${t.code ?? "diagnostic"}-${n}`);
  }) });
}
function gS(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function mS(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !kf.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function yS(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !kf.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function xS({
  context: e,
  selectedNode: t,
  selectedNodeLabel: n,
  selectedActivityType: i,
  selectedDescriptor: o,
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
  onSelectedActivityChange: x,
  onEnterSlot: y,
  onReplaceSlotActivity: j
}) {
  const [g, w] = O(null);
  return g && g.nodeId !== t?.nodeId && w(null), t ? /* @__PURE__ */ r.jsxs("div", { className: "wf-inspector-content", children: [
    /* @__PURE__ */ r.jsx("h3", { children: n }),
    c ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted wf-inspector-owner-hint", children: "Container of this canvas — select a node to inspect it instead." }) : null,
    /* @__PURE__ */ r.jsxs("dl", { children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Node ID" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.nodeId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity type" }),
      /* @__PURE__ */ r.jsx("dd", { children: i }),
      /* @__PURE__ */ r.jsx("dt", { children: "Activity version" }),
      /* @__PURE__ */ r.jsx("dd", { children: t.activityVersionId })
    ] }),
    s ? /* @__PURE__ */ r.jsxs("div", { className: "wf-availability-notice", children: [
      /* @__PURE__ */ r.jsx(Bn, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        qn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      aS,
      {
        activity: t,
        descriptor: o,
        editors: f,
        expressionEditors: p,
        expressionDescriptors: h,
        descriptorStatus: m,
        visibleVariables: v.visibleVariables,
        scopeStatus: v.status,
        onChange: x
      }
    ),
    d ? /* @__PURE__ */ r.jsx("div", { className: "wf-container-variables", children: /* @__PURE__ */ r.jsx(
      Nj,
      {
        context: e,
        variables: Ql(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: jh(v.shadowingWarnings, t.nodeId),
        onChange: (S) => x(wh(t, S))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((S) => {
        const b = vt(n, S);
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => y(t.nodeId, S, b), children: [
            S.label,
            /* @__PURE__ */ r.jsx("small", { children: iN(S, l) })
          ] }),
          S.cardinality === "single" ? /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${S.activities.length > 0 ? "Change" : "Choose"} ${S.label} activity`,
              title: S.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (I) => w({ nodeId: t.nodeId, slotId: S.id, clientX: I.clientX, clientY: I.clientY }),
              children: /* @__PURE__ */ r.jsx(ip, { size: 14 })
            }
          ) : null
        ] }, S.id);
      })
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    g ? /* @__PURE__ */ r.jsx(
      bf,
      {
        clientX: g.clientX,
        clientY: g.clientY,
        activities: u,
        onPick: (S) => {
          w(null);
          const b = a.find((I) => I.id === g.slotId);
          b && j(t.nodeId, b, vt(n, b), S);
        },
        onClose: () => w(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Df = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function $f({ label: e, hint: t }) {
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function wS({ value: e, onChange: t }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Df.map((n) => {
    const i = e === n.value;
    return /* @__PURE__ */ r.jsxs("label", { className: "wf-root-card", "data-checked": i || void 0, children: [
      /* @__PURE__ */ r.jsx(
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
      /* @__PURE__ */ r.jsx($f, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function vS({ onPick: e }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Df.map((t) => /* @__PURE__ */ r.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ r.jsx($f, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function bS({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const o = (s) => {
    const a = ff(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ r.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ r.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ r.jsx(vS, { onPick: o }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ r.jsx(ri, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function jS({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: o,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = _N(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
    loadDraft: m,
    replaceDraftByBatch: v,
    editDraft: x,
    editDraftAndSelect: y,
    select: j,
    navigateToScope: g,
    resetToRoot: w,
    startTestRun: S,
    clearTestRun: b,
    setPublishedArtifact: I
  } = u, [A, D] = O(""), [M, k] = O(""), [R, L] = O("idle"), [C, _] = O(() => /* @__PURE__ */ new Set()), [N, E] = O(""), [T, $] = O("activities"), [z, F] = O("inspector"), [B, Z] = O("designer"), {
    paletteWidth: H,
    inspectorWidth: U,
    paletteCollapsed: G,
    inspectorCollapsed: X,
    maximizedSidePanel: P,
    setInspectorCollapsed: q,
    paletteExpanded: de,
    inspectorExpanded: le,
    editorBodyClassName: te,
    editorBodyStyle: re,
    toggleSidePanelCollapsed: fe,
    toggleSidePanelMaximized: W,
    startSidePanelResize: ne,
    handleSidePanelResizeKeyDown: ge
  } = aa(), { resetHistory: ye, undo: Ee, redo: Re, canUndoNow: Ve, canRedoNow: ct } = kN({ draft: l, restoreDraft: m }), { saveDraft: Ye, autosaveEnabled: Ze, setAutosaveEnabled: Me, markSaved: Oe } = GN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: k, setError: D }), {
    details: we,
    setDetails: Ue,
    catalog: _e,
    activityDescriptors: Et,
    availabilityDiagnostics: bn,
    expressionDescriptors: V,
    descriptorStatus: K,
    reload: Y
  } = QN({ context: e, definitionId: t, resetHistory: ye, loadDraft: m, markSaved: Oe, setError: D }), { updateDefinitionMeta: oe } = eS({ context: e, details: we, setDetails: Ue, setStatus: k }), {
    catalogByVersion: ee,
    availabilityLookup: ve,
    scopeOwner: pe,
    isUnsupportedDesigner: be,
    scope: Se,
    selectedNode: He,
    selectedDescriptor: De,
    selectedSlots: mi,
    inspectedNode: We,
    inspectedIsScopeOwner: Ut,
    inspectedDescriptor: At,
    inspectedNodeAvailability: Lr,
    inspectedSlots: jn,
    inspectedSupportsScopedVariables: zr,
    scopedVariableAnalysis: Vr,
    isFlowchartDesigner: lt,
    canAddActivitiesToCanvas: Or
  } = iS({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: _e, activityDescriptors: Et, availabilityDiagnostics: bn }), Yt = ie(() => es(_e), [_e]), Hr = ie(() => {
    const J = N.trim().toLowerCase();
    if (!J) return Yt;
    const ue = _e.filter((me) => Ie(me).toLowerCase().includes(J) || me.activityTypeKey.toLowerCase().includes(J) || (me.category ?? "").toLowerCase().includes(J) || (me.description ?? "").toLowerCase().includes(J));
    return es(ue);
  }, [_e, N, Yt]), Zt = R !== "idle", Wr = !!l?.state.rootActivity && !Zt, yi = St(n, "weaver.workflows.find-draft-risks"), xi = St(n, "weaver.workflows.propose-update"), Fr = ON({
    draft: l,
    scope: Se,
    scopeOwner: pe,
    frames: d,
    catalog: _e,
    catalogByVersion: ee,
    isUnsupportedDesigner: be,
    isFlowchartDesigner: lt,
    canAddActivitiesToCanvas: Or,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: y,
    select: j,
    resetToRoot: w,
    setStatus: k,
    setError: D
  }), {
    nodes: Jt,
    edges: wi,
    canvasRef: vi,
    setReactFlowInstance: bi,
    connectMenu: Nn,
    setConnectMenu: Br,
    edgeActions: Kr,
    onNodesChange: qr,
    onEdgesChange: Xr,
    onNodesDelete: Ur,
    onEdgesDelete: ji,
    isValidConnection: Yr,
    onConnect: Zr,
    onConnectStart: Jr,
    onConnectEnd: Gr,
    onReconnect: Qr,
    commitLayout: eo,
    canAutoLayout: to,
    autoLayout: no,
    onCanvasDragOver: Sn,
    onCanvasDragLeave: Ni,
    onCanvasDrop: Si,
    openEmptyConnectMenu: Ci,
    onConnectMenuPick: io,
    addActivity: Ii,
    onPaletteClick: ro,
    onPaletteDragStart: oo,
    onPaletteDragEnd: so,
    onPalettePointerDown: ki
  } = Fr, ao = !be && !lt && d.length > 0 && !!Se && Se.slot.activities.length === 0 && Jt.length === 0, Ei = se((J) => {
    const ue = Ii(J);
    if (!ue || !Se || !pe || d.length === 0) return;
    const me = d[d.length - 1].label, Ce = On(d, pe, pe.nodeId, { ...Se.slot, activities: [ue] }, me, ee);
    Ce && Ce.frames.length > d.length && g(Ce.frames, Ce.selectedNodeId);
  }, [Ii, ee, d, g, Se, pe]);
  JN({ draft: l, details: we, catalog: _e, replaceDraftByBatch: v, setStatus: k, setError: D }), Q(() => {
    !l?.state.rootActivity || _e.length === 0 || x(({ draft: J }) => {
      if (!J?.state.rootActivity) return null;
      const ue = Xl(J.state.rootActivity, ee);
      return !ue || ue === J.state.rootActivity ? null : {
        ...J,
        state: {
          ...J.state,
          rootActivity: ue
        }
      };
    });
  }, [_e.length, ee, l?.state.rootActivity, x]), rS({ details: we, draft: l, selectedNode: He, selectedNodeId: f, selectedDescriptor: De, inspectedNode: We, inspectedDescriptor: At, inspectedIsScopeOwner: Ut, catalogByVersion: ee });
  const Ai = ie(() => !we || !l ? null : {
    definition: we.definition,
    draft: l,
    selectedActivity: He,
    selectedActivityDescriptor: De,
    selectedActivitySlots: mi,
    inspectedActivity: We,
    inspectedActivityDescriptor: At,
    inspectedActivitySlots: jn,
    inspectedIsScopeOwner: Ut,
    catalog: _e,
    currentScopeOwner: pe,
    frames: d
  }, [_e, we, l, d, At, Ut, We, jn, pe, De, He, mi]);
  Q(() => {
    _((J) => {
      let ue = !1;
      const me = new Set(J);
      for (const Ce of Yt)
        me.has(Ce.category) || (me.add(Ce.category), ue = !0);
      return ue ? me : J;
    });
  }, [Yt]);
  const { exportJson: co, save: Cn, promoteAndPublish: lo, run: uo } = tS({
    context: e,
    draft: l,
    details: we,
    busy: Zt,
    saveDraft: Ye,
    reload: Y,
    startTestRun: S,
    clearTestRun: b,
    setPublishedArtifact: I,
    setOperation: L,
    setStatus: k,
    setError: D,
    setActiveRightPanelId: F,
    setInspectorCollapsed: q
  }), _i = se((J) => {
    x(({ draft: ue }) => ue ? { ...ue, state: J(ue.state) } : null);
  }, [x]), Pf = se((J) => {
    if (!l) return "No draft is loaded.";
    const ue = rj(J, l);
    return ue.ok ? (m(ue.draft), k("Applied workflow JSON."), null) : ue.error;
  }, [l, m]);
  Q(() => {
    const J = (ue) => {
      if (B !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const me = ue.target;
      if (me && (me.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(me.tagName))) return;
      const Ce = ue.key.toLowerCase();
      Ce === "z" && !ue.shiftKey ? (ue.preventDefault(), Ee()) : (Ce === "z" && ue.shiftKey || Ce === "y") && (ue.preventDefault(), Re());
    };
    return window.addEventListener("keydown", J), () => window.removeEventListener("keydown", J);
  }, [B, Ee, Re]);
  const fo = se((J, ue, me) => {
    const Ce = On(d, pe, J, ue, me, ee);
    Ce && g(Ce.frames, Ce.selectedNodeId);
  }, [ee, d, g, pe]), Mf = se((J, ue, me, Ce) => {
    const Di = ue.activities.length > 0, ma = nr(Ce, ts(Ce));
    x(({ draft: $i }) => {
      const ya = $i?.state.rootActivity;
      return !$i || !ya ? null : {
        ...$i,
        state: {
          ...$i.state,
          rootActivity: Vo(ya, J, (Kf) => yn(Kf, ue, [ma]), ee)
        }
      };
    });
    const go = On(d, pe, J, { ...ue, activities: [ma] }, me, ee);
    go && g(go.frames, go.selectedNodeId), D(""), k(Di ? `Replaced ${ue.label} content` : `Assigned ${Ie(Ce)} to ${ue.label}`);
  }, [ee, x, d, g, pe]), Lf = ie(() => be ? null : (J, ue, me) => fo(J, me, vt(ue, me)), [fo, be]), zf = se((J) => {
    x(({ draft: ue }) => {
      const me = ue?.state.rootActivity;
      return !ue || !me ? null : {
        ...ue,
        state: {
          ...ue.state,
          rootActivity: Vo(me, J.nodeId, () => J, ee)
        }
      };
    });
  }, [ee, x]), Vf = se((J) => {
    if (!J) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const me = Wp(ue, J, (Ce) => {
      const Di = ee.get(Ce.activityVersionId);
      return Di ? Ie(Di) : Ce.nodeId;
    }, ee);
    me && (Z("designer"), g(me, J), q(!1));
  }, [l?.state.rootActivity, ee, q, g]), Of = (J) => {
    _((ue) => {
      const me = new Set(ue);
      return me.has(J) ? me.delete(J) : me.add(J), me;
    });
  }, In = l && p?.draftSignature === Be(l) ? p.view : null, Hf = nS(e, t, In);
  if (!we || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: A || "Loading workflow editor..." });
  const da = We ? ee.get(We.activityVersionId) : void 0, Wf = We ? Jt.find((J) => J.id === We.nodeId)?.data.label ?? (da ? Ie(da) : We.nodeId) : "", fa = In && M.startsWith("Test run") ? "" : M, Ff = (J) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(J)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, pa = s.map((J) => {
    const ue = J.component;
    return {
      id: J.id,
      title: J.title,
      side: J.side,
      order: J.order ?? 500,
      icon: null,
      render: () => Ai && /* @__PURE__ */ r.jsx(ue, { context: Ai })
    };
  }), po = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(ri, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        oS,
        {
          paletteSearch: N,
          onSearchChange: E,
          groups: Hr,
          expandedCategories: C,
          onToggleCategory: Of,
          onActivityClick: ro,
          onActivityDragStart: oo,
          onActivityDragEnd: so,
          onActivityPointerDown: ki
        }
      )
    },
    ...pa.filter((J) => J.side === "left")
  ].sort(hl), ho = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(yr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        xS,
        {
          context: e,
          selectedNode: We,
          selectedNodeLabel: Wf,
          selectedActivityType: We ? At?.typeName ?? ee.get(We.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: At,
          selectedNodeAvailability: Lr,
          selectedSlots: jn,
          inspectingScopeOwner: Ut,
          catalog: _e,
          catalogByVersion: ee,
          selectedSupportsScopedVariables: zr,
          propertyEditors: i,
          expressionEditors: o,
          expressionDescriptors: V,
          descriptorStatus: K,
          scopedVariableAnalysis: Vr,
          onSelectedActivityChange: zf,
          onEnterSlot: fo,
          onReplaceSlotActivity: Mf
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Mt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(xN, { testRun: In, publishedEquivalent: Hf, onOpenRun: Ff })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx(Rl, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        bN,
        {
          context: e,
          ai: n,
          definitionId: we.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...pa.filter((J) => J.side === "right")
  ].sort(hl), ha = po.find((J) => J.id === T) ?? po[0], ga = ho.find((J) => J.id === z) ?? ho[0], Bf = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(ps, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(cp, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(pt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: we.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      fa ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(Xt, { size: 13 }),
        " ",
        fa
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
              disabled: !Ve,
              onClick: Ee,
              children: /* @__PURE__ */ r.jsx(rp, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Redo",
              title: "Redo (Ctrl+Shift+Z)",
              disabled: !ct,
              onClick: Re,
              children: /* @__PURE__ */ r.jsx(op, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !to,
              onClick: no,
              children: /* @__PURE__ */ r.jsx(sp, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Ze, onChange: (J) => Me(J.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        yi ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(n, yi, { definition: we.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Risks"
        ] }) : null,
        xi ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(n, xi, { definition: we.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: co, children: [
          /* @__PURE__ */ r.jsx(ap, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Zt, onClick: () => {
          Cn();
        }, children: [
          /* @__PURE__ */ r.jsx(ls, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: Zt, onClick: () => {
          lo();
        }, children: [
          /* @__PURE__ */ r.jsx(_l, { size: 15 }),
          " Promote"
        ] }),
        In ? /* @__PURE__ */ r.jsx(
          yN,
          {
            testRun: In,
            onOpenDetails: () => {
              F("runtime"), q(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !Wr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              uo();
            },
            children: [
              /* @__PURE__ */ r.jsx(Mt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    A ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 16 }),
      " ",
      A
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: te, style: re, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Fn,
            {
              label: "Activities panel tabs",
              tabs: po,
              activeTabId: ha.id,
              onSelect: $
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": G ? "Expand activities panel" : "Collapse activities panel",
                title: G ? "Expand" : "Collapse",
                onClick: () => fe("palette"),
                children: G ? /* @__PURE__ */ r.jsx(pt, { size: 14 }) : /* @__PURE__ */ r.jsx(zt, { size: 14 })
              }
            ),
            G ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "palette" ? "Restore activities panel" : "Maximize activities panel",
                title: P === "palette" ? "Restore" : "Maximize",
                onClick: () => W("palette"),
                children: P === "palette" ? /* @__PURE__ */ r.jsx(er, { size: 14 }) : /* @__PURE__ */ r.jsx(sn, { size: 14 })
              }
            )
          ] })
        ] }),
        de ? ha.render() : null
      ] }),
      de && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Mn,
          "aria-valuemax": Ln,
          "aria-valuenow": H,
          tabIndex: 0,
          onPointerDown: (J) => ne("palette", J),
          onKeyDown: (J) => ge("palette", J)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          Fn,
          {
            label: "Editor view tabs",
            tabs: Bf,
            activeTabId: B,
            onSelect: (J) => Z(J)
          }
        ) }),
        B === "code" ? /* @__PURE__ */ r.jsx(uj, { draft: l, onApply: Pf }) : B === "properties" ? /* @__PURE__ */ r.jsx(Cj, { details: we, draft: l, context: e, onStateChange: _i, onDefinitionMetaChange: oe }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsx(ia, { frames: d, onNavigate: (J) => g(J, null) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: vi, onDragOver: Sn, onDragLeave: Ni, onDrop: Si, children: [
            /* @__PURE__ */ r.jsx(cf.Provider, { value: Kr, children: /* @__PURE__ */ r.jsx(lf.Provider, { value: ve, children: /* @__PURE__ */ r.jsx(uf.Provider, { value: Lf, children: /* @__PURE__ */ r.jsxs(
              qs,
              {
                nodes: Jt,
                edges: wi,
                nodeTypes: ta,
                edgeTypes: na,
                onInit: bi,
                onNodesChange: qr,
                onEdgesChange: Xr,
                onNodesDelete: Ur,
                onEdgesDelete: ji,
                onConnect: Zr,
                onConnectStart: lt ? Jr : void 0,
                onConnectEnd: lt ? Gr : void 0,
                onReconnect: lt ? Qr : void 0,
                isValidConnection: Yr,
                onDragOver: Sn,
                onDragLeave: Ni,
                onDrop: Si,
                onPaneClick: () => j(null),
                onNodeClick: (J, ue) => j(ue.id),
                onNodeDragStop: be ? void 0 : eo,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: lt,
                nodesDraggable: !be,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: be ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(Xs, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(Us, {}),
                  /* @__PURE__ */ r.jsx(Ys, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            ao ? /* @__PURE__ */ r.jsx(
              bS,
              {
                slotLabel: Se?.slot.label ?? "this slot",
                catalog: _e,
                onPickActivity: Ei,
                onBrowseAll: Ci
              }
            ) : lt && Jt.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => Ci(), children: [
              /* @__PURE__ */ r.jsx(on, { size: 15 }),
              " Add activity"
            ] }) : null,
            Nn ? /* @__PURE__ */ r.jsx(
              bf,
              {
                clientX: Nn.clientX,
                clientY: Nn.clientY,
                activities: _e,
                onPick: io,
                onClose: () => Br(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(mN, { draft: l, onRepair: Vf })
        ] })
      ] }),
      le && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": $t,
          "aria-valuemax": Tt,
          "aria-valuenow": U,
          tabIndex: 0,
          onPointerDown: (J) => ne("inspector", J),
          onKeyDown: (J) => ge("inspector", J)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            Fn,
            {
              label: "Inspector panel tabs",
              tabs: ho,
              activeTabId: ga.id,
              onSelect: F
            }
          ),
          /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
            /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": X ? "Expand inspector panel" : "Collapse inspector panel",
                title: X ? "Expand" : "Collapse",
                onClick: () => fe("inspector"),
                children: X ? /* @__PURE__ */ r.jsx(zt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
              }
            ),
            X ? null : /* @__PURE__ */ r.jsx(
              "button",
              {
                type: "button",
                className: "wf-panel-action-button",
                "aria-label": P === "inspector" ? "Restore inspector panel" : "Maximize inspector panel",
                title: P === "inspector" ? "Restore" : "Maximize",
                onClick: () => W("inspector"),
                children: P === "inspector" ? /* @__PURE__ */ r.jsx(er, { size: 14 }) : /* @__PURE__ */ r.jsx(sn, { size: 14 })
              }
            )
          ] })
        ] }),
        le ? ga.render() : null
      ] })
    ] })
  ] });
}
function NS({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: o }) {
  const s = df(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: Aj.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(zt, { size: 14 }),
        " Previous"
      ] }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "Page ",
        e,
        " of ",
        s
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e + 1), disabled: e >= s, "aria-label": "Next page", title: "Next page", children: [
        "Next ",
        /* @__PURE__ */ r.jsx(pt, { size: 14 })
      ] })
    ] })
  ] });
}
function SS({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = O(!1), [l, d] = O(""), [f, p] = O(!1), [h, m] = O(null), [v, x] = O(null), y = ae(null), j = ae(e);
  j.current = e;
  const g = ae(o);
  g.current = o;
  const w = se((b) => {
    const I = { ...j.current };
    b.name && (I.name = b.name), b.description && (I.description = b.description), g.current(I), m(null), x(null);
  }, []);
  Q(() => {
    if (i)
      return n.onPromptResult((b) => {
        if (b.requestId !== y.current) return;
        if (y.current = null, p(!1), b.status !== "completed") {
          x(b.status === "cancelled" ? "Weaver needs more detail — continue in the assistant panel." : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
          return;
        }
        const I = Rj(b.text);
        if (!I) {
          x("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
          return;
        }
        b.autoApply ? w(I) : m(I);
      });
  }, [n, i, w]);
  const S = () => {
    if (!i) return;
    const b = i.createPrompt({ draft: j.current, intent: l });
    if (!b) return;
    const I = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    y.current = I, p(!0), m(null), x(null), n.dispatchPrompt({ ...b, requestId: I });
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
          i ? /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-ai-action",
              "aria-expanded": c,
              onClick: () => u((b) => !b),
              title: i.description ?? i.label,
              children: [
                /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                " ",
                i.label
              ]
            }
          ) : null
        ] }),
        i && c ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest", role: "group", "aria-label": "Suggest name and description", children: [
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
                  (b.metaKey || b.ctrlKey) && b.key === "Enter" && (b.preventDefault(), S());
                }
              }
            )
          ] }),
          /* @__PURE__ */ r.jsx("div", { className: "wf-ai-suggest-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-ai-action", onClick: S, disabled: f, children: [
            /* @__PURE__ */ r.jsx(rt, { size: 13 }),
            " ",
            f ? "Generating…" : "Generate"
          ] }) }),
          v ? /* @__PURE__ */ r.jsx("p", { className: "wf-ai-suggest-error", role: "alert", children: v }) : null,
          h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-preview", children: [
            h.name ? /* @__PURE__ */ r.jsx("p", { children: /* @__PURE__ */ r.jsx("strong", { children: h.name }) }) : null,
            h.description ? /* @__PURE__ */ r.jsx("p", { children: h.description }) : null,
            /* @__PURE__ */ r.jsxs("div", { className: "wf-ai-suggest-actions", children: [
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => w(h), children: "Apply" }),
              /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => m(null), children: "Dismiss" })
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
              onChange: (b) => o({ ...e, name: b.target.value })
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
              onChange: (b) => o({ ...e, description: b.target.value })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("fieldset", { className: "wf-form-field wf-root-field", children: [
          /* @__PURE__ */ r.jsx("legend", { children: "Root activity" }),
          /* @__PURE__ */ r.jsx(
            wS,
            {
              value: e.rootKind,
              onChange: (b) => o({ ...e, rootKind: b, rootActivityVersionId: null })
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
function CS({ context: e, ai: t, onOpen: n }) {
  const [i, o] = O(""), [s, a] = O("active"), [c, u] = O(1), [l, d] = O(_j), [f, p] = O("loading"), [h, m] = O(""), [v, x] = O(""), [y, j] = O([]), [g, w] = O(0), [S, b] = O(() => /* @__PURE__ */ new Set()), [I, A] = O(null), [D, M] = O(!1), [k, R] = O([]), [L, C] = O("idle"), _ = ae(null), N = ie(() => y.map((W) => W.id), [y]), E = St(t, "weaver.workflows.suggest-create-metadata"), T = St(t, "weaver.workflows.explain-definition"), $ = N.filter((W) => S.has(W)).length, z = N.length > 0 && $ === N.length, F = se(async () => {
    p("loading"), m("");
    try {
      const W = await _h(e, { search: i, state: s, page: c, pageSize: l }), ne = typeof W.totalCount == "number", ge = W.totalCount ?? W.definitions.length, ye = df(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      j(ne ? W.definitions : Tj(W.definitions, c, l)), w(ge), p("ready");
    } catch (W) {
      m(W instanceof Error ? W.message : String(W)), p("failed");
    }
  }, [e, i, s, c, l]);
  Q(() => {
    F();
  }, [F]), Q(() => {
    _.current && (_.current.indeterminate = $ > 0 && !z);
  }, [z, $]);
  const B = se(async () => {
    if (!(L === "loading" || L === "ready")) {
      C("loading");
      try {
        const W = await ci(e);
        R(W.activities ?? []), C("ready");
      } catch (W) {
        C("failed"), m(W instanceof Error ? W.message : String(W));
      }
    }
  }, [L, e]), Z = () => {
    m(""), x(""), A({ name: "", description: "", rootKind: "flowchart" }), B();
  }, H = async () => {
    if (I?.name.trim()) {
      M(!0), m(""), x("");
      try {
        const W = await Rh(e, {
          name: I.name.trim(),
          description: I.description.trim() || null,
          rootKind: I.rootKind,
          rootActivityVersionId: Pj(I, k)
        });
        A(null), n(W.definition.id);
      } catch (W) {
        m(W instanceof Error ? W.message : String(W));
      } finally {
        M(!1);
      }
    }
  }, U = (W) => {
    window.history.pushState({}, "", `/workflows/executables?definition=${encodeURIComponent(W)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, G = async () => {
    if (y.length === 1 && c > 1) {
      u(c - 1);
      return;
    }
    await F();
  }, X = () => b(/* @__PURE__ */ new Set()), P = (W, ne) => {
    b((ge) => {
      const ye = new Set(ge);
      return ne ? ye.add(W) : ye.delete(W), ye;
    });
  }, q = (W) => {
    b((ne) => {
      const ge = new Set(ne);
      for (const ye of N)
        W ? ge.add(ye) : ge.delete(ye);
      return ge;
    });
  }, de = (W) => {
    a(W), u(1), X();
  }, le = (W) => {
    o(W), u(1), X();
  }, te = async (W) => {
    if (await tr().confirm({ message: `Delete workflow definition "${W.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), m("");
      try {
        await Ph(e, W.id), P(W.id, !1), x(`Deleted ${W.name}`), await G();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  }, re = async (W) => {
    x(""), m("");
    try {
      await Mh(e, W.id), P(W.id, !1), x(`Restored ${W.name}`), await G();
    } catch (ne) {
      m(ne instanceof Error ? ne.message : String(ne));
    }
  }, fe = async (W) => {
    if (await tr().confirm({ message: `Permanently delete workflow definition "${W.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), m("");
      try {
        await Lh(e, W.id), P(W.id, !1), x(`Permanently deleted ${W.name}`), await G();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  };
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-segmented", role: "tablist", "aria-label": "Definition state", children: [
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "active" ? "active" : "", "aria-selected": s === "active", onClick: () => de("active"), children: "Active" }),
        /* @__PURE__ */ r.jsx("button", { type: "button", className: s === "deleted" ? "active" : "", "aria-selected": s === "deleted", onClick: () => de("deleted"), children: "Deleted" })
      ] }),
      /* @__PURE__ */ r.jsxs("label", { className: "wf-search", children: [
        /* @__PURE__ */ r.jsx(xr, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: i, onChange: (W) => le(W.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(on, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(It, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(kt, { size: 16 }),
      " ",
      h
    ] }) : null,
    v ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(Xt, { size: 14 }),
      " ",
      v
    ] }) : null,
    S.size > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-selection-bar", "aria-live": "polite", children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        S.size,
        " selected"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: X, children: "Clear selection" })
    ] }) : null,
    f === "loading" ? /* @__PURE__ */ r.jsx(ra, {}) : null,
    f === "ready" && y.length === 0 ? /* @__PURE__ */ r.jsx(
      oa,
      {
        icon: /* @__PURE__ */ r.jsx(Rl, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ r.jsx(on, { size: 15 }),
          " Create workflow"
        ] })
      }
    ) : null,
    f === "ready" && y.length > 0 ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("div", { className: "wf-grid", role: "table", "aria-label": "Workflow definitions", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-grid-head", role: "row", children: [
          /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", children: /* @__PURE__ */ r.jsx(
            "input",
            {
              ref: _,
              type: "checkbox",
              checked: z,
              onChange: (W) => q(W.target.checked),
              "aria-label": "Select visible workflow definitions"
            }
          ) }),
          /* @__PURE__ */ r.jsx("span", { children: "Name" }),
          /* @__PURE__ */ r.jsx("span", { children: "Latest version" }),
          /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? "Deleted" : "Draft" }),
          /* @__PURE__ */ r.jsx("span", { children: "Modified" }),
          /* @__PURE__ */ r.jsx("span", { children: "Actions" })
        ] }),
        y.map((W) => /* @__PURE__ */ r.jsxs(
          "div",
          {
            className: "wf-grid-row",
            role: "row",
            "aria-label": `Open workflow definition ${W.name}`,
            "aria-selected": S.has(W.id),
            tabIndex: 0,
            onClick: () => n(W.id),
            onKeyDown: (ne) => {
              ne.currentTarget === ne.target && (ne.key !== "Enter" && ne.key !== " " || (ne.preventDefault(), n(W.id)));
            },
            children: [
              /* @__PURE__ */ r.jsx("label", { className: "wf-row-select", onClick: (ne) => ne.stopPropagation(), children: /* @__PURE__ */ r.jsx(
                "input",
                {
                  type: "checkbox",
                  checked: S.has(W.id),
                  onChange: (ne) => P(W.id, ne.target.checked),
                  "aria-label": `Select workflow definition ${W.name}`
                }
              ) }),
              /* @__PURE__ */ r.jsxs("span", { children: [
                /* @__PURE__ */ r.jsx("strong", { children: W.name }),
                /* @__PURE__ */ r.jsx("small", { children: W.description || W.id })
              ] }),
              /* @__PURE__ */ r.jsx("span", { children: W.latestVersion ?? "No version" }),
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? $e(W.deletedAt) : W.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: $e(W.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (ne) => ne.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), n(W.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), U(W.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(t, T, W), children: [
                  /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(Lt, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  re(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(oi, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(Lt, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          W.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        NS,
        {
          page: c,
          pageSize: l,
          totalCount: g,
          onPageChange: u,
          onPageSizeChange: (W) => {
            d(W), u(1);
          }
        }
      )
    ] }) : null,
    I ? /* @__PURE__ */ r.jsx(
      SS,
      {
        draft: I,
        creating: D,
        ai: t,
        suggestMetadataAction: E,
        onChange: (W) => A(W),
        onClose: () => A(null),
        onSubmit: H
      }
    ) : null
  ] });
}
function IS(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const s of t)
    n.set(s.activityTypeKey, [...n.get(s.activityTypeKey) ?? [], s]);
  const i = /* @__PURE__ */ new Map(), o = (s) => {
    const a = AS(s, n), c = s.authoredActivityId || s.executableNodeId;
    return i.set(c, {
      executableNodeId: s.executableNodeId,
      authoredActivityId: s.authoredActivityId,
      activityType: s.activityType,
      activityTypeVersion: s.activityTypeVersion,
      structureKind: s.structureKind ?? null,
      available: !!a,
      inputBindings: s.inputBindings ?? []
    }), {
      nodeId: c,
      // A catalog miss gets a synthetic version id no catalog can resolve, so downstream lookups
      // treat the node as unknown; the canvas layer relabels it and marks it as a ghost.
      activityVersionId: a?.activityVersionId ?? _S(s),
      inputs: [],
      outputs: [],
      structure: DS(s, a, o)
    };
  };
  return { root: o(e), factsByNodeId: i };
}
function kS(e) {
  return !!e && !e.available;
}
function ES(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function AS(e, t) {
  const n = t.get(e.activityType) ?? [];
  return n.find((i) => i.version === e.activityTypeVersion) ?? n[0];
}
function _S(e) {
  return `executable-missing:${e.activityType}@${e.activityTypeVersion}`;
}
function DS(e, t, n) {
  const i = e.childSlots ?? [];
  if (i.length === 0) return null;
  const o = jr(t), s = o && (!e.structureKind || o.kind === e.structureKind) ? $S(i, o.payload.slots) : null;
  if (o && s) {
    const c = { ...o.payload.initialPayload };
    for (const { slot: u, descriptor: l } of s) {
      const d = u.activities.map(n);
      c[l.property] = l.cardinality === "single" ? d[0] ?? null : d;
    }
    return { kind: o.kind, schemaVersion: o.schemaVersion, payload: c };
  }
  const a = {};
  for (const c of i)
    a[RS(a, TS(c.name))] = c.activities.map(n);
  return {
    kind: e.structureKind ?? `executable:${e.activityType}`,
    schemaVersion: "1.0.0",
    payload: a
  };
}
function $S(e, t) {
  const n = [];
  for (const i of e) {
    const o = t.find((s) => s.name === i.name);
    if (!o || o.collectionProperty || o.childProperty) return null;
    n.push({ slot: i, descriptor: o });
  }
  return n;
}
function TS(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.charAt(0).toLowerCase() + t.slice(1);
}
function RS(e, t) {
  if (!(t in e)) return t;
  let n = 2;
  for (; `${t}${n}` in e; ) n += 1;
  return `${t}${n}`;
}
function PS({ context: e, ai: t, artifactId: n, sourceReferenceId: i, onSelectReference: o }) {
  const [s, a] = O("loading"), [c, u] = O(""), [l, d] = O(null), [f, p] = O({ status: "idle" }), [h, m] = O(""), [v, x] = O(""), [y, j] = O(!1), [g, w] = O(null), [S, b] = O([]), [I, A] = O(null), {
    inspectorWidth: D,
    inspectorCollapsed: M,
    maximizedSidePanel: k,
    inspectorExpanded: R,
    editorBodyStyle: L,
    toggleSidePanelCollapsed: C,
    toggleSidePanelMaximized: _,
    startSidePanelResize: N,
    handleSidePanelResizeKeyDown: E
  } = aa(), T = St(t, "weaver.workflows.explain-executable"), $ = se(async () => {
    if (!n) {
      u("No executable artifact id was provided."), a("failed");
      return;
    }
    a("loading"), u("");
    try {
      const [X, P] = await Promise.all([
        Bh(e, n, i),
        ci(e)
      ]);
      d({ detail: X, activityCatalog: P.activities }), b([]), A(null), a("ready");
    } catch (X) {
      d(null), u(KS(X, n)), a("failed");
    }
  }, [n, e, i]);
  Q(() => {
    $();
  }, [$]);
  const z = ie(() => FS(l?.detail ?? null), [l]);
  Q(() => {
    const X = z?.definitionId;
    if (!X) {
      p({ status: "absent" });
      return;
    }
    let P = !1;
    return p({ status: "loading" }), tu(e, X).then(
      (q) => {
        P || p({ status: "ready", definition: q.definition });
      },
      (q) => {
        P || p(Tf(q) ? { status: "absent" } : { status: "failed", error: q instanceof Error ? q.message : String(q) });
      }
    ), () => {
      P = !0;
    };
  }, [z?.definitionId, e]);
  const F = ie(
    () => l ? IS(l.detail.rootActivity, l.activityCatalog) : null,
    [l]
  ), B = async () => {
    if (!(!l || y)) {
      j(!0), m(""), x(""), w(null);
      try {
        const X = await bs(e, l.detail.artifactId), P = Gs(X);
        w({ artifactId: l.detail.artifactId, workflowExecutionId: P }), m(`Started ${l.detail.artifactId}`);
      } catch (X) {
        x(Qs(X));
      } finally {
        j(!1);
      }
    }
  }, Z = () => {
    !l || !T || Ct(t, T, {
      ...l.detail,
      artifactVersion: z?.artifactVersion ?? "",
      definitionId: z?.definitionId ?? "",
      definitionVersionId: z?.definitionVersionId ?? "",
      publishedAt: z?.publishedAt ?? null,
      sourceKind: z?.sourceKind ?? null,
      sourceId: z?.sourceId ?? null,
      sourceVersion: z?.sourceVersion ?? null
    }) && (x(""), w(null), m(`Sent ${l.detail.artifactId} to Weaver`));
  }, H = () => {
    window.history.pushState({}, "", "/workflows/executables"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, U = () => {
    const X = z?.definitionId;
    X && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(X)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, G = [
    "wf-instance-detail-workbench",
    M ? "inspector-collapsed" : "",
    k === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: H, children: [
        /* @__PURE__ */ r.jsx(zt, { size: 14 }),
        " Executables"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        $();
      }, children: [
        /* @__PURE__ */ r.jsx(oi, { size: 14 }),
        " Refresh"
      ] }),
      l ? /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: y, onClick: () => {
        B();
      }, children: [
        /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
        " ",
        y ? "Running..." : "Run"
      ] }) : null,
      l && T ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(rt, { size: 13 }),
        " Explain"
      ] }) : null,
      l ? /* @__PURE__ */ r.jsx(
        OS,
        {
          reference: z,
          sourceDefinition: f,
          onOpen: U
        }
      ) : null
    ] }),
    v ? /* @__PURE__ */ r.jsx(It, { message: v, title: "The executable could not be run" }) : null,
    h ? /* @__PURE__ */ r.jsx(sa, { status: h, run: g }) : null,
    s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading executable..." }) : null,
    s === "failed" ? /* @__PURE__ */ r.jsx(It, { message: c }) : null,
    s === "ready" && l && F ? /* @__PURE__ */ r.jsxs("div", { className: G, style: L, children: [
      /* @__PURE__ */ r.jsx(
        MS,
        {
          detail: l.detail,
          activityCatalog: l.activityCatalog,
          graph: F,
          chosenReference: z,
          frames: S,
          onNavigateToScope: b,
          selectedNodeId: I,
          onSelectNode: A
        }
      ),
      R && !k ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize executable details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": $t,
          "aria-valuemax": Tt,
          "aria-valuenow": D,
          tabIndex: 0,
          onPointerDown: (X) => N("inspector", X),
          onKeyDown: (X) => E("inspector", X)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        zS,
        {
          detail: l.detail,
          graph: F,
          chosenReference: z,
          sourceDefinition: f,
          selectedNodeId: I,
          onSelectReference: o,
          collapsed: M,
          expanded: R,
          maximized: k === "inspector",
          onToggleCollapsed: () => C("inspector"),
          onToggleMaximized: () => _("inspector")
        }
      )
    ] }) : null
  ] });
}
function MS({ detail: e, activityCatalog: t, graph: n, chosenReference: i, frames: o, onNavigateToScope: s, selectedNodeId: a, onSelectNode: c }) {
  const u = ie(
    () => LS(n, t, e.chosenReference?.layout ?? [], o, s, a),
    [t, e, o, n, s, a]
  ), l = o.length === 0 ? "root" : o.map((d) => `${d.ownerNodeId}:${d.slotId}`).join("/");
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-canvas-shell", "aria-label": "Executable canvas", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Executable" }),
        /* @__PURE__ */ r.jsxs("h3", { children: [
          e.artifactId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: i ? `Version ${i.artifactVersion}` : e.rootActivityType })
        ] })
      ] }),
      e.chosenReference ? /* @__PURE__ */ r.jsx("span", { className: "wf-executable-selection", children: BS(e.chosenReference.selection, e.chosenReference.sourceReferenceId) }) : /* @__PURE__ */ r.jsx("span", { className: "wf-executable-selection", children: "No source reference; automatic layout" })
    ] }),
    /* @__PURE__ */ r.jsx(ia, { className: "wf-instance-breadcrumb", frames: o, onNavigate: s }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-canvas", children: u.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "This executable has no renderable activities." }) : /* @__PURE__ */ r.jsxs(
      qs,
      {
        nodes: u.nodes,
        edges: u.edges,
        nodeTypes: ta,
        edgeTypes: na,
        fitView: !0,
        nodesDraggable: !1,
        nodesConnectable: !1,
        elementsSelectable: !0,
        onNodeClick: (d, f) => c(f.id),
        onPaneClick: () => c(null),
        children: [
          /* @__PURE__ */ r.jsx(Xs, {}),
          /* @__PURE__ */ r.jsx(Ys, { pannable: !0, zoomable: !0 }),
          /* @__PURE__ */ r.jsx(Us, {})
        ]
      },
      l
    ) })
  ] });
}
function LS(e, t, n, i, o, s = null) {
  const a = an(e.root, i, t), c = a?.owner ?? e.root, u = t.find((f) => f.activityVersionId === c.activityVersionId), d = ws(c, u) === "unsupported" || !a ? xs(c, t, n) : ys(a, t, n);
  return {
    nodes: d.nodes.map((f) => {
      const p = e.factsByNodeId.get(f.id), h = kS(p), m = f.id === s;
      return {
        ...f,
        draggable: !1,
        connectable: !1,
        deletable: !1,
        selected: m,
        data: {
          ...f.data,
          ...h && p ? { ghost: !0, label: ES(p.activityType) } : {},
          onEnterSlot: (v) => {
            const x = On(i, c, f.id, v, vt(f.data.label, v), t);
            x && o(x.frames);
          }
        }
      };
    }),
    edges: d.edges.map((f) => ({ ...f, deletable: !1 }))
  };
}
function zS({ detail: e, graph: t, chosenReference: n, sourceDefinition: i, selectedNodeId: o, onSelectReference: s, collapsed: a, expanded: c, maximized: u, onToggleCollapsed: l, onToggleMaximized: d }) {
  const [f, p] = O("identity"), [h, m] = O(""), v = [
    { id: "identity", title: "Identity", order: 0, icon: /* @__PURE__ */ r.jsx(lp, { size: 14 }), render: () => null },
    { id: "references", title: `References (${e.references.length})`, order: 1, icon: /* @__PURE__ */ r.jsx(yr, { size: 14 }), render: () => null }
  ], x = o ? t.factsByNodeId.get(o) : null;
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Executable details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(Fn, { label: "Executable details tabs", tabs: v, activeTabId: f, onSelect: (y) => p(y) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": a ? "Expand executable details panel" : "Collapse executable details panel",
            title: a ? "Expand" : "Collapse",
            onClick: l,
            children: a ? /* @__PURE__ */ r.jsx(zt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
          }
        ),
        a ? null : /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": u ? "Restore executable details panel" : "Maximize executable details panel",
            title: u ? "Restore" : "Maximize",
            onClick: d,
            children: u ? /* @__PURE__ */ r.jsx(er, { size: 14 }) : /* @__PURE__ */ r.jsx(sn, { size: 14 })
          }
        )
      ] })
    ] }),
    c ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsx("header", { children: /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Executable Artifact" }),
        /* @__PURE__ */ r.jsx("h3", { children: e.artifactId })
      ] }) }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: f === "identity" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
          /* @__PURE__ */ r.jsx("h4", { children: "Identity" }),
          /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
            /* @__PURE__ */ r.jsx("dt", { children: "Artifact ID" }),
            /* @__PURE__ */ r.jsxs("dd", { className: "wf-cell-line", children: [
              /* @__PURE__ */ r.jsx("code", { title: e.artifactId, children: e.artifactId }),
              /* @__PURE__ */ r.jsx(ht, { value: e.artifactId, ariaLabel: `Copy artifact ID ${e.artifactId}`, copiedLabel: "artifact ID", onCopied: (y) => m(`Copied ${y}`), onCopyFailed: (y) => m(`Could not copy ${y}.`) })
            ] }),
            /* @__PURE__ */ r.jsx("dt", { children: "Artifact Hash" }),
            /* @__PURE__ */ r.jsxs("dd", { className: "wf-cell-line", children: [
              /* @__PURE__ */ r.jsx("code", { title: e.artifactHash, children: e.artifactHash }),
              /* @__PURE__ */ r.jsx(ht, { value: e.artifactHash, ariaLabel: `Copy artifact hash ${e.artifactHash}`, copiedLabel: "artifact hash", onCopied: (y) => m(`Copied ${y}`), onCopyFailed: (y) => m(`Could not copy ${y}.`) })
            ] }),
            /* @__PURE__ */ r.jsx("dt", { children: "Root activity" }),
            /* @__PURE__ */ r.jsxs("dd", { children: [
              e.rootActivityType,
              " ",
              /* @__PURE__ */ r.jsx("small", { children: e.rootActivityVersion })
            ] }),
            /* @__PURE__ */ r.jsx("dt", { children: "Nodes" }),
            /* @__PURE__ */ r.jsx("dd", { children: e.nodeCount }),
            /* @__PURE__ */ r.jsx("dt", { children: "Resume targets" }),
            /* @__PURE__ */ r.jsx("dd", { children: e.resumeTargetCount }),
            /* @__PURE__ */ r.jsx("dt", { children: "Created" }),
            /* @__PURE__ */ r.jsx("dd", { children: $e(e.createdAt) })
          ] }),
          h ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: h }) : null
        ] }),
        /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
          /* @__PURE__ */ r.jsx("h4", { children: "Source" }),
          n ? /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
            /* @__PURE__ */ r.jsx("dt", { children: "Kind" }),
            /* @__PURE__ */ r.jsx("dd", { children: Js(n.sourceKind) }),
            /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
            /* @__PURE__ */ r.jsxs("dd", { children: [
              n.definitionId,
              " ",
              /* @__PURE__ */ r.jsx("small", { children: n.definitionVersionId })
            ] }),
            /* @__PURE__ */ r.jsx("dt", { children: "Scope" }),
            /* @__PURE__ */ r.jsx("dd", { children: gf(n.scope) })
          ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This artifact carries no source references." }),
          /* @__PURE__ */ r.jsx(WS, { reference: n, sourceDefinition: i })
        ] }),
        /* @__PURE__ */ r.jsx(VS, { fact: x ?? null })
      ] }) : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
        /* @__PURE__ */ r.jsx("h4", { children: "Source references" }),
        /* @__PURE__ */ r.jsx(
          jf,
          {
            references: e.references,
            activeReferenceId: e.chosenReference?.sourceReferenceId ?? null,
            onSelect: (y) => s(y.sourceReferenceId)
          }
        )
      ] }) })
    ] }) : null
  ] });
}
function VS({ fact: e }) {
  return e ? /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Selected node" }),
    /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
      /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        e.activityType,
        " ",
        /* @__PURE__ */ r.jsx("small", { children: e.activityTypeVersion })
      ] }),
      /* @__PURE__ */ r.jsx("dt", { children: "Executable node" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.executableNodeId }),
      /* @__PURE__ */ r.jsx("dt", { children: "Authored activity" }),
      /* @__PURE__ */ r.jsx("dd", { children: e.authoredActivityId })
    ] }),
    e.available ? null : /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "This activity is not available in this environment; the executable cannot run here." }),
    e.inputBindings.length > 0 ? /* @__PURE__ */ r.jsx("dl", { className: "wf-instance-meta wf-executable-bindings", children: e.inputBindings.map((t) => /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("dt", { children: t.inputName }),
      /* @__PURE__ */ r.jsxs("dd", { children: [
        /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: t.source }),
        " ",
        t.summary ?? ""
      ] })
    ] }, t.inputName)) }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No input bindings." })
  ] }) : null;
}
function OS({ reference: e, sourceDefinition: t, onOpen: n }) {
  const i = HS(e, t);
  return /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      disabled: !!i,
      title: i ?? void 0,
      "aria-label": i ? `Open source definition unavailable: ${i}` : "Open source definition",
      onClick: n,
      children: [
        /* @__PURE__ */ r.jsx(ps, { size: 14 }),
        " Open source definition"
      ]
    }
  );
}
function HS(e, t) {
  return e ? t.status === "absent" ? "This workflow definition is not available in this environment." : t.status === "failed" ? `The source definition could not be loaded: ${t.error}` : t.status === "loading" || t.status === "idle" ? "Checking the source definition..." : null : "This artifact carries no source references.";
}
function WS({ reference: e, sourceDefinition: t }) {
  if (!e) return null;
  if (t.status === "absent")
    return /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "This workflow definition is not available in this environment. The artifact renders from its own Execution Material and carried layout." });
  if (t.status !== "ready") return null;
  const n = Kj(e, t.definition);
  return n.kind !== "behind" ? null : /* @__PURE__ */ r.jsxs("p", { className: "wf-instance-note wf-executable-drift", role: "note", children: [
    "This reference was published from version ",
    n.referenceVersion ?? e.definitionVersionId,
    "; the definition's latest is ",
    n.latestVersion ?? t.definition.latestVersionId,
    "."
  ] });
}
function FS(e) {
  if (!e) return null;
  const t = e.chosenReference?.sourceReferenceId;
  return e.references.find((n) => n.sourceReferenceId === t) ?? e.references[0] ?? null;
}
function BS(e, t) {
  const n = e.trim().toLowerCase();
  return n === "requested" ? `Showing requested reference ${t}` : n === "newest-live" ? `Showing newest live reference ${t}` : n === "newest" ? `Showing newest reference ${t} (no live references remain)` : `Showing reference ${t}`;
}
function KS(e, t) {
  return Tf(e) ? `Executable artifact ${t} was not found.` : e instanceof Error ? e.message : String(e);
}
function Tf(e) {
  const t = e instanceof Error ? e.message : String(e);
  return /\b404\b/.test(t) || /not found/i.test(t);
}
function qS({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const o = ie(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ie(() => US(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = xn(c), l = c ? Ie(c) : Ot(a.activityType) ?? a.activityType, d = Ot(a.activityType) ?? a.activityType, f = YS(a.startedAt ?? a.scheduledAt), p = Is(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: Sr(u) }),
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
          /* @__PURE__ */ r.jsx(XS, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function XS({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function US(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => Nl(t.activity) - Nl(n.activity) || t.index - n.index).map((t) => t.activity);
}
function Nl(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function YS(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function ZS({ context: e }) {
  const [t, n] = O("loading"), [i, o] = O(""), [s, a] = O(""), [c, u] = O(""), [l, d] = O([]), f = se(async () => {
    n("loading"), o("");
    try {
      const h = await Xh(e, {
        status: s || void 0,
        runKind: c || void 0,
        take: 100
      });
      d(h), n("ready");
    } catch (h) {
      o(h instanceof Error ? h.message : String(h)), d([]), n("failed");
    }
  }, [e, c, s]);
  Q(() => {
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
    t === "failed" ? /* @__PURE__ */ r.jsx(It, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(ra, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      oa,
      {
        icon: /* @__PURE__ */ r.jsx(ri, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: mf(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(vn, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: $e(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: Is(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function JS({ context: e, ai: t, workflowExecutionId: n }) {
  const [i, o] = O("loading"), [s, a] = O(""), [c, u] = O(null), [l, d] = O(null), [f, p] = O([]), {
    inspectorWidth: h,
    inspectorCollapsed: m,
    maximizedSidePanel: v,
    inspectorExpanded: x,
    editorBodyStyle: y,
    toggleSidePanelCollapsed: j,
    toggleSidePanelMaximized: g,
    startSidePanelResize: w,
    handleSidePanelResizeKeyDown: S
  } = aa(), b = St(t, "weaver.workflows.explain-instance"), I = se(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const k = await Uh(e, n), [R, L] = await Promise.all([
        Th(e, k.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        ci(e)
      ]);
      u({
        details: k,
        definitionVersion: R.definitionVersion,
        definitionVersionError: R.error,
        activityCatalog: L.activities
      }), d(null), p([]), o("ready");
    } catch (k) {
      u(null), a(sN(k, n)), o("failed");
    }
  }, [e, n]);
  Q(() => {
    I();
  }, [I]);
  const A = () => {
    window.history.pushState({}, "", "/workflows/instances"), window.dispatchEvent(new PopStateEvent("popstate"));
  }, D = () => {
    const k = c?.details.instance.definitionId;
    k && (window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(k)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  }, M = [
    "wf-instance-detail-workbench",
    m ? "inspector-collapsed" : "",
    v === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-toolbar", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: A, children: [
        /* @__PURE__ */ r.jsx(zt, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: D, children: [
        /* @__PURE__ */ r.jsx(ps, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        I();
      }, children: [
        /* @__PURE__ */ r.jsx(oi, { size: 14 }),
        " Refresh"
      ] }),
      c && b ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(t, b, c.details), children: [
        /* @__PURE__ */ r.jsx(rt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(It, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: M, style: y, children: [
      /* @__PURE__ */ r.jsx(
        GS,
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
      x && !v ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize run details panel",
          "aria-orientation": "vertical",
          "aria-valuemin": $t,
          "aria-valuemax": Tt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (k) => w("inspector", k),
          onKeyDown: (k) => S("inspector", k)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        nC,
        {
          context: e,
          ai: t,
          action: b ?? void 0,
          summary: c.details.instance,
          details: c.details,
          state: "ready",
          error: "",
          selectedEvidenceId: l,
          onSelectEvidence: d,
          activityCatalog: c.activityCatalog,
          graphNodeIds: c.definitionVersion ? tC(c.definitionVersion, c.activityCatalog) : void 0,
          rootNodeId: c.definitionVersion?.state.rootActivity?.nodeId,
          collapsed: m,
          expanded: x,
          maximized: v === "inspector",
          onToggleCollapsed: () => j("inspector"),
          onToggleMaximized: () => g("inspector")
        }
      )
    ] }) : null
  ] });
}
function GS({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: o,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = eC(a), l = ie(
    () => QS(e, n, i, o, a, c),
    [n, e, i, a, c, o]
  );
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
          /* @__PURE__ */ r.jsx("small", { children: i.instance.definitionVersionId })
        ] }) })
      ] }),
      /* @__PURE__ */ r.jsx(vn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ r.jsx(ia, { className: "wf-instance-breadcrumb", frames: a, onNavigate: c }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: oN(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        qs,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: ta,
          edgeTypes: na,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(Xs, {}),
            /* @__PURE__ */ r.jsx(Ys, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(Us, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function QS(e, t, n, i, o, s) {
  const a = e?.state.rootActivity;
  if (!e || !a) return { nodes: [], edges: [] };
  const c = an(a, o, t), u = c?.owner ?? a, l = t.find((h) => h.activityVersionId === u.activityVersionId), f = ws(u, l) === "unsupported" || !c ? xs(u, t, e.layout) : ys(c, t, e.layout), p = f.nodes.map((h) => ({
    ...h,
    draggable: !1,
    connectable: !1,
    deletable: !1,
    data: {
      ...h.data,
      onEnterSlot: (m) => {
        const v = On(o, u, h.id, m, vt(h.data.label, m), t);
        v && s(v.frames);
      }
    }
  }));
  return {
    nodes: Zp(p, n.activities, n.incidents, i),
    edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
  };
}
function eC(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function tC(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (o) => {
    if (o) {
      n.add(o.nodeId);
      for (const s of Te(o, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function nC({
  context: e,
  ai: t,
  action: n,
  summary: i,
  details: o,
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
  onToggleMaximized: x
}) {
  const [y, j] = O("timeline");
  if (!i)
    return /* @__PURE__ */ r.jsx("aside", { className: "wf-instance-inspector", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Select a workflow run to inspect its timeline." }) });
  const g = o?.incidents.length ?? 0, w = iC(o?.activities ?? [], c), S = (I) => {
    u?.(I), j("activity");
  }, b = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(yr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Dl, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(kt, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(Fn, { label: "Run details tabs", tabs: b, activeTabId: y, onSelect: (I) => j(I) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: v,
            children: p ? /* @__PURE__ */ r.jsx(zt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
          }
        ),
        p ? null : /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": m ? "Restore run details panel" : "Maximize run details panel",
            title: m ? "Restore" : "Maximize",
            onClick: x,
            children: m ? /* @__PURE__ */ r.jsx(er, { size: 14 }) : /* @__PURE__ */ r.jsx(sn, { size: 14 })
          }
        )
      ] })
    ] }),
    h ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("header", { children: [
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("span", { children: "Workflow Instance ID" }),
          /* @__PURE__ */ r.jsx("h3", { children: i.workflowExecutionId })
        ] }),
        n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => Ct(t, n, o ?? i), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ r.jsx(It, { message: a }) : null,
      s === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: y === "timeline" ? /* @__PURE__ */ r.jsx(
        qS,
        {
          activities: o.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: S
        }
      ) : y === "activity" ? /* @__PURE__ */ r.jsx(rC, { context: e, activity: w, activityCatalog: f }) : y === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsx(xC, { incidents: o.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ r.jsx(NC, { details: o, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(vn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ r.jsx("dd", { children: mf(i.runKind) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Artifact" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          i.artifactId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: i.artifactVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          i.definitionId,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: i.definitionVersionId })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ r.jsx("dd", { children: $e(i.createdAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: $e(i.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: $e(i.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ r.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function iC(e, t) {
  if (!t) return null;
  const n = e.find((o) => o.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((o) => o.executableNodeId === t || o.authoredActivityId === t);
  return i.length > 0 ? Gl(i) : null;
}
function rC({ context: e, activity: t, activityCatalog: n }) {
  const i = t?.activityExecutionId ?? null, o = t?.workflowExecutionId ?? null, [s, a] = O({
    activityExecutionId: null,
    status: "idle",
    inspection: null,
    error: ""
  });
  if (Q(() => {
    if (!i || !o) {
      a({ activityExecutionId: null, status: "idle", inspection: null, error: "" });
      return;
    }
    let f = !1;
    const p = i;
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Yh(e, o, p).then(
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
  }, [e, i, o]), !t)
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsx("p", { children: "No activity selected." })
    ] });
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || Ot(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: u }),
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(vn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          Ot(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: $e(t.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: $e(t.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ r.jsx("dd", { children: Is(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ r.jsx("dd", { children: l }),
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx(
      Sl,
      {
        state: s,
        subject: "ActivityInput",
        title: "Inputs",
        loadingText: "Loading runtime input evidence...",
        failureText: "Runtime input evidence is unavailable.",
        emptyText: "No runtime input snapshots were recorded for this execution."
      }
    ),
    /* @__PURE__ */ r.jsx(
      Sl,
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
function Sl({
  state: e,
  subject: t,
  title: n,
  loadingText: i,
  failureText: o,
  emptyText: s
}) {
  if (e.status === "idle") return null;
  if (e.status === "loading")
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: n }),
      /* @__PURE__ */ r.jsx("p", { children: i })
    ] });
  if (e.status === "failed")
    return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: n }),
      /* @__PURE__ */ r.jsx("p", { children: o }),
      e.error ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: e.error }) : null
    ] });
  const a = (e.inspection?.valueSnapshots ?? []).filter((c) => c.subject === t);
  return a.length === 0 ? /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: n }),
    /* @__PURE__ */ r.jsx("p", { children: s })
  ] }) : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: n }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-input-list", children: a.map((c) => /* @__PURE__ */ r.jsx(oC, { snapshot: c }, `${c.name}:${c.capturedAt}:${c.captureMode}`)) })
  ] });
}
function oC({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.snapshot ?? (e.captureMode === "DiagnosticSnapshot" ? e.payload : null), i = e.captureMode === "Payload" && e.payload !== void 0;
  return /* @__PURE__ */ r.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx("strong", { children: e.name }),
        /* @__PURE__ */ r.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-runtime-capture-mode", children: fC(e.captureMode) })
    ] }),
    gC(n) ? /* @__PURE__ */ r.jsx(ua, { node: n }) : i ? /* @__PURE__ */ r.jsx(dC, { payload: e.payload }) : /* @__PURE__ */ r.jsx("p", { children: hC(e) }),
    e.isSensitive ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
const sC = {
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
function aC(e) {
  return Object.hasOwn(sC, e.kind);
}
function ua({ node: e, depth: t = 0 }) {
  if (!aC(e))
    return /* @__PURE__ */ r.jsx(Cl, { node: { kind: "unsupported", reason: `Unknown snapshot node: ${e.kind}` } });
  switch (e.kind) {
    case "null":
      return /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: "null" });
    case "scalar":
    case "number":
      return /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: Rf(e.value) });
    case "string":
      return /* @__PURE__ */ r.jsxs("code", { className: "wf-runtime-input-value", children: [
        e.preview ?? "",
        e.truncated ? ` (${e.length ?? "unknown"} chars, truncated)` : ""
      ] });
    case "object":
      return /* @__PURE__ */ r.jsx(cC, { node: e, depth: t });
    case "array":
      return /* @__PURE__ */ r.jsx(lC, { node: e, depth: t });
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return /* @__PURE__ */ r.jsx(Cl, { node: e });
    case "payloadReference":
      return /* @__PURE__ */ r.jsx(uC, { node: e });
  }
}
function cC({ node: e, depth: t }) {
  const n = e.properties ?? [];
  return n.length === 0 ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: "{}" }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ r.jsxs("summary", { children: [
      e.typeName || "Object",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i) => /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ r.jsx("span", { children: i.name }),
      /* @__PURE__ */ r.jsx(ua, { node: i.value, depth: t + 1 })
    ] }, i.name)) })
  ] });
}
function lC({ node: e, depth: t }) {
  const n = e.items ?? [];
  return n.length === 0 ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: "[]" }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ r.jsxs("summary", { children: [
      "Array (",
      e.itemCount ?? n.length,
      ")",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i, o) => /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ r.jsx("span", { children: o }),
      /* @__PURE__ */ r.jsx(ua, { node: i, depth: t + 1 })
    ] }, o)) })
  ] });
}
function Cl({ node: e }) {
  const t = e.message || e.reason || e.requiredPermission || e.displayName;
  return /* @__PURE__ */ r.jsxs("span", { className: `wf-runtime-snapshot-marker ${e.kind}`, children: [
    pC(e.kind),
    t ? `: ${t}` : "",
    e.omittedCount ? ` (${e.omittedCount} omitted)` : ""
  ] });
}
function uC({ node: e }) {
  const t = e.displayName || e.referenceKind || "Referenced payload", n = e.resolution?.reason || "Reference resolution is not available.";
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-snapshot-reference", children: [
    /* @__PURE__ */ r.jsx("strong", { children: t }),
    e.contentType ? /* @__PURE__ */ r.jsx("small", { children: e.contentType }) : null,
    typeof e.size == "number" ? /* @__PURE__ */ r.jsxs("small", { children: [
      e.size,
      " bytes"
    ] }) : null,
    /* @__PURE__ */ r.jsx("span", { children: n })
  ] });
}
function dC({ payload: e }) {
  const t = Rf(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ r.jsx("summary", { children: mC(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] });
}
function fC(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function pC(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function hC(e) {
  return e.state === "permissionHidden" ? "Runtime value evidence is hidden by permissions." : e.state === "metadataOnly" ? e.captureReason || "Runtime value evidence is metadata-only." : e.state === "notCaptured" ? e.captureReason || "Runtime value evidence was not captured." : e.captureReason || "The runtime capture policy did not include this value.";
}
function gC(e) {
  return typeof e == "object" && e !== null && typeof e.kind == "string";
}
function mC(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function Rf(e) {
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
const yC = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function xC({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
  const [i, o] = O("");
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Incidents" }),
    e.length === 0 ? /* @__PURE__ */ r.jsx("p", { children: "No incidents recorded." }) : null,
    e.map((s) => /* @__PURE__ */ r.jsxs(
      "article",
      {
        className: "wf-instance-incident",
        "data-severity": s.severity.toLowerCase(),
        "data-selected": s.incidentId === t,
        children: [
          /* @__PURE__ */ r.jsxs(
            "button",
            {
              type: "button",
              className: "wf-instance-incident-summary",
              "aria-label": `Select incident ${s.failureType}`,
              onClick: () => n?.(s.incidentId),
              children: [
                /* @__PURE__ */ r.jsx("strong", { children: s.failureType }),
                /* @__PURE__ */ r.jsxs("span", { children: [
                  s.status,
                  " · ",
                  s.severity
                ] }),
                /* @__PURE__ */ r.jsx("p", { children: s.message })
              ]
            }
          ),
          /* @__PURE__ */ r.jsx(
            ht,
            {
              value: SC(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => o(`Copied ${a}`),
              onCopyFailed: (a) => o(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ r.jsx(wC, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function wC({ incident: e }) {
  const t = vC(e);
  return t ? /* @__PURE__ */ r.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ r.jsx("summary", { children: jC(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] }) : null;
}
function vC(e) {
  const t = bC(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of yC) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function bC(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function jC(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function NC({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((o) => {
    const s = Jj(o);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((o) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ r.jsx("strong", { children: Ot(o.activityType) ?? o.activityType }),
      /* @__PURE__ */ r.jsx("small", { children: o.activityExecutionId })
    ] }, `activity-${o.activityExecutionId}`)) })
  ] });
}
function SC(e) {
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
    `Created: ${$e(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${$e(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function CC({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: o,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = O(Il);
  Q(() => {
    const l = () => c(Il());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ r.jsx(jS, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: o, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ r.jsx(gi, { title: "Definitions", children: /* @__PURE__ */ r.jsx(CS, { context: e, ai: t, onOpen: u }) });
}
function IC({ context: e, ai: t }) {
  const [n, i] = O(kl);
  Q(() => {
    const s = () => i(kl());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = se((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(gi, { title: "Executables", children: /* @__PURE__ */ r.jsx(wN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function kC({ context: e, ai: t }) {
  const [n, i] = O(Mo);
  Q(() => {
    const s = () => i(Mo());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = se((s) => {
    const a = new URL(window.location.href);
    s ? a.searchParams.set("ref", s) : a.searchParams.delete("ref"), window.history.replaceState({}, "", `${a.pathname}${a.search}${a.hash}`), i(Mo());
  }, []);
  return /* @__PURE__ */ r.jsx(gi, { title: "Executable Inspector", children: /* @__PURE__ */ r.jsx(
    PS,
    {
      context: e,
      ai: t,
      artifactId: n.artifactId,
      sourceReferenceId: n.sourceReferenceId,
      onSelectReference: o
    }
  ) });
}
function EC({ context: e }) {
  return /* @__PURE__ */ r.jsx(gi, { title: "Runs", children: /* @__PURE__ */ r.jsx(ZS, { context: e }) });
}
function AC({ context: e, ai: t }) {
  const n = _C();
  return /* @__PURE__ */ r.jsx(gi, { title: "Run", children: /* @__PURE__ */ r.jsx(JS, { context: e, ai: t, workflowExecutionId: n }) });
}
function gi({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Il() {
  return new URLSearchParams(window.location.search).get("definition");
}
function kl() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Mo() {
  const e = /^\/workflows\/executables\/([^/]+)$/.exec(window.location.pathname);
  return {
    artifactId: e ? decodeURIComponent(e[1]) : "",
    sourceReferenceId: new URLSearchParams(window.location.search).get("ref")
  };
}
function _C() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function PC(e) {
  hp(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(CC, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(IC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-executable-inspector",
        path: "/workflows/executables/:artifactId",
        label: "Executable Inspector",
        component: () => /* @__PURE__ */ r.jsx(kC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(EC, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(AC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(Wg, { context: e.backend })
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => /* @__PURE__ */ r.jsx(Kg, { context: e.backend })
      }
    ]
  });
}
export {
  Qj as isConnectEndOverExistingWorkflowNode,
  PC as register,
  eN as resolveConnectEndSource
};
