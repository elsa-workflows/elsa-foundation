import { useMemo as q, lazy as E, Suspense as R } from "react";
var x = { exports: {} }, l = {};
var f;
function C() {
  if (f) return l;
  f = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), s = /* @__PURE__ */ Symbol.for("react.fragment");
  function n(a, r, i) {
    var u = null;
    if (i !== void 0 && (u = "" + i), r.key !== void 0 && (u = "" + r.key), "key" in r) {
      i = {};
      for (var o in r)
        o !== "key" && (i[o] = r[o]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: a,
      key: u,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return l.Fragment = s, l.jsx = n, l.jsxs = n, l;
}
var j;
function N() {
  return j || (j = 1, x.exports = C()), x.exports;
}
var t = N();
function m({
  document: e,
  readOnly: s,
  minHeight: n,
  ariaLabel: a,
  onChange: r
}) {
  const i = (u) => {
    s || r({ ...e, value: u.target.value });
  };
  return /* @__PURE__ */ t.jsx(
    "textarea",
    {
      "aria-label": a,
      "aria-readonly": s,
      className: "studio-code-editor-input",
      readOnly: s,
      spellCheck: !1,
      autoCapitalize: "off",
      autoCorrect: "off",
      value: e.value,
      style: { minHeight: n },
      onChange: i
    }
  );
}
function L({
  document: e,
  diagnostics: s = [],
  readOnly: n = !1,
  theme: a = "studio",
  minHeight: r = "220px",
  ariaLabel: i,
  languageAdapter: u,
  onChange: o
}) {
  const v = s.filter((p) => !p.uri || p.uri === e.uri), h = u?.displayName ?? e.language, d = u?.loadEditor, c = q(
    () => d ? E(d) : null,
    [d]
  );
  return /* @__PURE__ */ t.jsxs(
    "section",
    {
      className: "studio-code-editor",
      "data-language": e.language,
      "data-theme": a,
      "data-readonly": n,
      children: [
        /* @__PURE__ */ t.jsxs("div", { className: "studio-code-editor-header", children: [
          /* @__PURE__ */ t.jsx("span", { children: h }),
          /* @__PURE__ */ t.jsx("code", { children: e.uri })
        ] }),
        c ? /* @__PURE__ */ t.jsx(R, { fallback: /* @__PURE__ */ t.jsx(
          m,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: o
          }
        ), children: /* @__PURE__ */ t.jsx(
          c,
          {
            document: e,
            readOnly: n,
            theme: a,
            minHeight: r,
            ariaLabel: i,
            onChange: o
          }
        ) }) : /* @__PURE__ */ t.jsx(
          m,
          {
            document: e,
            readOnly: n,
            minHeight: r,
            ariaLabel: i,
            onChange: o
          }
        ),
        /* @__PURE__ */ t.jsx(k, { diagnostics: v })
      ]
    }
  );
}
function k({ diagnostics: e }) {
  return e.length === 0 ? null : /* @__PURE__ */ t.jsx("div", { className: "studio-code-editor-diagnostics", role: "status", children: e.map((s, n) => {
    const a = s.severity ?? "info", r = $(s);
    return /* @__PURE__ */ t.jsxs(
      "p",
      {
        className: `studio-code-editor-diagnostic ${a}`,
        children: [
          s.code ? /* @__PURE__ */ t.jsx("span", { children: s.code }) : null,
          r ? /* @__PURE__ */ t.jsx("small", { children: r }) : null,
          s.message
        ]
      },
      `${s.uri ?? "document"}-${s.code ?? "diagnostic"}-${n}`
    );
  }) });
}
function $(e) {
  return e.startLineNumber ? e.startColumn ? `${e.startLineNumber}:${e.startColumn}` : String(e.startLineNumber) : null;
}
const g = "Liquid", S = {
  language: "liquid",
  displayName: "Liquid"
};
function b(e) {
  e.expressionEditors.add({
    id: "elsa.liquid-expression-editor",
    order: 110,
    supports: (s) => s.syntax === g,
    surfaces: {
      expanded: _
    }
  });
}
function _({ descriptor: e, value: s, disabled: n, onChange: a }) {
  const r = {
    uri: `elsa://expressions/liquid/${encodeURIComponent(e.name || "expression")}`,
    language: "liquid",
    value: T(s)
  };
  return /* @__PURE__ */ t.jsxs("div", { className: "liquid-expression-expanded", children: [
    /* @__PURE__ */ t.jsx("div", { className: "liquid-expression-toolbar", "aria-hidden": "true", children: /* @__PURE__ */ t.jsx("span", { children: "Liquid" }) }),
    /* @__PURE__ */ t.jsx(
      L,
      {
        ariaLabel: "Liquid expanded expression",
        document: r,
        languageAdapter: S,
        minHeight: "240px",
        readOnly: n,
        theme: "dark",
        onChange: (i) => a(i.value)
      }
    )
  ] });
}
function T(e) {
  return e == null ? "" : String(e);
}
export {
  _ as LiquidExpandedEditor,
  b as register
};
