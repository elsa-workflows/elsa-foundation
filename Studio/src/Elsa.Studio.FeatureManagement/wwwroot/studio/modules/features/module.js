import { useMemo as E, useState as C, useEffect as k } from "react";
var J = { exports: {} }, w = {};
var Q;
function pe() {
  if (Q) return w;
  Q = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function r(i, l, s) {
    var m = null;
    if (s !== void 0 && (m = "" + s), l.key !== void 0 && (m = "" + l.key), "key" in l) {
      s = {};
      for (var o in l)
        o !== "key" && (s[o] = l[o]);
    } else s = l;
    return l = s.ref, {
      $$typeof: e,
      type: i,
      key: m,
      ref: l !== void 0 ? l : null,
      props: s
    };
  }
  return w.Fragment = t, w.jsx = r, w.jsxs = r, w;
}
var X;
function xe() {
  return X || (X = 1, J.exports = pe()), J.exports;
}
var n = xe();
const A = "__all", te = "__uncategorized", fe = "elsa-studio:modules-changed", je = {
  all: ["features"],
  catalog: (e) => [...je.all, e, "catalog"]
};
let V;
function We(e) {
  V = e, be(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: ke
  });
}
function be(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || M(t, "select"),
    component: Me
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => N(t.jsonType) === "boolean",
    component: Je
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(N(t.jsonType)),
    component: $e
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(N(t.jsonType)) || M(t, "json") || M(t, "textarea"),
    component: Ve
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: He
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Oe
  });
}
function ye(e, t) {
  const r = e.settingEditors.list().filter((i) => i.supports(t)).sort((i, l) => (i.order ?? 500) - (l.order ?? 500))[0];
  if (!r)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return r;
}
function ve(e, t) {
  return {
    revision: e,
    features: t.map((r) => ({
      id: r.id,
      enabled: r.enabled,
      configuration: r.enabled ? r.configuration : {}
    }))
  };
}
function Ne(e, t) {
  return e ? ee(e.features) !== ee(t) : !1;
}
function Se(e) {
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
function Z() {
  return {
    catalog: null,
    draft: [],
    selectedId: "",
    checkedFeatureIds: /* @__PURE__ */ new Set(),
    selectedCategory: A,
    filterText: "",
    loading: !0,
    applying: !1,
    status: null,
    error: null
  };
}
function ke() {
  const e = E(() => Se(V), []), [t, r] = C("server"), [i, l] = C(() => ({
    studio: Z(),
    server: Z()
  })), s = e.find((a) => a.id === t) ?? e[0], m = i[s.id], { catalog: o, draft: u, selectedId: p, checkedFeatureIds: j, selectedCategory: x, filterText: y, loading: b, applying: F, status: B, error: _ } = m;
  k(() => {
    for (const a of e)
      G(a.id);
  }, []);
  const I = E(() => Pe(u, y), [u, y]), T = E(() => ze(I), [I]), f = E(
    () => Le(I, x),
    [I, x]
  ), S = f.find((a) => a.id === p) ?? f.find((a) => a.enabled) ?? f[0] ?? null, R = Ne(o, u), le = u.filter((a) => a.enabled).length, de = T.find((a) => a.id === x)?.label ?? "All categories", z = E(() => new Set(f.map((a) => a.id)), [f]), L = f.filter((a) => j.has(a.id)).length, P = f.length > 0 && L === f.length, ce = u.filter((a) => j.has(a.id)), v = !s.writable, D = b || F || v;
  k(() => {
    S && S.id !== p && g(s.id, (a) => ({ ...a, selectedId: S.id }));
  }, [s.id, S, p]), k(() => {
    T.some((a) => a.id === x) || g(s.id, (a) => ({ ...a, selectedCategory: A }));
  }, [s.id, T, x]), k(() => {
    g(s.id, (a) => {
      const d = new Set(a.draft.map((h) => h.id)), c = new Set(Array.from(a.checkedFeatureIds).filter((h) => d.has(h)));
      return c.size === a.checkedFeatureIds.size ? a : { ...a, checkedFeatureIds: c };
    });
  }, [s.id, u]);
  function g(a, d) {
    l((c) => ({
      ...c,
      [a]: d(c[a])
    }));
  }
  async function G(a = s.id) {
    const d = e.find((c) => c.id === a) ?? s;
    g(d.id, (c) => ({ ...c, loading: !0, error: null }));
    try {
      const c = await d.context.http.getJson("/modularity/features");
      g(d.id, (h) => ({
        ...h,
        catalog: c,
        draft: c.features.map(O),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (c) {
      g(d.id, (h) => ({ ...h, error: ne(c), loading: !1 }));
    }
  }
  async function oe() {
    if (!(!o || !R || v)) {
      g(s.id, (a) => ({ ...a, applying: !0, error: null, status: null }));
      try {
        const a = await s.context.http.postJson(
          "/modularity/features/apply",
          ve(o.revision, u)
        );
        g(s.id, (d) => ({
          ...d,
          catalog: a.catalog,
          draft: a.catalog.features.map(O),
          status: `Applied ${a.featureDescriptorCount} descriptor(s); reloaded ${a.reloadedShellCount} shell(s).`,
          applying: !1
        })), s.id === "studio" && window.dispatchEvent(new Event(fe));
      } catch (a) {
        g(s.id, (d) => ({ ...d, error: ne(a), applying: !1 }));
      }
    }
  }
  function U(a, d) {
    v || g(s.id, (c) => ({
      ...c,
      draft: c.draft.map((h) => h.id === a ? d(h) : h)
    }));
  }
  function Y(a) {
    U(a.id, (d) => ({ ...d, enabled: !d.enabled }));
  }
  function ue(a, d) {
    v || g(s.id, (c) => {
      const h = new Set(c.checkedFeatureIds);
      return d ? h.add(a) : h.delete(a), { ...c, checkedFeatureIds: h };
    });
  }
  function me() {
    v || g(s.id, (a) => {
      const d = new Set(a.checkedFeatureIds);
      if (P)
        for (const c of z)
          d.delete(c);
      else
        for (const c of z)
          d.add(c);
      return { ...a, checkedFeatureIds: d };
    });
  }
  function W(a) {
    v || j.size === 0 || g(s.id, (d) => ({
      ...d,
      draft: d.draft.map((c) => d.checkedFeatureIds.has(c.id) ? { ...c, enabled: a } : c)
    }));
  }
  function ge(a, d, c) {
    U(a.id, (h) => ({
      ...h,
      configuration: {
        ...h.configuration,
        [d.name]: c
      }
    }));
  }
  function he() {
    o && g(s.id, (a) => ({
      ...a,
      draft: o.features.map(O),
      status: null,
      error: null
    }));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          s.label,
          ": ",
          le,
          " enabled of ",
          u.length,
          " available",
          R ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => G(), disabled: b || F, children: "Refresh" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: he, disabled: !R || b || F || v, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: oe, disabled: !R || b || F || v, children: F ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(Ce, { hosts: e, activeHostId: s.id, onSelect: r }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    B ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: B }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        Ee,
        {
          categories: T,
          selectedCategory: x,
          filterText: y,
          onSelect: (a) => g(s.id, (d) => ({ ...d, selectedCategory: a })),
          onFilterTextChange: (a) => g(s.id, (d) => ({ ...d, filterText: a }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: de }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              f.length,
              " feature",
              f.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            f.filter((a) => a.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          Fe,
          {
            disabled: D || f.length === 0,
            selectedCount: j.size,
            visibleCount: f.length,
            visibleSelectedCount: L,
            selectedEnabledCount: ce.filter((a) => a.enabled).length,
            allVisibleChecked: P,
            onToggleVisible: me,
            onClearSelection: () => g(s.id, (a) => ({ ...a, checkedFeatureIds: /* @__PURE__ */ new Set() })),
            onEnableSelected: () => W(!0),
            onDisableSelected: () => W(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": b, children: [
          b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !b && u.length > 0 && f.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: y.trim() ? "No features match this filter." : "No features match this category." }) : null,
          f.map((a) => /* @__PURE__ */ n.jsx(
            we,
            {
              feature: a,
              selected: a.id === S?.id,
              checked: j.has(a.id),
              disabled: D,
              onSelect: () => g(s.id, (d) => ({ ...d, selectedId: a.id })),
              onToggle: () => Y(a),
              onCheckedChange: (d) => ue(a.id, d)
            },
            a.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        Ie,
        {
          feature: S,
          disabled: D,
          onToggle: Y,
          onSettingChange: ge
        }
      )
    ] })
  ] });
}
function Ce({
  hosts: e,
  activeHostId: t,
  onSelect: r
}) {
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((i) => /* @__PURE__ */ n.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": i.id === t,
      className: i.id === t ? "active" : "",
      onClick: () => r(i.id),
      children: [
        /* @__PURE__ */ n.jsx("span", { children: i.label }),
        /* @__PURE__ */ n.jsx("small", { children: i.runtime })
      ]
    },
    i.id
  )) });
}
function Fe({
  disabled: e,
  selectedCount: t,
  visibleCount: r,
  visibleSelectedCount: i,
  selectedEnabledCount: l,
  allVisibleChecked: s,
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
          checked: s,
          disabled: e,
          onChange: m,
          "aria-label": s ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        i,
        "/",
        r,
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
function Ee({
  categories: e,
  selectedCategory: t,
  filterText: r,
  onFilterTextChange: i,
  onSelect: l
}) {
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ n.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ n.jsxs("label", { className: "feature-management-filter", children: [
      /* @__PURE__ */ n.jsx("span", { children: "Filter features" }),
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "search",
          "aria-label": "Filter features",
          placeholder: "Filter by name, package, setting...",
          value: r,
          onChange: (s) => i(s.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((s) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: s.id === t ? "active" : "",
        onClick: () => l(s.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: s.label }),
          /* @__PURE__ */ n.jsx("em", { children: s.count })
        ]
      },
      s.id
    )) })
  ] });
}
function we({
  feature: e,
  selected: t,
  checked: r,
  disabled: i,
  onSelect: l,
  onToggle: s,
  onCheckedChange: m
}) {
  const o = e.displayName || e.id, u = !Ge(o, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: r,
        disabled: i,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (p) => m(p.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: s,
        disabled: i,
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
function Ie({
  feature: e,
  disabled: t,
  onToggle: r,
  onSettingChange: i
}) {
  const [l, s] = C(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const m = e.displayName || e.id, o = t || !e.enabled, u = l === e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ n.jsx("h3", { children: m })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => r(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(Te, { feature: e, displayName: m }) }),
    /* @__PURE__ */ n.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-section-heading", children: [
        /* @__PURE__ */ n.jsx("h4", { children: "Settings" }),
        e.settings.length > 0 ? /* @__PURE__ */ n.jsx(
          "button",
          {
            type: "button",
            onClick: () => s(e.id),
            disabled: o,
            title: e.enabled ? void 0 : "Enable the feature before editing settings.",
            children: "Edit settings"
          }
        ) : null
      ] }),
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(Re, { feature: e })
    ] }),
    u ? /* @__PURE__ */ n.jsx(
      Ae,
      {
        feature: e,
        disabled: o,
        onSettingChange: i,
        onClose: () => s(null)
      }
    ) : null
  ] });
}
function Te({ feature: e, displayName: t }) {
  const r = e.categories.length > 0 ? e.categories.join(", ") : "Uncategorized", i = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: r })
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
function Re({ feature: e }) {
  const t = re(e.settings);
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings-summary", children: t.map((r) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-summary-group", children: [
    /* @__PURE__ */ n.jsx("h5", { children: r.label }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-setting-summary-list", children: r.settings.map((i) => /* @__PURE__ */ n.jsxs("div", { className: "feature-management-setting-summary-row", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-summary-meta", children: [
        /* @__PURE__ */ n.jsxs("span", { children: [
          /* @__PURE__ */ n.jsx("strong", { children: i.displayName || i.name }),
          i.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
          i.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
          i.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
        ] }),
        i.description ? /* @__PURE__ */ n.jsx("small", { children: i.description }) : null,
        /* @__PURE__ */ n.jsx("code", { children: i.name })
      ] }),
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: Be(i, ae(e, i)) })
    ] }, i.name)) })
  ] }, r.id)) });
}
function Ae({
  feature: e,
  disabled: t,
  onSettingChange: r,
  onClose: i
}) {
  const [l, s] = C(() => H(e)), m = re(e.settings);
  k(() => {
    s(H(e));
  }, [e.id]);
  function o(j, x) {
    s((y) => ({
      ...y,
      [j.name]: x
    }));
  }
  function u() {
    s(H(e));
  }
  function p() {
    for (const j of e.settings)
      r(e, j, l[j.name]);
    i();
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
          /* @__PURE__ */ n.jsx("button", { type: "button", onClick: i, children: "Close" })
        ] }),
        /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-body", children: /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: m.map((j) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
          /* @__PURE__ */ n.jsx("h5", { children: j.label }),
          /* @__PURE__ */ n.jsx("div", { children: j.settings.map((x) => {
            const y = ye(V, x).component;
            return /* @__PURE__ */ n.jsx(De, { setting: x, children: /* @__PURE__ */ n.jsx(
              y,
              {
                setting: x,
                value: l[x.name],
                disabled: t,
                onChange: (b) => o(x, b)
              }
            ) }, x.name);
          }) })
        ] }, j.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: i, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: u, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: p, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function De({ setting: e, children: t }) {
  const r = qe(e);
  return /* @__PURE__ */ n.jsxs("label", { className: r ? "feature-management-setting boolean" : "feature-management-setting", children: [
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-main", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.name }),
        e.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
        e.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      r ? /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }) : null
    ] }),
    r ? null : /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }),
    e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
    /* @__PURE__ */ n.jsx("code", { children: e.name })
  ] });
}
function Je({ value: e, disabled: t, onChange: r }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "checkbox",
      className: "feature-management-setting-switch-input",
      checked: !!e,
      disabled: t,
      onChange: (i) => r(i.target.checked)
    }
  );
}
function Oe({ value: e, disabled: t, onChange: r }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: q(e), disabled: t, onChange: (i) => r(i.target.value) });
}
function He({ value: e, disabled: t, onChange: r }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: q(e), disabled: t, onChange: (i) => r(i.target.value) });
}
function $e({ setting: e, value: t, disabled: r, onChange: i }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: q(t),
      disabled: r,
      onChange: (l) => i(N(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function Me({ setting: e, value: t, disabled: r, onChange: i }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: r, onChange: (s) => {
    const m = e.options.find((o) => JSON.stringify(o.value) === s.target.value);
    i(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((s) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(s.value), children: s.label }, `${e.name}-${JSON.stringify(s.value)}`))
  ] });
}
function Ve({ setting: e, value: t, disabled: r, onChange: i }) {
  const [l, s] = C(K(t, e)), [m, o] = C(!1);
  return k(() => {
    s(K(t, e)), o(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: r, rows: 5, onChange: (u) => {
      const p = u.target.value;
      s(p);
      try {
        i(JSON.parse(p || se(e))), o(!1);
      } catch {
        o(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function O(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function ae(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? _e(t);
}
function H(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, ae(e, t)]));
}
function qe(e) {
  return N(e.jsonType) === "boolean";
}
function Be(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (N(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const r = typeof t == "object" ? JSON.stringify(t) : String(t);
  return r.length > 96 ? `${r.slice(0, 93)}...` : r;
}
function _e(e) {
  const t = N(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function q(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function K(e, t) {
  return e == null || e === "" ? se(t) : JSON.stringify(e, null, 2);
}
function se(e) {
  return N(e.jsonType) === "array" ? "[]" : "{}";
}
function ze(e) {
  const t = /* @__PURE__ */ new Map();
  for (const r of e) {
    const i = ie(r);
    for (const l of i)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: A, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([r], [i]) => $(r).localeCompare($(i))).map(([r, i]) => ({ id: r, label: $(r), count: i }))
  ];
}
function Le(e, t) {
  return t === A ? e : e.filter((r) => ie(r).includes(t));
}
function Pe(e, t) {
  const r = t.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return r.length === 0 ? e : e.filter((i) => {
    const l = [
      i.id,
      i.displayName,
      i.description,
      i.sourceKind,
      i.packageId,
      i.packageVersion,
      ...i.categories,
      ...i.settings.flatMap((s) => [
        s.name,
        s.displayName,
        s.description,
        s.category,
        s.group,
        s.uiHint
      ])
    ].filter((s) => typeof s == "string").join(" ").toLowerCase();
    return r.every((s) => l.includes(s));
  });
}
function ie(e) {
  return e.categories.length > 0 ? e.categories : [te];
}
function $(e) {
  return e === te ? "Uncategorized" : e;
}
function re(e) {
  const t = /* @__PURE__ */ new Map();
  for (const r of e) {
    const i = r.group || r.category || "General", l = t.get(i);
    l ? l.settings.push(r) : t.set(i, { id: i, label: i, settings: [r] });
  }
  return Array.from(t.values());
}
function M(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function N(e) {
  return (e ?? "").trim().toLowerCase();
}
function Ge(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function ee(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, r) => t.id.localeCompare(r.id)));
}
function ne(e) {
  if (Ue(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function Ue(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  ke as FeatureManagementPage,
  ve as createApplyPayload,
  je as featureKeys,
  Pe as filterFeaturesByText,
  ne as getErrorMessage,
  Ne as isDirty,
  We as register,
  be as registerBuiltInSettingEditors,
  ye as selectSettingEditor
};
