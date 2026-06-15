import { useState as u, useEffect as S, useMemo as B } from "react";
var v = { exports: {} }, x = {};
var O;
function z() {
  if (O) return x;
  O = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), n = /* @__PURE__ */ Symbol.for("react.fragment");
  function r(o, i, s) {
    var l = null;
    if (s !== void 0 && (l = "" + s), i.key !== void 0 && (l = "" + i.key), "key" in i) {
      s = {};
      for (var d in i)
        d !== "key" && (s[d] = i[d]);
    } else s = i;
    return i = s.ref, {
      $$typeof: e,
      type: o,
      key: l,
      ref: i !== void 0 ? i : null,
      props: s
    };
  }
  return x.Fragment = n, x.jsx = r, x.jsxs = r, x;
}
var A;
function H() {
  return A || (A = 1, v.exports = z()), v.exports;
}
var t = H();
let y;
function ie(e) {
  y = e, Y(e), e.navigation.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    order: 130
  }), e.routes.add({
    id: "feature-management",
    label: "Features",
    path: "/features",
    component: U
  });
}
function Y(e) {
  e.settingEditors.add({
    id: "select",
    order: 10,
    supports: (n) => (n.options?.length ?? 0) > 0 || N(n, "select"),
    component: ne
  }), e.settingEditors.add({
    id: "boolean",
    order: 20,
    supports: (n) => h(n.jsonType) === "boolean",
    component: Z
  }), e.settingEditors.add({
    id: "number",
    order: 30,
    supports: (n) => ["integer", "number"].includes(h(n.jsonType)),
    component: ee
  }), e.settingEditors.add({
    id: "json",
    order: 40,
    supports: (n) => ["object", "array"].includes(h(n.jsonType)) || N(n, "json") || N(n, "textarea"),
    component: te
  }), e.settingEditors.add({
    id: "secret",
    order: 50,
    supports: (n) => n.secret || n.sensitive,
    component: K
  }), e.settingEditors.add({
    id: "text",
    order: 100,
    supports: () => !0,
    component: $
  });
}
function oe(e, n) {
  const r = e.settingEditors.list().filter((o) => o.supports(n)).sort((o, i) => (o.order ?? 500) - (i.order ?? 500))[0];
  if (!r)
    throw new Error(`No setting editor is registered for '${n.name}'.`);
  return r;
}
function G(e, n) {
  return {
    revision: e,
    features: n.map((r) => ({
      id: r.id,
      enabled: r.enabled,
      configuration: r.enabled ? r.configuration : {}
    }))
  };
}
function Q(e, n) {
  return e ? P(e.features) !== P(n) : !1;
}
function U() {
  const [e, n] = u(null), [r, o] = u([]), [i, s] = u(""), [l, d] = u(!0), [c, p] = u(!1), [C, J] = u(null), [T, j] = u(null);
  S(() => {
    R();
  }, []);
  const g = r.find((a) => a.id === i) ?? r.find((a) => a.enabled) ?? r[0] ?? null, k = Q(e, r), L = r.filter((a) => a.enabled).length;
  S(() => {
    g && g.id !== i && s(g.id);
  }, [g, i]);
  async function R() {
    d(!0), j(null);
    try {
      const a = await y.backend.http.getJson("/modularity/features");
      n(a), o(a.features.map(I)), J(null);
    } catch (a) {
      j(_(a));
    } finally {
      d(!1);
    }
  }
  async function M() {
    if (!(!e || !k)) {
      p(!0), j(null);
      try {
        const a = await y.backend.http.getJson("/modularity/features/apply", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(G(e.revision, r))
        });
        n(a.catalog), o(a.catalog.features.map(I)), J(`Applied ${a.featureDescriptorCount} descriptor(s); reloaded ${a.reloadedShellCount} shell(s).`);
      } catch (a) {
        j(_(a));
      } finally {
        p(!1);
      }
    }
  }
  function w(a, m) {
    o((b) => b.map((f) => f.id === a ? m(f) : f));
  }
  function F(a) {
    w(a.id, (m) => ({ ...m, enabled: !m.enabled }));
  }
  function V(a, m, b) {
    w(a.id, (f) => ({
      ...f,
      configuration: {
        ...f.configuration,
        [m.name]: b
      }
    }));
  }
  return /* @__PURE__ */ t.jsxs("section", { className: "feature-management-page", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "section-header feature-management-header", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("h2", { children: "Features" }),
        /* @__PURE__ */ t.jsxs("p", { children: [
          L,
          " enabled of ",
          r.length,
          " available"
        ] })
      ] }),
      /* @__PURE__ */ t.jsxs("div", { className: "feature-management-actions", children: [
        /* @__PURE__ */ t.jsx("button", { type: "button", onClick: R, disabled: l || c, children: "Refresh" }),
        /* @__PURE__ */ t.jsx("button", { type: "button", className: "primary", onClick: M, disabled: !k || l || c, children: c ? "Applying" : "Apply" })
      ] })
    ] }),
    T ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-error", children: T }) : null,
    C ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-status", children: C }) : null,
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-layout", children: [
      /* @__PURE__ */ t.jsxs("div", { className: "feature-management-list", "aria-busy": l, children: [
        l && r.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "Loading features..." }) : null,
        !l && r.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "No features are available." }) : null,
        r.map((a) => /* @__PURE__ */ t.jsxs(
          "button",
          {
            type: "button",
            className: a.id === g?.id ? "feature-management-row selected" : "feature-management-row",
            onClick: () => s(a.id),
            children: [
              /* @__PURE__ */ t.jsx("span", { className: a.enabled ? "feature-management-switch enabled" : "feature-management-switch", onClick: (m) => {
                m.stopPropagation(), F(a);
              }, role: "switch", "aria-checked": a.enabled, tabIndex: 0 }),
              /* @__PURE__ */ t.jsxs("span", { children: [
                /* @__PURE__ */ t.jsx("strong", { children: a.displayName || a.id }),
                /* @__PURE__ */ t.jsx("code", { children: a.id })
              ] }),
              /* @__PURE__ */ t.jsx("em", { children: a.settings.length })
            ]
          },
          a.id
        ))
      ] }),
      /* @__PURE__ */ t.jsx(
        W,
        {
          feature: g,
          disabled: c || l,
          onToggle: F,
          onSettingChange: V
        }
      )
    ] })
  ] });
}
function W({
  feature: e,
  disabled: n,
  onToggle: r,
  onSettingChange: o
}) {
  const i = B(() => y.settingEditors.list(), []);
  return e ? /* @__PURE__ */ t.jsxs("aside", { className: "feature-management-inspector", children: [
    /* @__PURE__ */ t.jsxs("div", { className: "feature-management-inspector-heading", children: [
      /* @__PURE__ */ t.jsxs("div", { children: [
        /* @__PURE__ */ t.jsx("span", { children: e.sourceKind }),
        /* @__PURE__ */ t.jsx("h3", { children: e.displayName || e.id }),
        /* @__PURE__ */ t.jsx("code", { children: e.id })
      ] }),
      /* @__PURE__ */ t.jsx("button", { type: "button", onClick: () => r(e), disabled: n, children: e.enabled ? "Disable" : "Enable" })
    ] }),
    e.description ? /* @__PURE__ */ t.jsx("p", { children: e.description }) : null,
    e.readError ? /* @__PURE__ */ t.jsx("div", { className: "feature-management-warning", children: e.readError }) : null,
    e.packageId ? /* @__PURE__ */ t.jsxs("p", { className: "feature-management-muted", children: [
      e.packageId,
      " ",
      e.packageVersion
    ] }) : null,
    e.settings.length === 0 ? /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "No configurable settings." }) : /* @__PURE__ */ t.jsx("div", { className: "feature-management-settings", children: e.settings.map((s) => {
      const d = i.filter((c) => c.supports(s)).sort((c, p) => (c.order ?? 500) - (p.order ?? 500))[0]?.component ?? $;
      return /* @__PURE__ */ t.jsx(X, { setting: s, children: /* @__PURE__ */ t.jsx(
        d,
        {
          setting: s,
          value: re(e, s),
          disabled: n || !e.enabled,
          onChange: (c) => o(e, s, c)
        }
      ) }, s.name);
    }) })
  ] }) : /* @__PURE__ */ t.jsx("aside", { className: "feature-management-inspector", children: /* @__PURE__ */ t.jsx("p", { className: "feature-management-muted", children: "Select a feature." }) });
}
function X({ setting: e, children: n }) {
  return /* @__PURE__ */ t.jsxs("label", { className: "feature-management-setting", children: [
    /* @__PURE__ */ t.jsxs("span", { children: [
      /* @__PURE__ */ t.jsx("strong", { children: e.displayName || e.name }),
      e.required ? /* @__PURE__ */ t.jsx("em", { children: "Required" }) : null,
      e.restartRequired ? /* @__PURE__ */ t.jsx("em", { children: "Reload" }) : null,
      e.advanced ? /* @__PURE__ */ t.jsx("em", { children: "Advanced" }) : null
    ] }),
    e.description ? /* @__PURE__ */ t.jsx("small", { children: e.description }) : null,
    n,
    /* @__PURE__ */ t.jsx("code", { children: e.name })
  ] });
}
function Z({ value: e, disabled: n, onChange: r }) {
  return /* @__PURE__ */ t.jsx("input", { type: "checkbox", checked: !!e, disabled: n, onChange: (o) => r(o.target.checked) });
}
function $({ value: e, disabled: n, onChange: r }) {
  return /* @__PURE__ */ t.jsx("input", { type: "text", value: E(e), disabled: n, onChange: (o) => r(o.target.value) });
}
function K({ value: e, disabled: n, onChange: r }) {
  return /* @__PURE__ */ t.jsx("input", { type: "password", value: E(e), disabled: n, onChange: (o) => r(o.target.value) });
}
function ee({ setting: e, value: n, disabled: r, onChange: o }) {
  return /* @__PURE__ */ t.jsx(
    "input",
    {
      type: "number",
      value: E(n),
      disabled: r,
      onChange: (i) => o(h(e.jsonType) === "integer" ? Number.parseInt(i.target.value || "0", 10) : Number.parseFloat(i.target.value || "0"))
    }
  );
}
function ne({ setting: e, value: n, disabled: r, onChange: o }) {
  const i = JSON.stringify(n ?? "");
  return /* @__PURE__ */ t.jsxs("select", { value: i, disabled: r, onChange: (s) => {
    const l = e.options.find((d) => JSON.stringify(d.value) === s.target.value);
    o(l?.value ?? "");
  }, children: [
    e.required ? null : /* @__PURE__ */ t.jsx("option", { value: JSON.stringify(""), children: "Empty" }),
    e.options.map((s) => /* @__PURE__ */ t.jsx("option", { value: JSON.stringify(s.value), children: s.label }, `${e.name}-${JSON.stringify(s.value)}`))
  ] });
}
function te({ setting: e, value: n, disabled: r, onChange: o }) {
  const [i, s] = u(q(n, e)), [l, d] = u(!1);
  return S(() => {
    s(q(n, e)), d(!1);
  }, [e.name, n]), /* @__PURE__ */ t.jsxs(t.Fragment, { children: [
    /* @__PURE__ */ t.jsx("textarea", { value: i, disabled: r, rows: 5, onChange: (c) => {
      const p = c.target.value;
      s(p);
      try {
        o(JSON.parse(p || D(e))), d(!1);
      } catch {
        d(!0);
      }
    } }),
    l ? /* @__PURE__ */ t.jsx("small", { className: "feature-management-setting-error", children: "Invalid JSON" }) : null
  ] });
}
function I(e) {
  return {
    ...e,
    configuration: e.configuration && typeof e.configuration == "object" && !Array.isArray(e.configuration) ? { ...e.configuration } : {}
  };
}
function re(e, n) {
  return Object.prototype.hasOwnProperty.call(e.configuration, n.name) ? e.configuration[n.name] : n.defaultValue ?? ae(n);
}
function ae(e) {
  const n = h(e.jsonType);
  return n === "boolean" ? !1 : n === "integer" || n === "number" ? 0 : n === "array" ? [] : n === "object" ? {} : "";
}
function E(e) {
  return e == null ? "" : typeof e == "object" ? JSON.stringify(e) : String(e);
}
function q(e, n) {
  return e == null || e === "" ? D(n) : JSON.stringify(e, null, 2);
}
function D(e) {
  return h(e.jsonType) === "array" ? "[]" : "{}";
}
function N(e, n) {
  return (e.uiHint ?? "").toLowerCase().includes(n);
}
function h(e) {
  return (e ?? "").trim().toLowerCase();
}
function P(e) {
  return JSON.stringify(e.map((n) => ({
    id: n.id,
    enabled: n.enabled,
    configuration: n.enabled ? n.configuration : {}
  })).sort((n, r) => n.id.localeCompare(r.id)));
}
function _(e) {
  return e instanceof Error ? e.message : String(e);
}
export {
  U as FeatureManagementPage,
  G as createApplyPayload,
  _ as getErrorMessage,
  Q as isDirty,
  ie as register,
  Y as registerBuiltInSettingEditors,
  oe as selectSettingEditor
};
