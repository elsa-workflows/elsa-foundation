import { Save as pe, RotateCcw as le, ShieldCheck as re, Ban as ie, Trash2 as oe, KeyRound as ce, Plus as xe, Search as je, AlertCircle as fe, CheckCircle2 as ye, Pencil as ve } from "lucide-react";
import { useState as r, useEffect as ee, useMemo as V } from "react";
var Q = { exports: {} }, U = {};
var te;
function ge() {
  if (te) return U;
  te = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(i, l, m) {
    var h = null;
    if (m !== void 0 && (h = "" + m), l.key !== void 0 && (h = "" + l.key), "key" in l) {
      m = {};
      for (var d in l)
        d !== "key" && (m[d] = l[d]);
    } else m = l;
    return l = m.ref, {
      $$typeof: t,
      type: i,
      key: h,
      ref: l !== void 0 ? l : null,
      props: m
    };
  }
  return U.Fragment = s, U.jsx = a, U.jsxs = a, U;
}
var ne;
function be() {
  return ne || (ne = 1, Q.exports = ge()), Q.exports;
}
var e = be();
const T = "/_elsa/secrets";
async function Se(t, s = {}) {
  const a = typeof s == "string" ? { search: s } : s, i = new URLSearchParams({
    activeOnly: String(a.activeOnly ?? !1),
    pageSize: String(a.pageSize ?? 100)
  }), l = a.search ?? "";
  return l.trim() && i.set("search", l.trim()), t.http.getJson(`${T}?${i.toString()}`);
}
async function Ne(t, s) {
  return t.http.postJson(T, s);
}
async function Ce(t, s, a) {
  return t.http.putJson(`${T}/${encodeURIComponent(s)}`, a);
}
async function ke(t, s, a) {
  return t.http.postJson(`${T}/${encodeURIComponent(s)}/rotate`, a);
}
async function we(t, s) {
  return t.http.postJson(`${T}/${encodeURIComponent(s)}/test`, {});
}
async function Re(t, s) {
  return t.http.postJson(`${T}/${encodeURIComponent(s)}/revoke`, {});
}
async function De(t, s) {
  await t.http.deleteJson(`${T}/${encodeURIComponent(s)}`);
}
async function Te(t) {
  return t.http.getJson(`${T}/descriptors`);
}
async function Ee(t, s = {}, a = []) {
  const i = typeof s == "string" ? { search: s } : s;
  return t.http.postJson(`${T}/picker`, {
    search: i.search ?? "",
    typeNames: i.typeNames ?? a,
    storeNames: i.storeNames ?? [],
    ...i.scope ? { scope: i.scope } : {},
    activeOnly: i.activeOnly ?? !0
  });
}
function $e({
  secret: t,
  descriptors: s,
  busy: a = !1,
  onSave: i,
  onRotate: l,
  onTest: m,
  onRevoke: h,
  onDelete: d
}) {
  const [u, C] = r(""), [g, x] = r(""), [k, b] = r(!1);
  if (ee(() => {
    C(t?.displayName ?? ""), x(t?.description ?? "");
  }, [t?.name]), !t)
    return /* @__PURE__ */ e.jsx("section", { className: "secrets-detail secrets-empty", children: "Select a secret." });
  const f = s?.stores.find((p) => p.name === t.storeName), E = s?.types.find((p) => p.name === t.typeName), v = t.status !== "Deleted", D = u !== (t.displayName ?? "") || g !== (t.description ?? "");
  async function j(p) {
    if (p.preventDefault(), !!D) {
      b(!0);
      try {
        await i(t.name, {
          displayName: u.trim(),
          description: g.trim()
        });
      } finally {
        b(!1);
      }
    }
  }
  return /* @__PURE__ */ e.jsxs("section", { className: "secrets-detail", children: [
    /* @__PURE__ */ e.jsxs("header", { children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("h3", { children: t.displayName || t.name }),
        /* @__PURE__ */ e.jsx("p", { children: t.name })
      ] }),
      /* @__PURE__ */ e.jsx("span", { className: `secrets-badge ${t.status.toLowerCase()}`, children: t.status })
    ] }),
    /* @__PURE__ */ e.jsxs("dl", { className: "secrets-metadata-grid", children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Type" }),
        /* @__PURE__ */ e.jsx("dd", { children: E?.displayName ?? t.typeName })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Store" }),
        /* @__PURE__ */ e.jsx("dd", { children: f?.displayName ?? t.storeName })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Scope" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.scope || "-" })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Current version" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.currentVersion ?? "none" })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Created" }),
        /* @__PURE__ */ e.jsx("dd", { children: X(t.createdAt) })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Updated" }),
        /* @__PURE__ */ e.jsx("dd", { children: X(t.updatedAt) })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.expiresAt ? X(t.expiresAt) : "never" })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Store capability" }),
        /* @__PURE__ */ e.jsx("dd", { children: f?.isReadOnly ? "Configuration-backed value" : "Managed value" })
      ] })
    ] }),
    t.tags.length > 0 ? /* @__PURE__ */ e.jsx("div", { className: "secrets-tags", "aria-label": "Tags", children: t.tags.map((p) => /* @__PURE__ */ e.jsx("span", { children: p }, p)) }) : null,
    /* @__PURE__ */ e.jsxs("form", { className: "secrets-edit-form", onSubmit: (p) => {
      j(p);
    }, children: [
      /* @__PURE__ */ e.jsxs("label", { children: [
        /* @__PURE__ */ e.jsx("span", { children: "Display name" }),
        /* @__PURE__ */ e.jsx("input", { value: u, onChange: (p) => C(p.target.value) })
      ] }),
      /* @__PURE__ */ e.jsxs("label", { children: [
        /* @__PURE__ */ e.jsx("span", { children: "Description" }),
        /* @__PURE__ */ e.jsx("textarea", { value: g, onChange: (p) => x(p.target.value), rows: 4 })
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "submit", disabled: !D || k || a, children: [
        /* @__PURE__ */ e.jsx(pe, { size: 15 }),
        " Save metadata"
      ] })
    ] }),
    /* @__PURE__ */ e.jsx("div", { className: "secrets-redaction", children: "Values are never displayed. Studio does not reveal, cache, or export current secret material." }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-detail-actions", children: [
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !v || a, onClick: () => l(t), children: [
        /* @__PURE__ */ e.jsx(le, { size: 15 }),
        " Rotate"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !v || a, onClick: () => m(t), children: [
        /* @__PURE__ */ e.jsx(re, { size: 15 }),
        " Test"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !v || t.status === "Revoked" || a, onClick: () => h(t), children: [
        /* @__PURE__ */ e.jsx(ie, { size: 15 }),
        " Revoke"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "danger", disabled: !v || a, onClick: () => d(t), children: [
        /* @__PURE__ */ e.jsx(oe, { size: 15 }),
        " Delete"
      ] })
    ] })
  ] });
}
function X(t) {
  return t ? new Date(t).toLocaleString() : "-";
}
const ze = /^[a-z0-9._:-]+$/;
function Ae({
  descriptors: t,
  onCancel: s,
  onSave: a
}) {
  const i = t.types[0]?.name ?? "text", l = Je(t, i) ?? t.stores[0]?.name ?? "encrypted", [m, h] = r(""), [d, u] = r(""), [C, g] = r(""), [x, k] = r(i), [b, f] = r(l), [E, v] = r(""), [D, j] = r(""), [p, P] = r(""), [L, $] = r(""), [S, _] = r(""), [w, M] = r(!1), [y, B] = r(""), N = Pe(m), R = N.length > 0 && !ze.test(N), z = t.types.find((o) => o.name === x), A = V(() => Z(t, x), [t, x]), H = t.stores.find((o) => o.name === b), I = b === "configuration", K = N.length > 0 && !R && x.length > 0 && b.length > 0 && (I ? L.trim().length > 0 : p.length > 0);
  async function G(o) {
    if (o.preventDefault(), !!K) {
      M(!0), B("");
      try {
        await a({
          name: N,
          displayName: d.trim() || N,
          description: C.trim() || void 0,
          typeName: x,
          storeName: b,
          scope: E.trim() || void 0,
          tags: Le(D),
          value: I ? void 0 : p,
          configurationKey: I ? L.trim() : void 0,
          expiresAt: ue(S)
        }), P(""), $(""), s();
      } catch (F) {
        B(F instanceof Error ? F.message : "Secret could not be created.");
      } finally {
        M(!1);
      }
    }
  }
  function Y(o) {
    k(o);
    const F = Z(t, o)[0]?.name ?? "";
    f(F);
  }
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Create secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (o) => {
    G(o);
  }, children: [
    /* @__PURE__ */ e.jsx("h3", { children: "Create secret" }),
    y ? /* @__PURE__ */ e.jsx("p", { className: "secrets-alert", children: y }) : null,
    /* @__PURE__ */ e.jsx(O, { label: "Technical name", value: m, onChange: h, onBlur: () => h(N), autoComplete: "off" }),
    /* @__PURE__ */ e.jsx("p", { className: R ? "secrets-field-error" : "secrets-field-hint", children: "Trimmed and lowercased. Use letters, numbers, dot, dash, underscore, or colon." }),
    /* @__PURE__ */ e.jsx(O, { label: "Display name", value: d, onChange: u }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Description",
      /* @__PURE__ */ e.jsx("textarea", { value: C, onChange: (o) => g(o.target.value), rows: 3 })
    ] }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Type",
      /* @__PURE__ */ e.jsx("select", { value: x, onChange: (o) => Y(o.target.value), children: t.types.map((o) => /* @__PURE__ */ e.jsx("option", { value: o.name, children: o.displayName || o.name }, o.name)) })
    ] }),
    z?.description ? /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: z.description }) : null,
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Store",
      /* @__PURE__ */ e.jsx("select", { value: b, onChange: (o) => f(o.target.value), children: A.map((o) => /* @__PURE__ */ e.jsx("option", { value: o.name, children: o.displayName || o.name }, o.name)) })
    ] }),
    H?.description ? /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: H.description }) : null,
    /* @__PURE__ */ e.jsx(O, { label: "Scope", value: E, onChange: v }),
    /* @__PURE__ */ e.jsx(O, { label: "Tags", value: D, onChange: j, placeholder: "comma-separated" }),
    I ? /* @__PURE__ */ e.jsx(O, { label: "Configuration key", value: L, onChange: $, autoComplete: "off" }) : /* @__PURE__ */ e.jsx(de, { label: "Value", value: p, typeName: x, editorHint: z?.editorHint ?? null, onChange: P }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Expires",
      /* @__PURE__ */ e.jsx("input", { type: "datetime-local", value: S, onChange: (o) => _(o.target.value) })
    ] }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", disabled: !K || w, children: w ? "Creating..." : "Create" })
    ] })
  ] }) });
}
function Fe({
  secret: t,
  descriptors: s,
  onCancel: a,
  onSave: i
}) {
  const [l, m] = r(""), [h, d] = r(""), [u, C] = r(""), [g, x] = r(!1), [k, b] = r(""), f = t.storeName === "configuration", E = s?.types.find((j) => j.name === t.typeName), v = f ? h.trim().length > 0 : l.length > 0;
  async function D(j) {
    if (j.preventDefault(), !!v) {
      x(!0), b("");
      try {
        await i({
          value: f ? void 0 : l,
          configurationKey: f ? h.trim() : void 0,
          expiresAt: ue(u)
        }), m(""), d(""), a();
      } catch (p) {
        b(p instanceof Error ? p.message : "Secret could not be rotated.");
      } finally {
        x(!1);
      }
    }
  }
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Rotate secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (j) => {
    D(j);
  }, children: [
    /* @__PURE__ */ e.jsxs("h3", { children: [
      "Rotate ",
      t.name
    ] }),
    k ? /* @__PURE__ */ e.jsx("p", { className: "secrets-alert", children: k }) : null,
    /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: "Rotation replaces the backing material or configuration lookup. The current value is never shown." }),
    f ? /* @__PURE__ */ e.jsx(O, { label: "Configuration key", value: h, onChange: d, autoComplete: "off" }) : /* @__PURE__ */ e.jsx(de, { label: "New value", value: l, typeName: t.typeName, editorHint: E?.editorHint ?? null, onChange: m }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Expires",
      /* @__PURE__ */ e.jsx("input", { type: "datetime-local", value: u, onChange: (j) => C(j.target.value) })
    ] }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: a, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", disabled: !v || g, children: g ? "Rotating..." : "Rotate" })
    ] })
  ] }) });
}
function O({
  label: t,
  value: s,
  type: a = "text",
  placeholder: i,
  autoComplete: l,
  onBlur: m,
  onChange: h
}) {
  return /* @__PURE__ */ e.jsxs("label", { children: [
    t,
    /* @__PURE__ */ e.jsx("input", { type: a, value: s, placeholder: i, autoComplete: l, onBlur: m, onChange: (d) => h(d.target.value) })
  ] });
}
function de({ label: t, value: s, typeName: a, editorHint: i, onChange: l }) {
  const m = i === "multiline" || a === "rsa-key" || a === "x509-certificate";
  return /* @__PURE__ */ e.jsxs("label", { children: [
    t,
    m ? /* @__PURE__ */ e.jsx("textarea", { value: s, rows: 6, autoComplete: "new-password", spellCheck: !1, onChange: (h) => l(h.target.value) }) : /* @__PURE__ */ e.jsx("input", { type: "password", value: s, autoComplete: "new-password", onChange: (h) => l(h.target.value) })
  ] });
}
function Z(t, s) {
  const a = t.types.find((i) => i.name === s)?.supportedStoreNames ?? [];
  return a.length === 0 ? t.stores : t.stores.filter((i) => a.includes(i.name));
}
function Je(t, s) {
  return Z(t, s)[0]?.name;
}
function Pe(t) {
  return t.trim().toLowerCase();
}
function Le(t) {
  return t.split(",").map((s) => s.trim()).filter(Boolean);
}
function ue(t) {
  return t ? new Date(t).toISOString() : void 0;
}
const Ie = ["Active", "Retired", "Expired", "Revoked", "Deleted"];
function se({ context: t, dialogs: s }) {
  const [a, i] = r(""), [l, m] = r([]), [h, d] = r(null), [u, C] = r(null), [g, x] = r(null), [k, b] = r(""), [f, E] = r(""), [v, D] = r(""), [j, p] = r(""), [P, L] = r(!0), [$, S] = r(null), [_, w] = r(null), [M, y] = r(null), B = V(() => Array.from(new Set(l.map((n) => n.scope?.trim()).filter((n) => !!n))).sort(), [l]), N = V(() => l.filter((n) => !(k && n.typeName !== k || f && n.storeName !== f || v && (n.scope ?? "") !== v || j && n.status !== j)), [v, l, j, f, k]), R = V(() => N.find((n) => n.name === h) ?? N[0] ?? null, [N, h]);
  async function z(n) {
    L(!0), y(null);
    try {
      const [c, J] = await Promise.all([Se(t, { search: a, activeOnly: !1, pageSize: 100 }), Te(t)]);
      m(c.items), C(J), d((me) => {
        const W = n ?? me;
        return W && c.items.some((he) => he.name === W) ? W : c.items[0]?.name ?? null;
      });
    } catch (c) {
      y(c instanceof Error ? c.message : "Secrets could not be loaded.");
    } finally {
      L(!1);
    }
  }
  ee(() => {
    z();
  }, []);
  async function A(n, c) {
    await z(c), w(n);
  }
  async function H(n, c) {
    S(n), y(null), w(null);
    try {
      await Ce(t, n, c), await A("Secret metadata updated.");
    } catch (J) {
      throw y(J instanceof Error ? J.message : "Secret metadata could not be updated."), J;
    } finally {
      S(null);
    }
  }
  async function I(n) {
    y(null), w(null);
    const c = await Ne(t, n);
    await A("Secret created.", c.name);
  }
  async function K(n, c) {
    S(n), y(null), w(null);
    try {
      await ke(t, n, c), await A("Secret rotated.", n);
    } finally {
      S(null);
    }
  }
  async function G(n) {
    S(n.name), y(null), w(null);
    try {
      const c = await we(t, n.name), J = c.message?.trim() || (c.succeeded ? "Secret resolved successfully." : "Secret test failed.");
      w(`${c.succeeded ? "Test succeeded" : "Test failed"} (${c.code}): ${J}`);
    } catch (c) {
      y(c instanceof Error ? c.message : "Secret test failed.");
    } finally {
      S(null);
    }
  }
  async function Y(n) {
    if (!(n.status === "Revoked" || n.status === "Deleted") && await s.confirm({ message: `Revoke secret "${n.name}"? Existing references keep the name, but the secret can no longer be used as active material.`, confirmLabel: "Revoke", tone: "danger" })) {
      S(n.name), y(null), w(null);
      try {
        await Re(t, n.name), await A("Secret revoked.");
      } catch (c) {
        y(c instanceof Error ? c.message : "Secret could not be revoked.");
      } finally {
        S(null);
      }
    }
  }
  async function o(n) {
    if (await s.confirm({ message: `Delete secret "${n.name}"? This removes the metadata record and cannot reveal or recover its value.`, confirmLabel: "Delete", tone: "danger" })) {
      S(n.name), y(null), w(null);
      try {
        await De(t, n.name), await A("Secret deleted.");
      } catch (c) {
        y(c instanceof Error ? c.message : "Secret could not be deleted.");
      } finally {
        S(null);
      }
    }
  }
  const F = (n) => {
    d(n.name), x("rotate");
  };
  return /* @__PURE__ */ e.jsxs("main", { className: "secrets-shell", children: [
    /* @__PURE__ */ e.jsxs("header", { className: "secrets-header", children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsxs("h2", { children: [
          /* @__PURE__ */ e.jsx(ce, { size: 18 }),
          " Secrets"
        ] }),
        /* @__PURE__ */ e.jsxs("p", { children: [
          N.length,
          " shown · ",
          l.length,
          " total"
        ] })
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "secrets-primary", onClick: () => x("create"), children: [
        /* @__PURE__ */ e.jsx(xe, { size: 16 }),
        " Create"
      ] })
    ] }),
    /* @__PURE__ */ e.jsxs("section", { className: "secrets-toolbar", "aria-label": "Secret filters", children: [
      /* @__PURE__ */ e.jsxs("label", { className: "secrets-search", children: [
        /* @__PURE__ */ e.jsx(je, { size: 16 }),
        /* @__PURE__ */ e.jsx("input", { value: a, onChange: (n) => i(n.target.value), onKeyDown: (n) => n.key === "Enter" && void z(), placeholder: "Search secrets" })
      ] }),
      /* @__PURE__ */ e.jsx(q, { label: "Type", value: k, onChange: b, options: u?.types.map((n) => ({ value: n.name, label: n.displayName || n.name })) ?? [] }),
      /* @__PURE__ */ e.jsx(q, { label: "Store", value: f, onChange: E, options: u?.stores.map((n) => ({ value: n.name, label: n.displayName || n.name })) ?? [] }),
      /* @__PURE__ */ e.jsx(q, { label: "Scope", value: v, onChange: D, options: B.map((n) => ({ value: n, label: n })) }),
      /* @__PURE__ */ e.jsx(q, { label: "Status", value: j, onChange: p, options: Ie.map((n) => ({ value: n, label: n })) }),
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: () => {
        z();
      }, children: "Search" })
    ] }),
    M ? /* @__PURE__ */ e.jsxs("p", { className: "secrets-alert", children: [
      /* @__PURE__ */ e.jsx(fe, { size: 16 }),
      " ",
      M
    ] }) : null,
    _ ? /* @__PURE__ */ e.jsxs("p", { className: "secrets-status", children: [
      /* @__PURE__ */ e.jsx(ye, { size: 16 }),
      " ",
      _
    ] }) : null,
    /* @__PURE__ */ e.jsxs("section", { className: "secrets-layout", children: [
      /* @__PURE__ */ e.jsx("div", { className: "secrets-table-shell", children: /* @__PURE__ */ e.jsxs("table", { className: "secrets-table", children: [
        /* @__PURE__ */ e.jsx("thead", { children: /* @__PURE__ */ e.jsxs("tr", { children: [
          /* @__PURE__ */ e.jsx("th", { children: "Name" }),
          /* @__PURE__ */ e.jsx("th", { children: "Display name" }),
          /* @__PURE__ */ e.jsx("th", { children: "Type" }),
          /* @__PURE__ */ e.jsx("th", { children: "Store" }),
          /* @__PURE__ */ e.jsx("th", { children: "Scope" }),
          /* @__PURE__ */ e.jsx("th", { children: "Status" }),
          /* @__PURE__ */ e.jsx("th", { children: "Version" }),
          /* @__PURE__ */ e.jsx("th", { children: "Updated" }),
          /* @__PURE__ */ e.jsx("th", { children: "Actions" })
        ] }) }),
        /* @__PURE__ */ e.jsxs("tbody", { children: [
          P ? /* @__PURE__ */ e.jsx("tr", { children: /* @__PURE__ */ e.jsx("td", { colSpan: 9, className: "secrets-empty", children: "Loading secrets..." }) }) : null,
          !P && N.length === 0 ? /* @__PURE__ */ e.jsx("tr", { children: /* @__PURE__ */ e.jsx("td", { colSpan: 9, className: "secrets-empty", children: "No secrets found." }) }) : null,
          !P && N.map((n) => /* @__PURE__ */ e.jsxs("tr", { className: n.name === R?.name ? "active" : "", children: [
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsx("button", { type: "button", className: "secrets-row-link", onClick: () => d(n.name), children: n.name }) }),
            /* @__PURE__ */ e.jsx("td", { children: n.displayName || "-" }),
            /* @__PURE__ */ e.jsx("td", { children: Me(u, n.typeName) }),
            /* @__PURE__ */ e.jsx("td", { children: Ue(u, n.storeName) }),
            /* @__PURE__ */ e.jsx("td", { children: n.scope || "-" }),
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsx(Oe, { status: n.status }) }),
            /* @__PURE__ */ e.jsx("td", { children: n.currentVersion ?? "-" }),
            /* @__PURE__ */ e.jsx("td", { children: Ve(n.updatedAt ?? n.createdAt) }),
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsxs("div", { className: "secrets-row-actions", children: [
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Open", "aria-label": `Open ${n.name}`, onClick: () => d(n.name), children: /* @__PURE__ */ e.jsx(ve, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Rotate", "aria-label": `Rotate ${n.name}`, disabled: n.status === "Deleted" || $ === n.name, onClick: () => F(n), children: /* @__PURE__ */ e.jsx(le, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Test", "aria-label": `Test ${n.name}`, disabled: $ === n.name || n.status === "Deleted", onClick: () => {
                G(n);
              }, children: /* @__PURE__ */ e.jsx(re, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Revoke", "aria-label": `Revoke ${n.name}`, disabled: $ === n.name || n.status === "Revoked" || n.status === "Deleted", onClick: () => {
                Y(n);
              }, children: /* @__PURE__ */ e.jsx(ie, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", className: "danger", title: "Delete", "aria-label": `Delete ${n.name}`, disabled: $ === n.name || n.status === "Deleted", onClick: () => {
                o(n);
              }, children: /* @__PURE__ */ e.jsx(oe, { size: 14 }) })
            ] }) })
          ] }, n.name))
        ] })
      ] }) }),
      /* @__PURE__ */ e.jsx(
        $e,
        {
          secret: R,
          descriptors: u,
          busy: R ? $ === R.name : !1,
          onSave: H,
          onRotate: F,
          onTest: (n) => {
            G(n);
          },
          onRevoke: (n) => {
            Y(n);
          },
          onDelete: (n) => {
            o(n);
          }
        }
      )
    ] }),
    g === "create" && u ? /* @__PURE__ */ e.jsx(
      Ae,
      {
        descriptors: u,
        onCancel: () => x(null),
        onSave: I
      }
    ) : null,
    g === "rotate" && R ? /* @__PURE__ */ e.jsx(
      Fe,
      {
        secret: R,
        descriptors: u,
        onCancel: () => x(null),
        onSave: (n) => K(R.name, n)
      }
    ) : null
  ] });
}
function q({ label: t, value: s, options: a, onChange: i }) {
  return /* @__PURE__ */ e.jsxs("label", { className: "secrets-filter", children: [
    /* @__PURE__ */ e.jsx("span", { children: t }),
    /* @__PURE__ */ e.jsxs("select", { value: s, onChange: (l) => i(l.target.value), children: [
      /* @__PURE__ */ e.jsx("option", { value: "", children: "All" }),
      a.map((l) => /* @__PURE__ */ e.jsx("option", { value: l.value, children: l.label }, l.value))
    ] })
  ] });
}
function Oe({ status: t }) {
  return /* @__PURE__ */ e.jsx("span", { className: `secrets-badge ${t.toLowerCase()}`, children: t });
}
function Me(t, s) {
  return t?.types.find((a) => a.name === s)?.displayName ?? s;
}
function Ue(t, s) {
  return t?.stores.find((a) => a.name === s)?.displayName ?? s;
}
function Ve(t) {
  return t ? new Date(t).toLocaleString() : "-";
}
function _e({ value: t, disabled: s, onChange: a, endpointContext: i }) {
  const [l, m] = r([]), h = V(() => Be(t), [t]);
  return ee(() => {
    Ee(i, { activeOnly: !0 }).then((d) => {
      m(d.items.filter((u) => !u.status || u.status === "Active"));
    });
  }, [i]), /* @__PURE__ */ e.jsxs("label", { className: "secret-picker-editor", children: [
    /* @__PURE__ */ e.jsx("span", { children: "Secret" }),
    /* @__PURE__ */ e.jsxs(
      "select",
      {
        disabled: s,
        value: h?.name ?? "",
        onChange: (d) => {
          const u = l.find((g) => g.name === d.target.value);
          if (!u) {
            a(null);
            return;
          }
          const C = { name: u.name, typeName: u.typeName, ...u.scope ? { scope: u.scope } : {} };
          a({ type: "Secret", value: C });
        },
        children: [
          /* @__PURE__ */ e.jsx("option", { value: "", children: "Select secret" }),
          l.map((d) => /* @__PURE__ */ e.jsx("option", { value: d.name, children: d.displayName || d.name }, d.name))
        ]
      }
    )
  ] });
}
function Be(t) {
  if (!t || typeof t != "object") return null;
  const s = t;
  if (s.type !== "Secret" || !s.value || typeof s.value != "object") return null;
  const a = s.value;
  return typeof a.name == "string" ? { name: a.name, typeName: a.typeName ?? null, scope: a.scope ?? null } : null;
}
let ae;
function Ge(t) {
  ae = t, t.featureAreas.add({
    id: "secrets",
    title: "Secrets",
    description: "Manage named secrets and bind workflow inputs to secret references.",
    navGroup: "Security",
    ownedPaths: ["/security/secrets", "/secrets"],
    required: !1,
    defaultEnabled: !0,
    order: 45,
    nav: {
      title: "Secrets",
      path: "/security/secrets",
      iconColor: "#64748b"
    },
    routes: [
      {
        id: "secrets",
        path: "/security/secrets",
        label: "Secrets",
        component: () => /* @__PURE__ */ e.jsx(se, { context: t.backend, dialogs: t.dialogs })
      },
      {
        id: "secrets-legacy",
        path: "/secrets",
        label: "Secrets",
        component: () => /* @__PURE__ */ e.jsx(se, { context: t.backend, dialogs: t.dialogs })
      }
    ]
  }), t.propertyEditors.add({
    id: "secret-picker",
    order: 30,
    supports: (s) => s.uiHint === "secret-picker" || s.defaultSyntax === "Secret",
    component: (s) => /* @__PURE__ */ e.jsx(_e, { ...s, endpointContext: ae.backend })
  });
}
function Ye() {
  return /* @__PURE__ */ e.jsx(ce, { "aria-hidden": "true", size: 16 });
}
export {
  Ye as SecretsIcon,
  Ge as register
};
