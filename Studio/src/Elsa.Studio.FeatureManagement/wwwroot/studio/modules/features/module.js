import { useMemo as C, useState as w, useEffect as F } from "react";
import { RefreshCcw as we, FlaskConical as ae, ShieldAlert as se, Settings2 as Ee, Package as Ie } from "lucide-react";
var z = { exports: {} }, I = {};
var Q;
function Te() {
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
function Ae() {
  return X || (X = 1, z.exports = Te()), z.exports;
}
var n = Ae();
const R = "__all", ie = "__uncategorized", De = "elsa-studio:modules-changed", Re = {
  all: ["features"],
  catalog: (e) => [...Re.all, e, "catalog"]
};
let B;
function fn(e) {
  B = e, Je(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: qe
  });
}
function Je(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (t) => (t.options?.length ?? 0) > 0 || H(t, "select"),
    component: tn
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (t) => j(t.jsonType) === "boolean",
    component: Ze
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (t) => ["integer", "number"].includes(j(t.jsonType)),
    component: nn
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (t) => ["object", "array"].includes(j(t.jsonType)) || H(t, "json") || H(t, "textarea"),
    component: an
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (t) => t.secret || t.sensitive,
    component: en
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: Ke
  });
}
function ze(e, t) {
  const a = e.settingEditors.list().filter((s) => s.supports(t)).sort((s, l) => (s.order ?? 500) - (l.order ?? 500))[0];
  if (!a)
    throw new Error(`No setting editor is registered for '${t.name}'.`);
  return a;
}
function Me(e, t) {
  return {
    revision: e,
    features: t.map((a) => ({
      id: a.id,
      enabled: a.enabled,
      configuration: a.enabled ? a.configuration : {}
    }))
  };
}
function $e(e, t) {
  return e ? ne(e.features) !== ne(t) : !1;
}
function Oe(e) {
  return new Map(e.map((t) => [j(t.id), t]));
}
function re(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e)
    for (const s of a.dependencies) {
      if (s.optional)
        continue;
      const l = j(s.id), i = t.get(l);
      i ? i.push(a.id) : t.set(l, [a.id]);
    }
  return t;
}
function He(e, t, a, s, l) {
  const i = [e];
  for (l.add(e.id); i.length > 0; ) {
    const m = i.pop(), c = t ? m.dependencies.filter((o) => !o.optional).map((o) => o.id) : s.get(j(m.id)) ?? [];
    for (const o of c) {
      const g = a.get(j(o));
      g && !l.has(g.id) && (l.add(g.id), i.push(g));
    }
  }
}
function le(e, t, a) {
  const s = Oe(e), l = a ? /* @__PURE__ */ new Map() : re(e), i = /* @__PURE__ */ new Set();
  for (const m of t) {
    const c = s.get(j(m));
    c && He(c, a, s, l, i);
  }
  return i;
}
function xn(e, t, a) {
  return le(e, [t], a);
}
function de(e, t, a) {
  const s = le(e, t, a);
  return s.size === 0 ? e : e.map((l) => s.has(l.id) && l.enabled !== a ? { ...l, enabled: a } : l);
}
function Be(e, t, a) {
  return de(e, [t], a);
}
function Pe(e) {
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
    selectedCategory: R,
    filterText: "",
    loading: !0,
    applying: !1,
    status: null,
    error: null
  };
}
function qe() {
  const e = C(() => Pe(B), []), [t, a] = w("server"), [s, l] = w(() => ({
    studio: Z(),
    server: Z()
  })), i = e.find((r) => r.id === t) ?? e[0], m = s[i.id], { catalog: c, draft: o, selectedId: g, checkedFeatureIds: b, selectedCategory: f, filterText: S, loading: v, applying: E, status: q, error: V } = m;
  F(() => {
    for (const r of e)
      U(r.id);
  }, []);
  const T = C(() => un(o, S), [o, S]), A = C(() => cn(T), [T]), x = C(
    () => on(T, f),
    [T, f]
  ), k = x.find((r) => r.id === g) ?? x.find((r) => r.enabled) ?? x[0] ?? null, fe = C(() => re(o), [o]), xe = k ? fe.get(j(k.id)) ?? [] : [], D = $e(c, o), je = o.filter((r) => r.enabled).length, be = A.find((r) => r.id === f)?.label ?? "All categories", L = C(() => new Set(x.map((r) => r.id)), [x]), _ = x.filter((r) => b.has(r.id)).length, G = x.length > 0 && _ === x.length, ye = o.filter((r) => b.has(r.id)), N = !i.writable, J = v || E || N;
  F(() => {
    k && k.id !== g && h(i.id, (r) => ({ ...r, selectedId: k.id }));
  }, [i.id, k, g]), F(() => {
    A.some((r) => r.id === f) || h(i.id, (r) => ({ ...r, selectedCategory: R }));
  }, [i.id, A, f]), F(() => {
    h(i.id, (r) => {
      const d = new Set(r.draft.map((p) => p.id)), u = new Set(Array.from(r.checkedFeatureIds).filter((p) => d.has(p)));
      return u.size === r.checkedFeatureIds.size ? r : { ...r, checkedFeatureIds: u };
    });
  }, [i.id, o]);
  function h(r, d) {
    l((u) => ({
      ...u,
      [r]: d(u[r])
    }));
  }
  async function U(r = i.id) {
    const d = e.find((u) => u.id === r) ?? i;
    h(d.id, (u) => ({ ...u, loading: !0, error: null }));
    try {
      const u = await d.context.http.getJson("/modularity/features");
      h(d.id, (p) => ({
        ...p,
        catalog: u,
        draft: u.features.map(M),
        checkedFeatureIds: /* @__PURE__ */ new Set(),
        status: null,
        loading: !1
      }));
    } catch (u) {
      h(d.id, (p) => ({ ...p, error: te(u), loading: !1 }));
    }
  }
  async function ve() {
    if (!(!c || !D || N)) {
      h(i.id, (r) => ({ ...r, applying: !0, error: null, status: null }));
      try {
        const r = await i.context.http.postJson(
          "/modularity/features/apply",
          Me(c.revision, o)
        );
        h(i.id, (d) => ({
          ...d,
          catalog: r.catalog,
          draft: r.catalog.features.map(M),
          status: `Applied ${r.featureDescriptorCount} descriptor(s); reloaded ${r.reloadedShellCount} shell(s).`,
          applying: !1
        })), i.id === "studio" && window.dispatchEvent(new Event(De));
      } catch (r) {
        h(i.id, (d) => ({ ...d, error: te(r), applying: !1 }));
      }
    }
  }
  function Ne(r, d) {
    N || h(i.id, (u) => ({
      ...u,
      draft: u.draft.map((p) => p.id === r ? d(p) : p)
    }));
  }
  function Y(r) {
    if (N)
      return;
    const d = !r.enabled;
    h(i.id, (u) => ({
      ...u,
      draft: Be(u.draft, r.id, d)
    }));
  }
  function Se(r, d) {
    N || h(i.id, (u) => {
      const p = new Set(u.checkedFeatureIds);
      return d ? p.add(r) : p.delete(r), { ...u, checkedFeatureIds: p };
    });
  }
  function ke() {
    N || h(i.id, (r) => {
      const d = new Set(r.checkedFeatureIds);
      if (G)
        for (const u of L)
          d.delete(u);
      else
        for (const u of L)
          d.add(u);
      return { ...r, checkedFeatureIds: d };
    });
  }
  function W(r) {
    N || b.size === 0 || h(i.id, (d) => ({
      ...d,
      draft: de(d.draft, d.checkedFeatureIds, r)
    }));
  }
  function Ce(r, d, u) {
    Ne(r.id, (p) => ({
      ...p,
      configuration: {
        ...p.configuration,
        [d.name]: u
      }
    }));
  }
  function Fe() {
    c && h(i.id, (r) => ({
      ...r,
      draft: c.features.map(M),
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
          je,
          " enabled of ",
          o.length,
          " available",
          D ? " - Unsaved changes" : ""
        ] })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "feature-management-icon-button", "aria-label": "Refresh features", title: "Refresh", onClick: () => U(), disabled: v || E, children: /* @__PURE__ */ n.jsx(we, { size: 15 }) }),
        /* @__PURE__ */ n.jsx("button", { type: "button", onClick: Fe, disabled: !D || v || E || N, children: "Reset" }),
        /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: ve, disabled: !D || v || E || N, children: E ? "Applying" : "Apply" })
      ] })
    ] }),
    /* @__PURE__ */ n.jsx(Ve, { hosts: e, activeHostId: i.id, onSelect: a }),
    V ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-error", children: V }) : null,
    q ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-status", children: q }) : null,
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ n.jsx(
        _e,
        {
          categories: A,
          selectedCategory: f,
          filterText: S,
          onSelect: (r) => h(i.id, (d) => ({ ...d, selectedCategory: r })),
          onFilterTextChange: (r) => h(i.id, (d) => ({ ...d, filterText: r }))
        }
      ),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-column", children: [
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list-heading", children: [
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("strong", { children: be }),
            /* @__PURE__ */ n.jsxs("span", { children: [
              x.length,
              " feature",
              x.length === 1 ? "" : "s"
            ] })
          ] }),
          /* @__PURE__ */ n.jsxs("span", { children: [
            x.filter((r) => r.enabled).length,
            " enabled"
          ] })
        ] }),
        /* @__PURE__ */ n.jsx(
          Le,
          {
            disabled: J || x.length === 0,
            selectedCount: b.size,
            visibleCount: x.length,
            visibleSelectedCount: _,
            selectedEnabledCount: ye.filter((r) => r.enabled).length,
            allVisibleChecked: G,
            onToggleVisible: ke,
            onClearSelection: () => h(i.id, (r) => ({ ...r, checkedFeatureIds: /* @__PURE__ */ new Set() })),
            onEnableSelected: () => W(!0),
            onDisableSelected: () => W(!1)
          }
        ),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-list", "aria-busy": v, children: [
          v && o.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
          !v && o.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
          !v && o.length > 0 && x.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: S.trim() ? "No features match this filter." : "No features match this category." }) : null,
          x.map((r) => /* @__PURE__ */ n.jsx(
            Ge,
            {
              feature: r,
              selected: r.id === k?.id,
              checked: b.has(r.id),
              disabled: J,
              onSelect: () => h(i.id, (d) => ({ ...d, selectedId: r.id })),
              onToggle: () => Y(r),
              onCheckedChange: (d) => Se(r.id, d)
            },
            r.id
          ))
        ] })
      ] }),
      /* @__PURE__ */ n.jsx(
        Ue,
        {
          feature: k,
          dependents: xe,
          disabled: J,
          onToggle: Y,
          onSettingChange: Ce
        }
      )
    ] })
  ] });
}
function Ve({
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
function Le({
  disabled: e,
  selectedCount: t,
  visibleCount: a,
  visibleSelectedCount: s,
  selectedEnabledCount: l,
  allVisibleChecked: i,
  onToggleVisible: m,
  onClearSelection: c,
  onEnableSelected: o,
  onDisableSelected: g
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
          onClick: g,
          children: "Disable"
        }
      ),
      /* @__PURE__ */ n.jsx("button", { type: "button", "aria-label": "Clear selected features", disabled: e || t === 0, onClick: c, children: "Clear" })
    ] })
  ] });
}
function _e({
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
function Ge({
  feature: e,
  selected: t,
  checked: a,
  disabled: s,
  onSelect: l,
  onToggle: i,
  onCheckedChange: m
}) {
  const c = e.displayName || e.id, o = !mn(c, e.id);
  return /* @__PURE__ */ n.jsxs("article", { className: t ? "feature-management-card selected" : "feature-management-card", children: [
    /* @__PURE__ */ n.jsx(
      "input",
      {
        type: "checkbox",
        className: "feature-management-row-checkbox",
        checked: a,
        disabled: s,
        "aria-label": `Select ${e.displayName || e.id}`,
        onChange: (g) => m(g.target.checked)
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
          /* @__PURE__ */ n.jsx(ae, { size: 11 }),
          "Experimental"
        ] }) : null,
        e.advanced ? /* @__PURE__ */ n.jsxs(y, { tone: "advanced", children: [
          /* @__PURE__ */ n.jsx(se, { size: 11 }),
          "Advanced"
        ] }) : null
      ] }),
      o ? /* @__PURE__ */ n.jsx("code", { children: e.id }) : null,
      e.description ? /* @__PURE__ */ n.jsx("small", { children: e.description }) : null,
      /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-meta", children: [
        /* @__PURE__ */ n.jsx(ce, { sourceKind: e.sourceKind }),
        /* @__PURE__ */ n.jsx(oe, { categories: e.categories, max: 2 })
      ] })
    ] }),
    /* @__PURE__ */ n.jsxs("span", { className: "feature-management-card-count", title: `${e.settings.length} setting${e.settings.length === 1 ? "" : "s"}`, children: [
      /* @__PURE__ */ n.jsx(Ee, { size: 13 }),
      e.settings.length
    ] })
  ] });
}
function Ue({
  feature: e,
  dependents: t,
  disabled: a,
  onToggle: s,
  onSettingChange: l
}) {
  const [i, m] = w(null);
  if (!e)
    return /* @__PURE__ */ n.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
  const c = e.displayName || e.id, o = a || !e.enabled, g = i === e.id;
  return /* @__PURE__ */ n.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ n.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsxs("span", { className: "feature-management-inspector-badges", children: [
          /* @__PURE__ */ n.jsx(ce, { sourceKind: e.sourceKind }),
          /* @__PURE__ */ n.jsx(y, { tone: e.enabled ? "enabled" : "neutral", children: e.enabled ? "Enabled" : "Disabled" }),
          e.experimental ? /* @__PURE__ */ n.jsxs(y, { tone: "experimental", children: [
            /* @__PURE__ */ n.jsx(ae, { size: 11 }),
            "Experimental"
          ] }) : null,
          e.advanced ? /* @__PURE__ */ n.jsxs(y, { tone: "advanced", children: [
            /* @__PURE__ */ n.jsx(se, { size: 11 }),
            "Advanced"
          ] }) : null
        ] }),
        /* @__PURE__ */ n.jsx("h3", { children: c })
      ] }),
      /* @__PURE__ */ n.jsx("button", { type: "button", onClick: () => s(e), disabled: a, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ n.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ n.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    /* @__PURE__ */ n.jsx("section", { className: "feature-management-inspector-section", "aria-label": "Feature metadata", children: /* @__PURE__ */ n.jsx(Ye, { feature: e, displayName: c, dependents: t }) }),
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
      e.settings.length === 0 ? /* @__PURE__ */ n.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ n.jsx(We, { feature: e })
    ] }),
    g ? /* @__PURE__ */ n.jsx(
      Qe,
      {
        feature: e,
        disabled: o,
        onSettingChange: l,
        onClose: () => m(null)
      }
    ) : null
  ] });
}
function Ye({ feature: e, displayName: t, dependents: a }) {
  const s = ue(e.sourceKind), l = e.dependencies.map((m) => m.id), i = e.packageId ? [e.packageId, e.packageVersion].filter(Boolean).join(" ") : "Not package-backed";
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
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(oe, { categories: e.categories }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Package" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.packageId ? /* @__PURE__ */ n.jsxs(y, { tone: "violet", children: [
          /* @__PURE__ */ n.jsx(Ie, { size: 11 }),
          i
        ] }) : /* @__PURE__ */ n.jsx("span", { className: "feature-management-muted", children: i }) })
      ] }),
      /* @__PURE__ */ n.jsxs("div", { children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Settings" }),
        /* @__PURE__ */ n.jsx("dd", { children: e.settings.length })
      ] }),
      l.length > 0 ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Depends on" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(K, { ids: l }) })
      ] }) : null,
      a.length > 0 ? /* @__PURE__ */ n.jsxs("div", { className: "feature-management-detail-wide", children: [
        /* @__PURE__ */ n.jsx("dt", { children: "Required by" }),
        /* @__PURE__ */ n.jsx("dd", { children: /* @__PURE__ */ n.jsx(K, { ids: a }) })
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
function ce({ sourceKind: e }) {
  const t = ue(e);
  return /* @__PURE__ */ n.jsx(y, { tone: t.tone, title: t.title, children: t.label });
}
function oe({ categories: e, max: t }) {
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
function K({ ids: e }) {
  return /* @__PURE__ */ n.jsx("span", { className: "feature-management-chips", children: e.map((t) => /* @__PURE__ */ n.jsx("span", { className: "feature-management-chip", children: /* @__PURE__ */ n.jsx("code", { children: t }) }, t)) });
}
function ue(e) {
  switch (j(e)) {
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
function We({ feature: e }) {
  const t = pe(e.settings);
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
      /* @__PURE__ */ n.jsx("span", { className: "feature-management-setting-summary-value", children: ln(s, me(e, s)) })
    ] }, s.name)) })
  ] }, a.id)) });
}
function Qe({
  feature: e,
  disabled: t,
  onSettingChange: a,
  onClose: s
}) {
  const [l, i] = w(() => $(e)), m = pe(e.settings);
  F(() => {
    i($(e));
  }, [e.id]);
  function c(b, f) {
    i((S) => ({
      ...S,
      [b.name]: f
    }));
  }
  function o() {
    i($(e));
  }
  function g() {
    for (const b of e.settings)
      a(e, b, l[b.name]);
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
        /* @__PURE__ */ n.jsx("div", { className: "feature-management-dialog-body", children: /* @__PURE__ */ n.jsx("div", { className: "feature-management-settings", children: m.map((b) => /* @__PURE__ */ n.jsxs("section", { className: "feature-management-setting-group", children: [
          /* @__PURE__ */ n.jsx("h5", { children: b.label }),
          /* @__PURE__ */ n.jsx("div", { children: b.settings.map((f) => {
            const S = ze(B, f).component;
            return /* @__PURE__ */ n.jsx(Xe, { setting: f, children: /* @__PURE__ */ n.jsx(
              S,
              {
                setting: f,
                value: l[f.name],
                disabled: t,
                onChange: (v) => c(f, v)
              }
            ) }, f.name);
          }) })
        ] }, b.id)) }) }),
        /* @__PURE__ */ n.jsxs("div", { className: "feature-management-dialog-actions", children: [
          /* @__PURE__ */ n.jsx("span", { children: "Save draft keeps changes local until the page Apply action runs." }),
          /* @__PURE__ */ n.jsxs("div", { children: [
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", onClick: o, disabled: t, children: "Reset changes" }),
            /* @__PURE__ */ n.jsx("button", { type: "button", className: "primary", onClick: g, disabled: t, children: "Save draft" })
          ] })
        ] })
      ]
    }
  ) });
}
function Xe({ setting: e, children: t }) {
  const a = rn(e);
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
function en({ value: e, disabled: t, onChange: a }) {
  return /* @__PURE__ */ n.jsx("input", { type: "password", value: P(e), disabled: t, onChange: (s) => a(s.target.value) });
}
function nn({ setting: e, value: t, disabled: a, onChange: s }) {
  return /* @__PURE__ */ n.jsx(
    "input",
    {
      type: "number",
      value: P(t),
      disabled: a,
      onChange: (l) => s(j(e.jsonType) === "integer" ? Number.parseInt(l.target.value || "0", 10) : Number.parseFloat(l.target.value || "0"))
    }
  );
}
function tn({ setting: e, value: t, disabled: a, onChange: s }) {
  const l = JSON.stringify(t ?? "");
  return /* @__PURE__ */ n.jsxs("select", { value: l, disabled: a, onChange: (i) => {
    const m = e.options.find((c) => JSON.stringify(c.value) === i.target.value);
    s(m?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((i) => /* @__PURE__ */ n.jsx("option", { value: JSON.stringify(i.value), children: i.label }, `${e.name}-${JSON.stringify(i.value)}`))
  ] });
}
function an({ setting: e, value: t, disabled: a, onChange: s }) {
  const [l, i] = w(ee(t, e)), [m, c] = w(!1);
  return F(() => {
    i(ee(t, e)), c(!1);
  }, [e.name, t]), /* @__PURE__ */ n.jsxs(n.Fragment, { children: [
    /* @__PURE__ */ n.jsx("textarea", { value: l, disabled: a, rows: 5, onChange: (o) => {
      const g = o.target.value;
      i(g);
      try {
        s(JSON.parse(g || ge(e))), c(!1);
      } catch {
        c(!0);
      }
    } }),
    m ? /* @__PURE__ */ n.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function sn(e) {
  if (!Array.isArray(e))
    return [];
  const t = [];
  for (const a of e)
    typeof a == "string" ? t.push({ id: a, optional: !1 }) : a && typeof a == "object" && typeof a.id == "string" && t.push({ id: a.id, optional: a.optional === !0 });
  return t;
}
function M(e) {
  return {
    ...e,
    dependencies: sn(e.dependencies),
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function me(e, t) {
  return Object.prototype.hasOwnProperty.call(e.configuration, t.name) ? e.configuration[t.name] : t.defaultValue ?? dn(t);
}
function $(e) {
  return Object.fromEntries(e.settings.map((t) => [t.name, me(e, t)]));
}
function rn(e) {
  return j(e.jsonType) === "boolean";
}
function ln(e, t) {
  if (e.secret || e.sensitive)
    return t == null || t === "" ? "Not set" : "********";
  if (j(e.jsonType) === "boolean")
    return t ? "Enabled" : "Disabled";
  if (t == null || t === "")
    return "Not set";
  const a = typeof t == "object" ? JSON.stringify(t) : String(t);
  return a.length > 96 ? `${a.slice(0, 93)}...` : a;
}
function dn(e) {
  const t = j(e.jsonType);
  return t === "boolean" ? !1 : t === "integer" || t === "number" ? 0 : t === "array" ? [] : t === "object" ? {} : "";
}
function P(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function ee(e, t) {
  return e == null || e === "" ? ge(t) : JSON.stringify(e, null, 2);
}
function ge(e) {
  return j(e.jsonType) === "array" ? "[]" : "{}";
}
function cn(e) {
  const t = /* @__PURE__ */ new Map();
  for (const a of e) {
    const s = he(a);
    for (const l of s)
      t.set(l, (t.get(l) ?? 0) + 1);
  }
  return [
    { id: R, label: "All categories", count: e.length },
    ...Array.from(t.entries()).sort(([a], [s]) => O(a).localeCompare(O(s))).map(([a, s]) => ({ id: a, label: O(a), count: s }))
  ];
}
function on(e, t) {
  return t === R ? e : e.filter((a) => he(a).includes(t));
}
function un(e, t) {
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
function he(e) {
  return e.categories.length > 0 ? e.categories : [ie];
}
function O(e) {
  return e === ie ? "Uncategorized" : e;
}
function pe(e) {
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
function j(e) {
  return (e ?? "").trim().toLowerCase();
}
function mn(e, t) {
  return (e ?? "").trim().toLowerCase() === (t ?? "").trim().toLowerCase();
}
function ne(e) {
  return JSON.stringify(e.map((t) => ({
    id: t.id,
    enabled: t.enabled,
    configuration: t.enabled ? t.configuration : {}
  })).sort((t, a) => t.id.localeCompare(a.id)));
}
function te(e) {
  if (gn(e)) {
    if (e.status === 409)
      return `The feature catalog changed before your changes were applied. Refresh and review the latest feature state. ${e.message}`;
    if (e.status === 400)
      return `The feature configuration was rejected. ${e.message}`;
  }
  return e instanceof Error ? e.message : String(e);
}
function gn(e) {
  return e instanceof Error && typeof e.status == "number";
}
export {
  qe as FeatureManagementPage,
  Be as applyFeatureToggle,
  re as buildDependentsIndex,
  xn as computeFeatureCascade,
  Me as createApplyPayload,
  Re as featureKeys,
  un as filterFeaturesByText,
  te as getErrorMessage,
  $e as isDirty,
  fn as register,
  Je as registerBuiltInSettingEditors,
  ze as selectSettingEditor
};
