import { useState as x, useEffect as C, useMemo as A } from "react";
var R = { exports: {} }, S = {};
var Y;
function oe() {
  if (Y) return S;
  Y = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(i, r, l) {
    var o = null;
    if (l !== void 0 && (o = "" + l), r.key !== void 0 && (o = "" + r.key), "key" in r) {
      l = {};
      for (var c in r)
        c !== "key" && (l[c] = r[c]);
    } else l = r;
    return r = l.ref, {
      $$typeof: e,
      type: i,
      key: o,
      ref: r !== void 0 ? r : null,
      props: l
    };
  }
  return S.Fragment = t, S.jsx = s, S.jsxs = s, S;
}
var Q;
function ce() {
  return Q || (Q = 1, R.exports = oe()), R.exports;
}
var n = ce();
const w = "__all", K = "__uncategorized";
let T;
function Ie(e) {
  T = e, de(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: he
  });
}
function de(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || $(t, "select"),
    component: Ce
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => y(t.jsonType) === "boolean",
    component: ye
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(y(t.jsonType)),
    component: Se
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(y(t.jsonType)) || $(t, "json") || $(t, "textarea"),
    component: ke
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: Ne
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: ve
  });
}
function ue(e, t) {
  const s = e.settingEditors.list().filter((i) => i.supports(t)).sort((i, r) => (i.order ?? 500) - (r.order ?? 500))[0];
  if (!s)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return s;
}
function me(e, t) {
  return {
    revision: e,
    features: t.map((s) => ({
      id: s.id,
      enabled: s.enabled,
      configuration: s.enabled ? s.configuration : {}
    }))
  };
}
function ge(e, t) {
  return e ? X(e.features) !== X(t) : !1;
}
function he() {
  const [e, t] = x(null), [s, i] = x([]), [r, l] = x(""), [o, c] = x(() => /* @__PURE__ */ new Set()), [f, h] = x(w), [u, k] = x(!0), [j, _] = x(!1), [D, E] = x(null), [P, v] = x(null);
  C(() => {
    M();
  }, []);
  const F = A(() => we(s), [s]), g = A(
    () => Te(s, f),
    [s, f]
  ), b = g.find((a) => a.id === r) ?? g.find((a) => a.enabled) ?? g[0] ?? null, N = ge(e, s), te = s.filter((a) => a.enabled).length, se = F.find((a) => a.id === f)?.label ?? "All categories", z = A(() => new Set(g.map((a) => a.id)), [g]), q = g.filter((a) => o.has(a.id)).length, L = g.length > 0 && q === g.length, ae = s.filter((a) => o.has(a.id));
  C(() => {
    b && b.id !== r && l(b.id);
  }, [b, r]), C(() => {
    F.some((a) => a.id === f) || h(w);
  }, [F, f]), C(() => {
    c((a) => {
      const d = new Set(s.map((p) => p.id)), m = new Set(Array.from(a).filter((p) => d.has(p)));
      return m.size === a.size ? a : m;
    });
  }, [s]);
  async function M() {
    k(!0), v(null);
    try {
      const a = await T.backend.http.getJson("/modularity/features");
      t(a), i(a.features.map(J)), c(/* @__PURE__ */ new Set()), E(null);
    } catch (a) {
      v(Z(a));
    } finally {
      k(!1);
    }
  }
  async function V() {
    if (!(!e || !N)) {
      _(!0), v(null), E(null);
      try {
        const a = await T.backend.http.postJson(
          "/modularity/features/apply",
          me(e.revision, s)
        );
        t(a.catalog), i(a.catalog.features.map(J)), E(`Applied ${a.featureDescriptorCount} descriptor(s); reloaded ${a.reloadedShellCount} shell(s).`);
      } catch (a) {
        v(Z(a));
      } finally {
        _(!1);
      }
    }
  }
  function B(a, d) {
    i((m) => m.map((p) => p.id === a ? d(p) : p));
  }
  function H(a) {
    B(a.id, (d) => ({ ...d, enabled: !d.enabled }));
  }
  function ie(a, d) {
    c((m) => {
      const p = new Set(m);
      return d ? p.add(a) : p.delete(a), p;
    });
  }
  function re() {
    c((a) => {
      const d = new Set(a);
      if (L)
        for (const m of z)
          d.delete(m);
      else
        for (const m of z)
          d.add(m);
      return d;
    });
  }
  function G(a) {
    o.size !== 0 && i((d) => d.map((m) => o.has(m.id) ? { ...m, enabled: a } : m));
  }
  function le(a, d, m) {
    B(a.id, (p) => ({
      ...p,
      configuration: {
        ...p.configuration,
        [d.name]: m
      }
    }));
  }
  function U() {
    e && (i(e.features.map(J)), E(null), v(null));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          te,
          " enabled of ",
          s.length,
          " available",
          N ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: M, disabled: u || j, children: "Refresh" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: U, disabled: !N || u || j, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: V, disabled: !N || u || j, children: j ? "Applying" : "Apply" })
      ] })
    ] }),
    P ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: P }) : null,
    D ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: D }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        fe,
        {
          categories: F,
          selectedCategory: f,
          onSelect: h
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: se }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              g.length,
              " feature",
              g.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            g.filter((a) => a.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          pe,
          {
            disabled: u || j || g.length === 0,
            selectedCount: o.size,
            visibleCount: g.length,
            visibleSelectedCount: q,
            selectedEnabledCount: ae.filter((a) => a.enabled).length,
            allVisibleChecked: L,
            onToggleVisible: re,
            onClearSelection: () => c(/* @__PURE__ */ new Set()),
            onEnableSelected: () => G(!0),
            onDisableSelected: () => G(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": u, children: [
          u && s.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !u && s.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !u && s.length > 0 && g.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features match this category." }) : null,
          g.map((a) => /* @__PURE__ */ n.jsx(
            xe,
            {
              feature: a,
              selected: a.id === b?.id,
              checked: o.has(a.id),
              onSelect: () => l(a.id),
              onToggle: () => H(a),
              onCheckedChange: (d) => ie(a.id, d)
            },
            a.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        je,
        {
          feature: b,
          disabled: j || u,
          dirty: N,
          onToggle: H,
          onSettingChange: le,
          onReset: U,
          onApply: V
        }
      )
    ] })
  ] });
}
function pe({
  disabled: e,
  selectedCount: t,
  visibleCount: s,
  visibleSelectedCount: i,
  selectedEnabledCount: r,
  allVisibleChecked: l,
  onToggleVisible: o,
  onClearSelection: c,
  onEnableSelected: f,
  onDisableSelected: h
}) {
  return /* @__PURE__ */ n.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ n.jsxs("label", { children: [
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "checkbox",
          checked: l,
          disabled: e,
          onChange: o,
          "aria-label": l ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        i,
        "/",
        s,
        " visible"
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("div", { children: [
      /* @__PURE__ */ n.jsxs("span", { title: `${r} enabled`, children: [
        t,
        " selected"
      ] }),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Enable selected features",
          disabled: e || t === 0 || r === t,
          onClick: f,
          children: "Enable"
        }
      ),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Disable selected features",
          disabled: e || t === 0 || r === 0,
          onClick: h,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: c, children: "Clear" })
    ] })
  ] });
}
function fe({
  categories: e,
  selectedCategory: t,
  onSelect: s
}) {
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((i) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: i.id === t ? "active" : "",
        onClick: () => s(i.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: i.label }),
          /* @__PURE__ */ n.jsx("em", { children: i.count })
        ]
      },
      i.id
    )) })
  ] });
}
function xe({
  feature: e,
  selected: t,
  checked: s,
  onSelect: i,
  onToggle: r,
  onCheckedChange: l
}) {
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: s,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (o) => l(o.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: r,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: i, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.id }),
        e.experimental ? /* @__PURE__ */ n.jsx("em", { children: "Experimental" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      /* @__PURE__ */ n.jsx("code", { children: e.id }),
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        e.categories.slice(0, 2).map((o) => /* @__PURE__ */ n.jsx("span", { children: o }, o)),
        e.categories.length > 2 ? /* @__PURE__ */ n.jsxs("span", { children: [
          "+",
          e.categories.length - 2
        ] }) : null
      ] })
    ] }),
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-card-count", children: e.settings.length })
  ] });
}
function je({
  feature: e,
  disabled: t,
  dirty: s,
  onToggle: i,
  onSettingChange: r,
  onReset: l,
  onApply: o
}) {
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const c = Ae(e.settings), f = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : null;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ n.jsx("h3", { children: e.displayName || e.id }),
        /* @__PURE__ */ n.jsx("code", { children: e.id })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => i(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-tags", children: [
      /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
      f ? /* @__PURE__ */ n.jsx("span", { children: f }) : null,
      e.categories.map((h) => /* @__PURE__ */ n.jsx("span", { children: h }, h))
    ] }),
    e.manifestHash || e.manifestPath ? /* @__PURE__ */ n.jsxs("dl", { className: "feature-management-detail-list", children: [
      e.manifestHash ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestHash })
      ] }) : null,
      e.manifestPath ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Path" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestPath })
      ] }) : null
    ] }) : null,
    e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: c.map((h) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
      /* @__PURE__ */ n.jsx("h4", { children: h.label }),
      /* @__PURE__ */ n.jsx("div", { children: h.settings.map((u) => {
        const k = ue(T, u).component;
        return /* @__PURE__ */ n.jsx(be, { setting: u, children: /* @__PURE__ */ n.jsx(
          k,
          {
            setting: u,
            value: Ee(e, u),
            disabled: t || !e.enabled,
            onChange: (j) => r(e, u, j)
          }
        ) }, u.name);
      }) })
    ] }, h.id)) }),
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-sticky-actions", children: [
      /* @__PURE__ */ n.jsx("span", { children: s ? "Unsaved changes" : "No pending changes" }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: l, disabled: !s || t, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: o, disabled: !s || t, children: "Apply" })
      ] })
    ] })
  ] });
}
function be({ setting: e, children: t }) {
  return /* @__PURE__ */ n.jsxs("label", { className: "feature-management-setting", children: [
    /* @__PURE__ */ n.jsxs("span", { children: [
      /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.name }),
      e.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
      e.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
      e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
    t,
    /* @__PURE__ */ n.jsx("code", { children: e.name })
  ] });
}
function ye({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "checkbox", checked: !!e, disabled: t, onChange: (i) => s(i.target.checked) });
}
function ve({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: O(e), disabled: t, onChange: (i) => s(i.target.value) });
}
function Ne({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: O(e), disabled: t, onChange: (i) => s(i.target.value) });
}
function Se({ setting: e, value: t, disabled: s, onChange: i }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: O(t),
      disabled: s,
      onChange: (r) => i(y(e.jsonType) === "integer" ? Number.parseInt(r.target.value || "0", 10) : Number.parseFloat(r.target.value || "0"))
    }
  );
}
function Ce({ setting: e, value: t, disabled: s, onChange: i }) {
  const r = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: r, disabled: s, onChange: (l) => {
    const o = e.options.find((c) => JSON.stringify(c.value) === l.target.value);
    i(o?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((l) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(l.value), children: l.label }, `${e.name}-${JSON.stringify(l.value)}`))
  ] });
}
function ke({ setting: e, value: t, disabled: s, onChange: i }) {
  const [r, l] = x(W(t, e)), [o, c] = x(!1);
  return C(() => {
    l(W(t, e)), c(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: r, disabled: s, rows: 5, onChange: (f) => {
      const h = f.target.value;
      l(h);
      try {
        i(JSON.parse(h || ee(e))), c(!1);
      } catch {
        c(!0);
      }
    } }),
    o ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function J(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function Ee(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? Fe(t);
}
function Fe(e) {
  const t = y(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function O(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function W(e, t) {
  return e == null || e === "" ? ee(t) : JSON.stringify(e, null, 2);
}
function ee(e) {
  return y(e.jsonType) === "array" ? "[]" : "{}";
}
function we(e) {
  const t = /* @__PURE__ */ new Map();
  for (const s of e) {
    const i = ne(s);
    for (const r of i)
      t.set(r, (t.get(r) ?? 0) + 1);
  }
  return [
    { id: w, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([s], [i]) => I(s).localeCompare(I(i))).map(([s, i]) => ({ id: s, label: I(s), count: i }))
  ];
}
function Te(e, t) {
  return t === w ? e : e.filter((s) => ne(s).includes(t));
}
function ne(e) {
  return e.categories.length > 0 ? e.categories : [K];
}
function I(e) {
  return e === K ? "Uncategorized" : e;
}
function Ae(e) {
  const t = /* @__PURE__ */ new Map();
  for (const s of e) {
    const i = s.group || s.category || "General", r = t.get(i);
    r ? r.settings.push(s) : t.set(i, { id: i, label: i, settings: [s] });
  }
  return Array.from(t.values());
}
function $(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function y(e) {
  return (e ?? "").trim().toLowerCase();
}
function X(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, s) => t.id.localeCompare(s.id)));
}
function Z(e) {
  if (Re(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function Re(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  he as FeatureManagementPage,
  me as createApplyPayload,
  Z as getErrorMessage,
  ge as isDirty,
  Ie as register,
  de as registerBuiltInSettingEditors,
  ue as selectSettingEditor
};
