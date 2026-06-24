import { KeyRound as P, Plus as q, Search as I, RotateCcw as L, ShieldCheck as U, Trash2 as V } from "lucide-react";
import { useState as c, useMemo as T, useEffect as z } from "react";
var N = { exports: {} }, j = {};
var w;
function M() {
  if (w) return j;
  w = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function i(a, l, n) {
    var d = null;
    if (n !== void 0 && (d = "" + n), l.key !== void 0 && (d = "" + l.key), "key" in l) {
      n = {};
      for (var o in l)
        o !== "key" && (n[o] = l[o]);
    } else n = l;
    return l = n.ref, {
      $$typeof: t,
      type: a,
      key: d,
      ref: l !== void 0 ? l : null,
      props: n
    };
  }
  return j.Fragment = s, j.jsx = i, j.jsxs = i, j;
}
var $;
function F() {
  return $ || ($ = 1, N.exports = M()), N.exports;
}
var e = F();
const m = "/_elsa/secrets";
async function G(t, s = "") {
  const i = new URLSearchParams({ activeOnly: "false", pageSize: "100" });
  return s.trim() && i.set("search", s.trim()), t.http.getJson(`${m}?${i.toString()}`);
}
async function O(t, s) {
  return t.http.postJson(m, s);
}
async function W(t, s, i) {
  return t.http.postJson(`${m}/${encodeURIComponent(s)}/rotate`, i);
}
async function Y(t, s) {
  return t.http.postJson(`${m}/${encodeURIComponent(s)}/test`, {});
}
async function H(t, s) {
  return t.http.postJson(`${m}/${encodeURIComponent(s)}/revoke`, {});
}
async function B(t, s) {
  await t.http.deleteJson(`${m}/${encodeURIComponent(s)}`);
}
async function Q(t) {
  return t.http.getJson(`${m}/descriptors`);
}
async function X(t, s = "", i = []) {
  return t.http.postJson(`${m}/picker`, { search: s, typeNames: i, activeOnly: !0 });
}
function Z({ secret: t, descriptors: s }) {
  if (!t)
    return /* @__PURE__ */ e.jsx("section", { className: "secrets-detail secrets-empty", children: "Select a secret." });
  const i = s?.stores.find((l) => l.name === t.storeName), a = s?.types.find((l) => l.name === t.typeName);
  return /* @__PURE__ */ e.jsxs("section", { className: "secrets-detail", children: [
    /* @__PURE__ */ e.jsxs("header", { children: [
      /* @__PURE__ */ e.jsx("h3", { children: t.displayName || t.name }),
      /* @__PURE__ */ e.jsx("span", { children: t.status })
    ] }),
    /* @__PURE__ */ e.jsxs("dl", { children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Name" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.name })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ e.jsx("dd", { children: a?.displayName ?? t.typeName })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Store" }),
        /* @__PURE__ */ e.jsx("dd", { children: i?.displayName ?? t.storeName })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Version" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.currentVersion ?? "none" })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ e.jsx("dd", { children: D(t.createdAt) })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.expiresAt ? D(t.expiresAt) : "never" })
      ] })
    ] }),
    t.description ? /* @__PURE__ */ e.jsx("p", { children: t.description }) : null,
    /* @__PURE__ */ e.jsx("div", { className: "secrets-redaction", children: "Values are never displayed." })
  ] });
}
function D(t) {
  return new Date(t).toLocaleString();
}
function K({
  descriptors: t,
  onCancel: s,
  onSave: i
}) {
  const [a, l] = c({
    name: "",
    typeName: t.types[0]?.name ?? "text",
    storeName: t.stores[0]?.name ?? "encrypted",
    value: ""
  });
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Create secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (n) => {
    n.preventDefault(), i(a);
  }, children: [
    /* @__PURE__ */ e.jsx("h3", { children: "New secret" }),
    /* @__PURE__ */ e.jsx(x, { label: "Name", value: a.name, onChange: (n) => l({ ...a, name: n }) }),
    /* @__PURE__ */ e.jsx(x, { label: "Display name", value: a.displayName ?? "", onChange: (n) => l({ ...a, displayName: n }) }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Type",
      /* @__PURE__ */ e.jsx("select", { value: a.typeName, onChange: (n) => l({ ...a, typeName: n.target.value }), children: t.types.map((n) => /* @__PURE__ */ e.jsx("option", { value: n.name, children: n.displayName }, n.name)) })
    ] }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Store",
      /* @__PURE__ */ e.jsx("select", { value: a.storeName, onChange: (n) => l({ ...a, storeName: n.target.value }), children: t.stores.map((n) => /* @__PURE__ */ e.jsx("option", { value: n.name, children: n.displayName }, n.name)) })
    ] }),
    a.storeName === "configuration" ? /* @__PURE__ */ e.jsx(x, { label: "Configuration key", value: a.configurationKey ?? "", onChange: (n) => l({ ...a, configurationKey: n }) }) : /* @__PURE__ */ e.jsx(x, { label: "Value", type: "password", value: a.value ?? "", onChange: (n) => l({ ...a, value: n }) }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", children: "Create" })
    ] })
  ] }) });
}
function ee({ secret: t, onCancel: s, onSave: i }) {
  const [a, l] = c(""), [n, d] = c(""), o = t.storeName === "configuration";
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Rotate secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (h) => {
    h.preventDefault(), i(o ? { configurationKey: n } : { value: a });
  }, children: [
    /* @__PURE__ */ e.jsxs("h3", { children: [
      "Rotate ",
      t.name
    ] }),
    o ? /* @__PURE__ */ e.jsx(x, { label: "Configuration key", value: n, onChange: d }) : /* @__PURE__ */ e.jsx(x, { label: "New value", type: "password", value: a, onChange: l }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", children: "Rotate" })
    ] })
  ] }) });
}
function x({ label: t, value: s, type: i = "text", onChange: a }) {
  return /* @__PURE__ */ e.jsxs("label", { children: [
    t,
    /* @__PURE__ */ e.jsx("input", { type: i, value: s, onChange: (l) => a(l.target.value) })
  ] });
}
function te({ context: t }) {
  const [s, i] = c(""), [a, l] = c([]), [n, d] = c(null), [o, h] = c(null), [f, p] = c(null), [b, g] = c(!0), [C, k] = c(null), [R, E] = c(null), u = T(() => a.find((r) => r.name === n) ?? a[0] ?? null, [a, n]);
  async function y() {
    g(!0), E(null);
    try {
      const [r, A] = await Promise.all([G(t, s), Q(t)]);
      l(r.items), h(A), d((S) => S && r.items.some((_) => _.name === S) ? S : r.items[0]?.name ?? null);
    } catch (r) {
      E(r instanceof Error ? r.message : "Secrets could not be loaded.");
    } finally {
      g(!1);
    }
  }
  z(() => {
    y();
  }, []);
  async function v(r) {
    await y(), k(r);
  }
  return /* @__PURE__ */ e.jsxs("main", { className: "secrets-shell", children: [
    /* @__PURE__ */ e.jsxs("header", { className: "secrets-header", children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsxs("h2", { children: [
          /* @__PURE__ */ e.jsx(P, { size: 18 }),
          " Secrets"
        ] }),
        /* @__PURE__ */ e.jsxs("p", { children: [
          a.length,
          " configured"
        ] })
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "secrets-primary", onClick: () => p("create"), children: [
        /* @__PURE__ */ e.jsx(q, { size: 16 }),
        " New"
      ] })
    ] }),
    /* @__PURE__ */ e.jsxs("section", { className: "secrets-toolbar", children: [
      /* @__PURE__ */ e.jsxs("label", { children: [
        /* @__PURE__ */ e.jsx(I, { size: 16 }),
        /* @__PURE__ */ e.jsx("input", { value: s, onChange: (r) => i(r.target.value), onKeyDown: (r) => r.key === "Enter" && void y(), placeholder: "Search secrets" })
      ] }),
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: () => {
        y();
      }, children: "Search" })
    ] }),
    R && /* @__PURE__ */ e.jsx("p", { className: "secrets-alert", children: R }),
    C && /* @__PURE__ */ e.jsx("p", { className: "secrets-status", children: C }),
    /* @__PURE__ */ e.jsxs("section", { className: "secrets-layout", children: [
      /* @__PURE__ */ e.jsxs("div", { className: "secrets-list", "aria-label": "Secrets", children: [
        b ? /* @__PURE__ */ e.jsx("div", { className: "secrets-empty", children: "Loading" }) : null,
        !b && a.length === 0 ? /* @__PURE__ */ e.jsx("div", { className: "secrets-empty", children: "No secrets found." }) : null,
        a.map((r) => /* @__PURE__ */ e.jsxs("button", { type: "button", className: r.name === u?.name ? "active" : "", onClick: () => d(r.name), children: [
          /* @__PURE__ */ e.jsx("span", { children: r.displayName || r.name }),
          /* @__PURE__ */ e.jsxs("small", { children: [
            r.name,
            " · ",
            r.typeName,
            " · ",
            r.status
          ] })
        ] }, r.name))
      ] }),
      /* @__PURE__ */ e.jsx(Z, { secret: u, descriptors: o })
    ] }),
    u && /* @__PURE__ */ e.jsxs("footer", { className: "secrets-actions", children: [
      /* @__PURE__ */ e.jsxs("button", { type: "button", onClick: () => p("rotate"), children: [
        /* @__PURE__ */ e.jsx(L, { size: 16 }),
        " Rotate"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", onClick: async () => {
        const r = await Y(t, u.name);
        k(r.message ?? (r.succeeded ? "Secret resolved successfully." : "Secret test failed."));
      }, children: [
        /* @__PURE__ */ e.jsx(U, { size: 16 }),
        " Test"
      ] }),
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: () => {
        H(t, u.name).then(() => v("Secret revoked."));
      }, children: "Revoke" }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "danger", onClick: () => {
        B(t, u.name).then(() => v("Secret deleted."));
      }, children: [
        /* @__PURE__ */ e.jsx(V, { size: 16 }),
        " Delete"
      ] })
    ] }),
    f === "create" && o && /* @__PURE__ */ e.jsx(
      K,
      {
        descriptors: o,
        onCancel: () => p(null),
        onSave: (r) => O(t, r).then(() => v("Secret created.")).finally(() => p(null))
      }
    ),
    f === "rotate" && u && /* @__PURE__ */ e.jsx(
      ee,
      {
        secret: u,
        onCancel: () => p(null),
        onSave: (r) => W(t, u.name, r).then(() => v("Secret rotated.")).finally(() => p(null))
      }
    )
  ] });
}
function se({ value: t, disabled: s, onChange: i, endpointContext: a }) {
  const [l, n] = c([]), d = T(() => ne(t), [t]);
  return z(() => {
    X(a).then((o) => n(o.items));
  }, [a]), /* @__PURE__ */ e.jsxs("label", { className: "secret-picker-editor", children: [
    /* @__PURE__ */ e.jsx("span", { children: "Secret" }),
    /* @__PURE__ */ e.jsxs(
      "select",
      {
        disabled: s,
        value: d?.name ?? "",
        onChange: (o) => {
          const h = l.find((f) => f.name === o.target.value);
          i(h ? { type: "Secret", value: { name: h.name, typeName: h.typeName } } : null);
        },
        children: [
          /* @__PURE__ */ e.jsx("option", { value: "", children: "Select secret" }),
          l.map((o) => /* @__PURE__ */ e.jsx("option", { value: o.name, children: o.displayName || o.name }, o.name))
        ]
      }
    )
  ] });
}
function ne(t) {
  if (!t || typeof t != "object") return null;
  const s = t;
  if (s.type !== "Secret" || !s.value || typeof s.value != "object") return null;
  const i = s.value;
  return typeof i.name == "string" ? { name: i.name, typeName: i.typeName ?? null, scope: i.scope ?? null } : null;
}
let J;
function le(t) {
  J = t, t.featureAreas.add({
    id: "secrets",
    title: "Secrets",
    description: "Manage named secrets and bind workflow inputs to secret references.",
    navGroup: "Workspace",
    ownedPaths: ["/secrets"],
    required: !1,
    defaultEnabled: !0,
    order: 45,
    nav: {
      title: "Secrets",
      path: "/secrets",
      iconColor: "#64748b"
    },
    routes: [
      {
        id: "secrets",
        path: "/secrets",
        label: "Secrets",
        component: () => /* @__PURE__ */ e.jsx(te, { context: t.backend })
      }
    ]
  }), t.propertyEditors.add({
    id: "secret-picker",
    order: 30,
    supports: (s) => s.uiHint === "secret-picker" || s.defaultSyntax === "Secret",
    component: (s) => /* @__PURE__ */ e.jsx(se, { ...s, endpointContext: J.backend })
  });
}
function ie() {
  return /* @__PURE__ */ e.jsx(P, { "aria-hidden": "true", size: 16 });
}
export {
  ie as SecretsIcon,
  le as register
};
