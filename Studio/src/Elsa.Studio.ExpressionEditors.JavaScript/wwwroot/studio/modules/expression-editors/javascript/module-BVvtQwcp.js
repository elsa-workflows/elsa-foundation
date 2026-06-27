import { lazy as C, Suspense as E } from "react";
var d = { exports: {} }, l = {};
var p;
function S() {
  if (p) return l;
  p = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), r = /* @__PURE__ */ Symbol.for("react.fragment");
  function s(i, a, n) {
    var o = null;
    if (n !== void 0 && (o = "" + n), a.key !== void 0 && (o = "" + a.key), "key" in a) {
      n = {};
      for (var u in a)
        u !== "key" && (n[u] = a[u]);
    } else n = a;
    return a = n.ref, {
      $$typeof: e,
      type: i,
      key: o,
      ref: a !== void 0 ? a : null,
      props: n
    };
  }
  return l.Fragment = r, l.jsx = s, l.jsxs = s, l;
}
var x;
function R() {
  return x || (x = 1, d.exports = S()), d.exports;
}
var t = R();
function v({
  document: e,
  readOnly: r,
  minHeight: s,
  ariaLabel: i,
  onChange: a
}) {
  const n = (o) => {
    r || a({ ...e, value: o.target.value });
  };
  return /* @__PURE__ */ t.jsx(
    "textarea",
    {
      "aria-label": i,
      "aria-readonly": r,
      className: "studio-code-editor-input",
      readOnly: r,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      value: e.value,
      style: { minHeight: s },
      onChange: n
    }
  );
}
const f = {
  language: "javascript",
  displayName: "JavaScript",
  async loadSupport() {
    return { language: "javascript" };
  }
};
function J(e) {
  const r = e.trim().toLowerCase();
  return r === "javascript" || r === "typescript" ? f : null;
}
const N = C(() => import("./CodeMirrorStudioCodeEditor-DXWjxCLM.js").then((e) => e.C).then((e) => ({
  default: e.CodeMirrorStudioCodeEditor
})));
function k({
  document: e,
  diagnostics: r = [],
  readOnly: s = !1,
  theme: i = "studio",
  minHeight: a = "220px",
  ariaLabel: n,
  languageAdapter: o,
  onChange: u
}) {
  const h = r.filter((c) => !c.uri || c.uri === e.uri), m = (o ?? J(e.language))?.displayName ?? e.language, g = L(e.language);
  return /* @__PURE__ */ t.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": i,
      "data-readonly": s,
      children: [
        /* @__PURE__ */ t.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ t.jsx("span", { children: m }),
          /* @__PURE__ */ t.jsx("code", { children: e.uri })
        ] }),
        g ? /* @__PURE__ */ t.jsx(E, { fallback: /* @__PURE__ */ t.jsx(
          v,
          {
            document: e,
            readOnly: s,
            minHeight: a,
            ariaLabel: n,
            onChange: u
          }
        ), children: /* @__PURE__ */ t.jsx(
          N,
          {
            document: e,
            readOnly: s,
            theme: i,
            minHeight: a,
            ariaLabel: n,
            onChange: u
          }
        ) }) : /* @__PURE__ */ t.jsx(
          v,
          {
            document: e,
            readOnly: s,
            minHeight: a,
            ariaLabel: n,
            onChange: u
          }
        ),
        /* @__PURE__ */ t.jsx($, { diagnostics: h })
      ]
    }
  );
}
function L(e) {
  const r = e.trim().toLowerCase();
  return r === "javascript" || r === "typescript";
}
function $({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ t.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((r, s) => {
    const i = r.severity ?? "info", a = A(r);
    return /* @__PURE__ */ t.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${i}`,
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
function A(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const y = "JavaScript";
function q(e) {
  e.expressionEditors.add({
    id: "elsa.javascript-expression-editor",
    order: 100,
    supports: (r) => r.syntax === y,
    surfaces: {
      inline: _,
      expanded: T
    }
  });
}
function _({ value: e, disabled: r, onChange: s }) {
  return /* @__PURE__ */ t.jsx(
    "textarea",
    {
      "aria-label": "JavaScript expression",
      className: "js-expression-editor inline",
      value: j(e),
      disabled: r,
      rows: 2,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      onChange: (i) => s(i.target.value)
    }
  );
}
function T({ descriptor: e, value: r, disabled: s, onChange: i }) {
  const a = {
    uri: `elsa://expressions/javascript/${encodeURIComponent(e.name || "expression")}`,
    language: "javascript",
    value: j(r)
  };
  return /* @__PURE__ */ t.jsxs("div", { className: "js-expression-expanded", children: [
    /* @__PURE__ */ t.jsx("div", { className: "js-expression-toolbar", "aria-hidden": "true", children: /* @__PURE__ */ t.jsx("span", { children: "JavaScript" }) }),
    /* @__PURE__ */ t.jsx(
      k,
      {
        ariaLabel: "JavaScript expanded expression",
        document: a,
        languageAdapter: f,
        minHeight: "260px",
        readOnly: s,
        theme: "dark",
        onChange: (n) => i(n.value)
      }
    )
  ] });
}
function j(e) {
  return e == null ? "" : String(e);
}
export {
  T as J,
  _ as a,
  t as j,
  q as r
};
