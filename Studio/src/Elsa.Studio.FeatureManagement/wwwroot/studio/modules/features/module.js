import { useState as m, useEffect as E, useMemo as L } from "react";
var A = { exports: {} }, v = {};
var z;
function K() {
  if (z) return v;
  z = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(a, i, l) {
    var c = null;
    if (l !== void 0 && (c = "" + l), i.key !== void 0 && (c = "" + i.key), "key" in i) {
      l = {};
      for (var d in i)
        d !== "key" && (l[d] = i[d]);
    } else l = i;
    return i = l.ref, {
      $$typeof: e,
      type: a,
      key: c,
      ref: i !== void 0 ? i : null,
      props: l
    };
  }
  return v.Fragment = t, v.jsx = s, v.jsxs = s, v;
}
var H;
function ee() {
  return H || (H = 1, A.exports = K()), A.exports;
}
var n = ee();
const k = "__all", U = "__uncategorized";
let R;
function Se(e) {
  R = e, ne(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: re
  });
}
function ne(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || w(t, "select"),
    component: pe
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => j(t.jsonType) === "boolean",
    component: de
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(j(t.jsonType)),
    component: ge
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(j(t.jsonType)) || w(t, "json") || w(t, "textarea"),
    component: he
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: me
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: ue
  });
}
function te(e, t) {
  const s = e.settingEditors.list().filter((a) => a.supports(t)).sort((a, i) => (a.order ?? 500) - (i.order ?? 500))[0];
  if (!s)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return s;
}
function se(e, t) {
  return {
    revision: e,
    features: t.map((s) => ({
      id: s.id,
      enabled: s.enabled,
      configuration: s.enabled ? s.configuration : {}
    }))
  };
}
function ae(e, t) {
  return e ? B(e.features) !== B(t) : !1;
}
function re() {
  const [e, t] = m(null), [s, a] = m([]), [i, l] = m(""), [c, d] = m(k), [o, u] = m(!0), [p, N] = m(!1), [O, S] = m(null), [_, b] = m(null);
  E(() => {
    $();
  }, []);
  const C = L(() => je(s), [s]), g = L(
    () => be(s, c),
    [s, c]
  ), h = g.find((r) => r.id === i) ?? g.find((r) => r.enabled) ?? g[0] ?? null, y = ae(e, s), W = s.filter((r) => r.enabled).length, X = C.find((r) => r.id === c)?.label ?? "All categories";
  E(() => {
    h && h.id !== i && l(h.id);
  }, [h, i]), E(() => {
    C.some((r) => r.id === c) || d(k);
  }, [C, c]);
  async function $() {
    u(!0), b(null);
    try {
      const r = await R.backend.http.getJson("/modularity/features");
      t(r), a(r.features.map(F)), S(null);
    } catch (r) {
      b(G(r));
    } finally {
      u(!1);
    }
  }
  async function P() {
    if (!(!e || !y)) {
      N(!0), b(null), S(null);
      try {
        const r = await R.backend.http.postJson(
          "/modularity/features/apply",
          se(e.revision, s)
        );
        t(r.catalog), a(r.catalog.features.map(F)), S(`Applied ${r.featureDescriptorCount} descriptor(s); reloaded ${r.reloadedShellCount} shell(s).`);
      } catch (r) {
        b(G(r));
      } finally {
        N(!1);
      }
    }
  }
  function q(r, f) {
    a((T) => T.map((x) => x.id === r ? f(x) : x));
  }
  function M(r) {
    q(r.id, (f) => ({ ...f, enabled: !f.enabled }));
  }
  function Z(r, f, T) {
    q(r.id, (x) => ({
      ...x,
      configuration: {
        ...x.configuration,
        [f.name]: T
      }
    }));
  }
  function D() {
    e && (a(e.features.map(F)), S(null), b(null));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          W,
          " enabled of ",
          s.length,
          " available",
          y ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: $, disabled: o || p, children: "Refresh" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: D, disabled: !y || o || p, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: P, disabled: !y || o || p, children: p ? "Applying" : "Apply" })
      ] })
    ] }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    O ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: O }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        ie,
        {
          categories: C,
          selectedCategory: c,
          onSelect: d
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: X }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              g.length,
              " feature",
              g.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            g.filter((r) => r.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": o, children: [
          o && s.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !o && s.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !o && s.length > 0 && g.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features match this category." }) : null,
          g.map((r) => /* @__PURE__ */ n.jsx(
            le,
            {
              feature: r,
              selected: r.id === h?.id,
              onSelect: () => l(r.id),
              onToggle: () => M(r)
            },
            r.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        oe,
        {
          feature: h,
          disabled: p || o,
          dirty: y,
          onToggle: M,
          onSettingChange: Z,
          onReset: D,
          onApply: P
        }
      )
    ] })
  ] });
}
function ie({
  categories: e,
  selectedCategory: t,
  onSelect: s
}) {
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((a) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: a.id === t ? "active" : "",
        onClick: () => s(a.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: a.label }),
          /* @__PURE__ */ n.jsx("em", { children: a.count })
        ]
      },
      a.id
    )) })
  ] });
}
function le({
  feature: e,
  selected: t,
  onSelect: s,
  onToggle: a
}) {
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: a,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: s, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.id }),
        e.experimental ? /* @__PURE__ */ n.jsx("em", { children: "Experimental" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      /* @__PURE__ */ n.jsx("code", { children: e.id }),
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        e.categories.slice(0, 2).map((i) => /* @__PURE__ */ n.jsx("span", { children: i }, i)),
        e.categories.length > 2 ? /* @__PURE__ */ n.jsxs("span", { children: [
          "+",
          e.categories.length - 2
        ] }) : null
      ] })
    ] }),
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-card-count", children: e.settings.length })
  ] });
}
function oe({
  feature: e,
  disabled: t,
  dirty: s,
  onToggle: a,
  onSettingChange: i,
  onReset: l,
  onApply: c
}) {
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const d = ye(e.settings);
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ n.jsx("h3", { children: e.displayName || e.id }),
        /* @__PURE__ */ n.jsx("code", { children: e.id })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => a(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    e.packageId ? /* @__PURE__ */ n.jsxs("p", { className: "feature-management-muted", children: [
      e.packageId,
      " ",
      e.packageVersion
    ] }) : null,
    e.categories.length > 0 ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-inspector-tags", children: e.categories.map((o) => /* @__PURE__ */ n.jsx("span", { children: o }, o)) }) : null,
    /* @__PURE__ */ n.jsxs("dl", { className: "feature-management-metadata", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Source" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.sourceKind })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.settings.length })
      ] }),
      e.manifestHash ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestHash })
      ] }) : null,
      e.manifestPath ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Path" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestPath })
      ] }) : null
    ] }),
    e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: d.map((o) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
      /* @__PURE__ */ n.jsx("h4", { children: o.label }),
      /* @__PURE__ */ n.jsx("div", { children: o.settings.map((u) => {
        const p = te(R, u).component;
        return /* @__PURE__ */ n.jsx(ce, { setting: u, children: /* @__PURE__ */ n.jsx(
          p,
          {
            setting: u,
            value: fe(e, u),
            disabled: t || !e.enabled,
            onChange: (N) => i(e, u, N)
          }
        ) }, u.name);
      }) })
    ] }, o.id)) }),
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-sticky-actions", children: [
      /* @__PURE__ */ n.jsx("span", { children: s ? "Unsaved changes" : "No pending changes" }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: l, disabled: !s || t, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: c, disabled: !s || t, children: "Apply" })
      ] })
    ] })
  ] });
}
function ce({ setting: e, children: t }) {
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
function de({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "checkbox", checked: !!e, disabled: t, onChange: (a) => s(a.target.checked) });
}
function ue({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: I(e), disabled: t, onChange: (a) => s(a.target.value) });
}
function me({ value: e, disabled: t, onChange: s }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: I(e), disabled: t, onChange: (a) => s(a.target.value) });
}
function ge({ setting: e, value: t, disabled: s, onChange: a }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: I(t),
      disabled: s,
      onChange: (i) => a(j(e.jsonType) === "integer" ? Number.parseInt(i.target.value || "0", 10) : Number.parseFloat(i.target.value || "0"))
    }
  );
}
function pe({ setting: e, value: t, disabled: s, onChange: a }) {
  const i = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: i, disabled: s, onChange: (l) => {
    const c = e.options.find((d) => JSON.stringify(d.value) === l.target.value);
    a(c?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((l) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(l.value), children: l.label }, `${e.name}-${JSON.stringify(l.value)}`))
  ] });
}
function he({ setting: e, value: t, disabled: s, onChange: a }) {
  const [i, l] = m(V(t, e)), [c, d] = m(!1);
  return E(() => {
    l(V(t, e)), d(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: i, disabled: s, rows: 5, onChange: (o) => {
      const u = o.target.value;
      l(u);
      try {
        a(JSON.parse(u || Y(e))), d(!1);
      } catch {
        d(!0);
      }
    } }),
    c ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function F(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function fe(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? xe(t);
}
function xe(e) {
  const t = j(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function I(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function V(e, t) {
  return e == null || e === "" ? Y(t) : JSON.stringify(e, null, 2);
}
function Y(e) {
  return j(e.jsonType) === "array" ? "[]" : "{}";
}
function je(e) {
  const t = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = Q(s);
    for (const i of a)
      t.set(i, (t.get(i) ?? 0) + 1);
  }
  return [
    { id: k, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([s], [a]) => J(s).localeCompare(J(a))).map(([s, a]) => ({ id: s, label: J(s), count: a }))
  ];
}
function be(e, t) {
  return t === k ? e : e.filter((s) => Q(s).includes(t));
}
function Q(e) {
  return e.categories.length > 0 ? e.categories : [U];
}
function J(e) {
  return e === U ? "Uncategorized" : e;
}
function ye(e) {
  const t = /* @__PURE__ */ new Map();
  for (const s of e) {
    const a = s.group || s.category || "General", i = t.get(a);
    i ? i.settings.push(s) : t.set(a, { id: a, label: a, settings: [s] });
  }
  return Array.from(t.values());
}
function w(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function j(e) {
  return (e ?? "").trim().toLowerCase();
}
function B(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, s) => t.id.localeCompare(s.id)));
}
function G(e) {
  if (ve(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function ve(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  re as FeatureManagementPage,
  se as createApplyPayload,
  G as getErrorMessage,
  ae as isDirty,
  Se as register,
  ne as registerBuiltInSettingEditors,
  te as selectSettingEditor
};
