import { forwardRef as ie, createElement as O, useMemo as C, useState as F, useEffect as w } from "react";
var J = { exports: {} }, I = {};
var Q;
function Ae() {
  if (Q) return I;
  Q = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(s, l, i) {
    var m = null;
    if (i !== void 0 && (m = "" + i), l.key !== void 0 && (m = "" + l.key), "key" in l) {
      i = {};
      for (var c in l)
        c !== "key" && (i[c] = l[c]);
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
var X;
function Me() {
  return X || (X = 1, J.exports = Ae()), J.exports;
}
var n = Me();
const Re = (e) => e.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase(), re = (...e) => e.filter((t, a, s) => !!t && t.trim() !== "" && s.indexOf(t) === a).join(" ").trim();
var De = {
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
const ze = ie(
  ({
    color: e = "currentColor",
    size: t = 24,
    strokeWidth: a = 2,
    absoluteStrokeWidth: s,
    className: l = "",
    children: i,
    iconNode: m,
    ...c
  }, o) => O(
    "svg",
    {
      ref: o,
      ...De,
      width: t,
      height: t,
      stroke: e,
      strokeWidth: s ? Number(a) * 24 / Number(t) : a,
      className: re("lucide", l),
      ...c
    },
    [
      ...m.map(([h, x]) => O(h, x)),
      ...Array.isArray(i) ? i : [i]
    ]
  )
);
const T = (e, t) => {
  const a = ie(
    ({ className: s, ...l }, i) => O(ze, {
      ref: i,
      iconNode: t,
      className: re(`lucide-${Re(e)}`, s),
      ...l
    })
  );
  return a.displayName = `${e}`, a;
};
const le = T("FlaskConical", [
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
const Je = T("Package", [
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
const $e = T("RefreshCcw", [
  ["path", { d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", key: "14sxne" }],
  ["path", { d: "M3 3v5h5", key: "1xhq8a" }],
  ["path", { d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16", key: "1hlbsb" }],
  ["path", { d: "M16 16h5v5", key: "ccwih5" }]
]);
const Le = T("Settings2", [
  ["path", { d: "M20 7h-9", key: "3s1dr2" }],
  ["path", { d: "M14 17H5", key: "gfn3mx" }],
  ["circle", { cx: "17", cy: "17", r: "3", key: "18b49y" }],
  ["circle", { cx: "7", cy: "7", r: "3", key: "dfmy0x" }]
]);
const de = T("ShieldAlert", [
  [
    "path",
    {
      d: "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
      key: "oel41y"
    }
  ],
  ["path", { d: "M12 8v4", key: "1got3b" }],
  ["path", { d: "M12 16h.01", key: "1drbdi" }]
]), D = "__all", ce = "__uncategorized", Be = "elsa-studio:modules-changed", He = {
  all: ["features"],
  catalog: (e) => [...He.all, e, "catalog"]
};
let V;
function Nn(e) {
  V = e, Oe(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: Ye
  });
}
function Oe(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || H(t, "select"),
    component: on
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => b(t.jsonType) === "boolean",
    component: rn
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(b(t.jsonType)),
    component: cn
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(b(t.jsonType)) || H(t, "json") || H(t, "textarea"),
    component: un
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: dn
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: ln
  });
}
function Ve(e, t) {
  const a = e.settingEditors.list().filter((s) => s.supports(t)).sort((s, l) => (s.order ?? 500) - (l.order ?? 500))[0];
  if (!a)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return a;
}
function qe(e, t) {
  return {
    revision: e,
    features: t.map((a) => ({
      id: a.id,
      enabled: a.enabled,
      configuration: a.enabled ? a.configuration : {}
    }))
  };
}
function Pe(e, t) {
  return e ? ae(e.features) !== ae(t) : !1;
}
function _e(e) {
  return new Map(e.map((t) => [b(t.id), t]));
}
function oe(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e)
    for (const s of a.dependencies) {
      if (s.optional)
        continue;
      const l = b(s.id), i = t.get(l);
      i ? i.push(a.id) : t.set(l, [a.id]);
    }
  return t;
}
function Ge(e, t, a, s, l) {
  const i = [e];
  for (l.add(e.id); i.length > 0; ) {
    const m = i.pop(), c = t ? m.dependencies.filter((o) => !o.optional).map((o) => o.id) : s.get(b(m.id)) ?? [];
    for (const o of c) {
      const h = a.get(b(o));
      h && !l.has(h.id) && (l.add(h.id), i.push(h));
    }
  }
}
function ue(e, t, a) {
  const s = _e(e), l = a ? /* @__PURE__ */ new Map() : oe(e), i = /* @__PURE__ */ new Set();
  for (const m of t) {
    const c = s.get(b(m));
    c && Ge(c, a, s, l, i);
  }
  return i;
}
function kn(e, t, a) {
  return ue(e, [t], a);
}
function me(e, t, a) {
  const s = ue(e, t, a);
  return s.size === 0 ? e : e.map((l) => s.has(l.id) && l.enabled !== a ? { ...l, enabled: a } : l);
}
function Ue(e, t, a) {
  return me(e, [t], a);
}
function We(e) {
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
function ee() {
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
function Ye() {
  const e = C(() => We(V), []), [t, a] = F("server"), [s, l] = F(() => ({
    studio: ee(),
    server: ee()
  })), i = e.find((r) => r.id === t) ?? e[0], m = s[i.id], { catalog: c, draft: o, selectedId: h, checkedFeatureIds: x, selectedCategory: f, filterText: k, loading: v, applying: E, status: P, error: _ } = m;
  w(() => {
    for (const r of e)
      Y(r.id);
  }, []);
  const A = C(() => jn(o, k), [o, k]), M = C(() => xn(A), [A]), j = C(
    () => fn(A, f),
    [A, f]
  ), S = j.find((r) => r.id === h) ?? j.find((r) => r.enabled) ?? j[0] ?? null, ye = C(() => oe(o), [o]), ve = S ? ye.get(b(S.id)) ?? [] : [], R = Pe(c, o), Ne = o.filter((r) => r.enabled).length, ke = M.find((r) => r.id === f)?.label ?? "All categories", G = C(() => new Set(j.map((r) => r.id)), [j]), U = j.filter((r) => x.has(r.id)).length, W = j.length > 0 && U === j.length, Se = o.filter((r) => x.has(r.id)), N = !i.writable, z = v || E || N;
  w(() => {
    S && S.id !== h && g(i.id, (r) => ({ ...r, selectedId: S.id }));
  }, [i.id, S, h]), w(() => {
    M.some((r) => r.id === f) || g(i.id, (r) => ({ ...r, selectedCategory: D }));
  }, [i.id, M, f]), w(() => {
    g(i.id, (r) => {
      const d = new Set(r.draft.map((p) => p.id)), u = new Set(Array.from(r.checkedFeatureIds).filter((p) => d.has(p)));
      return u.size === r.checkedFeatureIds.size ? r : { ...r, checkedFeatureIds: u };
    });
  }, [i.id, o]);
  function g(r, d) {
    l((u) => ({
      ...u,
      [r]: d(u[r])
    }));
  }
  async function Y(r = i.id) {
    const d = e.find((u) => u.id === r) ?? i;
    g(d.id, (u) => ({ ...u, loading: !0, error: null }));
    try {
      const u = await d.context.http.getJson("/modularity/features");
      g(d.id, (p) => ({
        ...p,
        catalog: u,
        draft: u.features.map($),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (u) {
      g(d.id, (p) => ({ ...p, error: se(u), loading: !1 }));
    }
  }
  async function Ce() {
    if (!(!c || !R || N)) {
      g(i.id, (r) => ({ ...r, applying: !0, error: null, status: null }));
      try {
        const r = await i.context.http.postJson(
          "/modularity/features/apply",
          qe(c.revision, o)
        );
        g(i.id, (d) => ({
          ...d,
          catalog: r.catalog,
          draft: r.catalog.features.map($),
          status: `Applied ${r.featureDescriptorCount} descriptor(s); reloaded ${r.reloadedShellCount} shell(s).`,
          applying: !1
        })), i.id === "studio" && window.dispatchEvent(new Event(Be));
      } catch (r) {
        g(i.id, (d) => ({ ...d, error: se(r), applying: !1 }));
      }
    }
  }
  function we(r, d) {
    N || g(i.id, (u) => ({
      ...u,
      draft: u.draft.map((p) => p.id === r ? d(p) : p)
    }));
  }
  function Z(r) {
    if (N)
      return;
    const d = !r.enabled;
    g(i.id, (u) => ({
      ...u,
      draft: Ue(u.draft, r.id, d)
    }));
  }
  function Fe(r, d) {
    N || g(i.id, (u) => {
      const p = new Set(u.checkedFeatureIds);
      return d ? p.add(r) : p.delete(r), { ...u, checkedFeatureIds: p };
    });
  }
  function Ee() {
    N || g(i.id, (r) => {
      const d = new Set(r.checkedFeatureIds);
      if (W)
        for (const u of G)
          d.delete(u);
      else
        for (const u of G)
          d.add(u);
      return { ...r, checkedFeatureIds: d };
    });
  }
  function K(r) {
    N || x.size === 0 || g(i.id, (d) => ({
      ...d,
      draft: me(d.draft, d.checkedFeatureIds, r)
    }));
  }
  function Ie(r, d, u) {
    we(r.id, (p) => ({
      ...p,
      configuration: {
        ...p.configuration,
        [d.name]: u
      }
    }));
  }
  function Te() {
    c && g(i.id, (r) => ({
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
          i.label,
          ": ",
          Ne,
          " enabled of ",
          o.length,
          " available",
          R ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "feature-management-icon-button", "aria-label": "Refresh features", title: "Refresh", onClick: () => Y(), disabled: v || E, children: /* @__PURE__ */ n.jsx($e, { size: 15 }) }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: Te, disabled: !R || v || E || N, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: Ce, disabled: !R || v || E || N, children: E ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(Ze, { hosts: e, activeHostId: i.id, onSelect: a }),
    _ ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: _ }) : null,
    P ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: P }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        Qe,
        {
          categories: M,
          selectedCategory: f,
          filterText: k,
          onSelect: (r) => g(i.id, (d) => ({ ...d, selectedCategory: r })),
          onFilterTextChange: (r) => g(i.id, (d) => ({ ...d, filterText: r }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: ke }),
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
          Ke,
          {
            disabled: z || j.length === 0,
            selectedCount: x.size,
            visibleCount: j.length,
            visibleSelectedCount: U,
            selectedEnabledCount: Se.filter((r) => r.enabled).length,
            allVisibleChecked: W,
            onToggleVisible: Ee,
            onClearSelection: () => g(i.id, (r) => ({ ...r, checkedFeatureIds: /* @__PURE__ */ new Set() })),
            onEnableSelected: () => K(!0),
            onDisableSelected: () => K(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": v, children: [
          v && o.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !v && o.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !v && o.length > 0 && j.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: k.trim() ? "No features match this filter." : "No features match this category." }) : null,
          j.map((r) => /* @__PURE__ */ n.jsx(
            Xe,
            {
              feature: r,
              selected: r.id === S?.id,
              checked: x.has(r.id),
              disabled: z,
              onSelect: () => g(i.id, (d) => ({ ...d, selectedId: r.id })),
              onToggle: () => Z(r),
              onCheckedChange: (d) => Fe(r.id, d)
            },
            r.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        en,
        {
          feature: S,
          dependents: ve,
          disabled: z,
          onToggle: Z,
          onSettingChange: Ie
        }
      )
    ] })
  ] });
}
function Ze({
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
function Ke({
  disabled: e,
  selectedCount: t,
  visibleCount: a,
  visibleSelectedCount: s,
  selectedEnabledCount: l,
  allVisibleChecked: i,
  onToggleVisible: m,
  onClearSelection: c,
  onEnableSelected: o,
  onDisableSelected: h
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
          onClick: o,
          children: "Enable"
        }
      ),
      /* @__PURE__ */ n.jsx(
        "button",
        {
          type: "button",
          "aria-label": "Disable selected features",
          disabled: e || t === 0 || l === 0,
          onClick: h,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: c, children: "Clear" })
    ] })
  ] });
}
function Qe({
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
function Xe({
  feature: e,
  selected: t,
  checked: a,
  disabled: s,
  onSelect: l,
  onToggle: i,
  onCheckedChange: m
}) {
  const c = e.displayName || e.id, o = !bn(c, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: a,
        disabled: s,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (h) => m(h.target.checked)
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
        /* @__PURE__ */ n.jsx("strong", { children: c }),
        e.experimental ? /* @__PURE__ */ n.jsxs(y, { tone: "experimental", children: [
          /* @__PURE__ */ n.jsx(le, { size: 11 }),
          "Experimental"
        ] }) : null,
        e.advanced ? /* @__PURE__ */ n.jsxs(y, { tone: "advanced", children: [
          /* @__PURE__ */ n.jsx(de, { size: 11 }),
          "Advanced"
        ] }) : null
      ] }),
      o ? /* @__PURE__ */ n.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx(he, { sourceKind: e.sourceKind }),
        /* @__PURE__ */ n.jsx(ge, { categories: e.categories, max: 2 })
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-count", title: `${e.settings.length} setting${e.settings.length === 1 ? "" : "s"}`, children: [
      /* @__PURE__ */ n.jsx(Le, { size: 13 }),
      e.settings.length
    ] })
  ] });
}
function en({
  feature: e,
  dependents: t,
  disabled: a,
  onToggle: s,
  onSettingChange: l
}) {
  const [i, m] = F(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const c = e.displayName || e.id, o = a || !e.enabled, h = i === e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsxs("span", { className: "feature-management-inspector-badges", children: [
          /* @__PURE__ */ n.jsx(he, { sourceKind: e.sourceKind }),
          /* @__PURE__ */ n.jsx(y, { tone: e.enabled ? "enabled" : "neutral", children: e.enabled ? "Enabled" : "Disabled" }),
          e.experimental ? /* @__PURE__ */ n.jsxs(y, { tone: "experimental", children: [
            /* @__PURE__ */ n.jsx(le, { size: 11 }),
            "Experimental"
          ] }) : null,
          e.advanced ? /* @__PURE__ */ n.jsxs(y, { tone: "advanced", children: [
            /* @__PURE__ */ n.jsx(de, { size: 11 }),
            "Advanced"
          ] }) : null
        ] }),
        /* @__PURE__ */ n.jsx("h3", { children: c })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => s(e), disabled: a, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(nn, { feature: e, displayName: c, dependents: t }) }),
    /* @__PURE__ */ n.jsxs("section", { className: "feature-management-inspector-section feature-management-settings-panel", "aria-label": "Feature settings", children: [
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-section-heading", children: [
        /* @__PURE__ */ n.jsx("h4", { children: "Settings" }),
        e.settings.length > 0 ? /* @__PURE__ */ n.jsx(
          "button",
          {
            type: "button",
            onClick: () => m(e.id),
            disabled: o,
            title: e.enabled ? void 0 : "Enable the feature before editing settings.",
            children: "Edit settings"
          }
        ) : null
      ] }),
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(tn, { feature: e })
    ] }),
    h ? /* @__PURE__ */ n.jsx(
      an,
      {
        feature: e,
        disabled: o,
        onSettingChange: l,
        onClose: () => m(null)
      }
    ) : null
  ] });
}
function nn({ feature: e, displayName: t, dependents: a }) {
  const s = pe(e.sourceKind), l = e.dependencies.map((m) => m.id), i = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(y, { tone: e.enabled ? "enabled" : "neutral", children: e.enabled ? "Enabled" : "Disabled" }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Source" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(y, { tone: s.tone, title: s.title, children: s.label }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Categories" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(ge, { categories: e.categories }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.packageId ? /* @__PURE__ */ n.jsxs(y, { tone: "violet", children: [
          /* @__PURE__ */ n.jsx(Je, { size: 11 }),
          i
        ] }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: i }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.settings.length })
      ] }),
      l.length > 0 ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Depends on" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(ne, { ids: l }) })
      ] }) : null,
      a.length > 0 ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Required by" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(ne, { ids: a }) })
      ] }) : null,
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Advanced" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.advanced ? /* @__PURE__ */ n.jsx(y, { tone: "advanced", children: "Advanced" }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: "No" }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Experimental" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.experimental ? /* @__PURE__ */ n.jsx(y, { tone: "experimental", children: "Experimental" }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: "No" }) })
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
function y({ tone: e = "neutral", title: t, children: a }) {
  return /* @__PURE__ */ n.jsx("span", { className: `feature-management-badge tone-${e}`, title: t, children: a });
}
function he({ sourceKind: e }) {
  const t = pe(e);
  return /* @__PURE__ */ n.jsx(y, { tone: t.tone, title: t.title, children: t.label });
}
function ge({ categories: e, max: t }) {
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
function ne({ ids: e }) {
  return /* @__PURE__ */ n.jsx("span", { className: "feature-management-chips", children: e.map((t) => /* @__PURE__ */ n.jsx("span", { className: "feature-management-chip", children: /* @__PURE__ */ n.jsx("code", { children: t }) }, t)) });
}
function pe(e) {
  switch (b(e)) {
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
function tn({ feature: e }) {
  const t = be(e.settings);
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
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: gn(s, xe(e, s)) })
    ] }, s.name)) })
  ] }, a.id)) });
}
function an({
  feature: e,
  disabled: t,
  onSettingChange: a,
  onClose: s
}) {
  const [l, i] = F(() => L(e)), m = be(e.settings);
  w(() => {
    i(L(e));
  }, [e.id]);
  function c(x, f) {
    i((k) => ({
      ...k,
      [x.name]: f
    }));
  }
  function o() {
    i(L(e));
  }
  function h() {
    for (const x of e.settings)
      a(e, x, l[x.name]);
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
            const k = Ve(V, f).component;
            return /* @__PURE__ */ n.jsx(sn, { setting: f, children: /* @__PURE__ */ n.jsx(
              k,
              {
                setting: f,
                value: l[f.name],
                disabled: t,
                onChange: (v) => c(f, v)
              }
            ) }, f.name);
          }) })
        ] }, x.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: o, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: h, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function sn({ setting: e, children: t }) {
  const a = hn(e);
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
function rn({ value: e, disabled: t, onChange: a }) {
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
function ln({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "text", value: q(e), disabled: t, onChange: (s) => a(s.target.value) });
}
function dn({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: q(e), disabled: t, onChange: (s) => a(s.target.value) });
}
function cn({ setting: e, value: t, disabled: a, onChange: s }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: q(t),
      disabled: a,
      onChange: (l) => s(b(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function on({ setting: e, value: t, disabled: a, onChange: s }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: a, onChange: (i) => {
    const m = e.options.find((c) => JSON.stringify(c.value) === i.target.value);
    s(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((i) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(i.value), children: i.label }, `${e.name}-${JSON.stringify(i.value)}`))
  ] });
}
function un({ setting: e, value: t, disabled: a, onChange: s }) {
  const [l, i] = F(te(t, e)), [m, c] = F(!1);
  return w(() => {
    i(te(t, e)), c(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: a, rows: 5, onChange: (o) => {
      const h = o.target.value;
      i(h);
      try {
        s(JSON.parse(h || fe(e))), c(!1);
      } catch {
        c(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function mn(e) {
  if (!Array.isArray(e))
    return [];
  const t = [];
  for (const a of e)
    typeof a == "string" ? t.push({ id: a, optional: !1 }) : a && typeof a == "object" && typeof a.id == "string" && t.push({ id: a.id, optional: a.optional === !0 });
  return t;
}
function $(e) {
  return {
    ...e,
    dependencies: mn(e.dependencies),
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function xe(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? pn(t);
}
function L(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, xe(e, t)]));
}
function hn(e) {
  return b(e.jsonType) === "boolean";
}
function gn(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (b(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const a = typeof t == "object" ? JSON.stringify(t) : String(t);
  return a.length > 96 ? `${a.slice(0, 93)}...` : a;
}
function pn(e) {
  const t = b(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function q(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function te(e, t) {
  return e == null || e === "" ? fe(t) : JSON.stringify(e, null, 2);
}
function fe(e) {
  return b(e.jsonType) === "array" ? "[]" : "{}";
}
function xn(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = je(a);
    for (const l of s)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: D, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([a], [s]) => B(a).localeCompare(B(s))).map(([a, s]) => ({ id: a, label: B(a), count: s }))
  ];
}
function fn(e, t) {
  return t === D ? e : e.filter((a) => je(a).includes(t));
}
function jn(e, t) {
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
function je(e) {
  return e.categories.length > 0 ? e.categories : [ce];
}
function B(e) {
  return e === ce ? "Uncategorized" : e;
}
function be(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = a.group || a.category || "General", l = t.get(s);
    l ? l.settings.push(a) : t.set(s, { id: s, label: s, settings: [a] });
  }
  return Array.from(t.values());
}
function H(e, t) {
  return (e.uiHint ?? "").toLowerCase().includes(t);
}
function b(e) {
  return (e ?? "").trim().toLowerCase();
}
function bn(e, t) {
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
  if (yn(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function yn(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  Ye as FeatureManagementPage,
  Ue as applyFeatureToggle,
  oe as buildDependentsIndex,
  kn as computeFeatureCascade,
  qe as createApplyPayload,
  He as featureKeys,
  jn as filterFeaturesByText,
  se as getErrorMessage,
  Pe as isDirty,
  Nn as register,
  Oe as registerBuiltInSettingEditors,
  Ve as selectSettingEditor
};
