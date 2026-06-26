import { forwardRef as ae, createElement as H, useMemo as w, useState as C, useEffect as k } from "react";
var J = { exports: {} }, E = {};
var K;
function je() {
  if (K) return E;
  K = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function i(s, l, a) {
    var m = null;
    if (a !== void 0 && (m = "" + a), l.key !== void 0 && (m = "" + l.key), "key" in l) {
      a = {};
      for (var c in l)
        c !== "key" && (a[c] = l[c]);
    } else a = l;
    return l = a.ref, {
      $$typeof: e,
      type: s,
      key: m,
      ref: l !== void 0 ? l : null,
      props: a
    };
  }
  return E.Fragment = t, E.jsx = i, E.jsxs = i, E;
}
var Q;
function be() {
  return Q || (Q = 1, J.exports = je()), J.exports;
}
var n = be();
const ye = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), se = (...e) => e.filter((t, i, s) => !!t && t.trim() !== "" && s.indexOf(t) === i).join(" ").trim();
var ve = {
  xmlns: "http://www.w3.org/2000/svg",
  width: 24,
  height: 24,
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: 2,
  strokeLinecap: "round",
  strokeLinejoin: "round"
};
const Ne = ae(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: i = 2,
    absoluteStrokeWidth: s,
    className: l = "",
    children: a,
    iconNode: m,
    ...c
  }, u) => H(
    "svg",
    {
      ref: u,
      ...ve,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: s ? Number(i) * 24 / Number(t) : i,
      className: se("lucide", l),
      ...c
    },
    [
      ...m.map(([p, x]) => H(p, x)),
      ...Array.isArray(a) ? a : [a]
    ]
  )
);
const Se = (e, t) => {
  const i = ae(
    ({ className: s, ...l }, a) => H(Ne, {
      ref: a,
      iconNode: t,
      className: se(`lucide-${ye(e)}`, s),
      ...l
    })
  );
  return i.displayName = `${e}`, i;
};
const ke = Se("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]), A = "__all", ie = "__uncategorized", Ce = "elsa-studio:modules-changed", Fe = {
  all: ["features"],
  catalog: (e) => [...Fe.all, e, "catalog"]
};
let B;
function an(e) {
  B = e, we(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: Ae
  });
}
function we(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || L(t, "select"),
    component: Ge
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => N(t.jsonType) === "boolean",
    component: Ve
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(N(t.jsonType)),
    component: Pe
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(N(t.jsonType)) || L(t, "json") || L(t, "textarea"),
    component: Ue
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: ze
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: _e
  });
}
function Ee(e, t) {
  const i = e.settingEditors.list().filter((s) => s.supports(t)).sort((s, l) => (s.order ?? 500) - (l.order ?? 500))[0];
  if (!i)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return i;
}
function Ie(e, t) {
  return {
    revision: e,
    features: t.map((i) => ({
      id: i.id,
      enabled: i.enabled,
      configuration: i.enabled ? i.configuration : {}
    }))
  };
}
function Te(e, t) {
  return e ? ne(e.features) !== ne(t) : !1;
}
function Re(e) {
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
function X() {
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
function Ae() {
  const e = w(() => Re(B), []), [t, i] = C("server"), [s, l] = C(() => ({
    studio: X(),
    server: X()
  })), a = e.find((r) => r.id === t) ?? e[0], m = s[a.id], { catalog: c, draft: u, selectedId: p, checkedFeatureIds: x, selectedCategory: f, filterText: y, loading: b, applying: F, status: V, error: _ } = m;
  k(() => {
    for (const r of e)
      U(r.id);
  }, []);
  const I = w(() => Xe(u, y), [u, y]), T = w(() => Ke(I), [I]), j = w(
    () => Qe(I, f),
    [I, f]
  ), S = j.find((r) => r.id === p) ?? j.find((r) => r.enabled) ?? j[0] ?? null, R = Te(c, u), ce = u.filter((r) => r.enabled).length, ue = T.find((r) => r.id === f)?.label ?? "All categories", z = w(() => new Set(j.map((r) => r.id)), [j]), P = j.filter((r) => x.has(r.id)).length, G = j.length > 0 && P === j.length, me = u.filter((r) => x.has(r.id)), v = !a.writable, D = b || F || v;
  k(() => {
    S && S.id !== p && h(a.id, (r) => ({ ...r, selectedId: S.id }));
  }, [a.id, S, p]), k(() => {
    T.some((r) => r.id === f) || h(a.id, (r) => ({ ...r, selectedCategory: A }));
  }, [a.id, T, f]), k(() => {
    h(a.id, (r) => {
      const d = new Set(r.draft.map((g) => g.id)), o = new Set(Array.from(r.checkedFeatureIds).filter((g) => d.has(g)));
      return o.size === r.checkedFeatureIds.size ? r : { ...r, checkedFeatureIds: o };
    });
  }, [a.id, u]);
  function h(r, d) {
    l((o) => ({
      ...o,
      [r]: d(o[r])
    }));
  }
  async function U(r = a.id) {
    const d = e.find((o) => o.id === r) ?? a;
    h(d.id, (o) => ({ ...o, loading: !0, error: null }));
    try {
      const o = await d.context.http.getJson("/modularity/features");
      h(d.id, (g) => ({
        ...g,
        catalog: o,
        draft: o.features.map($),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (o) {
      h(d.id, (g) => ({ ...g, error: te(o), loading: !1 }));
    }
  }
  async function he() {
    if (!(!c || !R || v)) {
      h(a.id, (r) => ({ ...r, applying: !0, error: null, status: null }));
      try {
        const r = await a.context.http.postJson(
          "/modularity/features/apply",
          Ie(c.revision, u)
        );
        h(a.id, (d) => ({
          ...d,
          catalog: r.catalog,
          draft: r.catalog.features.map($),
          status: `Applied ${r.featureDescriptorCount} descriptor(s); reloaded ${r.reloadedShellCount} shell(s).`,
          applying: !1
        })), a.id === "studio" && window.dispatchEvent(new Event(Ce));
      } catch (r) {
        h(a.id, (d) => ({ ...d, error: te(r), applying: !1 }));
      }
    }
  }
  function Y(r, d) {
    v || h(a.id, (o) => ({
      ...o,
      draft: o.draft.map((g) => g.id === r ? d(g) : g)
    }));
  }
  function W(r) {
    Y(r.id, (d) => ({ ...d, enabled: !d.enabled }));
  }
  function ge(r, d) {
    v || h(a.id, (o) => {
      const g = new Set(o.checkedFeatureIds);
      return d ? g.add(r) : g.delete(r), { ...o, checkedFeatureIds: g };
    });
  }
  function pe() {
    v || h(a.id, (r) => {
      const d = new Set(r.checkedFeatureIds);
      if (G)
        for (const o of z)
          d.delete(o);
      else
        for (const o of z)
          d.add(o);
      return { ...r, checkedFeatureIds: d };
    });
  }
  function Z(r) {
    v || x.size === 0 || h(a.id, (d) => ({
      ...d,
      draft: d.draft.map((o) => d.checkedFeatureIds.has(o.id) ? { ...o, enabled: r } : o)
    }));
  }
  function xe(r, d, o) {
    Y(r.id, (g) => ({
      ...g,
      configuration: {
        ...g.configuration,
        [d.name]: o
      }
    }));
  }
  function fe() {
    c && h(a.id, (r) => ({
      ...r,
      draft: c.features.map($),
      status: null,
      error: null
    }));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          a.label,
          ": ",
          ce,
          " enabled of ",
          u.length,
          " available",
          R ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "feature-management-icon-button", "aria-label": "Refresh features", title: "Refresh", onClick: () => U(), disabled: b || F, children: /* @__PURE__ */ n.jsx(ke, { size: 15 }) }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: fe, disabled: !R || b || F || v, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: he, disabled: !R || b || F || v, children: F ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(De, { hosts: e, activeHostId: a.id, onSelect: i }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    V ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: V }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        $e,
        {
          categories: T,
          selectedCategory: f,
          filterText: y,
          onSelect: (r) => h(a.id, (d) => ({ ...d, selectedCategory: r })),
          onFilterTextChange: (r) => h(a.id, (d) => ({ ...d, filterText: r }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: ue }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              j.length,
              " feature",
              j.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            j.filter((r) => r.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          Je,
          {
            disabled: D || j.length === 0,
            selectedCount: x.size,
            visibleCount: j.length,
            visibleSelectedCount: P,
            selectedEnabledCount: me.filter((r) => r.enabled).length,
            allVisibleChecked: G,
            onToggleVisible: pe,
            onClearSelection: () => h(a.id, (r) => ({ ...r, checkedFeatureIds: /* @__PURE__ */ new Set() })),
            onEnableSelected: () => Z(!0),
            onDisableSelected: () => Z(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": b, children: [
          b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !b && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !b && u.length > 0 && j.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: y.trim() ? "No features match this filter." : "No features match this category." }) : null,
          j.map((r) => /* @__PURE__ */ n.jsx(
            Me,
            {
              feature: r,
              selected: r.id === S?.id,
              checked: x.has(r.id),
              disabled: D,
              onSelect: () => h(a.id, (d) => ({ ...d, selectedId: r.id })),
              onToggle: () => W(r),
              onCheckedChange: (d) => ge(r.id, d)
            },
            r.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        Oe,
        {
          feature: S,
          disabled: D,
          onToggle: W,
          onSettingChange: xe
        }
      )
    ] })
  ] });
}
function De({
  hosts: e,
  activeHostId: t,
  onSelect: i
}) {
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((s) => /* @__PURE__ */ n.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": s.id === t,
      className: s.id === t ? "active" : "",
      onClick: () => i(s.id),
      children: [
        /* @__PURE__ */ n.jsx("span", { children: s.label }),
        /* @__PURE__ */ n.jsx("small", { children: s.runtime })
      ]
    },
    s.id
  )) });
}
function Je({
  disabled: e,
  selectedCount: t,
  visibleCount: i,
  visibleSelectedCount: s,
  selectedEnabledCount: l,
  allVisibleChecked: a,
  onToggleVisible: m,
  onClearSelection: c,
  onEnableSelected: u,
  onDisableSelected: p
}) {
  return /* @__PURE__ */ n.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ n.jsxs("label", { children: [
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "checkbox",
          checked: a,
          disabled: e,
          onChange: m,
          "aria-label": a ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        s,
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
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: c, children: "Clear" })
    ] })
  ] });
}
function $e({
  categories: e,
  selectedCategory: t,
  filterText: i,
  onFilterTextChange: s,
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
          value: i,
          onChange: (a) => s(a.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((a) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: a.id === t ? "active" : "",
        onClick: () => l(a.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: a.label }),
          /* @__PURE__ */ n.jsx("em", { children: a.count })
        ]
      },
      a.id
    )) })
  ] });
}
function Me({
  feature: e,
  selected: t,
  checked: i,
  disabled: s,
  onSelect: l,
  onToggle: a,
  onCheckedChange: m
}) {
  const c = e.displayName || e.id, u = !en(c, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: i,
        disabled: s,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (p) => m(p.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: a,
        disabled: s,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: l, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: c }),
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
function Oe({
  feature: e,
  disabled: t,
  onToggle: i,
  onSettingChange: s
}) {
  const [l, a] = C(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const m = e.displayName || e.id, c = t || !e.enabled, u = l === e.id;
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
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(Le, { feature: e, displayName: m }) }),
    /* @__PURE__ */ n.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-section-heading", children: [
        /* @__PURE__ */ n.jsx("h4", { children: "Settings" }),
        e.settings.length > 0 ? /* @__PURE__ */ n.jsx(
          "button",
          {
            type: "button",
            onClick: () => a(e.id),
            disabled: c,
            title: e.enabled ? void 0 : "Enable the feature before editing settings.",
            children: "Edit settings"
          }
        ) : null
      ] }),
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(He, { feature: e })
    ] }),
    u ? /* @__PURE__ */ n.jsx(
      Be,
      {
        feature: e,
        disabled: c,
        onSettingChange: s,
        onClose: () => a(null)
      }
    ) : null
  ] });
}
function Le({ feature: e, displayName: t }) {
  const i = e.categories.length > 0 ? e.categories.join(", ") : "Uncategorized", s = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: s })
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
function He({ feature: e }) {
  const t = oe(e.settings);
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings-summary", children: t.map((i) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-summary-group", children: [
    /* @__PURE__ */ n.jsx("h5", { children: i.label }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-setting-summary-list", children: i.settings.map((s) => /* @__PURE__ */ n.jsxs("div", { className: "feature-management-setting-summary-row", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-summary-meta", children: [
        /* @__PURE__ */ n.jsxs("span", { children: [
          /* @__PURE__ */ n.jsx("strong", { children: s.displayName || s.name }),
          s.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
          s.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
          s.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
        ] }),
        s.description ? /* @__PURE__ */ n.jsx("small", { children: s.description }) : null,
        /* @__PURE__ */ n.jsx("code", { children: s.name })
      ] }),
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: We(s, re(e, s)) })
    ] }, s.name)) })
  ] }, i.id)) });
}
function Be({
  feature: e,
  disabled: t,
  onSettingChange: i,
  onClose: s
}) {
  const [l, a] = C(() => M(e)), m = oe(e.settings);
  k(() => {
    a(M(e));
  }, [e.id]);
  function c(x, f) {
    a((y) => ({
      ...y,
      [x.name]: f
    }));
  }
  function u() {
    a(M(e));
  }
  function p() {
    for (const x of e.settings)
      i(e, x, l[x.name]);
    s();
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
          /* @__PURE__ */ n.jsx("button", { type: "button", onClick: s, children: "Close" })
        ] }),
        /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-body", children: /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: m.map((x) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
          /* @__PURE__ */ n.jsx("h5", { children: x.label }),
          /* @__PURE__ */ n.jsx("div", { children: x.settings.map((f) => {
            const y = Ee(B, f).component;
            return /* @__PURE__ */ n.jsx(qe, { setting: f, children: /* @__PURE__ */ n.jsx(
              y,
              {
                setting: f,
                value: l[f.name],
                disabled: t,
                onChange: (b) => c(f, b)
              }
            ) }, f.name);
          }) })
        ] }, x.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: u, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: p, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function qe({ setting: e, children: t }) {
  const i = Ye(e);
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
function Ve({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "checkbox",
      className: "feature-management-setting-switch-input",
      checked: !!e,
      disabled: t,
      onChange: (s) => i(s.target.checked)
    }
  );
}
function _e({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: q(e), disabled: t, onChange: (s) => i(s.target.value) });
}
function ze({ value: e, disabled: t, onChange: i }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: q(e), disabled: t, onChange: (s) => i(s.target.value) });
}
function Pe({ setting: e, value: t, disabled: i, onChange: s }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: q(t),
      disabled: i,
      onChange: (l) => s(N(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function Ge({ setting: e, value: t, disabled: i, onChange: s }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: i, onChange: (a) => {
    const m = e.options.find((c) => JSON.stringify(c.value) === a.target.value);
    s(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((a) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(a.value), children: a.label }, `${e.name}-${JSON.stringify(a.value)}`))
  ] });
}
function Ue({ setting: e, value: t, disabled: i, onChange: s }) {
  const [l, a] = C(ee(t, e)), [m, c] = C(!1);
  return k(() => {
    a(ee(t, e)), c(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: i, rows: 5, onChange: (u) => {
      const p = u.target.value;
      a(p);
      try {
        s(JSON.parse(p || le(e))), c(!1);
      } catch {
        c(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function $(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function re(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? Ze(t);
}
function M(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, re(e, t)]));
}
function Ye(e) {
  return N(e.jsonType) === "boolean";
}
function We(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (N(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const i = typeof t == "object" ? JSON.stringify(t) : String(t);
  return i.length > 96 ? `${i.slice(0, 93)}...` : i;
}
function Ze(e) {
  const t = N(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function q(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function ee(e, t) {
  return e == null || e === "" ? le(t) : JSON.stringify(e, null, 2);
}
function le(e) {
  return N(e.jsonType) === "array" ? "[]" : "{}";
}
function Ke(e) {
  const t = /* @__PURE__ */ new Map();
  for (const i of e) {
    const s = de(i);
    for (const l of s)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: A, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([i], [s]) => O(i).localeCompare(O(s))).map(([i, s]) => ({ id: i, label: O(i), count: s }))
  ];
}
function Qe(e, t) {
  return t === A ? e : e.filter((i) => de(i).includes(t));
}
function Xe(e, t) {
  const i = t.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return i.length === 0 ? e : e.filter((s) => {
    const l = [
      s.id,
      s.displayName,
      s.description,
      s.sourceKind,
      s.packageId,
      s.packageVersion,
      ...s.categories,
      ...s.settings.flatMap((a) => [
        a.name,
        a.displayName,
        a.description,
        a.category,
        a.group,
        a.uiHint
      ])
    ].filter((a) => typeof a == "string").join(" ").toLowerCase();
    return i.every((a) => l.includes(a));
  });
}
function de(e) {
  return e.categories.length > 0 ? e.categories : [ie];
}
function O(e) {
  return e === ie ? "Uncategorized" : e;
}
function oe(e) {
  const t = /* @__PURE__ */ new Map();
  for (const i of e) {
    const s = i.group || i.category || "General", l = t.get(s);
    l ? l.settings.push(i) : t.set(s, { id: s, label: s, settings: [i] });
  }
  return Array.from(t.values());
}
function L(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function N(e) {
  return (e ?? "").trim().toLowerCase();
}
function en(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function ne(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, i) => t.id.localeCompare(i.id)));
}
function te(e) {
  if (nn(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function nn(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  Ae as FeatureManagementPage,
  Ie as createApplyPayload,
  Fe as featureKeys,
  Xe as filterFeaturesByText,
  te as getErrorMessage,
  Te as isDirty,
  an as register,
  we as registerBuiltInSettingEditors,
  Ee as selectSettingEditor
};
