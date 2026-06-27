import { useMemo as E, lazy as S, Suspense as R } from "react";
var c = { exports: {} }, u = {};
var v;
function J() {
  if (v) return u;
  v = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), r = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(n, a, i) {
    var o = null;
    if (i !== void 0 && (o = "" + i), a.key !== void 0 && (o = "" + a.key), "key" in a) {
      i = {};
      for (var l in a)
        l !== "key" && (i[l] = a[l]);
    } else i = a;
    return a = i.ref, {
      $$typeof: e,
      type: n,
      key: o,
      ref: a !== void 0 ? a : null,
      props: i
    };
  }
  return u.Fragment = r, u.jsx = s, u.jsxs = s, u;
}
var f;
function N() {
  return f || (f = 1, c.exports = J()), c.exports;
}
var t = N();
function j({
  document: e,
  readOnly: r,
  minHeight: s,
  ariaLabel: n,
  onChange: a
}) {
  const i = (o) => {
    r || a({ ...e, value: o.target.value });
  };
  return /* @__PURE__ */ t.jsx(
    "textarea",
    {
      "aria-label": n,
      "aria-readonly": r,
      className: "studio-code-editor-input",
      readOnly: r,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      value: e.value,
      style: { minHeight: s },
      onChange: i
    }
  );
}
function g({
  document: e,
  diagnostics: r = [],
  readOnly: s = !1,
  theme: n = "studio",
  minHeight: a = "220px",
  ariaLabel: i,
  languageAdapter: o,
  onChange: l
}) {
  const h = r.filter((x) => !x.uri || x.uri === e.uri), C = o?.displayName ?? e.language, d = o?.loadEditor, p = E(
    () => d ? S(d) : null,
    [d]
  );
  return /* @__PURE__ */ t.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": n,
      "data-readonly": s,
      children: [
        /* @__PURE__ */ t.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ t.jsx("span", { children: C }),
          /* @__PURE__ */ t.jsx("code", { children: e.uri })
        ] }),
        p ? /* @__PURE__ */ t.jsx(R, { fallback: /* @__PURE__ */ t.jsx(
          j,
          {
            document: e,
            readOnly: s,
            minHeight: a,
            ariaLabel: i,
            onChange: l
          }
        ), children: /* @__PURE__ */ t.jsx(
          p,
          {
            document: e,
            readOnly: s,
            theme: n,
            minHeight: a,
            ariaLabel: i,
            onChange: l
          }
        ) }) : /* @__PURE__ */ t.jsx(
          j,
          {
            document: e,
            readOnly: s,
            minHeight: a,
            ariaLabel: i,
            onChange: l
          }
        ),
        /* @__PURE__ */ t.jsx(k, { diagnostics: h })
      ]
    }
  );
}
function k({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ t.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((r, s) => {
    const n = r.severity ?? "info", a = $(r);
    return /* @__PURE__ */ t.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${n}`,
        children: [
          r.code ? /* @__PURE__ */ t.jsx("span", { children: r.code }) : null,
          a ? /* @__PURE__ */ t.jsx("small", { children: a }) : null,
          r.message
        ]
      },
      `${r.uri ?? "document"}-${r.code ?? "diagnostic"}-${s}`
    );
  }) });
}
function $(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const _ = {
  language: "javascript",
  displayName: "JavaScript",
  async loadEditor() {
    return { default: (await import("./CodeMirrorStudioCodeEditor-CmsxsL0Q.js").then((r) => r.C)).CodeMirrorStudioCodeEditor };
  },
  async loadSupport() {
    return { language: "javascript" };
  }
}, y = "JavaScript";
function q(e) {
  e.expressionEditors.add({
    id: "elsa.javascript-expression-editor",
    order: 100,
    supports: (r) => r.syntax === y,
    surfaces: {
      inline: L,
      expanded: T
    }
  });
}
function L({ value: e, disabled: r, onChange: s }) {
  return /* @__PURE__ */ t.jsx(
    "textarea",
    {
      "aria-label": "JavaScript expression",
      className: "js-expression-editor inline",
      value: m(e),
      disabled: r,
      rows: 2,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      onChange: (n) => s(n.target.value)
    }
  );
}
function T({ descriptor: e, value: r, disabled: s, onChange: n }) {
  const a = {
    uri: `elsa://expressions/javascript/${encodeURIComponent(e.name || "expression")}`,
    language: "javascript",
    value: m(r)
  };
  return /* @__PURE__ */ t.jsxs("div", { className: "js-expression-expanded", children: [
    /* @__PURE__ */ t.jsx("div", { className: "js-expression-toolbar", "aria-hidden": "true", children: /* @__PURE__ */ t.jsx("span", { children: "JavaScript" }) }),
    /* @__PURE__ */ t.jsx(
      g,
      {
        ariaLabel: "JavaScript expanded expression",
        document: a,
        languageAdapter: _,
        minHeight: "260px",
        readOnly: s,
        theme: "dark",
        onChange: (i) => n(i.value)
      }
    )
  ] });
}
function m(e) {
  return e == null ? "" : String(e);
}
export {
  T as J,
  L as a,
  t as j,
  q as r
};
