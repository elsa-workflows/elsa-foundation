/**
 * Local stylelint rule: `elsa/no-raw-color-literal`.
 *
 * Bans raw colour literals (hex, `rgb()/rgba()`, `hsl()/hsla()`, `hwb()`, `oklch()`,
 * `oklab()`, `lab()`, `lch()`) in the colour-painting declarations of MODULE CSS, so every
 * module resolves its colours through the `--studio-*` token contract (or a module-private
 * alias onto it) rather than hard-coding a value that ignores the active theme. See
 * `docs/design-tokens.md` for the token contract this rule enforces.
 *
 * A literal is allowed only where it is a legitimate composition primitive:
 *   - inside `var(--token, <fallback>)` — the fallback is a last-resort default, not chrome;
 *   - inside `color-mix(…)` — the mixed-in base/anchor is part of a token-derived recipe;
 *   - keywords `transparent` / `currentColor` / `inherit` / `unset` / `initial` / `none`.
 * Gradients and `drop-shadow()` are treated as TRANSPARENT wrappers: their colour stops are
 * scanned, so `linear-gradient(180deg, var(--a), var(--b))` passes but a gradient built from
 * raw hex stops is still flagged.
 *
 * Options (primary): `true`.
 * Secondary (optional):
 *   `ignoreSelectors`: string|RegExp|Array — decls whose owning rule selector matches are
 *     skipped. Use for documented fixed-palette exceptions (e.g. the ConsoleStream ANSI
 *     colour table, whose values map terminal SGR codes to fixed sRGB and are not themeable).
 *
 * This is a from-scratch rule rather than `declaration-strict-value` because that plugin
 * requires the WHOLE value to be a single var()/keyword and so rejects the legitimate
 * `color-mix()`, gradient-of-tokens, and multi-part `box-shadow` recipes the modules use.
 */
import stylelint from "stylelint";

const { createPlugin, utils } = stylelint;

const ruleName = "elsa/no-raw-color-literal";

const messages = utils.ruleMessages(ruleName, {
  rejected: (prop, literal) =>
    `Unexpected raw color literal "${literal}" in "${prop}". ` +
    `Use a --studio-* token (or a module-private alias onto one) instead — ` +
    `see docs/design-tokens.md.`,
});

const meta = {
  url: "https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/docs/design-tokens.md",
};

// Declarations that paint colour and must therefore resolve through tokens. Custom
// properties are intentionally NOT matched by name: a module-private alias block (the
// documented pattern, e.g. `--otel-*`) legitimately maps literals onto the token contract,
// and per-module private aliases carry arbitrary names. The rule targets the concrete
// paint properties instead, where a raw literal actually escapes the token layer.
const COLOR_PROP =
  /^(color|background|background-color|border|border-color|border-(?:top|right|bottom|left|block|inline|block-start|block-end|inline-start|inline-end)(?:-color)?|box-shadow|outline|outline-color|fill|stroke|text-decoration-color|caret-color|column-rule-color|-webkit-text-fill-color)$/i;

// A raw colour literal: a hex triplet/quad/sextet/octet, or a colour function opening.
// The trailing `\([^)]*\)?` captures the call's arguments for a readable message.
const LITERAL =
  /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|hwb|oklch|oklab|lab|lch)\([^)]*\)?/i;

// `var()` fallbacks and `color-mix()` bases legitimately carry literals — drop the call whole.
const OPAQUE_FN = /\b(?:var|color-mix)\(/i;
// Gradients / drop-shadow are transparent wrappers — unwrap and keep scanning their stops.
const WRAPPER_FN =
  /\b(?:linear-gradient|radial-gradient|conic-gradient|repeating-linear-gradient|repeating-radial-gradient|repeating-conic-gradient|drop-shadow)\(/i;

/** Index of the ')' matching the '(' at `openIdx` (returns the last index if unbalanced). */
function matchParen(value, openIdx) {
  let depth = 0;
  for (let j = openIdx; j < value.length; j++) {
    if (value[j] === "(") depth++;
    else if (value[j] === ")" && --depth === 0) return j;
  }
  return value.length - 1;
}

/** Drop opaque fns (var/color-mix) whole; unwrap gradient/drop-shadow and recurse into stops. */
function normalize(value) {
  let out = "";
  let i = 0;
  while (i < value.length) {
    const rest = value.slice(i);
    const opaque = rest.match(OPAQUE_FN);
    const wrap = rest.match(WRAPPER_FN);
    let match = null;
    let kind = null;
    if (opaque && (!wrap || opaque.index <= wrap.index)) {
      match = opaque;
      kind = "opaque";
    } else if (wrap) {
      match = wrap;
      kind = "wrap";
    }
    if (!match) {
      out += rest;
      break;
    }
    out += rest.slice(0, match.index);
    const openIdx = i + match.index + match[0].length - 1;
    const closeIdx = matchParen(value, openIdx);
    out +=
      kind === "opaque"
        ? " "
        : " " + normalize(value.slice(openIdx + 1, closeIdx)) + " ";
    i = closeIdx + 1;
  }
  return out;
}

/** Coerce a string|RegExp|Array secondary option into an array of RegExp. */
function toRegexes(value) {
  if (value == null) return [];
  return (Array.isArray(value) ? value : [value]).map((entry) => {
    if (entry instanceof RegExp) return entry;
    const literal = /^\/(.*)\/([a-z]*)$/i.exec(String(entry));
    return literal ? new RegExp(literal[1], literal[2]) : new RegExp(String(entry));
  });
}

const rule = (primary, secondary) => (root, result) => {
  const valid = utils.validateOptions(
    result,
    ruleName,
    { actual: primary },
    {
      actual: secondary,
      optional: true,
      possible: {
        ignoreSelectors: [(value) => typeof value === "string" || value instanceof RegExp],
      },
    }
  );
  if (!valid) return;

  const ignoreSelectors = toRegexes(secondary?.ignoreSelectors);

  root.walkDecls((decl) => {
    if (!COLOR_PROP.test(decl.prop)) return;

    const parent = decl.parent;
    const selector = parent && parent.type === "rule" ? parent.selector : "";
    if (selector && ignoreSelectors.some((re) => re.test(selector))) return;

    const hit = normalize(decl.value).match(LITERAL);
    if (!hit) return;

    utils.report({
      ruleName,
      result,
      node: decl,
      word: hit[0],
      message: messages.rejected(decl.prop, hit[0].trim()),
    });
  });
};

rule.ruleName = ruleName;
rule.messages = messages;
rule.meta = meta;

export default createPlugin(ruleName, rule);
