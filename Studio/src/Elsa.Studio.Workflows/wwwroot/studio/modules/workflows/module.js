import Fe, { useMemo as ae, useState as O, useEffect as Q, memo as ke, forwardRef as Il, useRef as se, useCallback as oe, useContext as Qn, createContext as cs, useLayoutEffect as Wf, lazy as Ff, Suspense as Bf, useReducer as Kf, useId as kl } from "react";
import { Boxes as ei, Zap as qf, Play as Rt, Terminal as Xf, ListTree as pr, GitBranch as El, ListChecks as Uf, Save as ls, EyeOff as mo, Shield as ya, AlertTriangle as Vn, SlidersHorizontal as us, Activity as Al, Search as hr, X as ds, DatabaseZap as xa, ShieldCheck as Yf, Check as fn, Plus as en, Trash2 as Pt, ChevronRight as pt, AlertCircle as It, Wrench as Zf, Copy as Jf, ChevronDown as fs, ScanSearch as _l, Sparkles as rt, RotateCcw as ti, GripVertical as Dl, Maximize2 as tn, ChevronUp as Gf, Repeat2 as Qf, Package as $l, Undo2 as ep, Redo2 as tp, Network as np, Download as ip, ChevronLeft as Mt, Minimize2 as Zi, Workflow as ps, Code2 as rp, Fingerprint as op } from "lucide-react";
import { useQuery as gr, useQueryClient as Tl, useMutation as Rl } from "@tanstack/react-query";
import { useTablistKeyboard as sp } from "@elsa-workflows/studio-ui";
function ap(e) {
  return e && e.__esModule && Object.prototype.hasOwnProperty.call(e, "default") ? e.default : e;
}
var yo = { exports: {} }, jn = {};
var wa;
function cp() {
  if (wa) return jn;
  wa = 1;
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
  return jn.Fragment = t, jn.jsx = n, jn.jsxs = n, jn;
}
var va;
function lp() {
  return va || (va = 1, yo.exports = cp()), yo.exports;
}
var r = lp();
let Pl;
function up(e) {
  Pl = e;
}
function Ji() {
  return Pl;
}
const dp = "String", fp = "singleline";
function pp(e) {
  return e === "Single" || e === "Array" || e === "List" || e === "HashSet";
}
function hs(e, t = "Single") {
  return { alias: (e ?? "").trim() || dp, collectionKind: t };
}
function gs(e) {
  const t = e.type ?? e.Type;
  if (On(t))
    return { alias: typeof t.alias == "string" ? t.alias : typeof t.typeName == "string" ? t.typeName : "", collectionKind: pp(t.collectionKind) ? t.collectionKind : "Single" };
  const n = e.typeInformation ?? e.TypeInformation;
  return On(n) ? { alias: typeof n.typeName == "string" ? n.typeName : "", collectionKind: ba(e) ? "Array" : "Single" } : { alias: typeof t == "string" ? t : "", collectionKind: ba(e) ? "Array" : "Single" };
}
function ba(e) {
  const t = e.isArray ?? e.IsArray;
  return t === !0 || t === "true";
}
function ja(e) {
  return (e.alias ?? e.typeName ?? "").trim();
}
function mr() {
  return typeof crypto < "u" && typeof crypto.randomUUID == "function" ? crypto.randomUUID() : `id-${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
}
function hp(e, t) {
  const n = new Set(t);
  let i = 1, o = `${e}${i}`;
  for (; n.has(o); )
    i += 1, o = `${e}${i}`;
  return o;
}
function gp(e) {
  return {
    referenceKey: mr(),
    name: e.name,
    type: hs(e.alias),
    storageDriverType: e.storageDriver?.trim() || null,
    default: null
  };
}
function mp(e, t) {
  return { ...e, ...t };
}
function yp(e) {
  return e === "" ? null : { value: e, expressionType: "Literal" };
}
function xp(e) {
  return e == null || e.value == null ? "" : typeof e.value == "object" ? JSON.stringify(e.value) : String(e.value);
}
function wp(e) {
  return {
    referenceKey: mr(),
    name: e.name,
    type: hs(e.alias),
    displayName: e.name,
    description: "",
    category: "",
    uiHint: fp,
    storageDriverType: e.storageDriver ?? null,
    isRequired: !1
  };
}
function vp(e, t) {
  return { ...e, ...t };
}
function bp(e) {
  return {
    referenceKey: mr(),
    name: e.name,
    type: hs(e.alias),
    displayName: e.name,
    description: "",
    category: ""
  };
}
function jp(e, t) {
  return { ...e, ...t };
}
function Np(e) {
  const t = e.split(",")[0].trim();
  return (t.split(".").pop() ?? t).split("`")[0];
}
function Ml(e, t) {
  return e && !e.includes(",") && !e.includes(".") ? e : Np(e || t);
}
function Sp(e, t) {
  return Ml(e, t).replace(/StorageDriver$/, "");
}
function On(e) {
  return !!e && typeof e == "object" && !Array.isArray(e);
}
function dt(e, t) {
  for (const n of t) {
    const i = e[n];
    if (i != null && typeof i != "object") return String(i);
  }
  return "";
}
const Cp = ["name", "Name"], ms = ["name", "Name"], Ip = ["storageDriverType", "StorageDriverType"], Ll = ["referenceKey", "ReferenceKey"], kp = /* @__PURE__ */ new Set(["nodeId", "activityVersionId", "inputs", "outputs", "structure"]);
function ni(e) {
  return zl(e, Dp);
}
function yr(e) {
  return zl(e, $p);
}
function zl(e, t) {
  if (!e) return e;
  const n = { ...e };
  return e.rootActivity && (n.rootActivity = Lo(e.rootActivity, t)), Array.isArray(e.variables) && (n.variables = Oi(e.variables, zo)), Array.isArray(e.inputs) && (n.inputs = Oi(e.inputs, zo)), Array.isArray(e.outputs) && (n.outputs = Oi(e.outputs, (i) => Vl(i, !1))), n;
}
function Lo(e, t) {
  const n = t(e), i = n.structure;
  if (!i || !ot(i.payload)) return n;
  let o = !1;
  const s = { ...i.payload };
  for (const [a, c] of Object.entries(i.payload))
    Sa(c) ? (s[a] = Lo(c, t), o = !0) : Array.isArray(c) && c.length > 0 && c.every(Sa) && (s[a] = c.map((u) => Lo(u, t)), o = !0);
  return Array.isArray(i.payload.variables) && i.payload.variables.length > 0 && (s.variables = Oi(i.payload.variables, zo), o = !0), o ? { ...n, structure: { ...i, payload: s } } : n;
}
function Oi(e, t) {
  return e.map((n) => ot(n) && !Array.isArray(n) ? t(n) : n);
}
function zo(e) {
  return Vl(e, !0);
}
const Ep = [
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
function Vl(e, t) {
  const n = Ap(e, Ep);
  return dt(e, Ll).trim() || (n.referenceKey = mr()), n.type = gs(e), t && (n.storageDriverType = _p(e.storageDriverType ?? e.StorageDriverType)), n;
}
function Ap(e, t) {
  const n = new Set(t), i = {};
  for (const [o, s] of Object.entries(e))
    n.has(o) || (i[o] = s);
  return i;
}
function _p(e) {
  if (typeof e == "string") return e.trim() ? e : null;
  if (ot(e)) {
    const t = typeof e.typeName == "string" ? e.typeName : "";
    if (!t) return null;
    const n = typeof e.namespace == "string" ? e.namespace : "";
    return n ? `${n}.${t}` : t;
  }
  return null;
}
function Dp(e) {
  const t = [], n = {};
  for (const [o, s] of Object.entries(e))
    kp.has(o) || (Mp(s) ? t.push({
      referenceKey: Tp(o),
      value: Pp(s.expression)
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
function $p(e) {
  const t = Array.isArray(e.inputs) ? e.inputs : [], n = {};
  for (const i of t) {
    if (!ot(i) || typeof i.referenceKey != "string") continue;
    const o = ot(i.value) ? i.value : {};
    n[Rp(i.referenceKey)] = {
      typeName: "",
      expression: {
        type: typeof o.expressionType == "string" ? o.expressionType : "Literal",
        value: o.value ?? ""
      }
    };
  }
  return { ...e, ...n, inputs: [] };
}
function Tp(e) {
  return e && e.charAt(0).toUpperCase() + e.slice(1);
}
function Rp(e) {
  return e && e.charAt(0).toLowerCase() + e.slice(1);
}
function Pp(e) {
  const t = e.type || "Literal";
  return t === "Variable" && ot(e.value) ? { value: e.value, expressionType: t } : t === "Literal" && ot(e.value) ? { value: Na(e.value), expressionType: "Object" } : { value: Na(e.value), expressionType: t };
}
function Na(e) {
  return e == null ? null : typeof e == "string" ? e : typeof e == "number" || typeof e == "boolean" ? String(e) : JSON.stringify(e);
}
function Mp(e) {
  if (!ot(e) || Array.isArray(e)) return !1;
  const t = e.expression;
  return ot(t) && typeof t.type == "string";
}
function Sa(e) {
  return ot(e) && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function ot(e) {
  return typeof e == "object" && e !== null;
}
const ii = "elsa.sequence.structure", pn = "elsa.flowchart.structure";
function Ol(e, t, n) {
  if (!e) return null;
  let i = e;
  for (const o of t) {
    const s = zp(i, o.ownerNodeId, n);
    if (!s) return null;
    i = s;
  }
  return i;
}
function Lp(e, t, n = (o) => o.nodeId, i) {
  if (!e) return null;
  if (e.nodeId === t) return [];
  const o = (s, a) => {
    const c = Re(s, i);
    for (const u of c) {
      if (!u.activities.some((d) => d.nodeId === t)) continue;
      const l = a.at(-1);
      return l ? [...a.slice(0, -1), { ...l, slotId: u.id }] : u.id === c[0]?.id ? a : null;
    }
    for (const u of c)
      for (const l of u.activities) {
        const f = Re(l, i)[0]?.id ?? u.id, p = o(l, [...a, { ownerNodeId: l.nodeId, slotId: f, label: n(l) }]);
        if (p) return p;
      }
    return null;
  };
  return o(e, []);
}
function Pn(e, t, n, i, o, s) {
  const a = i.cardinality === "single" && i.activities.length === 1 ? i.activities[0] : null, c = a ? Re(a, s)[0] : void 0, u = a && c && c.mode !== "generic" ? a : null, l = t?.nodeId === n;
  if (l && e.length === 0)
    return t && Re(t, s)[0]?.id !== i.id ? null : u ? { frames: [{ ownerNodeId: u.nodeId, slotId: c.id, label: o }], selectedNodeId: null } : { frames: [], selectedNodeId: a?.nodeId ?? null };
  const d = l ? e.slice(0, -1) : e;
  return u ? {
    frames: [
      ...d,
      { ownerNodeId: n, slotId: i.id, label: "" },
      { ownerNodeId: u.nodeId, slotId: c.id, label: o }
    ],
    selectedNodeId: null
  } : {
    frames: [...d, { ownerNodeId: n, slotId: i.id, label: o }],
    selectedNodeId: a?.nodeId ?? null
  };
}
function Hn(e, t) {
  return `${e} / ${t.label}`;
}
function Hl(e, t, n) {
  const i = Re(e, n), o = t.at(-1);
  return o ? i.find((s) => s.id === o.slotId) ?? null : i[0] ?? null;
}
function nn(e, t, n) {
  const i = Ol(e, t, n);
  if (!i) return null;
  const o = Hl(i, t, n);
  return o ? { owner: i, slot: o } : null;
}
function Wl(e, t, n) {
  for (const i of Re(e, n)) {
    const o = i.activities.find((s) => s.nodeId === t);
    if (o) return { slot: i, child: o };
  }
  return null;
}
function zp(e, t, n) {
  return Wl(e, t, n)?.child ?? null;
}
function Re(e, t) {
  const n = e.structure;
  if (!n || !n.payload || typeof n.payload != "object") return [];
  const i = n.payload, o = xr(Fl(e, t));
  if (o?.kind === n.kind) return Hp(n, o);
  const s = ch(n), a = In(i.activities);
  return a ? [{
    id: `${n.kind}:activities`,
    label: lh(n),
    property: "activities",
    cardinality: "many",
    mode: s,
    activities: a
  }] : Object.entries(i).filter(([, c]) => In(c) || er(c)).map(([c, u]) => ({
    id: `${n.kind}:${c}`,
    label: dh(c),
    property: c,
    cardinality: In(u) ? "many" : "single",
    mode: "generic",
    activities: In(u) ?? (er(u) ? [u] : [])
  }));
}
function xr(e) {
  for (const t of e?.designFacets ?? []) {
    if (!st(t)) continue;
    const n = typeof t.kind == "string" ? t.kind : "", i = typeof t.schemaVersion == "string" ? t.schemaVersion : "";
    if (!n || !i || !st(t.payload)) continue;
    const o = Vp(t.payload);
    if (o) return { kind: n, schemaVersion: i, payload: o };
  }
  return null;
}
function Vp(e) {
  const t = e.mode;
  if (t !== "sequence" && t !== "flowchart" && t !== "generic" || typeof e.supportsScopedVariables != "boolean" || !Array.isArray(e.slots) || !st(e.initialPayload)) return null;
  const n = e.slots.map(Op).filter((i) => i !== null);
  return {
    mode: t,
    supportsScopedVariables: e.supportsScopedVariables,
    slots: n,
    initialPayload: e.initialPayload
  };
}
function Op(e) {
  if (!st(e)) return null;
  const t = typeof e.name == "string" ? e.name : "", n = typeof e.property == "string" ? e.property : "", i = typeof e.displayName == "string" ? e.displayName : "", o = e.cardinality;
  return !t || !n || !i || o !== "single" && o !== "many" ? null : {
    name: t,
    property: n,
    displayName: i,
    cardinality: o,
    collectionProperty: Yt(e.collectionProperty),
    childProperty: Yt(e.childProperty),
    labelProperty: Yt(e.labelProperty),
    slotNameTemplate: Yt(e.slotNameTemplate)
  };
}
function Hp(e, t) {
  return t.payload.slots.flatMap((n) => {
    if (n.collectionProperty && n.childProperty) {
      const i = n.childProperty, o = e.payload[n.collectionProperty];
      return Array.isArray(o) ? o.flatMap((s, a) => {
        if (!st(s)) return [];
        const c = n.labelProperty ? Yt(s[n.labelProperty]) : void 0;
        return [Ca(e.kind, n, s[i], t.payload.mode, a, c)];
      }) : [];
    }
    return [Ca(e.kind, n, e.payload[n.property], t.payload.mode)];
  });
}
function Ca(e, t, n, i, o, s) {
  const a = o === void 0 ? "" : `:${t.collectionProperty}:${t.childProperty}:${o}`;
  return {
    id: `${e}:${t.property}${a}`,
    label: o === void 0 ? t.displayName : Fp(t, o, s),
    property: t.property,
    cardinality: t.cardinality,
    mode: i,
    activities: Wp(n, t.cardinality),
    collectionProperty: t.collectionProperty,
    childProperty: t.childProperty,
    labelProperty: t.labelProperty,
    slotNameTemplate: t.slotNameTemplate,
    collectionIndex: o,
    collectionItemLabel: s
  };
}
function Wp(e, t) {
  return t === "many" ? In(e) ?? [] : er(e) ? [e] : [];
}
function Fp(e, t, n) {
  return e.slotNameTemplate ? e.slotNameTemplate.replaceAll("{name}", e.name).replaceAll("{displayName}", e.displayName).replaceAll("{label}", n ?? String(t + 1)).replaceAll("{index}", String(t + 1)) : n ? `${e.displayName}: ${n}` : `${e.displayName} ${t + 1}`;
}
function Fl(e, t) {
  if (t)
    return t instanceof Map ? t.get(e.activityVersionId) : Array.isArray(t) ? t.find((n) => n.activityVersionId === e.activityVersionId) : t.activityVersionId === e.activityVersionId ? t : void 0;
}
function Bp(e, t) {
  if (t.labelProperty && t.collectionItemLabel) {
    const n = e.map((i, o) => st(i) && Yt(i[t.labelProperty]) === t.collectionItemLabel ? o : -1).filter((i) => i >= 0);
    if (n.length === 1) return n[0];
  }
  return typeof t.collectionIndex == "number" && t.collectionIndex >= 0 && t.collectionIndex < e.length ? t.collectionIndex : -1;
}
function ys(e, t, n) {
  const i = new Map(t.map((a) => [a.activityVersionId, a])), o = new Map(n.map((a) => [a.nodeId, a])), s = e.slot.activities.map((a, c) => {
    const u = i.get(a.activityVersionId), l = o.get(a.nodeId) ?? uh(e.slot.mode, c);
    return Kl(a, u, { x: l.x, y: l.y });
  });
  return {
    nodes: s,
    edges: e.slot.mode === "flowchart" ? Xl(e.owner) : th(e.slot, s)
  };
}
function xs(e, t, n) {
  const i = t.find((s) => s.activityVersionId === e.activityVersionId), o = n.find((s) => s.nodeId === e.nodeId) ?? { x: 0, y: 0 };
  return {
    nodes: [Kl(e, i, { x: o.x, y: o.y }, {
      connectable: !1,
      deletable: !1,
      draggable: !1,
      suppressFlowPorts: !0
    })],
    edges: []
  };
}
function Kp(e, t, n, i = null) {
  const o = new Map(t.map((c) => [c.activityExecutionId, c])), s = Ea(t, (c) => c.authoredActivityId || c.executableNodeId), a = Ea(n, (c) => c.executableNodeId ? c.executableNodeId : c.activityExecutionId ? o.get(c.activityExecutionId)?.authoredActivityId ?? "" : "");
  return e.map((c) => {
    const u = s.get(c.id) ?? [], l = a.get(c.id) ?? [];
    if (u.length === 0 && l.length === 0) return c;
    const d = Yl(u), f = i === c.id || u.some((h) => h.activityExecutionId === i) || l.some((h) => h.incidentId === i), p = {
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
  if (e?.structure?.kind === pn || Jp(t)) return "flowchart";
  if (e?.structure?.kind === ii || Gp(t)) return "sequence";
  if (e) {
    const n = Re(e, t)[0];
    if (n) return n.mode === "flowchart" ? "flowchart" : "sequence";
  }
  return "unsupported";
}
function Ia(e, t, n, i) {
  return vs(e, t, i, (o) => {
    const s = Hl(o, t, i);
    return s ? hn(o, s, n) : o;
  });
}
function qp(e, t, n, i) {
  return t.length === 0 ? n : vs(e, t, i, () => n);
}
function vs(e, t, n, i) {
  if (t.length === 0) return i(e);
  const [o, ...s] = t, a = Wl(e, o.ownerNodeId, n);
  if (!a) return e;
  const c = vs(a.child, s, n, i);
  if (c === a.child) return e;
  const u = a.slot.activities.map((l) => l.nodeId === o.ownerNodeId ? c : l);
  return hn(e, a.slot, u);
}
function Vo(e, t, n, i) {
  if (e.nodeId === t) return n(e);
  const o = Re(e, i);
  if (o.length === 0) return e;
  let s = !1, a = e;
  for (const c of o) {
    const u = c.activities.map((l) => {
      const d = Vo(l, t, n, i);
      return d !== l && (s = !0), d;
    });
    s && (a = hn(a, c, u));
  }
  return s ? a : e;
}
function hn(e, t, n) {
  if (!e.structure) return e;
  const i = t.cardinality === "single" ? n[0] ?? null : n;
  if (t.collectionProperty && t.childProperty) {
    const o = e.structure.payload[t.collectionProperty];
    if (!Array.isArray(o)) return e;
    const s = Bp(o, t);
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
function Xp(e, t, n, i = []) {
  const o = new Map(e.slot.activities.map((a) => [a.nodeId, a]));
  for (const a of i)
    o.set(a.nodeId, a);
  const s = t.map((a) => o.get(a.id)).filter((a) => !!a);
  return e.slot.mode === "sequence" && s.sort((a, c) => {
    const u = t.find((d) => d.id === a.nodeId), l = t.find((d) => d.id === c.nodeId);
    return (u?.position.x ?? 0) - (l?.position.x ?? 0);
  }), hn(e.owner, e.slot, s);
}
function Up(e, t) {
  return {
    ...e,
    structure: eh(e.structure, t)
  };
}
function Yp(e, t) {
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
function Gi(e, t) {
  return {
    nodeId: t,
    activityVersionId: e.activityVersionId,
    inputs: [],
    outputs: [],
    structure: ql(e)
  };
}
function Bl(e, t) {
  if (!e) return null;
  const n = Fl(e, t), i = e.structure ?? (n ? ql(n) : null);
  let o = i === e.structure ? e : { ...e, structure: i };
  const s = Re(o, t);
  for (const a of s) {
    const c = a.activities.map((u) => Bl(u, t) ?? u);
    c.some((u, l) => u !== a.activities[l]) && (o = hn(o, a, c));
  }
  return o;
}
function Ie(e) {
  const t = e.activityTypeKey.split(".").at(-1) || e.activityTypeKey, n = e.displayName?.trim();
  return !n || n === e.activityTypeKey || n.includes(".") ? Qp(t) : n;
}
function Kl(e, t, n, i = {}) {
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
      icon: gn(t),
      childSlots: Re(e, t),
      acceptsInbound: nh(e, t),
      sourcePorts: i.suppressFlowPorts ? [] : Ul(e, t),
      suppressFlowPorts: i.suppressFlowPorts
    }
  };
}
function gn(e) {
  if (!e) return "activity";
  const t = Zp(e.icon);
  if (t) return t;
  const n = e.activityTypeKey.toLowerCase(), i = Ie(e).toLowerCase(), o = e.category?.toLowerCase() ?? "", s = e.executionType?.toLowerCase() ?? "";
  return n.endsWith(".flowchart") || i === "flowchart" ? "flowchart" : n.endsWith(".sequence") || i === "sequence" ? "sequence" : n.includes("writeline") || i.includes("write line") ? "terminal" : o.includes("runtime") ? "runtime" : s === "trigger" ? "trigger" : "activity";
}
function Zp(e) {
  if (!e) return null;
  const t = e.trim().toLowerCase();
  return ["activity", "flowchart", "sequence", "terminal", "runtime", "trigger"].includes(t) ? t : null;
}
function Jp(e) {
  return !!e && (Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart"));
}
function Gp(e) {
  return !!e && (Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence"));
}
function Qp(e) {
  return e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").trim();
}
function ql(e) {
  const t = xr(e);
  return t ? {
    kind: t.kind,
    schemaVersion: t.schemaVersion,
    payload: sh(t.payload.initialPayload)
  } : e.activityTypeKey.endsWith(".Sequence") || e.displayName === "Sequence" ? {
    kind: ii,
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
function eh(e, t) {
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
function th(e, t) {
  return e.mode === "sequence" ? t.slice(0, -1).map((n, i) => ({
    id: `sequence-${n.id}-${t[i + 1].id}`,
    source: n.id,
    target: t[i + 1].id,
    type: "smoothstep",
    animated: !1
  })) : [];
}
function Xl(e) {
  if (e.structure?.kind !== pn) return [];
  const t = e.structure.payload.connections;
  return Array.isArray(t) ? t.map((n, i) => {
    if (!n || typeof n != "object") return null;
    const o = n.source, s = n.target;
    if (!o?.nodeId || !s?.nodeId) return null;
    const a = Array.isArray(n.vertices) ? n.vertices.filter(ah) : [];
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
function Ul(e, t) {
  const n = ka(e.cases);
  if (rh(e, t) && n.length > 0)
    return [...n.map((s) => ({ name: s, displayName: s })), { name: "Default", displayName: "Default" }];
  const i = [
    ...Hi(t?.designFacets),
    ...Hi(t?.ports),
    ...Hi(t?.outputs)
  ];
  if (i.length > 0) return oh(i);
  const o = ka(e.outcomes);
  return o.length > 0 ? o.map((s) => ({ name: s, displayName: s })) : [{ name: "Done", displayName: "Done" }];
}
function nh(e, t) {
  return String(t?.executionType ?? "").toLowerCase() !== "trigger";
}
function Qi(e, t, n, i) {
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
function ih(e, t, n) {
  const i = Qi(t.source, n, t.sourceHandle ?? "Done", void 0), o = Qi(n, t.target, "Done", t.targetHandle ?? void 0);
  return e.filter((s) => s.id !== t.id).concat(i, o);
}
function In(e) {
  return Array.isArray(e) ? e.filter(er) : null;
}
function rh(e, t) {
  const n = t?.activityTypeKey ?? e.activityVersionId, i = t?.displayName ?? "";
  return n.endsWith(".FlowSwitch") || n === "FlowSwitch" || i === "FlowSwitch";
}
function Hi(e) {
  if (!Array.isArray(e)) return [];
  const t = [];
  for (const n of e) {
    if (!st(n)) continue;
    if (Array.isArray(n.ports)) {
      t.push(...Hi(n.ports));
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
function oh(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e)
    t.has(n.name) || t.set(n.name, n);
  return [...t.values()];
}
function ka(e) {
  return Array.isArray(e) ? e.filter((t) => typeof t == "string" && t.length > 0) : [];
}
function Yt(e) {
  return typeof e == "string" && e.length > 0 ? e : void 0;
}
function sh(e) {
  return JSON.parse(JSON.stringify(e));
}
function Ea(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i);
    o && n.set(o, [...n.get(o) ?? [], i]);
  }
  return n;
}
function Yl(e) {
  return [...e].sort((t, n) => Aa(n).localeCompare(Aa(t)))[0];
}
function Aa(e) {
  return e.completedAt ?? e.startedAt ?? e.scheduledAt;
}
function ah(e) {
  return st(e) && typeof e.x == "number" && typeof e.y == "number";
}
function st(e) {
  return typeof e == "object" && e !== null;
}
function er(e) {
  return typeof e == "object" && e !== null && typeof e.nodeId == "string" && typeof e.activityVersionId == "string";
}
function ch(e) {
  return e.kind === ii ? "sequence" : e.kind === pn ? "flowchart" : "generic";
}
function lh(e) {
  return e.kind === ii || e.kind === pn, "Activities";
}
function uh(e, t) {
  return e === "sequence" ? { nodeId: "", x: t * 280, y: 0 } : { nodeId: "", x: t % 4 * 280, y: Math.floor(t / 4) * 150 };
}
function dh(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[-_]/g, " ").replace(/^\w/, (t) => t.toUpperCase());
}
const tr = "workflow", fh = /* @__PURE__ */ new Set([ii, pn]);
function ph(e, t) {
  const n = e?.structure?.kind;
  if (!n) return !1;
  if (fh.has(n)) return !0;
  const i = t?.activityVersionId === e?.activityVersionId ? xr(t) : null;
  return i?.kind === n && i.payload.supportsScopedVariables;
}
function Zl(e) {
  const t = e?.structure?.payload?.variables;
  return Array.isArray(t) ? t.filter(On) : [];
}
function hh(e, t) {
  return e.structure ? {
    ...e,
    structure: {
      ...e.structure,
      payload: { ...e.structure.payload, variables: t }
    }
  } : e;
}
function gh(e, t) {
  return {
    referenceKey: e,
    declaringScopeId: t && t !== tr ? t : tr
  };
}
function Jl(e) {
  if (e && typeof e == "object") {
    const t = e;
    return typeof t.referenceKey == "string" && t.referenceKey ? { referenceKey: t.referenceKey, declaringScopeId: typeof t.declaringScopeId == "string" ? t.declaringScopeId : null } : null;
  }
  if (typeof e == "string") {
    const t = e.trim();
    if (t.startsWith("{"))
      try {
        return Jl(JSON.parse(t));
      } catch {
      }
    return t ? { referenceKey: t, declaringScopeId: null } : null;
  }
  return null;
}
function mh(e, t) {
  if (!e) return "";
  const n = [`workflow:${_a(e.variables)}`], i = (o) => {
    const s = Re(o, t), a = s.flatMap((c) => c.activities.map((u) => u.nodeId));
    n.push(`${o.nodeId}:${_a(Zl(o))}>${a.join(",")}`), s.forEach((c) => c.activities.forEach(i));
  };
  return e.rootActivity && i(e.rootActivity), n.join(";");
}
function _a(e) {
  return (e ?? []).map((t) => `${t.referenceKey}=${t.name}`).join(",");
}
function yh(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e ?? [])
    i.scopeId === t && n.set(i.referenceKey, `Shadows "${i.name}" declared in an outer scope.`);
  return n;
}
const xe = "/_elsa/workflow-management", xh = "/publishing", vt = {
  activities: ["workflows", "activities"],
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"],
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"],
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"]
};
function wh(e) {
  return gr({
    queryKey: vt.activities,
    queryFn: () => ri(e),
    staleTime: 6e4
  });
}
function vh(e) {
  return gr({
    queryKey: vt.activityAvailabilitySettings,
    queryFn: () => Kh(e)
  });
}
function bh(e) {
  return gr({
    queryKey: vt.activityAvailabilityDiagnostics,
    queryFn: () => tu(e)
  });
}
function jh(e) {
  const t = Tl();
  return Rl({
    mutationFn: (n) => qh(e, n),
    onSuccess: (n) => {
      t.setQueryData(vt.activityAvailabilitySettings, n), t.invalidateQueries({ queryKey: vt.activityAvailabilityDiagnostics }), t.invalidateQueries({ queryKey: vt.activities });
    }
  });
}
function Nh(e) {
  return gr({
    queryKey: vt.runtimeDiagnosticsSettings,
    queryFn: () => Xh(e)
  });
}
function Sh(e) {
  const t = Tl();
  return Rl({
    mutationFn: (n) => Uh(e, n),
    onSuccess: () => {
      t.invalidateQueries({ queryKey: vt.runtimeDiagnosticsSettings });
    }
  });
}
async function Ch(e, t) {
  const n = new URLSearchParams({
    state: t.state ?? "active",
    page: t.page.toString(),
    pageSize: t.pageSize.toString()
  }), i = t.search.trim();
  return i && n.set("search", i), e.http.getJson(`${xe}/definitions?${n.toString()}`);
}
async function Gl(e, t) {
  const n = await e.http.getJson(`${xe}/definitions/${encodeURIComponent(t)}`);
  return n.draft ? { ...n, draft: { ...n.draft, state: yr(n.draft.state) } } : n;
}
async function Ih(e, t, n) {
  const i = await e.http.postJson(
    `${xe}/design/scoped-variables/analyze`,
    { state: ni(t), nodeId: n }
  );
  return {
    visibleVariables: Array.isArray(i?.visibleVariables) ? i.visibleVariables : [],
    shadowingWarnings: Array.isArray(i?.shadowingWarnings) ? i.shadowingWarnings : []
  };
}
const xo = (e) => ({ visibleVariables: [], shadowingWarnings: [], status: e });
function kh(e, t, n, i) {
  const o = ae(() => mh(t, i), [i, t]), [s, a] = O(() => xo("loading"));
  return Q(() => {
    if (!t) {
      a(xo("unavailable"));
      return;
    }
    let c = !1;
    return a((u) => ({ ...u, status: "loading" })), Ih(e, t, n).then(
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
async function Eh(e, t) {
  const n = await e.http.getJson(`${xe}/versions/${encodeURIComponent(t)}`);
  return { ...n, state: yr(n.state) };
}
async function Ah(e, t) {
  return e.http.postJson(`${xe}/definitions`, t);
}
async function _h(e, t) {
  await e.http.deleteJson(`${xe}/definitions/${encodeURIComponent(t)}`);
}
async function Dh(e, t) {
  await e.http.postJson(`${xe}/definitions/${encodeURIComponent(t)}/restore`, {});
}
async function $h(e, t) {
  await e.http.deleteJson(`${xe}/definitions/${encodeURIComponent(t)}/permanent`);
}
async function Th(e, t, n) {
  return e.http.requestJson(
    `${xe}/definitions/${encodeURIComponent(t)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(n)
    }
  );
}
async function Rh(e, t) {
  const n = await e.http.putJson(
    `${xe}/drafts/${encodeURIComponent(t.id)}`,
    { state: ni(t.state), layout: t.layout }
  );
  return { ...n, state: yr(n.state) };
}
async function Ph(e, t) {
  return e.http.postJson(`${xe}/drafts/${encodeURIComponent(t)}/promote`, {});
}
async function Mh(e, t) {
  return e.http.postJson(`${xe}/versions/${encodeURIComponent(t)}/publish`, {});
}
async function Lh(e, t) {
  const n = { ...t, state: ni(t.state) };
  try {
    return await e.http.postJson(`${xh}/workflows/drafts/test-runs`, n);
  } catch (i) {
    const o = tg(i);
    if (o) return o;
    throw i;
  }
}
async function bs(e, t) {
  return e.http.postJson(`${xe}/executables/${encodeURIComponent(t)}/run`, {});
}
function zh(e) {
  const t = new URLSearchParams();
  e.scope && e.scope !== "published" && t.set("scope", e.scope), e.includeRetired && t.set("includeRetired", "true");
  const n = t.toString();
  return n ? `?${n}` : "";
}
async function Vh(e, t, n) {
  const i = n ? `?ref=${encodeURIComponent(n)}` : "";
  return e.http.getJson(`${xe}/executables/${encodeURIComponent(t)}${i}`);
}
async function Ql(e, t, n) {
  const i = n ? `?definitionId=${encodeURIComponent(n)}` : "";
  await e.http.deleteJson(`${xe}/executables/${encodeURIComponent(t)}${i}`);
}
async function Oh(e, t, n) {
  await e.http.postJson(`${xe}/executables/${encodeURIComponent(t)}/restore`, {});
}
async function eu(e, t = {}) {
  const n = zh(t), i = [`${xe}/executables${n}`, `/_demo/workflows/executables${n}`], o = [];
  for (const s of i)
    try {
      const a = await e.http.getJson(s);
      return Hh(a);
    } catch (a) {
      o.push(a);
    }
  if (o.length > 0 && o.every(Da)) return [];
  throw o.find((s) => !Da(s)) ?? o[o.length - 1] ?? new Error("Workflow executables could not be loaded.");
}
function Hh(e) {
  return Array.isArray(e) ? e : e.executables ?? [];
}
function Da(e) {
  if (!(e instanceof Error)) return !1;
  const t = e.message.toLowerCase();
  return /\b404\b/.test(t) || t.includes("not found");
}
async function Wh(e, t = {}) {
  const n = new URLSearchParams();
  t.status && n.set("status", t.status), t.runKind && n.set("runKind", t.runKind), t.definitionId && n.set("definitionId", t.definitionId), t.correlationId && n.set("correlationId", t.correlationId), t.take && n.set("take", String(t.take));
  const i = n.toString();
  return e.http.getJson(`/runtime/workflows/instances${i ? `?${i}` : ""}`);
}
async function Fh(e, t) {
  return e.http.getJson(`/runtime/workflows/instances/${encodeURIComponent(t)}`);
}
async function Bh(e, t, n) {
  return e.http.getJson(
    `/runtime/workflows/instances/${encodeURIComponent(t)}/activity-executions/${encodeURIComponent(n)}`
  );
}
async function ri(e) {
  return e.http.getJson(`${xe}/activities`);
}
async function Kh(e) {
  return e.http.getJson(`${xe}/activities/availability/settings`);
}
async function qh(e, t) {
  return e.http.putJson(`${xe}/activities/availability/settings`, t);
}
async function tu(e) {
  return e.http.getJson(`${xe}/activities/availability/diagnostics`);
}
async function Xh(e) {
  return e.http.getJson(`${xe}/runtime-diagnostics/settings`);
}
async function Uh(e, t) {
  return e.http.putJson(`${xe}/runtime-diagnostics/settings`, t);
}
async function Yh(e) {
  const t = await wr(e, [
    `${xe}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  return Array.isArray(t) ? $a(t) : $a(t.items ?? t.activities ?? t.descriptors ?? []);
}
async function Zh(e) {
  const t = await wr(e, [
    `${xe}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(t)) return t;
  const n = t.items ?? t.descriptors ?? t.expressionDescriptors ?? [];
  return n.length > 0 ? n : Wi;
}
async function Jh(e) {
  const t = await wr(e, [
    `${xe}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => Gh(i));
}
function Gh(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = typeof t.alias == "string" && t.alias.length > 0, i = typeof t.typeName == "string" && t.typeName.length > 0;
  return n || i;
}
async function Qh(e) {
  const t = await wr(e, [
    `${xe}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  return (Array.isArray(t) ? t : t.items ?? t.descriptors ?? []).filter((i) => eg(i));
}
function eg(e) {
  return !!e && typeof e == "object" && typeof e.typeName == "string" && e.typeName.length > 0;
}
async function wr(e, t) {
  let n;
  for (const i of t)
    try {
      return await e.http.getJson(i);
    } catch (o) {
      n = o;
    }
  throw n;
}
function $a(e) {
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
function tg(e) {
  const t = e && typeof e == "object" && "payload" in e ? e.payload : null, n = Ta(t);
  if (n) return n;
  if (!(e instanceof Error)) return null;
  try {
    return Ta(JSON.parse(e.message));
  } catch {
    return null;
  }
}
function Ta(e) {
  if (!e || typeof e != "object") return null;
  const t = e;
  return typeof t.testRunId == "string" && typeof t.status == "string" ? t : null;
}
const Wi = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function nu(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const i of e) {
    const o = t(i)?.trim() || "Uncategorized", s = n.get(o);
    s ? s.push(i) : n.set(o, [i]);
  }
  return [...n.entries()].map(([i, o]) => ({ category: i, items: o })).sort((i, o) => i.category.localeCompare(o.category));
}
const ng = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
], ig = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
}, iu = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
}, rg = Object.keys(iu);
function og(e) {
  const t = typeof e == "number" ? rg[e] : e;
  return t && iu[t] || t?.toString() || "";
}
function Lt(e) {
  return typeof e == "string" ? e : typeof e == "number" ? ng[e] ?? "Available" : "Available";
}
function Wn(e) {
  const t = Lt(e);
  return ig[t] ?? t;
}
function ru(e) {
  return Lt(e).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function ou(e) {
  return Lt(e) !== "Available";
}
function su(e) {
  return Lt(e.state) === "BlockedByHostBaseline";
}
function sg(e) {
  return e === "Only" || e === 1 ? "Only" : "AllExcept";
}
function ag(e) {
  return e === "Only" ? 1 : 0;
}
function Oo(e) {
  const t = e?.rules;
  return {
    mode: sg(e?.mode),
    activityTypes: t?.activityTypes ?? [],
    sets: t?.sets ?? []
  };
}
function cg(e) {
  return e.referenceKind === 0 || e.referenceKind === "ActivityType";
}
function kn(e) {
  return e.activityTypeKey ?? e.activityDefinitionId ?? "";
}
function lg(e) {
  return [...e?.items ?? []].filter(cg).filter((t) => t.activityTypeKey && t.activityDefinitionId).sort((t, n) => Fn(t).localeCompare(Fn(n)));
}
function ug(e) {
  return [...e?.items ?? []].filter((t) => {
    const n = Lt(t.state);
    return n === "RemovedFromCatalog" || n === "UnresolvedReference";
  }).sort((t, n) => (t.referenceName ?? "").localeCompare(n.referenceName ?? ""));
}
function dg(e) {
  return nu(e, (t) => t.category).map((t) => ({ category: t.category, entries: t.items }));
}
function fg(e, t, n) {
  const i = e[t].includes(n);
  return e.mode === "Only" ? i : !i;
}
function wo(e, t, n, i) {
  const o = e.mode === "Only" ? i : !i;
  return e[t].includes(n) === o ? e : { ...e, [t]: gg(e[t], n) };
}
function pg(e, t, n) {
  const i = new Set(n.activityTypes), o = new Set(n.sets), s = /* @__PURE__ */ new Map();
  for (const c of t)
    if (o.has(c.name))
      for (const u of c.activityTypeKeys ?? [])
        s.has(u) || s.set(u, c.name);
  const a = /* @__PURE__ */ new Map();
  for (const c of e) {
    const u = kn(c);
    if (su(c)) {
      a.set(u, { enabled: !1, lockedBy: "host-baseline" });
      continue;
    }
    const l = s.get(u), d = i.has(u) || l !== void 0, f = n.mode === "Only" ? d : !d;
    a.set(u, l ? { enabled: f, lockedBy: "set-rule", governingSet: l } : { enabled: f, lockedBy: null });
  }
  return a;
}
function hg(e, t) {
  const n = Oo(t), i = (o) => [...o].sort((s, a) => s.localeCompare(a)).join(`
`);
  return e.mode !== n.mode || i(e.activityTypes) !== i(n.activityTypes) || i(e.sets) !== i(n.sets);
}
function gg(e, t) {
  return e.includes(t) ? e.filter((n) => n !== t) : [...e, t].sort((n, i) => n.localeCompare(i));
}
function Fn(e) {
  const t = e?.displayName?.trim();
  if (t && t.toLowerCase() !== "activity") return t;
  const n = yg(e?.activityTypeKey);
  return mg(n) || t || e?.activityTypeKey || "Activity";
}
function Ra(e) {
  const t = au(e?.activityTypeKey);
  if (t.length === 0) return "";
  const n = t[t.length - 1], i = t[t.length - 2];
  return n === "Activity" && i ? `${i}.${n}` : n;
}
function Pa(e) {
  const t = e.description?.trim();
  if (t) return t;
  const n = e.reason?.trim();
  return !n || !ou(e.state) ? "" : n;
}
function mg(e) {
  return e.replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/\bHttp\b/g, "HTTP").replace(/\bJson\b/g, "JSON").replace(/\bJava Script\b/g, "JavaScript").replace(/\bUrl\b/g, "URL").replace(/\bXml\b/g, "XML").trim();
}
function yg(e) {
  const t = au(e), n = t[t.length - 1] ?? "";
  return n === "Activity" && t.length > 1 ? t[t.length - 2] : n;
}
function au(e) {
  return e?.split(".").filter(Boolean) ?? [];
}
function Ma(e) {
  return (e ?? []).flatMap((t) => {
    if (!On(t)) return [];
    const n = (dt(t, ["displayName", "DisplayName"]) || dt(t, ms)).trim();
    if (!n) return [];
    const i = (gs(t).alias || dt(t, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name: n, typeName: i, description: dt(t, ["description", "Description"]).trim() }];
  });
}
function xg(e, t) {
  const n = new Set(e.filter((i) => !!i));
  return (t?.items ?? []).find((i) => ou(i.state) ? [i.activityDefinitionId, i.activityTypeKey, i.referenceName].some((o) => o && n.has(o)) : !1) ?? null;
}
const wg = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
function cu(e) {
  const t = e.trim();
  return t ? t.charAt(0).toLowerCase() + t.slice(1) : e;
}
function js(e) {
  return cu(e.name);
}
function vg(e, t) {
  const n = js(t), i = e[n];
  return t.isWrapped === !1 ? i ?? t.defaultValue ?? "" : uu(i, t);
}
function lu(e, t) {
  return uu(e[js(t)], t);
}
function bg(e, t) {
  return {
    ...e,
    expression: {
      type: e.expression.type || "Literal",
      value: t
    }
  };
}
function jg(e, t) {
  return {
    ...e,
    expression: {
      type: t,
      value: e.expression.value
    }
  };
}
function La(e, t, n) {
  return {
    ...e,
    [js(t)]: n
  };
}
function Ng(e, t) {
  return t.isWrapped === !1 ? vg(e, t) : lu(e, t).expression.value;
}
function uu(e, t) {
  return Dg(e) ? {
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
const Sg = /* @__PURE__ */ new Set([
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
function Cg(e) {
  const t = e?.trim();
  if (!t) return null;
  const n = /^([\w.+]+)\[\]/.exec(t);
  if (n) return { elementTypeName: n[1] };
  const i = t.indexOf("`");
  if (i < 0) return null;
  const o = t.slice(0, i), s = (o.split(".").pop() ?? o).toLowerCase();
  return Sg.has(s) ? { elementTypeName: Ig(t.slice(i)) } : null;
}
function Ig(e) {
  const t = /\[\[([\w.+]+)/.exec(e);
  if (t) return t[1];
  const n = /\[([\w.+]+)/.exec(e);
  return n ? n[1] : null;
}
function kg(e) {
  const t = e.uiHint?.toLowerCase();
  return t === "json" || t === "code" ? !0 : e.uiSpecifications?.repeater === !1;
}
function Eg(e) {
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
function Ag(e) {
  const t = (e ?? "").toLowerCase();
  return t === "system.boolean" || t === "boolean" || t === "bool" ? !1 : "";
}
function _g(e, t) {
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
function Dg(e) {
  if (!e || typeof e != "object") return !1;
  const t = e, n = t.expression;
  return typeof t.typeName == "string" && !!n && typeof n == "object" && typeof n.type == "string";
}
function Ns(e) {
  return Ss(e?.trim() ?? "") || e;
}
function Ss(e) {
  if (!e) return "";
  const t = $g(e);
  if (!t) return "";
  const n = /^(.*)((?:\[\])+)$/.exec(t);
  if (n) return `${Ss(n[1])}${n[2]}`;
  const i = t.indexOf("`");
  if (i >= 0) {
    const o = za(t.slice(0, i)), s = Tg(t.slice(i));
    return s.length > 0 ? `${o}<${s.join(", ")}>` : o;
  }
  return za(t);
}
function $g(e) {
  let t = 0;
  for (let n = 0; n < e.length; n++) {
    const i = e[n];
    if (i === "[") t++;
    else if (i === "]") t--;
    else if (i === "," && t === 0) return e.slice(0, n).trim();
  }
  return e.trim();
}
function za(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.split("+").filter(Boolean).at(-1) ?? t;
}
function Tg(e) {
  const t = e.indexOf("[");
  if (t < 0) return [];
  const n = Va(e, t);
  return n == null ? [] : Rg(n).map((i) => {
    const o = i.trim(), s = o.startsWith("[") ? Va(o, 0) ?? o : o;
    return Ss(s);
  }).filter(Boolean);
}
function Va(e, t) {
  let n = 0;
  for (let i = t; i < e.length; i++)
    if (e[i] === "[") n++;
    else if (e[i] === "]" && --n === 0) return e.slice(t + 1, i);
  return null;
}
function Rg(e) {
  const t = [];
  let n = 0, i = 0;
  for (let o = 0; o < e.length; o++) {
    const s = e[o];
    s === "[" ? n++ : s === "]" ? n-- : s === "," && n === 0 && (t.push(e.slice(i, o)), i = o + 1);
  }
  return t.push(e.slice(i)), t.map((o) => o.trim()).filter(Boolean);
}
function _e(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? e : t.toLocaleString();
}
function Cs(e, t) {
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
function zt(e) {
  return e?.split(".").filter(Boolean).at(-1);
}
function vr(e) {
  switch (e) {
    case "flowchart":
      return /* @__PURE__ */ r.jsx(El, { size: 15 });
    case "sequence":
      return /* @__PURE__ */ r.jsx(pr, { size: 15 });
    case "terminal":
      return /* @__PURE__ */ r.jsx(Xf, { size: 15 });
    case "runtime":
      return /* @__PURE__ */ r.jsx(Rt, { size: 15 });
    case "trigger":
      return /* @__PURE__ */ r.jsx(qf, { size: 15 });
    default:
      return /* @__PURE__ */ r.jsx(ei, { size: 15 });
  }
}
function Oa(e) {
  return `${e.activityTypeKey ?? ""}|${e.activityDefinitionId ?? ""}`;
}
function Pg(e, t) {
  const n = (a) => (a ?? "").split(".").map((c) => Number.parseInt(c, 10) || 0), i = n(e), o = n(t), s = Math.max(i.length, o.length);
  for (let a = 0; a < s; a++) {
    const c = (i[a] ?? 0) - (o[a] ?? 0);
    if (c !== 0) return c;
  }
  return 0;
}
function Mg(e) {
  if (e.lockedBy === "host-baseline") return "Blocked by the host baseline";
  if (e.lockedBy === "set-rule") return `Controlled by the "${e.governingSet}" set rule`;
}
function du({ icon: e }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": e, "aria-hidden": "true", children: vr(e) });
}
function Lg({ context: e }) {
  const t = vh(e), n = bh(e), i = wh(e), o = jh(e), s = t.data ?? null, a = n.data ?? null, c = t.isLoading || n.isLoading, u = o.isPending, [l, d] = O(() => Oo(s)), [f, p] = O(""), [h, m] = O(!1), [v, x] = O(null), [y, j] = O(null);
  Q(() => {
    d(Oo(s));
  }, [s]);
  const g = ae(() => lg(a), [a]), w = ae(() => ug(a), [a]), S = ae(() => a?.sets ?? [], [a]), b = ae(
    () => pg(g, S, l),
    [g, S, l]
  ), I = (H) => b.get(kn(H)) ?? { enabled: !0, lockedBy: null }, A = ae(() => {
    const H = /* @__PURE__ */ new Map();
    for (const U of i.data?.activities ?? []) {
      if (!U.activityTypeKey) continue;
      const G = H.get(U.activityTypeKey);
      (!G || Pg(U.version, G.version) > 0) && H.set(U.activityTypeKey, U);
    }
    return H;
  }, [i.data]), D = (H) => A.get(H.activityTypeKey ?? ""), M = ae(() => {
    const H = f.trim().toLowerCase(), U = h ? g.filter((G) => !(b.get(kn(G))?.enabled ?? !0)) : g;
    return H ? U.filter((G) => [
      Fn(G),
      Ra(G),
      Pa(G),
      G.activityTypeKey,
      G.category
    ].some((X) => (X ?? "").toLowerCase().includes(H))) : U;
  }, [g, f, h, b]), k = ae(() => dg(M), [M]), R = y ? g.find((H) => Oa(H) === y) ?? null : null, L = g.filter(su).length, C = g.filter((H) => Lt(H.state) === "HiddenByManagementSettings").length, _ = ae(() => hg(l, s), [l, s]), N = o.error ?? t.error ?? n.error, E = N instanceof Error ? N.message : N ? "Activity availability could not be loaded." : null, T = (H) => {
    x(null), d(H);
  }, $ = (H) => T((U) => ({ ...U, mode: H })), z = (H, U) => T((G) => wo(G, "activityTypes", H, U)), F = (H, U) => T((G) => H.reduce((X, P) => wo(X, "activityTypes", P, U), G)), B = (H, U) => T((G) => wo(G, "sets", H, U)), Z = () => {
    x(null), o.mutate(
      {
        scope: s?.scope ?? "host-default",
        mode: ag(l.mode),
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
          /* @__PURE__ */ r.jsx(Uf, { size: 18 }),
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
          /* @__PURE__ */ r.jsx(ya, { size: 15 }),
          /* @__PURE__ */ r.jsxs("span", { children: [
            /* @__PURE__ */ r.jsx("strong", { children: "Only" }),
            /* @__PURE__ */ r.jsx("em", { children: "Activities are hidden unless you turn them on" })
          ] })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "availability-counts", children: [
        /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx(ya, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(Vn, { size: 14 }),
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
              checked: fg(l, "sets", H.name),
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
            /* @__PURE__ */ r.jsx(Al, { size: 14 }),
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
              /* @__PURE__ */ r.jsx(hr, { size: 14 }),
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
                      onChange: () => F(U.map(kn), !X)
                    }
                  )
                ] }),
                H.entries.map((q) => {
                  const de = kn(q), le = Oa(q), te = Lt(q.state), ie = I(q), fe = ie.lockedBy !== null, W = gn(D(q)), ne = Fn(q), ge = Ra(q), ye = Pa(q), Ee = y === le;
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
                          /* @__PURE__ */ r.jsx(du, { icon: W }),
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
                    te !== "Available" && /* @__PURE__ */ r.jsx("em", { className: `availability-state ${ru(q.state)}`, children: Wn(q.state) }),
                    /* @__PURE__ */ r.jsx(
                      "input",
                      {
                        type: "checkbox",
                        role: "switch",
                        className: "wf-switch-input",
                        "aria-label": `${ne} available in new workflows`,
                        title: Mg(ie),
                        checked: ie.enabled,
                        disabled: c || u || fe,
                        onChange: (De) => z(de, De.target.checked)
                      }
                    )
                  ] }, le);
                })
              ] }, H.category);
            })
          ] }),
          R && /* @__PURE__ */ r.jsx(
            zg,
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
          /* @__PURE__ */ r.jsx(Vn, { size: 14 }),
          " Unresolved references"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "availability-unresolved-list", children: w.map((H) => /* @__PURE__ */ r.jsxs("span", { children: [
          /* @__PURE__ */ r.jsx("strong", { children: H.referenceName }),
          /* @__PURE__ */ r.jsx("em", { children: Wn(H.state) })
        ] }, `${H.layer}-${H.referenceKind}-${H.referenceName}`)) })
      ] })
    ] })
  ] });
}
function zg({
  entry: e,
  catalogItem: t,
  onClose: n
}) {
  const i = Fn(e), o = e.description?.trim() || t?.description?.trim(), s = ae(() => Ma(t?.inputs), [t]), a = ae(() => Ma(t?.outputs), [t]), c = ae(() => JSON.stringify({ diagnostic: e, catalog: t }, null, 2), [e, t]), u = [
    ["Type", e.activityTypeKey],
    ["Definition ID", e.activityDefinitionId],
    ["Category", e.category],
    ["Version", t?.version],
    ["Execution", t?.executionType],
    ["Policy layer", og(e.layer)]
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "availability-details", "aria-label": `${i} details`, children: [
    /* @__PURE__ */ r.jsxs("header", { className: "availability-details-header", children: [
      /* @__PURE__ */ r.jsx(du, { icon: gn(t) }),
      /* @__PURE__ */ r.jsx("h4", { children: i }),
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-icon-button availability-details-close", "aria-label": "Close details", onClick: n, children: /* @__PURE__ */ r.jsx(ds, { size: 14 }) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "availability-details-body", children: [
      /* @__PURE__ */ r.jsxs("p", { className: "availability-details-status", children: [
        /* @__PURE__ */ r.jsx("em", { className: `availability-state ${ru(e.state)}`, children: Wn(e.state) }),
        e.reason?.trim() && /* @__PURE__ */ r.jsx("span", { children: e.reason })
      ] }),
      o && /* @__PURE__ */ r.jsx("p", { className: "availability-details-description", children: o }),
      /* @__PURE__ */ r.jsx("dl", { className: "availability-details-meta", children: u.map(([l, d]) => d?.trim() ? /* @__PURE__ */ r.jsxs(Fe.Fragment, { children: [
        /* @__PURE__ */ r.jsx("dt", { children: l }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx("code", { children: d }) })
      ] }, l) : null) }),
      s.length > 0 && /* @__PURE__ */ r.jsx(Ha, { title: "Inputs", items: s }),
      a.length > 0 && /* @__PURE__ */ r.jsx(Ha, { title: "Outputs", items: a }),
      !t && /* @__PURE__ */ r.jsx("p", { className: "wf-muted availability-details-note", children: "Catalog metadata is unavailable for this activity — it is not exposed by the currently saved policy." }),
      /* @__PURE__ */ r.jsxs("details", { className: "availability-details-raw", children: [
        /* @__PURE__ */ r.jsx("summary", { children: "Raw metadata" }),
        /* @__PURE__ */ r.jsx("pre", { children: c })
      ] })
    ] })
  ] });
}
function Ha({ title: e, items: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "availability-details-arguments", children: [
    /* @__PURE__ */ r.jsx("h5", { children: e }),
    /* @__PURE__ */ r.jsx("ul", { children: t.map((n) => /* @__PURE__ */ r.jsxs("li", { title: n.description || void 0, children: [
      /* @__PURE__ */ r.jsx("strong", { children: n.name }),
      n.typeName && /* @__PURE__ */ r.jsx("code", { children: Ns(n.typeName) })
    ] }, n.name)) })
  ] });
}
const fu = ["Off", "Metadata", "DiagnosticSnapshot", "Payload"], Wa = new Map(fu.map((e, t) => [e, t])), Vg = [
  { id: "workflowInputs", label: "Workflow inputs", detail: "Start arguments and workflow-level input values." },
  { id: "workflowOutputs", label: "Workflow outputs", detail: "Values emitted as workflow outputs." },
  { id: "activityInputs", label: "Activity inputs", detail: "Materialized values passed to activities." },
  { id: "activityOutputs", label: "Activity outputs", detail: "Values produced by completed activities." },
  { id: "containerVariables", label: "Container variables", detail: "Runtime values scoped to containers." },
  { id: "durableValues", label: "Durable values", detail: "Persisted wait/resume payload evidence." },
  { id: "incidents", label: "Incidents", detail: "Fault and incident diagnostic material." },
  { id: "diagnostics", label: "Diagnostics", detail: "Internal runtime diagnostic payloads." }
];
function Og({ context: e }) {
  const t = Nh(e), n = Sh(e), i = t.data ?? null, o = t.isLoading, s = n.isPending, a = i?.permissions.canManage ?? !1, [c, u] = O(() => Fa(i)), [l, d] = O(null);
  Q(() => {
    u(Fa(i));
  }, [i]);
  const f = n.error ?? t.error, p = f instanceof Error ? f.message : f ? "Runtime diagnostics settings could not be loaded." : null, h = i?.hostPolicy.limitationReasons ?? [], m = i?.effective.limitationReasons ?? [], v = ae(() => fu.filter((g) => Hg(g, i)), [i]), x = (g) => u((w) => ({ ...w, defaultLevel: g })), y = (g, w) => u((S) => {
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
          /* @__PURE__ */ r.jsx(xa, { size: 18 }),
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
            /* @__PURE__ */ r.jsx(Yf, { size: 14 }),
            " Default capture"
          ] }),
          i && /* @__PURE__ */ r.jsxs("span", { className: "runtime-diagnostics-effective", children: [
            "Effective: ",
            En(i.effective.defaultLevel)
          ] })
        ] }),
        /* @__PURE__ */ r.jsx(
          Fg,
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
          /* @__PURE__ */ r.jsx(xa, { size: 14 }),
          " Subject overrides"
        ] }),
        /* @__PURE__ */ r.jsx("div", { className: "runtime-diagnostics-subjects", children: Vg.map((g) => {
          const w = c.subjectOverrides[g.id] ?? "inherit", S = i ? Wg(i, g.id) : null;
          return /* @__PURE__ */ r.jsxs("label", { className: "runtime-diagnostics-subject", children: [
            /* @__PURE__ */ r.jsxs("span", { children: [
              /* @__PURE__ */ r.jsx("strong", { children: g.label }),
              /* @__PURE__ */ r.jsx("em", { children: g.detail })
            ] }),
            /* @__PURE__ */ r.jsxs("span", { className: "runtime-diagnostics-subject-controls", children: [
              S && /* @__PURE__ */ r.jsxs("code", { children: [
                "Effective: ",
                En(S)
              ] }),
              /* @__PURE__ */ r.jsxs(
                "select",
                {
                  value: w,
                  disabled: o || s || !a,
                  onChange: (b) => y(g.id, b.target.value),
                  children: [
                    /* @__PURE__ */ r.jsx("option", { value: "inherit", children: "Inherit default" }),
                    v.map((b) => /* @__PURE__ */ r.jsx("option", { value: b, children: En(b) }, b))
                  ]
                }
              )
            ] })
          ] }, g.id);
        }) })
      ] }),
      (h.length > 0 || m.length > 0 || !a) && /* @__PURE__ */ r.jsxs("section", { className: "runtime-diagnostics-section", children: [
        /* @__PURE__ */ r.jsxs("h3", { className: "wf-section-label", children: [
          /* @__PURE__ */ r.jsx(Vn, { size: 14 }),
          " Policy"
        ] }),
        /* @__PURE__ */ r.jsxs("div", { className: "runtime-diagnostics-policy", children: [
          !a && /* @__PURE__ */ r.jsx("span", { children: "Current user can view settings but cannot change them." }),
          i && /* @__PURE__ */ r.jsxs("span", { children: [
            "Host maximum: ",
            En(i.hostPolicy.maximumLevel)
          ] }),
          m.map((g) => /* @__PURE__ */ r.jsx("span", { children: g }, `effective-${g}`)),
          h.map((g) => /* @__PURE__ */ r.jsx("span", { children: g }, `host-${g}`))
        ] })
      ] })
    ] })
  ] });
}
function Fa(e) {
  return {
    defaultLevel: e?.requested.defaultLevel ?? "DiagnosticSnapshot",
    subjectOverrides: { ...e?.requested.subjectOverrides ?? {} }
  };
}
function Hg(e, t) {
  return t ? e === "Payload" && !t.permissions.canEnableFullPayloads ? !1 : (Wa.get(e) ?? 0) <= (Wa.get(t.hostPolicy.maximumLevel) ?? 0) : e !== "Payload";
}
function Wg(e, t) {
  return e.effective.subjectOverrides?.[t] ?? e.effective.defaultLevel;
}
function En(e) {
  return e === "DiagnosticSnapshot" ? "Diagnostic snapshot" : e;
}
function Fg({
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
        /* @__PURE__ */ r.jsx("strong", { children: En(o) }),
        /* @__PURE__ */ r.jsx("em", { children: Bg(o) })
      ]
    },
    o
  )) });
}
function Bg(e) {
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
var Kg = { value: () => {
} };
function br() {
  for (var e = 0, t = arguments.length, n = {}, i; e < t; ++e) {
    if (!(i = arguments[e] + "") || i in n || /[\s.]/.test(i)) throw new Error("illegal type: " + i);
    n[i] = [];
  }
  return new Fi(n);
}
function Fi(e) {
  this._ = e;
}
function qg(e, t) {
  return e.trim().split(/^|\s+/).map(function(n) {
    var i = "", o = n.indexOf(".");
    if (o >= 0 && (i = n.slice(o + 1), n = n.slice(0, o)), n && !t.hasOwnProperty(n)) throw new Error("unknown type: " + n);
    return { type: n, name: i };
  });
}
Fi.prototype = br.prototype = {
  constructor: Fi,
  on: function(e, t) {
    var n = this._, i = qg(e + "", n), o, s = -1, a = i.length;
    if (arguments.length < 2) {
      for (; ++s < a; ) if ((o = (e = i[s]).type) && (o = Xg(n[o], e.name))) return o;
      return;
    }
    if (t != null && typeof t != "function") throw new Error("invalid callback: " + t);
    for (; ++s < a; )
      if (o = (e = i[s]).type) n[o] = Ba(n[o], e.name, t);
      else if (t == null) for (o in n) n[o] = Ba(n[o], e.name, null);
    return this;
  },
  copy: function() {
    var e = {}, t = this._;
    for (var n in t) e[n] = t[n].slice();
    return new Fi(e);
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
function Xg(e, t) {
  for (var n = 0, i = e.length, o; n < i; ++n)
    if ((o = e[n]).name === t)
      return o.value;
}
function Ba(e, t, n) {
  for (var i = 0, o = e.length; i < o; ++i)
    if (e[i].name === t) {
      e[i] = Kg, e = e.slice(0, i).concat(e.slice(i + 1));
      break;
    }
  return n != null && e.push({ name: t, value: n }), e;
}
var Ho = "http://www.w3.org/1999/xhtml";
const Ka = {
  svg: "http://www.w3.org/2000/svg",
  xhtml: Ho,
  xlink: "http://www.w3.org/1999/xlink",
  xml: "http://www.w3.org/XML/1998/namespace",
  xmlns: "http://www.w3.org/2000/xmlns/"
};
function jr(e) {
  var t = e += "", n = t.indexOf(":");
  return n >= 0 && (t = e.slice(0, n)) !== "xmlns" && (e = e.slice(n + 1)), Ka.hasOwnProperty(t) ? { space: Ka[t], local: e } : e;
}
function Ug(e) {
  return function() {
    var t = this.ownerDocument, n = this.namespaceURI;
    return n === Ho && t.documentElement.namespaceURI === Ho ? t.createElement(e) : t.createElementNS(n, e);
  };
}
function Yg(e) {
  return function() {
    return this.ownerDocument.createElementNS(e.space, e.local);
  };
}
function pu(e) {
  var t = jr(e);
  return (t.local ? Yg : Ug)(t);
}
function Zg() {
}
function Is(e) {
  return e == null ? Zg : function() {
    return this.querySelector(e);
  };
}
function Jg(e) {
  typeof e != "function" && (e = Is(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = new Array(a), u, l, d = 0; d < a; ++d)
      (u = s[d]) && (l = e.call(u, u.__data__, d, s)) && ("__data__" in u && (l.__data__ = u.__data__), c[d] = l);
  return new Be(i, this._parents);
}
function Gg(e) {
  return e == null ? [] : Array.isArray(e) ? e : Array.from(e);
}
function Qg() {
  return [];
}
function hu(e) {
  return e == null ? Qg : function() {
    return this.querySelectorAll(e);
  };
}
function em(e) {
  return function() {
    return Gg(e.apply(this, arguments));
  };
}
function tm(e) {
  typeof e == "function" ? e = em(e) : e = hu(e);
  for (var t = this._groups, n = t.length, i = [], o = [], s = 0; s < n; ++s)
    for (var a = t[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && (i.push(e.call(u, u.__data__, l, a)), o.push(u));
  return new Be(i, o);
}
function gu(e) {
  return function() {
    return this.matches(e);
  };
}
function mu(e) {
  return function(t) {
    return t.matches(e);
  };
}
var nm = Array.prototype.find;
function im(e) {
  return function() {
    return nm.call(this.children, e);
  };
}
function rm() {
  return this.firstElementChild;
}
function om(e) {
  return this.select(e == null ? rm : im(typeof e == "function" ? e : mu(e)));
}
var sm = Array.prototype.filter;
function am() {
  return Array.from(this.children);
}
function cm(e) {
  return function() {
    return sm.call(this.children, e);
  };
}
function lm(e) {
  return this.selectAll(e == null ? am : cm(typeof e == "function" ? e : mu(e)));
}
function um(e) {
  typeof e != "function" && (e = gu(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new Be(i, this._parents);
}
function yu(e) {
  return new Array(e.length);
}
function dm() {
  return new Be(this._enter || this._groups.map(yu), this._parents);
}
function nr(e, t) {
  this.ownerDocument = e.ownerDocument, this.namespaceURI = e.namespaceURI, this._next = null, this._parent = e, this.__data__ = t;
}
nr.prototype = {
  constructor: nr,
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
function fm(e) {
  return function() {
    return e;
  };
}
function pm(e, t, n, i, o, s) {
  for (var a = 0, c, u = t.length, l = s.length; a < l; ++a)
    (c = t[a]) ? (c.__data__ = s[a], i[a] = c) : n[a] = new nr(e, s[a]);
  for (; a < u; ++a)
    (c = t[a]) && (o[a] = c);
}
function hm(e, t, n, i, o, s, a) {
  var c, u, l = /* @__PURE__ */ new Map(), d = t.length, f = s.length, p = new Array(d), h;
  for (c = 0; c < d; ++c)
    (u = t[c]) && (p[c] = h = a.call(u, u.__data__, c, t) + "", l.has(h) ? o[c] = u : l.set(h, u));
  for (c = 0; c < f; ++c)
    h = a.call(e, s[c], c, s) + "", (u = l.get(h)) ? (i[c] = u, u.__data__ = s[c], l.delete(h)) : n[c] = new nr(e, s[c]);
  for (c = 0; c < d; ++c)
    (u = t[c]) && l.get(p[c]) === u && (o[c] = u);
}
function gm(e) {
  return e.__data__;
}
function mm(e, t) {
  if (!arguments.length) return Array.from(this, gm);
  var n = t ? hm : pm, i = this._parents, o = this._groups;
  typeof e != "function" && (e = fm(e));
  for (var s = o.length, a = new Array(s), c = new Array(s), u = new Array(s), l = 0; l < s; ++l) {
    var d = i[l], f = o[l], p = f.length, h = ym(e.call(d, d && d.__data__, l, i)), m = h.length, v = c[l] = new Array(m), x = a[l] = new Array(m), y = u[l] = new Array(p);
    n(d, f, v, x, y, h, t);
    for (var j = 0, g = 0, w, S; j < m; ++j)
      if (w = v[j]) {
        for (j >= g && (g = j + 1); !(S = x[g]) && ++g < m; ) ;
        w._next = S || null;
      }
  }
  return a = new Be(a, i), a._enter = c, a._exit = u, a;
}
function ym(e) {
  return typeof e == "object" && "length" in e ? e : Array.from(e);
}
function xm() {
  return new Be(this._exit || this._groups.map(yu), this._parents);
}
function wm(e, t, n) {
  var i = this.enter(), o = this, s = this.exit();
  return typeof e == "function" ? (i = e(i), i && (i = i.selection())) : i = i.append(e + ""), t != null && (o = t(o), o && (o = o.selection())), n == null ? s.remove() : n(s), i && o ? i.merge(o).order() : o;
}
function vm(e) {
  for (var t = e.selection ? e.selection() : e, n = this._groups, i = t._groups, o = n.length, s = i.length, a = Math.min(o, s), c = new Array(o), u = 0; u < a; ++u)
    for (var l = n[u], d = i[u], f = l.length, p = c[u] = new Array(f), h, m = 0; m < f; ++m)
      (h = l[m] || d[m]) && (p[m] = h);
  for (; u < o; ++u)
    c[u] = n[u];
  return new Be(c, this._parents);
}
function bm() {
  for (var e = this._groups, t = -1, n = e.length; ++t < n; )
    for (var i = e[t], o = i.length - 1, s = i[o], a; --o >= 0; )
      (a = i[o]) && (s && a.compareDocumentPosition(s) ^ 4 && s.parentNode.insertBefore(a, s), s = a);
  return this;
}
function jm(e) {
  e || (e = Nm);
  function t(f, p) {
    return f && p ? e(f.__data__, p.__data__) : !f - !p;
  }
  for (var n = this._groups, i = n.length, o = new Array(i), s = 0; s < i; ++s) {
    for (var a = n[s], c = a.length, u = o[s] = new Array(c), l, d = 0; d < c; ++d)
      (l = a[d]) && (u[d] = l);
    u.sort(t);
  }
  return new Be(o, this._parents).order();
}
function Nm(e, t) {
  return e < t ? -1 : e > t ? 1 : e >= t ? 0 : NaN;
}
function Sm() {
  var e = arguments[0];
  return arguments[0] = this, e.apply(null, arguments), this;
}
function Cm() {
  return Array.from(this);
}
function Im() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length; o < s; ++o) {
      var a = i[o];
      if (a) return a;
    }
  return null;
}
function km() {
  let e = 0;
  for (const t of this) ++e;
  return e;
}
function Em() {
  return !this.node();
}
function Am(e) {
  for (var t = this._groups, n = 0, i = t.length; n < i; ++n)
    for (var o = t[n], s = 0, a = o.length, c; s < a; ++s)
      (c = o[s]) && e.call(c, c.__data__, s, o);
  return this;
}
function _m(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function Dm(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function $m(e, t) {
  return function() {
    this.setAttribute(e, t);
  };
}
function Tm(e, t) {
  return function() {
    this.setAttributeNS(e.space, e.local, t);
  };
}
function Rm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttribute(e) : this.setAttribute(e, n);
  };
}
function Pm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? this.removeAttributeNS(e.space, e.local) : this.setAttributeNS(e.space, e.local, n);
  };
}
function Mm(e, t) {
  var n = jr(e);
  if (arguments.length < 2) {
    var i = this.node();
    return n.local ? i.getAttributeNS(n.space, n.local) : i.getAttribute(n);
  }
  return this.each((t == null ? n.local ? Dm : _m : typeof t == "function" ? n.local ? Pm : Rm : n.local ? Tm : $m)(n, t));
}
function xu(e) {
  return e.ownerDocument && e.ownerDocument.defaultView || e.document && e || e.defaultView;
}
function Lm(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function zm(e, t, n) {
  return function() {
    this.style.setProperty(e, t, n);
  };
}
function Vm(e, t, n) {
  return function() {
    var i = t.apply(this, arguments);
    i == null ? this.style.removeProperty(e) : this.style.setProperty(e, i, n);
  };
}
function Om(e, t, n) {
  return arguments.length > 1 ? this.each((t == null ? Lm : typeof t == "function" ? Vm : zm)(e, t, n ?? "")) : rn(this.node(), e);
}
function rn(e, t) {
  return e.style.getPropertyValue(t) || xu(e).getComputedStyle(e, null).getPropertyValue(t);
}
function Hm(e) {
  return function() {
    delete this[e];
  };
}
function Wm(e, t) {
  return function() {
    this[e] = t;
  };
}
function Fm(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    n == null ? delete this[e] : this[e] = n;
  };
}
function Bm(e, t) {
  return arguments.length > 1 ? this.each((t == null ? Hm : typeof t == "function" ? Fm : Wm)(e, t)) : this.node()[e];
}
function wu(e) {
  return e.trim().split(/^|\s+/);
}
function ks(e) {
  return e.classList || new vu(e);
}
function vu(e) {
  this._node = e, this._names = wu(e.getAttribute("class") || "");
}
vu.prototype = {
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
function bu(e, t) {
  for (var n = ks(e), i = -1, o = t.length; ++i < o; ) n.add(t[i]);
}
function ju(e, t) {
  for (var n = ks(e), i = -1, o = t.length; ++i < o; ) n.remove(t[i]);
}
function Km(e) {
  return function() {
    bu(this, e);
  };
}
function qm(e) {
  return function() {
    ju(this, e);
  };
}
function Xm(e, t) {
  return function() {
    (t.apply(this, arguments) ? bu : ju)(this, e);
  };
}
function Um(e, t) {
  var n = wu(e + "");
  if (arguments.length < 2) {
    for (var i = ks(this.node()), o = -1, s = n.length; ++o < s; ) if (!i.contains(n[o])) return !1;
    return !0;
  }
  return this.each((typeof t == "function" ? Xm : t ? Km : qm)(n, t));
}
function Ym() {
  this.textContent = "";
}
function Zm(e) {
  return function() {
    this.textContent = e;
  };
}
function Jm(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.textContent = t ?? "";
  };
}
function Gm(e) {
  return arguments.length ? this.each(e == null ? Ym : (typeof e == "function" ? Jm : Zm)(e)) : this.node().textContent;
}
function Qm() {
  this.innerHTML = "";
}
function ey(e) {
  return function() {
    this.innerHTML = e;
  };
}
function ty(e) {
  return function() {
    var t = e.apply(this, arguments);
    this.innerHTML = t ?? "";
  };
}
function ny(e) {
  return arguments.length ? this.each(e == null ? Qm : (typeof e == "function" ? ty : ey)(e)) : this.node().innerHTML;
}
function iy() {
  this.nextSibling && this.parentNode.appendChild(this);
}
function ry() {
  return this.each(iy);
}
function oy() {
  this.previousSibling && this.parentNode.insertBefore(this, this.parentNode.firstChild);
}
function sy() {
  return this.each(oy);
}
function ay(e) {
  var t = typeof e == "function" ? e : pu(e);
  return this.select(function() {
    return this.appendChild(t.apply(this, arguments));
  });
}
function cy() {
  return null;
}
function ly(e, t) {
  var n = typeof e == "function" ? e : pu(e), i = t == null ? cy : typeof t == "function" ? t : Is(t);
  return this.select(function() {
    return this.insertBefore(n.apply(this, arguments), i.apply(this, arguments) || null);
  });
}
function uy() {
  var e = this.parentNode;
  e && e.removeChild(this);
}
function dy() {
  return this.each(uy);
}
function fy() {
  var e = this.cloneNode(!1), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function py() {
  var e = this.cloneNode(!0), t = this.parentNode;
  return t ? t.insertBefore(e, this.nextSibling) : e;
}
function hy(e) {
  return this.select(e ? py : fy);
}
function gy(e) {
  return arguments.length ? this.property("__data__", e) : this.node().__data__;
}
function my(e) {
  return function(t) {
    e.call(this, t, this.__data__);
  };
}
function yy(e) {
  return e.trim().split(/^|\s+/).map(function(t) {
    var n = "", i = t.indexOf(".");
    return i >= 0 && (n = t.slice(i + 1), t = t.slice(0, i)), { type: t, name: n };
  });
}
function xy(e) {
  return function() {
    var t = this.__on;
    if (t) {
      for (var n = 0, i = -1, o = t.length, s; n < o; ++n)
        s = t[n], (!e.type || s.type === e.type) && s.name === e.name ? this.removeEventListener(s.type, s.listener, s.options) : t[++i] = s;
      ++i ? t.length = i : delete this.__on;
    }
  };
}
function wy(e, t, n) {
  return function() {
    var i = this.__on, o, s = my(t);
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
function vy(e, t, n) {
  var i = yy(e + ""), o, s = i.length, a;
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
  for (c = t ? wy : xy, o = 0; o < s; ++o) this.each(c(i[o], t, n));
  return this;
}
function Nu(e, t, n) {
  var i = xu(e), o = i.CustomEvent;
  typeof o == "function" ? o = new o(t, n) : (o = i.document.createEvent("Event"), n ? (o.initEvent(t, n.bubbles, n.cancelable), o.detail = n.detail) : o.initEvent(t, !1, !1)), e.dispatchEvent(o);
}
function by(e, t) {
  return function() {
    return Nu(this, e, t);
  };
}
function jy(e, t) {
  return function() {
    return Nu(this, e, t.apply(this, arguments));
  };
}
function Ny(e, t) {
  return this.each((typeof t == "function" ? jy : by)(e, t));
}
function* Sy() {
  for (var e = this._groups, t = 0, n = e.length; t < n; ++t)
    for (var i = e[t], o = 0, s = i.length, a; o < s; ++o)
      (a = i[o]) && (yield a);
}
var Su = [null];
function Be(e, t) {
  this._groups = e, this._parents = t;
}
function oi() {
  return new Be([[document.documentElement]], Su);
}
function Cy() {
  return this;
}
Be.prototype = oi.prototype = {
  constructor: Be,
  select: Jg,
  selectAll: tm,
  selectChild: om,
  selectChildren: lm,
  filter: um,
  data: mm,
  enter: dm,
  exit: xm,
  join: wm,
  merge: vm,
  selection: Cy,
  order: bm,
  sort: jm,
  call: Sm,
  nodes: Cm,
  node: Im,
  size: km,
  empty: Em,
  each: Am,
  attr: Mm,
  style: Om,
  property: Bm,
  classed: Um,
  text: Gm,
  html: ny,
  raise: ry,
  lower: sy,
  append: ay,
  insert: ly,
  remove: dy,
  clone: hy,
  datum: gy,
  on: vy,
  dispatch: Ny,
  [Symbol.iterator]: Sy
};
function He(e) {
  return typeof e == "string" ? new Be([[document.querySelector(e)]], [document.documentElement]) : new Be([[e]], Su);
}
function Iy(e) {
  let t;
  for (; t = e.sourceEvent; ) e = t;
  return e;
}
function Ze(e, t) {
  if (e = Iy(e), t === void 0 && (t = e.currentTarget), t) {
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
const ky = { passive: !1 }, Bn = { capture: !0, passive: !1 };
function bo(e) {
  e.stopImmediatePropagation();
}
function Gt(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function Cu(e) {
  var t = e.document.documentElement, n = He(e).on("dragstart.drag", Gt, Bn);
  "onselectstart" in t ? n.on("selectstart.drag", Gt, Bn) : (t.__noselect = t.style.MozUserSelect, t.style.MozUserSelect = "none");
}
function Iu(e, t) {
  var n = e.document.documentElement, i = He(e).on("dragstart.drag", null);
  t && (i.on("click.drag", Gt, Bn), setTimeout(function() {
    i.on("click.drag", null);
  }, 0)), "onselectstart" in n ? i.on("selectstart.drag", null) : (n.style.MozUserSelect = n.__noselect, delete n.__noselect);
}
const Ai = (e) => () => e;
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
function Ey(e) {
  return !e.ctrlKey && !e.button;
}
function Ay() {
  return this.parentNode;
}
function _y(e, t) {
  return t ?? { x: e.x, y: e.y };
}
function Dy() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function ku() {
  var e = Ey, t = Ay, n = _y, i = Dy, o = {}, s = br("start", "drag", "end"), a = 0, c, u, l, d, f = 0;
  function p(w) {
    w.on("mousedown.drag", h).filter(i).on("touchstart.drag", x).on("touchmove.drag", y, ky).on("touchend.drag touchcancel.drag", j).style("touch-action", "none").style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  function h(w, S) {
    if (!(d || !e.call(this, w, S))) {
      var b = g(this, t.call(this, w, S), w, S, "mouse");
      b && (He(w.view).on("mousemove.drag", m, Bn).on("mouseup.drag", v, Bn), Cu(w.view), bo(w), l = !1, c = w.clientX, u = w.clientY, b("start", w));
    }
  }
  function m(w) {
    if (Gt(w), !l) {
      var S = w.clientX - c, b = w.clientY - u;
      l = S * S + b * b > f;
    }
    o.mouse("drag", w);
  }
  function v(w) {
    He(w.view).on("mousemove.drag mouseup.drag", null), Iu(w.view, l), Gt(w), o.mouse("end", w);
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
      (A = o[S[I].identifier]) && (Gt(w), A("drag", w, S[I]));
  }
  function j(w) {
    var S = w.changedTouches, b = S.length, I, A;
    for (d && clearTimeout(d), d = setTimeout(function() {
      d = null;
    }, 500), I = 0; I < b; ++I)
      (A = o[S[I].identifier]) && (bo(w), A("end", w, S[I]));
  }
  function g(w, S, b, I, A, D) {
    var M = s.copy(), k = Ze(D || b, S), R, L, C;
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
            k = Ze(T || E, S), z = a;
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
    return arguments.length ? (e = typeof w == "function" ? w : Ai(!!w), p) : e;
  }, p.container = function(w) {
    return arguments.length ? (t = typeof w == "function" ? w : Ai(w), p) : t;
  }, p.subject = function(w) {
    return arguments.length ? (n = typeof w == "function" ? w : Ai(w), p) : n;
  }, p.touchable = function(w) {
    return arguments.length ? (i = typeof w == "function" ? w : Ai(!!w), p) : i;
  }, p.on = function() {
    var w = s.on.apply(s, arguments);
    return w === s ? p : w;
  }, p.clickDistance = function(w) {
    return arguments.length ? (f = (w = +w) * w, p) : Math.sqrt(f);
  }, p;
}
function Es(e, t, n) {
  e.prototype = t.prototype = n, n.constructor = e;
}
function Eu(e, t) {
  var n = Object.create(e.prototype);
  for (var i in t) n[i] = t[i];
  return n;
}
function si() {
}
var Kn = 0.7, ir = 1 / Kn, Qt = "\\s*([+-]?\\d+)\\s*", qn = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)\\s*", it = "\\s*([+-]?(?:\\d*\\.)?\\d+(?:[eE][+-]?\\d+)?)%\\s*", $y = /^#([0-9a-f]{3,8})$/, Ty = new RegExp(`^rgb\\(${Qt},${Qt},${Qt}\\)$`), Ry = new RegExp(`^rgb\\(${it},${it},${it}\\)$`), Py = new RegExp(`^rgba\\(${Qt},${Qt},${Qt},${qn}\\)$`), My = new RegExp(`^rgba\\(${it},${it},${it},${qn}\\)$`), Ly = new RegExp(`^hsl\\(${qn},${it},${it}\\)$`), zy = new RegExp(`^hsla\\(${qn},${it},${it},${qn}\\)$`), qa = {
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
Es(si, Vt, {
  copy(e) {
    return Object.assign(new this.constructor(), this, e);
  },
  displayable() {
    return this.rgb().displayable();
  },
  hex: Xa,
  // Deprecated! Use color.formatHex.
  formatHex: Xa,
  formatHex8: Vy,
  formatHsl: Oy,
  formatRgb: Ua,
  toString: Ua
});
function Xa() {
  return this.rgb().formatHex();
}
function Vy() {
  return this.rgb().formatHex8();
}
function Oy() {
  return Au(this).formatHsl();
}
function Ua() {
  return this.rgb().formatRgb();
}
function Vt(e) {
  var t, n;
  return e = (e + "").trim().toLowerCase(), (t = $y.exec(e)) ? (n = t[1].length, t = parseInt(t[1], 16), n === 6 ? Ya(t) : n === 3 ? new ze(t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, (t & 15) << 4 | t & 15, 1) : n === 8 ? _i(t >> 24 & 255, t >> 16 & 255, t >> 8 & 255, (t & 255) / 255) : n === 4 ? _i(t >> 12 & 15 | t >> 8 & 240, t >> 8 & 15 | t >> 4 & 240, t >> 4 & 15 | t & 240, ((t & 15) << 4 | t & 15) / 255) : null) : (t = Ty.exec(e)) ? new ze(t[1], t[2], t[3], 1) : (t = Ry.exec(e)) ? new ze(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, 1) : (t = Py.exec(e)) ? _i(t[1], t[2], t[3], t[4]) : (t = My.exec(e)) ? _i(t[1] * 255 / 100, t[2] * 255 / 100, t[3] * 255 / 100, t[4]) : (t = Ly.exec(e)) ? Ga(t[1], t[2] / 100, t[3] / 100, 1) : (t = zy.exec(e)) ? Ga(t[1], t[2] / 100, t[3] / 100, t[4]) : qa.hasOwnProperty(e) ? Ya(qa[e]) : e === "transparent" ? new ze(NaN, NaN, NaN, 0) : null;
}
function Ya(e) {
  return new ze(e >> 16 & 255, e >> 8 & 255, e & 255, 1);
}
function _i(e, t, n, i) {
  return i <= 0 && (e = t = n = NaN), new ze(e, t, n, i);
}
function Hy(e) {
  return e instanceof si || (e = Vt(e)), e ? (e = e.rgb(), new ze(e.r, e.g, e.b, e.opacity)) : new ze();
}
function Fo(e, t, n, i) {
  return arguments.length === 1 ? Hy(e) : new ze(e, t, n, i ?? 1);
}
function ze(e, t, n, i) {
  this.r = +e, this.g = +t, this.b = +n, this.opacity = +i;
}
Es(ze, Fo, Eu(si, {
  brighter(e) {
    return e = e == null ? ir : Math.pow(ir, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Kn : Math.pow(Kn, e), new ze(this.r * e, this.g * e, this.b * e, this.opacity);
  },
  rgb() {
    return this;
  },
  clamp() {
    return new ze($t(this.r), $t(this.g), $t(this.b), rr(this.opacity));
  },
  displayable() {
    return -0.5 <= this.r && this.r < 255.5 && -0.5 <= this.g && this.g < 255.5 && -0.5 <= this.b && this.b < 255.5 && 0 <= this.opacity && this.opacity <= 1;
  },
  hex: Za,
  // Deprecated! Use color.formatHex.
  formatHex: Za,
  formatHex8: Wy,
  formatRgb: Ja,
  toString: Ja
}));
function Za() {
  return `#${At(this.r)}${At(this.g)}${At(this.b)}`;
}
function Wy() {
  return `#${At(this.r)}${At(this.g)}${At(this.b)}${At((isNaN(this.opacity) ? 1 : this.opacity) * 255)}`;
}
function Ja() {
  const e = rr(this.opacity);
  return `${e === 1 ? "rgb(" : "rgba("}${$t(this.r)}, ${$t(this.g)}, ${$t(this.b)}${e === 1 ? ")" : `, ${e})`}`;
}
function rr(e) {
  return isNaN(e) ? 1 : Math.max(0, Math.min(1, e));
}
function $t(e) {
  return Math.max(0, Math.min(255, Math.round(e) || 0));
}
function At(e) {
  return e = $t(e), (e < 16 ? "0" : "") + e.toString(16);
}
function Ga(e, t, n, i) {
  return i <= 0 ? e = t = n = NaN : n <= 0 || n >= 1 ? e = t = NaN : t <= 0 && (e = NaN), new Je(e, t, n, i);
}
function Au(e) {
  if (e instanceof Je) return new Je(e.h, e.s, e.l, e.opacity);
  if (e instanceof si || (e = Vt(e)), !e) return new Je();
  if (e instanceof Je) return e;
  e = e.rgb();
  var t = e.r / 255, n = e.g / 255, i = e.b / 255, o = Math.min(t, n, i), s = Math.max(t, n, i), a = NaN, c = s - o, u = (s + o) / 2;
  return c ? (t === s ? a = (n - i) / c + (n < i) * 6 : n === s ? a = (i - t) / c + 2 : a = (t - n) / c + 4, c /= u < 0.5 ? s + o : 2 - s - o, a *= 60) : c = u > 0 && u < 1 ? 0 : a, new Je(a, c, u, e.opacity);
}
function Fy(e, t, n, i) {
  return arguments.length === 1 ? Au(e) : new Je(e, t, n, i ?? 1);
}
function Je(e, t, n, i) {
  this.h = +e, this.s = +t, this.l = +n, this.opacity = +i;
}
Es(Je, Fy, Eu(si, {
  brighter(e) {
    return e = e == null ? ir : Math.pow(ir, e), new Je(this.h, this.s, this.l * e, this.opacity);
  },
  darker(e) {
    return e = e == null ? Kn : Math.pow(Kn, e), new Je(this.h, this.s, this.l * e, this.opacity);
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
    return new Je(Qa(this.h), Di(this.s), Di(this.l), rr(this.opacity));
  },
  displayable() {
    return (0 <= this.s && this.s <= 1 || isNaN(this.s)) && 0 <= this.l && this.l <= 1 && 0 <= this.opacity && this.opacity <= 1;
  },
  formatHsl() {
    const e = rr(this.opacity);
    return `${e === 1 ? "hsl(" : "hsla("}${Qa(this.h)}, ${Di(this.s) * 100}%, ${Di(this.l) * 100}%${e === 1 ? ")" : `, ${e})`}`;
  }
}));
function Qa(e) {
  return e = (e || 0) % 360, e < 0 ? e + 360 : e;
}
function Di(e) {
  return Math.max(0, Math.min(1, e || 0));
}
function jo(e, t, n) {
  return (e < 60 ? t + (n - t) * e / 60 : e < 180 ? n : e < 240 ? t + (n - t) * (240 - e) / 60 : t) * 255;
}
const As = (e) => () => e;
function By(e, t) {
  return function(n) {
    return e + n * t;
  };
}
function Ky(e, t, n) {
  return e = Math.pow(e, n), t = Math.pow(t, n) - e, n = 1 / n, function(i) {
    return Math.pow(e + i * t, n);
  };
}
function qy(e) {
  return (e = +e) == 1 ? _u : function(t, n) {
    return n - t ? Ky(t, n, e) : As(isNaN(t) ? n : t);
  };
}
function _u(e, t) {
  var n = t - e;
  return n ? By(e, n) : As(isNaN(e) ? t : e);
}
const or = (function e(t) {
  var n = qy(t);
  function i(o, s) {
    var a = n((o = Fo(o)).r, (s = Fo(s)).r), c = n(o.g, s.g), u = n(o.b, s.b), l = _u(o.opacity, s.opacity);
    return function(d) {
      return o.r = a(d), o.g = c(d), o.b = u(d), o.opacity = l(d), o + "";
    };
  }
  return i.gamma = e, i;
})(1);
function Xy(e, t) {
  t || (t = []);
  var n = e ? Math.min(t.length, e.length) : 0, i = t.slice(), o;
  return function(s) {
    for (o = 0; o < n; ++o) i[o] = e[o] * (1 - s) + t[o] * s;
    return i;
  };
}
function Uy(e) {
  return ArrayBuffer.isView(e) && !(e instanceof DataView);
}
function Yy(e, t) {
  var n = t ? t.length : 0, i = e ? Math.min(n, e.length) : 0, o = new Array(i), s = new Array(n), a;
  for (a = 0; a < i; ++a) o[a] = Mn(e[a], t[a]);
  for (; a < n; ++a) s[a] = t[a];
  return function(c) {
    for (a = 0; a < i; ++a) s[a] = o[a](c);
    return s;
  };
}
function Zy(e, t) {
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
function Jy(e, t) {
  var n = {}, i = {}, o;
  (e === null || typeof e != "object") && (e = {}), (t === null || typeof t != "object") && (t = {});
  for (o in t)
    o in e ? n[o] = Mn(e[o], t[o]) : i[o] = t[o];
  return function(s) {
    for (o in n) i[o] = n[o](s);
    return i;
  };
}
var Bo = /[-+]?(?:\d+\.?\d*|\.?\d+)(?:[eE][-+]?\d+)?/g, No = new RegExp(Bo.source, "g");
function Gy(e) {
  return function() {
    return e;
  };
}
function Qy(e) {
  return function(t) {
    return e(t) + "";
  };
}
function Du(e, t) {
  var n = Bo.lastIndex = No.lastIndex = 0, i, o, s, a = -1, c = [], u = [];
  for (e = e + "", t = t + ""; (i = Bo.exec(e)) && (o = No.exec(t)); )
    (s = o.index) > n && (s = t.slice(n, s), c[a] ? c[a] += s : c[++a] = s), (i = i[0]) === (o = o[0]) ? c[a] ? c[a] += o : c[++a] = o : (c[++a] = null, u.push({ i: a, x: nt(i, o) })), n = No.lastIndex;
  return n < t.length && (s = t.slice(n), c[a] ? c[a] += s : c[++a] = s), c.length < 2 ? u[0] ? Qy(u[0].x) : Gy(t) : (t = u.length, function(l) {
    for (var d = 0, f; d < t; ++d) c[(f = u[d]).i] = f.x(l);
    return c.join("");
  });
}
function Mn(e, t) {
  var n = typeof t, i;
  return t == null || n === "boolean" ? As(t) : (n === "number" ? nt : n === "string" ? (i = Vt(t)) ? (t = i, or) : Du : t instanceof Vt ? or : t instanceof Date ? Zy : Uy(t) ? Xy : Array.isArray(t) ? Yy : typeof t.valueOf != "function" && typeof t.toString != "function" || isNaN(t) ? Jy : nt)(e, t);
}
var ec = 180 / Math.PI, Ko = {
  translateX: 0,
  translateY: 0,
  rotate: 0,
  skewX: 0,
  scaleX: 1,
  scaleY: 1
};
function $u(e, t, n, i, o, s) {
  var a, c, u;
  return (a = Math.sqrt(e * e + t * t)) && (e /= a, t /= a), (u = e * n + t * i) && (n -= e * u, i -= t * u), (c = Math.sqrt(n * n + i * i)) && (n /= c, i /= c, u /= c), e * i < t * n && (e = -e, t = -t, u = -u, a = -a), {
    translateX: o,
    translateY: s,
    rotate: Math.atan2(t, e) * ec,
    skewX: Math.atan(u) * ec,
    scaleX: a,
    scaleY: c
  };
}
var $i;
function ex(e) {
  const t = new (typeof DOMMatrix == "function" ? DOMMatrix : WebKitCSSMatrix)(e + "");
  return t.isIdentity ? Ko : $u(t.a, t.b, t.c, t.d, t.e, t.f);
}
function tx(e) {
  return e == null || ($i || ($i = document.createElementNS("http://www.w3.org/2000/svg", "g")), $i.setAttribute("transform", e), !(e = $i.transform.baseVal.consolidate())) ? Ko : (e = e.matrix, $u(e.a, e.b, e.c, e.d, e.e, e.f));
}
function Tu(e, t, n, i) {
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
var nx = Tu(ex, "px, ", "px)", "deg)"), ix = Tu(tx, ", ", ")", ")"), rx = 1e-12;
function tc(e) {
  return ((e = Math.exp(e)) + 1 / e) / 2;
}
function ox(e) {
  return ((e = Math.exp(e)) - 1 / e) / 2;
}
function sx(e) {
  return ((e = Math.exp(2 * e)) - 1) / (e + 1);
}
const Bi = (function e(t, n, i) {
  function o(s, a) {
    var c = s[0], u = s[1], l = s[2], d = a[0], f = a[1], p = a[2], h = d - c, m = f - u, v = h * h + m * m, x, y;
    if (v < rx)
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
        var A = I * y, D = tc(S), M = l / (n * j) * (D * sx(t * A + S) - ox(S));
        return [
          c + M * h,
          u + M * m,
          l * D / tc(t * A + S)
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
var on = 0, An = 0, Nn = 0, Ru = 1e3, sr, _n, ar = 0, Ot = 0, Nr = 0, Xn = typeof performance == "object" && performance.now ? performance : Date, Pu = typeof window == "object" && window.requestAnimationFrame ? window.requestAnimationFrame.bind(window) : function(e) {
  setTimeout(e, 17);
};
function _s() {
  return Ot || (Pu(ax), Ot = Xn.now() + Nr);
}
function ax() {
  Ot = 0;
}
function cr() {
  this._call = this._time = this._next = null;
}
cr.prototype = Mu.prototype = {
  constructor: cr,
  restart: function(e, t, n) {
    if (typeof e != "function") throw new TypeError("callback is not a function");
    n = (n == null ? _s() : +n) + (t == null ? 0 : +t), !this._next && _n !== this && (_n ? _n._next = this : sr = this, _n = this), this._call = e, this._time = n, qo();
  },
  stop: function() {
    this._call && (this._call = null, this._time = 1 / 0, qo());
  }
};
function Mu(e, t, n) {
  var i = new cr();
  return i.restart(e, t, n), i;
}
function cx() {
  _s(), ++on;
  for (var e = sr, t; e; )
    (t = Ot - e._time) >= 0 && e._call.call(void 0, t), e = e._next;
  --on;
}
function nc() {
  Ot = (ar = Xn.now()) + Nr, on = An = 0;
  try {
    cx();
  } finally {
    on = 0, ux(), Ot = 0;
  }
}
function lx() {
  var e = Xn.now(), t = e - ar;
  t > Ru && (Nr -= t, ar = e);
}
function ux() {
  for (var e, t = sr, n, i = 1 / 0; t; )
    t._call ? (i > t._time && (i = t._time), e = t, t = t._next) : (n = t._next, t._next = null, t = e ? e._next = n : sr = n);
  _n = e, qo(i);
}
function qo(e) {
  if (!on) {
    An && (An = clearTimeout(An));
    var t = e - Ot;
    t > 24 ? (e < 1 / 0 && (An = setTimeout(nc, e - Xn.now() - Nr)), Nn && (Nn = clearInterval(Nn))) : (Nn || (ar = Xn.now(), Nn = setInterval(lx, Ru)), on = 1, Pu(nc));
  }
}
function ic(e, t, n) {
  var i = new cr();
  return t = t == null ? 0 : +t, i.restart((o) => {
    i.stop(), e(o + t);
  }, t, n), i;
}
var dx = br("start", "end", "cancel", "interrupt"), fx = [], Lu = 0, rc = 1, Xo = 2, Ki = 3, oc = 4, Uo = 5, qi = 6;
function Sr(e, t, n, i, o, s) {
  var a = e.__transition;
  if (!a) e.__transition = {};
  else if (n in a) return;
  px(e, n, {
    name: t,
    index: i,
    // For context during callback.
    group: o,
    // For context during callback.
    on: dx,
    tween: fx,
    time: s.time,
    delay: s.delay,
    duration: s.duration,
    ease: s.ease,
    timer: null,
    state: Lu
  });
}
function Ds(e, t) {
  var n = et(e, t);
  if (n.state > Lu) throw new Error("too late; already scheduled");
  return n;
}
function at(e, t) {
  var n = et(e, t);
  if (n.state > Ki) throw new Error("too late; already running");
  return n;
}
function et(e, t) {
  var n = e.__transition;
  if (!n || !(n = n[t])) throw new Error("transition not found");
  return n;
}
function px(e, t, n) {
  var i = e.__transition, o;
  i[t] = n, n.timer = Mu(s, 0, n.time);
  function s(l) {
    n.state = rc, n.timer.restart(a, n.delay, n.time), n.delay <= l && a(l - n.delay);
  }
  function a(l) {
    var d, f, p, h;
    if (n.state !== rc) return u();
    for (d in i)
      if (h = i[d], h.name === n.name) {
        if (h.state === Ki) return ic(a);
        h.state === oc ? (h.state = qi, h.timer.stop(), h.on.call("interrupt", e, e.__data__, h.index, h.group), delete i[d]) : +d < t && (h.state = qi, h.timer.stop(), h.on.call("cancel", e, e.__data__, h.index, h.group), delete i[d]);
      }
    if (ic(function() {
      n.state === Ki && (n.state = oc, n.timer.restart(c, n.delay, n.time), c(l));
    }), n.state = Xo, n.on.call("start", e, e.__data__, n.index, n.group), n.state === Xo) {
      for (n.state = Ki, o = new Array(p = n.tween.length), d = 0, f = -1; d < p; ++d)
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
    n.state = qi, n.timer.stop(), delete i[t];
    for (var l in i) return;
    delete e.__transition;
  }
}
function Xi(e, t) {
  var n = e.__transition, i, o, s = !0, a;
  if (n) {
    t = t == null ? null : t + "";
    for (a in n) {
      if ((i = n[a]).name !== t) {
        s = !1;
        continue;
      }
      o = i.state > Xo && i.state < Uo, i.state = qi, i.timer.stop(), i.on.call(o ? "interrupt" : "cancel", e, e.__data__, i.index, i.group), delete n[a];
    }
    s && delete e.__transition;
  }
}
function hx(e) {
  return this.each(function() {
    Xi(this, e);
  });
}
function gx(e, t) {
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
function mx(e, t, n) {
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
function yx(e, t) {
  var n = this._id;
  if (e += "", arguments.length < 2) {
    for (var i = et(this.node(), n).tween, o = 0, s = i.length, a; o < s; ++o)
      if ((a = i[o]).name === e)
        return a.value;
    return null;
  }
  return this.each((t == null ? gx : mx)(n, e, t));
}
function $s(e, t, n) {
  var i = e._id;
  return e.each(function() {
    var o = at(this, i);
    (o.value || (o.value = {}))[t] = n.apply(this, arguments);
  }), function(o) {
    return et(o, i).value[t];
  };
}
function zu(e, t) {
  var n;
  return (typeof t == "number" ? nt : t instanceof Vt ? or : (n = Vt(t)) ? (t = n, or) : Du)(e, t);
}
function xx(e) {
  return function() {
    this.removeAttribute(e);
  };
}
function wx(e) {
  return function() {
    this.removeAttributeNS(e.space, e.local);
  };
}
function vx(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttribute(e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function bx(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = this.getAttributeNS(e.space, e.local);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function jx(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttribute(e) : (a = this.getAttribute(e), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Nx(e, t, n) {
  var i, o, s;
  return function() {
    var a, c = n(this), u;
    return c == null ? void this.removeAttributeNS(e.space, e.local) : (a = this.getAttributeNS(e.space, e.local), u = c + "", a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c)));
  };
}
function Sx(e, t) {
  var n = jr(e), i = n === "transform" ? ix : zu;
  return this.attrTween(e, typeof t == "function" ? (n.local ? Nx : jx)(n, i, $s(this, "attr." + e, t)) : t == null ? (n.local ? wx : xx)(n) : (n.local ? bx : vx)(n, i, t));
}
function Cx(e, t) {
  return function(n) {
    this.setAttribute(e, t.call(this, n));
  };
}
function Ix(e, t) {
  return function(n) {
    this.setAttributeNS(e.space, e.local, t.call(this, n));
  };
}
function kx(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Ix(e, s)), n;
  }
  return o._value = t, o;
}
function Ex(e, t) {
  var n, i;
  function o() {
    var s = t.apply(this, arguments);
    return s !== i && (n = (i = s) && Cx(e, s)), n;
  }
  return o._value = t, o;
}
function Ax(e, t) {
  var n = "attr." + e;
  if (arguments.length < 2) return (n = this.tween(n)) && n._value;
  if (t == null) return this.tween(n, null);
  if (typeof t != "function") throw new Error();
  var i = jr(e);
  return this.tween(n, (i.local ? kx : Ex)(i, t));
}
function _x(e, t) {
  return function() {
    Ds(this, e).delay = +t.apply(this, arguments);
  };
}
function Dx(e, t) {
  return t = +t, function() {
    Ds(this, e).delay = t;
  };
}
function $x(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? _x : Dx)(t, e)) : et(this.node(), t).delay;
}
function Tx(e, t) {
  return function() {
    at(this, e).duration = +t.apply(this, arguments);
  };
}
function Rx(e, t) {
  return t = +t, function() {
    at(this, e).duration = t;
  };
}
function Px(e) {
  var t = this._id;
  return arguments.length ? this.each((typeof e == "function" ? Tx : Rx)(t, e)) : et(this.node(), t).duration;
}
function Mx(e, t) {
  if (typeof t != "function") throw new Error();
  return function() {
    at(this, e).ease = t;
  };
}
function Lx(e) {
  var t = this._id;
  return arguments.length ? this.each(Mx(t, e)) : et(this.node(), t).ease;
}
function zx(e, t) {
  return function() {
    var n = t.apply(this, arguments);
    if (typeof n != "function") throw new Error();
    at(this, e).ease = n;
  };
}
function Vx(e) {
  if (typeof e != "function") throw new Error();
  return this.each(zx(this._id, e));
}
function Ox(e) {
  typeof e != "function" && (e = gu(e));
  for (var t = this._groups, n = t.length, i = new Array(n), o = 0; o < n; ++o)
    for (var s = t[o], a = s.length, c = i[o] = [], u, l = 0; l < a; ++l)
      (u = s[l]) && e.call(u, u.__data__, l, s) && c.push(u);
  return new gt(i, this._parents, this._name, this._id);
}
function Hx(e) {
  if (e._id !== this._id) throw new Error();
  for (var t = this._groups, n = e._groups, i = t.length, o = n.length, s = Math.min(i, o), a = new Array(i), c = 0; c < s; ++c)
    for (var u = t[c], l = n[c], d = u.length, f = a[c] = new Array(d), p, h = 0; h < d; ++h)
      (p = u[h] || l[h]) && (f[h] = p);
  for (; c < i; ++c)
    a[c] = t[c];
  return new gt(a, this._parents, this._name, this._id);
}
function Wx(e) {
  return (e + "").trim().split(/^|\s+/).every(function(t) {
    var n = t.indexOf(".");
    return n >= 0 && (t = t.slice(0, n)), !t || t === "start";
  });
}
function Fx(e, t, n) {
  var i, o, s = Wx(t) ? Ds : at;
  return function() {
    var a = s(this, e), c = a.on;
    c !== i && (o = (i = c).copy()).on(t, n), a.on = o;
  };
}
function Bx(e, t) {
  var n = this._id;
  return arguments.length < 2 ? et(this.node(), n).on.on(e) : this.each(Fx(n, e, t));
}
function Kx(e) {
  return function() {
    var t = this.parentNode;
    for (var n in this.__transition) if (+n !== e) return;
    t && t.removeChild(this);
  };
}
function qx() {
  return this.on("end.remove", Kx(this._id));
}
function Xx(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = Is(e));
  for (var i = this._groups, o = i.length, s = new Array(o), a = 0; a < o; ++a)
    for (var c = i[a], u = c.length, l = s[a] = new Array(u), d, f, p = 0; p < u; ++p)
      (d = c[p]) && (f = e.call(d, d.__data__, p, c)) && ("__data__" in d && (f.__data__ = d.__data__), l[p] = f, Sr(l[p], t, n, p, l, et(d, n)));
  return new gt(s, this._parents, t, n);
}
function Ux(e) {
  var t = this._name, n = this._id;
  typeof e != "function" && (e = hu(e));
  for (var i = this._groups, o = i.length, s = [], a = [], c = 0; c < o; ++c)
    for (var u = i[c], l = u.length, d, f = 0; f < l; ++f)
      if (d = u[f]) {
        for (var p = e.call(d, d.__data__, f, u), h, m = et(d, n), v = 0, x = p.length; v < x; ++v)
          (h = p[v]) && Sr(h, t, n, v, p, m);
        s.push(p), a.push(d);
      }
  return new gt(s, a, t, n);
}
var Yx = oi.prototype.constructor;
function Zx() {
  return new Yx(this._groups, this._parents);
}
function Jx(e, t) {
  var n, i, o;
  return function() {
    var s = rn(this, e), a = (this.style.removeProperty(e), rn(this, e));
    return s === a ? null : s === n && a === i ? o : o = t(n = s, i = a);
  };
}
function Vu(e) {
  return function() {
    this.style.removeProperty(e);
  };
}
function Gx(e, t, n) {
  var i, o = n + "", s;
  return function() {
    var a = rn(this, e);
    return a === o ? null : a === i ? s : s = t(i = a, n);
  };
}
function Qx(e, t, n) {
  var i, o, s;
  return function() {
    var a = rn(this, e), c = n(this), u = c + "";
    return c == null && (u = c = (this.style.removeProperty(e), rn(this, e))), a === u ? null : a === i && u === o ? s : (o = u, s = t(i = a, c));
  };
}
function ew(e, t) {
  var n, i, o, s = "style." + t, a = "end." + s, c;
  return function() {
    var u = at(this, e), l = u.on, d = u.value[s] == null ? c || (c = Vu(t)) : void 0;
    (l !== n || o !== d) && (i = (n = l).copy()).on(a, o = d), u.on = i;
  };
}
function tw(e, t, n) {
  var i = (e += "") == "transform" ? nx : zu;
  return t == null ? this.styleTween(e, Jx(e, i)).on("end.style." + e, Vu(e)) : typeof t == "function" ? this.styleTween(e, Qx(e, i, $s(this, "style." + e, t))).each(ew(this._id, e)) : this.styleTween(e, Gx(e, i, t), n).on("end.style." + e, null);
}
function nw(e, t, n) {
  return function(i) {
    this.style.setProperty(e, t.call(this, i), n);
  };
}
function iw(e, t, n) {
  var i, o;
  function s() {
    var a = t.apply(this, arguments);
    return a !== o && (i = (o = a) && nw(e, a, n)), i;
  }
  return s._value = t, s;
}
function rw(e, t, n) {
  var i = "style." + (e += "");
  if (arguments.length < 2) return (i = this.tween(i)) && i._value;
  if (t == null) return this.tween(i, null);
  if (typeof t != "function") throw new Error();
  return this.tween(i, iw(e, t, n ?? ""));
}
function ow(e) {
  return function() {
    this.textContent = e;
  };
}
function sw(e) {
  return function() {
    var t = e(this);
    this.textContent = t ?? "";
  };
}
function aw(e) {
  return this.tween("text", typeof e == "function" ? sw($s(this, "text", e)) : ow(e == null ? "" : e + ""));
}
function cw(e) {
  return function(t) {
    this.textContent = e.call(this, t);
  };
}
function lw(e) {
  var t, n;
  function i() {
    var o = e.apply(this, arguments);
    return o !== n && (t = (n = o) && cw(o)), t;
  }
  return i._value = e, i;
}
function uw(e) {
  var t = "text";
  if (arguments.length < 1) return (t = this.tween(t)) && t._value;
  if (e == null) return this.tween(t, null);
  if (typeof e != "function") throw new Error();
  return this.tween(t, lw(e));
}
function dw() {
  for (var e = this._name, t = this._id, n = Ou(), i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      if (u = a[l]) {
        var d = et(u, t);
        Sr(u, e, n, l, a, {
          time: d.time + d.delay + d.duration,
          delay: 0,
          duration: d.duration,
          ease: d.ease
        });
      }
  return new gt(i, this._parents, e, n);
}
function fw() {
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
var pw = 0;
function gt(e, t, n, i) {
  this._groups = e, this._parents = t, this._name = n, this._id = i;
}
function Ou() {
  return ++pw;
}
var ut = oi.prototype;
gt.prototype = {
  constructor: gt,
  select: Xx,
  selectAll: Ux,
  selectChild: ut.selectChild,
  selectChildren: ut.selectChildren,
  filter: Ox,
  merge: Hx,
  selection: Zx,
  transition: dw,
  call: ut.call,
  nodes: ut.nodes,
  node: ut.node,
  size: ut.size,
  empty: ut.empty,
  each: ut.each,
  on: Bx,
  attr: Sx,
  attrTween: Ax,
  style: tw,
  styleTween: rw,
  text: aw,
  textTween: uw,
  remove: qx,
  tween: yx,
  delay: $x,
  duration: Px,
  ease: Lx,
  easeVarying: Vx,
  end: fw,
  [Symbol.iterator]: ut[Symbol.iterator]
};
function hw(e) {
  return ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2;
}
var gw = {
  time: null,
  // Set on use.
  delay: 0,
  duration: 250,
  ease: hw
};
function mw(e, t) {
  for (var n; !(n = e.__transition) || !(n = n[t]); )
    if (!(e = e.parentNode))
      throw new Error(`transition ${t} not found`);
  return n;
}
function yw(e) {
  var t, n;
  e instanceof gt ? (t = e._id, e = e._name) : (t = Ou(), (n = gw).time = _s(), e = e == null ? null : e + "");
  for (var i = this._groups, o = i.length, s = 0; s < o; ++s)
    for (var a = i[s], c = a.length, u, l = 0; l < c; ++l)
      (u = a[l]) && Sr(u, e, t, l, a, n || mw(u, t));
  return new gt(i, this._parents, e, t);
}
oi.prototype.interrupt = hx;
oi.prototype.transition = yw;
const Ti = (e) => () => e;
function xw(e, {
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
var Cr = new ft(1, 0, 0);
Hu.prototype = ft.prototype;
function Hu(e) {
  for (; !e.__zoom; ) if (!(e = e.parentNode)) return Cr;
  return e.__zoom;
}
function So(e) {
  e.stopImmediatePropagation();
}
function Sn(e) {
  e.preventDefault(), e.stopImmediatePropagation();
}
function ww(e) {
  return (!e.ctrlKey || e.type === "wheel") && !e.button;
}
function vw() {
  var e = this;
  return e instanceof SVGElement ? (e = e.ownerSVGElement || e, e.hasAttribute("viewBox") ? (e = e.viewBox.baseVal, [[e.x, e.y], [e.x + e.width, e.y + e.height]]) : [[0, 0], [e.width.baseVal.value, e.height.baseVal.value]]) : [[0, 0], [e.clientWidth, e.clientHeight]];
}
function sc() {
  return this.__zoom || Cr;
}
function bw(e) {
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * (e.ctrlKey ? 10 : 1);
}
function jw() {
  return navigator.maxTouchPoints || "ontouchstart" in this;
}
function Nw(e, t, n) {
  var i = e.invertX(t[0][0]) - n[0][0], o = e.invertX(t[1][0]) - n[1][0], s = e.invertY(t[0][1]) - n[0][1], a = e.invertY(t[1][1]) - n[1][1];
  return e.translate(
    o > i ? (i + o) / 2 : Math.min(0, i) || Math.max(0, o),
    a > s ? (s + a) / 2 : Math.min(0, s) || Math.max(0, a)
  );
}
function Wu() {
  var e = ww, t = vw, n = Nw, i = bw, o = jw, s = [0, 1 / 0], a = [[-1 / 0, -1 / 0], [1 / 0, 1 / 0]], c = 250, u = Bi, l = br("start", "zoom", "end"), d, f, p, h = 500, m = 150, v = 0, x = 10;
  function y(C) {
    C.property("__zoom", sc).on("wheel.zoom", A, { passive: !1 }).on("mousedown.zoom", D).on("dblclick.zoom", M).filter(o).on("touchstart.zoom", k).on("touchmove.zoom", R).on("touchend.zoom touchcancel.zoom", L).style("-webkit-tap-highlight-color", "rgba(0,0,0,0)");
  }
  y.transform = function(C, _, N, E) {
    var T = C.selection ? C.selection() : C;
    T.property("__zoom", sc), C !== T ? S(C, _, N, E) : T.interrupt().each(function() {
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
      return n(Cr.translate(F[0], F[1]).scale(z.k).translate(
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
      var _ = He(this.that).datum();
      l.call(
        C,
        this.that,
        new xw(C, {
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
    var N = b(this, _).event(C), E = this.__zoom, T = Math.max(s[0], Math.min(s[1], E.k * Math.pow(2, i.apply(this, arguments)))), $ = Ze(C);
    if (N.wheel)
      (N.mouse[0][0] !== $[0] || N.mouse[0][1] !== $[1]) && (N.mouse[1] = E.invert(N.mouse[0] = $)), clearTimeout(N.wheel);
    else {
      if (E.k === T) return;
      N.mouse = [$, E.invert($)], Xi(this), N.start();
    }
    Sn(C), N.wheel = setTimeout(z, m), N.zoom("mouse", n(g(j(E, T), N.mouse[0], N.mouse[1]), N.extent, a));
    function z() {
      N.wheel = null, N.end();
    }
  }
  function D(C, ..._) {
    if (p || !e.apply(this, arguments)) return;
    var N = C.currentTarget, E = b(this, _, !0).event(C), T = He(C.view).on("mousemove.zoom", B, !0).on("mouseup.zoom", Z, !0), $ = Ze(C, N), z = C.clientX, F = C.clientY;
    Cu(C.view), So(C), E.mouse = [$, this.__zoom.invert($)], Xi(this), E.start();
    function B(H) {
      if (Sn(H), !E.moved) {
        var U = H.clientX - z, G = H.clientY - F;
        E.moved = U * U + G * G > v;
      }
      E.event(H).zoom("mouse", n(g(E.that.__zoom, E.mouse[0] = Ze(H, N), E.mouse[1]), E.extent, a));
    }
    function Z(H) {
      T.on("mousemove.zoom mouseup.zoom", null), Iu(H.view, E.moved), Sn(H), E.event(H).end();
    }
  }
  function M(C, ..._) {
    if (e.apply(this, arguments)) {
      var N = this.__zoom, E = Ze(C.changedTouches ? C.changedTouches[0] : C, this), T = N.invert(E), $ = N.k * (C.shiftKey ? 0.5 : 2), z = n(g(j(N, $), E, T), t.apply(this, _), a);
      Sn(C), c > 0 ? He(this).transition().duration(c).call(S, z, E, C) : He(this).call(y.transform, z, E, C);
    }
  }
  function k(C, ..._) {
    if (e.apply(this, arguments)) {
      var N = C.touches, E = N.length, T = b(this, _, C.changedTouches.length === E).event(C), $, z, F, B;
      for (So(C), z = 0; z < E; ++z)
        F = N[z], B = Ze(F, this), B = [B, this.__zoom.invert(B), F.identifier], T.touch0 ? !T.touch1 && T.touch0[2] !== B[2] && (T.touch1 = B, T.taps = 0) : (T.touch0 = B, $ = !0, T.taps = 1 + !!d);
      d && (d = clearTimeout(d)), $ && (T.taps < 2 && (f = B[0], d = setTimeout(function() {
        d = null;
      }, h)), Xi(this), T.start());
    }
  }
  function R(C, ..._) {
    if (this.__zooming) {
      var N = b(this, _).event(C), E = C.changedTouches, T = E.length, $, z, F, B;
      for (Sn(C), $ = 0; $ < T; ++$)
        z = E[$], F = Ze(z, this), N.touch0 && N.touch0[2] === z.identifier ? N.touch0[0] = F : N.touch1 && N.touch1[2] === z.identifier && (N.touch1[0] = F);
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
      else if (N.end(), N.taps === 2 && (z = Ze(z, this), Math.hypot(f[0] - z[0], f[1] - z[1]) < x)) {
        var F = He(this).on("dblclick.zoom");
        F && F.apply(this, arguments);
      }
    }
  }
  return y.wheelDelta = function(C) {
    return arguments.length ? (i = typeof C == "function" ? C : Ti(+C), y) : i;
  }, y.filter = function(C) {
    return arguments.length ? (e = typeof C == "function" ? C : Ti(!!C), y) : e;
  }, y.touchable = function(C) {
    return arguments.length ? (o = typeof C == "function" ? C : Ti(!!C), y) : o;
  }, y.extent = function(C) {
    return arguments.length ? (t = typeof C == "function" ? C : Ti([[+C[0][0], +C[0][1]], [+C[1][0], +C[1][1]]]), y) : t;
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
}, Un = [
  [Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY],
  [Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY]
], Fu = ["Enter", " ", "Escape"], Bu = {
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
var Yn;
(function(e) {
  e.Partial = "partial", e.Full = "full";
})(Yn || (Yn = {}));
const Ku = {
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
var lr;
(function(e) {
  e.Arrow = "arrow", e.ArrowClosed = "arrowclosed";
})(lr || (lr = {}));
var ce;
(function(e) {
  e.Left = "left", e.Top = "top", e.Right = "right", e.Bottom = "bottom";
})(ce || (ce = {}));
const ac = {
  [ce.Left]: ce.Right,
  [ce.Right]: ce.Left,
  [ce.Top]: ce.Bottom,
  [ce.Bottom]: ce.Top
};
function qu(e) {
  return e === null ? null : e ? "valid" : "invalid";
}
const Xu = (e) => "id" in e && "source" in e && "target" in e, Sw = (e) => "id" in e && "position" in e && !("source" in e) && !("target" in e), Ts = (e) => "id" in e && "internals" in e && !("source" in e) && !("target" in e), ai = (e, t = [0, 0]) => {
  const { width: n, height: i } = mt(e), o = e.origin ?? t, s = n * o[0], a = i * o[1];
  return {
    x: e.position.x - s,
    y: e.position.y - a
  };
}, Cw = (e, t = { nodeOrigin: [0, 0] }) => {
  if (e.length === 0)
    return { x: 0, y: 0, width: 0, height: 0 };
  const n = e.reduce((i, o) => {
    const s = typeof o == "string";
    let a = !t.nodeLookup && !s ? o : void 0;
    t.nodeLookup && (a = s ? t.nodeLookup.get(o) : Ts(o) ? o : t.nodeLookup.get(o.id));
    const c = a ? ur(a, t.nodeOrigin) : { x: 0, y: 0, x2: 0, y2: 0 };
    return Ir(i, c);
  }, { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 });
  return kr(n);
}, ci = (e, t = {}) => {
  let n = { x: 1 / 0, y: 1 / 0, x2: -1 / 0, y2: -1 / 0 }, i = !1;
  return e.forEach((o) => {
    (t.filter === void 0 || t.filter(o)) && (n = Ir(n, ur(o)), i = !0);
  }), i ? kr(n) : { x: 0, y: 0, width: 0, height: 0 };
}, Rs = (e, t, [n, i, o] = [0, 0, 1], s = !1, a = !1) => {
  const c = {
    ...mn(t, [n, i, o]),
    width: t.width / o,
    height: t.height / o
  }, u = [];
  for (const l of e.values()) {
    const { measured: d, selectable: f = !0, hidden: p = !1 } = l;
    if (a && !f || p)
      continue;
    const h = d.width ?? l.width ?? l.initialWidth ?? null, m = d.height ?? l.height ?? l.initialHeight ?? null, v = Zn(c, cn(l)), x = (h ?? 0) * (m ?? 0), y = s && v > 0;
    (!l.internals.handleBounds || y || v >= x || l.dragging) && u.push(l);
  }
  return u;
}, Iw = (e, t) => {
  const n = /* @__PURE__ */ new Set();
  return e.forEach((i) => {
    n.add(i.id);
  }), t.filter((i) => n.has(i.source) || n.has(i.target));
};
function kw(e, t) {
  const n = /* @__PURE__ */ new Map(), i = t?.nodes ? new Set(t.nodes.map((o) => o.id)) : null;
  return e.forEach((o) => {
    o.measured.width && o.measured.height && (t?.includeHiddenNodes || !o.hidden) && (!i || i.has(o.id)) && n.set(o.id, o);
  }), n;
}
async function Ew({ nodes: e, width: t, height: n, panZoom: i, minZoom: o, maxZoom: s }, a) {
  if (e.size === 0)
    return !0;
  const c = kw(e, a), u = ci(c), l = Ms(u, t, n, a?.minZoom ?? o, a?.maxZoom ?? s, a?.padding ?? 0.1);
  return await i.setViewport(l, {
    duration: a?.duration,
    ease: a?.ease,
    interpolate: a?.interpolate
  }), !0;
}
function Uu({ nodeId: e, nextPosition: t, nodeLookup: n, nodeOrigin: i = [0, 0], nodeExtent: o, onError: s }) {
  const a = n.get(e), c = a.parentId ? n.get(a.parentId) : void 0, { x: u, y: l } = c ? c.internals.positionAbsolute : { x: 0, y: 0 }, d = a.origin ?? i;
  let f = a.extent || o;
  if (a.extent === "parent" && !a.expandParent)
    if (!c)
      s?.("005", Ke.error005());
    else {
      const h = c.measured.width, m = c.measured.height;
      h && m && (f = [
        [u, l],
        [u + h, l + m]
      ]);
    }
  else c && Wt(a.extent) && (f = [
    [a.extent[0][0] + u, a.extent[0][1] + l],
    [a.extent[1][0] + u, a.extent[1][1] + l]
  ]);
  const p = Wt(f) ? Ht(t, f, a.measured) : t;
  return (a.measured.width === void 0 || a.measured.height === void 0) && s?.("015", Ke.error015()), {
    position: {
      x: p.x - u + (a.measured.width ?? 0) * d[0],
      y: p.y - l + (a.measured.height ?? 0) * d[1]
    },
    positionAbsolute: p
  };
}
async function Aw({ nodesToRemove: e = [], edgesToRemove: t = [], nodes: n, edges: i, onBeforeDelete: o }) {
  const s = new Set(e.map((p) => p.id)), a = [];
  for (const p of n) {
    if (p.deletable === !1)
      continue;
    const h = s.has(p.id), m = !h && p.parentId && a.find((v) => v.id === p.parentId);
    (h || m) && a.push(p);
  }
  const c = new Set(t.map((p) => p.id)), u = i.filter((p) => p.deletable !== !1), d = Iw(a, u);
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
const an = (e, t = 0, n = 1) => Math.min(Math.max(e, t), n), Ht = (e = { x: 0, y: 0 }, t, n) => ({
  x: an(e.x, t[0][0], t[1][0] - (n?.width ?? 0)),
  y: an(e.y, t[0][1], t[1][1] - (n?.height ?? 0))
});
function Yu(e, t, n) {
  const { width: i, height: o } = mt(n), { x: s, y: a } = n.internals.positionAbsolute;
  return Ht(e, [
    [s, a],
    [s + i, a + o]
  ], t);
}
const cc = (e, t, n) => e < t ? an(Math.abs(e - t), 1, t) / t : e > n ? -an(Math.abs(e - n), 1, t) / t : 0, Ps = (e, t, n = 15, i = 40) => {
  const o = cc(e.x, i, t.width - i) * n, s = cc(e.y, i, t.height - i) * n;
  return [o, s];
}, Ir = (e, t) => ({
  x: Math.min(e.x, t.x),
  y: Math.min(e.y, t.y),
  x2: Math.max(e.x2, t.x2),
  y2: Math.max(e.y2, t.y2)
}), Yo = ({ x: e, y: t, width: n, height: i }) => ({
  x: e,
  y: t,
  x2: e + n,
  y2: t + i
}), kr = ({ x: e, y: t, x2: n, y2: i }) => ({
  x: e,
  y: t,
  width: n - e,
  height: i - t
}), cn = (e, t = [0, 0]) => {
  const { x: n, y: i } = Ts(e) ? e.internals.positionAbsolute : ai(e, t);
  return {
    x: n,
    y: i,
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}, ur = (e, t = [0, 0]) => {
  const { x: n, y: i } = Ts(e) ? e.internals.positionAbsolute : ai(e, t);
  return {
    x: n,
    y: i,
    x2: n + (e.measured?.width ?? e.width ?? e.initialWidth ?? 0),
    y2: i + (e.measured?.height ?? e.height ?? e.initialHeight ?? 0)
  };
}, Zu = (e, t) => kr(Ir(Yo(e), Yo(t))), Zn = (e, t) => {
  const n = Math.max(0, Math.min(e.x + e.width, t.x + t.width) - Math.max(e.x, t.x)), i = Math.max(0, Math.min(e.y + e.height, t.y + t.height) - Math.max(e.y, t.y));
  return Math.ceil(n * i);
}, lc = (e) => Ge(e.width) && Ge(e.height) && Ge(e.x) && Ge(e.y), Ge = (e) => !isNaN(e) && isFinite(e), Ju = (e, t) => (n, i) => {
}, li = (e, t = [1, 1]) => ({
  x: t[0] * Math.round(e.x / t[0]),
  y: t[1] * Math.round(e.y / t[1])
}), mn = ({ x: e, y: t }, [n, i, o], s = !1, a = [1, 1]) => {
  const c = {
    x: (e - n) / o,
    y: (t - i) / o
  };
  return s ? li(c, a) : c;
}, ln = ({ x: e, y: t }, [n, i, o]) => ({
  x: e * o + n,
  y: t * o + i
});
function Ut(e, t) {
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
function _w(e, t, n) {
  if (typeof e == "string" || typeof e == "number") {
    const i = Ut(e, n), o = Ut(e, t);
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
    const i = Ut(e.top ?? e.y ?? 0, n), o = Ut(e.bottom ?? e.y ?? 0, n), s = Ut(e.left ?? e.x ?? 0, t), a = Ut(e.right ?? e.x ?? 0, t);
    return { top: i, right: a, bottom: o, left: s, x: s + a, y: i + o };
  }
  return { top: 0, right: 0, bottom: 0, left: 0, x: 0, y: 0 };
}
function Dw(e, t, n, i, o, s) {
  const { x: a, y: c } = ln(e, [t, n, i]), { x: u, y: l } = ln({ x: e.x + e.width, y: e.y + e.height }, [t, n, i]), d = o - u, f = s - l;
  return {
    left: Math.floor(a),
    top: Math.floor(c),
    right: Math.floor(d),
    bottom: Math.floor(f)
  };
}
const Ms = (e, t, n, i, o, s) => {
  const a = _w(s, t, n), c = (t - a.x) / e.width, u = (n - a.y) / e.height, l = Math.min(c, u), d = an(l, i, o), f = e.x + e.width / 2, p = e.y + e.height / 2, h = t / 2 - f * d, m = n / 2 - p * d, v = Dw(e, h, m, d, t, n), x = {
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
}, Jn = () => typeof navigator < "u" && navigator?.userAgent?.indexOf("Mac") >= 0;
function Wt(e) {
  return e != null && e !== "parent";
}
function mt(e) {
  return {
    width: e.measured?.width ?? e.width ?? e.initialWidth ?? 0,
    height: e.measured?.height ?? e.height ?? e.initialHeight ?? 0
  };
}
function Gu(e) {
  return (e.measured?.width ?? e.width ?? e.initialWidth) !== void 0 && (e.measured?.height ?? e.height ?? e.initialHeight) !== void 0;
}
function Qu(e, t = { width: 0, height: 0 }, n, i, o) {
  const s = { ...e }, a = i.get(n);
  if (a) {
    const c = a.origin || o;
    s.x += a.internals.positionAbsolute.x - (t.width ?? 0) * c[0], s.y += a.internals.positionAbsolute.y - (t.height ?? 0) * c[1];
  }
  return s;
}
function uc(e, t) {
  if (e.size !== t.size)
    return !1;
  for (const n of e)
    if (!t.has(n))
      return !1;
  return !0;
}
function $w() {
  let e, t;
  return { promise: new Promise((i, o) => {
    e = i, t = o;
  }), resolve: e, reject: t };
}
function Tw(e) {
  return { ...Bu, ...e || {} };
}
function Ln(e, { snapGrid: t = [0, 0], snapToGrid: n = !1, transform: i, containerBounds: o }) {
  const { x: s, y: a } = Qe(e), c = mn({ x: s - (o?.left ?? 0), y: a - (o?.top ?? 0) }, i), { x: u, y: l } = n ? li(c, t) : c;
  return {
    xSnapped: u,
    ySnapped: l,
    ...c
  };
}
const Ls = (e) => ({
  width: e.offsetWidth,
  height: e.offsetHeight
}), ed = (e) => e?.getRootNode?.() || window?.document, Rw = ["INPUT", "SELECT", "TEXTAREA"];
function td(e) {
  const t = e.composedPath?.()?.[0] || e.target;
  return t?.nodeType !== 1 ? !1 : Rw.includes(t.nodeName) || t.hasAttribute("contenteditable") || !!t.closest(".nokey");
}
const nd = (e) => "clientX" in e, Qe = (e, t) => {
  const n = nd(e), i = n ? e.clientX : e.touches?.[0].clientX, o = n ? e.clientY : e.touches?.[0].clientY;
  return {
    x: i - (t?.left ?? 0),
    y: o - (t?.top ?? 0)
  };
}, dc = (e, t, n, i, o) => {
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
      ...Ls(a)
    };
  });
};
function id({ sourceX: e, sourceY: t, targetX: n, targetY: i, sourceControlX: o, sourceControlY: s, targetControlX: a, targetControlY: c }) {
  const u = e * 0.125 + o * 0.375 + a * 0.375 + n * 0.125, l = t * 0.125 + s * 0.375 + c * 0.375 + i * 0.125, d = Math.abs(u - e), f = Math.abs(l - t);
  return [u, l, d, f];
}
function Ri(e, t) {
  return e >= 0 ? 0.5 * e : t * 25 * Math.sqrt(-e);
}
function fc({ pos: e, x1: t, y1: n, x2: i, y2: o, c: s }) {
  switch (e) {
    case ce.Left:
      return [t - Ri(t - i, s), n];
    case ce.Right:
      return [t + Ri(i - t, s), n];
    case ce.Top:
      return [t, n - Ri(n - o, s)];
    case ce.Bottom:
      return [t, n + Ri(o - n, s)];
  }
}
function rd({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top, curvature: a = 0.25 }) {
  const [c, u] = fc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o,
    c: a
  }), [l, d] = fc({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t,
    c: a
  }), [f, p, h, m] = id({
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
function od({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const o = Math.abs(n - e) / 2, s = n < e ? n + o : n - o, a = Math.abs(i - t) / 2, c = i < t ? i + a : i - a;
  return [s, c, o, a];
}
function Pw({ sourceNode: e, targetNode: t, selected: n = !1, zIndex: i = 0, elevateOnSelect: o = !1, zIndexMode: s = "basic" }) {
  if (s === "manual")
    return i;
  const a = o && n ? i + 1e3 : i, c = Math.max(e.parentId || o && e.selected ? e.internals.z : 0, t.parentId || o && t.selected ? t.internals.z : 0);
  return a + c;
}
function Mw({ sourceNode: e, targetNode: t, width: n, height: i, transform: o }) {
  const s = Ir(ur(e), ur(t));
  s.x === s.x2 && (s.x2 += 1), s.y === s.y2 && (s.y2 += 1);
  const a = {
    x: -o[0] / o[2],
    y: -o[1] / o[2],
    width: n / o[2],
    height: i / o[2]
  };
  return Zn(a, kr(s)) > 0;
}
const sd = ({ source: e, sourceHandle: t, target: n, targetHandle: i }) => `xy-edge__${e}${t || ""}-${n}${i || ""}`, Lw = (e, t) => t.some((n) => n.source === e.source && n.target === e.target && (n.sourceHandle === e.sourceHandle || !n.sourceHandle && !e.sourceHandle) && (n.targetHandle === e.targetHandle || !n.targetHandle && !e.targetHandle)), zw = (e, t, n = {}) => {
  if (!e.source || !e.target)
    return n.onError?.("006", Ke.error006()), t;
  const i = n.getEdgeId || sd;
  let o;
  return Xu(e) ? o = { ...e } : o = {
    ...e,
    id: i(e)
  }, Lw(o, t) ? t : (o.sourceHandle === null && delete o.sourceHandle, o.targetHandle === null && delete o.targetHandle, t.concat(o));
}, Vw = (e, t, n, i = { shouldReplaceId: !0 }) => {
  const { id: o, ...s } = e;
  if (!t.source || !t.target)
    return i.onError?.("006", Ke.error006()), n;
  if (!n.find((l) => l.id === e.id))
    return i.onError?.("007", Ke.error007(o)), n;
  const c = i.getEdgeId || sd, u = {
    ...s,
    id: i.shouldReplaceId ? c(t) : o,
    source: t.source,
    target: t.target,
    sourceHandle: t.sourceHandle,
    targetHandle: t.targetHandle
  };
  return n.filter((l) => l.id !== o).concat(u);
};
function ad({ sourceX: e, sourceY: t, targetX: n, targetY: i }) {
  const [o, s, a, c] = od({
    sourceX: e,
    sourceY: t,
    targetX: n,
    targetY: i
  });
  return [`M ${e},${t}L ${n},${i}`, o, s, a, c];
}
const pc = {
  [ce.Left]: { x: -1, y: 0 },
  [ce.Right]: { x: 1, y: 0 },
  [ce.Top]: { x: 0, y: -1 },
  [ce.Bottom]: { x: 0, y: 1 }
}, Ow = ({ source: e, sourcePosition: t = ce.Bottom, target: n }) => t === ce.Left || t === ce.Right ? e.x < n.x ? { x: 1, y: 0 } : { x: -1, y: 0 } : e.y < n.y ? { x: 0, y: 1 } : { x: 0, y: -1 }, hc = (e, t) => Math.sqrt(Math.pow(t.x - e.x, 2) + Math.pow(t.y - e.y, 2));
function Hw({ source: e, sourcePosition: t = ce.Bottom, target: n, targetPosition: i = ce.Top, center: o, offset: s, stepPosition: a }) {
  const c = pc[t], u = pc[i], l = { x: e.x + c.x * s, y: e.y + c.y * s }, d = { x: n.x + u.x * s, y: n.y + u.y * s }, f = Ow({
    source: l,
    sourcePosition: t,
    target: d
  }), p = f.x !== 0 ? "x" : "y", h = f[p];
  let m = [], v, x;
  const y = { x: 0, y: 0 }, j = { x: 0, y: 0 }, [, , g, w] = od({
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
function Ww(e, t, n, i) {
  const o = Math.min(hc(e, t) / 2, hc(t, n) / 2, i), { x: s, y: a } = t;
  if (e.x === s && s === n.x || e.y === a && a === n.y)
    return `L${s} ${a}`;
  if (e.y === a) {
    const l = e.x < n.x ? -1 : 1, d = e.y < n.y ? 1 : -1;
    return `L ${s + o * l},${a}Q ${s},${a} ${s},${a + o * d}`;
  }
  const c = e.x < n.x ? 1 : -1, u = e.y < n.y ? -1 : 1;
  return `L ${s},${a + o * u}Q ${s},${a} ${s + o * c},${a}`;
}
function dr({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top, borderRadius: a = 5, centerX: c, centerY: u, offset: l = 20, stepPosition: d = 0.5 }) {
  const [f, p, h, m, v] = Hw({
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
    x += Ww(f[y - 1], f[y], f[y + 1], a);
  return x += `L${f[f.length - 1].x} ${f[f.length - 1].y}`, [x, p, h, m, v];
}
function gc(e) {
  return e && !!(e.internals.handleBounds || e.handles?.length) && !!(e.measured.width || e.width || e.initialWidth);
}
function Fw(e) {
  const { sourceNode: t, targetNode: n } = e;
  if (!gc(t) || !gc(n))
    return null;
  const i = t.internals.handleBounds || mc(t.handles), o = n.internals.handleBounds || mc(n.handles), s = yc(i?.source ?? [], e.sourceHandle), a = yc(
    // when connection type is loose we can define all handles as sources and connect source -> source
    e.connectionMode === sn.Strict ? o?.target ?? [] : (o?.target ?? []).concat(o?.source ?? []),
    e.targetHandle
  );
  if (!s || !a)
    return e.onError?.("008", Ke.error008(s ? "target" : "source", {
      id: e.id,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle
    })), null;
  const c = s?.position || ce.Bottom, u = a?.position || ce.Top, l = Ft(t, s, c), d = Ft(n, a, u);
  return {
    sourceX: l.x,
    sourceY: l.y,
    targetX: d.x,
    targetY: d.y,
    sourcePosition: c,
    targetPosition: u
  };
}
function mc(e) {
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
function Ft(e, t, n = ce.Left, i = !1) {
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
function yc(e, t) {
  return e && (t ? e.find((n) => n.id === t) : e[0]) || null;
}
function Zo(e, t) {
  return e ? typeof e == "string" ? e : `${t ? `${t}__` : ""}${Object.keys(e).sort().map((i) => `${i}=${e[i]}`).join("&")}` : "";
}
function Bw(e, { id: t, defaultColor: n, defaultMarkerStart: i, defaultMarkerEnd: o }) {
  const s = /* @__PURE__ */ new Set();
  return e.reduce((a, c) => ([c.markerStart || i, c.markerEnd || o].forEach((u) => {
    if (u && typeof u == "object") {
      const l = Zo(u, t);
      s.has(l) || (a.push({ id: l, color: u.color || n, ...u }), s.add(l));
    }
  }), a), []).sort((a, c) => a.id.localeCompare(c.id));
}
const cd = 1e3, Kw = 10, zs = {
  nodeOrigin: [0, 0],
  nodeExtent: Un,
  elevateNodesOnSelect: !0,
  zIndexMode: "basic",
  defaults: {}
}, qw = {
  ...zs,
  checkEquality: !0
};
function Vs(e, t) {
  const n = { ...e };
  for (const i in t)
    t[i] !== void 0 && (n[i] = t[i]);
  return n;
}
function Xw(e, t, n) {
  const i = Vs(zs, n);
  for (const o of e.values())
    if (o.parentId)
      Hs(o, e, t, i);
    else {
      const s = ai(o, i.nodeOrigin), a = Wt(o.extent) ? o.extent : i.nodeExtent, c = Ht(s, a, mt(o));
      o.internals.positionAbsolute = c;
    }
}
function Uw(e, t) {
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
function Os(e) {
  return e === "manual";
}
function Jo(e, t, n, i = {}) {
  const o = Vs(qw, i), s = { i: 0 }, a = new Map(t), c = o?.elevateNodesOnSelect && !Os(o.zIndexMode) ? cd : 0;
  let u = e.length > 0, l = !1;
  t.clear(), n.clear();
  for (const d of e) {
    let f = a.get(d.id);
    if (o.checkEquality && d === f?.internals.userNode)
      t.set(d.id, f);
    else {
      const p = ai(d, o.nodeOrigin), h = Wt(d.extent) ? d.extent : o.nodeExtent, m = Ht(p, h, mt(d));
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
          handleBounds: Uw(d, f),
          z: ld(d, c, o.zIndexMode),
          userNode: d
        }
      }, t.set(d.id, f);
    }
    (f.measured === void 0 || f.measured.width === void 0 || f.measured.height === void 0) && !f.hidden && (u = !1), d.parentId && Hs(f, t, n, i, s), l ||= d.selected ?? !1;
  }
  return { nodesInitialized: u, hasSelectedNodes: l };
}
function Yw(e, t) {
  if (!e.parentId)
    return;
  const n = t.get(e.parentId);
  n ? n.set(e.id, e) : t.set(e.parentId, /* @__PURE__ */ new Map([[e.id, e]]));
}
function Hs(e, t, n, i, o) {
  const { elevateNodesOnSelect: s, nodeOrigin: a, nodeExtent: c, zIndexMode: u } = Vs(zs, i), l = e.parentId, d = t.get(l);
  if (!d) {
    console.warn(`Parent node ${l} not found. Please make sure that parent nodes are in front of their child nodes in the nodes array.`);
    return;
  }
  Yw(e, n), o && !d.parentId && d.internals.rootParentIndex === void 0 && u === "auto" && (d.internals.rootParentIndex = ++o.i, d.internals.z = d.internals.z + o.i * Kw), o && d.internals.rootParentIndex !== void 0 && (o.i = d.internals.rootParentIndex);
  const f = s && !Os(u) ? cd : 0, { x: p, y: h, z: m } = Zw(e, d, a, c, f, u), { positionAbsolute: v } = e.internals, x = p !== v.x || h !== v.y;
  (x || m !== e.internals.z) && t.set(e.id, {
    ...e,
    internals: {
      ...e.internals,
      positionAbsolute: x ? { x: p, y: h } : v,
      z: m
    }
  });
}
function ld(e, t, n) {
  const i = Ge(e.zIndex) ? e.zIndex : 0;
  return Os(n) ? i : i + (e.selected ? t : 0);
}
function Zw(e, t, n, i, o, s) {
  const { x: a, y: c } = t.internals.positionAbsolute, u = mt(e), l = ai(e, n), d = Wt(e.extent) ? Ht(l, e.extent, u) : l;
  let f = Ht({ x: a + d.x, y: c + d.y }, i, u);
  e.extent === "parent" && (f = Yu(f, u, t));
  const p = ld(e, o, s), h = t.internals.z ?? 0;
  return {
    x: f.x,
    y: f.y,
    z: h >= p ? h + 1 : p
  };
}
function Ws(e, t, n, i = [0, 0]) {
  const o = [], s = /* @__PURE__ */ new Map();
  for (const a of e) {
    const c = t.get(a.parentId);
    if (!c)
      continue;
    const u = s.get(a.parentId)?.expandedRect ?? cn(c), l = Zu(u, a.rect);
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
function Jw(e, t, n, i, o, s, a) {
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
    const v = Ls(h.nodeElement), x = m.measured.width !== v.width || m.measured.height !== v.height;
    if (!!(v.width && v.height && (x || !m.internals.handleBounds || h.force))) {
      const j = h.nodeElement.getBoundingClientRect(), g = Wt(m.extent) ? m.extent : s;
      let { positionAbsolute: w } = m.internals;
      m.parentId && m.extent === "parent" ? w = Yu(w, v, t.get(m.parentId)) : g && (w = Ht(w, g, v));
      const S = {
        ...m,
        measured: v,
        internals: {
          ...m.internals,
          positionAbsolute: w,
          handleBounds: {
            source: dc("source", h.nodeElement, j, f, m.id),
            target: dc("target", h.nodeElement, j, f, m.id)
          }
        }
      };
      t.set(m.id, S), m.parentId && Hs(S, t, n, { nodeOrigin: o, zIndexMode: a }), u = !0, x && (l.push({
        id: m.id,
        type: "dimensions",
        dimensions: v
      }), m.expandParent && m.parentId && p.push({
        id: m.id,
        parentId: m.parentId,
        rect: cn(S, o)
      }));
    }
  }
  if (p.length > 0) {
    const h = Ws(p, t, n, o);
    l.push(...h);
  }
  return { changes: l, updatedInternals: u };
}
async function Gw({ delta: e, panZoom: t, transform: n, translateExtent: i, width: o, height: s }) {
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
function xc(e, t, n, i, o, s) {
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
function ud(e, t, n) {
  e.clear(), t.clear();
  for (const i of n) {
    const { source: o, target: s, sourceHandle: a = null, targetHandle: c = null } = i, u = { edgeId: i.id, source: o, target: s, sourceHandle: a, targetHandle: c }, l = `${o}-${a}--${s}-${c}`, d = `${s}-${c}--${o}-${a}`;
    xc("source", u, d, e, o, a), xc("target", u, l, e, s, c), t.set(i.id, i);
  }
}
function dd(e, t) {
  if (!e.parentId)
    return !1;
  const n = t.get(e.parentId);
  return n ? n.selected ? !0 : dd(n, t) : !1;
}
function wc(e, t, n) {
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
function Qw(e, t, n, i) {
  const o = /* @__PURE__ */ new Map();
  for (const [s, a] of e)
    if ((a.selected || a.id === i) && (!a.parentId || !dd(a, e)) && (a.draggable || t && typeof a.draggable > "u")) {
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
function ev({ dragItems: e, snapGrid: t, x: n, y: i }) {
  const o = e.values().next().value;
  if (!o)
    return null;
  const s = {
    x: n - o.distance.x,
    y: i - o.distance.y
  }, a = li(s, t);
  return {
    x: a.x - s.x,
    y: a.y - s.y
  };
}
function tv({ onNodeMouseDown: e, getStoreItems: t, onDragStart: n, onDrag: i, onDragStop: o }) {
  let s = { x: null, y: null }, a = 0, c = /* @__PURE__ */ new Map(), u = !1, l = { x: 0, y: 0 }, d = null, f = !1, p = null, h = !1, m = !1, v = null;
  function x({ noDragClassName: j, handleSelector: g, domNode: w, isSelectable: S, nodeId: b, nodeClickDistance: I = 0 }) {
    p = He(w);
    function A({ x: R, y: L }) {
      const { nodeLookup: C, nodeExtent: _, snapGrid: N, snapToGrid: E, nodeOrigin: T, onNodeDrag: $, onSelectionDrag: z, onError: F, updateNodePositions: B } = t();
      s = { x: R, y: L };
      let Z = !1;
      const H = c.size > 1, U = H && _ ? Yo(ci(c)) : null, G = H && E ? ev({
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
        } : li(q, N));
        let de = null;
        if (H && _ && !P.extent && U) {
          const { positionAbsolute: ie } = P.internals, fe = ie.x - U.x + _[0][0], W = ie.x + P.measured.width - U.x2 + _[1][0], ne = ie.y - U.y + _[0][1], ge = ie.y + P.measured.height - U.y2 + _[1][1];
          de = [
            [fe, ne],
            [W, ge]
          ];
        }
        const { position: le, positionAbsolute: te } = Uu({
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
      const [N, E] = Ps(l, d, C);
      (N !== 0 || E !== 0) && (s.x = (s.x ?? 0) - N / R[2], s.y = (s.y ?? 0) - E / R[2], await L({ x: N, y: E }) && A(s)), a = requestAnimationFrame(D);
    }
    function M(R) {
      const { nodeLookup: L, multiSelectionActive: C, nodesDraggable: _, transform: N, snapGrid: E, snapToGrid: T, selectNodesOnDrag: $, onNodeDragStart: z, onSelectionDragStart: F, unselectNodesAndEdges: B } = t();
      f = !0, (!$ || !S) && !C && b && (L.get(b)?.selected || B()), S && $ && b && e?.(b);
      const Z = Ln(R.sourceEvent, { transform: N, snapGrid: E, snapToGrid: T, containerBounds: d });
      if (s = Z, c = Qw(L, _, Z, b), c.size > 0 && (n || z || !b && F)) {
        const [H, U] = Co({
          nodeId: b,
          dragItems: c,
          nodeLookup: L
        });
        n?.(R.sourceEvent, c, H, U), z?.(R.sourceEvent, H, U), b || F?.(R.sourceEvent, U);
      }
    }
    const k = ku().clickDistance(I).on("start", (R) => {
      const { domNode: L, nodeDragThreshold: C, transform: _, snapGrid: N, snapToGrid: E } = t();
      d = L?.getBoundingClientRect() || null, h = !1, m = !1, v = R.sourceEvent, C === 0 && M(R), s = Ln(R.sourceEvent, { transform: _, snapGrid: N, snapToGrid: E, containerBounds: d }), l = Qe(R.sourceEvent, d);
    }).on("drag", (R) => {
      const { autoPanOnNodeDrag: L, transform: C, snapGrid: _, snapToGrid: N, nodeDragThreshold: E, nodeLookup: T } = t(), $ = Ln(R.sourceEvent, { transform: C, snapGrid: _, snapToGrid: N, containerBounds: d });
      if (v = R.sourceEvent, (R.sourceEvent.type === "touchmove" && R.sourceEvent.touches.length > 1 || // if user deletes a node while dragging, we need to abort the drag to prevent errors
      b && !T.has(b)) && (h = !0), !h) {
        if (!u && L && f && (u = !0, D()), !f) {
          const z = Qe(R.sourceEvent, d), F = z.x - l.x, B = z.y - l.y;
          Math.sqrt(F * F + B * B) > E && M(R);
        }
        (s.x !== $.xSnapped || s.y !== $.ySnapped) && c && f && (l = Qe(R.sourceEvent, d), A($));
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
      return !R.button && (!j || !wc(L, `.${j}`, w)) && (!g || wc(L, g, w));
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
function nv(e, t, n) {
  const i = [], o = {
    x: e.x - n,
    y: e.y - n,
    width: n * 2,
    height: n * 2
  };
  for (const s of t.values())
    Zn(o, cn(s)) > 0 && i.push(s);
  return i;
}
const iv = 250;
function rv(e, t, n, i) {
  let o = [], s = 1 / 0;
  const a = nv(e, n, t + iv);
  for (const c of a) {
    const u = [...c.internals.handleBounds?.source ?? [], ...c.internals.handleBounds?.target ?? []];
    for (const l of u) {
      if (i.nodeId === l.nodeId && i.type === l.type && i.id === l.id)
        continue;
      const { x: d, y: f } = Ft(c, l, l.position, !0), p = Math.sqrt(Math.pow(d - e.x, 2) + Math.pow(f - e.y, 2));
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
function fd(e, t, n, i, o, s = !1) {
  const a = i.get(e);
  if (!a)
    return null;
  const c = o === "strict" ? a.internals.handleBounds?.[t] : [...a.internals.handleBounds?.source ?? [], ...a.internals.handleBounds?.target ?? []], u = (n ? c?.find((l) => l.id === n) : c?.[0]) ?? null;
  return u && s ? { ...u, ...Ft(a, u, u.position, !0) } : u;
}
function pd(e, t) {
  return e || (t?.classList.contains("target") ? "target" : t?.classList.contains("source") ? "source" : null);
}
function ov(e, t) {
  let n = null;
  return t ? n = !0 : e && !t && (n = !1), n;
}
const hd = () => !0;
function sv(e, { connectionMode: t, connectionRadius: n, handleId: i, nodeId: o, edgeUpdaterType: s, isTarget: a, domNode: c, nodeLookup: u, lib: l, autoPanOnConnect: d, flowId: f, panBy: p, cancelConnection: h, onConnectStart: m, onConnect: v, onConnectEnd: x, isValidConnection: y = hd, onReconnectEnd: j, updateConnection: g, getTransform: w, getFromHandle: S, autoPanSpeed: b, dragThreshold: I = 1, handleDomNode: A }) {
  const D = ed(e.target);
  let M = 0, k;
  const { x: R, y: L } = Qe(e), C = pd(s, A), _ = c?.getBoundingClientRect();
  let N = !1;
  if (!_ || !C)
    return;
  const E = fd(o, C, i, u, t);
  if (!E)
    return;
  let T = Qe(e, _), $ = !1, z = null, F = !1, B = null;
  function Z() {
    if (!d || !_)
      return;
    const [le, te] = Ps(T, _, b);
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
    from: Ft(U, H, ce.Left, !0),
    fromHandle: H,
    fromPosition: H.position,
    fromNode: U,
    to: T,
    toHandle: null,
    toPosition: ac[H.position],
    toNode: null,
    pointer: T
  };
  function P() {
    N = !0, g(X), m?.(e, { nodeId: o, handleId: i, handleType: C });
  }
  I === 0 && P();
  function q(le) {
    if (!N) {
      const { x: ge, y: ye } = Qe(le), Ee = ge - R, De = ye - L;
      if (!(Ee * Ee + De * De > I * I))
        return;
      P();
    }
    if (!S() || !H) {
      de(le);
      return;
    }
    const te = w();
    T = Qe(le, _), k = rv(mn(T, te, !1, [1, 1]), n, u, H), $ || (Z(), $ = !0);
    const ie = gd(le, {
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
    B = ie.handleDomNode, z = ie.connection, F = ov(!!k, ie.isValid);
    const fe = u.get(o), W = fe ? Ft(fe, H, ce.Left, !0) : X.from, ne = {
      ...X,
      from: W,
      isValid: F,
      to: ie.toHandle && F ? ln({ x: ie.toHandle.x, y: ie.toHandle.y }, te) : T,
      toHandle: ie.toHandle,
      toPosition: F && ie.toHandle ? ie.toHandle.position : ac[H.position],
      toNode: ie.toHandle ? u.get(ie.toHandle.nodeId) : null,
      pointer: T
    };
    g(ne), X = ne;
  }
  function de(le) {
    if (!("touches" in le && le.touches.length > 0)) {
      if (N) {
        (k || B) && z && F && v?.(z);
        const { inProgress: te, ...ie } = X, fe = {
          ...ie,
          toPosition: X.toHandle ? X.toPosition : null
        };
        x?.(le, fe), s && j?.(le, fe);
      }
      h(), cancelAnimationFrame(M), $ = !1, F = !1, z = null, B = null, D.removeEventListener("mousemove", q), D.removeEventListener("mouseup", de), D.removeEventListener("touchmove", q), D.removeEventListener("touchend", de);
    }
  }
  D.addEventListener("mousemove", q), D.addEventListener("mouseup", de), D.addEventListener("touchmove", q), D.addEventListener("touchend", de);
}
function gd(e, { handle: t, connectionMode: n, fromNodeId: i, fromHandleId: o, fromType: s, doc: a, lib: c, flowId: u, isValidConnection: l = hd, nodeLookup: d }) {
  const f = s === "target", p = t ? a.querySelector(`.${c}-flow__handle[data-id="${u}-${t?.nodeId}-${t?.id}-${t?.type}"]`) : null, { x: h, y: m } = Qe(e), v = a.elementFromPoint(h, m), x = v?.classList.contains(`${c}-flow__handle`) ? v : p, y = {
    handleDomNode: x,
    isValid: !1,
    connection: null,
    toHandle: null
  };
  if (x) {
    const j = pd(void 0, x), g = x.getAttribute("data-nodeid"), w = x.getAttribute("data-handleid"), S = x.classList.contains("connectable"), b = x.classList.contains("connectableend");
    if (!g || !j)
      return y;
    const I = {
      source: f ? g : i,
      sourceHandle: f ? w : o,
      target: f ? i : g,
      targetHandle: f ? o : w
    };
    y.connection = I;
    const D = S && b && (n === sn.Strict ? f && j === "source" || !f && j === "target" : g !== i || w !== o);
    y.isValid = D && l(I), y.toHandle = fd(g, j, w, d, n, !0);
  }
  return y;
}
const Go = {
  onPointerDown: sv,
  isValid: gd
};
function av({ domNode: e, panZoom: t, getTransform: n, getViewScale: i }) {
  const o = He(e);
  function s({ translateExtent: c, width: u, height: l, zoomStep: d = 1, pannable: f = !0, zoomable: p = !0, inversePan: h = !1 }) {
    const m = (g) => {
      if (g.sourceEvent.type !== "wheel" || !t)
        return;
      const w = n(), S = g.sourceEvent.ctrlKey && Jn() ? 10 : 1, b = -g.sourceEvent.deltaY * (g.sourceEvent.deltaMode === 1 ? 0.05 : g.sourceEvent.deltaMode ? 1 : 2e-3) * d, I = w[2] * Math.pow(2, b * S);
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
    }, j = Wu().on("start", x).on("zoom", f ? y : null).on("zoom.wheel", p ? m : null);
    o.call(j, {});
  }
  function a() {
    o.on("zoom", null);
  }
  return {
    update: s,
    destroy: a,
    pointer: Ze
  };
}
const Er = (e) => ({
  x: e.x,
  y: e.y,
  zoom: e.k
}), Io = ({ x: e, y: t, zoom: n }) => Cr.translate(e, t).scale(n), Zt = (e, t) => e.target.closest(`.${t}`), md = (e, t) => t === 2 && Array.isArray(e) && e.includes(2), cv = (e) => ((e *= 2) <= 1 ? e * e * e : (e -= 2) * e * e + 2) / 2, ko = (e, t = 0, n = cv, i = () => {
}) => {
  const o = typeof t == "number" && t > 0;
  return o || i(), o ? e.transition().duration(t).ease(n).on("end", i) : e;
}, yd = (e) => {
  const t = e.ctrlKey && Jn() ? 10 : 1;
  return -e.deltaY * (e.deltaMode === 1 ? 0.05 : e.deltaMode ? 1 : 2e-3) * t;
};
function lv({ zoomPanValues: e, noWheelClassName: t, d3Selection: n, d3Zoom: i, panOnScrollMode: o, panOnScrollSpeed: s, zoomOnPinch: a, onPanZoomStart: c, onPanZoom: u, onPanZoomEnd: l }) {
  return (d) => {
    if (Zt(d, t))
      return d.ctrlKey && d.preventDefault(), !1;
    d.preventDefault(), d.stopImmediatePropagation();
    const f = n.property("__zoom").k || 1;
    if (d.ctrlKey && a) {
      const x = Ze(d), y = yd(d), j = f * Math.pow(2, y);
      i.scaleTo(n, j, x, d);
      return;
    }
    const p = d.deltaMode === 1 ? 20 : 1;
    let h = o === Tt.Vertical ? 0 : d.deltaX * p, m = o === Tt.Horizontal ? 0 : d.deltaY * p;
    !Jn() && d.shiftKey && o !== Tt.Vertical && (h = d.deltaY * p, m = 0), i.translateBy(
      n,
      -(h / f) * s,
      -(m / f) * s,
      // @ts-ignore
      { internal: !0 }
    );
    const v = Er(n.property("__zoom"));
    clearTimeout(e.panScrollTimeout), e.isPanScrolling ? (u?.(d, v), e.panScrollTimeout = setTimeout(() => {
      l?.(d, v), e.isPanScrolling = !1;
    }, 150)) : (e.isPanScrolling = !0, c?.(d, v));
  };
}
function uv({ noWheelClassName: e, preventScrolling: t, d3ZoomHandler: n }) {
  return function(i, o) {
    const s = i.type === "wheel", a = !t && s && !i.ctrlKey, c = Zt(i, e);
    if (i.ctrlKey && s && c && i.preventDefault(), a || c)
      return null;
    i.preventDefault(), n.call(this, i, o);
  };
}
function dv({ zoomPanValues: e, onDraggingChange: t, onPanZoomStart: n }) {
  return (i) => {
    if (i.sourceEvent?.internal)
      return;
    const o = Er(i.transform);
    e.mouseButton = i.sourceEvent?.button || 0, e.isZoomingOrPanning = !0, e.prevViewport = o, i.sourceEvent?.type === "mousedown" && t(!0), n && n?.(i.sourceEvent, o);
  };
}
function fv({ zoomPanValues: e, panOnDrag: t, onPaneContextMenu: n, onTransformChange: i, onPanZoom: o }) {
  return (s) => {
    e.usedRightMouseButton = !!(n && md(t, e.mouseButton ?? 0)), s.sourceEvent?.sync || i([s.transform.x, s.transform.y, s.transform.k]), o && !s.sourceEvent?.internal && o?.(s.sourceEvent, Er(s.transform));
  };
}
function pv({ zoomPanValues: e, panOnDrag: t, panOnScroll: n, onDraggingChange: i, onPanZoomEnd: o, onPaneContextMenu: s }) {
  return (a) => {
    if (!a.sourceEvent?.internal && (e.isZoomingOrPanning = !1, s && md(t, e.mouseButton ?? 0) && !e.usedRightMouseButton && a.sourceEvent && s(a.sourceEvent), e.usedRightMouseButton = !1, i(!1), o)) {
      const c = Er(a.transform);
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
function hv({ zoomActivationKeyPressed: e, zoomOnScroll: t, zoomOnPinch: n, panOnDrag: i, panOnScroll: o, zoomOnDoubleClick: s, userSelectionActive: a, noWheelClassName: c, noPanClassName: u, lib: l, connectionInProgress: d }) {
  return (f) => {
    const p = e || t, h = n && f.ctrlKey, m = f.type === "wheel";
    if (f.button === 1 && f.type === "mousedown" && (Zt(f, `${l}-flow__node`) || Zt(f, `${l}-flow__edge`)))
      return !0;
    if (!i && !p && !o && !s && !n || a || d && !m || Zt(f, c) && m || Zt(f, u) && (!m || o && m && !e) || !n && f.ctrlKey && m)
      return !1;
    if (!n && f.type === "touchstart" && f.touches?.length > 1)
      return f.preventDefault(), !1;
    if (!p && !o && !h && m || !i && (f.type === "mousedown" || f.type === "touchstart") || Array.isArray(i) && !i.includes(f.button) && f.type === "mousedown")
      return !1;
    const v = Array.isArray(i) && i.includes(f.button) || !f.button || f.button <= 1;
    return (!f.ctrlKey || m) && v;
  };
}
function gv({ domNode: e, minZoom: t, maxZoom: n, translateExtent: i, viewport: o, onPanZoom: s, onPanZoomStart: a, onPanZoomEnd: c, onDraggingChange: u }) {
  const l = {
    isZoomingOrPanning: !1,
    usedRightMouseButton: !1,
    prevViewport: {},
    mouseButton: 0,
    timerId: void 0,
    panScrollTimeout: void 0,
    isPanScrolling: !1
  }, d = e.getBoundingClientRect(), f = Wu().scaleExtent([t, n]).translateExtent(i), p = He(e).call(f);
  j({
    x: o.x,
    y: o.y,
    zoom: an(o.zoom, t, n)
  }, [
    [0, 0],
    [d.width, d.height]
  ], i);
  const h = p.on("wheel.zoom"), m = p.on("dblclick.zoom");
  f.wheelDelta(yd);
  async function v(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Mn : Bi).transform(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  function x({ noWheelClassName: k, noPanClassName: R, onPaneContextMenu: L, userSelectionActive: C, panOnScroll: _, panOnDrag: N, panOnScrollMode: E, panOnScrollSpeed: T, preventScrolling: $, zoomOnPinch: z, zoomOnScroll: F, zoomOnDoubleClick: B, zoomActivationKeyPressed: Z, lib: H, onTransformChange: U, connectionInProgress: G, paneClickDistance: X, selectionOnDrag: P }) {
    C && !l.isZoomingOrPanning && y();
    const q = _ && !Z && !C;
    f.clickDistance(P ? 1 / 0 : !Ge(X) || X < 0 ? 0 : X);
    const de = q ? lv({
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
    }) : uv({
      noWheelClassName: k,
      preventScrolling: $,
      d3ZoomHandler: h
    });
    p.on("wheel.zoom", de, { passive: !1 });
    const le = dv({
      zoomPanValues: l,
      onDraggingChange: u,
      onPanZoomStart: a
    });
    f.on("start", le);
    const te = fv({
      zoomPanValues: l,
      panOnDrag: N,
      onPaneContextMenu: !!L,
      onPanZoom: s,
      onTransformChange: U
    });
    f.on("zoom", te);
    const ie = pv({
      zoomPanValues: l,
      panOnDrag: N,
      panOnScroll: _,
      onPaneContextMenu: L,
      onPanZoomEnd: c,
      onDraggingChange: u
    });
    f.on("end", ie);
    const fe = hv({
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
    const k = p ? Hu(p.node()) : { x: 0, y: 0, k: 1 };
    return { x: k.x, y: k.y, zoom: k.k };
  }
  async function b(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Mn : Bi).scaleTo(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  async function I(k, R) {
    return p ? new Promise((L) => {
      f?.interpolate(R?.interpolate === "linear" ? Mn : Bi).scaleBy(ko(p, R?.duration, R?.ease, () => L(!0)), k);
    }) : !1;
  }
  function A(k) {
    f?.scaleExtent(k);
  }
  function D(k) {
    f?.translateExtent(k);
  }
  function M(k) {
    const R = !Ge(k) || k < 0 ? 0 : k;
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
var un;
(function(e) {
  e.Line = "line", e.Handle = "handle";
})(un || (un = {}));
function mv({ width: e, prevWidth: t, height: n, prevHeight: i, affectsX: o, affectsY: s }) {
  const a = e - t, c = n - i, u = [a > 0 ? 1 : a < 0 ? -1 : 0, c > 0 ? 1 : c < 0 ? -1 : 0];
  return a && o && (u[0] = u[0] * -1), c && s && (u[1] = u[1] * -1), u;
}
function vc(e) {
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
function Pi(e, t, n) {
  return Math.max(0, t - e, e - n);
}
function bc(e, t) {
  return e ? !t : t;
}
function yv(e, t, n, i, o, s, a, c) {
  let { affectsX: u, affectsY: l } = t;
  const { isHorizontal: d, isVertical: f } = t, p = d && f, { xSnapped: h, ySnapped: m } = n, { minWidth: v, maxWidth: x, minHeight: y, maxHeight: j } = i, { x: g, y: w, width: S, height: b, aspectRatio: I } = e;
  let A = Math.floor(d ? h - e.pointerX : 0), D = Math.floor(f ? m - e.pointerY : 0);
  const M = S + (u ? -A : A), k = b + (l ? -D : D), R = -s[0] * S, L = -s[1] * b;
  let C = Pi(M, v, x), _ = Pi(k, y, j);
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
      const T = Pi(M / I, y, j) * I;
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
      const T = Pi(k * I, v, x) / I;
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
  D = D + (D < 0 ? _ : -_), A = A + (A < 0 ? C : -C), o && (p ? M > k * I ? D = (bc(u, l) ? -A : A) / I : A = (bc(u, l) ? -D : D) * I : d ? (D = A / I, l = u) : (A = D * I, u = l));
  const N = u ? g + A : g, E = l ? w + D : w;
  return {
    width: S + (u ? -A : A),
    height: b + (l ? -D : D),
    x: s[0] * A * (u ? -1 : 1) + N,
    y: s[1] * D * (l ? -1 : 1) + E
  };
}
const xd = { width: 0, height: 0, x: 0, y: 0 }, xv = {
  ...xd,
  pointerX: 0,
  pointerY: 0,
  aspectRatio: 1
};
function wv(e, t, n) {
  const i = t.position.x + e.position.x, o = t.position.y + e.position.y, s = e.measured.width ?? 0, a = e.measured.height ?? 0, c = n[0] * s, u = n[1] * a;
  return [
    [i - c, o - u],
    [i + s - c, o + a - u]
  ];
}
function vv({ domNode: e, nodeId: t, getStoreItems: n, onChange: i, onEnd: o }) {
  const s = He(e);
  let a = {
    controlDirection: vc("bottom-right"),
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
    let y = { ...xd }, j = { ...xv };
    a = {
      boundaries: d,
      resizeDirection: p,
      keepAspectRatio: f,
      controlDirection: vc(l)
    };
    let g, w = null, S = [], b, I, A, D = !1;
    const M = ku().on("start", (k) => {
      const { nodeLookup: R, transform: L, snapGrid: C, snapToGrid: _, nodeOrigin: N, paneDomNode: E } = n();
      if (g = R.get(t), !g)
        return;
      w = E?.getBoundingClientRect() ?? null;
      const { xSnapped: T, ySnapped: $ } = Ln(k.sourceEvent, {
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
      }, b = void 0, I = Wt(g.extent) ? g.extent : void 0, g.parentId && (g.extent === "parent" || g.expandParent) && (b = R.get(g.parentId)), b && g.extent === "parent" && (I = [
        [0, 0],
        [b.measured.width, b.measured.height]
      ]), S = [], A = void 0;
      for (const [z, F] of R)
        if (F.parentId === t && (S.push({
          id: z,
          position: { ...F.position },
          extent: F.extent
        }), F.extent === "parent" || F.expandParent)) {
          const B = wv(F, g, F.origin ?? N);
          A ? A = [
            [Math.min(B[0][0], A[0][0]), Math.min(B[0][1], A[0][1])],
            [Math.max(B[1][0], A[1][0]), Math.max(B[1][1], A[1][1])]
          ] : A = B;
        }
      h?.(k, { ...y });
    }).on("drag", (k) => {
      const { transform: R, snapGrid: L, snapToGrid: C, nodeOrigin: _ } = n(), N = Ln(k.sourceEvent, {
        transform: R,
        snapGrid: L,
        snapToGrid: C,
        containerBounds: w
      }), E = [];
      if (!g)
        return;
      const { x: T, y: $, width: z, height: F } = y, B = {}, Z = g.origin ?? _, { width: H, height: U, x: G, y: X } = yv(j, a.controlDirection, N, a.boundaries, a.keepAspectRatio, Z, I, A), P = H !== z, q = U !== F, de = G !== T && P, le = X !== $ && q;
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
      const te = mv({
        width: y.width,
        prevWidth: z,
        height: y.height,
        prevHeight: F,
        affectsX: a.controlDirection.affectsX,
        affectsY: a.controlDirection.affectsY
      }), ie = { ...y, direction: te };
      x?.(k, ie) !== !1 && (D = !0, m?.(k, ie), i(B, E));
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
var jc;
function bv() {
  if (jc) return Do;
  jc = 1;
  var e = Fe;
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
var Nc;
function jv() {
  return Nc || (Nc = 1, _o.exports = bv()), _o.exports;
}
var Sc;
function Nv() {
  if (Sc) return Ao;
  Sc = 1;
  var e = Fe, t = jv();
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
var Cc;
function Sv() {
  return Cc || (Cc = 1, Eo.exports = Nv()), Eo.exports;
}
var Cv = Sv();
const Iv = /* @__PURE__ */ ap(Cv), kv = {}, Ic = (e) => {
  let t;
  const n = /* @__PURE__ */ new Set(), i = (d, f) => {
    const p = typeof d == "function" ? d(t) : d;
    if (!Object.is(p, t)) {
      const h = t;
      t = f ?? (typeof p != "object" || p === null) ? p : Object.assign({}, t, p), n.forEach((m) => m(t, h));
    }
  }, o = () => t, u = { setState: i, getState: o, getInitialState: () => l, subscribe: (d) => (n.add(d), () => n.delete(d)), destroy: () => {
    (kv ? "production" : void 0) !== "production" && console.warn(
      "[DEPRECATED] The `destroy` method will be unsupported in a future version. Instead use unsubscribe function returned by subscribe. Everything will be garbage-collected if store is garbage-collected."
    ), n.clear();
  } }, l = t = e(i, o, u);
  return u;
}, Ev = (e) => e ? Ic(e) : Ic, { useDebugValue: Av } = Fe, { useSyncExternalStoreWithSelector: _v } = Iv, Dv = (e) => e;
function wd(e, t = Dv, n) {
  const i = _v(
    e.subscribe,
    e.getState,
    e.getServerState || e.getInitialState,
    t,
    n
  );
  return Av(i), i;
}
const kc = (e, t) => {
  const n = Ev(e), i = (o, s = t) => wd(n, o, s);
  return Object.assign(i, n), i;
}, $v = (e, t) => e ? kc(e, t) : kc;
function be(e, t) {
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
var Ec;
function Tv() {
  if (Ec) return Pe;
  Ec = 1;
  var e = Fe;
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
var Ac;
function Rv() {
  if (Ac) return $o.exports;
  Ac = 1;
  function e() {
    if (!(typeof __REACT_DEVTOOLS_GLOBAL_HOOK__ > "u" || typeof __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE != "function"))
      try {
        __REACT_DEVTOOLS_GLOBAL_HOOK__.checkDCE(e);
      } catch (t) {
        console.error(t);
      }
  }
  return e(), $o.exports = Tv(), $o.exports;
}
var Pv = Rv();
const Ar = cs(null), Mv = Ar.Provider, vd = Ke.error001("react");
function he(e, t) {
  const n = Qn(Ar);
  if (n === null)
    throw new Error(vd);
  return wd(n, e, t);
}
function je() {
  const e = Qn(Ar);
  if (e === null)
    throw new Error(vd);
  return ae(() => ({
    getState: e.getState,
    setState: e.setState,
    subscribe: e.subscribe
  }), [e]);
}
const _c = { display: "none" }, Lv = {
  position: "absolute",
  width: 1,
  height: 1,
  margin: -1,
  border: 0,
  padding: 0,
  overflow: "hidden",
  clip: "rect(0px, 0px, 0px, 0px)",
  clipPath: "inset(100%)"
}, bd = "react-flow__node-desc", jd = "react-flow__edge-desc", zv = "react-flow__aria-live", Vv = (e) => e.ariaLiveMessage, Ov = (e) => e.ariaLabelConfig;
function Hv({ rfId: e }) {
  const t = he(Vv);
  return r.jsx("div", { id: `${zv}-${e}`, "aria-live": "assertive", "aria-atomic": "true", style: Lv, children: t });
}
function Wv({ rfId: e, disableKeyboardA11y: t }) {
  const n = he(Ov);
  return r.jsxs(r.Fragment, { children: [r.jsx("div", { id: `${bd}-${e}`, style: _c, children: t ? n["node.a11yDescription.default"] : n["node.a11yDescription.keyboardDisabled"] }), r.jsx("div", { id: `${jd}-${e}`, style: _c, children: n["edge.a11yDescription.default"] }), !t && r.jsx(Hv, { rfId: e })] });
}
const _r = Il(({ position: e = "top-left", children: t, className: n, style: i, ...o }, s) => {
  const a = `${e}`.split("-");
  return r.jsx("div", { className: Ae(["react-flow__panel", n, ...a]), style: i, ref: s, ...o, children: t });
});
_r.displayName = "Panel";
function Fv({ proOptions: e, position: t = "bottom-right" }) {
  return e?.hideAttribution ? null : r.jsx(_r, { position: t, className: "react-flow__attribution", "data-message": "Please only hide this attribution when you are subscribed to React Flow Pro: https://pro.reactflow.dev", children: r.jsx("a", { href: "https://reactflow.dev", target: "_blank", rel: "noopener noreferrer", "aria-label": "React Flow attribution", children: "React Flow" }) });
}
const Bv = (e) => {
  const t = [], n = [];
  for (const [, i] of e.nodeLookup)
    i.selected && t.push(i.internals.userNode);
  for (const [, i] of e.edgeLookup)
    i.selected && n.push(i);
  return { selectedNodes: t, selectedEdges: n };
}, Mi = (e) => e.id;
function Kv(e, t) {
  return be(e.selectedNodes.map(Mi), t.selectedNodes.map(Mi)) && be(e.selectedEdges.map(Mi), t.selectedEdges.map(Mi));
}
function qv({ onSelectionChange: e }) {
  const t = je(), { selectedNodes: n, selectedEdges: i } = he(Bv, Kv);
  return Q(() => {
    const o = { nodes: n, edges: i };
    e?.(o), t.getState().onSelectionChangeHandlers.forEach((s) => s(o));
  }, [n, i, e]), null;
}
const Xv = (e) => !!e.onSelectionChangeHandlers;
function Uv({ onSelectionChange: e }) {
  const t = he(Xv);
  return e || t ? r.jsx(qv, { onSelectionChange: e }) : null;
}
const Nd = [0, 0], Yv = { x: 0, y: 0, zoom: 1 }, Zv = [
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
], Dc = [...Zv, "rfId"], Jv = (e) => ({
  setNodes: e.setNodes,
  setEdges: e.setEdges,
  setMinZoom: e.setMinZoom,
  setMaxZoom: e.setMaxZoom,
  setTranslateExtent: e.setTranslateExtent,
  setNodeExtent: e.setNodeExtent,
  reset: e.reset,
  setDefaultNodesAndEdges: e.setDefaultNodesAndEdges
}), $c = {
  /*
   * these are values that are also passed directly to other components
   * than the StoreUpdater. We can reduce the number of setStore calls
   * by setting the same values here as prev fields.
   */
  translateExtent: Un,
  nodeOrigin: Nd,
  minZoom: 0.5,
  maxZoom: 2,
  elementsSelectable: !0,
  noPanClassName: "nopan",
  rfId: "1"
};
function Gv(e) {
  const { setNodes: t, setEdges: n, setMinZoom: i, setMaxZoom: o, setTranslateExtent: s, setNodeExtent: a, reset: c, setDefaultNodesAndEdges: u } = he(Jv, be), l = je();
  Q(() => (u(e.defaultNodes, e.defaultEdges), () => {
    d.current = $c, c();
  }), []);
  const d = se($c);
  return Q(
    () => {
      for (const f of Dc) {
        const p = e[f], h = d.current[f];
        p !== h && (typeof e[f] > "u" || (f === "nodes" ? t(p) : f === "edges" ? n(p) : f === "minZoom" ? i(p) : f === "maxZoom" ? o(p) : f === "translateExtent" ? s(p) : f === "nodeExtent" ? a(p) : f === "ariaLabelConfig" ? l.setState({ ariaLabelConfig: Tw(p) }) : f === "fitView" ? l.setState({ fitViewQueued: p }) : f === "fitViewOptions" ? l.setState({ fitViewOptions: p }) : l.setState({ [f]: p })));
      }
      d.current = e;
    },
    // Only re-run the effect if one of the fields we track changes
    Dc.map((f) => e[f])
  ), null;
}
function Tc() {
  return typeof window > "u" || !window.matchMedia ? null : window.matchMedia("(prefers-color-scheme: dark)");
}
function Qv(e) {
  const [t, n] = O(e === "system" ? null : e);
  return Q(() => {
    if (e !== "system") {
      n(e);
      return;
    }
    const i = Tc(), o = () => n(i?.matches ? "dark" : "light");
    return o(), i?.addEventListener("change", o), () => {
      i?.removeEventListener("change", o);
    };
  }, [e]), t !== null ? t : Tc()?.matches ? "dark" : "light";
}
const Rc = typeof document < "u" ? document : null;
function Gn(e = null, t = { target: Rc, actInsideInputWithModifier: !0 }) {
  const [n, i] = O(!1), o = se(!1), s = se(/* @__PURE__ */ new Set([])), [a, c] = ae(() => {
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
    const u = t?.target ?? Rc, l = t?.actInsideInputWithModifier ?? !0;
    if (e !== null) {
      const d = (h) => {
        if (o.current = h.ctrlKey || h.metaKey || h.shiftKey || h.altKey, (!o.current || o.current && !l) && td(h))
          return !1;
        const v = Mc(h.code, c);
        if (s.current.add(h[v]), Pc(a, s.current, !1)) {
          const x = h.composedPath?.()?.[0] || h.target, y = x?.nodeName === "BUTTON" || x?.nodeName === "A";
          t.preventDefault !== !1 && (o.current || !y) && h.preventDefault(), i(!0);
        }
      }, f = (h) => {
        const m = Mc(h.code, c);
        Pc(a, s.current, !0) ? (i(!1), s.current.clear()) : s.current.delete(h[m]), h.key === "Meta" && s.current.clear(), o.current = !1;
      }, p = () => {
        s.current.clear(), i(!1);
      };
      return u?.addEventListener("keydown", d), u?.addEventListener("keyup", f), window.addEventListener("blur", p), window.addEventListener("contextmenu", p), () => {
        u?.removeEventListener("keydown", d), u?.removeEventListener("keyup", f), window.removeEventListener("blur", p), window.removeEventListener("contextmenu", p);
      };
    }
  }, [e, i]), n;
}
function Pc(e, t, n) {
  return e.filter((i) => n || i.length === t.size).some((i) => i.every((o) => t.has(o)));
}
function Mc(e, t) {
  return t.includes(e) ? "code" : "key";
}
const eb = () => {
  const e = je();
  return ae(() => ({
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
      const { width: i, height: o, minZoom: s, maxZoom: a, panZoom: c } = e.getState(), u = Ms(t, i, o, s, a, n?.padding ?? 0.1);
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
      return mn(l, i, f, d);
    },
    flowToScreenPosition: (t) => {
      const { transform: n, domNode: i } = e.getState();
      if (!i)
        return t;
      const { x: o, y: s } = i.getBoundingClientRect(), a = ln(t, n);
      return {
        x: a.x + o,
        y: a.y + s
      };
    }
  }), []);
};
function Sd(e, t) {
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
      tb(u, c);
    n.push(c);
  }
  return o.length && o.forEach((s) => {
    s.index !== void 0 ? n.splice(s.index, 0, { ...s.item }) : n.push({ ...s.item });
  }), n;
}
function tb(e, t) {
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
function Cd(e, t) {
  return Sd(e, t);
}
function Id(e, t) {
  return Sd(e, t);
}
function Et(e, t) {
  return {
    id: e,
    type: "select",
    selected: t
  };
}
function Jt(e, t = /* @__PURE__ */ new Set(), n = !1) {
  const i = [];
  for (const [o, s] of e) {
    const a = t.has(o);
    !(s.selected === void 0 && !a) && s.selected !== a && (n && (s.selected = a), i.push(Et(s.id, a)));
  }
  return i;
}
function Lc({ items: e = [], lookup: t }) {
  const n = [], i = new Map(e.map((o) => [o.id, o]));
  for (const [o, s] of e.entries()) {
    const a = t.get(s.id), c = a?.internals?.userNode ?? a;
    c !== void 0 && c !== s && n.push({ id: s.id, item: s, type: "replace" }), c === void 0 && n.push({ item: s, type: "add", index: o });
  }
  for (const [o] of t)
    i.get(o) === void 0 && n.push({ id: o, type: "remove" });
  return n;
}
function zc(e) {
  return {
    id: e.id,
    type: "remove"
  };
}
const kd = Ju();
function Ed(e, t, n = {}) {
  return zw(e, t, {
    ...n,
    onError: n.onError ?? kd
  });
}
function nb(e, t, n, i = { shouldReplaceId: !0 }) {
  return Vw(e, t, n, {
    ...i,
    onError: i.onError ?? kd
  });
}
const Vc = (e) => Sw(e), ib = (e) => Xu(e);
function Ad(e) {
  return Il(e);
}
const rb = typeof window < "u" ? Wf : Q;
function Oc(e) {
  const [t, n] = O(BigInt(0)), [i] = O(() => ob(() => n((o) => o + BigInt(1))));
  return rb(() => {
    const o = i.get();
    o.length && (e(o), i.reset());
  }, [t]), i;
}
function ob(e) {
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
const _d = cs(null);
function sb({ children: e }) {
  const t = je(), n = oe((c) => {
    const { nodes: u = [], setNodes: l, hasDefaultNodes: d, onNodesChange: f, nodeLookup: p, fitViewQueued: h, onNodesChangeMiddlewareMap: m } = t.getState();
    let v = u;
    for (const y of c)
      v = typeof y == "function" ? y(v) : y;
    let x = Lc({
      items: v,
      lookup: p
    });
    for (const y of m.values())
      x = y(x);
    d && l(v), x.length > 0 ? f?.(x) : h && window.requestAnimationFrame(() => {
      const { fitViewQueued: y, nodes: j, setNodes: g } = t.getState();
      y && g(j);
    });
  }, []), i = Oc(n), o = oe((c) => {
    const { edges: u = [], setEdges: l, hasDefaultEdges: d, onEdgesChange: f, edgeLookup: p } = t.getState();
    let h = u;
    for (const m of c)
      h = typeof m == "function" ? m(h) : m;
    d ? l(h) : f && f(Lc({
      items: h,
      lookup: p
    }));
  }, []), s = Oc(o), a = ae(() => ({ nodeQueue: i, edgeQueue: s }), []);
  return r.jsx(_d.Provider, { value: a, children: e });
}
function ab() {
  const e = Qn(_d);
  if (!e)
    throw new Error("useBatchContext must be used within a BatchProvider");
  return e;
}
const cb = (e) => !!e.panZoom;
function Fs() {
  const e = eb(), t = je(), n = ab(), i = he(cb), o = ae(() => {
    const s = (f) => t.getState().nodeLookup.get(f), a = (f) => {
      n.nodeQueue.push(f);
    }, c = (f) => {
      n.edgeQueue.push(f);
    }, u = (f) => {
      const { nodeLookup: p, nodeOrigin: h } = t.getState(), m = Vc(f) ? f : p.get(f.id), v = m.parentId ? Qu(m.position, m.measured, m.parentId, p, h) : m.position, x = {
        ...m,
        position: v,
        width: m.measured?.width ?? m.width,
        height: m.measured?.height ?? m.height
      };
      return cn(x);
    }, l = (f, p, h = { replace: !1 }) => {
      a((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && Vc(x) ? x : { ...v, ...x };
        }
        return v;
      }));
    }, d = (f, p, h = { replace: !1 }) => {
      c((m) => m.map((v) => {
        if (v.id === f) {
          const x = typeof p == "function" ? p(v) : p;
          return h.replace && ib(x) ? x : { ...v, ...x };
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
        const { nodes: h, edges: m, onNodesDelete: v, onEdgesDelete: x, triggerNodeChanges: y, triggerEdgeChanges: j, onDelete: g, onBeforeDelete: w } = t.getState(), { nodes: S, edges: b } = await Aw({
          nodesToRemove: f,
          edgesToRemove: p,
          nodes: h,
          edges: m,
          onBeforeDelete: w
        }), I = b.length > 0, A = S.length > 0;
        if (I) {
          const D = b.map(zc);
          x?.(b), j(D);
        }
        if (A) {
          const D = S.map(zc);
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
        const m = lc(f), v = m ? f : u(f), x = h !== void 0;
        return v ? (h || t.getState().nodes).filter((y) => {
          const j = t.getState().nodeLookup.get(y.id);
          if (j && !m && (y.id === f.id || !j.internals.positionAbsolute))
            return !1;
          const g = cn(x ? y : j), w = Zn(g, v);
          return p && w > 0 || w >= g.width * g.height || w >= v.width * v.height;
        }) : [];
      },
      isNodeIntersecting: (f, p, h = !0) => {
        const v = lc(f) ? f : u(f);
        if (!v)
          return !1;
        const x = Zn(v, p);
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
        return Cw(f, { nodeLookup: p, nodeOrigin: h });
      },
      getHandleConnections: ({ type: f, id: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}-${f}${p ? `-${p}` : ""}`)?.values() ?? []),
      getNodeConnections: ({ type: f, handleId: p, nodeId: h }) => Array.from(t.getState().connectionLookup.get(`${h}${f ? p ? `-${f}-${p}` : `-${f}` : ""}`)?.values() ?? []),
      fitView: async (f) => {
        const p = t.getState().fitViewResolver ?? $w();
        return t.setState({ fitViewQueued: !0, fitViewOptions: f, fitViewResolver: p }), n.nodeQueue.push((h) => [...h]), p.promise;
      }
    };
  }, []);
  return ae(() => ({
    ...o,
    ...e,
    viewportInitialized: i
  }), [i]);
}
const Hc = (e) => e.selected, lb = typeof window < "u" ? window : void 0;
function ub({ deleteKeyCode: e, multiSelectionKeyCode: t }) {
  const n = je(), { deleteElements: i } = Fs(), o = Gn(e, { actInsideInputWithModifier: !1 }), s = Gn(t, { target: lb });
  Q(() => {
    if (o) {
      const { edges: a, nodes: c } = n.getState();
      i({ nodes: c.filter(Hc), edges: a.filter(Hc) }), n.setState({ nodesSelectionActive: !1 });
    }
  }, [o]), Q(() => {
    n.setState({ multiSelectionActive: s });
  }, [s]);
}
function db(e) {
  const t = je();
  Q(() => {
    const n = () => {
      if (!e.current || !(e.current.checkVisibility?.() ?? !0))
        return !1;
      const i = Ls(e.current);
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
const Dr = {
  position: "absolute",
  width: "100%",
  height: "100%",
  top: 0,
  left: 0
}, fb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  lib: e.lib,
  connectionInProgress: e.connection.inProgress
});
function pb({ onPaneContextMenu: e, zoomOnScroll: t = !0, zoomOnPinch: n = !0, panOnScroll: i = !1, panOnScrollSpeed: o = 0.5, panOnScrollMode: s = Tt.Free, zoomOnDoubleClick: a = !0, panOnDrag: c = !0, defaultViewport: u, translateExtent: l, minZoom: d, maxZoom: f, zoomActivationKeyCode: p, preventScrolling: h = !0, children: m, noWheelClassName: v, noPanClassName: x, onViewportChange: y, isControlledViewport: j, paneClickDistance: g, selectionOnDrag: w }) {
  const S = je(), b = se(null), { userSelectionActive: I, lib: A, connectionInProgress: D } = he(fb, be), M = Gn(p), k = se();
  db(b);
  const R = oe((L) => {
    y?.({ x: L[0], y: L[1], zoom: L[2] }), j || S.setState({ transform: L });
  }, [y, j]);
  return Q(() => {
    if (b.current) {
      k.current = gv({
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
  ]), r.jsx("div", { className: "react-flow__renderer", ref: b, style: Dr, children: m });
}
const hb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  userSelectionRect: e.userSelectionRect
});
function gb() {
  const { userSelectionActive: e, userSelectionRect: t } = he(hb, be);
  return e && t ? r.jsx("div", { className: "react-flow__selection react-flow__container", style: {
    width: t.width,
    height: t.height,
    transform: `translate(${t.x}px, ${t.y}px)`
  } }) : null;
}
const To = (e, t) => (n) => {
  n.target === t.current && e?.(n);
}, mb = (e) => ({
  userSelectionActive: e.userSelectionActive,
  elementsSelectable: e.elementsSelectable,
  connectionInProgress: e.connection.inProgress,
  dragging: e.paneDragging,
  panBy: e.panBy,
  autoPanSpeed: e.autoPanSpeed
});
function yb({ isSelecting: e, selectionKeyPressed: t, selectionMode: n = Yn.Full, panOnDrag: i, autoPanOnSelection: o, paneClickDistance: s, selectionOnDrag: a, onSelectionStart: c, onSelectionEnd: u, onPaneClick: l, onPaneContextMenu: d, onPaneScroll: f, onPaneMouseEnter: p, onPaneMouseMove: h, onPaneMouseLeave: m, children: v }) {
  const x = se(0), y = je(), { userSelectionActive: j, elementsSelectable: g, dragging: w, connectionInProgress: S, panBy: b, autoPanSpeed: I } = he(mb, be), A = g && (e || j), D = se(null), M = se(), k = se(/* @__PURE__ */ new Set()), R = se(/* @__PURE__ */ new Set()), L = se(!1), C = se({ x: 0, y: 0 }), _ = se(!1), N = (P) => {
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
    const { x: fe, y: W } = Qe(P.nativeEvent, M.current), ne = mn({ x: fe, y: W }, de);
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
    const { transform: le, nodeLookup: te, edgeLookup: ie, connectionLookup: fe, triggerNodeChanges: W, triggerEdgeChanges: ne, defaultEdgeOptions: ge } = y.getState(), ye = { x: de.startX, y: de.startY }, { x: Ee, y: De } = ln(ye, le), Ve = {
      startX: ye.x,
      startY: ye.y,
      x: P < Ee ? P : Ee,
      y: q < De ? q : De,
      width: Math.abs(P - Ee),
      height: Math.abs(q - De)
    }, ct = k.current, Ue = R.current;
    k.current = new Set(Rs(te, Ve, le, n === Yn.Partial, !0).map((Me) => Me.id)), R.current = /* @__PURE__ */ new Set();
    const Ye = ge?.selectable ?? !0;
    for (const Me of k.current) {
      const Oe = fe.get(Me);
      if (Oe)
        for (const { edgeId: Ne } of Oe.values()) {
          const qe = ie.get(Ne);
          qe && (qe.selectable ?? Ye) && R.current.add(Ne);
        }
    }
    if (!uc(ct, k.current)) {
      const Me = Jt(te, k.current, !0);
      W(Me);
    }
    if (!uc(Ue, R.current)) {
      const Me = Jt(ie, R.current);
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
    const [P, q] = Ps(C.current, M.current, I);
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
    const { x: te, y: ie } = Qe(P.nativeEvent, M.current);
    C.current = { x: te, y: ie };
    const fe = ln({ x: q.startX, y: q.startY }, de);
    if (!L.current) {
      const W = t ? 0 : s;
      if (Math.hypot(te - fe.x, ie - fe.y) <= W)
        return;
      le(), c?.(P);
    }
    L.current = !0, _.current || (B(), _.current = !0), F(te, ie);
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
  return r.jsxs("div", { className: Ae(["react-flow__pane", { draggable: X, dragging: w, selection: e }]), onClick: A ? void 0 : To(N, D), onContextMenu: To(E, D), onWheel: To(T, D), onPointerEnter: A ? void 0 : p, onPointerMove: A ? H : h, onPointerUp: A ? U : void 0, onPointerCancel: A ? G : void 0, onPointerDownCapture: A ? z : void 0, onClickCapture: A ? $ : void 0, onPointerLeave: m, ref: D, style: Dr, children: [v, r.jsx(gb, {})] });
}
function Qo({ id: e, store: t, unselect: n = !1, nodeRef: i }) {
  const { addSelectedNodes: o, unselectNodesAndEdges: s, multiSelectionActive: a, nodeLookup: c, onError: u } = t.getState(), l = c.get(e);
  if (!l) {
    u?.("012", Ke.error012(e));
    return;
  }
  t.setState({ nodesSelectionActive: !1 }), l.selected ? (n || l.selected && a) && (s({ nodes: [l], edges: [] }), requestAnimationFrame(() => i?.current?.blur())) : o([e]);
}
function Dd({ nodeRef: e, disabled: t = !1, noDragClassName: n, handleSelector: i, nodeId: o, isSelectable: s, nodeClickDistance: a }) {
  const c = je(), [u, l] = O(!1), d = se();
  return Q(() => {
    d.current = tv({
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
const xb = (e) => (t) => t.selected && (t.draggable || e && typeof t.draggable > "u");
function $d() {
  const e = je();
  return oe((n) => {
    const { nodeExtent: i, snapToGrid: o, snapGrid: s, nodesDraggable: a, onError: c, updateNodePositions: u, nodeLookup: l, nodeOrigin: d } = e.getState(), f = /* @__PURE__ */ new Map(), p = xb(a), h = o ? s[0] : 5, m = o ? s[1] : 5, v = n.direction.x * h * n.factor, x = n.direction.y * m * n.factor;
    for (const [, y] of l) {
      if (!p(y))
        continue;
      let j = {
        x: y.internals.positionAbsolute.x + v,
        y: y.internals.positionAbsolute.y + x
      };
      o && (j = li(j, s));
      const { position: g, positionAbsolute: w } = Uu({
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
const Bs = cs(null), wb = Bs.Provider;
Bs.Consumer;
const Td = () => Qn(Bs), vb = (e) => ({
  connectOnClick: e.connectOnClick,
  noPanClassName: e.noPanClassName,
  rfId: e.rfId
}), bb = (e, t, n) => (i) => {
  const { connectionClickStartHandle: o, connectionMode: s, connection: a } = i, { fromHandle: c, toHandle: u, isValid: l } = a, d = u?.nodeId === e && u?.id === t && u?.type === n;
  return {
    connectingFrom: c?.nodeId === e && c?.id === t && c?.type === n,
    connectingTo: d,
    clickConnecting: o?.nodeId === e && o?.id === t && o?.type === n,
    isPossibleEndHandle: s === sn.Strict ? c?.type !== n : e !== c?.nodeId || t !== c?.id,
    connectionInProcess: !!c,
    clickConnectionInProcess: !!o,
    valid: d && l
  };
};
function jb({ type: e = "source", position: t = ce.Top, isValidConnection: n, isConnectable: i = !0, isConnectableStart: o = !0, isConnectableEnd: s = !0, id: a, onConnect: c, children: u, className: l, onMouseDown: d, onTouchStart: f, ...p }, h) {
  const m = a || null, v = e === "target", x = je(), y = Td(), { connectOnClick: j, noPanClassName: g, rfId: w } = he(vb, be), { connectingFrom: S, connectingTo: b, clickConnecting: I, isPossibleEndHandle: A, connectionInProcess: D, clickConnectionInProcess: M, valid: k } = he(bb(y, m, e), be);
  y || x.getState().onError?.("010", Ke.error010());
  const R = (_) => {
    const { defaultEdgeOptions: N, onConnect: E, hasDefaultEdges: T } = x.getState(), $ = {
      ...N,
      ..._
    };
    if (T) {
      const { edges: z, setEdges: F, onError: B } = x.getState();
      F(Ed($, z, { onError: B }));
    }
    E?.($), c?.($);
  }, L = (_) => {
    if (!y)
      return;
    const N = nd(_.nativeEvent);
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
    const U = ed(_.target), G = n || z, { connection: X, isValid: P } = Go.isValid(_.nativeEvent, {
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
const dn = ke(Ad(jb));
function Nb({ data: e, isConnectable: t, sourcePosition: n = ce.Bottom }) {
  return r.jsxs(r.Fragment, { children: [e?.label, r.jsx(dn, { type: "source", position: n, isConnectable: t })] });
}
function Sb({ data: e, isConnectable: t, targetPosition: n = ce.Top, sourcePosition: i = ce.Bottom }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(dn, { type: "target", position: n, isConnectable: t }), e?.label, r.jsx(dn, { type: "source", position: i, isConnectable: t })] });
}
function Cb() {
  return null;
}
function Ib({ data: e, isConnectable: t, targetPosition: n = ce.Top }) {
  return r.jsxs(r.Fragment, { children: [r.jsx(dn, { type: "target", position: n, isConnectable: t }), e?.label] });
}
const fr = {
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 },
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 }
}, Wc = {
  input: Nb,
  default: Sb,
  output: Ib,
  group: Cb
};
function kb(e) {
  return e.internals.handleBounds === void 0 ? {
    width: e.width ?? e.initialWidth ?? e.style?.width,
    height: e.height ?? e.initialHeight ?? e.style?.height
  } : {
    width: e.width ?? e.style?.width,
    height: e.height ?? e.style?.height
  };
}
const Eb = (e) => {
  const { width: t, height: n, x: i, y: o } = ci(e.nodeLookup, {
    filter: (s) => !!s.selected
  });
  return {
    width: Ge(t) ? t : null,
    height: Ge(n) ? n : null,
    userSelectionActive: e.userSelectionActive,
    transformString: `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]}) translate(${i}px,${o}px)`
  };
};
function Ab({ onSelectionContextMenu: e, noPanClassName: t, disableKeyboardA11y: n }) {
  const i = je(), { width: o, height: s, transformString: a, userSelectionActive: c } = he(Eb, be), u = $d(), l = se(null);
  Q(() => {
    n || l.current?.focus({
      preventScroll: !0
    });
  }, [n]);
  const d = !c && o !== null && s !== null;
  if (Dd({
    nodeRef: l,
    disabled: !d
  }), !d)
    return null;
  const f = e ? (h) => {
    const m = i.getState().nodes.filter((v) => v.selected);
    e(h, m);
  } : void 0, p = (h) => {
    Object.prototype.hasOwnProperty.call(fr, h.key) && (h.preventDefault(), u({
      direction: fr[h.key],
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
const Fc = typeof window < "u" ? window : void 0, _b = (e) => ({ nodesSelectionActive: e.nodesSelectionActive, userSelectionActive: e.userSelectionActive });
function Rd({ children: e, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, paneClickDistance: c, deleteKeyCode: u, selectionKeyCode: l, selectionOnDrag: d, selectionMode: f, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: m, panActivationKeyCode: v, zoomActivationKeyCode: x, elementsSelectable: y, zoomOnScroll: j, zoomOnPinch: g, panOnScroll: w, panOnScrollSpeed: S, panOnScrollMode: b, zoomOnDoubleClick: I, panOnDrag: A, autoPanOnSelection: D, defaultViewport: M, translateExtent: k, minZoom: R, maxZoom: L, preventScrolling: C, onSelectionContextMenu: _, noWheelClassName: N, noPanClassName: E, disableKeyboardA11y: T, onViewportChange: $, isControlledViewport: z }) {
  const { nodesSelectionActive: F, userSelectionActive: B } = he(_b, be), Z = Gn(l, { target: Fc }), H = Gn(v, { target: Fc }), U = H || A, G = H || w, X = d && U !== !0, P = Z || B || X;
  return ub({ deleteKeyCode: u, multiSelectionKeyCode: m }), r.jsx(pb, { onPaneContextMenu: s, elementsSelectable: y, zoomOnScroll: j, zoomOnPinch: g, panOnScroll: G, panOnScrollSpeed: S, panOnScrollMode: b, zoomOnDoubleClick: I, panOnDrag: !Z && U, defaultViewport: M, translateExtent: k, minZoom: R, maxZoom: L, zoomActivationKeyCode: x, preventScrolling: C, noWheelClassName: N, noPanClassName: E, onViewportChange: $, isControlledViewport: z, paneClickDistance: c, selectionOnDrag: X, children: r.jsxs(yb, { onSelectionStart: p, onSelectionEnd: h, onPaneClick: t, onPaneMouseEnter: n, onPaneMouseMove: i, onPaneMouseLeave: o, onPaneContextMenu: s, onPaneScroll: a, panOnDrag: U, autoPanOnSelection: D, isSelecting: !!P, selectionMode: f, selectionKeyPressed: Z, paneClickDistance: c, selectionOnDrag: X, children: [e, F && r.jsx(Ab, { onSelectionContextMenu: _, noPanClassName: E, disableKeyboardA11y: T })] }) });
}
Rd.displayName = "FlowRenderer";
const Db = ke(Rd), $b = (e) => (t) => e ? Rs(t.nodeLookup, { x: 0, y: 0, width: t.width, height: t.height }, t.transform, !0).map((n) => n.id) : Array.from(t.nodeLookup.keys());
function Tb(e) {
  return he(oe($b(e), [e]), be);
}
const Rb = (e) => e.updateNodeInternals;
function Pb() {
  const e = he(Rb), [t] = O(() => typeof ResizeObserver > "u" ? null : new ResizeObserver((n) => {
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
function Mb({ node: e, nodeType: t, hasDimensions: n, resizeObserver: i }) {
  const o = je(), s = se(null), a = se(null), c = se(e.sourcePosition), u = se(e.targetPosition), l = se(t), d = n && !!e.internals.handleBounds;
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
function Lb({ id: e, onClick: t, onMouseEnter: n, onMouseMove: i, onMouseLeave: o, onContextMenu: s, onDoubleClick: a, nodesDraggable: c, elementsSelectable: u, nodesConnectable: l, nodesFocusable: d, resizeObserver: f, noDragClassName: p, noPanClassName: h, disableKeyboardA11y: m, rfId: v, nodeTypes: x, nodeClickDistance: y, onError: j }) {
  const { node: g, internals: w, isParent: S } = he((P) => {
    const q = P.nodeLookup.get(e), de = P.parentLookup.has(e);
    return {
      node: q,
      internals: q.internals,
      isParent: de
    };
  }, be);
  let b = g.type || "default", I = x?.[b] || Wc[b];
  I === void 0 && (j?.("003", Ke.error003(b)), b = "default", I = x?.default || Wc.default);
  const A = !!(g.draggable || c && typeof g.draggable > "u"), D = !!(g.selectable || u && typeof g.selectable > "u"), M = !!(g.connectable || l && typeof g.connectable > "u"), k = !!(g.focusable || d && typeof g.focusable > "u"), R = je(), L = Gu(g), C = Mb({ node: g, nodeType: b, hasDimensions: L, resizeObserver: f }), _ = Dd({
    nodeRef: C,
    disabled: g.hidden || !A,
    noDragClassName: p,
    handleSelector: g.dragHandle,
    nodeId: e,
    isSelectable: D,
    nodeClickDistance: y
  }), N = $d();
  if (g.hidden)
    return null;
  const E = mt(g), T = kb(g), $ = D || A || t || n || i || o, z = n ? (P) => n(P, { ...w.userNode }) : void 0, F = i ? (P) => i(P, { ...w.userNode }) : void 0, B = o ? (P) => o(P, { ...w.userNode }) : void 0, Z = s ? (P) => s(P, { ...w.userNode }) : void 0, H = a ? (P) => a(P, { ...w.userNode }) : void 0, U = (P) => {
    const { selectNodesOnDrag: q, nodeDragThreshold: de } = R.getState();
    D && (!q || !A || de > 0) && Qo({
      id: e,
      store: R,
      nodeRef: C
    }), t && t(P, { ...w.userNode });
  }, G = (P) => {
    if (!(td(P.nativeEvent) || m)) {
      if (Fu.includes(P.key) && D) {
        const q = P.key === "Escape";
        Qo({
          id: e,
          store: R,
          unselect: q,
          nodeRef: C
        });
      } else if (A && g.selected && Object.prototype.hasOwnProperty.call(fr, P.key)) {
        P.preventDefault();
        const { ariaLabelConfig: q } = R.getState();
        R.setState({
          ariaLiveMessage: q["node.a11yDescription.ariaLiveMessage"]({
            direction: P.key.replace("Arrow", "").toLowerCase(),
            x: ~~w.positionAbsolute.x,
            y: ~~w.positionAbsolute.y
          })
        }), N({
          direction: fr[P.key],
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
    Rs(/* @__PURE__ */ new Map([[e, g]]), { x: 0, y: 0, width: q, height: de }, P, !0).length > 0 || te(g.position.x + E.width / 2, g.position.y + E.height / 2, {
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
  }, "data-id": e, "data-testid": `rf__node-${e}`, onMouseEnter: z, onMouseMove: F, onMouseLeave: B, onContextMenu: Z, onClick: U, onDoubleClick: H, onKeyDown: k ? G : void 0, tabIndex: k ? 0 : void 0, onFocus: k ? X : void 0, role: g.ariaRole ?? (k ? "group" : void 0), "aria-roledescription": "node", "aria-describedby": m ? void 0 : `${bd}-${v}`, "aria-label": g.ariaLabel, ...g.domAttributes, children: r.jsx(wb, { value: e, children: r.jsx(I, { id: e, data: g.data, type: b, positionAbsoluteX: w.positionAbsolute.x, positionAbsoluteY: w.positionAbsolute.y, selected: g.selected ?? !1, selectable: D, draggable: A, deletable: g.deletable ?? !0, isConnectable: M, sourcePosition: g.sourcePosition, targetPosition: g.targetPosition, dragging: _, dragHandle: g.dragHandle, zIndex: w.z, parentId: g.parentId, ...E }) }) });
}
var zb = ke(Lb);
const Vb = (e) => ({
  nodesDraggable: e.nodesDraggable,
  nodesConnectable: e.nodesConnectable,
  nodesFocusable: e.nodesFocusable,
  elementsSelectable: e.elementsSelectable,
  onError: e.onError
});
function Pd(e) {
  const { nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, onError: s } = he(Vb, be), a = Tb(e.onlyRenderVisibleElements), c = Pb();
  return r.jsx("div", { className: "react-flow__nodes", style: Dr, children: a.map((u) => (
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
    r.jsx(zb, { id: u, nodeTypes: e.nodeTypes, nodeExtent: e.nodeExtent, onClick: e.onNodeClick, onMouseEnter: e.onNodeMouseEnter, onMouseMove: e.onNodeMouseMove, onMouseLeave: e.onNodeMouseLeave, onContextMenu: e.onNodeContextMenu, onDoubleClick: e.onNodeDoubleClick, noDragClassName: e.noDragClassName, noPanClassName: e.noPanClassName, rfId: e.rfId, disableKeyboardA11y: e.disableKeyboardA11y, resizeObserver: c, nodesDraggable: t, nodesConnectable: n, nodesFocusable: i, elementsSelectable: o, nodeClickDistance: e.nodeClickDistance, onError: s }, u)
  )) });
}
Pd.displayName = "NodeRenderer";
const Ob = ke(Pd);
function Hb(e) {
  return he(oe((n) => {
    if (!e)
      return n.edges.map((o) => o.id);
    const i = [];
    if (n.width && n.height)
      for (const o of n.edges) {
        const s = n.nodeLookup.get(o.source), a = n.nodeLookup.get(o.target);
        s && a && Mw({
          sourceNode: s,
          targetNode: a,
          width: n.width,
          height: n.height,
          transform: n.transform
        }) && i.push(o.id);
      }
    return i;
  }, [e]), be);
}
const Wb = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e }
  };
  return r.jsx("polyline", { className: "arrow", style: n, strokeLinecap: "round", fill: "none", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4" });
}, Fb = ({ color: e = "none", strokeWidth: t = 1 }) => {
  const n = {
    strokeWidth: t,
    ...e && { stroke: e, fill: e }
  };
  return r.jsx("polyline", { className: "arrowclosed", style: n, strokeLinecap: "round", strokeLinejoin: "round", points: "-5,-4 0,0 -5,4 -5,-4" });
}, Bc = {
  [lr.Arrow]: Wb,
  [lr.ArrowClosed]: Fb
};
function Bb(e) {
  const t = je();
  return ae(() => Object.prototype.hasOwnProperty.call(Bc, e) ? Bc[e] : (t.getState().onError?.("009", Ke.error009(e)), null), [e]);
}
const Kb = ({ id: e, type: t, color: n, width: i = 12.5, height: o = 12.5, markerUnits: s = "strokeWidth", strokeWidth: a, orient: c = "auto-start-reverse" }) => {
  const u = Bb(t);
  return u ? r.jsx("marker", { className: "react-flow__arrowhead", id: e, markerWidth: `${i}`, markerHeight: `${o}`, viewBox: "-10 -10 20 20", markerUnits: s, orient: c, refX: "0", refY: "0", children: r.jsx(u, { color: n, strokeWidth: a }) }) : null;
}, Md = ({ defaultColor: e, rfId: t }) => {
  const n = he((s) => s.edges), i = he((s) => s.defaultEdgeOptions), o = ae(() => Bw(n, {
    id: t,
    defaultColor: e,
    defaultMarkerStart: i?.markerStart,
    defaultMarkerEnd: i?.markerEnd
  }), [n, i, t, e]);
  return o.length ? r.jsx("svg", { className: "react-flow__marker", "aria-hidden": "true", children: r.jsx("defs", { children: o.map((s) => r.jsx(Kb, { id: s.id, type: s.type, color: s.color, width: s.width, height: s.height, markerUnits: s.markerUnits, strokeWidth: s.strokeWidth, orient: s.orient }, s.id)) }) }) : null;
};
Md.displayName = "MarkerDefinitions";
var qb = ke(Md);
function Ld({ x: e, y: t, label: n, labelStyle: i, labelShowBg: o = !0, labelBgStyle: s, labelBgPadding: a = [2, 4], labelBgBorderRadius: c = 2, children: u, className: l, ...d }) {
  const [f, p] = O({ x: 1, y: 0, width: 0, height: 0 }), h = Ae(["react-flow__edge-textwrapper", l]), m = se(null);
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
Ld.displayName = "EdgeText";
const Xb = ke(Ld);
function ui({ path: e, labelX: t, labelY: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u, interactionWidth: l = 20, ...d }) {
  return r.jsxs(r.Fragment, { children: [r.jsx("path", { ...d, d: e, fill: "none", className: Ae(["react-flow__edge-path", d.className]) }), l ? r.jsx("path", { d: e, fill: "none", strokeOpacity: 0, strokeWidth: l, className: "react-flow__edge-interaction" }) : null, i && Ge(t) && Ge(n) ? r.jsx(Xb, { x: t, y: n, label: i, labelStyle: o, labelShowBg: s, labelBgStyle: a, labelBgPadding: c, labelBgBorderRadius: u }) : null] });
}
function Kc({ pos: e, x1: t, y1: n, x2: i, y2: o }) {
  return e === ce.Left || e === ce.Right ? [0.5 * (t + i), n] : [t, 0.5 * (n + o)];
}
function zd({ sourceX: e, sourceY: t, sourcePosition: n = ce.Bottom, targetX: i, targetY: o, targetPosition: s = ce.Top }) {
  const [a, c] = Kc({
    pos: n,
    x1: e,
    y1: t,
    x2: i,
    y2: o
  }), [u, l] = Kc({
    pos: s,
    x1: i,
    y1: o,
    x2: e,
    y2: t
  }), [d, f, p, h] = id({
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
function Vd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: y }) => {
    const [j, g, w] = zd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c
    }), S = e.isInternal ? void 0 : t;
    return r.jsx(ui, { id: S, path: j, labelX: g, labelY: w, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: y });
  });
}
const Ub = Vd({ isInternal: !1 }), Od = Vd({ isInternal: !0 });
Ub.displayName = "SimpleBezierEdge";
Od.displayName = "SimpleBezierEdgeInternal";
function Hd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, sourcePosition: h = ce.Bottom, targetPosition: m = ce.Top, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: j }) => {
    const [g, w, S] = dr({
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
    return r.jsx(ui, { id: b, path: g, labelX: w, labelY: S, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: v, markerStart: x, interactionWidth: j });
  });
}
const Wd = Hd({ isInternal: !1 }), Fd = Hd({ isInternal: !0 });
Wd.displayName = "SmoothStepEdge";
Fd.displayName = "SmoothStepEdgeInternal";
function Bd(e) {
  return ke(({ id: t, ...n }) => {
    const i = e.isInternal ? void 0 : t;
    return r.jsx(Wd, { ...n, id: i, pathOptions: ae(() => ({ borderRadius: 0, offset: n.pathOptions?.offset }), [n.pathOptions?.offset]) });
  });
}
const Yb = Bd({ isInternal: !1 }), Kd = Bd({ isInternal: !0 });
Yb.displayName = "StepEdge";
Kd.displayName = "StepEdgeInternal";
function qd(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v }) => {
    const [x, y, j] = ad({ sourceX: n, sourceY: i, targetX: o, targetY: s }), g = e.isInternal ? void 0 : t;
    return r.jsx(ui, { id: g, path: x, labelX: y, labelY: j, label: a, labelStyle: c, labelShowBg: u, labelBgStyle: l, labelBgPadding: d, labelBgBorderRadius: f, style: p, markerEnd: h, markerStart: m, interactionWidth: v });
  });
}
const Zb = qd({ isInternal: !1 }), Xd = qd({ isInternal: !0 });
Zb.displayName = "StraightEdge";
Xd.displayName = "StraightEdgeInternal";
function Ud(e) {
  return ke(({ id: t, sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a = ce.Bottom, targetPosition: c = ce.Top, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, pathOptions: y, interactionWidth: j }) => {
    const [g, w, S] = rd({
      sourceX: n,
      sourceY: i,
      sourcePosition: a,
      targetX: o,
      targetY: s,
      targetPosition: c,
      curvature: y?.curvature
    }), b = e.isInternal ? void 0 : t;
    return r.jsx(ui, { id: b, path: g, labelX: w, labelY: S, label: u, labelStyle: l, labelShowBg: d, labelBgStyle: f, labelBgPadding: p, labelBgBorderRadius: h, style: m, markerEnd: v, markerStart: x, interactionWidth: j });
  });
}
const Jb = Ud({ isInternal: !1 }), Yd = Ud({ isInternal: !0 });
Jb.displayName = "BezierEdge";
Yd.displayName = "BezierEdgeInternal";
const qc = {
  default: Yd,
  straight: Xd,
  step: Kd,
  smoothstep: Fd,
  simplebezier: Od
}, Xc = {
  sourceX: null,
  sourceY: null,
  targetX: null,
  targetY: null,
  sourcePosition: null,
  targetPosition: null
}, Gb = (e, t, n) => n === ce.Left ? e - t : n === ce.Right ? e + t : e, Qb = (e, t, n) => n === ce.Top ? e - t : n === ce.Bottom ? e + t : e, Uc = "react-flow__edgeupdater";
function Yc({ position: e, centerX: t, centerY: n, radius: i = 10, onMouseDown: o, onMouseEnter: s, onMouseOut: a, type: c }) {
  return r.jsx("circle", { onMouseDown: o, onMouseEnter: s, onMouseOut: a, className: Ae([Uc, `${Uc}-${c}`]), cx: Gb(t, i, e), cy: Qb(n, i, e), r: i, stroke: "transparent", fill: "transparent" });
}
function e0({ isReconnectable: e, reconnectRadius: t, edge: n, sourceX: i, sourceY: o, targetX: s, targetY: a, sourcePosition: c, targetPosition: u, onReconnect: l, onReconnectStart: d, onReconnectEnd: f, setReconnecting: p, setUpdateHover: h }) {
  const m = je(), v = (w, S) => {
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
  return r.jsxs(r.Fragment, { children: [(e === !0 || e === "source") && r.jsx(Yc, { position: c, centerX: i, centerY: o, radius: t, onMouseDown: x, onMouseEnter: j, onMouseOut: g, type: "source" }), (e === !0 || e === "target") && r.jsx(Yc, { position: u, centerX: s, centerY: a, radius: t, onMouseDown: y, onMouseEnter: j, onMouseOut: g, type: "target" })] });
}
function t0({ id: e, edgesFocusable: t, edgesReconnectable: n, elementsSelectable: i, onClick: o, onDoubleClick: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, rfId: m, edgeTypes: v, noPanClassName: x, onError: y, disableKeyboardA11y: j }) {
  let g = he((te) => te.edgeLookup.get(e));
  const w = he((te) => te.defaultEdgeOptions);
  g = w ? { ...w, ...g } : g;
  let S = g.type || "default", b = v?.[S] || qc[S];
  b === void 0 && (y?.("011", Ke.error011(S)), S = "default", b = v?.default || qc.default);
  const I = !!(g.focusable || t && typeof g.focusable > "u"), A = typeof f < "u" && (g.reconnectable || n && typeof g.reconnectable > "u"), D = !!(g.selectable || i && typeof g.selectable > "u"), M = se(null), [k, R] = O(!1), [L, C] = O(!1), _ = je(), { zIndex: N, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B } = he(oe((te) => {
    const ie = te.nodeLookup.get(g.source), fe = te.nodeLookup.get(g.target);
    if (!ie || !fe)
      return {
        zIndex: g.zIndex,
        ...Xc
      };
    const W = Fw({
      id: e,
      sourceNode: ie,
      targetNode: fe,
      sourceHandle: g.sourceHandle || null,
      targetHandle: g.targetHandle || null,
      connectionMode: te.connectionMode,
      onError: y
    });
    return {
      zIndex: Pw({
        selected: g.selected,
        zIndex: g.zIndex,
        sourceNode: ie,
        targetNode: fe,
        elevateOnSelect: te.elevateEdgesOnSelect,
        zIndexMode: te.zIndexMode
      }),
      ...W || Xc
    };
  }, [g.source, g.target, g.sourceHandle, g.targetHandle, g.selected, g.zIndex]), be), Z = ae(() => g.markerStart ? `url('#${Zo(g.markerStart, m)}')` : void 0, [g.markerStart, m]), H = ae(() => g.markerEnd ? `url('#${Zo(g.markerEnd, m)}')` : void 0, [g.markerEnd, m]);
  if (g.hidden || E === null || T === null || $ === null || z === null)
    return null;
  const U = (te) => {
    const { addSelectedEdges: ie, unselectNodesAndEdges: fe, multiSelectionActive: W } = _.getState();
    D && (_.setState({ nodesSelectionActive: !1 }), g.selected && W ? (fe({ nodes: [], edges: [g] }), M.current?.blur()) : ie([e])), o && o(te, g);
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
    if (!j && Fu.includes(te.key) && D) {
      const { unselectNodesAndEdges: ie, addSelectedEdges: fe } = _.getState();
      te.key === "Escape" ? (M.current?.blur(), ie({ edges: [g] })) : fe([e]);
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
  ]), onClick: U, onDoubleClick: G, onContextMenu: X, onMouseEnter: P, onMouseMove: q, onMouseLeave: de, onKeyDown: I ? le : void 0, tabIndex: I ? 0 : void 0, role: g.ariaRole ?? (I ? "group" : "img"), "aria-roledescription": "edge", "data-id": e, "data-testid": `rf__edge-${e}`, "aria-label": g.ariaLabel === null ? void 0 : g.ariaLabel || `Edge from ${g.source} to ${g.target}`, "aria-describedby": I ? `${jd}-${m}` : void 0, ref: M, ...g.domAttributes, children: [!L && r.jsx(b, { id: e, source: g.source, target: g.target, type: g.type, selected: g.selected, animated: g.animated, selectable: D, deletable: g.deletable ?? !0, label: g.label, labelStyle: g.labelStyle, labelShowBg: g.labelShowBg, labelBgStyle: g.labelBgStyle, labelBgPadding: g.labelBgPadding, labelBgBorderRadius: g.labelBgBorderRadius, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B, data: g.data, style: g.style, sourceHandleId: g.sourceHandle, targetHandleId: g.targetHandle, markerStart: Z, markerEnd: H, pathOptions: "pathOptions" in g ? g.pathOptions : void 0, interactionWidth: g.interactionWidth }), A && r.jsx(e0, { edge: g, isReconnectable: A, reconnectRadius: d, onReconnect: f, onReconnectStart: p, onReconnectEnd: h, sourceX: E, sourceY: T, targetX: $, targetY: z, sourcePosition: F, targetPosition: B, setUpdateHover: R, setReconnecting: C })] }) });
}
var n0 = ke(t0);
const i0 = (e) => ({
  edgesFocusable: e.edgesFocusable,
  edgesReconnectable: e.edgesReconnectable,
  elementsSelectable: e.elementsSelectable,
  connectionMode: e.connectionMode,
  onError: e.onError
});
function Zd({ defaultMarkerColor: e, onlyRenderVisibleElements: t, rfId: n, edgeTypes: i, noPanClassName: o, onReconnect: s, onEdgeContextMenu: a, onEdgeMouseEnter: c, onEdgeMouseMove: u, onEdgeMouseLeave: l, onEdgeClick: d, reconnectRadius: f, onEdgeDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, disableKeyboardA11y: v }) {
  const { edgesFocusable: x, edgesReconnectable: y, elementsSelectable: j, onError: g } = he(i0, be), w = Hb(t);
  return r.jsxs("div", { className: "react-flow__edges", children: [r.jsx(qb, { defaultColor: e, rfId: n }), w.map((S) => r.jsx(n0, { id: S, edgesFocusable: x, edgesReconnectable: y, elementsSelectable: j, noPanClassName: o, onReconnect: s, onContextMenu: a, onMouseEnter: c, onMouseMove: u, onMouseLeave: l, onClick: d, reconnectRadius: f, onDoubleClick: p, onReconnectStart: h, onReconnectEnd: m, rfId: n, onError: g, edgeTypes: i, disableKeyboardA11y: v }, S))] });
}
Zd.displayName = "EdgeRenderer";
const r0 = ke(Zd), o0 = (e) => `translate(${e.transform[0]}px,${e.transform[1]}px) scale(${e.transform[2]})`;
function s0({ children: e }) {
  const t = he(o0);
  return r.jsx("div", { className: "react-flow__viewport xyflow__viewport react-flow__container", style: { transform: t }, children: e });
}
function a0(e) {
  const t = Fs(), n = se(!1);
  Q(() => {
    !n.current && t.viewportInitialized && e && (setTimeout(() => e(t), 1), n.current = !0);
  }, [e, t.viewportInitialized]);
}
const c0 = (e) => e.panZoom?.syncViewport;
function l0(e) {
  const t = he(c0), n = je();
  return Q(() => {
    e && (t?.(e), n.setState({ transform: [e.x, e.y, e.zoom] }));
  }, [e, t]), null;
}
function u0(e) {
  return e.connection.inProgress ? { ...e.connection, to: mn(e.connection.to, e.transform) } : { ...e.connection };
}
function d0(e) {
  return u0;
}
function f0(e) {
  const t = d0();
  return he(t, be);
}
const p0 = (e) => ({
  nodesConnectable: e.nodesConnectable,
  isValid: e.connection.isValid,
  inProgress: e.connection.inProgress,
  width: e.width,
  height: e.height
});
function h0({ containerStyle: e, style: t, type: n, component: i }) {
  const { nodesConnectable: o, width: s, height: a, isValid: c, inProgress: u } = he(p0, be);
  return !(s && o && u) ? null : r.jsx("svg", { style: e, width: s, height: a, className: "react-flow__connectionline react-flow__container", children: r.jsx("g", { className: Ae(["react-flow__connection", qu(c)]), children: r.jsx(Jd, { style: t, type: n, CustomComponent: i, isValid: c }) }) });
}
const Jd = ({ style: e, type: t = wt.Bezier, CustomComponent: n, isValid: i }) => {
  const { inProgress: o, from: s, fromNode: a, fromHandle: c, fromPosition: u, to: l, toNode: d, toHandle: f, toPosition: p, pointer: h } = f0();
  if (!o)
    return;
  if (n)
    return r.jsx(n, { connectionLineType: t, connectionLineStyle: e, fromNode: a, fromHandle: c, fromX: s.x, fromY: s.y, toX: l.x, toY: l.y, fromPosition: u, toPosition: p, connectionStatus: qu(i), toNode: d, toHandle: f, pointer: h });
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
      [m] = rd(v);
      break;
    case wt.SimpleBezier:
      [m] = zd(v);
      break;
    case wt.Step:
      [m] = dr({
        ...v,
        borderRadius: 0
      });
      break;
    case wt.SmoothStep:
      [m] = dr(v);
      break;
    default:
      [m] = ad(v);
  }
  return r.jsx("path", { d: m, fill: "none", className: "react-flow__connection-path", style: e });
};
Jd.displayName = "ConnectionLine";
const g0 = {};
function Zc(e = g0) {
  se(e), je(), Q(() => {
  }, [e]);
}
function m0() {
  je(), se(!1), Q(() => {
  }, []);
}
function Gd({ nodeTypes: e, edgeTypes: t, onInit: n, onNodeClick: i, onEdgeClick: o, onNodeDoubleClick: s, onEdgeDoubleClick: a, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, onSelectionContextMenu: f, onSelectionStart: p, onSelectionEnd: h, connectionLineType: m, connectionLineStyle: v, connectionLineComponent: x, connectionLineContainerStyle: y, selectionKeyCode: j, selectionOnDrag: g, selectionMode: w, multiSelectionKeyCode: S, panActivationKeyCode: b, zoomActivationKeyCode: I, deleteKeyCode: A, onlyRenderVisibleElements: D, elementsSelectable: M, defaultViewport: k, translateExtent: R, minZoom: L, maxZoom: C, preventScrolling: _, defaultMarkerColor: N, zoomOnScroll: E, zoomOnPinch: T, panOnScroll: $, panOnScrollSpeed: z, panOnScrollMode: F, zoomOnDoubleClick: B, panOnDrag: Z, autoPanOnSelection: H, onPaneClick: U, onPaneMouseEnter: G, onPaneMouseMove: X, onPaneMouseLeave: P, onPaneScroll: q, onPaneContextMenu: de, paneClickDistance: le, nodeClickDistance: te, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ne, reconnectRadius: ge, onReconnect: ye, onReconnectStart: Ee, onReconnectEnd: De, noDragClassName: Ve, noWheelClassName: ct, noPanClassName: Ue, disableKeyboardA11y: Ye, nodeExtent: Me, rfId: Oe, viewport: Ne, onViewportChange: qe }) {
  return Zc(e), Zc(t), m0(), a0(n), l0(Ne), r.jsx(Db, { onPaneClick: U, onPaneMouseEnter: G, onPaneMouseMove: X, onPaneMouseLeave: P, onPaneContextMenu: de, onPaneScroll: q, paneClickDistance: le, deleteKeyCode: A, selectionKeyCode: j, selectionOnDrag: g, selectionMode: w, onSelectionStart: p, onSelectionEnd: h, multiSelectionKeyCode: S, panActivationKeyCode: b, zoomActivationKeyCode: I, elementsSelectable: M, zoomOnScroll: E, zoomOnPinch: T, zoomOnDoubleClick: B, panOnScroll: $, panOnScrollSpeed: z, panOnScrollMode: F, panOnDrag: Z, autoPanOnSelection: H, defaultViewport: k, translateExtent: R, minZoom: L, maxZoom: C, onSelectionContextMenu: f, preventScrolling: _, noDragClassName: Ve, noWheelClassName: ct, noPanClassName: Ue, disableKeyboardA11y: Ye, onViewportChange: qe, isControlledViewport: !!Ne, children: r.jsxs(s0, { children: [r.jsx(r0, { edgeTypes: t, onEdgeClick: o, onEdgeDoubleClick: a, onReconnect: ye, onReconnectStart: Ee, onReconnectEnd: De, onlyRenderVisibleElements: D, onEdgeContextMenu: ie, onEdgeMouseEnter: fe, onEdgeMouseMove: W, onEdgeMouseLeave: ne, reconnectRadius: ge, defaultMarkerColor: N, noPanClassName: Ue, disableKeyboardA11y: Ye, rfId: Oe }), r.jsx(h0, { style: v, type: m, component: x, containerStyle: y }), r.jsx("div", { className: "react-flow__edgelabel-renderer" }), r.jsx(Ob, { nodeTypes: e, onNodeClick: i, onNodeDoubleClick: s, onNodeMouseEnter: c, onNodeMouseMove: u, onNodeMouseLeave: l, onNodeContextMenu: d, nodeClickDistance: te, onlyRenderVisibleElements: D, noPanClassName: Ue, noDragClassName: Ve, disableKeyboardA11y: Ye, nodeExtent: Me, rfId: Oe }), r.jsx("div", { className: "react-flow__viewport-portal" })] }) });
}
Gd.displayName = "GraphView";
const y0 = ke(Gd), x0 = Ju(), Jc = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u = 0.5, maxZoom: l = 2, nodeOrigin: d, nodeExtent: f, zIndexMode: p = "basic" } = {}) => {
  const h = /* @__PURE__ */ new Map(), m = /* @__PURE__ */ new Map(), v = /* @__PURE__ */ new Map(), x = /* @__PURE__ */ new Map(), y = i ?? t ?? [], j = n ?? e ?? [], g = d ?? [0, 0], w = f ?? Un;
  ud(v, x, y);
  const { nodesInitialized: S } = Jo(j, h, m, {
    nodeOrigin: g,
    nodeExtent: w,
    zIndexMode: p
  });
  let b = [0, 0, 1];
  if (a && o && s) {
    const I = ci(h, {
      filter: (k) => !!((k.width || k.initialWidth) && (k.height || k.initialHeight))
    }), { x: A, y: D, zoom: M } = Ms(I, o, s, u, l, c?.padding ?? 0.1);
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
    translateExtent: Un,
    nodeExtent: w,
    nodesSelectionActive: !1,
    userSelectionActive: !1,
    userSelectionRect: null,
    connectionMode: sn.Strict,
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
    connection: { ...Ku },
    connectionClickStartHandle: null,
    connectOnClick: !0,
    ariaLiveMessage: "",
    autoPanOnConnect: !0,
    autoPanOnNodeDrag: !0,
    autoPanOnNodeFocus: !0,
    autoPanSpeed: 15,
    connectionRadius: 20,
    onError: x0,
    isValidConnection: void 0,
    onSelectionChangeHandlers: [],
    lib: "react",
    debug: !1,
    ariaLabelConfig: Bu,
    zIndexMode: p,
    onNodesChangeMiddlewareMap: /* @__PURE__ */ new Map(),
    onEdgesChangeMiddlewareMap: /* @__PURE__ */ new Map()
  };
}, w0 = ({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, width: o, height: s, fitView: a, fitViewOptions: c, minZoom: u, maxZoom: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p }) => $v((h, m) => {
  async function v() {
    const { nodeLookup: x, panZoom: y, fitViewOptions: j, fitViewResolver: g, width: w, height: S, minZoom: b, maxZoom: I } = m();
    y && (await Ew({
      nodes: x,
      width: w,
      height: S,
      panZoom: y,
      minZoom: b,
      maxZoom: I
    }, j), g?.resolve(!0), h({ fitViewResolver: null }));
  }
  return {
    ...Jc({
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
      ud(y, j, x), h({ edges: x });
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
      const { triggerNodeChanges: y, nodeLookup: j, parentLookup: g, domNode: w, nodeOrigin: S, nodeExtent: b, debug: I, fitViewQueued: A, zIndexMode: D } = m(), { changes: M, updatedInternals: k } = Jw(x, j, g, w, S, b, D);
      k && (Xw(j, g, { nodeOrigin: S, nodeExtent: b, zIndexMode: D }), A ? (v(), h({ fitViewQueued: !1, fitViewOptions: void 0 })) : h({}), M?.length > 0 && (I && console.log("React Flow: trigger node changes", M), y?.(M)));
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
          const C = Ft(k, b.fromHandle, ce.Left, !0);
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
        const { parentLookup: D, nodeOrigin: M } = m(), k = Ws(j, w, D, M);
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
          const b = Cd(x, g);
          j(b);
        }
        S && console.log("React Flow: trigger node changes", x), y?.(x);
      }
    },
    triggerEdgeChanges: (x) => {
      const { onEdgesChange: y, setEdges: j, edges: g, hasDefaultEdges: w, debug: S } = m();
      if (x?.length) {
        if (w) {
          const b = Id(x, g);
          j(b);
        }
        S && console.log("React Flow: trigger edge changes", x), y?.(x);
      }
    },
    addSelectedNodes: (x) => {
      const { multiSelectionActive: y, edgeLookup: j, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: S } = m();
      if (y) {
        const b = x.map((I) => Et(I, !0));
        w(b);
        return;
      }
      w(Jt(g, /* @__PURE__ */ new Set([...x]), !0)), S(Jt(j));
    },
    addSelectedEdges: (x) => {
      const { multiSelectionActive: y, edgeLookup: j, nodeLookup: g, triggerNodeChanges: w, triggerEdgeChanges: S } = m();
      if (y) {
        const b = x.map((I) => Et(I, !0));
        S(b);
        return;
      }
      S(Jt(j, /* @__PURE__ */ new Set([...x]))), w(Jt(g, /* @__PURE__ */ new Set(), !0));
    },
    unselectNodesAndEdges: ({ nodes: x, edges: y } = {}) => {
      const { edges: j, nodes: g, nodeLookup: w, triggerNodeChanges: S, triggerEdgeChanges: b } = m(), I = x || g, A = y || j, D = [];
      for (const k of I) {
        if (!k.selected)
          continue;
        const R = w.get(k.id);
        R && (R.selected = !1), D.push(Et(k.id, !1));
      }
      const M = [];
      for (const k of A)
        k.selected && M.push(Et(k.id, !1));
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
      const S = y.reduce((I, A) => A.selected ? [...I, Et(A.id, !1)] : I, []), b = x.reduce((I, A) => A.selected ? [...I, Et(A.id, !1)] : I, []);
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
      return Gw({ delta: x, panZoom: w, transform: y, translateExtent: S, width: j, height: g });
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
        connection: { ...Ku }
      });
    },
    updateConnection: (x) => {
      h({ connection: x });
    },
    reset: () => h({ ...Jc() })
  };
}, Object.is);
function v0({ initialNodes: e, initialEdges: t, defaultNodes: n, defaultEdges: i, initialWidth: o, initialHeight: s, initialMinZoom: a, initialMaxZoom: c, initialFitViewOptions: u, fitView: l, nodeOrigin: d, nodeExtent: f, zIndexMode: p, children: h }) {
  const [m] = O(() => w0({
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
  return r.jsx(Mv, { value: m, children: r.jsx(sb, { children: h }) });
}
function b0({ children: e, nodes: t, edges: n, defaultNodes: i, defaultEdges: o, width: s, height: a, fitView: c, fitViewOptions: u, minZoom: l, maxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h }) {
  return Qn(Ar) ? r.jsx(r.Fragment, { children: e }) : r.jsx(v0, { initialNodes: t, initialEdges: n, defaultNodes: i, defaultEdges: o, initialWidth: s, initialHeight: a, fitView: c, initialFitViewOptions: u, initialMinZoom: l, initialMaxZoom: d, nodeOrigin: f, nodeExtent: p, zIndexMode: h, children: e });
}
const j0 = {
  width: "100%",
  height: "100%",
  overflow: "hidden",
  position: "relative",
  zIndex: 0
};
function N0({ nodes: e, edges: t, defaultNodes: n, defaultEdges: i, className: o, nodeTypes: s, edgeTypes: a, onNodeClick: c, onEdgeClick: u, onInit: l, onMove: d, onMoveStart: f, onMoveEnd: p, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, onNodeMouseEnter: j, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: S, onNodeDoubleClick: b, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: D, onNodesDelete: M, onEdgesDelete: k, onDelete: R, onSelectionChange: L, onSelectionDragStart: C, onSelectionDrag: _, onSelectionDragStop: N, onSelectionContextMenu: E, onSelectionStart: T, onSelectionEnd: $, onBeforeDelete: z, connectionMode: F, connectionLineType: B = wt.Bezier, connectionLineStyle: Z, connectionLineComponent: H, connectionLineContainerStyle: U, deleteKeyCode: G = "Backspace", selectionKeyCode: X = "Shift", selectionOnDrag: P = !1, selectionMode: q = Yn.Full, panActivationKeyCode: de = "Space", multiSelectionKeyCode: le = Jn() ? "Meta" : "Control", zoomActivationKeyCode: te = Jn() ? "Meta" : "Control", snapToGrid: ie, snapGrid: fe, onlyRenderVisibleElements: W = !1, selectNodesOnDrag: ne, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ee, nodesFocusable: De, nodeOrigin: Ve = Nd, edgesFocusable: ct, edgesReconnectable: Ue, elementsSelectable: Ye = !0, defaultViewport: Me = Yv, minZoom: Oe = 0.5, maxZoom: Ne = 2, translateExtent: qe = Un, preventScrolling: $e = !0, nodeExtent: kt, defaultMarkerColor: xn = "#b1b1b7", zoomOnScroll: V = !0, zoomOnPinch: K = !0, panOnScroll: Y = !1, panOnScrollSpeed: re = 0.5, panOnScrollMode: ee = Tt.Free, zoomOnDoubleClick: we = !0, panOnDrag: pe = !0, onPaneClick: ve, onPaneMouseEnter: Se, onPaneMouseMove: Xe, onPaneMouseLeave: Te, onPaneScroll: Tr, onPaneContextMenu: tt, paneClickDistance: Rr = 1, nodeClickDistance: fi = 0, children: Pr, onReconnect: Mr, onReconnectStart: Lr, onReconnectEnd: zr, onEdgeContextMenu: lt, onEdgeDoubleClick: Vr, onEdgeMouseEnter: Kt, onEdgeMouseMove: Or, onEdgeMouseLeave: qt, reconnectRadius: Hr = 10, onNodesChange: pi, onEdgesChange: hi, noDragClassName: Wr = "nodrag", noWheelClassName: Xt = "nowheel", noPanClassName: gi = "nopan", fitView: mi, fitViewOptions: yi, connectOnClick: wn, attributionPosition: Fr, proOptions: Br, defaultEdgeOptions: Kr, elevateNodesOnSelect: qr = !0, elevateEdgesOnSelect: Xr = !1, disableKeyboardA11y: xi = !1, autoPanOnConnect: Ur, autoPanOnNodeDrag: Yr, autoPanOnSelection: Zr = !0, autoPanSpeed: Jr, connectionRadius: Gr, isValidConnection: Qr, onError: eo, style: to, id: vn, nodeDragThreshold: wi, connectionDragThreshold: vi, viewport: bi, onViewportChange: no, width: ji, height: io, colorMode: ro = "light", debug: oo, onScroll: Ni, ariaLabelConfig: so, zIndexMode: Si = "basic", ...ao }, co) {
  const bn = vn || "1", lo = Qv(ro), uo = oe((Ci) => {
    Ci.currentTarget.scrollTo({ top: 0, left: 0, behavior: "instant" }), Ni?.(Ci);
  }, [Ni]);
  return r.jsx("div", { "data-testid": "rf__wrapper", ...ao, onScroll: uo, style: { ...to, ...j0 }, ref: co, className: Ae(["react-flow", o, lo]), id: vn, role: "application", children: r.jsxs(b0, { nodes: e, edges: t, width: ji, height: io, fitView: mi, fitViewOptions: yi, minZoom: Oe, maxZoom: Ne, nodeOrigin: Ve, nodeExtent: kt, zIndexMode: Si, children: [r.jsx(Gv, { nodes: e, edges: t, defaultNodes: n, defaultEdges: i, onConnect: h, onConnectStart: m, onConnectEnd: v, onClickConnectStart: x, onClickConnectEnd: y, nodesDraggable: ge, autoPanOnNodeFocus: ye, nodesConnectable: Ee, nodesFocusable: De, edgesFocusable: ct, edgesReconnectable: Ue, elementsSelectable: Ye, elevateNodesOnSelect: qr, elevateEdgesOnSelect: Xr, minZoom: Oe, maxZoom: Ne, nodeExtent: kt, onNodesChange: pi, onEdgesChange: hi, snapToGrid: ie, snapGrid: fe, connectionMode: F, translateExtent: qe, connectOnClick: wn, defaultEdgeOptions: Kr, fitView: mi, fitViewOptions: yi, onNodesDelete: M, onEdgesDelete: k, onDelete: R, onNodeDragStart: I, onNodeDrag: A, onNodeDragStop: D, onSelectionDrag: _, onSelectionDragStart: C, onSelectionDragStop: N, onMove: d, onMoveStart: f, onMoveEnd: p, noPanClassName: gi, nodeOrigin: Ve, rfId: bn, autoPanOnConnect: Ur, autoPanOnNodeDrag: Yr, autoPanSpeed: Jr, onError: eo, connectionRadius: Gr, isValidConnection: Qr, selectNodesOnDrag: ne, nodeDragThreshold: wi, connectionDragThreshold: vi, onBeforeDelete: z, debug: oo, ariaLabelConfig: so, zIndexMode: Si }), r.jsx(y0, { onInit: l, onNodeClick: c, onEdgeClick: u, onNodeMouseEnter: j, onNodeMouseMove: g, onNodeMouseLeave: w, onNodeContextMenu: S, onNodeDoubleClick: b, nodeTypes: s, edgeTypes: a, connectionLineType: B, connectionLineStyle: Z, connectionLineComponent: H, connectionLineContainerStyle: U, selectionKeyCode: X, selectionOnDrag: P, selectionMode: q, deleteKeyCode: G, multiSelectionKeyCode: le, panActivationKeyCode: de, zoomActivationKeyCode: te, onlyRenderVisibleElements: W, defaultViewport: Me, translateExtent: qe, minZoom: Oe, maxZoom: Ne, preventScrolling: $e, zoomOnScroll: V, zoomOnPinch: K, zoomOnDoubleClick: we, panOnScroll: Y, panOnScrollSpeed: re, panOnScrollMode: ee, panOnDrag: pe, autoPanOnSelection: Zr, onPaneClick: ve, onPaneMouseEnter: Se, onPaneMouseMove: Xe, onPaneMouseLeave: Te, onPaneScroll: Tr, onPaneContextMenu: tt, paneClickDistance: Rr, nodeClickDistance: fi, onSelectionContextMenu: E, onSelectionStart: T, onSelectionEnd: $, onReconnect: Mr, onReconnectStart: Lr, onReconnectEnd: zr, onEdgeContextMenu: lt, onEdgeDoubleClick: Vr, onEdgeMouseEnter: Kt, onEdgeMouseMove: Or, onEdgeMouseLeave: qt, reconnectRadius: Hr, defaultMarkerColor: xn, noDragClassName: Wr, noWheelClassName: Xt, noPanClassName: gi, rfId: bn, disableKeyboardA11y: xi, nodeExtent: kt, viewport: bi, onViewportChange: no }), r.jsx(Uv, { onSelectionChange: L }), Pr, r.jsx(Fv, { proOptions: Br, position: Fr }), r.jsx(Wv, { rfId: bn, disableKeyboardA11y: xi })] }) });
}
var Ks = Ad(N0);
const S0 = (e) => e.domNode?.querySelector(".react-flow__edgelabel-renderer");
function C0({ children: e }) {
  const t = he(S0);
  return t ? Pv.createPortal(e, t) : null;
}
function I0({ dimensions: e, lineWidth: t, variant: n, className: i }) {
  return r.jsx("path", { strokeWidth: t, d: `M${e[0] / 2} 0 V${e[1]} M0 ${e[1] / 2} H${e[0]}`, className: Ae(["react-flow__background-pattern", n, i]) });
}
function k0({ radius: e, className: t }) {
  return r.jsx("circle", { cx: e, cy: e, r: e, className: Ae(["react-flow__background-pattern", "dots", t]) });
}
var bt;
(function(e) {
  e.Lines = "lines", e.Dots = "dots", e.Cross = "cross";
})(bt || (bt = {}));
const E0 = {
  [bt.Dots]: 1,
  [bt.Lines]: 1,
  [bt.Cross]: 6
}, A0 = (e) => ({ transform: e.transform, patternId: `pattern-${e.rfId}` });
function Qd({
  id: e,
  variant: t = bt.Dots,
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
  const f = se(null), { transform: p, patternId: h } = he(A0, be), m = i || E0[t], v = t === bt.Dots, x = t === bt.Cross, y = Array.isArray(n) ? n : [n, n], j = [y[0] * p[2] || 1, y[1] * p[2] || 1], g = m * p[2], w = Array.isArray(s) ? s : [s, s], S = x ? [g, g] : j, b = [
    w[0] * p[2] || 1 + S[0] / 2,
    w[1] * p[2] || 1 + S[1] / 2
  ], I = `${h}${e || ""}`;
  return r.jsxs("svg", { className: Ae(["react-flow__background", l]), style: {
    ...u,
    ...Dr,
    "--xy-background-color-props": c,
    "--xy-background-pattern-color-props": a
  }, ref: f, "data-testid": "rf__background", children: [r.jsx("pattern", { id: I, x: p[0] % j[0], y: p[1] % j[1], width: j[0], height: j[1], patternUnits: "userSpaceOnUse", patternTransform: `translate(-${b[0]},-${b[1]})`, children: v ? r.jsx(k0, { radius: g / 2, className: d }) : r.jsx(I0, { dimensions: S, lineWidth: o, variant: t, className: d }) }), r.jsx("rect", { x: "0", y: "0", width: "100%", height: "100%", fill: `url(#${I})` })] });
}
Qd.displayName = "Background";
const qs = ke(Qd);
function _0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 32", children: r.jsx("path", { d: "M32 18.133H18.133V32h-4.266V18.133H0v-4.266h13.867V0h4.266v13.867H32z" }) });
}
function D0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 5", children: r.jsx("path", { d: "M0 0h32v4.2H0z" }) });
}
function $0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 32 30", children: r.jsx("path", { d: "M3.692 4.63c0-.53.4-.938.939-.938h5.215V0H4.708C2.13 0 0 2.054 0 4.63v5.216h3.692V4.631zM27.354 0h-5.2v3.692h5.17c.53 0 .984.4.984.939v5.215H32V4.631A4.624 4.624 0 0027.354 0zm.954 24.83c0 .532-.4.94-.939.94h-5.215v3.768h5.215c2.577 0 4.631-2.13 4.631-4.707v-5.139h-3.692v5.139zm-23.677.94c-.531 0-.939-.4-.939-.94v-5.138H0v5.139c0 2.577 2.13 4.707 4.708 4.707h5.138V25.77H4.631z" }) });
}
function T0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0 8 0 4.571 3.429 4.571 7.619v3.048H3.048A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047zm4.724-13.866H7.467V7.619c0-2.59 2.133-4.724 4.723-4.724 2.591 0 4.724 2.133 4.724 4.724v3.048z" }) });
}
function R0() {
  return r.jsx("svg", { xmlns: "http://www.w3.org/2000/svg", viewBox: "0 0 25 32", children: r.jsx("path", { d: "M21.333 10.667H19.81V7.619C19.81 3.429 16.38 0 12.19 0c-4.114 1.828-1.37 2.133.305 2.438 1.676.305 4.42 2.59 4.42 5.181v3.048H3.047A3.056 3.056 0 000 13.714v15.238A3.056 3.056 0 003.048 32h18.285a3.056 3.056 0 003.048-3.048V13.714a3.056 3.056 0 00-3.048-3.047zM12.19 24.533a3.056 3.056 0 01-3.047-3.047 3.056 3.056 0 013.047-3.048 3.056 3.056 0 013.048 3.048 3.056 3.056 0 01-3.048 3.047z" }) });
}
function Li({ children: e, className: t, ...n }) {
  return r.jsx("button", { type: "button", className: Ae(["react-flow__controls-button", t]), ...n, children: e });
}
const P0 = (e) => ({
  isInteractive: e.nodesDraggable || e.nodesConnectable || e.elementsSelectable,
  minZoomReached: e.transform[2] <= e.minZoom,
  maxZoomReached: e.transform[2] >= e.maxZoom,
  ariaLabelConfig: e.ariaLabelConfig
});
function ef({ style: e, showZoom: t = !0, showFitView: n = !0, showInteractive: i = !0, fitViewOptions: o, onZoomIn: s, onZoomOut: a, onFitView: c, onInteractiveChange: u, className: l, children: d, position: f = "bottom-left", orientation: p = "vertical", "aria-label": h }) {
  const m = je(), { isInteractive: v, minZoomReached: x, maxZoomReached: y, ariaLabelConfig: j } = he(P0, be), { zoomIn: g, zoomOut: w, fitView: S } = Fs(), b = () => {
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
  return r.jsxs(_r, { className: Ae(["react-flow__controls", M, l]), position: f, style: e, "data-testid": "rf__controls", "aria-label": h ?? j["controls.ariaLabel"], children: [t && r.jsxs(r.Fragment, { children: [r.jsx(Li, { onClick: b, className: "react-flow__controls-zoomin", title: j["controls.zoomIn.ariaLabel"], "aria-label": j["controls.zoomIn.ariaLabel"], disabled: y, children: r.jsx(_0, {}) }), r.jsx(Li, { onClick: I, className: "react-flow__controls-zoomout", title: j["controls.zoomOut.ariaLabel"], "aria-label": j["controls.zoomOut.ariaLabel"], disabled: x, children: r.jsx(D0, {}) })] }), n && r.jsx(Li, { className: "react-flow__controls-fitview", onClick: A, title: j["controls.fitView.ariaLabel"], "aria-label": j["controls.fitView.ariaLabel"], children: r.jsx($0, {}) }), i && r.jsx(Li, { className: "react-flow__controls-interactive", onClick: D, title: j["controls.interactive.ariaLabel"], "aria-label": j["controls.interactive.ariaLabel"], children: v ? r.jsx(R0, {}) : r.jsx(T0, {}) }), d] });
}
ef.displayName = "Controls";
const Xs = ke(ef);
function M0({ id: e, x: t, y: n, width: i, height: o, style: s, color: a, strokeColor: c, strokeWidth: u, className: l, borderRadius: d, shapeRendering: f, selected: p, onClick: h }) {
  const { background: m, backgroundColor: v } = s || {}, x = a || m || v;
  return r.jsx("rect", { className: Ae(["react-flow__minimap-node", { selected: p }, l]), x: t, y: n, rx: d, ry: d, width: i, height: o, style: {
    fill: x,
    stroke: c,
    strokeWidth: u
  }, shapeRendering: f, onClick: h ? (y) => h(y, e) : void 0 });
}
const L0 = ke(M0), z0 = (e) => e.nodes.map((t) => t.id), Ro = (e) => e instanceof Function ? e : () => e;
function V0({
  nodeStrokeColor: e,
  nodeColor: t,
  nodeClassName: n = "",
  nodeBorderRadius: i = 5,
  nodeStrokeWidth: o,
  /*
   * We need to rename the prop to be `CapitalCase` so that JSX will render it as
   * a component properly.
   */
  nodeComponent: s = L0,
  onClick: a
}) {
  const c = he(z0, be), u = Ro(t), l = Ro(e), d = Ro(n), f = typeof window > "u" || window.chrome ? "crispEdges" : "geometricPrecision";
  return r.jsx(r.Fragment, { children: c.map((p) => (
    /*
     * The split of responsibilities between MiniMapNodes and
     * NodeComponentWrapper may appear weird. However, it’s designed to
     * minimize the cost of updates when individual nodes change.
     *
     * For more details, see a similar commit in `NodeRenderer/index.tsx`.
     */
    r.jsx(H0, { id: p, nodeColorFunc: u, nodeStrokeColorFunc: l, nodeClassNameFunc: d, nodeBorderRadius: i, nodeStrokeWidth: o, NodeComponent: s, onClick: a, shapeRendering: f }, p)
  )) });
}
function O0({ id: e, nodeColorFunc: t, nodeStrokeColorFunc: n, nodeClassNameFunc: i, nodeBorderRadius: o, nodeStrokeWidth: s, shapeRendering: a, NodeComponent: c, onClick: u }) {
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
  }, be);
  return !l || l.hidden || !Gu(l) ? null : r.jsx(c, { x: d, y: f, width: p, height: h, style: l.style, selected: !!l.selected, className: i(l), color: t(l), borderRadius: o, strokeColor: n(l), strokeWidth: s, shapeRendering: a, onClick: u, id: l.id });
}
const H0 = ke(O0);
var W0 = ke(V0);
const F0 = 200, B0 = 150, K0 = (e) => !e.hidden, q0 = (e) => {
  const t = {
    x: -e.transform[0] / e.transform[2],
    y: -e.transform[1] / e.transform[2],
    width: e.width / e.transform[2],
    height: e.height / e.transform[2]
  };
  return {
    viewBB: t,
    boundingRect: e.nodeLookup.size > 0 ? Zu(ci(e.nodeLookup, { filter: K0 }), t) : t,
    rfId: e.rfId,
    panZoom: e.panZoom,
    translateExtent: e.translateExtent,
    flowWidth: e.width,
    flowHeight: e.height,
    ariaLabelConfig: e.ariaLabelConfig
  };
}, X0 = "react-flow__minimap-desc";
function tf({
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
  const S = je(), b = se(null), { boundingRect: I, viewBB: A, rfId: D, panZoom: M, translateExtent: k, flowWidth: R, flowHeight: L, ariaLabelConfig: C } = he(q0, be), _ = e?.width ?? F0, N = e?.height ?? B0, E = I.width / _, T = I.height / N, $ = Math.max(E, T), z = $ * _, F = $ * N, B = w * $, Z = I.x - (z - I.width) / 2 - B, H = I.y - (F - I.height) / 2 - B, U = z + B * 2, G = F + B * 2, X = `${X0}-${D}`, P = se(0), q = se();
  P.current = $, Q(() => {
    if (b.current && M)
      return q.current = av({
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
  const de = h ? (ie) => {
    const [fe, W] = q.current?.pointer(ie) || [0, 0];
    h(ie, { x: fe, y: W });
  } : void 0, le = m ? oe((ie, fe) => {
    const W = S.getState().nodeLookup.get(fe).internals.userNode;
    m(ie, W);
  }, []) : void 0, te = y ?? C["minimap.ariaLabel"];
  return r.jsx(_r, { position: p, style: {
    ...e,
    "--xy-minimap-background-color-props": typeof u == "string" ? u : void 0,
    "--xy-minimap-mask-background-color-props": typeof l == "string" ? l : void 0,
    "--xy-minimap-mask-stroke-color-props": typeof d == "string" ? d : void 0,
    "--xy-minimap-mask-stroke-width-props": typeof f == "number" ? f * $ : void 0,
    "--xy-minimap-node-background-color-props": typeof i == "string" ? i : void 0,
    "--xy-minimap-node-stroke-color-props": typeof n == "string" ? n : void 0,
    "--xy-minimap-node-stroke-width-props": typeof a == "number" ? a : void 0
  }, className: Ae(["react-flow__minimap", t]), "data-testid": "rf__minimap", children: r.jsxs("svg", { width: _, height: N, viewBox: `${Z} ${H} ${U} ${G}`, className: "react-flow__minimap-svg", role: "img", "aria-labelledby": X, ref: b, onClick: de, children: [te && r.jsx("title", { id: X, children: te }), r.jsx(W0, { onClick: le, nodeColor: i, nodeStrokeColor: n, nodeBorderRadius: s, nodeClassName: o, nodeStrokeWidth: a, nodeComponent: c }), r.jsx("path", { className: "react-flow__minimap-mask", d: `M${Z - B},${H - B}h${U + B * 2}v${G + B * 2}h${-U - B * 2}z
        M${A.x},${A.y}h${A.width}v${A.height}h${-A.width}z`, fillRule: "evenodd", pointerEvents: "none" })] }) });
}
tf.displayName = "MiniMap";
const Us = ke(tf), U0 = (e) => (t) => e ? `${Math.max(1 / t.transform[2], 1)}` : void 0, Y0 = {
  [un.Line]: "right",
  [un.Handle]: "bottom-right"
};
function Z0({ nodeId: e, position: t, variant: n = un.Handle, className: i, style: o = void 0, children: s, color: a, minWidth: c = 10, minHeight: u = 10, maxWidth: l = Number.MAX_VALUE, maxHeight: d = Number.MAX_VALUE, keepAspectRatio: f = !1, resizeDirection: p, autoScale: h = !0, shouldResize: m, onResizeStart: v, onResize: x, onResizeEnd: y }) {
  const j = Td(), g = typeof e == "string" ? e : j, w = je(), S = se(null), b = n === un.Handle, I = he(oe(U0(b && h), [b, h]), be), A = se(null), D = t ?? Y0[n];
  Q(() => {
    if (!(!S.current || !g))
      return A.current || (A.current = vv({
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
                ...Qu({
                  x: k.x ?? $.position.x,
                  y: k.y ?? $.position.y
                }, { width: F, height: B }, $.parentId, C, z)
              }
            }, H = Ws([Z], C, _, N);
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
ke(Z0);
function J0(e, t) {
  return {
    ...t ? { name: t } : {},
    definitionId: e.definitionId,
    state: ni(e.state),
    layout: e.layout
  };
}
function G0(e) {
  return JSON.stringify(
    {
      state: ni(e.state),
      layout: e.layout
    },
    null,
    2
  );
}
function Q0(e, t) {
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
      state: yr(i.state),
      layout: i.layout ?? t.layout
    }
  };
}
function ej(e, t) {
  const n = (t ?? "workflow").trim().replace(/[^\w.-]+/g, "-") || "workflow", i = new Blob([JSON.stringify(e, null, 2)], { type: "application/json" }), o = URL.createObjectURL(i), s = document.createElement("a");
  s.href = o, s.download = `${n}.json`, document.body.appendChild(s), s.click(), s.remove(), URL.revokeObjectURL(o);
}
function Gc({
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
function tj({
  document: e,
  diagnostics: t = [],
  readOnly: n = !1,
  theme: i = "studio",
  minHeight: o = "220px",
  ariaLabel: s,
  languageAdapter: a,
  onChange: c
}) {
  const u = t.filter((p) => !p.uri || p.uri === e.uri), l = a?.displayName ?? e.language, d = a?.loadEditor, f = ae(
    () => d ? Ff(d) : null,
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
        f ? /* @__PURE__ */ r.jsx(Bf, { fallback: /* @__PURE__ */ r.jsx(
          Gc,
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
          Gc,
          {
            document: e,
            readOnly: n,
            minHeight: o,
            ariaLabel: s,
            onChange: c
          }
        ),
        /* @__PURE__ */ r.jsx(nj, { diagnostics: u })
      ]
    }
  );
}
function nj({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ r.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((t, n) => {
    const i = t.severity ?? "info", o = ij(t);
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
function ij(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const rj = { language: "json", displayName: "JSON" };
function oj({ draft: e, onApply: t }) {
  const n = ae(() => G0(e), [e]), [i, o] = O(n), [s, a] = O(n), [c, u] = O(null);
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
          /* @__PURE__ */ r.jsx(fn, { size: 14 }),
          " Apply"
        ] })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-code-view-editor", children: /* @__PURE__ */ r.jsx(
      tj,
      {
        ariaLabel: "Workflow JSON",
        document: { uri: "elsa://workflows/definition.json", language: "json", value: i },
        languageAdapter: rj,
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
const sj = ["Single", "Array", "List", "HashSet"];
function nf(e) {
  const [t, n] = O(null), [i, o] = O(null);
  Q(() => {
    let u = !1;
    return Jh(e).then(
      (l) => {
        u || n(l);
      },
      () => {
        u || n([]);
      }
    ), Qh(e).then(
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
  const s = ae(
    () => t && t.length > 0 ? t.map((u) => {
      const l = ja(u);
      return {
        value: l,
        label: Ml(u.displayName, l),
        group: u.category?.trim() || "Other"
      };
    }) : null,
    [t]
  ), a = ae(
    () => i && i.length > 0 ? i.filter((u) => !u.deprecated).map((u) => ({
      value: u.typeName,
      label: Sp(u.displayName, u.typeName)
    })) : null,
    [i]
  ), c = ae(() => {
    const u = /* @__PURE__ */ new Map();
    for (const l of t ?? []) {
      const d = ja(l), f = l.defaultEditor?.trim();
      d && u.set(d, f && f.length > 0 ? f : "text");
    }
    return (l) => u.get(l) ?? "text";
  }, [t]);
  return { typeOptions: s, storageOptions: a, editorForAlias: c };
}
function aj(e) {
  return !e || e.length === 0 ? void 0 : (e.find((n) => /(^|\.)String$/i.test(n.value) || n.label.toLowerCase() === "string") ?? e[0]).value;
}
function cj(e, t, n) {
  return {
    add: () => {
      const i = hp(n.namePrefix, e.map((o) => dt(o, n.nameKeys)));
      t([...e, n.create(i)]);
    },
    update: (i, o) => t(e.map((s, a) => a === i ? n.patch(s, o) : s)),
    remove: (i) => t(e.filter((o, s) => s !== i))
  };
}
function Qc({ value: e, options: t, placeholder: n, allowEmpty: i, ariaLabel: o, onChange: s }) {
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
const lj = {
  Single: "Single",
  Array: "Array",
  List: "List",
  HashSet: "Hash set"
};
function uj({ value: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("select", { "aria-label": t, value: e, onChange: (i) => n(i.target.value), children: sj.map((i) => /* @__PURE__ */ r.jsx("option", { value: i, children: lj[i] }, i)) });
}
function dj(e, t) {
  return t === "" ? !0 : e === "checkbox" ? t === "true" || t === "false" || t === "True" || t === "False" : e === "number" ? Number.isFinite(Number(t.trim())) && t.trim() !== "" : e === "date" ? /^\d{4}-\d{2}-\d{2}$/.test(t) : !0;
}
function fj({ value: e, editor: t, ariaLabel: n, onChange: i }) {
  const o = dj(t, e);
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
function pj({ title: e, addLabel: t, emptyLabel: n, headers: i, isEmpty: o, onAdd: s, children: a }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-properties-section", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-section-head", children: [
      /* @__PURE__ */ r.jsx("h3", { children: e }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-properties-add", onClick: s, children: [
        /* @__PURE__ */ r.jsx(en, { size: 14 }),
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
function hj({ label: e, onRemove: t }) {
  return /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-properties-remove", "aria-label": e, title: e, onClick: t, children: /* @__PURE__ */ r.jsx(Pt, { size: 14 }) }) });
}
function gj({ checked: e, ariaLabel: t, onChange: n }) {
  return /* @__PURE__ */ r.jsx("input", { type: "checkbox", "aria-label": t, checked: e, onChange: (i) => n(i.target.checked) });
}
function Ys({
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
  const { add: m, update: v, remove: x } = cj(e, h, {
    namePrefix: o,
    nameKeys: s,
    create: (g) => l(g, aj(t)),
    patch: d
  }), y = ["Name", "Type", "Collection", ...f.default ? ["Default"] : [], ...f.storage ? ["Storage"] : [], ...f.required ? ["Required"] : []], j = o.toLowerCase();
  return /* @__PURE__ */ r.jsx(
    pj,
    {
      title: a,
      addLabel: c,
      emptyLabel: u,
      headers: y,
      isEmpty: e.length === 0,
      onAdd: m,
      children: e.map((g, w) => {
        const S = dt(g, s), b = gs(g), I = dt(g, Ll), A = I ? p?.get(I) : void 0, D = b.collectionKind === "Single" ? i(b.alias) : "text";
        return /* @__PURE__ */ r.jsxs("tr", { children: [
          /* @__PURE__ */ r.jsxs("td", { children: [
            /* @__PURE__ */ r.jsx("input", { type: "text", "aria-label": `${o} name`, value: S, onChange: (M) => v(w, { name: M.target.value }) }),
            A ? /* @__PURE__ */ r.jsx("span", { className: "wf-properties-warning", role: "note", title: A, children: A }) : null
          ] }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            Qc,
            {
              ariaLabel: `${o} type`,
              value: b.alias,
              options: t,
              placeholder: "Type",
              onChange: (M) => v(w, { type: { alias: M, collectionKind: b.collectionKind } })
            }
          ) }),
          /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            uj,
            {
              ariaLabel: `${o} collection kind`,
              value: b.collectionKind,
              onChange: (M) => v(w, { type: { alias: b.alias, collectionKind: M } })
            }
          ) }),
          f.default ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            fj,
            {
              ariaLabel: `${o} default value`,
              value: xp(g.default),
              editor: D,
              onChange: (M) => v(w, { default: yp(M) })
            }
          ) }) : null,
          f.storage ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            Qc,
            {
              ariaLabel: `${o} storage driver`,
              value: dt(g, Ip),
              options: n,
              placeholder: "—",
              allowEmpty: !0,
              onChange: (M) => v(w, { storageDriverType: M || null })
            }
          ) }) : null,
          f.required ? /* @__PURE__ */ r.jsx("td", { children: /* @__PURE__ */ r.jsx(
            gj,
            {
              ariaLabel: `${o} required`,
              checked: g.isRequired === !0,
              onChange: (M) => v(w, { isRequired: M })
            }
          ) }) : null,
          /* @__PURE__ */ r.jsx(hj, { label: `Remove ${j} ${S || w + 1}`, onRemove: () => x(w) })
        ] }, w);
      })
    }
  );
}
function rf({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, title: o = "Variables", addLabel: s = "Add variable", emptyLabel: a = "No variables defined.", warnings: c, onChange: u }) {
  return /* @__PURE__ */ r.jsx(
    Ys,
    {
      items: e,
      typeOptions: t,
      storageOptions: n,
      editorForAlias: i,
      namePrefix: "Variable",
      nameKeys: Cp,
      title: o,
      addLabel: s,
      emptyLabel: a,
      create: (l, d) => gp({ name: l, alias: d }),
      patch: (l, d) => mp(l, d),
      columns: { default: !0, storage: !0 },
      warnings: c,
      onChange: u
    }
  );
}
function mj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Ys,
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
      create: (s, a) => wp({ name: s, alias: a }),
      patch: (s, a) => vp(s, a),
      columns: { default: !1, storage: !0, required: !0 },
      onChange: o
    }
  );
}
function yj({ items: e, typeOptions: t, storageOptions: n, editorForAlias: i, onChange: o }) {
  return /* @__PURE__ */ r.jsx(
    Ys,
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
      create: (s, a) => bp({ name: s, alias: a }),
      patch: (s, a) => jp(s, a),
      columns: { default: !1, storage: !1 },
      onChange: o
    }
  );
}
function Ui(e) {
  return (e ?? []).filter(On);
}
function xj({ context: e, variables: t, title: n, addLabel: i, emptyLabel: o, warnings: s, onChange: a }) {
  const { typeOptions: c, storageOptions: u, editorForAlias: l } = nf(e);
  return /* @__PURE__ */ r.jsx(
    rf,
    {
      items: Ui(t),
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
function wj({ definition: e, definitionId: t, onMetaChange: n }) {
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
function vj({ details: e, draft: t, context: n, onStateChange: i, onDefinitionMetaChange: o }) {
  const { typeOptions: s, storageOptions: a, editorForAlias: c } = nf(n), u = Ui(t.state.variables), l = Ui(t.state.inputs), d = Ui(t.state.outputs), f = e?.versions ?? [];
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties-view", children: [
    /* @__PURE__ */ r.jsx(
      wj,
      {
        definition: e?.definition,
        definitionId: t.definitionId,
        onMetaChange: o
      }
    ),
    /* @__PURE__ */ r.jsx(
      rf,
      {
        items: u,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, variables: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      mj,
      {
        items: l,
        typeOptions: s,
        storageOptions: a,
        editorForAlias: c,
        onChange: (p) => i((h) => ({ ...h, inputs: p }))
      }
    ),
    /* @__PURE__ */ r.jsx(
      yj,
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
        /* @__PURE__ */ r.jsx("time", { children: _e(p.createdAt) })
      ] }, p.id)) })
    ] })
  ] });
}
const el = "application/x-elsa-activity-version-id", bj = 6, jj = 1200, Nj = 250, Sj = [10, 25, 50], Cj = 10, tl = "elsa-studio-workflow-palette-width", nl = "elsa-studio-workflow-inspector-width", il = "elsa-studio-workflow-palette-collapsed", rl = "elsa-studio-workflow-inspector-collapsed", of = "elsa-studio-workflow-side-panel-maximized", Dn = 180, $n = 460, Ij = 260, _t = 260, Dt = 560, kj = 320, ol = 42, zi = 16, sf = Fe.createContext(null), af = Fe.createContext(null), cf = Fe.createContext(null);
function Ej(e, t, n) {
  return e.slice((t - 1) * n, t * n);
}
function lf(e, t) {
  return Math.max(1, Math.ceil(e / t));
}
function Nt(e, t) {
  return e.promptActions.list().find((n) => n.id === t) ?? null;
}
function St(e, t, n) {
  const i = t.createPrompt(n);
  return i ? (e.dispatchPrompt(i), !0) : !1;
}
function Aj(e) {
  if (!e) return null;
  const t = [], n = e.match(/```(?:json)?\s*([\s\S]*?)```/i);
  n && t.push(n[1]);
  const i = e.match(/\{[\s\S]*?\}/);
  i && t.push(i[0]);
  for (const a of t)
    try {
      const c = JSON.parse(a.trim()), u = sl(c.name, c.description);
      if (u) return u;
    } catch {
    }
  const o = e.match(/name\s*[:-]\s*(.+)/i)?.[1], s = e.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return sl(o, s);
}
function sl(e, t) {
  const n = typeof e == "string" ? al(e) : void 0, i = typeof t == "string" ? al(t) : void 0;
  return n || i ? { name: n || void 0, description: i || void 0 } : null;
}
function al(e) {
  return e.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}
function _j(e, t) {
  return e.rootActivityVersionId ?? uf(t, e.rootKind)?.activityVersionId ?? null;
}
function uf(e, t) {
  return e.find((n) => Dj(n) === t);
}
function Dj(e) {
  return e ? $j(e) ? "flowchart" : Tj(e) ? "sequence" : null : null;
}
function es(e) {
  return nu(e, (t) => t.category).map((t) => ({
    category: t.category,
    activities: t.items.sort((n, i) => Ie(n).localeCompare(Ie(i)))
  }));
}
function $j(e) {
  return Ie(e) === "Flowchart" || e.activityTypeKey.endsWith(".Flowchart");
}
function Tj(e) {
  return Ie(e) === "Sequence" || e.activityTypeKey.endsWith(".Sequence");
}
function Rj(e) {
  return e.isBrowsable !== !1 && e.browsable !== !1;
}
function df(e) {
  return Wj(e.rootActivityType) || e.rootActivityType;
}
function Pj(e, t) {
  return [
    e.definitionId,
    e.definitionVersionId,
    e.sourceId,
    e.sourceVersion
  ].some((n) => n?.toLowerCase().includes(t));
}
function Mj(e, t) {
  return e.definitionId === t || e.sourceId === t;
}
function Lj(e, t) {
  return cl(t) - cl(e);
}
function cl(e) {
  const t = e.publishedAt ?? e.createdAt, n = t ? new Date(t).getTime() : 0;
  return Number.isNaN(n) ? 0 : n;
}
function Zs(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return !t || t === "definition" || t === "workflowdefinition" ? "Definition" : t === "definitionversion" || t === "workflowdefinitionversion" ? "Definition version" : e.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/[-_]+/g, " ").replace(/\s+/g, " ").trim().replace(/\b\w/g, (n) => n.toUpperCase());
}
function Js(e) {
  const t = e?.workflowExecutionId ?? e?.runId ?? e?.executionId;
  return typeof t == "string" && t.trim() ? t : null;
}
function Gs(e) {
  const t = e instanceof Error ? e.message : String(e), n = (typeof e == "object" && e && "payload" in e ? e.payload : null) ?? zj(t), i = n && typeof n == "object" && typeof n.error == "string" ? n.error : null;
  return i ? /no live .* reference/i.test(i) ? /expired/i.test(i) ? `${i} Start a new test run from the definition editor to mint a fresh reference.` : `${i} Restore the executable or republish its definition to run it.` : i : t;
}
function zj(e) {
  try {
    return JSON.parse(e);
  } catch {
    return null;
  }
}
function ff(e) {
  const t = e?.trim().toLowerCase() ?? "";
  return t === "published" ? "Published" : t === "testrun" || t === "test-run" ? "Test run" : e?.trim() || "Unknown";
}
function Vj(e, t = /* @__PURE__ */ new Date()) {
  return e.deletedAt ? "retired" : e.expiresAt && new Date(e.expiresAt).getTime() <= t.getTime() ? "expired" : "live";
}
function Oj(e, t) {
  return t ? !t.latestVersionId || t.latestVersionId === e.definitionVersionId ? { kind: "current" } : {
    kind: "behind",
    referenceVersion: e.sourceVersion ?? e.artifactVersion ?? null,
    latestVersion: t.latestVersion ?? null
  } : { kind: "absent" };
}
async function Hj(e) {
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
function Wj(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function Fj(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    Vi(t, n.typeName, n), Vi(t, n.name, n), Vi(t, n.displayName, n);
    const i = n.typeName.split(".").filter(Boolean).at(-1);
    Vi(t, i, n);
  }
  return t;
}
function ll(e, t, n) {
  const i = t.get(e.activityVersionId);
  return n.get(Tn(i?.activityTypeKey)) ?? n.get(Tn(zt(i?.activityTypeKey))) ?? n.get(Tn(i?.displayName)) ?? n.get(Tn(e.activityVersionId)) ?? null;
}
function Vi(e, t, n) {
  const i = Tn(t);
  i && !e.has(i) && e.set(i, n);
}
function Tn(e) {
  return e?.trim().toLowerCase() ?? "";
}
function ul(e, t, n, i) {
  const o = $r();
  if (!o) return t;
  const s = o.getItem(e);
  if (s == null) return t;
  const a = Number(s);
  return Number.isFinite(a) ? Yi(a, n, i) : t;
}
function dl(e, t) {
  const n = $r();
  if (!n) return t;
  const i = n.getItem(e);
  return i === "true" ? !0 : i === "false" ? !1 : t;
}
function Bj() {
  const e = $r();
  if (!e) return null;
  const t = e.getItem(of);
  return t === "palette" || t === "inspector" ? t : null;
}
function $r() {
  if (typeof window > "u") return null;
  const e = window.localStorage;
  return e && typeof e.getItem == "function" && typeof e.setItem == "function" && typeof e.removeItem == "function" ? e : null;
}
function Cn(e, t) {
  const n = $r();
  n && (t == null ? n.removeItem(e) : n.setItem(e, t));
}
function Yi(e, t, n) {
  return Math.min(n, Math.max(t, Math.round(e)));
}
function pf(e) {
  switch (Kj(e)) {
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
function Kj(e) {
  return (e ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}
function qj(e) {
  return e.authoredActivityId || e.executableNodeId;
}
function ts(e) {
  return `${Ie(e).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity"}-${crypto.randomUUID().slice(0, 8)}`;
}
function fl(e) {
  return { x: e.position.x + 280, y: e.position.y };
}
function Xj(e, t) {
  return {
    x: Math.round((e.position.x + t.position.x) / 2),
    y: Math.round((e.position.y + t.position.y) / 2)
  };
}
function hf(e) {
  return "changedTouches" in e && e.changedTouches.length > 0 ? { x: e.changedTouches[0].clientX, y: e.changedTouches[0].clientY } : { x: e.clientX, y: e.clientY };
}
function Uj(e) {
  const t = hf(e);
  return !!(document.elementFromPoint?.(t.x, t.y) ?? e.target)?.closest(".react-flow__handle, .react-flow__node");
}
function Yj(e, t) {
  if (e) return e;
  const n = t.fromNode?.id;
  return n ? { nodeId: n, handleId: t.fromHandle?.id ?? null } : null;
}
function We(e) {
  return JSON.stringify({ state: e.state, layout: e.layout });
}
function Zj(e) {
  return yf(We(e));
}
function Jj(e, t) {
  const n = t?.get(e.activityVersionId);
  return n ? Ie(n) : zt(e.activityVersionId) ?? e.activityVersionId;
}
function Gj(e, t) {
  if (e.cardinality === "single") {
    const i = e.activities[0];
    return i ? Jj(i, t) : "Empty — click to choose";
  }
  const n = e.activities.length;
  return `${n} activit${n === 1 ? "y" : "ies"}`;
}
function gf(e, t, n = []) {
  if (!e) return n;
  const i = t.get(e.activityVersionId);
  n.push({
    id: e.nodeId,
    type: i?.activityTypeKey ?? e.activityVersionId,
    displayName: i ? Ie(i) : void 0
  });
  for (const o of Re(e, t))
    for (const s of o.activities) gf(s, t, n);
  return n;
}
function mf(e, t, n = []) {
  if (!e) return n;
  for (const i of Xl(e))
    n.push({ source: i.source, target: i.target, sourcePort: i.sourceHandle ?? void 0, targetPort: i.targetHandle ?? void 0 });
  for (const i of Re(e, t))
    for (const o of i.activities) mf(o, t, n);
  return n;
}
function Rn(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function Qj(e) {
  return `${e.id}-${yf(JSON.stringify(e.state))}`;
}
function yf(e) {
  let t = 2166136261;
  for (let n = 0; n < e.length; n += 1)
    t ^= e.charCodeAt(n), t = Math.imul(t, 16777619);
  return (t >>> 0).toString(16).padStart(8, "0");
}
function Qs(e) {
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
    return [i.error, i.title, i.detail].some((o) => typeof o == "string" && /not found/i.test(o));
  } catch {
    return /not found/i.test(t);
  }
}
function yn({ status: e, subStatus: t }) {
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge", "data-status": e.toLowerCase(), children: t ? `${e} · ${t}` : e });
}
const ea = { workflowActivity: iN }, ta = { workflow: oN };
function iN({ id: e, data: t, selected: n }) {
  const i = t, o = i.runtime, s = !i.suppressFlowPorts, a = s ? i.sourcePorts.length > 0 ? i.sourcePorts : [{ name: "Done", displayName: "Done" }] : [], c = rN(i), l = Fe.useContext(af)?.({ activityVersionId: i.activityVersionId, activityTypeKey: i.activityTypeKey }) ?? null, d = Fe.useContext(cf), f = i.onEnterSlot ?? (d ? (p) => d(e, i.label, p) : void 0);
  return /* @__PURE__ */ r.jsxs(
    "div",
    {
      className: ["wf-node", n ? "selected" : "", o ? "wf-node-runtime" : "", o?.hasBlockingIncident ? "faulted" : "", l ? "wf-node-unavailable" : "", i.ghost ? "wf-node-ghost" : ""].filter(Boolean).join(" "),
      "data-icon": i.icon ?? "activity",
      children: [
        s && i.acceptsInbound ? /* @__PURE__ */ r.jsx(dn, { type: "target", position: ce.Left }) : null,
        l ? /* @__PURE__ */ r.jsx("span", { className: "wf-node-availability", title: `No longer available for new use · ${Wn(l.state)}`, children: /* @__PURE__ */ r.jsx(Vn, { size: 13 }) }) : null,
        /* @__PURE__ */ r.jsxs("div", { className: "wf-node-content", children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-node-icon", "aria-hidden": "true", children: vr(i.icon) }),
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
          o.status ? /* @__PURE__ */ r.jsx(yn, { status: o.status, subStatus: o.subStatus }) : null,
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
          return /* @__PURE__ */ r.jsxs(Fe.Fragment, { children: [
            /* @__PURE__ */ r.jsx("span", { className: "wf-node-port-label", style: { top: m }, children: p.displayName }),
            /* @__PURE__ */ r.jsx(dn, { type: "source", position: ce.Right, id: p.name, style: { top: m } })
          ] }, p.name);
        })
      ]
    }
  );
}
function rN(e) {
  const t = e.category?.trim(), n = e.executionType?.trim();
  return [t, n].filter((o) => !!o).join(" · ");
}
function oN(e) {
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
  } = e, p = Fe.useContext(sf), [h, m] = O(!1), [v, x, y] = dr({ sourceX: n, sourceY: i, targetX: o, targetY: s, sourcePosition: a, targetPosition: c }), j = p?.highlightedEdgeId === t;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx(
      ui,
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
    p ? /* @__PURE__ */ r.jsx(C0, { children: /* @__PURE__ */ r.jsxs(
      "div",
      {
        className: ["wf-edge-actions", h ? "visible" : "", j ? "highlighted" : ""].filter(Boolean).join(" "),
        style: { transform: `translate(-50%, -50%) translate(${x}px, ${y}px)` },
        onMouseEnter: () => m(!0),
        onMouseLeave: () => m(!1),
        children: [
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Insert activity into connection", title: "Insert activity", onClick: (g) => p.requestInsertActivity(t, g.clientX, g.clientY), children: /* @__PURE__ */ r.jsx(en, { size: 12 }) }),
          /* @__PURE__ */ r.jsx("button", { type: "button", "aria-label": "Delete connection", title: "Delete connection", onClick: () => p.deleteEdge(t), children: /* @__PURE__ */ r.jsx(Pt, { size: 12 }) })
        ]
      }
    ) }) : null
  ] });
}
function xf({ clientX: e, clientY: t, activities: n, onPick: i, onClose: o }) {
  const [s, a] = O(""), [c, u] = O(0), l = se(null), d = se(null), f = ae(() => {
    const j = s.trim().toLowerCase(), g = n.filter(Rj);
    return j ? g.filter((w) => Ie(w).toLowerCase().includes(j) || w.activityTypeKey.toLowerCase().includes(j) || (w.category ?? "").toLowerCase().includes(j) || (w.description ?? "").toLowerCase().includes(j)) : g;
  }, [n, s]), p = ae(() => es(f), [f]), h = ae(() => p.flatMap((j) => j.activities), [p]);
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
function zn({
  label: e,
  tabs: t,
  activeTabId: n,
  onSelect: i
}) {
  const o = sp(t.map((s) => s.id), n, i);
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
function pl(e, t) {
  return e.order - t.order || e.title.localeCompare(t.title);
}
function na({ frames: e, onNavigate: t, className: n }) {
  return /* @__PURE__ */ r.jsxs("div", { className: n ? `wf-breadcrumb ${n}` : "wf-breadcrumb", children: [
    /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t([]), children: "Root" }),
    e.map((i, o) => i.label ? /* @__PURE__ */ r.jsxs(Fe.Fragment, { children: [
      /* @__PURE__ */ r.jsx(pt, { size: 13 }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(e.slice(0, o + 1)), children: i.label })
    ] }, `${i.ownerNodeId}-${i.slotId}-${o}`) : null)
  ] });
}
const sN = "Expressions/UnresolvedVariable";
function aN(e) {
  return String(e.type ?? e.code ?? "");
}
function cN(e) {
  return aN(e) === sN;
}
function lN(e) {
  const t = String(e ?? "").trim().split("/").filter(Boolean), [n, i, ...o] = t, s = n === "$workflow";
  return {
    nodeId: !n || s ? null : n,
    isWorkflowScope: s,
    bag: i === "inputs" || i === "outputs" || i === "variables" ? i : null,
    referenceKey: o.length > 0 ? o.join("/") : null
  };
}
function uN(e) {
  return (e ?? []).filter(cN).map((t) => ({
    error: t,
    path: lN(t.path),
    message: t.message ?? "Variable reference is not visible from this activity's scope."
  }));
}
function dN({ draft: e, onRepair: t }) {
  const n = e.validationErrors;
  if (!n.length)
    return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation ok", children: [
      /* @__PURE__ */ r.jsx(fn, { size: 14 }),
      " No validation errors"
    ] });
  const i = uN(n), o = new Map(i.map((s) => [s.error, s]));
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-validation", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-validation-summary", children: [
      /* @__PURE__ */ r.jsx(It, { size: 14 }),
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
          /* @__PURE__ */ r.jsx(Zf, { size: 12 }),
          " Repair"
        ] }) : null
      ] }, a);
    }) })
  ] });
}
function fN({
  testRun: e,
  onOpenDetails: t
}) {
  const n = Qs(e);
  return /* @__PURE__ */ r.jsx("div", { className: "wf-test-run-status", "data-state": n ? "rejected" : "accepted", children: /* @__PURE__ */ r.jsxs(
    "button",
    {
      type: "button",
      className: "wf-test-run-trigger",
      onClick: t,
      children: [
        n ? /* @__PURE__ */ r.jsx(It, { size: 16 }) : /* @__PURE__ */ r.jsx(fn, { size: 16 }),
        n ? "Test run rejected" : "Test run dispatched"
      ]
    }
  ) });
}
function pN({ testRun: e, onOpenRun: t }) {
  if (!e)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Run the draft to see Runtime Evidence." }) });
  const n = Qs(e), i = e.workflowExecutionId;
  return /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-panel", children: /* @__PURE__ */ r.jsxs("section", { className: "wf-runtime-card", "data-state": n ? "rejected" : "accepted", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("span", { children: "Latest Test Run" }),
        /* @__PURE__ */ r.jsx("h3", { children: n ? "Rejected by the server" : "Transient run accepted" })
      ] }),
      /* @__PURE__ */ r.jsx(yn, { status: e.status, subStatus: e.commandDispatchStatus ?? void 0 })
    ] }),
    /* @__PURE__ */ r.jsx("p", { children: "Ephemeral - not saved, promoted, or published." }),
    n && e.reason ? /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-reason", children: [
      /* @__PURE__ */ r.jsx(It, { size: 14 }),
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
        /* @__PURE__ */ r.jsx("dd", { title: i ?? "None", children: i ? /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => t(i), children: i }) : "None" })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Activities" }),
        /* @__PURE__ */ r.jsx("dd", { children: hl(e.activityCount, "activity") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: hl(e.incidentCount, "incident") })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ r.jsx("dd", { title: e.expiresAt ? _e(e.expiresAt) : "None", children: e.expiresAt ? _e(e.expiresAt) : "None" })
      ] })
    ] })
  ] }) });
}
function hl(e, t) {
  return typeof e != "number" ? "Available on linked Run" : `${e} ${t}${e === 1 ? "" : "s"}`;
}
function ia({ rows: e = 5 }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-grid", "aria-busy": "true", "aria-label": "Loading", children: Array.from({ length: e }).map((t, n) => /* @__PURE__ */ r.jsx("div", { className: "wf-skeleton wf-skeleton-row", style: { width: `${90 - n % 3 * 12}%` } }, n)) });
}
function ra({ icon: e, title: t, description: n, action: i }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-empty-state", role: "status", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-icon", "aria-hidden": !0, children: e ?? /* @__PURE__ */ r.jsx(ei, { size: 22 }) }),
    /* @__PURE__ */ r.jsx("h3", { children: t }),
    n ? /* @__PURE__ */ r.jsx("p", { children: n }) : null,
    i ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty-state-action", children: i }) : null
  ] });
}
function Ct({ message: e, title: t = "Something went wrong" }) {
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card", role: "alert", children: [
    /* @__PURE__ */ r.jsx(It, { size: 18 }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-error-card-body", children: [
      /* @__PURE__ */ r.jsx("strong", { children: t }),
      /* @__PURE__ */ r.jsx("span", { children: e || "Please try again, or check that the Elsa server is reachable." })
    ] })
  ] });
}
function oa({ status: e, run: t, compact: n = !1 }) {
  const i = () => {
    t?.workflowExecutionId && (window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(t.workflowExecutionId)}`), window.dispatchEvent(new PopStateEvent("popstate")));
  };
  return /* @__PURE__ */ r.jsxs("div", { className: `wf-status-line${n ? " compact" : ""}`, children: [
    /* @__PURE__ */ r.jsx(fn, { size: n ? 13 : 14 }),
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
      await Hj(e), i(n);
    } catch {
      o(n);
    }
  };
  return /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-copy-button", "aria-label": t, title: t, onClick: (a) => {
    s(a);
  }, children: /* @__PURE__ */ r.jsx(Jf, { size: 12 }) });
}
function wf({ references: e, activeReferenceId: t, onSelect: n, ariaLabel: i = "Source references" }) {
  return e.length === 0 ? /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "No source references." }) : /* @__PURE__ */ r.jsx("ul", { className: "wf-reference-list", "aria-label": i, children: e.map((o) => {
    const s = Vj(o), a = o.sourceReferenceId === t, c = /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
      /* @__PURE__ */ r.jsxs("span", { className: "wf-reference-heading", children: [
        /* @__PURE__ */ r.jsxs("strong", { children: [
          "Version ",
          o.artifactVersion
        ] }),
        /* @__PURE__ */ r.jsx("span", { className: `wf-chip wf-reference-scope-${o.scope.toLowerCase()}`, children: ff(o.scope) }),
        s !== "live" ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip wf-reference-retired", children: s === "retired" ? "Retired" : "Expired" }) : null,
        a ? /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Shown" }) : null
      ] }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-reference-meta", children: [
        /* @__PURE__ */ r.jsx("small", { children: o.publishedAt ? `Published ${_e(o.publishedAt)}` : `Created ${_e(o.createdAt)}` }),
        o.expiresAt ? /* @__PURE__ */ r.jsxs("small", { children: [
          "Expires ",
          _e(o.expiresAt)
        ] }) : null,
        o.deletedReason ? /* @__PURE__ */ r.jsx("small", { children: o.deletedReason }) : null
      ] })
    ] });
    return /* @__PURE__ */ r.jsx("li", { className: "wf-reference-item", "data-active": a ? "true" : void 0, children: n ? /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-reference-select", "aria-label": `Show reference ${o.sourceReferenceId}`, onClick: () => n(o), children: c }) : /* @__PURE__ */ r.jsx("span", { className: "wf-reference-select", children: c }) }, o.sourceReferenceId);
  }) });
}
function vf(e, t) {
  window.history.pushState({}, "", `/workflows/executables/${encodeURIComponent(e)}`), window.dispatchEvent(new PopStateEvent("popstate"));
}
function hN({ context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: i }) {
  const [o, s] = O("loading"), [a, c] = O(""), [u, l] = O(""), [d, f] = O(null), [p, h] = O([]), [m, v] = O("published"), [x, y] = O(!1), [j, g] = O(/* @__PURE__ */ new Set()), w = n?.trim().toLowerCase() ?? "", S = ae(
    () => w ? p.filter((N) => Pj(N, w)) : p,
    [w, p]
  ), b = ae(
    () => Array.from(new Set(p.flatMap((N) => [
      N.definitionId,
      N.definitionVersionId,
      N.sourceId
    ]).filter((N) => !!N))).sort((N, E) => N.localeCompare(E)),
    [p]
  ), I = Nt(t, "weaver.workflows.explain-executable"), A = oe(async () => {
    s("loading"), c("");
    try {
      h(await eu(e, { scope: m, includeRetired: x })), s("ready");
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
      const E = await bs(e, N.artifactId), T = Js(E);
      f({ artifactId: N.artifactId, workflowExecutionId: T }), l(`Started ${N.artifactId}`);
    } catch (E) {
      c(Gs(E));
    }
  }, M = (N) => {
    I && St(t, I, N) && (c(""), f(null), l(`Sent ${N.artifactId} to Weaver`));
  }, k = async (N) => {
    if (await Ji().confirm({ message: `Delete executable ${N.artifactId}? This retires all of its source references; the artifact can no longer be run.`, confirmLabel: "Delete", tone: "danger" })) {
      l(""), f(null), c("");
      try {
        await Ql(e, N.artifactId), l(`Deleted ${N.artifactId}`), await A();
      } catch (E) {
        c(E instanceof Error ? E.message : String(E));
      }
    }
  }, R = async (N) => {
    l(""), f(null), c("");
    try {
      await Oh(e, N.artifactId), l(`Restored ${N.artifactId}`), await A();
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
        /* @__PURE__ */ r.jsx(hr, { size: 14 }),
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
    o === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: a }) : null,
    o === "ready" && a ? /* @__PURE__ */ r.jsx(Ct, { message: a }) : null,
    u ? /* @__PURE__ */ r.jsx(oa, { status: u, run: d }) : null,
    o === "loading" ? /* @__PURE__ */ r.jsx(ia, {}) : null,
    o === "ready" && S.length === 0 ? /* @__PURE__ */ r.jsx(
      ra,
      {
        icon: /* @__PURE__ */ r.jsx(Rt, { size: 22 }),
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
            /* @__PURE__ */ r.jsx(gN, { executable: N, onCopied: C, onCopyFailed: _ }),
            /* @__PURE__ */ r.jsx("span", { children: df(N) }),
            /* @__PURE__ */ r.jsx("span", { children: _e(N.publishedAt ?? N.createdAt) }),
            /* @__PURE__ */ r.jsxs("span", { className: "wf-row-actions", children: [
              /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Inspect executable ${N.artifactId}`, onClick: () => vf(N.artifactId), children: [
                /* @__PURE__ */ r.jsx(_l, { size: 13 }),
                " Inspect"
              ] }),
              /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                D(N);
              }, children: [
                /* @__PURE__ */ r.jsx(Rt, { size: 13 }),
                " Run"
              ] }),
              I ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => M(N), children: [
                /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                " Explain"
              ] }) : null,
              $ ? /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Restore executable ${N.artifactId}`, onClick: () => {
                R(N);
              }, children: [
                /* @__PURE__ */ r.jsx(ti, { size: 13 }),
                " Restore"
              ] }) : /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Delete executable ${N.artifactId}`, onClick: () => {
                k(N);
              }, children: [
                /* @__PURE__ */ r.jsx(Pt, { size: 13 }),
                " Delete"
              ] })
            ] })
          ] }),
          T ? /* @__PURE__ */ r.jsx("div", { className: "wf-executable-references", children: /* @__PURE__ */ r.jsx(wf, { references: E, ariaLabel: `References of ${N.artifactId}` }) }) : null
        ] }, N.artifactId);
      })
    ] }) : null
  ] });
}
function gN({ executable: e, onCopied: t, onCopyFailed: n }) {
  const i = e.sourceId || e.definitionVersionId || e.definitionId, o = e.sourceVersion, s = e.definitionId, a = () => {
    window.history.pushState({}, "", `/workflows/definitions?definition=${encodeURIComponent(s)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return /* @__PURE__ */ r.jsxs("span", { className: "wf-source-cell", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-source-kind", children: Zs(e.sourceKind) }),
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
function mN({ context: e, ai: t, definitionId: n, publishedArtifactId: i }) {
  const [o, s] = O("loading"), [a, c] = O(""), [u, l] = O(""), [d, f] = O(null), [p, h] = O([]), m = Nt(t, "weaver.workflows.explain-executable"), v = oe(async () => {
    s("loading"), c("");
    try {
      const b = await eu(e);
      h(b.filter((I) => Mj(I, n)).sort(Lj)), s("ready");
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
      f({ artifactId: b.artifactId, workflowExecutionId: Js(I) }), l(`Started ${b.artifactId}`);
    } catch (I) {
      c(Gs(I));
    }
  }, y = (b) => {
    m && St(t, m, b) && (c(""), f(null), l(`Sent ${b.artifactId} to Weaver`));
  }, j = async (b) => {
    if (await Ji().confirm({ message: `Delete executable ${b.artifactId} for this workflow? This retires this definition's references to it.`, confirmLabel: "Delete", tone: "danger" })) {
      l(""), f(null), c("");
      try {
        await Ql(e, b.artifactId, n), l(`Deleted ${b.artifactId}`), await v();
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
        /* @__PURE__ */ r.jsx(ti, { size: 13 }),
        " Refresh"
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: g, children: "Open list" })
    ] }),
    o === "failed" || a ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert compact", children: [
      /* @__PURE__ */ r.jsx(It, { size: 14 }),
      " ",
      a
    ] }) : null,
    u ? /* @__PURE__ */ r.jsx(oa, { status: u, run: d, compact: !0 }) : null,
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
        /* @__PURE__ */ r.jsx("span", { children: _e(b.publishedAt ?? b.createdAt) })
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
            Zs(b.sourceKind),
            " ",
            b.sourceVersion ? `v${b.sourceVersion}` : ""
          ] })
        ] }),
        /* @__PURE__ */ r.jsxs("div", { children: [
          /* @__PURE__ */ r.jsx("dt", { children: "Root" }),
          /* @__PURE__ */ r.jsx("dd", { children: df(b) })
        ] })
      ] }),
      /* @__PURE__ */ r.jsxs("div", { className: "wf-row-actions", children: [
        /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Inspect executable ${b.artifactId}`, onClick: () => vf(b.artifactId), children: [
          /* @__PURE__ */ r.jsx(_l, { size: 13 }),
          " Inspect"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
          x(b);
        }, children: [
          /* @__PURE__ */ r.jsx(Rt, { size: 13 }),
          " Run"
        ] }),
        m ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => y(b), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", "aria-label": `Delete executable ${b.artifactId} for this workflow`, onClick: () => {
          j(b);
        }, children: [
          /* @__PURE__ */ r.jsx(Pt, { size: 13 }),
          " Delete"
        ] })
      ] })
    ] }, b.artifactId)) }) : null
  ] });
}
function sa() {
  const [e, t] = O(() => ul(tl, Ij, Dn, $n)), [n, i] = O(() => ul(nl, kj, _t, Dt)), [o, s] = O(() => dl(il, !1)), [a, c] = O(() => dl(rl, !1)), [u, l] = O(Bj);
  Q(() => {
    Cn(tl, String(e));
  }, [e]), Q(() => {
    Cn(nl, String(n));
  }, [n]), Q(() => {
    Cn(il, String(o));
  }, [o]), Q(() => {
    Cn(rl, String(a));
  }, [a]), Q(() => {
    Cn(of, u);
  }, [u]), Q(() => {
    if (!u) return;
    const g = (w) => {
      w.key === "Escape" && l(null);
    };
    return window.addEventListener("keydown", g), () => window.removeEventListener("keydown", g);
  }, [u]);
  const d = oe((g) => {
    l((w) => w === g ? null : w), g === "palette" ? s((w) => !w) : c((w) => !w);
  }, []), f = oe((g) => {
    g === "palette" ? s(!1) : c(!1), l((w) => w === g ? null : g);
  }, []), p = oe((g, w) => {
    l(null), g === "palette" ? (s(!1), t((S) => Yi(S + w, Dn, $n))) : (c(!1), i((S) => Yi(S + w, _t, Dt)));
  }, []), h = oe((g, w) => {
    w.preventDefault(), l(null), g === "palette" ? s(!1) : c(!1);
    const S = w.clientX, b = g === "palette" ? e : n, I = g === "palette" ? Dn : _t, A = g === "palette" ? $n : Dt;
    document.body.classList.add("wf-side-panel-resizing");
    const D = (k) => {
      const R = g === "palette" ? k.clientX - S : S - k.clientX, L = Yi(b + R, I, A);
      g === "palette" ? t(L) : i(L);
    }, M = () => {
      document.body.classList.remove("wf-side-panel-resizing"), window.removeEventListener("pointermove", D), window.removeEventListener("pointerup", M), window.removeEventListener("pointercancel", M);
    };
    window.addEventListener("pointermove", D), window.addEventListener("pointerup", M), window.addEventListener("pointercancel", M);
  }, [n, e]), m = oe((g, w) => {
    w.key === "ArrowLeft" ? (w.preventDefault(), p(g, g === "palette" ? -zi : zi)) : w.key === "ArrowRight" ? (w.preventDefault(), p(g, g === "palette" ? zi : -zi)) : w.key === "Home" ? (w.preventDefault(), g === "palette" ? t(Dn) : i(_t)) : w.key === "End" && (w.preventDefault(), g === "palette" ? t($n) : i(Dt));
  }, [p]), v = !o && u !== "inspector", x = !a && u !== "palette", y = [
    "wf-editor-body",
    o ? "palette-collapsed" : "",
    a ? "inspector-collapsed" : "",
    u === "palette" ? "palette-maximized" : "",
    u === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" "), j = {
    "--wf-palette-width": `${o ? ol : e}px`,
    "--wf-inspector-width": `${a ? ol : n}px`
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
const yN = 50;
function gl() {
  return { past: [], future: [] };
}
function xN(e) {
  return e.past.length > 0;
}
function wN(e) {
  return e.future.length > 0;
}
function ml(e, t, n = yN) {
  const i = [...e.past, t];
  return i.length > n && i.splice(0, i.length - n), { past: i, future: [] };
}
function vN(e, t) {
  if (e.past.length === 0) return null;
  const n = e.past.slice(), i = n.pop();
  return { history: { past: n, future: [...e.future, t] }, snapshot: i };
}
function bN(e, t) {
  if (e.future.length === 0) return null;
  const n = e.future.slice(), i = n.pop();
  return { history: { past: [...e.past, t], future: n }, snapshot: i };
}
function jN({ draft: e, restoreDraft: t }) {
  const n = se(gl()), i = se(null), o = se(""), s = se(!1), [a, c] = O(0), u = oe((v) => {
    n.current = gl(), i.current = v ? Rn(v) : null, o.current = v ? We(v) : "", s.current = !1, c(0);
  }, []);
  Q(() => {
    if (!e) return;
    if (s.current) {
      s.current = !1;
      return;
    }
    const v = We(e);
    if (v === o.current) return;
    const x = window.setTimeout(() => {
      const y = i.current;
      y && (n.current = ml(n.current, y), c((j) => j + 1)), i.current = Rn(e), o.current = v;
    }, Nj);
    return () => window.clearTimeout(x);
  }, [e]);
  const l = oe(() => {
    if (!e) return;
    const v = We(e);
    if (v === o.current) return;
    const x = i.current;
    x && (n.current = ml(n.current, x)), i.current = Rn(e), o.current = v;
  }, [e]), d = oe((v) => {
    s.current = !0, i.current = Rn(v), o.current = We(v), t(v), c((x) => x + 1);
  }, [t]), f = oe(() => {
    if (!e) return;
    l();
    const v = vN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), p = oe(() => {
    if (!e) return;
    l();
    const v = bN(n.current, e);
    v && (n.current = v.history, d(v.snapshot));
  }, [e, l, d]), { canUndoNow: h, canRedoNow: m } = ae(() => {
    const v = !!e && !!i.current && We(e) !== o.current;
    return {
      canUndoNow: xN(n.current) || v,
      canRedoNow: wN(n.current) && !v
    };
  }, [e, a]);
  return { resetHistory: u, undo: f, redo: p, canUndoNow: h, canRedoNow: m };
}
const NN = {
  draft: null,
  frames: [],
  selectedNodeId: null,
  testRun: null,
  publishedArtifactId: null
};
function SN(e, t) {
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
function CN() {
  const [e, t] = Kf(SN, NN), n = ae(() => ({
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
const IN = 320, kN = 140;
function EN(e, t, n) {
  return n === "sequence" ? AN(e) : _N(e, t);
}
function AN(e) {
  const t = /* @__PURE__ */ new Map();
  return e.forEach((n, i) => {
    t.set(n.id, { x: i * 280, y: 0 });
  }), t;
}
function _N(e, t) {
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
      n.set(p, { x: d * IN, y: h * kN });
    });
  return n;
}
function DN(e, t, n, i, o) {
  if (!e) return { kind: "becomeRoot" };
  const s = nn(e, t, o);
  if (!s)
    return t.length > 0 ? { kind: "staleFrames" } : Re(n, i)[0] ? { kind: "wrapRoot" } : { kind: "leafError" };
  const a = s.slot.cardinality === "single" ? s.slot.activities.find((c) => c.nodeId !== n.nodeId) ?? null : null;
  return { kind: "addToSlot", slot: s.slot, replacedActivity: a };
}
const $N = "root";
function TN(e) {
  return e.length === 0 ? $N : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function RN(e, t) {
  return e.length === t.length && e.every((n, i) => n.id === t[i]);
}
function PN({
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
  const [x, y] = O([]), [j, g] = O([]), [w, S] = O(null), [b, I] = O(null), [A, D] = O(null), M = se(null), k = se(null), R = se(/* @__PURE__ */ new Map()), L = se(/* @__PURE__ */ new Set()), C = se(null), _ = se([]), N = se(null), E = se(null), T = se(!1), $ = ae(() => TN(i), [i]);
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
    if (!w || C.current !== $ || !RN(x, _.current)) return;
    C.current = null;
    const V = R.current.get($), K = L.current.has($);
    L.current.add($), window.requestAnimationFrame(() => {
      V ? w.setViewport(V) : !K && x.length > 0 && w.fitView({ padding: 0.2 });
    });
  }, [x, w, $]);
  const z = oe((V, K, Y) => Y ? [
    ...V.filter((re) => re.nodeId !== K),
    { nodeId: K, x: Math.round(Y.x), y: Math.round(Y.y) }
  ] : V, []), F = oe((V, K) => {
    if (e?.state.rootActivity && a)
      return null;
    const Y = Gi(V, ts(V)), re = DN(e?.state.rootActivity, i, Y, V, s);
    return re.kind === "becomeRoot" ? (f(
      ({ draft: ee }) => ee ? { ...ee, state: { ...ee.state, rootActivity: Y } } : null,
      Y.nodeId
    ), null) : re.kind === "leafError" ? (m(""), v("The current root activity does not accept child activities. Drop Flowchart or Sequence to wrap it in a composite root."), null) : re.kind === "staleFrames" ? (m(""), v("This slot could not be resolved — returning to the workflow root."), h(), null) : re.kind === "wrapRoot" ? (f(({ draft: ee }) => {
      const we = ee?.state.rootActivity;
      return we ? {
        ...ee,
        layout: z(ee.layout, we.nodeId, K),
        state: { ...ee.state, rootActivity: Ia(Y, [], [we], V) }
      } : null;
    }, e?.state.rootActivity?.nodeId ?? null), v(""), m(`Wrapped root in ${Ie(V)}`), null) : (f(({ draft: ee, frames: we }) => {
      if (!ee?.state.rootActivity) return null;
      const pe = nn(ee.state.rootActivity, we, s);
      if (!pe) return null;
      const ve = pe.slot.cardinality === "single" ? [Y] : [...pe.slot.activities, Y], Se = Ia(ee.state.rootActivity, we, ve, s);
      return {
        ...ee,
        layout: z(ee.layout, Y.nodeId, K),
        state: { ...ee.state, rootActivity: Se }
      };
    }, Y.nodeId), re.replacedActivity && (v(""), m(`Replaced ${re.slot.label} content`)), Y);
  }, [s, e?.state.rootActivity, i, a, f, h, z, v, m]), B = oe((V, K) => {
    const Y = Gi(V, ts(V)), re = {
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
        icon: gn(V),
        childSlots: Re(Y, V),
        acceptsInbound: String(V.executionType ?? "").toLowerCase() !== "trigger",
        sourcePorts: Ul(Y, V)
      }
    };
    return { activityNode: Y, node: re };
  }, []), Z = oe((V, K, Y = []) => {
    a || d(({ draft: re, frames: ee }) => {
      if (!re) return null;
      const we = Yp(re.layout, V), pe = re.state.rootActivity;
      if (!pe) return { ...re, layout: we };
      const ve = nn(pe, ee, s);
      if (!ve) return { ...re, layout: we };
      const Se = Xp(ve, V, K, Y), Xe = ve.slot.mode === "flowchart" ? Up(Se, K) : Se;
      return {
        ...re,
        layout: we,
        state: {
          ...re.state,
          rootActivity: qp(pe, ee, Xe, s)
        }
      };
    });
  }, [s, a, d]), H = oe((V, K) => {
    if (!M.current) return null;
    const Y = M.current.getBoundingClientRect();
    return w ? w.screenToFlowPosition({ x: V, y: K }) : {
      x: V - Y.left,
      y: K - Y.top
    };
  }, [w]), U = oe((V, K) => document.elementFromPoint(V, K)?.closest(".react-flow__edge")?.getAttribute("data-id") ?? null, []), G = oe((V, K, Y) => {
    const re = x.find((Te) => Te.id === K.source), ee = x.find((Te) => Te.id === K.target), we = re && ee ? Xj(re, ee) : re ? fl(re) : Y, pe = B(V, we), Se = [...x.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], Xe = ih(j, K, pe.node.id);
    y(Se), g(Xe), p(pe.node.id), Z(Se, Xe, [pe.activityNode]);
  }, [Z, B, j, x, p]), X = oe((V, K, Y) => {
    if (!u || !M.current) return !1;
    const re = M.current.getBoundingClientRect();
    if (!(K >= re.left && K <= re.right && Y >= re.top && Y <= re.bottom)) return !1;
    const we = H(K, Y);
    if (!we) return !1;
    if (c) {
      const pe = U(K, Y), ve = pe ? j.find((Se) => Se.id === pe) : void 0;
      if (ve)
        return G(V, ve, we), !0;
    }
    return F(V, we), !0;
  }, [F, u, j, U, c, G, H]);
  Q(() => {
    const V = (Y) => {
      const re = N.current;
      if (!re) return;
      Math.hypot(Y.clientX - re.startX, Y.clientY - re.startY) >= bj && (re.dragging = !0);
    }, K = (Y) => {
      const re = N.current;
      if (N.current = null, !re?.dragging || !M.current || E.current) return;
      const ee = M.current.getBoundingClientRect();
      Y.clientX >= ee.left && Y.clientX <= ee.right && Y.clientY >= ee.top && Y.clientY <= ee.bottom && (T.current = !0, window.setTimeout(() => {
        T.current = !1;
      }, 0), X(re.activity, Y.clientX, Y.clientY));
    };
    return window.addEventListener("pointermove", V), window.addEventListener("pointerup", K), window.addEventListener("pointercancel", K), () => {
      window.removeEventListener("pointermove", V), window.removeEventListener("pointerup", K), window.removeEventListener("pointercancel", K);
    };
  }, [w, X]);
  const P = (V, K) => {
    E.current = { activityVersionId: K.activityVersionId, handledDrop: !1 }, V.dataTransfer.setData(el, K.activityVersionId), V.dataTransfer.setData("text/plain", K.activityVersionId), V.dataTransfer.effectAllowed = "copy";
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
  }, ie = (V) => {
    if (!M.current) return;
    const K = V.relatedTarget;
    K && M.current.contains(K) || D(null);
  }, fe = (V) => {
    V.preventDefault(), D(null);
    const K = V.dataTransfer.getData(el) || V.dataTransfer.getData("text/plain");
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
    K.length !== 0 && y((Y) => Cd(K, Y));
  }, ge = (V) => {
    a || g((K) => Id(V, K));
  }, ye = (V) => !V.source || !V.target || V.source === V.target || !c ? !1 : !V.targetHandle, Ee = (V) => {
    if (!e?.state.rootActivity || !t || !c || !ye(V)) return;
    const K = Qi(V.source, V.target, V.sourceHandle ?? "Done", V.targetHandle ?? void 0), Y = Ed(K, j);
    g(Y), Z(x, Y);
  }, De = () => {
    Z(x, j);
  }, Ve = !a && x.length > 0, ct = oe(() => {
    if (a || x.length === 0) return;
    const V = t?.slot.mode === "sequence" ? "sequence" : "flowchart", K = EN(x, j, V), Y = x.map((re) => {
      const ee = K.get(re.id);
      return ee ? { ...re, position: ee } : re;
    });
    y(Y), Z(Y, j), window.requestAnimationFrame(() => w?.fitView({ padding: 0.2 })), m("Rearranged the canvas.");
  }, [j, x, t, a, Z, w, m]), Ue = (V, K) => {
    if (!K.nodeId || K.handleType === "target") {
      k.current = null;
      return;
    }
    k.current = {
      nodeId: K.nodeId,
      handleId: K.handleId ?? null
    };
  }, Ye = (V, K) => {
    const Y = Yj(k.current, K);
    if (k.current = null, !Y || !c || K.toNode || K.toHandle || Uj(V)) return;
    const re = hf(V);
    I({
      kind: "fromPort",
      sourceNodeId: Y.nodeId,
      sourceHandleId: Y.handleId,
      clientX: re.x,
      clientY: re.y
    });
  }, Me = (V, K) => {
    if (!c || !ye(K)) return;
    const Y = nb(V, {
      ...K,
      sourceHandle: K.sourceHandle ?? "Done",
      // Connection.targetHandle is string | null; null and undefined are indistinguishable downstream
      // (every consumer checks truthiness), so keep the type-correct null.
      targetHandle: K.targetHandle ?? null
    }, j, { shouldReplaceId: !1 });
    g(Y), Z(x, Y);
  }, Oe = (V) => {
    if (a || V.length === 0) return;
    const K = new Set(V.map((ee) => ee.id)), Y = x.filter((ee) => !K.has(ee.id)), re = j.filter((ee) => !K.has(ee.source) && !K.has(ee.target));
    y(Y), g(re), l && K.has(l) && p(null), Z(Y, re);
  }, Ne = (V) => {
    if (a || V.length === 0) return;
    const K = new Set(V.map((re) => re.id)), Y = j.filter((re) => !K.has(re.id));
    g(Y), Z(x, Y);
  }, qe = oe((V) => {
    if (a) return;
    const K = j.filter((Y) => Y.id !== V);
    g(K), Z(x, K);
  }, [Z, j, a, x]), $e = oe((V, K, Y) => {
    c && I({ kind: "spliceEdge", edgeId: V, clientX: K, clientY: Y });
  }, [c]), kt = (V) => {
    const K = b;
    if (!K) return;
    I(null);
    const Y = H(K.clientX, K.clientY) ?? { x: 0, y: 0 };
    if (K.kind === "fromEmpty") {
      const ee = B(V, Y), pe = [...x.map((ve) => ve.selected ? { ...ve, selected: !1 } : ve), ee.node];
      y(pe), p(ee.node.id), Z(pe, j, [ee.activityNode]);
      return;
    }
    if (K.kind === "fromPort") {
      const ee = x.find((Te) => Te.id === K.sourceNodeId), we = ee ? fl(ee) : Y, pe = B(V, we), Se = [...x.map((Te) => Te.selected ? { ...Te, selected: !1 } : Te), pe.node], Xe = [...j, Qi(K.sourceNodeId, pe.node.id, K.sourceHandleId ?? "Done")];
      y(Se), g(Xe), p(pe.node.id), Z(Se, Xe, [pe.activityNode]);
      return;
    }
    const re = j.find((ee) => ee.id === K.edgeId);
    re && G(V, re, Y);
  }, xn = ae(() => ({
    highlightedEdgeId: A,
    deleteEdge: qe,
    requestInsertActivity: $e
  }), [qe, A, $e]);
  return {
    nodes: x,
    edges: j,
    canvasRef: M,
    setReactFlowInstance: S,
    connectMenu: b,
    setConnectMenu: I,
    edgeActions: xn,
    onNodesChange: ne,
    onEdgesChange: ge,
    onNodesDelete: Oe,
    onEdgesDelete: Ne,
    isValidConnection: ye,
    onConnect: Ee,
    onConnectStart: Ue,
    onConnectEnd: Ye,
    onReconnect: Me,
    commitLayout: De,
    canAutoLayout: Ve,
    autoLayout: ct,
    onCanvasDragOver: te,
    onCanvasDragLeave: ie,
    onCanvasDrop: fe,
    openEmptyConnectMenu: W,
    onConnectMenuPick: kt,
    addActivity: F,
    onPaletteClick: le,
    onPaletteDragStart: P,
    onPaletteDragEnd: q,
    onPalettePointerDown: de
  };
}
const yl = "elsa-studio:apply-workflow-graph-operation-batch", xl = "elsa-studio:undo-workflow-graph-operation-batch", MN = [
  "add-activity",
  "update-activity",
  "remove-activity",
  "connect-activities",
  "disconnect-activities",
  "set-root",
  "set-designer-position",
  "set-activity-property"
];
function LN(e, t, n) {
  if (!Array.isArray(t.operations)) throw new Error("Weaver batch does not contain operations.");
  const i = KN(e), o = jf(i.state.rootActivity), s = /* @__PURE__ */ new Map(), a = /* @__PURE__ */ new Map(), c = [];
  for (const u of t.operations) {
    const l = BN(u.kind), d = u.parameters ?? {};
    if (l === "add-activity") {
      const f = Le(d.activityId) ?? u.temporaryReferences?.[0], p = FN(f ?? Le(d.displayName) ?? Le(d.activityType) ?? "weaver-activity", o), h = zN(u, p, n);
      a.set(p, h), c.push(p), f && s.set(f, p), i.state.rootActivity && VN(i.state.rootActivity, h);
      const m = jt(d.position) ? ns(d.position, { x: 280, y: 160 }) : null;
      m && (i.layout = wl(i.layout, p, m));
      continue;
    }
    if (l === "set-root") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown root activity.");
      i.state.rootActivity = f;
      continue;
    }
    if (l === "set-designer-position") {
      const f = Bt(d.activityId, s);
      if (!f || !aa(i.state.rootActivity, f)) throw new Error("Weaver batch referenced an unknown activity position.");
      i.layout = wl(i.layout, f, ns(d, { x: 280, y: 160 }));
      continue;
    }
    if (l === "set-activity-property") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity property target.");
      WN(f, Le(d.propertyName) ?? "Value", d.value ?? "");
      continue;
    }
    if (l === "update-activity") {
      const f = Po(i, d.activityId, s, a);
      if (!f) throw new Error("Weaver batch referenced an unknown activity update target.");
      const p = jt(d.patch) ? d.patch : d;
      Object.assign(f, p);
      continue;
    }
    if (l === "remove-activity") {
      const f = Bt(d.activityId, s);
      if (!f) throw new Error("Weaver batch referenced an unknown activity remove target.");
      i.state.rootActivity = bf(i.state.rootActivity, f), i.layout = i.layout.filter((p) => p.nodeId !== f);
      continue;
    }
    if (l === "connect-activities") {
      ON(i, d, s);
      continue;
    }
    if (l === "disconnect-activities") {
      HN(i, d, s);
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
function zN(e, t, n) {
  const i = e.parameters ?? {}, o = Le(i.activityVersionId) ?? Le(i.activityType) ?? "Elsa.Workflows.Activity", s = n.find((a) => a.activityVersionId === o || a.activityTypeKey === o || a.displayName === Le(i.displayName));
  return s ? Gi(s, t) : {
    nodeId: t,
    // catalogItem is provably undefined here (the truthy branch returned above), so use the id directly.
    activityVersionId: o,
    inputs: [],
    outputs: [],
    ...Le(i.displayName) ? { displayName: Le(i.displayName) } : {},
    designer: { position: ns(i.position, { x: 280, y: 160 }) }
  };
}
function VN(e, t) {
  if (e.nodeId === t.nodeId) return;
  const n = ca(e);
  n && !n.some((i) => i.nodeId === t.nodeId) && n.push(t);
}
function ON(e, t, n) {
  const i = e.state.rootActivity;
  if (!i?.structure) throw new Error("Weaver batch cannot connect activities without a flowchart root.");
  const o = Bt(t.sourceActivityId ?? t.sourceId ?? t.from, n), s = Bt(t.targetActivityId ?? t.targetId ?? t.to, n);
  if (!o || !s) throw new Error("Weaver batch connection is missing source or target activity.");
  const a = i.structure.payload, c = Array.isArray(a.connections) ? a.connections : [], u = Le(t.connectionId) ?? `flow-${o}-${s}`;
  a.connections = [
    ...c.filter((l) => !jt(l) || l.id !== u),
    {
      id: u,
      source: { nodeId: o, port: Le(t.outcome) ?? Le(t.sourcePort) ?? "Done" },
      target: { nodeId: s }
    }
  ];
}
function HN(e, t, n) {
  const i = e.state.rootActivity, o = i?.structure?.payload.connections;
  if (!Array.isArray(o)) return;
  const s = Le(t.connectionId), a = Bt(t.sourceActivityId ?? t.sourceId ?? t.from, n), c = Bt(t.targetActivityId ?? t.targetId ?? t.to, n);
  i.structure.payload.connections = o.filter((u) => {
    if (!jt(u)) return !0;
    if (s && u.id === s) return !1;
    const l = jt(u.source) ? u.source.nodeId : void 0, d = jt(u.target) ? u.target.nodeId : void 0;
    return l !== a || d !== c;
  });
}
function WN(e, t, n) {
  const i = jt(n);
  e[cu(t)] = {
    typeName: typeof n == "string" ? "String" : "Object",
    expression: { type: i ? "Object" : "Literal", value: n }
  };
}
function Po(e, t, n, i) {
  const o = Bt(t, n);
  return o ? aa(e.state.rootActivity, o) ?? i.get(o) ?? null : null;
}
function Bt(e, t) {
  const n = Le(e);
  return n ? t.get(n) ?? n : null;
}
function aa(e, t) {
  if (!e) return null;
  if (e.nodeId === t) return e;
  for (const n of Nf(e)) {
    const i = aa(n, t);
    if (i) return i;
  }
  return null;
}
function bf(e, t) {
  if (!e || e.nodeId === t) return null;
  const n = ca(e);
  if (n) {
    const i = n.map((o) => bf(o, t)).filter((o) => !!o);
    n.splice(0, n.length, ...i);
  }
  return e;
}
function jf(e, t = /* @__PURE__ */ new Set()) {
  if (!e) return t;
  t.add(e.nodeId);
  for (const n of Nf(e)) jf(n, t);
  return t;
}
function Nf(e) {
  return ca(e) ?? [];
}
function ca(e) {
  const t = e.structure?.payload;
  return Array.isArray(t?.activities) ? t.activities : null;
}
function wl(e, t, n) {
  return [
    ...e.filter((i) => i.nodeId !== t),
    { nodeId: t, x: n.x, y: n.y }
  ];
}
function ns(e, t) {
  const n = jt(e) ? e : {}, i = Number(n.x), o = Number(n.y);
  return {
    x: Number.isFinite(i) ? Math.max(40, Math.round(i)) : t.x,
    y: Number.isFinite(o) ? Math.max(40, Math.round(o)) : t.y
  };
}
function FN(e, t) {
  const n = e.replace(/^temp:/, "").replace(/[^a-zA-Z0-9_-]+/g, "-").replace(/^-+|-+$/g, "").toLowerCase() || "weaver-activity";
  let i = n, o = 2;
  for (; t.has(i); )
    i = `${n}-${o}`, o += 1;
  return t.add(i), i;
}
function BN(e) {
  return typeof e == "number" ? MN[e] ?? "" : e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}
function Le(e) {
  return typeof e == "string" && e.trim() ? e : null;
}
function KN(e) {
  return typeof structuredClone == "function" ? structuredClone(e) : JSON.parse(JSON.stringify(e));
}
function jt(e) {
  return typeof e == "object" && e !== null;
}
function qN({
  draft: e,
  details: t,
  catalog: n,
  replaceDraftByBatch: i,
  setStatus: o,
  setError: s
}) {
  const a = se(/* @__PURE__ */ new Map());
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
        const p = Rn(e), h = LN(e, d.batch, n), m = `weaver-batch-${Date.now()}`;
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
    return window.addEventListener(yl, c), window.addEventListener(xl, u), () => {
      window.removeEventListener(yl, c), window.removeEventListener(xl, u);
    };
  }, [n, t, e, i, o, s]);
}
function XN({ context: e, draft: t, autosaveEnabledByDefault: n = !0, editDraft: i, setStatus: o, setError: s }) {
  const [a, c] = O(n), u = se(""), l = se(0), d = se(Promise.resolve()), f = oe((h) => {
    u.current = h ? We(h) : "";
  }, []), p = oe(async (h, m) => {
    const v = async () => {
      const y = ++l.current, j = We(h);
      s("");
      try {
        const g = await Rh(e, h), w = We(g);
        return u.current = w, i(({ draft: S }) => !S || S.id !== g.id ? null : We(S) === j ? g : { ...S, validationErrors: g.validationErrors }), y === l.current && o(m), g;
      } catch (g) {
        throw y === l.current && (o(""), s(g instanceof Error ? g.message : String(g))), g;
      }
    }, x = d.current.then(v, v);
    return d.current = x.catch(() => {
    }), x;
  }, [e, i, o, s]);
  return Q(() => {
    if (!a || !t || We(t) === u.current) return;
    o("Autosaving...");
    const m = window.setTimeout(() => {
      p(t, "Autosaved").catch(() => {
      });
    }, jj);
    return () => window.clearTimeout(m);
  }, [a, t, p, o]), { saveDraft: p, autosaveEnabled: a, setAutosaveEnabled: c, markSaved: f };
}
function UN({ context: e, definitionId: t, resetHistory: n, loadDraft: i, markSaved: o, setError: s }) {
  const [a, c] = O(null), [u, l] = O([]), [d, f] = O([]), [p, h] = O(null), [m, v] = O(Wi), [x, y] = O("loading"), j = oe(async () => {
    s(""), y("loading");
    const [g, w, S, b, I] = await Promise.all([
      Gl(e, t),
      ri(e),
      Yh(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: [] })
      ),
      Zh(e).then(
        (D) => ({ ok: !0, descriptors: D }),
        () => ({ ok: !1, descriptors: Wi })
      ),
      // Non-essential: drives only the non-blocking availability warnings, so failure is tolerated.
      tu(e).then(
        (D) => D,
        () => null
      )
    ]), A = g.draft ?? null;
    c(g), o(A), n(A), i(A), l(w.activities ?? []), f(S.descriptors), h(I), v(b.descriptors.length > 0 ? b.descriptors : Wi), y(S.ok ? "ready" : "failed");
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
function YN({ context: e, details: t, setDetails: n, setStatus: i }) {
  const o = se(null), s = se(null), a = se({});
  Q(() => {
    o.current = t;
  }, [t]);
  const c = oe(() => {
    s.current !== null && (window.clearTimeout(s.current), s.current = null);
    const l = a.current;
    a.current = {};
    const d = o.current?.definition;
    !d || l.name === void 0 && l.description === void 0 || Th(e, d.id, {
      name: l.name ?? d.name,
      description: l.description ?? d.description ?? null
    }).then((f) => n((p) => p && p.definition.id === f.definition.id ? { ...p, definition: f.definition } : p)).catch(() => i("Couldn't save name/description."));
  }, [e, n, i]), u = oe((l) => {
    n((d) => d && { ...d, definition: { ...d.definition, ...l } }), a.current = { ...a.current, ...l }, s.current !== null && window.clearTimeout(s.current), s.current = window.setTimeout(c, 800);
  }, [c, n]);
  return Q(() => () => {
    c();
  }, [c]), { updateDefinitionMeta: u };
}
function ZN({
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
  const m = oe(() => {
    if (!t) return;
    const j = n?.definition.name;
    ej(J0(t, j), j), d("Exported workflow as JSON.");
  }, [t, n, d]), v = oe(async () => {
    if (!(!t || i)) {
      l("saving"), d("Saving...");
      try {
        await o(t, "Saved");
      } catch {
      } finally {
        l("idle");
      }
    }
  }, [t, i, o, l, d]), x = oe(async () => {
    if (!(!t || i)) {
      l("promoting"), d("Saving...");
      try {
        await o(t, "Saved"), d("Promoting...");
        const j = await Ph(e, t.id), g = await Mh(e, j.versionId);
        u(g.artifactId), d(`Published ${g.artifactVersion}`), await s();
      } catch (j) {
        d(""), f(j instanceof Error ? j.message : String(j));
      } finally {
        l("idle");
      }
    }
  }, [t, i, e, o, s, u, l, d, f]), y = oe(async () => {
    if (!t?.state.rootActivity || i) return;
    const j = t, g = We(j);
    c(), d("Preparing test run...");
    try {
      l("testRunPreparing"), d("Preparing test run...");
      const w = Qj(j);
      l("testRunStarting"), d("Starting test run...");
      const S = await Lh(e, {
        definitionId: j.definitionId,
        snapshotId: w,
        state: j.state
      });
      a({ draftSignature: g, view: S }), p("runtime"), h(!1), d(Qs(S) ? "Test run rejected" : "Test run dispatched");
    } catch (w) {
      d(""), f(w instanceof Error ? w.message : String(w));
    } finally {
      l("idle");
    }
  }, [t, i, e, c, a, p, h, l, d, f]);
  return { exportJson: m, save: v, promoteAndPublish: x, run: y };
}
function JN({
  context: e,
  draft: t,
  frames: n,
  selectedNodeId: i,
  catalog: o,
  activityDescriptors: s,
  availabilityDiagnostics: a
}) {
  const c = t?.state.rootActivity ?? null, u = ae(() => new Map(o.map((k) => [k.activityVersionId, k])), [o]), l = oe(
    (k) => xg([k.activityVersionId, k.activityTypeKey], a),
    [a]
  ), d = ae(() => Fj(s), [s]), f = ae(() => Ol(c, n, u), [c, n, u]), p = ws(f, f ? u.get(f.activityVersionId) : void 0), h = !!f && p === "unsupported", m = ae(() => h ? null : nn(c, n, u), [c, n, u, h]), v = ae(() => h && f?.nodeId === i ? f : m?.slot.activities.find((k) => k.nodeId === i) ?? null, [h, m, f, i]), x = ae(
    () => v ? ll(v, u, d) : null,
    [u, d, v]
  ), y = v ? Re(v, u) : [], j = v ?? f, g = !v && !!f, w = ae(
    () => j ? ll(j, u, d) : null,
    [u, d, j]
  ), S = ae(
    () => j ? l({ activityVersionId: j.activityVersionId, activityTypeKey: u.get(j.activityVersionId)?.activityTypeKey }) : null,
    [l, u, j]
  ), b = j ? Re(j, u) : [], I = j ? ph(j, u.get(j.activityVersionId)) : !1, A = kh(e, t?.state, v?.nodeId ?? null, u), D = !h && m?.slot.mode === "flowchart";
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
function GN({
  details: e,
  draft: t,
  selectedNode: n,
  selectedNodeId: i,
  selectedDescriptor: o,
  catalogByVersion: s
}) {
  Q(() => {
    if (!(!e || !t))
      return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
        workflowId: e.definition.id,
        workflowDefinitionId: e.definition.id,
        workflowVersionId: t.sourceVersionId ?? null,
        draftId: t.id,
        revision: Zj(t),
        selectedNodeId: i,
        selectedActivityType: o?.typeName ?? (n ? s.get(n.activityVersionId)?.activityTypeKey ?? n.activityVersionId : null),
        summary: e.definition.name,
        activities: gf(t.state.rootActivity, s),
        connections: mf(t.state.rootActivity, s),
        diagnostics: t.validationErrors.map((a) => ({ severity: a.code ?? "warning", message: a.message ?? "Workflow validation issue." }))
      }, () => {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === e.definition.id && (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = void 0);
      };
  }, [s, e, t, o, n, i]);
}
function QN({
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
      /* @__PURE__ */ r.jsx(hr, { size: 14, "aria-hidden": "true" }),
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
          const h = p.description?.trim(), m = h ? `wf-palette-description-${p.activityVersionId}` : void 0, v = Ie(p), x = gn(p);
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
                /* @__PURE__ */ r.jsx("span", { className: "wf-activity-icon", "data-icon": x, "aria-hidden": "true", children: vr(x) }),
                /* @__PURE__ */ r.jsxs("span", { className: "wf-palette-activity-text", children: [
                  /* @__PURE__ */ r.jsx("strong", { children: v }),
                  h ? /* @__PURE__ */ r.jsx("small", { id: m, children: h }) : null
                ] }),
                /* @__PURE__ */ r.jsx(Dl, { className: "wf-palette-activity-grip", size: 14, "aria-hidden": "true" })
              ]
            },
            p.activityVersionId
          );
        }) }) : null
      ] }, d.category);
    }) })
  ] });
}
const Sf = /* @__PURE__ */ new Set(["studio.property.singleline", "studio.property.text-fallback"]), eS = "Variable";
function tS({
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
  const d = lS(l), f = o.length > 0 ? o : wg;
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-properties", children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-section-label", children: "Properties" }),
    d.map((p) => /* @__PURE__ */ r.jsxs("section", { className: "wf-property-group", children: [
      d.length > 1 ? /* @__PURE__ */ r.jsx("h4", { children: p.category }) : null,
      p.inputs.map((h) => /* @__PURE__ */ r.jsx(
        nS,
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
function nS({
  activity: e,
  input: t,
  editors: n,
  expressionEditors: i,
  expressionDescriptors: o,
  visibleVariables: s,
  scopeStatus: a,
  onChange: c
}) {
  const u = t.isReadOnly === !0, l = { activity: e, expressionDescriptors: o, readOnly: u }, d = ss(n, t, l), f = d?.component, p = t.isWrapped !== !1 ? lu(e, t) : null, h = p?.expression.type ?? "Literal", m = Ng(e, t), v = h.toLowerCase(), y = p && (v === "literal" || v === "object") && !kg(t) ? Cg(t.typeName) : null, j = y ? ss(n, t, { ...l, scope: "collection" }) : void 0, g = p ? {
    activity: e,
    descriptor: t,
    expressionDescriptors: o,
    readOnly: u,
    surface: "inline",
    syntax: h
  } : null, w = g ? If(i, g) : null, S = w?.surfaces.inline, b = w && g ? kf(w, g, m) : [], I = y != null, A = !!(p && !I && uS(t, d?.id)), D = !!(p && !I && dS(t, d?.id)), [M, k] = O(!1), R = (N) => {
    const E = p ? bg(p, N) : N;
    c(La(e, t, E));
  }, L = (N) => {
    p && c(La(e, t, jg(p, N)));
  }, C = y ? j ? is(j.component, t, m, u, { ...l, scope: "collection" }, R) : /* @__PURE__ */ r.jsx(
    rS,
    {
      input: t,
      elementTypeName: y.elementTypeName,
      value: m,
      editors: n,
      context: l,
      disabled: u,
      onChange: R
    }
  ) : null, _ = h === eS && p ? /* @__PURE__ */ r.jsx(
    aS,
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
      /* @__PURE__ */ r.jsx("span", { children: Ns(t.typeName) })
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
          children: /* @__PURE__ */ r.jsx(tn, { size: 13 })
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
          /* @__PURE__ */ r.jsx(tn, { size: 13 }),
          " Open expanded editor"
        ]
      }
    ) : null,
    M ? /* @__PURE__ */ r.jsx(
      oS,
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
function iS(e, t, n) {
  return [
    "wf-collection-item",
    t === e ? "dragging" : "",
    t !== null && t !== e && n === e ? "drop-target" : ""
  ].filter(Boolean).join(" ");
}
function rS({
  input: e,
  elementTypeName: t,
  value: n,
  editors: i,
  context: o,
  disabled: s,
  onChange: a
}) {
  const c = Eg(n), u = _g(e, t), l = { ...o, scope: "element" }, d = ss(i, u, l)?.component, f = e.displayName || e.name, p = (S, b) => a(c.map((I, A) => A === S ? b : I)), [h, m] = O(null), [v, x] = O(null), y = () => {
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
        className: iS(b, h, v),
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
              children: /* @__PURE__ */ r.jsx(Dl, { size: 13, "aria-hidden": "true" })
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
                children: /* @__PURE__ */ r.jsx(Gf, { size: 13 })
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
                children: /* @__PURE__ */ r.jsx(Pt, { size: 13 })
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
        onClick: () => a([...c, Ag(t)]),
        children: [
          /* @__PURE__ */ r.jsx(en, { size: 13 }),
          " Add item"
        ]
      }
    )
  ] });
}
function oS({
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
  const d = kl(), f = e.displayName || e.name, p = {
    activity: o,
    descriptor: e,
    expressionDescriptors: i,
    readOnly: a,
    surface: "expanded",
    syntax: n
  }, h = If(s, p), m = h?.surfaces.expanded, v = h ? kf(h, p, t) : [], x = m ? null : cS(s, p);
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
        /* @__PURE__ */ r.jsx("span", { children: Ns(e.typeName) })
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
  const [a, c] = O(!1), u = kl(), l = n.find((f) => f.type === t), d = [
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
function Cf(e) {
  return !e || e === tr ? tr : e;
}
function vl(e, t) {
  return `${Cf(t)}${os}${e}`;
}
function sS(e) {
  const t = e.indexOf(os);
  if (t < 0) return null;
  const n = e.slice(t + os.length);
  return n ? { scopeId: e.slice(0, t), referenceKey: n } : null;
}
function aS({ value: e, visibleVariables: t, scopeStatus: n, disabled: i, onChange: o }) {
  const s = Jl(e), a = !!e && typeof e == "object" || typeof e == "string" && e.trim().startsWith("{"), c = s && (a || t.some((d) => d.referenceKey === s.referenceKey)) ? s : null, u = c ? vl(c.referenceKey, c.declaringScopeId) : "", l = !!c && t.some(
    (d) => d.referenceKey === c.referenceKey && d.scopeId === Cf(c.declaringScopeId)
  );
  return /* @__PURE__ */ r.jsxs("div", { className: "wf-variable-picker", children: [
    /* @__PURE__ */ r.jsxs(
      "select",
      {
        "aria-label": "Variable reference",
        value: u,
        disabled: i,
        onChange: (d) => {
          const f = sS(d.target.value);
          f && o(gh(f.referenceKey, f.scopeId));
        },
        children: [
          /* @__PURE__ */ r.jsx("option", { value: "", children: "Select a variable…" }),
          c && !l ? /* @__PURE__ */ r.jsxs("option", { value: u, children: [
            c.referenceKey,
            " (not visible from this scope)"
          ] }) : null,
          t.map((d) => {
            const f = vl(d.referenceKey, d.scopeId);
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
function If(e, t) {
  return [...e].sort((n, i) => (n.order ?? 500) - (i.order ?? 500)).find((n) => !!n.surfaces[t.surface] && n.supports(t));
}
function kf(e, t, n) {
  return e.diagnostics?.(t, n) ?? [];
}
function cS(e, t) {
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
function lS(e) {
  const t = /* @__PURE__ */ new Map();
  for (const n of e) {
    const i = n.category?.trim() || "General";
    t.set(i, [...t.get(i) ?? [], n]);
  }
  return [...t.entries()].map(([n, i]) => ({ category: n, inputs: i }));
}
function uS(e, t) {
  if (e.uiHint?.toLowerCase() === "multiline" || t && !Sf.has(t)) return !1;
  const n = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(n) || e.uiHint?.toLowerCase() === "singleline";
}
function dS(e, t) {
  const n = e.uiHint?.toLowerCase();
  if (n === "checkbox" || n === "dropdown" || t && !Sf.has(t) && n !== "multiline") return !1;
  const i = e.typeName.toLowerCase();
  return ["string", "system.string", "text"].includes(i) || n === "singleline" || n === "multiline";
}
function fS({
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
      /* @__PURE__ */ r.jsx(Vn, { size: 14 }),
      /* @__PURE__ */ r.jsxs("span", { children: [
        "No longer available for new use · ",
        Wn(s.state)
      ] })
    ] }) : null,
    /* @__PURE__ */ r.jsx(
      tS,
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
      xj,
      {
        context: e,
        variables: Zl(t),
        title: "Container variables",
        addLabel: "Add container variable",
        emptyLabel: "No container variables declared on this activity.",
        warnings: yh(v.shadowingWarnings, t.nodeId),
        onChange: (S) => x(hh(t, S))
      }
    ) }) : null,
    a.length > 0 ? /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-list", children: [
      /* @__PURE__ */ r.jsx("span", { children: "Embedded slots" }),
      a.map((S) => {
        const b = Hn(n, S);
        return /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-row", children: [
          /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => y(t.nodeId, S, b), children: [
            S.label,
            /* @__PURE__ */ r.jsx("small", { children: Gj(S, l) })
          ] }),
          S.cardinality === "single" ? /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-slot-change",
              "aria-label": `${S.activities.length > 0 ? "Change" : "Choose"} ${S.label} activity`,
              title: S.activities.length > 0 ? "Change activity" : "Choose activity",
              onClick: (I) => w({ nodeId: t.nodeId, slotId: S.id, clientX: I.clientX, clientY: I.clientY }),
              children: /* @__PURE__ */ r.jsx(Qf, { size: 14 })
            }
          ) : null
        ] }, S.id);
      })
    ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This activity does not expose embedded child slots." }),
    g ? /* @__PURE__ */ r.jsx(
      xf,
      {
        clientX: g.clientX,
        clientY: g.clientY,
        activities: u,
        onPick: (S) => {
          w(null);
          const b = a.find((I) => I.id === g.slotId);
          b && j(t.nodeId, b, Hn(n, b), S);
        },
        onClose: () => w(null)
      }
    ) : null
  ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "Select an activity to inspect properties and embedded slots." });
}
const Ef = [
  { value: "flowchart", label: "Flowchart", hint: "Free-form graph of connected activities." },
  { value: "sequence", label: "Sequence", hint: "Ordered list of activities that run top to bottom." }
];
function Af({ label: e, hint: t }) {
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-title", children: e }),
    /* @__PURE__ */ r.jsx("span", { className: "wf-root-card-hint", children: t })
  ] });
}
function pS({ value: e, onChange: t }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "radiogroup", "aria-label": "Root activity", children: Ef.map((n) => {
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
      /* @__PURE__ */ r.jsx(Af, { label: n.label, hint: n.hint })
    ] }, n.value);
  }) });
}
function hS({ onPick: e }) {
  return /* @__PURE__ */ r.jsx("div", { className: "wf-root-cards", role: "group", "aria-label": "Fill this slot", children: Ef.map((t) => /* @__PURE__ */ r.jsx(
    "button",
    {
      type: "button",
      className: "wf-root-card",
      "aria-label": t.label,
      onClick: () => e(t.value),
      children: /* @__PURE__ */ r.jsx(Af, { label: t.label, hint: t.hint })
    },
    t.value
  )) });
}
function gS({ slotLabel: e, catalog: t, onPickActivity: n, onBrowseAll: i }) {
  const o = (s) => {
    const a = uf(t, s);
    a && n(a);
  };
  return /* @__PURE__ */ r.jsx("div", { className: "wf-slot-empty", role: "group", "aria-label": `Fill ${e}`, children: /* @__PURE__ */ r.jsxs("div", { className: "wf-slot-empty-card", children: [
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-title", children: "This slot is empty" }),
    /* @__PURE__ */ r.jsxs("p", { className: "wf-slot-empty-hint", children: [
      "Choose a container for ",
      /* @__PURE__ */ r.jsx("strong", { children: e }),
      ", or pick any activity."
    ] }),
    /* @__PURE__ */ r.jsx(hS, { onPick: o }),
    /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-slot-empty-browse",
        onClick: (s) => i({ clientX: s.clientX, clientY: s.clientY }),
        children: [
          /* @__PURE__ */ r.jsx(ei, { size: 15 }),
          " Browse all activities…"
        ]
      }
    ),
    /* @__PURE__ */ r.jsx("p", { className: "wf-slot-empty-drag-hint", children: "Tip: you can also drag activities from the palette onto the canvas." })
  ] }) });
}
function mS({
  context: e,
  definitionId: t,
  ai: n,
  propertyEditors: i,
  expressionEditors: o,
  workflowDesignerPanels: s,
  autosaveEnabledByDefault: a,
  onBack: c
}) {
  const u = CN(), { draft: l, frames: d, selectedNodeId: f, testRun: p, publishedArtifactId: h } = u.state, {
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
    editorBodyStyle: ie,
    toggleSidePanelCollapsed: fe,
    toggleSidePanelMaximized: W,
    startSidePanelResize: ne,
    handleSidePanelResizeKeyDown: ge
  } = sa(), { resetHistory: ye, undo: Ee, redo: De, canUndoNow: Ve, canRedoNow: ct } = jN({ draft: l, restoreDraft: m }), { saveDraft: Ue, autosaveEnabled: Ye, setAutosaveEnabled: Me, markSaved: Oe } = XN({ context: e, draft: l, autosaveEnabledByDefault: a, editDraft: x, setStatus: k, setError: D }), {
    details: Ne,
    setDetails: qe,
    catalog: $e,
    activityDescriptors: kt,
    availabilityDiagnostics: xn,
    expressionDescriptors: V,
    descriptorStatus: K,
    reload: Y
  } = UN({ context: e, definitionId: t, resetHistory: ye, loadDraft: m, markSaved: Oe, setError: D }), { updateDefinitionMeta: re } = YN({ context: e, details: Ne, setDetails: qe, setStatus: k }), {
    catalogByVersion: ee,
    availabilityLookup: we,
    scopeOwner: pe,
    isUnsupportedDesigner: ve,
    scope: Se,
    selectedNode: Xe,
    selectedDescriptor: Te,
    selectedSlots: Tr,
    inspectedNode: tt,
    inspectedIsScopeOwner: Rr,
    inspectedDescriptor: fi,
    inspectedNodeAvailability: Pr,
    inspectedSlots: Mr,
    inspectedSupportsScopedVariables: Lr,
    scopedVariableAnalysis: zr,
    isFlowchartDesigner: lt,
    canAddActivitiesToCanvas: Vr
  } = JN({ context: e, draft: l, frames: d, selectedNodeId: f, catalog: $e, activityDescriptors: kt, availabilityDiagnostics: xn }), Kt = ae(() => es($e), [$e]), Or = ae(() => {
    const J = N.trim().toLowerCase();
    if (!J) return Kt;
    const ue = $e.filter((me) => Ie(me).toLowerCase().includes(J) || me.activityTypeKey.toLowerCase().includes(J) || (me.category ?? "").toLowerCase().includes(J) || (me.description ?? "").toLowerCase().includes(J));
    return es(ue);
  }, [$e, N, Kt]), qt = R !== "idle", Hr = !!l?.state.rootActivity && !qt, pi = Nt(n, "weaver.workflows.find-draft-risks"), hi = Nt(n, "weaver.workflows.propose-update"), Wr = PN({
    draft: l,
    scope: Se,
    scopeOwner: pe,
    frames: d,
    catalog: $e,
    catalogByVersion: ee,
    isUnsupportedDesigner: ve,
    isFlowchartDesigner: lt,
    canAddActivitiesToCanvas: Vr,
    selectedNodeId: f,
    editDraft: x,
    editDraftAndSelect: y,
    select: j,
    resetToRoot: w,
    setStatus: k,
    setError: D
  }), {
    nodes: Xt,
    edges: gi,
    canvasRef: mi,
    setReactFlowInstance: yi,
    connectMenu: wn,
    setConnectMenu: Fr,
    edgeActions: Br,
    onNodesChange: Kr,
    onEdgesChange: qr,
    onNodesDelete: Xr,
    onEdgesDelete: xi,
    isValidConnection: Ur,
    onConnect: Yr,
    onConnectStart: Zr,
    onConnectEnd: Jr,
    onReconnect: Gr,
    commitLayout: Qr,
    canAutoLayout: eo,
    autoLayout: to,
    onCanvasDragOver: vn,
    onCanvasDragLeave: wi,
    onCanvasDrop: vi,
    openEmptyConnectMenu: bi,
    onConnectMenuPick: no,
    addActivity: ji,
    onPaletteClick: io,
    onPaletteDragStart: ro,
    onPaletteDragEnd: oo,
    onPalettePointerDown: Ni
  } = Wr, so = !ve && !lt && d.length > 0 && !!Se && Se.slot.activities.length === 0 && Xt.length === 0, Si = oe((J) => {
    const ue = ji(J);
    if (!ue || !Se || !pe || d.length === 0) return;
    const me = d[d.length - 1].label, Ce = Pn(d, pe, pe.nodeId, { ...Se.slot, activities: [ue] }, me, ee);
    Ce && Ce.frames.length > d.length && g(Ce.frames, Ce.selectedNodeId);
  }, [ji, ee, d, g, Se, pe]);
  qN({ draft: l, details: Ne, catalog: $e, replaceDraftByBatch: v, setStatus: k, setError: D }), Q(() => {
    !l?.state.rootActivity || $e.length === 0 || x(({ draft: J }) => {
      if (!J?.state.rootActivity) return null;
      const ue = Bl(J.state.rootActivity, ee);
      return !ue || ue === J.state.rootActivity ? null : {
        ...J,
        state: {
          ...J.state,
          rootActivity: ue
        }
      };
    });
  }, [$e.length, ee, l?.state.rootActivity, x]), GN({ details: Ne, draft: l, selectedNode: Xe, selectedNodeId: f, selectedDescriptor: Te, catalogByVersion: ee }), Q(() => {
    _((J) => {
      let ue = !1;
      const me = new Set(J);
      for (const Ce of Kt)
        me.has(Ce.category) || (me.add(Ce.category), ue = !0);
      return ue ? me : J;
    });
  }, [Kt]);
  const { exportJson: ao, save: co, promoteAndPublish: bn, run: lo } = ZN({
    context: e,
    draft: l,
    details: Ne,
    busy: qt,
    saveDraft: Ue,
    reload: Y,
    startTestRun: S,
    clearTestRun: b,
    setPublishedArtifact: I,
    setOperation: L,
    setStatus: k,
    setError: D,
    setActiveRightPanelId: F,
    setInspectorCollapsed: q
  }), uo = oe((J) => {
    x(({ draft: ue }) => ue ? { ...ue, state: J(ue.state) } : null);
  }, [x]), Ci = oe((J) => {
    if (!l) return "No draft is loaded.";
    const ue = Q0(J, l);
    return ue.ok ? (m(ue.draft), k("Applied workflow JSON."), null) : ue.error;
  }, [l, m]);
  Q(() => {
    const J = (ue) => {
      if (B !== "designer" || !(ue.metaKey || ue.ctrlKey)) return;
      const me = ue.target;
      if (me && (me.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(me.tagName))) return;
      const Ce = ue.key.toLowerCase();
      Ce === "z" && !ue.shiftKey ? (ue.preventDefault(), Ee()) : (Ce === "z" && ue.shiftKey || Ce === "y") && (ue.preventDefault(), De());
    };
    return window.addEventListener("keydown", J), () => window.removeEventListener("keydown", J);
  }, [B, Ee, De]);
  const fo = oe((J, ue, me) => {
    const Ce = Pn(d, pe, J, ue, me, ee);
    Ce && g(Ce.frames, Ce.selectedNodeId);
  }, [ee, d, g, pe]), $f = oe((J, ue, me, Ce) => {
    const ki = ue.activities.length > 0, ga = Gi(Ce, ts(Ce));
    x(({ draft: Ei }) => {
      const ma = Ei?.state.rootActivity;
      return !Ei || !ma ? null : {
        ...Ei,
        state: {
          ...Ei.state,
          rootActivity: Vo(ma, J, (Hf) => hn(Hf, ue, [ga]), ee)
        }
      };
    });
    const go = Pn(d, pe, J, { ...ue, activities: [ga] }, me, ee);
    go && g(go.frames, go.selectedNodeId), D(""), k(ki ? `Replaced ${ue.label} content` : `Assigned ${Ie(Ce)} to ${ue.label}`);
  }, [ee, x, d, g, pe]), Tf = ae(() => ve ? null : (J, ue, me) => fo(J, me, Hn(ue, me)), [fo, ve]), Rf = oe((J) => {
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
  }, [ee, x]), Pf = oe((J) => {
    if (!J) return;
    const ue = l?.state.rootActivity;
    if (!ue) return;
    const me = Lp(ue, J, (Ce) => {
      const ki = ee.get(Ce.activityVersionId);
      return ki ? Ie(ki) : Ce.nodeId;
    }, ee);
    me && (Z("designer"), g(me, J), q(!1));
  }, [l?.state.rootActivity, ee, q, g]), Mf = (J) => {
    _((ue) => {
      const me = new Set(ue);
      return me.has(J) ? me.delete(J) : me.add(J), me;
    });
  };
  if (!Ne || !l)
    return /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: A || "Loading workflow editor..." });
  const ua = tt ? ee.get(tt.activityVersionId) : void 0, Lf = tt ? Xt.find((J) => J.id === tt.nodeId)?.data.label ?? (ua ? Ie(ua) : tt.nodeId) : "", Ii = p?.draftSignature === We(l) ? p.view : null, da = Ii && M.startsWith("Test run") ? "" : M, zf = (J) => {
    window.history.pushState({}, "", `/workflows/instances/${encodeURIComponent(J)}`), window.dispatchEvent(new PopStateEvent("popstate"));
  }, Vf = {
    definition: Ne.definition,
    draft: l,
    selectedActivity: Xe,
    selectedActivityDescriptor: Te,
    selectedActivitySlots: Tr,
    catalog: $e,
    currentScopeOwner: pe,
    frames: d
  }, fa = s.map((J) => {
    const ue = J.component;
    return {
      id: J.id,
      title: J.title,
      side: J.side,
      order: J.order ?? 500,
      icon: null,
      render: () => /* @__PURE__ */ r.jsx(ue, { context: Vf })
    };
  }), po = [
    {
      id: "activities",
      title: "Activities",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(ei, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        QN,
        {
          paletteSearch: N,
          onSearchChange: E,
          groups: Or,
          expandedCategories: C,
          onToggleCategory: Mf,
          onActivityClick: io,
          onActivityDragStart: ro,
          onActivityDragEnd: oo,
          onActivityPointerDown: Ni
        }
      )
    },
    ...fa.filter((J) => J.side === "left")
  ].sort(pl), ho = [
    {
      id: "inspector",
      title: "Inspector",
      order: 0,
      icon: /* @__PURE__ */ r.jsx(pr, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        fS,
        {
          context: e,
          selectedNode: tt,
          selectedNodeLabel: Lf,
          selectedActivityType: tt ? fi?.typeName ?? ee.get(tt.activityVersionId)?.activityTypeKey ?? "Unknown" : "",
          selectedDescriptor: fi,
          selectedNodeAvailability: Pr,
          selectedSlots: Mr,
          inspectingScopeOwner: Rr,
          catalog: $e,
          catalogByVersion: ee,
          selectedSupportsScopedVariables: Lr,
          propertyEditors: i,
          expressionEditors: o,
          expressionDescriptors: V,
          descriptorStatus: K,
          scopedVariableAnalysis: zr,
          onSelectedActivityChange: Rf,
          onEnterSlot: fo,
          onReplaceSlotActivity: $f
        }
      )
    },
    {
      id: "runtime",
      title: "Runtime",
      order: 5,
      icon: /* @__PURE__ */ r.jsx(Rt, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(pN, { testRun: Ii, onOpenRun: zf })
    },
    {
      id: "artifacts",
      title: "Artifacts",
      order: 10,
      icon: /* @__PURE__ */ r.jsx($l, { size: 15 }),
      render: () => /* @__PURE__ */ r.jsx(
        mN,
        {
          context: e,
          ai: n,
          definitionId: Ne.definition.id,
          publishedArtifactId: h
        }
      )
    },
    ...fa.filter((J) => J.side === "right")
  ].sort(pl), pa = po.find((J) => J.id === T) ?? po[0], ha = ho.find((J) => J.id === z) ?? ho[0], Of = [
    { id: "designer", title: "Designer", order: 0, icon: /* @__PURE__ */ r.jsx(ps, { size: 14 }), render: () => null },
    { id: "code", title: "Code", order: 1, icon: /* @__PURE__ */ r.jsx(rp, { size: 14 }), render: () => null },
    { id: "properties", title: "Properties", order: 2, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-editor", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-editor-top", children: [
      /* @__PURE__ */ r.jsx("button", { type: "button", className: "wf-link-button", onClick: c, children: "Definitions" }),
      /* @__PURE__ */ r.jsx(pt, { size: 14 }),
      /* @__PURE__ */ r.jsx("strong", { children: Ne.definition.name }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-chip", children: "Draft" }),
      da ? /* @__PURE__ */ r.jsxs("span", { className: "wf-status", children: [
        /* @__PURE__ */ r.jsx(fn, { size: 13 }),
        " ",
        da
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
              children: /* @__PURE__ */ r.jsx(ep, { size: 16 })
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
              onClick: De,
              children: /* @__PURE__ */ r.jsx(tp, { size: 16 })
            }
          ),
          /* @__PURE__ */ r.jsx(
            "button",
            {
              type: "button",
              className: "wf-icon-button",
              "aria-label": "Auto-layout",
              title: "Auto-layout the canvas",
              disabled: !eo,
              onClick: to,
              children: /* @__PURE__ */ r.jsx(np, { size: 16 })
            }
          )
        ] }),
        /* @__PURE__ */ r.jsxs("label", { className: "wf-autosave-toggle", children: [
          /* @__PURE__ */ r.jsx("input", { className: "wf-switch-input", type: "checkbox", checked: Ye, onChange: (J) => Me(J.target.checked) }),
          /* @__PURE__ */ r.jsx("span", { children: "Autosave" })
        ] }),
        pi ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, pi, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Risks"
        ] }) : null,
        hi ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(n, hi, { definition: Ne.definition, draft: l }), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 15 }),
          " Propose"
        ] }) : null,
        /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Export workflow as JSON", onClick: ao, children: [
          /* @__PURE__ */ r.jsx(ip, { size: 15 }),
          " Export"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: qt, onClick: () => {
          co();
        }, children: [
          /* @__PURE__ */ r.jsx(ls, { size: 15 }),
          " Save"
        ] }),
        /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: qt, onClick: () => {
          bn();
        }, children: [
          /* @__PURE__ */ r.jsx(El, { size: 15 }),
          " Promote"
        ] }),
        Ii ? /* @__PURE__ */ r.jsx(
          fN,
          {
            testRun: Ii,
            onOpenDetails: () => {
              F("runtime"), q(!1);
            }
          }
        ) : null,
        /* @__PURE__ */ r.jsxs(
          "button",
          {
            type: "button",
            disabled: !Hr,
            title: l.state.rootActivity ? "Run a transient test of the current design" : "Add a root activity before running",
            onClick: () => {
              lo();
            },
            children: [
              /* @__PURE__ */ r.jsx(Rt, { size: 15 }),
              " Run"
            ]
          }
        )
      ] })
    ] }),
    A ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(It, { size: 16 }),
      " ",
      A
    ] }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: te, style: ie, children: [
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-palette", "aria-label": "Activities panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            zn,
            {
              label: "Activities panel tabs",
              tabs: po,
              activeTabId: pa.id,
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
                children: G ? /* @__PURE__ */ r.jsx(pt, { size: 14 }) : /* @__PURE__ */ r.jsx(Mt, { size: 14 })
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
                children: P === "palette" ? /* @__PURE__ */ r.jsx(Zi, { size: 14 }) : /* @__PURE__ */ r.jsx(tn, { size: 14 })
              }
            )
          ] })
        ] }),
        de ? pa.render() : null
      ] }),
      de && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle left",
          role: "separator",
          "aria-label": "Resize activities panel",
          "aria-orientation": "vertical",
          "aria-valuemin": Dn,
          "aria-valuemax": $n,
          "aria-valuenow": H,
          tabIndex: 0,
          onPointerDown: (J) => ne("palette", J),
          onKeyDown: (J) => ge("palette", J)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("main", { className: "wf-canvas-shell", children: [
        /* @__PURE__ */ r.jsx("div", { className: "wf-canvas-tabs", children: /* @__PURE__ */ r.jsx(
          zn,
          {
            label: "Editor view tabs",
            tabs: Of,
            activeTabId: B,
            onSelect: (J) => Z(J)
          }
        ) }),
        B === "code" ? /* @__PURE__ */ r.jsx(oj, { draft: l, onApply: Ci }) : B === "properties" ? /* @__PURE__ */ r.jsx(vj, { details: Ne, draft: l, context: e, onStateChange: uo, onDefinitionMetaChange: re }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
          /* @__PURE__ */ r.jsx(na, { frames: d, onNavigate: (J) => g(J, null) }),
          /* @__PURE__ */ r.jsxs("div", { className: "wf-canvas", ref: mi, onDragOver: vn, onDragLeave: wi, onDrop: vi, children: [
            /* @__PURE__ */ r.jsx(sf.Provider, { value: Br, children: /* @__PURE__ */ r.jsx(af.Provider, { value: we, children: /* @__PURE__ */ r.jsx(cf.Provider, { value: Tf, children: /* @__PURE__ */ r.jsxs(
              Ks,
              {
                nodes: Xt,
                edges: gi,
                nodeTypes: ea,
                edgeTypes: ta,
                onInit: yi,
                onNodesChange: Kr,
                onEdgesChange: qr,
                onNodesDelete: Xr,
                onEdgesDelete: xi,
                onConnect: Yr,
                onConnectStart: lt ? Zr : void 0,
                onConnectEnd: lt ? Jr : void 0,
                onReconnect: lt ? Gr : void 0,
                isValidConnection: Ur,
                onDragOver: vn,
                onDragLeave: wi,
                onDrop: vi,
                onPaneClick: () => j(null),
                onNodeClick: (J, ue) => j(ue.id),
                onNodeDragStop: ve ? void 0 : Qr,
                minZoom: 0.2,
                maxZoom: 1.8,
                nodesConnectable: lt,
                nodesDraggable: !ve,
                selectionOnDrag: !0,
                multiSelectionKeyCode: ["Shift", "Meta", "Control"],
                deleteKeyCode: ve ? null : ["Backspace", "Delete"],
                panActivationKeyCode: null,
                defaultEdgeOptions: { type: "workflow" },
                children: [
                  /* @__PURE__ */ r.jsx(qs, { gap: 18, size: 1 }),
                  /* @__PURE__ */ r.jsx(Xs, {}),
                  /* @__PURE__ */ r.jsx(Us, { pannable: !0, zoomable: !0 })
                ]
              }
            ) }) }) }),
            so ? /* @__PURE__ */ r.jsx(
              gS,
              {
                slotLabel: Se?.slot.label ?? "this slot",
                catalog: $e,
                onPickActivity: Si,
                onBrowseAll: bi
              }
            ) : lt && Xt.length === 0 ? /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-empty-canvas-add", onClick: () => bi(), children: [
              /* @__PURE__ */ r.jsx(en, { size: 15 }),
              " Add activity"
            ] }) : null,
            wn ? /* @__PURE__ */ r.jsx(
              xf,
              {
                clientX: wn.clientX,
                clientY: wn.clientY,
                activities: $e,
                onPick: no,
                onClose: () => Fr(null)
              }
            ) : null
          ] }),
          /* @__PURE__ */ r.jsx(dN, { draft: l, onRepair: Pf })
        ] })
      ] }),
      le && !P ? /* @__PURE__ */ r.jsx(
        "div",
        {
          className: "wf-side-resize-handle right",
          role: "separator",
          "aria-label": "Resize inspector panel",
          "aria-orientation": "vertical",
          "aria-valuemin": _t,
          "aria-valuemax": Dt,
          "aria-valuenow": U,
          tabIndex: 0,
          onPointerDown: (J) => ne("inspector", J),
          onKeyDown: (J) => ge("inspector", J)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsxs("aside", { className: "wf-inspector", "aria-label": "Inspector panel", children: [
        /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title", children: [
          /* @__PURE__ */ r.jsx(
            zn,
            {
              label: "Inspector panel tabs",
              tabs: ho,
              activeTabId: ha.id,
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
                children: X ? /* @__PURE__ */ r.jsx(Mt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
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
                children: P === "inspector" ? /* @__PURE__ */ r.jsx(Zi, { size: 14 }) : /* @__PURE__ */ r.jsx(tn, { size: 14 })
              }
            )
          ] })
        ] }),
        le ? ha.render() : null
      ] })
    ] })
  ] });
}
function yS({ page: e, pageSize: t, totalCount: n, onPageChange: i, onPageSizeChange: o }) {
  const s = lf(n, t), a = n === 0 ? 0 : (e - 1) * t + 1, c = Math.min(e * t, n);
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
      /* @__PURE__ */ r.jsx("select", { value: t, onChange: (u) => o(Number(u.target.value)), children: Sj.map((u) => /* @__PURE__ */ r.jsx("option", { value: u, children: u }, u)) })
    ] }),
    /* @__PURE__ */ r.jsxs("div", { className: "wf-page-controls", children: [
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => i(e - 1), disabled: e <= 1, "aria-label": "Previous page", title: "Previous page", children: [
        /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
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
function xS({ draft: e, creating: t, ai: n, suggestMetadataAction: i, onChange: o, onClose: s, onSubmit: a }) {
  const [c, u] = O(!1), [l, d] = O(""), [f, p] = O(!1), [h, m] = O(null), [v, x] = O(null), y = se(null), j = se(e);
  j.current = e;
  const g = se(o);
  g.current = o;
  const w = oe((b) => {
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
        const I = Aj(b.text);
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
            pS,
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
function wS({ context: e, ai: t, onOpen: n }) {
  const [i, o] = O(""), [s, a] = O("active"), [c, u] = O(1), [l, d] = O(Cj), [f, p] = O("loading"), [h, m] = O(""), [v, x] = O(""), [y, j] = O([]), [g, w] = O(0), [S, b] = O(() => /* @__PURE__ */ new Set()), [I, A] = O(null), [D, M] = O(!1), [k, R] = O([]), [L, C] = O("idle"), _ = se(null), N = ae(() => y.map((W) => W.id), [y]), E = Nt(t, "weaver.workflows.suggest-create-metadata"), T = Nt(t, "weaver.workflows.explain-definition"), $ = N.filter((W) => S.has(W)).length, z = N.length > 0 && $ === N.length, F = oe(async () => {
    p("loading"), m("");
    try {
      const W = await Ch(e, { search: i, state: s, page: c, pageSize: l }), ne = typeof W.totalCount == "number", ge = W.totalCount ?? W.definitions.length, ye = lf(ge, l);
      if (ge > 0 && c > ye) {
        u(ye);
        return;
      }
      j(ne ? W.definitions : Ej(W.definitions, c, l)), w(ge), p("ready");
    } catch (W) {
      m(W instanceof Error ? W.message : String(W)), p("failed");
    }
  }, [e, i, s, c, l]);
  Q(() => {
    F();
  }, [F]), Q(() => {
    _.current && (_.current.indeterminate = $ > 0 && !z);
  }, [z, $]);
  const B = oe(async () => {
    if (!(L === "loading" || L === "ready")) {
      C("loading");
      try {
        const W = await ri(e);
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
        const W = await Ah(e, {
          name: I.name.trim(),
          description: I.description.trim() || null,
          rootKind: I.rootKind,
          rootActivityVersionId: _j(I, k)
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
    if (await Ji().confirm({ message: `Delete workflow definition "${W.name}"? You can restore it from the Deleted view.`, confirmLabel: "Delete", tone: "danger" })) {
      x(""), m("");
      try {
        await _h(e, W.id), P(W.id, !1), x(`Deleted ${W.name}`), await G();
      } catch (ne) {
        m(ne instanceof Error ? ne.message : String(ne));
      }
    }
  }, ie = async (W) => {
    x(""), m("");
    try {
      await Dh(e, W.id), P(W.id, !1), x(`Restored ${W.name}`), await G();
    } catch (ne) {
      m(ne instanceof Error ? ne.message : String(ne));
    }
  }, fe = async (W) => {
    if (await Ji().confirm({ message: `Permanently delete workflow definition "${W.name}"? This removes its drafts, versions, layouts, and validations and cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) {
      x(""), m("");
      try {
        await $h(e, W.id), P(W.id, !1), x(`Permanently deleted ${W.name}`), await G();
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
        /* @__PURE__ */ r.jsx(hr, { size: 15 }),
        /* @__PURE__ */ r.jsx("input", { value: i, onChange: (W) => le(W.target.value), placeholder: "Search definitions" })
      ] }),
      /* @__PURE__ */ r.jsx("button", { type: "button", onClick: () => {
        F();
      }, children: "Refresh" }),
      /* @__PURE__ */ r.jsx("div", { className: "wf-actions", children: /* @__PURE__ */ r.jsxs("button", { type: "button", title: "Create workflow", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(en, { size: 15 }),
        " Create"
      ] }) })
    ] }),
    f === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: h, title: "Couldn't load workflow definitions" }) : null,
    f !== "failed" && h ? /* @__PURE__ */ r.jsxs("div", { className: "wf-alert", children: [
      /* @__PURE__ */ r.jsx(It, { size: 16 }),
      " ",
      h
    ] }) : null,
    v ? /* @__PURE__ */ r.jsxs("div", { className: "wf-status-line", children: [
      /* @__PURE__ */ r.jsx(fn, { size: 14 }),
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
    f === "loading" ? /* @__PURE__ */ r.jsx(ia, {}) : null,
    f === "ready" && y.length === 0 ? /* @__PURE__ */ r.jsx(
      ra,
      {
        icon: /* @__PURE__ */ r.jsx($l, { size: 22 }),
        title: `No ${s} workflow definitions`,
        description: "Create a workflow to start designing automation, or adjust your filters to see more.",
        action: /* @__PURE__ */ r.jsxs("button", { type: "button", className: "wf-link-button", onClick: Z, children: [
          /* @__PURE__ */ r.jsx(en, { size: 15 }),
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
              /* @__PURE__ */ r.jsx("span", { children: s === "deleted" ? _e(W.deletedAt) : W.draftId ? "Draft" : "None" }),
              /* @__PURE__ */ r.jsx("span", { children: _e(W.lastModifiedAt) }),
              /* @__PURE__ */ r.jsx("span", { className: "wf-row-actions", onClick: (ne) => ne.stopPropagation(), children: s === "active" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), n(W.id);
                }, children: "Open" }),
                /* @__PURE__ */ r.jsx("button", { type: "button", onClick: (ne) => {
                  ne.stopPropagation(), U(W.id);
                }, children: "Artifacts" }),
                T ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, T, W), children: [
                  /* @__PURE__ */ r.jsx(rt, { size: 13 }),
                  " Explain"
                ] }) : null,
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  te(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(Pt, { size: 13 }),
                  " Delete"
                ] })
              ] }) : /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
                /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
                  ie(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(ti, { size: 13 }),
                  " Restore"
                ] }),
                /* @__PURE__ */ r.jsxs("button", { type: "button", className: "danger", onClick: () => {
                  fe(W);
                }, children: [
                  /* @__PURE__ */ r.jsx(Pt, { size: 13 }),
                  " Delete permanently"
                ] })
              ] }) })
            ]
          },
          W.id
        ))
      ] }),
      /* @__PURE__ */ r.jsx(
        yS,
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
      xS,
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
function vS(e, t) {
  const n = /* @__PURE__ */ new Map();
  for (const s of t)
    n.set(s.activityTypeKey, [...n.get(s.activityTypeKey) ?? [], s]);
  const i = /* @__PURE__ */ new Map(), o = (s) => {
    const a = NS(s, n), c = s.authoredActivityId || s.executableNodeId;
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
      activityVersionId: a?.activityVersionId ?? SS(s),
      inputs: [],
      outputs: [],
      structure: CS(s, a, o)
    };
  };
  return { root: o(e), factsByNodeId: i };
}
function bS(e) {
  return !!e && !e.available;
}
function jS(e) {
  return e.split(".").filter(Boolean).at(-1) ?? e;
}
function NS(e, t) {
  const n = t.get(e.activityType) ?? [];
  return n.find((i) => i.version === e.activityTypeVersion) ?? n[0];
}
function SS(e) {
  return `executable-missing:${e.activityType}@${e.activityTypeVersion}`;
}
function CS(e, t, n) {
  const i = e.childSlots ?? [];
  if (i.length === 0) return null;
  const o = xr(t), s = o && (!e.structureKind || o.kind === e.structureKind) ? IS(i, o.payload.slots) : null;
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
    a[ES(a, kS(c.name))] = c.activities.map(n);
  return {
    kind: e.structureKind ?? `executable:${e.activityType}`,
    schemaVersion: "1.0.0",
    payload: a
  };
}
function IS(e, t) {
  const n = [];
  for (const i of e) {
    const o = t.find((s) => s.name === i.name);
    if (!o || o.collectionProperty || o.childProperty) return null;
    n.push({ slot: i, descriptor: o });
  }
  return n;
}
function kS(e) {
  const t = e.split(".").filter(Boolean).at(-1) ?? e;
  return t.charAt(0).toLowerCase() + t.slice(1);
}
function ES(e, t) {
  if (!(t in e)) return t;
  let n = 2;
  for (; `${t}${n}` in e; ) n += 1;
  return `${t}${n}`;
}
function AS({ context: e, ai: t, artifactId: n, sourceReferenceId: i, onSelectReference: o }) {
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
  } = sa(), T = Nt(t, "weaver.workflows.explain-executable"), $ = oe(async () => {
    if (!n) {
      u("No executable artifact id was provided."), a("failed");
      return;
    }
    a("loading"), u("");
    try {
      const [X, P] = await Promise.all([
        Vh(e, n, i),
        ri(e)
      ]);
      d({ detail: X, activityCatalog: P.activities }), b([]), A(null), a("ready");
    } catch (X) {
      d(null), u(VS(X, n)), a("failed");
    }
  }, [n, e, i]);
  Q(() => {
    $();
  }, [$]);
  const z = ae(() => LS(l?.detail ?? null), [l]);
  Q(() => {
    const X = z?.definitionId;
    if (!X) {
      p({ status: "absent" });
      return;
    }
    let P = !1;
    return p({ status: "loading" }), Gl(e, X).then(
      (q) => {
        P || p({ status: "ready", definition: q.definition });
      },
      (q) => {
        P || p(_f(q) ? { status: "absent" } : { status: "failed", error: q instanceof Error ? q.message : String(q) });
      }
    ), () => {
      P = !0;
    };
  }, [z?.definitionId, e]);
  const F = ae(
    () => l ? vS(l.detail.rootActivity, l.activityCatalog) : null,
    [l]
  ), B = async () => {
    if (!(!l || y)) {
      j(!0), m(""), x(""), w(null);
      try {
        const X = await bs(e, l.detail.artifactId), P = Js(X);
        w({ artifactId: l.detail.artifactId, workflowExecutionId: P }), m(`Started ${l.detail.artifactId}`);
      } catch (X) {
        x(Gs(X));
      } finally {
        j(!1);
      }
    }
  }, Z = () => {
    !l || !T || St(t, T, {
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
        /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
        " Executables"
      ] }),
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        $();
      }, children: [
        /* @__PURE__ */ r.jsx(ti, { size: 14 }),
        " Refresh"
      ] }),
      l ? /* @__PURE__ */ r.jsxs("button", { type: "button", disabled: y, onClick: () => {
        B();
      }, children: [
        /* @__PURE__ */ r.jsx(Rt, { size: 14 }),
        " ",
        y ? "Running..." : "Run"
      ] }) : null,
      l && T ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: Z, children: [
        /* @__PURE__ */ r.jsx(rt, { size: 13 }),
        " Explain"
      ] }) : null,
      l ? /* @__PURE__ */ r.jsx(
        RS,
        {
          reference: z,
          sourceDefinition: f,
          onOpen: U
        }
      ) : null
    ] }),
    v ? /* @__PURE__ */ r.jsx(Ct, { message: v, title: "The executable could not be run" }) : null,
    h ? /* @__PURE__ */ r.jsx(oa, { status: h, run: g }) : null,
    s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading executable..." }) : null,
    s === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: c }) : null,
    s === "ready" && l && F ? /* @__PURE__ */ r.jsxs("div", { className: G, style: L, children: [
      /* @__PURE__ */ r.jsx(
        _S,
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
          "aria-valuemin": _t,
          "aria-valuemax": Dt,
          "aria-valuenow": D,
          tabIndex: 0,
          onPointerDown: (X) => N("inspector", X),
          onKeyDown: (X) => E("inspector", X)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        $S,
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
function _S({ detail: e, activityCatalog: t, graph: n, chosenReference: i, frames: o, onNavigateToScope: s, selectedNodeId: a, onSelectNode: c }) {
  const u = ae(
    () => DS(n, t, e.chosenReference?.layout ?? [], o, s, a),
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
      e.chosenReference ? /* @__PURE__ */ r.jsx("span", { className: "wf-executable-selection", children: zS(e.chosenReference.selection, e.chosenReference.sourceReferenceId) }) : /* @__PURE__ */ r.jsx("span", { className: "wf-executable-selection", children: "No source reference; automatic layout" })
    ] }),
    /* @__PURE__ */ r.jsx(na, { className: "wf-instance-breadcrumb", frames: o, onNavigate: s }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-canvas", children: u.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "This executable has no renderable activities." }) : /* @__PURE__ */ r.jsxs(
      Ks,
      {
        nodes: u.nodes,
        edges: u.edges,
        nodeTypes: ea,
        edgeTypes: ta,
        fitView: !0,
        nodesDraggable: !1,
        nodesConnectable: !1,
        elementsSelectable: !0,
        onNodeClick: (d, f) => c(f.id),
        onPaneClick: () => c(null),
        children: [
          /* @__PURE__ */ r.jsx(qs, {}),
          /* @__PURE__ */ r.jsx(Us, { pannable: !0, zoomable: !0 }),
          /* @__PURE__ */ r.jsx(Xs, {})
        ]
      },
      l
    ) })
  ] });
}
function DS(e, t, n, i, o, s = null) {
  const a = nn(e.root, i, t), c = a?.owner ?? e.root, u = t.find((f) => f.activityVersionId === c.activityVersionId), d = ws(c, u) === "unsupported" || !a ? xs(c, t, n) : ys(a, t, n);
  return {
    nodes: d.nodes.map((f) => {
      const p = e.factsByNodeId.get(f.id), h = bS(p), m = f.id === s;
      return {
        ...f,
        draggable: !1,
        connectable: !1,
        deletable: !1,
        selected: m,
        data: {
          ...f.data,
          ...h && p ? { ghost: !0, label: jS(p.activityType) } : {},
          onEnterSlot: (v) => {
            const x = Pn(i, c, f.id, v, Hn(f.data.label, v), t);
            x && o(x.frames);
          }
        }
      };
    }),
    edges: d.edges.map((f) => ({ ...f, deletable: !1 }))
  };
}
function $S({ detail: e, graph: t, chosenReference: n, sourceDefinition: i, selectedNodeId: o, onSelectReference: s, collapsed: a, expanded: c, maximized: u, onToggleCollapsed: l, onToggleMaximized: d }) {
  const [f, p] = O("identity"), [h, m] = O(""), v = [
    { id: "identity", title: "Identity", order: 0, icon: /* @__PURE__ */ r.jsx(op, { size: 14 }), render: () => null },
    { id: "references", title: `References (${e.references.length})`, order: 1, icon: /* @__PURE__ */ r.jsx(pr, { size: 14 }), render: () => null }
  ], x = o ? t.factsByNodeId.get(o) : null;
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Executable details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(zn, { label: "Executable details tabs", tabs: v, activeTabId: f, onSelect: (y) => p(y) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": a ? "Expand executable details panel" : "Collapse executable details panel",
            title: a ? "Expand" : "Collapse",
            onClick: l,
            children: a ? /* @__PURE__ */ r.jsx(Mt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
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
            children: u ? /* @__PURE__ */ r.jsx(Zi, { size: 14 }) : /* @__PURE__ */ r.jsx(tn, { size: 14 })
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
            /* @__PURE__ */ r.jsx("dd", { children: _e(e.createdAt) })
          ] }),
          h ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: h }) : null
        ] }),
        /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
          /* @__PURE__ */ r.jsx("h4", { children: "Source" }),
          n ? /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
            /* @__PURE__ */ r.jsx("dt", { children: "Kind" }),
            /* @__PURE__ */ r.jsx("dd", { children: Zs(n.sourceKind) }),
            /* @__PURE__ */ r.jsx("dt", { children: "Definition" }),
            /* @__PURE__ */ r.jsxs("dd", { children: [
              n.definitionId,
              " ",
              /* @__PURE__ */ r.jsx("small", { children: n.definitionVersionId })
            ] }),
            /* @__PURE__ */ r.jsx("dt", { children: "Scope" }),
            /* @__PURE__ */ r.jsx("dd", { children: ff(n.scope) })
          ] }) : /* @__PURE__ */ r.jsx("p", { className: "wf-muted", children: "This artifact carries no source references." }),
          /* @__PURE__ */ r.jsx(MS, { reference: n, sourceDefinition: i })
        ] }),
        /* @__PURE__ */ r.jsx(TS, { fact: x ?? null })
      ] }) : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
        /* @__PURE__ */ r.jsx("h4", { children: "Source references" }),
        /* @__PURE__ */ r.jsx(
          wf,
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
function TS({ fact: e }) {
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
function RS({ reference: e, sourceDefinition: t, onOpen: n }) {
  const i = PS(e, t);
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
function PS(e, t) {
  return e ? t.status === "absent" ? "This workflow definition is not available in this environment." : t.status === "failed" ? `The source definition could not be loaded: ${t.error}` : t.status === "loading" || t.status === "idle" ? "Checking the source definition..." : null : "This artifact carries no source references.";
}
function MS({ reference: e, sourceDefinition: t }) {
  if (!e) return null;
  if (t.status === "absent")
    return /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "This workflow definition is not available in this environment. The artifact renders from its own Execution Material and carried layout." });
  if (t.status !== "ready") return null;
  const n = Oj(e, t.definition);
  return n.kind !== "behind" ? null : /* @__PURE__ */ r.jsxs("p", { className: "wf-instance-note wf-executable-drift", role: "note", children: [
    "This reference was published from version ",
    n.referenceVersion ?? e.definitionVersionId,
    "; the definition's latest is ",
    n.latestVersion ?? t.definition.latestVersionId,
    "."
  ] });
}
function LS(e) {
  if (!e) return null;
  const t = e.chosenReference?.sourceReferenceId;
  return e.references.find((n) => n.sourceReferenceId === t) ?? e.references[0] ?? null;
}
function zS(e, t) {
  const n = e.trim().toLowerCase();
  return n === "requested" ? `Showing requested reference ${t}` : n === "newest-live" ? `Showing newest live reference ${t}` : n === "newest" ? `Showing newest reference ${t} (no live references remain)` : `Showing reference ${t}`;
}
function VS(e, t) {
  return _f(e) ? `Executable artifact ${t} was not found.` : e instanceof Error ? e.message : String(e);
}
function _f(e) {
  const t = e instanceof Error ? e.message : String(e);
  return /\b404\b/.test(t) || /not found/i.test(t);
}
function OS({ activities: e, activityCatalog: t, selectedEvidenceId: n = null, onSelectEvidence: i }) {
  const o = ae(
    () => new Map(t.map((a) => [a.activityTypeKey, a])),
    [t]
  ), s = ae(() => WS(e), [e]);
  return s.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty wf-timeline-empty", children: "No activity executions recorded yet." }) : /* @__PURE__ */ r.jsx("ol", { className: "wf-timeline", "aria-label": "Execution timeline", children: s.map((a) => {
    const c = o.get(a.activityType), u = gn(c), l = c ? Ie(c) : zt(a.activityType) ?? a.activityType, d = zt(a.activityType) ?? a.activityType, f = FS(a.startedAt ?? a.scheduledAt), p = Cs(a.startedAt, a.completedAt);
    return /* @__PURE__ */ r.jsx("li", { children: /* @__PURE__ */ r.jsxs(
      "button",
      {
        type: "button",
        className: "wf-timeline-entry",
        "data-selected": a.activityExecutionId === n,
        onClick: () => i?.(a.activityExecutionId),
        children: [
          /* @__PURE__ */ r.jsx("span", { className: "wf-timeline-icon wf-activity-icon", "data-icon": u, "aria-hidden": "true", children: vr(u) }),
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
          /* @__PURE__ */ r.jsx(HS, { status: a.status })
        ]
      }
    ) }, a.activityExecutionId);
  }) });
}
function HS({ status: e }) {
  const t = e.toLowerCase(), n = t === "completed" ? "OK" : e;
  return /* @__PURE__ */ r.jsx("span", { className: "wf-status-badge wf-timeline-pill", "data-status": t, children: n });
}
function WS(e) {
  return e.map((t, n) => ({ activity: t, index: n })).sort((t, n) => bl(t.activity) - bl(n.activity) || t.index - n.index).map((t) => t.activity);
}
function bl(e) {
  const t = e.startedAt ?? e.scheduledAt, n = t ? Date.parse(t) : Number.NaN;
  return Number.isNaN(n) ? Number.MAX_SAFE_INTEGER : n;
}
function FS(e) {
  if (!e) return "";
  const t = new Date(e);
  return Number.isNaN(t.getTime()) ? "" : t.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: !1 });
}
function BS({ context: e }) {
  const [t, n] = O("loading"), [i, o] = O(""), [s, a] = O(""), [c, u] = O(""), [l, d] = O([]), f = oe(async () => {
    n("loading"), o("");
    try {
      const h = await Wh(e, {
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
    t === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: i }) : null,
    t === "loading" ? /* @__PURE__ */ r.jsx(ia, {}) : null,
    t === "ready" && l.length === 0 ? /* @__PURE__ */ r.jsx(
      ra,
      {
        icon: /* @__PURE__ */ r.jsx(ei, { size: 22 }),
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
            /* @__PURE__ */ r.jsx("span", { children: pf(h.runKind) }),
            /* @__PURE__ */ r.jsx("span", { children: /* @__PURE__ */ r.jsx(yn, { status: h.status, subStatus: h.subStatus }) }),
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
            /* @__PURE__ */ r.jsx("span", { children: _e(h.startedAt ?? h.createdAt) }),
            /* @__PURE__ */ r.jsx("span", { children: Cs(h.startedAt ?? h.createdAt, h.completedAt ?? h.updatedAt) })
          ]
        },
        h.workflowExecutionId
      ))
    ] }) : null
  ] });
}
function KS({ context: e, ai: t, workflowExecutionId: n }) {
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
  } = sa(), b = Nt(t, "weaver.workflows.explain-instance"), I = oe(async () => {
    if (!n) {
      a("No workflow execution id was provided."), o("failed");
      return;
    }
    o("loading"), a("");
    try {
      const k = await Fh(e, n), [R, L] = await Promise.all([
        Eh(e, k.instance.definitionVersionId).then(
          (C) => ({ definitionVersion: C, error: "" }),
          (C) => ({ definitionVersion: null, error: C instanceof Error ? C.message : String(C) })
        ),
        ri(e)
      ]);
      u({
        details: k,
        definitionVersion: R.definitionVersion,
        definitionVersionError: R.error,
        activityCatalog: L.activities
      }), d(null), p([]), o("ready");
    } catch (k) {
      u(null), a(tN(k, n)), o("failed");
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
        /* @__PURE__ */ r.jsx(Mt, { size: 14 }),
        " Runs"
      ] }),
      c?.details.instance.definitionId ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: D, children: [
        /* @__PURE__ */ r.jsx(ps, { size: 14 }),
        " Designer"
      ] }) : null,
      /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => {
        I();
      }, children: [
        /* @__PURE__ */ r.jsx(ti, { size: 14 }),
        " Refresh"
      ] }),
      c && b ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, b, c.details), children: [
        /* @__PURE__ */ r.jsx(rt, { size: 13 }),
        " Explain"
      ] }) : null
    ] }),
    i === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading workflow run..." }) : null,
    i === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: s }) : null,
    i === "ready" && c ? /* @__PURE__ */ r.jsxs("div", { className: M, style: y, children: [
      /* @__PURE__ */ r.jsx(
        qS,
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
          "aria-valuemin": _t,
          "aria-valuemax": Dt,
          "aria-valuenow": h,
          tabIndex: 0,
          onPointerDown: (k) => w("inspector", k),
          onKeyDown: (k) => S("inspector", k)
        }
      ) : /* @__PURE__ */ r.jsx("div", { className: "wf-side-resize-spacer" }),
      /* @__PURE__ */ r.jsx(
        ZS,
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
          graphNodeIds: c.definitionVersion ? YS(c.definitionVersion, c.activityCatalog) : void 0,
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
function qS({
  definitionVersion: e,
  definitionVersionError: t,
  activityCatalog: n,
  details: i,
  selectedEvidenceId: o,
  onSelectEvidence: s,
  frames: a,
  onNavigateToScope: c
}) {
  const u = US(a), l = ae(
    () => XS(e, n, i, o, a, c),
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
      /* @__PURE__ */ r.jsx(yn, { status: i.instance.status, subStatus: i.instance.subStatus })
    ] }),
    e ? /* @__PURE__ */ r.jsx(na, { className: "wf-instance-breadcrumb", frames: a, onNavigate: c }) : null,
    /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-canvas", children: [
      e ? null : /* @__PURE__ */ r.jsxs("div", { className: "wf-empty", children: [
        "The workflow run loaded, but its definition graph could not be resolved for this version.",
        t ? /* @__PURE__ */ r.jsx("small", { children: eN(t) }) : null
      ] }),
      e && l.nodes.length === 0 ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "No workflow activities are available for this definition version." }) : null,
      l.nodes.length > 0 ? /* @__PURE__ */ r.jsxs(
        Ks,
        {
          nodes: l.nodes,
          edges: l.edges,
          nodeTypes: ea,
          edgeTypes: ta,
          fitView: !0,
          nodesDraggable: !1,
          nodesConnectable: !1,
          elementsSelectable: !0,
          onNodeClick: (d, f) => s(f.id),
          onPaneClick: () => s(null),
          children: [
            /* @__PURE__ */ r.jsx(qs, {}),
            /* @__PURE__ */ r.jsx(Us, { pannable: !0, zoomable: !0 }),
            /* @__PURE__ */ r.jsx(Xs, {})
          ]
        },
        u
      ) : null
    ] })
  ] });
}
function XS(e, t, n, i, o, s) {
  const a = e?.state.rootActivity;
  if (!e || !a) return { nodes: [], edges: [] };
  const c = nn(a, o, t), u = c?.owner ?? a, l = t.find((h) => h.activityVersionId === u.activityVersionId), f = ws(u, l) === "unsupported" || !c ? xs(u, t, e.layout) : ys(c, t, e.layout), p = f.nodes.map((h) => ({
    ...h,
    draggable: !1,
    connectable: !1,
    deletable: !1,
    data: {
      ...h.data,
      onEnterSlot: (m) => {
        const v = Pn(o, u, h.id, m, Hn(h.data.label, m), t);
        v && s(v.frames);
      }
    }
  }));
  return {
    nodes: Kp(p, n.activities, n.incidents, i),
    edges: f.edges.map((h) => ({ ...h, deletable: !1 }))
  };
}
function US(e) {
  return e.length === 0 ? "root" : e.map((t) => `${t.ownerNodeId}:${t.slotId}`).join("/");
}
function YS(e, t) {
  const n = /* @__PURE__ */ new Set(), i = (o) => {
    if (o) {
      n.add(o.nodeId);
      for (const s of Re(o, t))
        s.activities.forEach(i);
    }
  };
  return i(e.state.rootActivity), n;
}
function ZS({
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
  const g = o?.incidents.length ?? 0, w = JS(o?.activities ?? [], c), S = (I) => {
    u?.(I), j("activity");
  }, b = [
    { id: "timeline", title: "Timeline", order: 0, icon: /* @__PURE__ */ r.jsx(pr, { size: 14 }), render: () => null },
    { id: "activity", title: "Activity", order: 1, icon: /* @__PURE__ */ r.jsx(Al, { size: 14 }), render: () => null },
    { id: "issues", title: g > 0 ? `Issues (${g})` : "Issues", order: 2, icon: /* @__PURE__ */ r.jsx(It, { size: 14 }), render: () => null },
    { id: "details", title: "Details", order: 3, icon: /* @__PURE__ */ r.jsx(us, { size: 14 }), render: () => null }
  ];
  return /* @__PURE__ */ r.jsxs("aside", { className: "wf-instance-inspector", "aria-label": "Run details panel", children: [
    /* @__PURE__ */ r.jsxs("div", { className: "wf-panel-title wf-instance-panel-title", children: [
      /* @__PURE__ */ r.jsx(zn, { label: "Run details tabs", tabs: b, activeTabId: y, onSelect: (I) => j(I) }),
      /* @__PURE__ */ r.jsxs("span", { className: "wf-panel-actions", children: [
        /* @__PURE__ */ r.jsx(
          "button",
          {
            type: "button",
            className: "wf-panel-action-button",
            "aria-label": p ? "Expand run details panel" : "Collapse run details panel",
            title: p ? "Expand" : "Collapse",
            onClick: v,
            children: p ? /* @__PURE__ */ r.jsx(Mt, { size: 14 }) : /* @__PURE__ */ r.jsx(pt, { size: 14 })
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
            children: m ? /* @__PURE__ */ r.jsx(Zi, { size: 14 }) : /* @__PURE__ */ r.jsx(tn, { size: 14 })
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
        n ? /* @__PURE__ */ r.jsxs("button", { type: "button", onClick: () => St(t, n, o ?? i), children: [
          /* @__PURE__ */ r.jsx(rt, { size: 13 }),
          " Explain"
        ] }) : null
      ] }),
      s === "loading" ? /* @__PURE__ */ r.jsx("div", { className: "wf-empty", children: "Loading run details..." }) : null,
      s === "failed" ? /* @__PURE__ */ r.jsx(Ct, { message: a }) : null,
      s === "ready" && o ? /* @__PURE__ */ r.jsx("div", { className: "wf-instance-tab-content", children: y === "timeline" ? /* @__PURE__ */ r.jsx(
        OS,
        {
          activities: o.activities,
          activityCatalog: f,
          selectedEvidenceId: c,
          onSelectEvidence: S
        }
      ) : y === "activity" ? /* @__PURE__ */ r.jsx(GS, { context: e, activity: w, activityCatalog: f }) : y === "issues" ? /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
        /* @__PURE__ */ r.jsx(fC, { incidents: o.incidents, selectedEvidenceId: c, onSelectEvidence: u }),
        /* @__PURE__ */ r.jsx(yC, { details: o, graphNodeIds: l, rootNodeId: d })
      ] }) : /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(yn, { status: i.status, subStatus: i.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Run Kind" }),
        /* @__PURE__ */ r.jsx("dd", { children: pf(i.runKind) }),
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
        /* @__PURE__ */ r.jsx("dd", { children: _e(i.createdAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: _e(i.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: _e(i.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Correlation" }),
        /* @__PURE__ */ r.jsx("dd", { children: i.correlationId || "None" })
      ] }) }) : null
    ] }) : null
  ] });
}
function JS(e, t) {
  if (!t) return null;
  const n = e.find((o) => o.activityExecutionId === t);
  if (n) return n;
  const i = e.filter((o) => o.executableNodeId === t || o.authoredActivityId === t);
  return i.length > 0 ? Yl(i) : null;
}
function GS({ context: e, activity: t, activityCatalog: n }) {
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
    return a({ activityExecutionId: p, status: "loading", inspection: null, error: "" }), Bh(e, o, p).then(
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
  const u = n.find((f) => f.activityTypeKey === t.activityType)?.displayName || zt(t.activityType) || t.activityType, l = t.bookmarkIds?.length ?? 0, d = t.incidentIds?.length ?? 0;
  return /* @__PURE__ */ r.jsxs(r.Fragment, { children: [
    /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
      /* @__PURE__ */ r.jsx("h4", { children: "Activity" }),
      /* @__PURE__ */ r.jsxs("dl", { className: "wf-instance-meta", children: [
        /* @__PURE__ */ r.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ r.jsx("dd", { children: u }),
        /* @__PURE__ */ r.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ r.jsx("dd", { children: /* @__PURE__ */ r.jsx(yn, { status: t.status, subStatus: t.subStatus }) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Activity Execution ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.activityExecutionId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Authored Activity ID" }),
        /* @__PURE__ */ r.jsx("dd", { children: t.authoredActivityId }),
        /* @__PURE__ */ r.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ r.jsxs("dd", { children: [
          zt(t.activityType) ?? t.activityType,
          " ",
          /* @__PURE__ */ r.jsx("small", { children: t.activityTypeVersion })
        ] }),
        /* @__PURE__ */ r.jsx("dt", { children: "Started" }),
        /* @__PURE__ */ r.jsx("dd", { children: _e(t.startedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Completed" }),
        /* @__PURE__ */ r.jsx("dd", { children: _e(t.completedAt) }),
        /* @__PURE__ */ r.jsx("dt", { children: "Duration" }),
        /* @__PURE__ */ r.jsx("dd", { children: Cs(t.startedAt, t.completedAt) || "Unknown" }),
        /* @__PURE__ */ r.jsx("dt", { children: "Bookmarks" }),
        /* @__PURE__ */ r.jsx("dd", { children: l }),
        /* @__PURE__ */ r.jsx("dt", { children: "Incidents" }),
        /* @__PURE__ */ r.jsx("dd", { children: d })
      ] })
    ] }),
    /* @__PURE__ */ r.jsx(
      jl,
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
      jl,
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
function jl({
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
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-input-list", children: a.map((c) => /* @__PURE__ */ r.jsx(QS, { snapshot: c }, `${c.name}:${c.capturedAt}:${c.captureMode}`)) })
  ] });
}
function QS({ snapshot: e }) {
  const t = e.type?.displayName || e.type?.typeName || e.type?.alias || "Unknown", n = e.snapshot ?? (e.captureMode === "DiagnosticSnapshot" ? e.payload : null), i = e.captureMode === "Payload" && e.payload !== void 0;
  return /* @__PURE__ */ r.jsxs("article", { className: "wf-runtime-input", children: [
    /* @__PURE__ */ r.jsxs("header", { children: [
      /* @__PURE__ */ r.jsxs("span", { children: [
        /* @__PURE__ */ r.jsx("strong", { children: e.name }),
        /* @__PURE__ */ r.jsx("small", { children: t })
      ] }),
      /* @__PURE__ */ r.jsx("span", { className: "wf-runtime-capture-mode", children: sC(e.captureMode) })
    ] }),
    lC(n) ? /* @__PURE__ */ r.jsx(la, { node: n }) : i ? /* @__PURE__ */ r.jsx(oC, { payload: e.payload }) : /* @__PURE__ */ r.jsx("p", { children: cC(e) }),
    e.isSensitive ? /* @__PURE__ */ r.jsx("p", { className: "wf-instance-note", children: "Marked sensitive by runtime evidence." }) : null
  ] });
}
const eC = {
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
function tC(e) {
  return Object.hasOwn(eC, e.kind);
}
function la({ node: e, depth: t = 0 }) {
  if (!tC(e))
    return /* @__PURE__ */ r.jsx(Nl, { node: { kind: "unsupported", reason: `Unknown snapshot node: ${e.kind}` } });
  switch (e.kind) {
    case "null":
      return /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: "null" });
    case "scalar":
    case "number":
      return /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: Df(e.value) });
    case "string":
      return /* @__PURE__ */ r.jsxs("code", { className: "wf-runtime-input-value", children: [
        e.preview ?? "",
        e.truncated ? ` (${e.length ?? "unknown"} chars, truncated)` : ""
      ] });
    case "object":
      return /* @__PURE__ */ r.jsx(nC, { node: e, depth: t });
    case "array":
      return /* @__PURE__ */ r.jsx(iC, { node: e, depth: t });
    case "redacted":
    case "truncated":
    case "unsupported":
    case "error":
    case "permissionHidden":
      return /* @__PURE__ */ r.jsx(Nl, { node: e });
    case "payloadReference":
      return /* @__PURE__ */ r.jsx(rC, { node: e });
  }
}
function nC({ node: e, depth: t }) {
  const n = e.properties ?? [];
  return n.length === 0 ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: "{}" }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-snapshot-node", open: t === 0, children: [
    /* @__PURE__ */ r.jsxs("summary", { children: [
      e.typeName || "Object",
      e.truncated ? " (truncated)" : ""
    ] }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-runtime-snapshot-children", children: n.map((i) => /* @__PURE__ */ r.jsxs("div", { className: "wf-runtime-snapshot-property", children: [
      /* @__PURE__ */ r.jsx("span", { children: i.name }),
      /* @__PURE__ */ r.jsx(la, { node: i.value, depth: t + 1 })
    ] }, i.name)) })
  ] });
}
function iC({ node: e, depth: t }) {
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
      /* @__PURE__ */ r.jsx(la, { node: i, depth: t + 1 })
    ] }, o)) })
  ] });
}
function Nl({ node: e }) {
  const t = e.message || e.reason || e.requiredPermission || e.displayName;
  return /* @__PURE__ */ r.jsxs("span", { className: `wf-runtime-snapshot-marker ${e.kind}`, children: [
    aC(e.kind),
    t ? `: ${t}` : "",
    e.omittedCount ? ` (${e.omittedCount} omitted)` : ""
  ] });
}
function rC({ node: e }) {
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
function oC({ payload: e }) {
  const t = Df(e);
  return t.length <= 160 && !t.includes(`
`) ? /* @__PURE__ */ r.jsx("code", { className: "wf-runtime-input-value", children: t }) : /* @__PURE__ */ r.jsxs("details", { className: "wf-runtime-input-value-details", children: [
    /* @__PURE__ */ r.jsx("summary", { children: uC(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] });
}
function sC(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function aC(e) {
  return e.replace(/([a-z])([A-Z])/g, "$1 $2");
}
function cC(e) {
  return e.state === "permissionHidden" ? "Runtime value evidence is hidden by permissions." : e.state === "metadataOnly" ? e.captureReason || "Runtime value evidence is metadata-only." : e.state === "notCaptured" ? e.captureReason || "Runtime value evidence was not captured." : e.captureReason || "The runtime capture policy did not include this value.";
}
function lC(e) {
  return typeof e == "object" && e !== null && typeof e.kind == "string";
}
function uC(e) {
  const t = e.split(`
`, 1)[0] || e;
  return t.length > 120 ? `${t.slice(0, 117)}...` : t;
}
function Df(e) {
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
const dC = [
  "runtime.faultStackTrace",
  "runtime.exceptionStackTrace",
  "runtime.stackTrace",
  "faultStackTrace",
  "exceptionStackTrace",
  "stackTrace"
];
function fC({ incidents: e, selectedEvidenceId: t = null, onSelectEvidence: n }) {
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
              value: xC(s),
              ariaLabel: `Copy incident ${s.failureType}`,
              copiedLabel: "incident",
              onCopied: (a) => o(`Copied ${a}`),
              onCopyFailed: (a) => o(`Could not copy ${a}.`)
            }
          ),
          /* @__PURE__ */ r.jsx(pC, { incident: s })
        ]
      },
      s.incidentId
    )),
    i ? /* @__PURE__ */ r.jsx("p", { className: "wf-copy-status", role: "status", children: i }) : null
  ] });
}
function pC({ incident: e }) {
  const t = hC(e);
  return t ? /* @__PURE__ */ r.jsxs("details", { className: "wf-incident-stacktrace", children: [
    /* @__PURE__ */ r.jsx("summary", { children: mC(t) }),
    /* @__PURE__ */ r.jsx("pre", { children: t })
  ] }) : null;
}
function hC(e) {
  const t = gC(e.stackTrace, e.exceptionStackTrace);
  if (t) return t;
  for (const n of dC) {
    const i = e.metadata?.[n];
    if (i && i.trim()) return i;
  }
  return null;
}
function gC(...e) {
  return e.find((t) => t?.trim()) ?? null;
}
function mC(e) {
  const n = (e.split(`
`).find((i) => i.trim()) ?? e).trim();
  return n.length > 120 ? `${n.slice(0, 117)}...` : n;
}
function yC({ details: e, graphNodeIds: t, rootNodeId: n }) {
  if (!t) return null;
  const i = e.activities.filter((o) => {
    const s = qj(o);
    return s && s !== n && !t.has(s);
  });
  return i.length === 0 ? null : /* @__PURE__ */ r.jsxs("section", { className: "wf-instance-section", children: [
    /* @__PURE__ */ r.jsx("h4", { children: "Executions outside canvas" }),
    /* @__PURE__ */ r.jsx("div", { className: "wf-instance-unmatched-list", children: i.map((o) => /* @__PURE__ */ r.jsxs("div", { className: "wf-instance-unmatched", children: [
      /* @__PURE__ */ r.jsx("strong", { children: zt(o.activityType) ?? o.activityType }),
      /* @__PURE__ */ r.jsx("small", { children: o.activityExecutionId })
    ] }, `activity-${o.activityExecutionId}`)) })
  ] });
}
function xC(e) {
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
    `Created: ${_e(e.createdAt)}`,
    e.resolvedAt ? `Resolved: ${_e(e.resolvedAt)}` : "",
    "",
    e.message
  ].filter(Boolean);
  return e.metadata && Object.keys(e.metadata).length > 0 && t.push("", "Metadata:", JSON.stringify(e.metadata, null, 2)), t.join(`
`);
}
function wC({
  context: e,
  ai: t,
  propertyEditors: n,
  expressionEditors: i,
  workflowDesignerPanels: o,
  autosaveEnabledByDefault: s
}) {
  const [a, c] = O(Sl);
  Q(() => {
    const l = () => c(Sl());
    return window.addEventListener("popstate", l), () => window.removeEventListener("popstate", l);
  }, []);
  const u = (l) => {
    const d = l ? `/workflows/definitions?definition=${encodeURIComponent(l)}` : "/workflows/definitions";
    window.history.pushState({}, "", d), window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return a ? /* @__PURE__ */ r.jsx(mS, { context: e, definitionId: a, ai: t, propertyEditors: n, expressionEditors: i, workflowDesignerPanels: o, autosaveEnabledByDefault: s, onBack: () => u(null) }) : /* @__PURE__ */ r.jsx(di, { title: "Definitions", children: /* @__PURE__ */ r.jsx(wS, { context: e, ai: t, onOpen: u }) });
}
function vC({ context: e, ai: t }) {
  const [n, i] = O(Cl);
  Q(() => {
    const s = () => i(Cl());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = oe((s) => {
    const a = s?.trim() ?? "", c = new URL(window.location.href);
    a ? c.searchParams.set("definition", a) : c.searchParams.delete("definition"), i(a || null), window.history.replaceState({}, "", `${c.pathname}${c.search}${c.hash}`);
  }, []);
  return /* @__PURE__ */ r.jsx(di, { title: "Executables", children: /* @__PURE__ */ r.jsx(hN, { context: e, ai: t, definitionFilter: n, onDefinitionFilterChange: o }) });
}
function bC({ context: e, ai: t }) {
  const [n, i] = O(Mo);
  Q(() => {
    const s = () => i(Mo());
    return window.addEventListener("popstate", s), () => window.removeEventListener("popstate", s);
  }, []);
  const o = oe((s) => {
    const a = new URL(window.location.href);
    s ? a.searchParams.set("ref", s) : a.searchParams.delete("ref"), window.history.replaceState({}, "", `${a.pathname}${a.search}${a.hash}`), i(Mo());
  }, []);
  return /* @__PURE__ */ r.jsx(di, { title: "Executable Inspector", children: /* @__PURE__ */ r.jsx(
    AS,
    {
      context: e,
      ai: t,
      artifactId: n.artifactId,
      sourceReferenceId: n.sourceReferenceId,
      onSelectReference: o
    }
  ) });
}
function jC({ context: e }) {
  return /* @__PURE__ */ r.jsx(di, { title: "Runs", children: /* @__PURE__ */ r.jsx(BS, { context: e }) });
}
function NC({ context: e, ai: t }) {
  const n = SC();
  return /* @__PURE__ */ r.jsx(di, { title: "Run", children: /* @__PURE__ */ r.jsx(KS, { context: e, ai: t, workflowExecutionId: n }) });
}
function di({ title: e, children: t }) {
  return /* @__PURE__ */ r.jsxs("section", { className: "wf-page", children: [
    /* @__PURE__ */ r.jsx("div", { className: "wf-page-header", children: /* @__PURE__ */ r.jsxs("div", { children: [
      /* @__PURE__ */ r.jsx("span", { className: "wf-kicker", children: "Workflow management" }),
      /* @__PURE__ */ r.jsx("h2", { children: e })
    ] }) }),
    t
  ] });
}
function Sl() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Cl() {
  return new URLSearchParams(window.location.search).get("definition");
}
function Mo() {
  const e = /^\/workflows\/executables\/([^/]+)$/.exec(window.location.pathname);
  return {
    artifactId: e ? decodeURIComponent(e[1]) : "",
    sourceReferenceId: new URLSearchParams(window.location.search).get("ref")
  };
}
function SC() {
  const e = /^\/workflows\/instances\/([^/]+)$/.exec(window.location.pathname);
  return e ? decodeURIComponent(e[1]) : "";
}
function AC(e) {
  up(e.dialogs), e.featureAreas.add({
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
        component: () => /* @__PURE__ */ r.jsx(wC, { context: e.backend, ai: e.ai, propertyEditors: e.propertyEditors.list(), expressionEditors: e.expressionEditors?.list() ?? [], workflowDesignerPanels: e.workflowDesigner.panels.list(), autosaveEnabledByDefault: e.runtime.workflows?.autosaveEnabledByDefault ?? !0 })
      },
      {
        id: "workflows-executables",
        path: "/workflows/executables",
        label: "Workflow executables",
        component: () => /* @__PURE__ */ r.jsx(vC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-executable-inspector",
        path: "/workflows/executables/:artifactId",
        label: "Executable Inspector",
        component: () => /* @__PURE__ */ r.jsx(bC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-instances",
        path: "/workflows/instances",
        label: "Workflow runs",
        component: () => /* @__PURE__ */ r.jsx(jC, { context: e.backend })
      },
      {
        id: "workflows-instance-detail",
        path: "/workflows/instances/:workflowExecutionId",
        label: "Workflow run",
        component: () => /* @__PURE__ */ r.jsx(NC, { context: e.backend, ai: e.ai })
      },
      {
        id: "workflows-activity-availability",
        path: "/workflows/activity-availability",
        label: "Activity availability",
        component: () => /* @__PURE__ */ r.jsx(Lg, { context: e.backend })
      },
      {
        id: "workflows-runtime-diagnostics",
        path: "/workflows/runtime-diagnostics",
        label: "Runtime diagnostics",
        component: () => /* @__PURE__ */ r.jsx(Og, { context: e.backend })
      }
    ]
  });
}
export {
  Uj as isConnectEndOverExistingWorkflowNode,
  AC as register,
  Yj as resolveConnectEndSource
};
