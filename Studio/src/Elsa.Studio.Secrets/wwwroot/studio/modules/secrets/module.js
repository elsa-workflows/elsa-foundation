import { Save as pe, RotateCcw as ae, ShieldCheck as le, Ban as re, Trash2 as ie, KeyRound as oe, Plus as he, Search as xe, AlertCircle as je, CheckCircle2 as fe, Pencil as ye } from "lucide-react";
import { useState as i, useEffect as Z, useMemo as V } from "react";
var W = { exports: {} }, U = {};
var ee;
function ve() {
  if (ee) return U;
  ee = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(l, r, p) {
    var u = null;
    if (p !== void 0 && (u = "" + p), r.key !== void 0 && (u = "" + r.key), "key" in r) {
      p = {};
      for (var o in r)
        o !== "key" && (p[o] = r[o]);
    } else p = r;
    return r = p.ref, {
      $$typeof: t,
      type: l,
      key: u,
      ref: r !== void 0 ? r : null,
      props: p
    };
  }
  return U.Fragment = s, U.jsx = a, U.jsxs = a, U;
}
var te;
function ge() {
  return te || (te = 1, W.exports = ve()), W.exports;
}
var e = ge();
const $ = "/_elsa/secrets";
async function be(t, s = {}) {
  const a = typeof s == "string" ? { search: s } : s, l = new URLSearchParams({
    activeOnly: String(a.activeOnly ?? !1),
    pageSize: String(a.pageSize ?? 100)
  }), r = a.search ?? "";
  return r.trim() && l.set("search", r.trim()), t.http.getJson(`${$}?${l.toString()}`);
}
async function Se(t, s) {
  return t.http.postJson($, s);
}
async function Ne(t, s, a) {
  return t.http.putJson(`${$}/${encodeURIComponent(s)}`, a);
}
async function Ce(t, s, a) {
  return t.http.postJson(`${$}/${encodeURIComponent(s)}/rotate`, a);
}
async function ke(t, s) {
  return t.http.postJson(`${$}/${encodeURIComponent(s)}/test`, {});
}
async function we(t, s) {
  return t.http.postJson(`${$}/${encodeURIComponent(s)}/revoke`, {});
}
async function Re(t, s) {
  await t.http.deleteJson(`${$}/${encodeURIComponent(s)}`);
}
async function De(t) {
  return t.http.getJson(`${$}/descriptors`);
}
async function Te(t, s = {}, a = []) {
  const l = typeof s == "string" ? { search: s } : s;
  return t.http.postJson(`${$}/picker`, {
    search: l.search ?? "",
    typeNames: l.typeNames ?? a,
    storeNames: l.storeNames ?? [],
    ...l.scope ? { scope: l.scope } : {},
    activeOnly: l.activeOnly ?? !0
  });
}
function Ee({
  secret: t,
  descriptors: s,
  busy: a = !1,
  onSave: l,
  onRotate: r,
  onTest: p,
  onRevoke: u,
  onDelete: o
}) {
  const [h, S] = i(""), [f, x] = i(""), [z, j] = i(!1);
  if (Z(() => {
    S(t?.displayName ?? ""), x(t?.description ?? "");
  }, [t?.name]), !t)
    return /* @__PURE__ */ e.jsx("section", { className: "secrets-detail secrets-empty", children: "Select a secret." });
  const N = s?.stores.find((m) => m.name === t.storeName), k = s?.types.find((m) => m.name === t.typeName), w = t.status !== "Deleted", C = h !== (t.displayName ?? "") || f !== (t.description ?? "");
  async function g(m) {
    if (m.preventDefault(), !!C) {
      j(!0);
      try {
        await l(t.name, {
          displayName: h.trim(),
          description: f.trim()
        });
      } finally {
        j(!1);
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
        /* @__PURE__ */ e.jsx("dd", { children: k?.displayName ?? t.typeName })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Store" }),
        /* @__PURE__ */ e.jsx("dd", { children: N?.displayName ?? t.storeName })
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
        /* @__PURE__ */ e.jsx("dd", { children: Q(t.createdAt) })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Updated" }),
        /* @__PURE__ */ e.jsx("dd", { children: Q(t.updatedAt) })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Expires" }),
        /* @__PURE__ */ e.jsx("dd", { children: t.expiresAt ? Q(t.expiresAt) : "never" })
      ] }),
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsx("dt", { children: "Store capability" }),
        /* @__PURE__ */ e.jsx("dd", { children: N?.isReadOnly ? "Configuration-backed value" : "Managed value" })
      ] })
    ] }),
    t.tags.length > 0 ? /* @__PURE__ */ e.jsx("div", { className: "secrets-tags", "aria-label": "Tags", children: t.tags.map((m) => /* @__PURE__ */ e.jsx("span", { children: m }, m)) }) : null,
    /* @__PURE__ */ e.jsxs("form", { className: "secrets-edit-form", onSubmit: (m) => {
      g(m);
    }, children: [
      /* @__PURE__ */ e.jsxs("label", { children: [
        /* @__PURE__ */ e.jsx("span", { children: "Display name" }),
        /* @__PURE__ */ e.jsx("input", { value: h, onChange: (m) => S(m.target.value) })
      ] }),
      /* @__PURE__ */ e.jsxs("label", { children: [
        /* @__PURE__ */ e.jsx("span", { children: "Description" }),
        /* @__PURE__ */ e.jsx("textarea", { value: f, onChange: (m) => x(m.target.value), rows: 4 })
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "submit", disabled: !C || z || a, children: [
        /* @__PURE__ */ e.jsx(pe, { size: 15 }),
        " Save metadata"
      ] })
    ] }),
    /* @__PURE__ */ e.jsx("div", { className: "secrets-redaction", children: "Values are never displayed. Studio does not reveal, cache, or export current secret material." }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-detail-actions", children: [
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !w || a, onClick: () => r(t), children: [
        /* @__PURE__ */ e.jsx(ae, { size: 15 }),
        " Rotate"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !w || a, onClick: () => p(t), children: [
        /* @__PURE__ */ e.jsx(le, { size: 15 }),
        " Test"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", disabled: !w || t.status === "Revoked" || a, onClick: () => u(t), children: [
        /* @__PURE__ */ e.jsx(re, { size: 15 }),
        " Revoke"
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "danger", disabled: !w || a, onClick: () => o(t), children: [
        /* @__PURE__ */ e.jsx(ie, { size: 15 }),
        " Delete"
      ] })
    ] })
  ] });
}
function Q(t) {
  return t ? new Date(t).toLocaleString() : "-";
}
const $e = /^[a-z0-9._:-]+$/;
function ze({
  descriptors: t,
  onCancel: s,
  onSave: a
}) {
  const l = t.types[0]?.name ?? "text", r = Fe(t, l) ?? t.stores[0]?.name ?? "encrypted", [p, u] = i(""), [o, h] = i(""), [S, f] = i(""), [x, z] = i(l), [j, N] = i(r), [k, w] = i(""), [C, g] = i(""), [m, I] = i(""), [D, b] = i(""), [O, R] = i(""), [L, y] = i(!1), [_, T] = i(""), v = Je(p), A = v.length > 0 && !$e.test(v), E = t.types.find((c) => c.name === x), Y = V(() => X(t, x), [t, x]), B = t.stores.find((c) => c.name === j), J = j === "configuration", M = v.length > 0 && !A && x.length > 0 && j.length > 0 && (J ? D.trim().length > 0 : m.length > 0);
  async function H(c) {
    if (c.preventDefault(), !!M) {
      y(!0), T("");
      try {
        await a({
          name: v,
          displayName: o.trim() || v,
          description: S.trim() || void 0,
          typeName: x,
          storeName: j,
          scope: k.trim() || void 0,
          tags: Pe(C),
          value: J ? void 0 : m,
          configurationKey: J ? D.trim() : void 0,
          expiresAt: de(O)
        }), I(""), b(""), s();
      } catch (n) {
        T(n instanceof Error ? n.message : "Secret could not be created.");
      } finally {
        y(!1);
      }
    }
  }
  function K(c) {
    z(c);
    const n = X(t, c)[0]?.name ?? "";
    N(n);
  }
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Create secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (c) => {
    H(c);
  }, children: [
    /* @__PURE__ */ e.jsx("h3", { children: "Create secret" }),
    _ ? /* @__PURE__ */ e.jsx("p", { className: "secrets-alert", children: _ }) : null,
    /* @__PURE__ */ e.jsx(P, { label: "Technical name", value: p, onChange: u, onBlur: () => u(v), autoComplete: "off" }),
    /* @__PURE__ */ e.jsx("p", { className: A ? "secrets-field-error" : "secrets-field-hint", children: "Trimmed and lowercased. Use letters, numbers, dot, dash, underscore, or colon." }),
    /* @__PURE__ */ e.jsx(P, { label: "Display name", value: o, onChange: h }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Description",
      /* @__PURE__ */ e.jsx("textarea", { value: S, onChange: (c) => f(c.target.value), rows: 3 })
    ] }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Type",
      /* @__PURE__ */ e.jsx("select", { value: x, onChange: (c) => K(c.target.value), children: t.types.map((c) => /* @__PURE__ */ e.jsx("option", { value: c.name, children: c.displayName || c.name }, c.name)) })
    ] }),
    E?.description ? /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: E.description }) : null,
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Store",
      /* @__PURE__ */ e.jsx("select", { value: j, onChange: (c) => N(c.target.value), children: Y.map((c) => /* @__PURE__ */ e.jsx("option", { value: c.name, children: c.displayName || c.name }, c.name)) })
    ] }),
    B?.description ? /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: B.description }) : null,
    /* @__PURE__ */ e.jsx(P, { label: "Scope", value: k, onChange: w }),
    /* @__PURE__ */ e.jsx(P, { label: "Tags", value: C, onChange: g, placeholder: "comma-separated" }),
    J ? /* @__PURE__ */ e.jsx(P, { label: "Configuration key", value: D, onChange: b, autoComplete: "off" }) : /* @__PURE__ */ e.jsx(ce, { label: "Value", value: m, typeName: x, editorHint: E?.editorHint ?? null, onChange: I }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Expires",
      /* @__PURE__ */ e.jsx("input", { type: "datetime-local", value: O, onChange: (c) => R(c.target.value) })
    ] }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: s, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", disabled: !M || L, children: L ? "Creating..." : "Create" })
    ] })
  ] }) });
}
function Ae({
  secret: t,
  descriptors: s,
  onCancel: a,
  onSave: l
}) {
  const [r, p] = i(""), [u, o] = i(""), [h, S] = i(""), [f, x] = i(!1), [z, j] = i(""), N = t.storeName === "configuration", k = s?.types.find((g) => g.name === t.typeName), w = N ? u.trim().length > 0 : r.length > 0;
  async function C(g) {
    if (g.preventDefault(), !!w) {
      x(!0), j("");
      try {
        await l({
          value: N ? void 0 : r,
          configurationKey: N ? u.trim() : void 0,
          expiresAt: de(h)
        }), p(""), o(""), a();
      } catch (m) {
        j(m instanceof Error ? m.message : "Secret could not be rotated.");
      } finally {
        x(!1);
      }
    }
  }
  return /* @__PURE__ */ e.jsx("div", { className: "secrets-modal", role: "dialog", "aria-modal": "true", "aria-label": "Rotate secret", children: /* @__PURE__ */ e.jsxs("form", { onSubmit: (g) => {
    C(g);
  }, children: [
    /* @__PURE__ */ e.jsxs("h3", { children: [
      "Rotate ",
      t.name
    ] }),
    z ? /* @__PURE__ */ e.jsx("p", { className: "secrets-alert", children: z }) : null,
    /* @__PURE__ */ e.jsx("p", { className: "secrets-field-hint", children: "Rotation replaces the backing material or configuration lookup. The current value is never shown." }),
    N ? /* @__PURE__ */ e.jsx(P, { label: "Configuration key", value: u, onChange: o, autoComplete: "off" }) : /* @__PURE__ */ e.jsx(ce, { label: "New value", value: r, typeName: t.typeName, editorHint: k?.editorHint ?? null, onChange: p }),
    /* @__PURE__ */ e.jsxs("label", { children: [
      "Expires",
      /* @__PURE__ */ e.jsx("input", { type: "datetime-local", value: h, onChange: (g) => S(g.target.value) })
    ] }),
    /* @__PURE__ */ e.jsxs("div", { className: "secrets-dialog-actions", children: [
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: a, children: "Cancel" }),
      /* @__PURE__ */ e.jsx("button", { type: "submit", disabled: !w || f, children: f ? "Rotating..." : "Rotate" })
    ] })
  ] }) });
}
function P({
  label: t,
  value: s,
  type: a = "text",
  placeholder: l,
  autoComplete: r,
  onBlur: p,
  onChange: u
}) {
  return /* @__PURE__ */ e.jsxs("label", { children: [
    t,
    /* @__PURE__ */ e.jsx("input", { type: a, value: s, placeholder: l, autoComplete: r, onBlur: p, onChange: (o) => u(o.target.value) })
  ] });
}
function ce({ label: t, value: s, typeName: a, editorHint: l, onChange: r }) {
  const p = l === "multiline" || a === "rsa-key" || a === "x509-certificate";
  return /* @__PURE__ */ e.jsxs("label", { children: [
    t,
    p ? /* @__PURE__ */ e.jsx("textarea", { value: s, rows: 6, autoComplete: "new-password", spellCheck: !1, onChange: (u) => r(u.target.value) }) : /* @__PURE__ */ e.jsx("input", { type: "password", value: s, autoComplete: "new-password", onChange: (u) => r(u.target.value) })
  ] });
}
function X(t, s) {
  const a = t.types.find((l) => l.name === s)?.supportedStoreNames ?? [];
  return a.length === 0 ? t.stores : t.stores.filter((l) => a.includes(l.name));
}
function Fe(t, s) {
  return X(t, s)[0]?.name;
}
function Je(t) {
  return t.trim().toLowerCase();
}
function Pe(t) {
  return t.split(",").map((s) => s.trim()).filter(Boolean);
}
function de(t) {
  return t ? new Date(t).toISOString() : void 0;
}
const Ie = ["Active", "Retired", "Expired", "Revoked", "Deleted"];
function ne({ context: t }) {
  const [s, a] = i(""), [l, r] = i([]), [p, u] = i(null), [o, h] = i(null), [S, f] = i(null), [x, z] = i(""), [j, N] = i(""), [k, w] = i(""), [C, g] = i(""), [m, I] = i(!0), [D, b] = i(null), [O, R] = i(null), [L, y] = i(null), _ = V(() => Array.from(new Set(l.map((n) => n.scope?.trim()).filter((n) => !!n))).sort(), [l]), T = V(() => l.filter((n) => !(x && n.typeName !== x || j && n.storeName !== j || k && (n.scope ?? "") !== k || C && n.status !== C)), [k, l, C, j, x]), v = V(() => T.find((n) => n.name === p) ?? T[0] ?? null, [T, p]);
  async function A(n) {
    I(!0), y(null);
    try {
      const [d, F] = await Promise.all([be(t, { search: s, activeOnly: !1, pageSize: 100 }), De(t)]);
      r(d.items), h(F), u((ue) => {
        const q = n ?? ue;
        return q && d.items.some((me) => me.name === q) ? q : d.items[0]?.name ?? null;
      });
    } catch (d) {
      y(d instanceof Error ? d.message : "Secrets could not be loaded.");
    } finally {
      I(!1);
    }
  }
  Z(() => {
    A();
  }, []);
  async function E(n, d) {
    await A(d), R(n);
  }
  async function Y(n, d) {
    b(n), y(null), R(null);
    try {
      await Ne(t, n, d), await E("Secret metadata updated.");
    } catch (F) {
      throw y(F instanceof Error ? F.message : "Secret metadata could not be updated."), F;
    } finally {
      b(null);
    }
  }
  async function B(n) {
    y(null), R(null);
    const d = await Se(t, n);
    await E("Secret created.", d.name);
  }
  async function J(n, d) {
    b(n), y(null), R(null);
    try {
      await Ce(t, n, d), await E("Secret rotated.", n);
    } finally {
      b(null);
    }
  }
  async function M(n) {
    b(n.name), y(null), R(null);
    try {
      const d = await ke(t, n.name), F = d.message?.trim() || (d.succeeded ? "Secret resolved successfully." : "Secret test failed.");
      R(`${d.succeeded ? "Test succeeded" : "Test failed"} (${d.code}): ${F}`);
    } catch (d) {
      y(d instanceof Error ? d.message : "Secret test failed.");
    } finally {
      b(null);
    }
  }
  async function H(n) {
    if (!(n.status === "Revoked" || n.status === "Deleted") && window.confirm(`Revoke secret "${n.name}"? Existing references keep the name, but the secret can no longer be used as active material.`)) {
      b(n.name), y(null), R(null);
      try {
        await we(t, n.name), await E("Secret revoked.");
      } catch (d) {
        y(d instanceof Error ? d.message : "Secret could not be revoked.");
      } finally {
        b(null);
      }
    }
  }
  async function K(n) {
    if (window.confirm(`Delete secret "${n.name}"? This removes the metadata record and cannot reveal or recover its value.`)) {
      b(n.name), y(null), R(null);
      try {
        await Re(t, n.name), await E("Secret deleted.");
      } catch (d) {
        y(d instanceof Error ? d.message : "Secret could not be deleted.");
      } finally {
        b(null);
      }
    }
  }
  const c = (n) => {
    u(n.name), f("rotate");
  };
  return /* @__PURE__ */ e.jsxs("main", { className: "secrets-shell", children: [
    /* @__PURE__ */ e.jsxs("header", { className: "secrets-header", children: [
      /* @__PURE__ */ e.jsxs("div", { children: [
        /* @__PURE__ */ e.jsxs("h2", { children: [
          /* @__PURE__ */ e.jsx(oe, { size: 18 }),
          " Secrets"
        ] }),
        /* @__PURE__ */ e.jsxs("p", { children: [
          T.length,
          " shown · ",
          l.length,
          " total"
        ] })
      ] }),
      /* @__PURE__ */ e.jsxs("button", { type: "button", className: "secrets-primary", onClick: () => f("create"), children: [
        /* @__PURE__ */ e.jsx(he, { size: 16 }),
        " Create"
      ] })
    ] }),
    /* @__PURE__ */ e.jsxs("section", { className: "secrets-toolbar", "aria-label": "Secret filters", children: [
      /* @__PURE__ */ e.jsxs("label", { className: "secrets-search", children: [
        /* @__PURE__ */ e.jsx(xe, { size: 16 }),
        /* @__PURE__ */ e.jsx("input", { value: s, onChange: (n) => a(n.target.value), onKeyDown: (n) => n.key === "Enter" && void A(), placeholder: "Search secrets" })
      ] }),
      /* @__PURE__ */ e.jsx(G, { label: "Type", value: x, onChange: z, options: o?.types.map((n) => ({ value: n.name, label: n.displayName || n.name })) ?? [] }),
      /* @__PURE__ */ e.jsx(G, { label: "Store", value: j, onChange: N, options: o?.stores.map((n) => ({ value: n.name, label: n.displayName || n.name })) ?? [] }),
      /* @__PURE__ */ e.jsx(G, { label: "Scope", value: k, onChange: w, options: _.map((n) => ({ value: n, label: n })) }),
      /* @__PURE__ */ e.jsx(G, { label: "Status", value: C, onChange: g, options: Ie.map((n) => ({ value: n, label: n })) }),
      /* @__PURE__ */ e.jsx("button", { type: "button", onClick: () => {
        A();
      }, children: "Search" })
    ] }),
    L ? /* @__PURE__ */ e.jsxs("p", { className: "secrets-alert", children: [
      /* @__PURE__ */ e.jsx(je, { size: 16 }),
      " ",
      L
    ] }) : null,
    O ? /* @__PURE__ */ e.jsxs("p", { className: "secrets-status", children: [
      /* @__PURE__ */ e.jsx(fe, { size: 16 }),
      " ",
      O
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
          m ? /* @__PURE__ */ e.jsx("tr", { children: /* @__PURE__ */ e.jsx("td", { colSpan: 9, className: "secrets-empty", children: "Loading secrets..." }) }) : null,
          !m && T.length === 0 ? /* @__PURE__ */ e.jsx("tr", { children: /* @__PURE__ */ e.jsx("td", { colSpan: 9, className: "secrets-empty", children: "No secrets found." }) }) : null,
          !m && T.map((n) => /* @__PURE__ */ e.jsxs("tr", { className: n.name === v?.name ? "active" : "", children: [
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsx("button", { type: "button", className: "secrets-row-link", onClick: () => u(n.name), children: n.name }) }),
            /* @__PURE__ */ e.jsx("td", { children: n.displayName || "-" }),
            /* @__PURE__ */ e.jsx("td", { children: Le(o, n.typeName) }),
            /* @__PURE__ */ e.jsx("td", { children: Me(o, n.storeName) }),
            /* @__PURE__ */ e.jsx("td", { children: n.scope || "-" }),
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsx(Oe, { status: n.status }) }),
            /* @__PURE__ */ e.jsx("td", { children: n.currentVersion ?? "-" }),
            /* @__PURE__ */ e.jsx("td", { children: Ue(n.updatedAt ?? n.createdAt) }),
            /* @__PURE__ */ e.jsx("td", { children: /* @__PURE__ */ e.jsxs("div", { className: "secrets-row-actions", children: [
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Open", "aria-label": `Open ${n.name}`, onClick: () => u(n.name), children: /* @__PURE__ */ e.jsx(ye, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Rotate", "aria-label": `Rotate ${n.name}`, disabled: n.status === "Deleted" || D === n.name, onClick: () => c(n), children: /* @__PURE__ */ e.jsx(ae, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Test", "aria-label": `Test ${n.name}`, disabled: D === n.name || n.status === "Deleted", onClick: () => {
                M(n);
              }, children: /* @__PURE__ */ e.jsx(le, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", title: "Revoke", "aria-label": `Revoke ${n.name}`, disabled: D === n.name || n.status === "Revoked" || n.status === "Deleted", onClick: () => {
                H(n);
              }, children: /* @__PURE__ */ e.jsx(re, { size: 14 }) }),
              /* @__PURE__ */ e.jsx("button", { type: "button", className: "danger", title: "Delete", "aria-label": `Delete ${n.name}`, disabled: D === n.name || n.status === "Deleted", onClick: () => {
                K(n);
              }, children: /* @__PURE__ */ e.jsx(ie, { size: 14 }) })
            ] }) })
          ] }, n.name))
        ] })
      ] }) }),
      /* @__PURE__ */ e.jsx(
        Ee,
        {
          secret: v,
          descriptors: o,
          busy: v ? D === v.name : !1,
          onSave: Y,
          onRotate: c,
          onTest: (n) => {
            M(n);
          },
          onRevoke: (n) => {
            H(n);
          },
          onDelete: (n) => {
            K(n);
          }
        }
      )
    ] }),
    S === "create" && o ? /* @__PURE__ */ e.jsx(
      ze,
      {
        descriptors: o,
        onCancel: () => f(null),
        onSave: B
      }
    ) : null,
    S === "rotate" && v ? /* @__PURE__ */ e.jsx(
      Ae,
      {
        secret: v,
        descriptors: o,
        onCancel: () => f(null),
        onSave: (n) => J(v.name, n)
      }
    ) : null
  ] });
}
function G({ label: t, value: s, options: a, onChange: l }) {
  return /* @__PURE__ */ e.jsxs("label", { className: "secrets-filter", children: [
    /* @__PURE__ */ e.jsx("span", { children: t }),
    /* @__PURE__ */ e.jsxs("select", { value: s, onChange: (r) => l(r.target.value), children: [
      /* @__PURE__ */ e.jsx("option", { value: "", children: "All" }),
      a.map((r) => /* @__PURE__ */ e.jsx("option", { value: r.value, children: r.label }, r.value))
    ] })
  ] });
}
function Oe({ status: t }) {
  return /* @__PURE__ */ e.jsx("span", { className: `secrets-badge ${t.toLowerCase()}`, children: t });
}
function Le(t, s) {
  return t?.types.find((a) => a.name === s)?.displayName ?? s;
}
function Me(t, s) {
  return t?.stores.find((a) => a.name === s)?.displayName ?? s;
}
function Ue(t) {
  return t ? new Date(t).toLocaleString() : "-";
}
function Ve({ value: t, disabled: s, onChange: a, endpointContext: l }) {
  const [r, p] = i([]), u = V(() => _e(t), [t]);
  return Z(() => {
    Te(l, { activeOnly: !0 }).then((o) => {
      p(o.items.filter((h) => !h.status || h.status === "Active"));
    });
  }, [l]), /* @__PURE__ */ e.jsxs("label", { className: "secret-picker-editor", children: [
    /* @__PURE__ */ e.jsx("span", { children: "Secret" }),
    /* @__PURE__ */ e.jsxs(
      "select",
      {
        disabled: s,
        value: u?.name ?? "",
        onChange: (o) => {
          const h = r.find((f) => f.name === o.target.value);
          if (!h) {
            a(null);
            return;
          }
          const S = { name: h.name, typeName: h.typeName, ...h.scope ? { scope: h.scope } : {} };
          a({ type: "Secret", value: S });
        },
        children: [
          /* @__PURE__ */ e.jsx("option", { value: "", children: "Select secret" }),
          r.map((o) => /* @__PURE__ */ e.jsx("option", { value: o.name, children: o.displayName || o.name }, o.name))
        ]
      }
    )
  ] });
}
function _e(t) {
  if (!t || typeof t != "object") return null;
  const s = t;
  if (s.type !== "Secret" || !s.value || typeof s.value != "object") return null;
  const a = s.value;
  return typeof a.name == "string" ? { name: a.name, typeName: a.typeName ?? null, scope: a.scope ?? null } : null;
}
let se;
function Ke(t) {
  se = t, t.featureAreas.add({
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
        component: () => /* @__PURE__ */ e.jsx(ne, { context: t.backend })
      },
      {
        id: "secrets-legacy",
        path: "/secrets",
        label: "Secrets",
        component: () => /* @__PURE__ */ e.jsx(ne, { context: t.backend })
      }
    ]
  }), t.propertyEditors.add({
    id: "secret-picker",
    order: 30,
    supports: (s) => s.uiHint === "secret-picker" || s.defaultSyntax === "Secret",
    component: (s) => /* @__PURE__ */ e.jsx(Ve, { ...s, endpointContext: se.backend })
  });
}
function Ge() {
  return /* @__PURE__ */ e.jsx(oe, { "aria-hidden": "true", size: 16 });
}
export {
  Ge as SecretsIcon,
  Ke as register
};
