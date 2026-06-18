import { useMemo as I, useState as C, useEffect as k } from "react";
var A = { exports: {} }, E = {};
var Y;
function ge() {
  if (Y) return E;
  Y = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function i(a, l, r) {
    var m = null;
    if (r !== void 0 && (m = "" + r), l.key !== void 0 && (m = "" + l.key), "key" in l) {
      r = {};
      for (var o in l)
        o !== "key" && (r[o] = l[o]);
    } else r = l;
    return l = r.ref, {
      $$typeof: e,
      type: a,
      key: m,
      ref: l !== void 0 ? l : null,
      props: r
    };
  }
  return E.Fragment = t, E.jsx = i, E.jsxs = i, E;
}
var W;
function he() {
  return W || (W = 1, A.exports = ge()), A.exports;
}
var n = he();
const T = "__all", ee = "__uncategorized", pe = "elsa-studio:modules-changed";
let $;
function Le(e) {
  $ = e, xe(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: ve
  });
}
function xe(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || H(t, "select"),
    component: Oe
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => v(t.jsonType) === "boolean",
    component: Re
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(v(t.jsonType)),
    component: Je
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(v(t.jsonType)) || H(t, "json") || H(t, "textarea"),
    component: He
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: De
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Ae
  });
}
function fe(e, t) {
  const i = e.settingEditors.list().filter((a) => a.supports(t)).sort((a, l) => (a.order ?? 500) - (l.order ?? 500))[0];
  if (!i)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return i;
}
function je(e, t) {
  return {
    revision: e,
    features: t.map((i) => ({
      id: i.id,
      enabled: i.enabled,
      configuration: i.enabled ? i.configuration : {}
    }))
  };
}
function be(e, t) {
  return e ? Z(e.features) !== Z(t) : !1;
}
function ye(e) {
  return [
    {
      id: "studio",
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: e.host,
      writable: !0
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
function Q() {
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
function ve() {
  const e = I(() => ye($), []), [t, i] = C("server"), [a, l] = C(() => ({
    studio: Q(),
    server: Q()
  })), r = e.find((s) => s.id === t) ?? e[0], m = a[r.id], { catalog: o, draft: u, selectedId: p, checkedFeatureIds: j, selectedCategory: x, loading: b, applying: N, status: q, error: V } = m;
  k(() => {
    for (const s of e)
      B(s.id);
  }, []);
  const F = I(() => Ve(u), [u]), f = I(
    () => _e(u, x),
    [u, x]
  ), S = f.find((s) => s.id === p) ?? f.find((s) => s.enabled) ?? f[0] ?? null, w = be(o, u), ie = u.filter((s) => s.enabled).length, re = F.find((s) => s.id === x)?.label ?? "All categories", _ = I(() => new Set(f.map((s) => s.id)), [f]), z = f.filter((s) => j.has(s.id)).length, P = f.length > 0 && z === f.length, le = u.filter((s) => j.has(s.id)), y = !r.writable, R = b || N || y;
  k(() => {
    S && S.id !== p && h(r.id, (s) => ({ ...s, selectedId: S.id }));
  }, [r.id, S, p]), k(() => {
    F.some((s) => s.id === x) || h(r.id, (s) => ({ ...s, selectedCategory: T }));
  }, [r.id, F, x]), k(() => {
    h(r.id, (s) => {
      const d = new Set(s.draft.map((g) => g.id)), c = new Set(Array.from(s.checkedFeatureIds).filter((g) => d.has(g)));
      return c.size === s.checkedFeatureIds.size ? s : { ...s, checkedFeatureIds: c };
    });
  }, [r.id, u]);
  function h(s, d) {
    l((c) => ({
      ...c,
      [s]: d(c[s])
    }));
  }
  async function B(s = r.id) {
    const d = e.find((c) => c.id === s) ?? r;
    h(d.id, (c) => ({ ...c, loading: !0, error: null }));
    try {
      const c = await d.context.http.getJson("/modularity/features");
      h(d.id, (g) => ({
        ...g,
        catalog: c,
        draft: c.features.map(D),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (c) {
      h(d.id, (g) => ({ ...g, error: K(c), loading: !1 }));
    }
  }
  async function de() {
    if (!(!o || !w || y)) {
      h(r.id, (s) => ({ ...s, applying: !0, error: null, status: null }));
      try {
        const s = await r.context.http.postJson(
          "/modularity/features/apply",
          je(o.revision, u)
        );
        h(r.id, (d) => ({
          ...d,
          catalog: s.catalog,
          draft: s.catalog.features.map(D),
          status: `Applied ${s.featureDescriptorCount} descriptor(s); reloaded ${s.reloadedShellCount} shell(s).`,
          applying: !1
        })), r.id === "studio" && window.dispatchEvent(new Event(pe));
      } catch (s) {
        h(r.id, (d) => ({ ...d, error: K(s), applying: !1 }));
      }
    }
  }
  function L(s, d) {
    y || h(r.id, (c) => ({
      ...c,
      draft: c.draft.map((g) => g.id === s ? d(g) : g)
    }));
  }
  function G(s) {
    L(s.id, (d) => ({ ...d, enabled: !d.enabled }));
  }
  function ce(s, d) {
    y || h(r.id, (c) => {
      const g = new Set(c.checkedFeatureIds);
      return d ? g.add(s) : g.delete(s), { ...c, checkedFeatureIds: g };
    });
  }
  function oe() {
    y || h(r.id, (s) => {
      const d = new Set(s.checkedFeatureIds);
      if (P)
        for (const c of _)
          d.delete(c);
      else
        for (const c of _)
          d.add(c);
      return { ...s, checkedFeatureIds: d };
    });
  }
  function U(s) {
    y || j.size === 0 || h(r.id, (d) => ({
      ...d,
      draft: d.draft.map((c) => d.checkedFeatureIds.has(c.id) ? { ...c, enabled: s } : c)
    }));
  }
  function ue(s, d, c) {
    L(s.id, (g) => ({
      ...g,
      configuration: {
        ...g.configuration,
        [d.name]: c
      }
    }));
  }
  function me() {
    o && h(r.id, (s) => ({
      ...s,
      draft: o.features.map(D),
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
          ie,
          " enabled of ",
          u.length,
          " available",
          w ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => B(), disabled: b || N, children: "Refresh" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: me, disabled: !w || b || N || y, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: de, disabled: !w || b || N || y, children: N ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(Ne, { hosts: e, activeHostId: r.id, onSelect: i }),
    V ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: V }) : null,
    q ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: q }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        ke,
        {
          categories: F,
          selectedCategory: x,
          onSelect: (s) => h(r.id, (d) => ({ ...d, selectedCategory: s }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: re }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              f.length,
              " feature",
              f.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            f.filter((s) => s.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          Se,
          {
            disabled: R || f.length === 0,
            selectedCount: j.size,
            visibleCount: f.length,
            visibleSelectedCount: z,
            selectedEnabledCount: le.filter((s) => s.enabled).length,
            allVisibleChecked: P,
            onToggleVisible: oe,
            onClearSelection: () => setCheckedFeatureIds(/* @__PURE__ */ new Set()),
            onEnableSelected: () => U(!0),
            onDisableSelected: () => U(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": b, children: [
          b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !b && u.length > 0 && f.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features match this category." }) : null,
          f.map((s) => /* @__PURE__ */ n.jsx(
            Ce,
            {
              feature: s,
              selected: s.id === S?.id,
              checked: j.has(s.id),
              disabled: R,
              onSelect: () => h(r.id, (d) => ({ ...d, selectedId: s.id })),
              onToggle: () => G(s),
              onCheckedChange: (d) => ce(s.id, d)
            },
            s.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        Ee,
        {
          feature: S,
          disabled: R,
          onToggle: G,
          onSettingChange: ue
        }
      )
    ] })
  ] });
}
function Ne({
  hosts: e,
  activeHostId: t,
  onSelect: i
}) {
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((a) => /* @__PURE__ */ n.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": a.id === t,
      className: a.id === t ? "active" : "",
      onClick: () => i(a.id),
      children: [
        /* @__PURE__ */ n.jsx("span", { children: a.label }),
        /* @__PURE__ */ n.jsx("small", { children: a.runtime })
      ]
    },
    a.id
  )) });
}
function Se({
  disabled: e,
  selectedCount: t,
  visibleCount: i,
  visibleSelectedCount: a,
  selectedEnabledCount: l,
  allVisibleChecked: r,
  onToggleVisible: m,
  onClearSelection: o,
  onEnableSelected: u,
  onDisableSelected: p
}) {
  return /* @__PURE__ */ n.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ n.jsxs("label", { children: [
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "checkbox",
          checked: r,
          disabled: e,
          onChange: m,
          "aria-label": r ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        a,
        "/",
        i,
        " visible"
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("div", { children: [
      /* @__PURE__ */ n.jsxs("span", { title: `${l} enabled`, children: [
        t,
        " selected"
      ] }),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Enable selected features",
          disabled: e || t === 0 || l === t,
          onClick: u,
          children: "Enable"
        }
      ),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Disable selected features",
          disabled: e || t === 0 || l === 0,
          onClick: p,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: o, children: "Clear" })
    ] })
  ] });
}
function ke({
  categories: e,
  selectedCategory: t,
  onSelect: i
}) {
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((a) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: a.id === t ? "active" : "",
        onClick: () => i(a.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: a.label }),
          /* @__PURE__ */ n.jsx("em", { children: a.count })
        ]
      },
      a.id
    )) })
  ] });
}
function Ce({
  feature: e,
  selected: t,
  checked: i,
  disabled: a,
  onSelect: l,
  onToggle: r,
  onCheckedChange: m
}) {
  const o = e.displayName || e.id, u = !ze(o, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: i,
        disabled: a,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (p) => m(p.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: r,
        disabled: a,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: l, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: o }),
        e.experimental ? /* @__PURE__ */ n.jsx("em", { children: "Experimental" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      u ? /* @__PURE__ */ n.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        e.categories.slice(0, 2).map((p) => /* @__PURE__ */ n.jsx("span", { children: p }, p)),
        e.categories.length > 2 ? /* @__PURE__ */ n.jsxs("span", { children: [
          "+",
          e.categories.length - 2
        ] }) : null
      ] })
    ] }),
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-card-count", children: e.settings.length })
  ] });
}
function Ee({
  feature: e,
  disabled: t,
  onToggle: i,
  onSettingChange: a
}) {
  const [l, r] = C(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const m = e.displayName || e.id, o = t || !e.enabled, u = l === e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ n.jsx("h3", { children: m })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => i(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(Fe, { feature: e, displayName: m }) }),
    /* @__PURE__ */ n.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-section-heading", children: [
        /* @__PURE__ */ n.jsx("h4", { children: "Settings" }),
        e.settings.length > 0 ? /* @__PURE__ */ n.jsx(
          "button",
          {
            type: "button",
            onClick: () => r(e.id),
            disabled: o,
            title: e.enabled ? void 0 : "Enable the feature before editing settings.",
            children: "Edit settings"
          }
        ) : null
      ] }),
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(we, { feature: e })
    ] }),
    u ? /* @__PURE__ */ n.jsx(
      Ie,
      {
        feature: e,
        disabled: o,
        onSettingChange: a,
        onClose: () => r(null)
      }
    ) : null
  ] });
}
function Fe({ feature: e, displayName: t }) {
  const i = e.categories.length > 0 ? e.categories.join(", ") : "Uncategorized", a = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: i })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ n.jsx("dd", { children: a })
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
function we({ feature: e }) {
  const t = ae(e.settings);
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings-summary", children: t.map((i) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-summary-group", children: [
    /* @__PURE__ */ n.jsx("h5", { children: i.label }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-setting-summary-list", children: i.settings.map((a) => /* @__PURE__ */ n.jsxs("div", { className: "feature-management-setting-summary-row", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-summary-meta", children: [
        /* @__PURE__ */ n.jsxs("span", { children: [
          /* @__PURE__ */ n.jsx("strong", { children: a.displayName || a.name }),
          a.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
          a.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
          a.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
        ] }),
        a.description ? /* @__PURE__ */ n.jsx("small", { children: a.description }) : null,
        /* @__PURE__ */ n.jsx("code", { children: a.name })
      ] }),
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: Me(a, ne(e, a)) })
    ] }, a.name)) })
  ] }, i.id)) });
}
function Ie({
  feature: e,
  disabled: t,
  onSettingChange: i,
  onClose: a
}) {
  const [l, r] = C(() => J(e)), m = ae(e.settings);
  k(() => {
    r(J(e));
  }, [e.id]);
  function o(j, x) {
    r((b) => ({
      ...b,
      [j.name]: x
    }));
  }
  function u() {
    r(J(e));
  }
  function p() {
    for (const j of e.settings)
      i(e, j, l[j.name]);
    a();
  }
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-backdrop", role: "presentation", children: /* @__PURE__ */ n.jsxs(
    "section",
    {
      className: "feature-management-settings-dialog",
      role: "dialog",
      "aria-modal": "true",
      "aria-labelledby": "feature-management-settings-dialog-title",
      children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("span", { children: "Settings" }),
            /* @__PURE__ */ n.jsx("h3", { id: "feature-management-settings-dialog-title", children: e.displayName || e.id })
          ] }),
          /* @__PURE__ */ n.jsx("button", { type: "button", onClick: a, children: "Close" })
        ] }),
        /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-body", children: /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: m.map((j) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
          /* @__PURE__ */ n.jsx("h5", { children: j.label }),
          /* @__PURE__ */ n.jsx("div", { children: j.settings.map((x) => {
            const b = fe($, x).component;
            return /* @__PURE__ */ n.jsx(Te, { setting: x, children: /* @__PURE__ */ n.jsx(
              b,
              {
                setting: x,
                value: l[x.name],
                disabled: t,
                onChange: (N) => o(x, N)
              }
            ) }, x.name);
          }) })
        ] }, j.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: a, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: u, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: p, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function Te({ setting: e, children: t }) {
  const i = $e(e);
  return /* @__PURE__ */ n.jsxs("label", { className: i ? "feature-management-setting boolean" : "feature-management-setting", children: [
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-main", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.name }),
        e.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
        e.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      i ? /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }) : null
    ] }),
    i ? null : /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }),
    e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
    /* @__PURE__ */ n.jsx("code", { children: e.name })
  ] });
}
function Re({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "checkbox",
      className: "feature-management-setting-switch-input",
      checked: !!e,
      disabled: t,
      onChange: (a) => i(a.target.checked)
    }
  );
}
function Ae({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: M(e), disabled: t, onChange: (a) => i(a.target.value) });
}
function De({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: M(e), disabled: t, onChange: (a) => i(a.target.value) });
}
function Je({ setting: e, value: t, disabled: i, onChange: a }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: M(t),
      disabled: i,
      onChange: (l) => a(v(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function Oe({ setting: e, value: t, disabled: i, onChange: a }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: i, onChange: (r) => {
    const m = e.options.find((o) => JSON.stringify(o.value) === r.target.value);
    a(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((r) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(r.value), children: r.label }, `${e.name}-${JSON.stringify(r.value)}`))
  ] });
}
function He({ setting: e, value: t, disabled: i, onChange: a }) {
  const [l, r] = C(X(t, e)), [m, o] = C(!1);
  return k(() => {
    r(X(t, e)), o(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: i, rows: 5, onChange: (u) => {
      const p = u.target.value;
      r(p);
      try {
        a(JSON.parse(p || te(e))), o(!1);
      } catch {
        o(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function D(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function ne(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? qe(t);
}
function J(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, ne(e, t)]));
}
function $e(e) {
  return v(e.jsonType) === "boolean";
}
function Me(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (v(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const i = typeof t == "object" ? JSON.stringify(t) : String(t);
  return i.length > 96 ? `${i.slice(0, 93)}...` : i;
}
function qe(e) {
  const t = v(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function M(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function X(e, t) {
  return e == null || e === "" ? te(t) : JSON.stringify(e, null, 2);
}
function te(e) {
  return v(e.jsonType) === "array" ? "[]" : "{}";
}
function Ve(e) {
  const t = /* @__PURE__ */ new Map();
  for (const i of e) {
    const a = se(i);
    for (const l of a)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: T, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([i], [a]) => O(i).localeCompare(O(a))).map(([i, a]) => ({ id: i, label: O(i), count: a }))
  ];
}
function _e(e, t) {
  return t === T ? e : e.filter((i) => se(i).includes(t));
}
function se(e) {
  return e.categories.length > 0 ? e.categories : [ee];
}
function O(e) {
  return e === ee ? "Uncategorized" : e;
}
function ae(e) {
  const t = /* @__PURE__ */ new Map();
  for (const i of e) {
    const a = i.group || i.category || "General", l = t.get(a);
    l ? l.settings.push(i) : t.set(a, { id: a, label: a, settings: [i] });
  }
  return Array.from(t.values());
}
function H(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function v(e) {
  return (e ?? "").trim().toLowerCase();
}
function ze(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function Z(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, i) => t.id.localeCompare(i.id)));
}
function K(e) {
  if (Pe(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function Pe(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  ve as FeatureManagementPage,
  je as createApplyPayload,
  K as getErrorMessage,
  be as isDirty,
  Le as register,
  xe as registerBuiltInSettingEditors,
  fe as selectSettingEditor
};
