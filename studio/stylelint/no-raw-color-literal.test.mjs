/**
 * Smoke test for the `elsa/no-raw-color-literal` rule. Run with `node`:
 *
 *   node stylelint/no-raw-color-literal.test.mjs
 *
 * Kept dependency-light (stylelint's own node API + `node:assert`) so it needs no vitest
 * harness at the repo root. Exits non-zero on the first failing case.
 */
/* global console, process */
import assert from "node:assert/strict";
import stylelint from "stylelint";

const config = {
  plugins: ["./stylelint/no-raw-color-literal.mjs"],
  rules: {
    "elsa/no-raw-color-literal": [true, { ignoreSelectors: ["/console-stream-ansi/"] }],
  },
};

// [css, expectViolation, label]
const cases = [
  [".a { color: var(--studio-text); }", false, "token reference passes"],
  [".a { color: var(--studio-text, #123456); }", false, "literal as var() fallback passes"],
  [
    ".a { background: color-mix(in srgb, var(--studio-accent) 10%, transparent); }",
    false,
    "color-mix over tokens passes",
  ],
  [
    ".a { background: linear-gradient(180deg, var(--a), var(--b)); }",
    false,
    "gradient of tokens passes",
  ],
  [
    ".a { background: linear-gradient(180deg, #fff, #000); }",
    true,
    "gradient with raw hex stops is flagged",
  ],
  [".a { color: transparent; border-color: currentColor; }", false, "keywords pass"],
  [".a { color: #ff0000; }", true, "bare hex is flagged"],
  [".a { background: rgba(0, 0, 0, 0.5); }", true, "bare rgba() is flagged"],
  [".a { color: oklch(0.5 0.1 20); }", true, "bare oklch() is flagged"],
  [
    ".a { box-shadow: 0 0 0 3px color-mix(in srgb, var(--x) 18%, transparent), 0 14px 30px rgba(0,0,0,.2); }",
    true,
    "trailing raw rgba() in a multi-part box-shadow is flagged",
  ],
  [
    ":root { --otel-surface: oklch(1 0 0); }",
    false,
    "custom-property alias definitions are not paint props (private-alias-block pattern)",
  ],
  [
    ".console-stream-ansi-fg-red { color: #c0392b; }",
    false,
    "documented ANSI palette exempt via ignoreSelectors",
  ],
  [".a { width: 100px; margin: 10px; }", false, "non-colour properties pass"],
];

let failed = 0;
for (const [code, expectViolation, label] of cases) {
  const { results } = await stylelint.lint({ code, config });
  const hits = results[0].warnings.filter((w) => w.rule === "elsa/no-raw-color-literal").length;
  try {
    if (expectViolation) assert.ok(hits >= 1, `expected a violation`);
    else assert.equal(hits, 0, `expected no violation`);
    console.log(`PASS  ${label}`);
  } catch (err) {
    failed++;
    console.error(`FAIL  ${label} — got ${hits} violation(s): ${err.message}`);
  }
}

console.log(`\n${cases.length - failed}/${cases.length} passed`);
if (failed) process.exit(1);
