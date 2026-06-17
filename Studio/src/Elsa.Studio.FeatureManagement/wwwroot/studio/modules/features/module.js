import { useMemo as A, useState as T, useEffect as I } from "react";
var D = { exports: {} }, w = {};
var W;
function ue() {
  if (W) return w;
  W = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), n = /* @__PURE__ */ Symbol.for("react.fragment");
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
  return w.Fragment = n, w.jsx = a, w.jsxs = a, w;
}
var Q;
function me() {
  return Q || (Q = 1, D.exports = ue()), D.exports;
}
var t = me();
const R = "__all", ne = "__uncategorized";
let M;
function ze(e) {
  M = e, he(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: je
  });
}
function he(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (n) => (n.options?.length ?? 0) > 0 || $(n, "select"),
    component: Te
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (n) => F(n.jsonType) === "boolean",
    component: Fe
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (n) => ["integer", "number"].includes(F(n.jsonType)),
    component: Ie
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (n) => ["object", "array"].includes(F(n.jsonType)) || $(n, "json") || $(n, "textarea"),
    component: Ae
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (n) => n.secret || n.sensitive,
    component: we
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Ee
  });
}
function ge(e, n) {
  const a = e.settingEditors.list().filter((i) => i.supports(n)).sort((i, d) => (i.order ?? 500) - (d.order ?? 500))[0];
  if (!a)
    throw new Error(`No setting editor is registered for '${n.name}'.`);
  return a;
}
function pe(e, n) {
  return {
    revision: e,
    features: n.map((a) => ({
      id: a.id,
      enabled: a.enabled,
      configuration: a.enabled ? a.configuration : {}
    }))
  };
}
function xe(e, n) {
  return e ? ee(e.features) !== ee(n) : !1;
}
function fe(e) {
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
    selectedCategory: R,
    loading: !0,
    applying: !1,
    status: null,
    error: null
  };
}
function je() {
  const e = A(() => fe(M), []), [n, a] = T("server"), [i, d] = T(() => ({
    studio: X(),
    server: X()
  })), r = e.find((s) => s.id === n) ?? e[0], p = i[r.id], { catalog: o, draft: u, selectedId: x, checkedFeatureIds: f, selectedCategory: b, loading: j, applying: N, status: S, error: v } = p;
  I(() => {
    for (const s of e)
      L(s.id);
  }, []);
  const k = A(() => De(u), [u]), h = A(
    () => He(u, b),
    [u, b]
  ), C = h.find((s) => s.id === x) ?? h.find((s) => s.enabled) ?? h[0] ?? null, E = xe(o, u), ie = u.filter((s) => s.enabled).length, re = k.find((s) => s.id === b)?.label ?? "All categories", z = A(() => new Set(h.map((s) => s.id)), [h]), P = h.filter((s) => f.has(s.id)).length, q = h.length > 0 && P === h.length, le = u.filter((s) => f.has(s.id)), y = !r.writable, J = j || N || y;
  I(() => {
    C && C.id !== x && g(r.id, (s) => ({ ...s, selectedId: C.id }));
  }, [r.id, C, x]), I(() => {
    k.some((s) => s.id === b) || g(r.id, (s) => ({ ...s, selectedCategory: R }));
  }, [r.id, k, b]), I(() => {
    g(r.id, (s) => {
      const l = new Set(s.draft.map((m) => m.id)), c = new Set(Array.from(s.checkedFeatureIds).filter((m) => l.has(m)));
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
      g(l.id, (m) => ({
        ...m,
        catalog: c,
        draft: c.features.map(H),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (c) {
      g(l.id, (m) => ({ ...m, error: te(c), loading: !1 }));
    }
  }
  async function V() {
    if (!(!o || !E || y)) {
      g(r.id, (s) => ({ ...s, applying: !0, error: null, status: null }));
      try {
        const s = await r.context.http.postJson(
          "/modularity/features/apply",
          pe(o.revision, u)
        );
        g(r.id, (l) => ({
          ...l,
          catalog: s.catalog,
          draft: s.catalog.features.map(H),
          status: `Applied ${s.featureDescriptorCount} descriptor(s); reloaded ${s.reloadedShellCount} shell(s).`,
          applying: !1
        }));
      } catch (s) {
        g(r.id, (l) => ({ ...l, error: te(s), applying: !1 }));
      }
    }
  }
  function B(s, l) {
    y || g(r.id, (c) => ({
      ...c,
      draft: c.draft.map((m) => m.id === s ? l(m) : m)
    }));
  }
  function U(s) {
    B(s.id, (l) => ({ ...l, enabled: !l.enabled }));
  }
  function de(s, l) {
    y || g(r.id, (c) => {
      const m = new Set(c.checkedFeatureIds);
      return l ? m.add(s) : m.delete(s), { ...c, checkedFeatureIds: m };
    });
  }
  function ce() {
    y || g(r.id, (s) => {
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
    y || f.size === 0 || g(r.id, (l) => ({
      ...l,
      draft: l.draft.map((c) => l.checkedFeatureIds.has(c.id) ? { ...c, enabled: s } : c)
    }));
  }
  function oe(s, l, c) {
    B(s.id, (m) => ({
      ...m,
      configuration: {
        ...m.configuration,
        [l.name]: c
      }
    }));
  }
  function Y() {
    o && g(r.id, (s) => ({
      ...s,
      draft: o.features.map(H),
      status: null,
      error: null
    }));
  }
  return /* @__PURE__ */ t.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ t.jsxs("p", { children: [
          r.label,
          ": ",
          ie,
          " enabled of ",
          u.length,
          " available",
          E ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => L(), disabled: j || N, children: "Refresh" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: Y, disabled: !E || j || N || y, children: "Reset" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", className: "primary", onClick: V, disabled: !E || j || N || y, children: N ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ t.jsx(be, { hosts: e, activeHostId: r.id, onSelect: a }),
    v ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-error", children: v }) : null,
    S ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-status", children: S }) : null,
    y ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-status", children: "Studio features are shown from the Studio host runtime. Feature toggles are available for backend features on the Server tab." }) : null,
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ t.jsx(
        ye,
        {
          categories: k,
          selectedCategory: b,
          onSelect: (s) => g(r.id, (l) => ({ ...l, selectedCategory: s }))
        }
      ),
      /* @__PURE__ */ t.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ t.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ t.jsxs("div", { children: [
            /* @__PURE__ */ t.jsx("strong", { children: re }),
            /* @__PURE__ */ t.jsxs("span", { children: [
              h.length,
              " feature",
              h.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ t.jsxs("span", { children: [
            h.filter((s) => s.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ t.jsx(
          ve,
          {
            disabled: J || h.length === 0,
            selectedCount: f.size,
            visibleCount: h.length,
            visibleSelectedCount: P,
            selectedEnabledCount: le.filter((s) => s.enabled).length,
            allVisibleChecked: q,
            onToggleVisible: ce,
            onClearSelection: () => setCheckedFeatureIds(/* @__PURE__ */ new Set()),
            onEnableSelected: () => G(!0),
            onDisableSelected: () => G(!1)
          }
        ),
        /* @__PURE__ */ t.jsxs("div", { className: "feature-management-list", "aria-busy": j, children: [
          j && u.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !j && u.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !j && u.length > 0 && h.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "No features match this category." }) : null,
          h.map((s) => /* @__PURE__ */ t.jsx(
            Ne,
            {
              feature: s,
              selected: s.id === C?.id,
              checked: f.has(s.id),
              disabled: J,
              onSelect: () => g(r.id, (l) => ({ ...l, selectedId: s.id })),
              onToggle: () => U(s),
              onCheckedChange: (l) => de(s.id, l)
            },
            s.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ t.jsx(
        Se,
        {
          feature: C,
          disabled: J,
          dirty: E,
          onToggle: U,
          onSettingChange: oe,
          onReset: Y,
          onApply: V
        }
      )
    ] })
  ] });
}
function be({
  hosts: e,
  activeHostId: n,
  onSelect: a
}) {
  return /* @__PURE__ */ t.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((i) => /* @__PURE__ */ t.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": i.id === n,
      className: i.id === n ? "active" : "",
      onClick: () => a(i.id),
      children: [
        /* @__PURE__ */ t.jsx("span", { children: i.label }),
        /* @__PURE__ */ t.jsx("small", { children: i.runtime })
      ]
    },
    i.id
  )) });
}
function ve({
  disabled: e,
  selectedCount: n,
  visibleCount: a,
  visibleSelectedCount: i,
  selectedEnabledCount: d,
  allVisibleChecked: r,
  onToggleVisible: p,
  onClearSelection: o,
  onEnableSelected: u,
  onDisableSelected: x
}) {
  return /* @__PURE__ */ t.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ t.jsxs("label", { children: [
      /* @__PURE__ */ t.jsx(
        "input",
        {
          type: "checkbox",
          checked: r,
          disabled: e,
          onChange: p,
          "aria-label": r ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ t.jsxs("span", { children: [
        i,
        "/",
        a,
        " visible"
      ] })
    ] }),
    /* @__PURE__ */ t.jsxs("div", { children: [
      /* @__PURE__ */ t.jsxs("span", { title: `${d} enabled`, children: [
        n,
        " selected"
      ] }),
      /* @__PURE__ */ t.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Enable selected features",
          disabled: e || n === 0 || d === n,
          onClick: u,
          children: "Enable"
        }
      ),
      /* @__PURE__ */ t.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Disable selected features",
          disabled: e || n === 0 || d === 0,
          onClick: x,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ t.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || n === 0, onClick: o, children: "Clear" })
    ] })
  ] });
}
function ye({
  categories: e,
  selectedCategory: n,
  onSelect: a
}) {
  return /* @__PURE__ */ t.jsxs("aside", { className: "feature-management-categories", "aria-label": "Feature categories", children: [
    /* @__PURE__ */ t.jsx("span", { className: "feature-management-panel-label", children: "Categories" }),
    /* @__PURE__ */ t.jsx("div", { className: "feature-management-category-list", children: e.map((i) => /* @__PURE__ */ t.jsxs(
      "button",
      {
        type: "button",
        className: i.id === n ? "active" : "",
        onClick: () => a(i.id),
        children: [
          /* @__PURE__ */ t.jsx("span", { children: i.label }),
          /* @__PURE__ */ t.jsx("em", { children: i.count })
        ]
      },
      i.id
    )) })
  ] });
}
function Ne({
  feature: e,
  selected: n,
  checked: a,
  disabled: i,
  onSelect: d,
  onToggle: r,
  onCheckedChange: p
}) {
  const o = e.displayName || e.id, u = !$e(o, e.id);
  return /* @__PURE__ */ t.jsxs("article", { className: n ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ t.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: a,
        disabled: i,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (x) => p(x.target.checked)
      }
    ),
    /* @__PURE__ */ t.jsx(
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
    /* @__PURE__ */ t.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: d, children: [
      /* @__PURE__ */ t.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ t.jsx("strong", { children: o }),
        e.experimental ? /* @__PURE__ */ t.jsx("em", { children: "Experimental" }) : null,
        e.advanced ? /* @__PURE__ */ t.jsx("em", { children: "Advanced" }) : null
      ] }),
      u ? /* @__PURE__ */ t.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ t.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ t.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ t.jsx("span", { children: e.sourceKind }),
        e.categories.slice(0, 2).map((x) => /* @__PURE__ */ t.jsx("span", { children: x }, x)),
        e.categories.length > 2 ? /* @__PURE__ */ t.jsxs("span", { children: [
          "+",
          e.categories.length - 2
        ] }) : null
      ] })
    ] }),
    /* @__PURE__ */ t.jsx("span", { className: "feature-management-card-count", children: e.settings.length })
  ] });
}
function Se({
  feature: e,
  disabled: n,
  dirty: a,
  onToggle: i,
  onSettingChange: d,
  onReset: r,
  onApply: p
}) {
  const o = e?.id ?? null, [u, x] = T(() => ({
    featureId: o,
    tab: Z(e)
  })), f = u.featureId === o ? u.tab : Z(e), b = (S) => x({ featureId: o, tab: S });
  if (!e)
    return /* @__PURE__ */ t.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const j = Oe(e.settings), N = e.displayName || e.id;
  return /* @__PURE__ */ t.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ t.jsx("h3", { children: N })
      ] }),
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => i(e), disabled: n, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ t.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-inspector-tabs", role: "tablist", "aria-label": "Feature inspector", children: [
      /* @__PURE__ */ t.jsx(
        "button",
        {
          type: "button",
          role: "tab",
          "aria-selected": f === "details",
          className: f === "details" ? "active" : "",
          onClick: () => b("details"),
          children: "Details"
        }
      ),
      /* @__PURE__ */ t.jsxs(
        "button",
        {
          type: "button",
          role: "tab",
          "aria-selected": f === "settings",
          className: f === "settings" ? "active" : "",
          onClick: () => b("settings"),
          children: [
            "Settings ",
            /* @__PURE__ */ t.jsx("span", { children: e.settings.length })
          ]
        }
      )
    ] }),
    f === "details" ? /* @__PURE__ */ t.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ t.jsx(ke, { feature: e, displayName: N }) }) : /* @__PURE__ */ t.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ t.jsx("h4", { children: "Settings" }),
      e.settings.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ t.jsx("div", { className: "feature-management-settings", children: j.map((S) => /* @__PURE__ */ t.jsxs("section", { className: "feature-management-setting-group", children: [
        /* @__PURE__ */ t.jsx("h5", { children: S.label }),
        /* @__PURE__ */ t.jsx("div", { children: S.settings.map((v) => {
          const k = ge(M, v).component;
          return /* @__PURE__ */ t.jsx(Ce, { setting: v, children: /* @__PURE__ */ t.jsx(
            k,
            {
              setting: v,
              value: Re(e, v),
              disabled: n || !e.enabled,
              onChange: (h) => d(e, v, h)
            }
          ) }, v.name);
        }) })
      ] }, S.id)) })
    ] }),
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-sticky-actions", children: [
      /* @__PURE__ */ t.jsx("span", { children: a ? "Unsaved changes" : "No pending changes" }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: r, disabled: !a || n, children: "Reset" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", className: "primary", onClick: p, disabled: !a || n, children: "Apply" })
      ] })
    ] })
  ] });
}
function Z(e) {
  return e?.settings.length ? "settings" : "details";
}
function ke({ feature: e, displayName: n }) {
  const a = e.categories.length > 0 ? e.categories.join(", ") : "Uncategorized", i = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
  return /* @__PURE__ */ t.jsxs("section", { className: "feature-management-metadata", "aria-label": "Feature metadata", children: [
    /* @__PURE__ */ t.jsx("h4", { children: "Metadata" }),
    /* @__PURE__ */ t.jsxs("dl", { className: "feature-management-detail-list", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Display name" }),
        /* @__PURE__ */ t.jsx("dd", { children: n })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Technical name" }),
        /* @__PURE__ */ t.jsx("dd", { children: /* @__PURE__ */ t.jsx("code", { children: e.id }) })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Status" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.enabled ? "Enabled" : "Disabled" })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Source" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.sourceKind })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Categories" }),
        /* @__PURE__ */ t.jsx("dd", { children: a })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ t.jsx("dd", { children: i })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.settings.length })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Advanced" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.advanced ? "Yes" : "No" })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Experimental" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.experimental ? "Yes" : "No" })
      ] }),
      e.description ? /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.description })
      ] }) : null,
      e.manifestHash ? /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Manifest hash" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.manifestHash })
      ] }) : null,
      e.manifestPath ? /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("dt", { children: "Manifest path" }),
        /* @__PURE__ */ t.jsx("dd", { children: e.manifestPath })
      ] }) : null
    ] })
  ] });
}
function Ce({ setting: e, children: n }) {
  return /* @__PURE__ */ t.jsxs("label", { className: "feature-management-setting", children: [
    /* @__PURE__ */ t.jsxs("span", { className: "feature-management-setting-label", children: [
      /* @__PURE__ */ t.jsxs("span", { children: [
        /* @__PURE__ */ t.jsx("strong", { children: e.displayName || e.name }),
        e.required ? /* @__PURE__ */ t.jsx("em", { children: "Required" }) : null,
        e.restartRequired ? /* @__PURE__ */ t.jsx("em", { children: "Reload" }) : null,
        e.advanced ? /* @__PURE__ */ t.jsx("em", { children: "Advanced" }) : null
      ] }),
      e.description ? /* @__PURE__ */ t.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ t.jsx("code", { children: e.name })
    ] }),
    /* @__PURE__ */ t.jsx("span", { className: "feature-management-setting-control", children: n })
  ] });
}
function Fe({ value: e, disabled: n, onChange: a }) {
  return /* @__PURE__ */ t.jsx("input", { type: "checkbox", checked: !!e, disabled: n, onChange: (i) => a(i.target.checked) });
}
function Ee({ value: e, disabled: n, onChange: a }) {
  return /* @__PURE__ */ t.jsx("input", { type: "text", value: _(e), disabled: n, onChange: (i) => a(i.target.value) });
}
function we({ value: e, disabled: n, onChange: a }) {
  return /* @__PURE__ */ t.jsx("input", { type: "password", value: _(e), disabled: n, onChange: (i) => a(i.target.value) });
}
function Ie({ setting: e, value: n, disabled: a, onChange: i }) {
  return /* @__PURE__ */ t.jsx(
    "input",
    {
      type: "number",
      value: _(n),
      disabled: a,
      onChange: (d) => i(F(e.jsonType) === "integer" ? Number.parseInt(d.target.value || "0", 10) : Number.parseFloat(d.target.value || "0"))
    }
  );
}
function Te({ setting: e, value: n, disabled: a, onChange: i }) {
  const d = JSON.stringify(n ?? "");
  return /* @__PURE__ */ t.jsxs("select", { value: d, disabled: a, onChange: (r) => {
    const p = e.options.find((o) => JSON.stringify(o.value) === r.target.value);
    i(p?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ t.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((r) => /* @__PURE__ */ t.jsx("option", { value: JSON.stringify(r.value), children: r.label }, `${e.name}-${JSON.stringify(r.value)}`))
  ] });
}
function Ae({ setting: e, value: n, disabled: a, onChange: i }) {
  const [d, r] = T(K(n, e)), [p, o] = T(!1);
  return I(() => {
    r(K(n, e)), o(!1);
  }, [e.name, n]), /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
    /* @__PURE__ */ t.jsx("textarea", { value: d, disabled: a, rows: 5, onChange: (u) => {
      const x = u.target.value;
      r(x);
      try {
        i(JSON.parse(x || se(e))), o(!1);
      } catch {
        o(!0);
      }
    } }),
    p ? /* @__PURE__ */ t.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function H(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function Re(e, n) {
  return Object.prototype.hasOwnProperty.call(e.configuration, n.name) ? e.configuration[n.name] : n.defaultValue ?? Je(n);
}
function Je(e) {
  const n = F(e.jsonType);
  return n === "boolean" ? !1 : n === "integer" || n === "number" ? 0 : n === "array" ? [] : n === "object" ? {} : "";
}
function _(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function K(e, n) {
  return e == null || e === "" ? se(n) : JSON.stringify(e, null, 2);
}
function se(e) {
  return F(e.jsonType) === "array" ? "[]" : "{}";
}
function De(e) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e) {
    const i = ae(a);
    for (const d of i)
      n.set(d, (n.get(d) ?? 0) + 1);
  }
  return [
    { id: R, label: "All categories", count: e.length },
    ...Array.from(n.entries()).sort(([a], [i]) => O(a).localeCompare(O(i))).map(([a, i]) => ({ id: a, label: O(a), count: i }))
  ];
}
function He(e, n) {
  return n === R ? e : e.filter((a) => ae(a).includes(n));
}
function ae(e) {
  return e.categories.length > 0 ? e.categories : [ne];
}
function O(e) {
  return e === ne ? "Uncategorized" : e;
}
function Oe(e) {
  const n = /* @__PURE__ */ new Map();
  for (const a of e) {
    const i = a.group || a.category || "General", d = n.get(i);
    d ? d.settings.push(a) : n.set(i, { id: i, label: i, settings: [a] });
  }
  return Array.from(n.values());
}
function $(e, n) {
  return (e.uiHint ?? "").toLowerCase().includes(n);
}
function F(e) {
  return (e ?? "").trim().toLowerCase();
}
function $e(e, n) {
  return (e ?? "").trim().toLowerCase() === (n ?? "").trim().toLowerCase();
}
function ee(e) {
  return JSON.stringify(e.map((n) => ({
    id: n.id,
    enabled: n.enabled,
    configuration: n.enabled ? n.configuration : {}
  })).sort((n, a) => n.id.localeCompare(a.id)));
}
function te(e) {
  if (Me(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function Me(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  je as FeatureManagementPage,
  pe as createApplyPayload,
  te as getErrorMessage,
  xe as isDirty,
  ze as register,
  he as registerBuiltInSettingEditors,
  ge as selectSettingEditor
};
