import { forwardRef as ie, createElement as B, useMemo as E, useState as w, useEffect as C } from "react";
var $ = { exports: {} }, I = {};
var X;
function Ce() {
  if (X) return I;
  X = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(s, l, i) {
    var m = null;
    if (i !== void 0 && (m = "" + i), l.key !== void 0 && (m = "" + l.key), "key" in l) {
      i = {};
      for (var o in l)
        o !== "key" && (i[o] = l[o]);
    } else i = l;
    return l = i.ref, {
      $$typeof: e,
      type: s,
      key: m,
      ref: l !== void 0 ? l : null,
      props: i
    };
  }
  return I.Fragment = t, I.jsx = a, I.jsxs = a, I;
}
var ee;
function we() {
  return ee || (ee = 1, $.exports = Ce()), $.exports;
}
var n = we();
const Fe = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), re = (...e) => e.filter((t, a, s) => !!t && t.trim() !== "" && s.indexOf(t) === a).join(" ").trim();
var Ee = {
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
const Ie = ie(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: a = 2,
    absoluteStrokeWidth: s,
    className: l = "",
    children: i,
    iconNode: m,
    ...o
  }, u) => B(
    "svg",
    {
      ref: u,
      ...Ee,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: s ? Number(a) * 24 / Number(t) : a,
      className: re("lucide", l),
      ...o
    },
    [
      ...m.map(([j, p]) => B(j, p)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const A = (e, t) => {
  const a = ie(
    ({ className: s, ...l }, i) => B(Ie, {
      ref: i,
      iconNode: t,
      className: re(`lucide-${Fe(e)}`, s),
      ...l
    })
  );
  return a.displayName = `${e}`, a;
};
const le = A("FlaskConical", [
  [
    "path",
    {
      d: "M14 2v6a2 2 0 0 0 .245.96l5.51 10.08A2 2 0 0 1 18 22H6a2 2 0 0 1-1.755-2.96l5.51-10.08A2 2 0 0 0 10 8V2",
      key: "18mbvz"
    }
  ],
  ["path", { d: "M6.453 15h11.094", key: "3shlmq" }],
  ["path", { d: "M8.5 2h7", key: "csnxdl" }]
]);
const Ae = A("Package", [
  [
    "path",
    {
      d: "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",
      key: "1a0edw"
    }
  ],
  ["path", { d: "M12 22V12", key: "d0xqtd" }],
  ["path", { d: "m3.3 7 7.703 4.734a2 2 0 0 0 1.994 0L20.7 7", key: "yx3hmr" }],
  ["path", { d: "m7.5 4.27 9 5.15", key: "1c824w" }]
]);
const Te = A("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const Re = A("Settings2", [
  ["path", { d: "M20 7h-9", key: "3s1dr2" }],
  ["path", { d: "M14 17H5", key: "gfn3mx" }],
  ["circle", { cx: "17", cy: "17", r: "3", key: "18b49y" }],
  ["circle", { cx: "7", cy: "7", r: "3", key: "dfmy0x" }]
]);
const de = A("ShieldAlert", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ],
  ["path", { d: "M12 8v4", key: "1got3b" }],
  ["path", { d: "M12 16h.01", key: "1drbdi" }]
]), D = "__all", ce = "__uncategorized", Me = "elsa-studio:modules-changed", De = {
  all: ["features"],
  catalog: (e) => [...De.all, e, "catalog"]
};
let V;
function mn(e) {
  V = e, Je(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: Oe
  });
}
function Je(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || O(t, "select"),
    component: en
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => v(t.jsonType) === "boolean",
    component: Ze
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(v(t.jsonType)),
    component: Xe
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(v(t.jsonType)) || O(t, "json") || O(t, "textarea"),
    component: nn
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: Qe
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Ke
  });
}
function $e(e, t) {
  const a = e.settingEditors.list().filter((s) => s.supports(t)).sort((s, l) => (s.order ?? 500) - (l.order ?? 500))[0];
  if (!a)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return a;
}
function ze(e, t) {
  return {
    revision: e,
    features: t.map((a) => ({
      id: a.id,
      enabled: a.enabled,
      configuration: a.enabled ? a.configuration : {}
    }))
  };
}
function He(e, t) {
  return e ? ae(e.features) !== ae(t) : !1;
}
function Le(e) {
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
function ne() {
  return {
    catalog: null,
    draft: [],
    selectedId: "",
    checkedFeatureIds: /* @__PURE__ */ new Set(),
    selectedCategory: D,
    filterText: "",
    loading: !0,
    applying: !1,
    status: null,
    error: null
  };
}
function Oe() {
  const e = E(() => Le(V), []), [t, a] = w("server"), [s, l] = w(() => ({
    studio: ne(),
    server: ne()
  })), i = e.find((r) => r.id === t) ?? e[0], m = s[i.id], { catalog: o, draft: u, selectedId: j, checkedFeatureIds: p, selectedCategory: x, filterText: N, loading: y, applying: F, status: q, error: _ } = m;
  C(() => {
    for (const r of e)
      Y(r.id);
  }, []);
  const T = E(() => dn(u, N), [u, N]), R = E(() => rn(T), [T]), f = E(
    () => ln(T, x),
    [T, x]
  ), S = f.find((r) => r.id === j) ?? f.find((r) => r.enabled) ?? f[0] ?? null, M = He(o, u), fe = u.filter((r) => r.enabled).length, je = R.find((r) => r.id === x)?.label ?? "All categories", G = E(() => new Set(f.map((r) => r.id)), [f]), U = f.filter((r) => p.has(r.id)).length, W = f.length > 0 && U === f.length, be = u.filter((r) => p.has(r.id)), k = !i.writable, J = y || F || k;
  C(() => {
    S && S.id !== j && h(i.id, (r) => ({ ...r, selectedId: S.id }));
  }, [i.id, S, j]), C(() => {
    R.some((r) => r.id === x) || h(i.id, (r) => ({ ...r, selectedCategory: D }));
  }, [i.id, R, x]), C(() => {
    h(i.id, (r) => {
      const d = new Set(r.draft.map((g) => g.id)), c = new Set(Array.from(r.checkedFeatureIds).filter((g) => d.has(g)));
      return c.size === r.checkedFeatureIds.size ? r : { ...r, checkedFeatureIds: c };
    });
  }, [i.id, u]);
  function h(r, d) {
    l((c) => ({
      ...c,
      [r]: d(c[r])
    }));
  }
  async function Y(r = i.id) {
    const d = e.find((c) => c.id === r) ?? i;
    h(d.id, (c) => ({ ...c, loading: !0, error: null }));
    try {
      const c = await d.context.http.getJson("/modularity/features");
      h(d.id, (g) => ({
        ...g,
        catalog: c,
        draft: c.features.map(z),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (c) {
      h(d.id, (g) => ({ ...g, error: se(c), loading: !1 }));
    }
  }
  async function ye() {
    if (!(!o || !M || k)) {
      h(i.id, (r) => ({ ...r, applying: !0, error: null, status: null }));
      try {
        const r = await i.context.http.postJson(
          "/modularity/features/apply",
          ze(o.revision, u)
        );
        h(i.id, (d) => ({
          ...d,
          catalog: r.catalog,
          draft: r.catalog.features.map(z),
          status: `Applied ${r.featureDescriptorCount} descriptor(s); reloaded ${r.reloadedShellCount} shell(s).`,
          applying: !1
        })), i.id === "studio" && window.dispatchEvent(new Event(Me));
      } catch (r) {
        h(i.id, (d) => ({ ...d, error: se(r), applying: !1 }));
      }
    }
  }
  function Z(r, d) {
    k || h(i.id, (c) => ({
      ...c,
      draft: c.draft.map((g) => g.id === r ? d(g) : g)
    }));
  }
  function K(r) {
    Z(r.id, (d) => ({ ...d, enabled: !d.enabled }));
  }
  function ve(r, d) {
    k || h(i.id, (c) => {
      const g = new Set(c.checkedFeatureIds);
      return d ? g.add(r) : g.delete(r), { ...c, checkedFeatureIds: g };
    });
  }
  function Ne() {
    k || h(i.id, (r) => {
      const d = new Set(r.checkedFeatureIds);
      if (W)
        for (const c of G)
          d.delete(c);
      else
        for (const c of G)
          d.add(c);
      return { ...r, checkedFeatureIds: d };
    });
  }
  function Q(r) {
    k || p.size === 0 || h(i.id, (d) => ({
      ...d,
      draft: d.draft.map((c) => d.checkedFeatureIds.has(c.id) ? { ...c, enabled: r } : c)
    }));
  }
  function ke(r, d, c) {
    Z(r.id, (g) => ({
      ...g,
      configuration: {
        ...g.configuration,
        [d.name]: c
      }
    }));
  }
  function Se() {
    o && h(i.id, (r) => ({
      ...r,
      draft: o.features.map(z),
      status: null,
      error: null
    }));
  }
  return /* @__PURE__ */ n.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ n.jsxs("p", { children: [
          i.label,
          ": ",
          fe,
          " enabled of ",
          u.length,
          " available",
          M ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "feature-management-icon-button", "aria-label": "Refresh features", title: "Refresh", onClick: () => Y(), disabled: y || F, children: /* @__PURE__ */ n.jsx(Te, { size: 15 }) }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: Se, disabled: !M || y || F || k, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: ye, disabled: !M || y || F || k, children: F ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(Be, { hosts: e, activeHostId: i.id, onSelect: a }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    q ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: q }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        Pe,
        {
          categories: R,
          selectedCategory: x,
          filterText: N,
          onSelect: (r) => h(i.id, (d) => ({ ...d, selectedCategory: r })),
          onFilterTextChange: (r) => h(i.id, (d) => ({ ...d, filterText: r }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: je }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              f.length,
              " feature",
              f.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            f.filter((r) => r.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          Ve,
          {
            disabled: J || f.length === 0,
            selectedCount: p.size,
            visibleCount: f.length,
            visibleSelectedCount: U,
            selectedEnabledCount: be.filter((r) => r.enabled).length,
            allVisibleChecked: W,
            onToggleVisible: Ne,
            onClearSelection: () => h(i.id, (r) => ({ ...r, checkedFeatureIds: /* @__PURE__ */ new Set() })),
            onEnableSelected: () => Q(!0),
            onDisableSelected: () => Q(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": y, children: [
          y && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !y && u.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !y && u.length > 0 && f.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: N.trim() ? "No features match this filter." : "No features match this category." }) : null,
          f.map((r) => /* @__PURE__ */ n.jsx(
            qe,
            {
              feature: r,
              selected: r.id === S?.id,
              checked: p.has(r.id),
              disabled: J,
              onSelect: () => h(i.id, (d) => ({ ...d, selectedId: r.id })),
              onToggle: () => K(r),
              onCheckedChange: (d) => ve(r.id, d)
            },
            r.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        _e,
        {
          feature: S,
          disabled: J,
          onToggle: K,
          onSettingChange: ke
        }
      )
    ] })
  ] });
}
function Be({
  hosts: e,
  activeHostId: t,
  onSelect: a
}) {
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-host-tabs", role: "tablist", "aria-label": "Feature host", children: e.map((s) => /* @__PURE__ */ n.jsxs(
    "button",
    {
      type: "button",
      role: "tab",
      "aria-selected": s.id === t,
      className: s.id === t ? "active" : "",
      onClick: () => a(s.id),
      children: [
        /* @__PURE__ */ n.jsx("span", { children: s.label }),
        /* @__PURE__ */ n.jsx("small", { children: s.runtime })
      ]
    },
    s.id
  )) });
}
function Ve({
  disabled: e,
  selectedCount: t,
  visibleCount: a,
  visibleSelectedCount: s,
  selectedEnabledCount: l,
  allVisibleChecked: i,
  onToggleVisible: m,
  onClearSelection: o,
  onEnableSelected: u,
  onDisableSelected: j
}) {
  return /* @__PURE__ */ n.jsxs("div", { className: "feature-management-bulk-actions", "aria-label": "Bulk feature actions", children: [
    /* @__PURE__ */ n.jsxs("label", { children: [
      /* @__PURE__ */ n.jsx(
        "input",
        {
          type: "checkbox",
          checked: i,
          disabled: e,
          onChange: m,
          "aria-label": i ? "Clear visible feature selection" : "Select visible features"
        }
      ),
      /* @__PURE__ */ n.jsxs("span", { children: [
        s,
        "/",
        a,
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
          onClick: j,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: o, children: "Clear" })
    ] })
  ] });
}
function Pe({
  categories: e,
  selectedCategory: t,
  filterText: a,
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
          value: a,
          onChange: (i) => s(i.target.value)
        }
      )
    ] }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-category-list", children: e.map((i) => /* @__PURE__ */ n.jsxs(
      "button",
      {
        type: "button",
        className: i.id === t ? "active" : "",
        onClick: () => l(i.id),
        children: [
          /* @__PURE__ */ n.jsx("span", { children: i.label }),
          /* @__PURE__ */ n.jsx("em", { children: i.count })
        ]
      },
      i.id
    )) })
  ] });
}
function qe({
  feature: e,
  selected: t,
  checked: a,
  disabled: s,
  onSelect: l,
  onToggle: i,
  onCheckedChange: m
}) {
  const o = e.displayName || e.id, u = !cn(o, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: a,
        disabled: s,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (j) => m(j.target.checked)
      }
    ),
    /* @__PURE__ */ n.jsx(
      "button",
      {
        type: "button",
        className: e.enabled ? "feature-management-switch enabled" : "feature-management-switch",
        onClick: i,
        disabled: s,
        role: "switch",
        "aria-checked": e.enabled,
        "aria-label": `${e.enabled ? "Disable" : "Enable"} ${e.displayName || e.id}`
      }
    ),
    /* @__PURE__ */ n.jsxs("button", { type: "button", className: "feature-management-feature-button", onClick: l, children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: o }),
        e.experimental ? /* @__PURE__ */ n.jsxs(b, { tone: "experimental", children: [
          /* @__PURE__ */ n.jsx(le, { size: 11 }),
          "Experimental"
        ] }) : null,
        e.advanced ? /* @__PURE__ */ n.jsxs(b, { tone: "advanced", children: [
          /* @__PURE__ */ n.jsx(de, { size: 11 }),
          "Advanced"
        ] }) : null
      ] }),
      u ? /* @__PURE__ */ n.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx(oe, { sourceKind: e.sourceKind }),
        /* @__PURE__ */ n.jsx(ue, { categories: e.categories, max: 2 })
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-count", title: `${e.settings.length} setting${e.settings.length === 1 ? "" : "s"}`, children: [
      /* @__PURE__ */ n.jsx(Re, { size: 13 }),
      e.settings.length
    ] })
  ] });
}
function _e({
  feature: e,
  disabled: t,
  onToggle: a,
  onSettingChange: s
}) {
  const [l, i] = w(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const m = e.displayName || e.id, o = t || !e.enabled, u = l === e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsxs("span", { className: "feature-management-inspector-badges", children: [
          /* @__PURE__ */ n.jsx(oe, { sourceKind: e.sourceKind }),
          /* @__PURE__ */ n.jsx(b, { tone: e.enabled ? "enabled" : "neutral", children: e.enabled ? "Enabled" : "Disabled" }),
          e.experimental ? /* @__PURE__ */ n.jsxs(b, { tone: "experimental", children: [
            /* @__PURE__ */ n.jsx(le, { size: 11 }),
            "Experimental"
          ] }) : null,
          e.advanced ? /* @__PURE__ */ n.jsxs(b, { tone: "advanced", children: [
            /* @__PURE__ */ n.jsx(de, { size: 11 }),
            "Advanced"
          ] }) : null
        ] }),
        /* @__PURE__ */ n.jsx("h3", { children: m })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => a(e), disabled: t, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(Ge, { feature: e, displayName: m }) }),
    /* @__PURE__ */ n.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-section-heading", children: [
        /* @__PURE__ */ n.jsx("h4", { children: "Settings" }),
        e.settings.length > 0 ? /* @__PURE__ */ n.jsx(
          "button",
          {
            type: "button",
            onClick: () => i(e.id),
            disabled: o,
            title: e.enabled ? void 0 : "Enable the feature before editing settings.",
            children: "Edit settings"
          }
        ) : null
      ] }),
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(Ue, { feature: e })
    ] }),
    u ? /* @__PURE__ */ n.jsx(
      We,
      {
        feature: e,
        disabled: o,
        onSettingChange: s,
        onClose: () => i(null)
      }
    ) : null
  ] });
}
function Ge({ feature: e, displayName: t }) {
  const a = me(e.sourceKind), s = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(b, { tone: e.enabled ? "enabled" : "neutral", children: e.enabled ? "Enabled" : "Disabled" }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Source" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(b, { tone: a.tone, title: a.title, children: a.label }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Categories" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(ue, { categories: e.categories }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.packageId ? /* @__PURE__ */ n.jsxs(b, { tone: "violet", children: [
          /* @__PURE__ */ n.jsx(Ae, { size: 11 }),
          s
        ] }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: s }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.settings.length })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Advanced" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.advanced ? /* @__PURE__ */ n.jsx(b, { tone: "advanced", children: "Advanced" }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: "No" }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Experimental" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.experimental ? /* @__PURE__ */ n.jsx(b, { tone: "experimental", children: "Experimental" }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: "No" }) })
      ] }),
      e.description ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Description" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.description })
      ] }) : null,
      e.manifestHash ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest hash" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx("code", { children: e.manifestHash }) })
      ] }) : null,
      e.manifestPath ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Manifest path" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx("code", { children: e.manifestPath }) })
      ] }) : null
    ] })
  ] });
}
function b({ tone: e = "neutral", title: t, children: a }) {
  return /* @__PURE__ */ n.jsx("span", { className: `feature-management-badge tone-${e}`, title: t, children: a });
}
function oe({ sourceKind: e }) {
  const t = me(e);
  return /* @__PURE__ */ n.jsx(b, { tone: t.tone, title: t.title, children: t.label });
}
function ue({ categories: e, max: t }) {
  if (e.length === 0)
    return /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: "Uncategorized" });
  const a = t ? e.slice(0, t) : e, s = t ? e.length - a.length : 0;
  return /* @__PURE__ */ n.jsxs("span", { className: "feature-management-chips", children: [
    a.map((l) => /* @__PURE__ */ n.jsx("span", { className: "feature-management-chip", children: l }, l)),
    s > 0 ? /* @__PURE__ */ n.jsxs("span", { className: "feature-management-chip feature-management-chip-overflow", title: e.slice(a.length).join(", "), children: [
      "+",
      s
    ] }) : null
  ] });
}
function me(e) {
  switch (v(e)) {
    case "shell":
      return { label: "Built-in", tone: "neutral", title: "Compiled into the host shell." };
    case "runtime":
      return { label: "Runtime", tone: "info", title: "Discovered at runtime from loaded assemblies." };
    case "manifest":
      return { label: "Package", tone: "violet", title: "Provided by an installed package manifest." };
    case "manifest-error":
      return { label: "Package error", tone: "danger", title: "The package manifest could not be read." };
    default:
      return { label: e || "Unknown", tone: "neutral", title: e };
  }
}
function Ue({ feature: e }) {
  const t = xe(e.settings);
  return /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings-summary", children: t.map((a) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-summary-group", children: [
    /* @__PURE__ */ n.jsx("h5", { children: a.label }),
    /* @__PURE__ */ n.jsx("div", { className: "feature-management-setting-summary-list", children: a.settings.map((s) => /* @__PURE__ */ n.jsxs("div", { className: "feature-management-setting-summary-row", children: [
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
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: an(s, he(e, s)) })
    ] }, s.name)) })
  ] }, a.id)) });
}
function We({
  feature: e,
  disabled: t,
  onSettingChange: a,
  onClose: s
}) {
  const [l, i] = w(() => H(e)), m = xe(e.settings);
  C(() => {
    i(H(e));
  }, [e.id]);
  function o(p, x) {
    i((N) => ({
      ...N,
      [p.name]: x
    }));
  }
  function u() {
    i(H(e));
  }
  function j() {
    for (const p of e.settings)
      a(e, p, l[p.name]);
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
        /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-body", children: /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: m.map((p) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
          /* @__PURE__ */ n.jsx("h5", { children: p.label }),
          /* @__PURE__ */ n.jsx("div", { children: p.settings.map((x) => {
            const N = $e(V, x).component;
            return /* @__PURE__ */ n.jsx(Ye, { setting: x, children: /* @__PURE__ */ n.jsx(
              N,
              {
                setting: x,
                value: l[x.name],
                disabled: t,
                onChange: (y) => o(x, y)
              }
            ) }, x.name);
          }) })
        ] }, p.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: u, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: j, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function Ye({ setting: e, children: t }) {
  const a = tn(e);
  return /* @__PURE__ */ n.jsxs("label", { className: a ? "feature-management-setting boolean" : "feature-management-setting", children: [
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-main", children: [
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-setting-title", children: [
        /* @__PURE__ */ n.jsx("strong", { children: e.displayName || e.name }),
        e.required ? /* @__PURE__ */ n.jsx("em", { children: "Required" }) : null,
        e.restartRequired ? /* @__PURE__ */ n.jsx("em", { children: "Reload" }) : null,
        e.advanced ? /* @__PURE__ */ n.jsx("em", { children: "Advanced" }) : null
      ] }),
      a ? /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }) : null
    ] }),
    a ? null : /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-control", children: t }),
    e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
    /* @__PURE__ */ n.jsx("code", { children: e.name })
  ] });
}
function Ze({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "checkbox",
      className: "feature-management-setting-switch-input",
      checked: !!e,
      disabled: t,
      onChange: (s) => a(s.target.checked)
    }
  );
}
function Ke({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: P(e), disabled: t, onChange: (s) => a(s.target.value) });
}
function Qe({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: P(e), disabled: t, onChange: (s) => a(s.target.value) });
}
function Xe({ setting: e, value: t, disabled: a, onChange: s }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: P(t),
      disabled: a,
      onChange: (l) => s(v(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function en({ setting: e, value: t, disabled: a, onChange: s }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: a, onChange: (i) => {
    const m = e.options.find((o) => JSON.stringify(o.value) === i.target.value);
    s(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((i) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(i.value), children: i.label }, `${e.name}-${JSON.stringify(i.value)}`))
  ] });
}
function nn({ setting: e, value: t, disabled: a, onChange: s }) {
  const [l, i] = w(te(t, e)), [m, o] = w(!1);
  return C(() => {
    i(te(t, e)), o(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: a, rows: 5, onChange: (u) => {
      const j = u.target.value;
      i(j);
      try {
        s(JSON.parse(j || ge(e))), o(!1);
      } catch {
        o(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function z(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function he(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? sn(t);
}
function H(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, he(e, t)]));
}
function tn(e) {
  return v(e.jsonType) === "boolean";
}
function an(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (v(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const a = typeof t == "object" ? JSON.stringify(t) : String(t);
  return a.length > 96 ? `${a.slice(0, 93)}...` : a;
}
function sn(e) {
  const t = v(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function P(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function te(e, t) {
  return e == null || e === "" ? ge(t) : JSON.stringify(e, null, 2);
}
function ge(e) {
  return v(e.jsonType) === "array" ? "[]" : "{}";
}
function rn(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = pe(a);
    for (const l of s)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: D, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([a], [s]) => L(a).localeCompare(L(s))).map(([a, s]) => ({ id: a, label: L(a), count: s }))
  ];
}
function ln(e, t) {
  return t === D ? e : e.filter((a) => pe(a).includes(t));
}
function dn(e, t) {
  const a = t.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return a.length === 0 ? e : e.filter((s) => {
    const l = [
      s.id,
      s.displayName,
      s.description,
      s.sourceKind,
      s.packageId,
      s.packageVersion,
      ...s.categories,
      ...s.settings.flatMap((i) => [
        i.name,
        i.displayName,
        i.description,
        i.category,
        i.group,
        i.uiHint
      ])
    ].filter((i) => typeof i == "string").join(" ").toLowerCase();
    return a.every((i) => l.includes(i));
  });
}
function pe(e) {
  return e.categories.length > 0 ? e.categories : [ce];
}
function L(e) {
  return e === ce ? "Uncategorized" : e;
}
function xe(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = a.group || a.category || "General", l = t.get(s);
    l ? l.settings.push(a) : t.set(s, { id: s, label: s, settings: [a] });
  }
  return Array.from(t.values());
}
function O(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function v(e) {
  return (e ?? "").trim().toLowerCase();
}
function cn(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function ae(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, a) => t.id.localeCompare(a.id)));
}
function se(e) {
  if (on(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function on(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  Oe as FeatureManagementPage,
  ze as createApplyPayload,
  De as featureKeys,
  dn as filterFeaturesByText,
  se as getErrorMessage,
  He as isDirty,
  mn as register,
  Je as registerBuiltInSettingEditors,
  $e as selectSettingEditor
};
