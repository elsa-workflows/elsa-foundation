import { useMemo as w, useState as I, useEffect as E } from "react";
var R = { exports: {} }, C = {};
var W;
function oe() {
  if (W) return C;
  W = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(i, d, r) {
    var p = null;
    if (r !== void 0 && (p = "" + r), d.key !== void 0 && (p = "" + d.key), "key" in d) {
      r = {};
      for (var o in d)
        o !== "key" && (r[o] = d[o]);
    } else r = d;
    return d = r.ref, {
      $$typeof: e,
      type: i,
      key: p,
      ref: d !== void 0 ? d : null,
      props: r
    };
  }
  return C.Fragment = t, C.jsx = a, C.jsxs = a, C;
}
var Q;
function ue() {
  return Q || (Q = 1, R.exports = oe()), R.exports;
}
var n = ue();
const T = "__all", ne = "__uncategorized";
let O;
function _e(e) {
  O = e, he(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: fe
  });
}
function he(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || D(t, "select"),
    component: Ie
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => N(t.jsonType) === "boolean",
    component: Ce
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(N(t.jsonType)),
    component: we
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(N(t.jsonType)) || D(t, "json") || D(t, "textarea"),
    component: Te
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: Fe
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Ee
  });
}
function me(e, t) {
  const a = e.settingEditors.list().filter((i) => i.supports(t)).sort((i, d) => (i.order ?? 500) - (d.order ?? 500))[0];
  if (!a)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return a;
}
function ge(e, t) {
  return {
    revision: e,
    features: t.map((a) => ({
      id: a.id,
      enabled: a.enabled,
      configuration: a.enabled ? a.configuration : {}
    }))
  };
}
function pe(e, t) {
  return e ? K(e.features) !== K(t) : !1;
}
function xe(e) {
  return [
    {
      id: "studio",
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: e.host,
      writable: !1
    },
    {
      id: "server",
      label: "Server",
      runtime: "Elsa.Server",
      context: e.backend,
      writable: !0
    }
  ];
}
function X() {
  return {
    catalog: null,
    draft: [],
    selectedId: "",
    checkedFeatureIds: /* @__PURE__ */ new Set(),
    selectedCategory: T,
    loading: !0,
    applying: !1,
    status: null,
    error: null
  };
}
function fe() {
  const e = w(() => xe(O), []), [t, a] = I("server"), [i, d] = I(() => ({
    studio: X(),
    server: X()
  })), r = e.find((s) => s.id === t) ?? e[0], p = i[r.id], { catalog: o, draft: u, selectedId: m, checkedFeatureIds: f, selectedCategory: v, loading: j, applying: S, status: M, error: _ } = p;
  E(() => {
    for (const s of e)
      L(s.id);
  }, []);
  const F = w(() => Je(u), [u]), x = w(
    () => He(u, v),
    [u, v]
  ), y = x.find((s) => s.id === m) ?? x.find((s) => s.enabled) ?? x[0] ?? null, k = pe(o, u), ae = u.filter((s) => s.enabled).length, ie = F.find((s) => s.id === v)?.label ?? "All categories", z = w(() => new Set(x.map((s) => s.id)), [x]), P = x.filter((s) => f.has(s.id)).length, q = x.length > 0 && P === x.length, re = u.filter((s) => f.has(s.id)), b = !r.writable, A = j || S || b;
  E(() => {
    y && y.id !== m && g(r.id, (s) => ({ ...s, selectedId: y.id }));
  }, [r.id, y, m]), E(() => {
    F.some((s) => s.id === v) || g(r.id, (s) => ({ ...s, selectedCategory: T }));
  }, [r.id, F, v]), E(() => {
    g(r.id, (s) => {
      const l = new Set(s.draft.map((h) => h.id)), c = new Set(Array.from(s.checkedFeatureIds).filter((h) => l.has(h)));
      return c.size === s.checkedFeatureIds.size ? s : { ...s, checkedFeatureIds: c };
    });
  }, [r.id, u]);
  function g(s, l) {
    d((c) => ({
      ...c,
      [s]: l(c[s])
    }));
  }
  async function L(s = r.id) {
    const l = e.find((c) => c.id === s) ?? r;
    g(l.id, (c) => ({ ...c, loading: !0, error: null }));
    try {
      const c = await l.context.http.getJson("/modularity/features");
      g(l.id, (h) => ({
        ...h,
        catalog: c,
        draft: c.features.map(J),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (c) {
      g(l.id, (h) => ({ ...h, error: ee(c), loading: !1 }));
    }
  }
  async function V() {
    if (!(!o || !k || b)) {
      g(r.id, (s) => ({ ...s, applying: !0, error: null, status: null }));
      try {
        const s = await r.context.http.postJson(
          "/modularity/features/apply",
          ge(o.revision, u)
        );
        g(r.id, (l) => ({
          ...l,
          catalog: s.catalog,
          draft: s.catalog.features.map(J),
          status: `Applied ${s.featureDescriptorCount} descriptor(s); reloaded ${s.reloadedShellCount} shell(s).`,
          applying: !1
        }));
      } catch (s) {
        g(r.id, (l) => ({ ...l, error: ee(s), applying: !1 }));
      }
    }
  }
  function B(s, l) {
    b || g(r.id, (c) => ({
      ...c,
      draft: c.draft.map((h) => h.id === s ? l(h) : h)
    }));
  }
  function U(s) {
    B(s.id, (l) => ({ ...l, enabled: !l.enabled }));
  }
  function le(s, l) {
    b || g(r.id, (c) => {
      const h = new Set(c.checkedFeatureIds);
      return l ? h.add(s) : h.delete(s), { ...c, checkedFeatureIds: h };
    });
  }
  function de() {
    b || g(r.id, (s) => {
      const l = new Set(s.checkedFeatureIds);
      if (q)
        for (const c of z)
          l.delete(c);
      else
        for (const c of z)
          l.add(c);
      return { ...s, checkedFeatureIds: l };
    });
  }
  function G(s) {
    b || f.size === 0 || g(r.id, (l) => ({
      ...l,
      draft: l.draft.map((c) => l.checkedFeatureIds.has(c.id) ? { ...c, enabled: s } : c)
    }));
  }
  function ce(s, l, c) {
    B(s.id, (h) => ({
      ...h,
      configuration: {
        ...h.configuration,
        [l.name]: c
      }
    }));
  }
  function Y() {
    o && g(r.id, (s) => ({
      ...s,
      draft: o.features.map(J),
      status: null,
      error: null
    }));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          r.label,
          ": ",
          ae,
          " enabled of ",
          u.length,
          " available",
          k ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => L(), disabled: j || S, children: "Refresh" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: Y, disabled: !k || j || S || b, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: V, disabled: !k || j || S || b, children: S ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(je, { hosts: e, activeHostId: r.id, onSelect: a }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    M ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: M }) : null,
    b ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: "Studio features are shown from the Studio host runtime. Feature toggles are available for backend features on the Server tab." }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        ve,
        {
          categories: F,
          selectedCategory: v,
          onSelect: (s) => g(r.id, (l) => ({ ...l, selectedCategory: s }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: ie }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              x.length,
              " feature",
              x.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            x.filter((s) => s.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          be,
          {
            disabled: A || x.length === 0,
            selectedCount: f.size,
            visibleCount: x.length,
            visibleSelectedCount: P,
            selectedEnabledCount: re.filter((s) => s.enabled).length,
            allVisibleChecked: q,
            onToggleVisible: de,
            onClearSelection: () => setCheckedFeatureIds(/* @__PURE__ */ new Set()),
            onEnableSelected: () => G(!0),
            onDisableSelected: () => G(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": j, children: [
          j && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !j && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !j && u.length > 0 && x.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features match this category." }) : null,
          x.map((s) => /* @__PURE__ */ n.jsx(
            ye,
            {
              feature: s,
              selected: s.id === y?.id,
              checked: f.has(s.id),
              disabled: A,
              onSelect: () => g(r.id, (l) => ({ ...l, selectedId: s.id })),
              onToggle: () => U(s),
              onCheckedChange: (l) => le(s.id, l)
            },
            s.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        Ne,
        {
          feature: y,
          disabled: A,
          dirty: k,
          onToggle: U,
          onSettingChange: ce,
          onReset: Y,
          onApply: V
        }
      )
    ] })
  ] });
}
function je({
  hosts: e,
  activeHostId: t,
  onSelect: a
}) {
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((i) => /* @__PURE__ */ n.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": i.id === t,
      className: i.id === t ? "active" : "",
      onClick: () => a(i.id),
      children: [
        /* @__PURE__ */ n.jsx("span", { children: i.label }),
        /* @__PURE__ */ n.jsx("small", { children: i.runtime })
      ]
    },
    i.id
  )) });
}
function be({
  disabled: e,
  selectedCount: t,
  visibleCount: a,
  visibleSelectedCount: i,
  selectedEnabledCount: d,
  allVisibleChecked: r,
  onToggleVisible: p,
  onClearSelection: o,
  onEnableSelected: u,
  onDisableSelected: m
}) {
  return /* @__PURE__ */ n.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ n.jsxs("label", { children: [
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "checkbox",
          checked: r,
          disabled: e,
          onChange: p,
          "aria-label": r ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        i,
        "/",
        a,
        " visible"
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("div", { children: [
      /* @__PURE__ */ n.jsxs("span", { title: `${d} enabled`, children: [
        t,
        " selected"
      ] }),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Enable selected features",
          disabled: e || t === 0 || d === t,
          onClick: u,
          children: "Enable"
        }
      ),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Disable selected features",
          disabled: e || t === 0 || d === 0,
          onClick: m,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: o, children: "Clear" })
    ] })
  ] });
}
function ve({
  categories: e,
  selectedCategory: t,
  onSelect: a
}) {
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((i) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: i.id === t ? "active" : "",
        onClick: () => a(i.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: i.label }),
          /* @__PURE__ */ n.jsx("em", { children: i.count })
        ]
      },
      i.id
    )) })
  ] });
}
function ye({
  feature: e,
  selected: t,
  checked: a,
  disabled: i,
  onSelect: d,
  onToggle: r,
  onCheckedChange: p
}) {
  const o = e.displayName || e.id, u = !Oe(o, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: a,
        disabled: i,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (m) => p(m.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: r,
        disabled: i,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: d, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: o }),
        e.experimental ? /* @__PURE__ */ n.jsx("em", { children: "Experimental" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      u ? /* @__PURE__ */ n.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        e.categories.slice(0, 2).map((m) => /* @__PURE__ */ n.jsx("span", { children: m }, m)),
        e.categories.length > 2 ? /* @__PURE__ */ n.jsxs("span", { children: [
          "+",
          e.categories.length - 2
        ] }) : null
      ] })
    ] }),
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-card-count", children: e.settings.length })
  ] });
}
function Ne({
  feature: e,
  disabled: t,
  dirty: a,
  onToggle: i,
  onSettingChange: d,
  onReset: r,
  onApply: p
}) {
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const o = De(e.settings), u = e.displayName || e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ n.jsx("h3", { children: u })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => i(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx(Se, { feature: e, displayName: u }),
    e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: o.map((m) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
      /* @__PURE__ */ n.jsx("h4", { children: m.label }),
      /* @__PURE__ */ n.jsx("div", { children: m.settings.map((f) => {
        const v = me(O, f).component;
        return /* @__PURE__ */ n.jsx(ke, { setting: f, children: /* @__PURE__ */ n.jsx(
          v,
          {
            setting: f,
            value: Ae(e, f),
            disabled: t || !e.enabled,
            onChange: (j) => d(e, f, j)
          }
        ) }, f.name);
      }) })
    ] }, m.id)) }),
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-sticky-actions", children: [
      /* @__PURE__ */ n.jsx("span", { children: a ? "Unsaved changes" : "No pending changes" }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: r, disabled: !a || t, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: p, disabled: !a || t, children: "Apply" })
      ] })
    ] })
  ] });
}
function Se({ feature: e, displayName: t }) {
  const a = e.categories.length > 0 ? e.categories.join(", ") : "Uncategorized", i = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-metadata", "aria-label": "Feature metadata", children: [
    /* @__PURE__ */ n.jsx("h4", { children: "Metadata" }),
    /* @__PURE__ */ n.jsxs("dl", { className: "feature-management-detail-list", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Display name" }),
        /* @__PURE__ */ n.jsx("dd", { children: t })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Technical name" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx("code", { children: e.id }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.enabled ? "Enabled" : "Disabled" })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Source" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.sourceKind })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Categories" }),
        /* @__PURE__ */ n.jsx("dd", { children: a })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ n.jsx("dd", { children: i })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.settings.length })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Advanced" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.advanced ? "Yes" : "No" })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Experimental" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.experimental ? "Yes" : "No" })
      ] }),
      e.description ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.description })
      ] }) : null,
      e.manifestHash ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest hash" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestHash })
      ] }) : null,
      e.manifestPath ? /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest path" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.manifestPath })
      ] }) : null
    ] })
  ] });
}
function ke({ setting: e, children: t }) {
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
function Ce({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "checkbox", checked: !!e, disabled: t, onChange: (i) => a(i.target.checked) });
}
function Ee({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: $(e), disabled: t, onChange: (i) => a(i.target.value) });
}
function Fe({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: $(e), disabled: t, onChange: (i) => a(i.target.value) });
}
function we({ setting: e, value: t, disabled: a, onChange: i }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: $(t),
      disabled: a,
      onChange: (d) => i(N(e.jsonType) === "integer" ? Number.parseInt(d.target.value || "0", 10) : Number.parseFloat(d.target.value || "0"))
    }
  );
}
function Ie({ setting: e, value: t, disabled: a, onChange: i }) {
  const d = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: d, disabled: a, onChange: (r) => {
    const p = e.options.find((o) => JSON.stringify(o.value) === r.target.value);
    i(p?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((r) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(r.value), children: r.label }, `${e.name}-${JSON.stringify(r.value)}`))
  ] });
}
function Te({ setting: e, value: t, disabled: a, onChange: i }) {
  const [d, r] = I(Z(t, e)), [p, o] = I(!1);
  return E(() => {
    r(Z(t, e)), o(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: d, disabled: a, rows: 5, onChange: (u) => {
      const m = u.target.value;
      r(m);
      try {
        i(JSON.parse(m || te(e))), o(!1);
      } catch {
        o(!0);
      }
    } }),
    p ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function J(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function Ae(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? Re(t);
}
function Re(e) {
  const t = N(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function $(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function Z(e, t) {
  return e == null || e === "" ? te(t) : JSON.stringify(e, null, 2);
}
function te(e) {
  return N(e.jsonType) === "array" ? "[]" : "{}";
}
function Je(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const i = se(a);
    for (const d of i)
      t.set(d, (t.get(d) ?? 0) + 1);
  }
  return [
    { id: T, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([a], [i]) => H(a).localeCompare(H(i))).map(([a, i]) => ({ id: a, label: H(a), count: i }))
  ];
}
function He(e, t) {
  return t === T ? e : e.filter((a) => se(a).includes(t));
}
function se(e) {
  return e.categories.length > 0 ? e.categories : [ne];
}
function H(e) {
  return e === ne ? "Uncategorized" : e;
}
function De(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const i = a.group || a.category || "General", d = t.get(i);
    d ? d.settings.push(a) : t.set(i, { id: i, label: i, settings: [a] });
  }
  return Array.from(t.values());
}
function D(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function N(e) {
  return (e ?? "").trim().toLowerCase();
}
function Oe(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function K(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, a) => t.id.localeCompare(a.id)));
}
function ee(e) {
  if ($e(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function $e(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  fe as FeatureManagementPage,
  ge as createApplyPayload,
  ee as getErrorMessage,
  pe as isDirty,
  _e as register,
  he as registerBuiltInSettingEditors,
  me as selectSettingEditor
};
