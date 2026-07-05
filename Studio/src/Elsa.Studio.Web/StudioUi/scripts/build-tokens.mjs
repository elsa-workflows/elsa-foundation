/**
 * Emits `dist/tokens.css` for the published `@elsa-workflows/studio-ui` package.
 *
 * BACKGROUND — the two-layer token system (see `Client/src/app/ui/tokens.css`):
 *
 *   Layer 2 (`--studio-*`): the component-facing vocabulary the shipped primitives
 *     style against. It lives in `Client/src/app/ui/tokens.css` and is almost entirely
 *     *aliases* onto layer-1 primitives (e.g. `--studio-bg: var(--background)`).
 *   Layer 1 (`--background`, `--radius`, `--primary`, `--ring`, …): the raw theme
 *     palette + chrome scale. In-repo it lives in the host's `Client/src/app/styles.css`
 *     `:root` block and is (re)written per theme/mode by ThemeProvider at runtime.
 *
 * External consumers of the published package do NOT get the host's stylesheet, so the
 * alias layer alone would resolve to dangling `var(--background)` etc. This generator
 * makes the package self-contained:
 *
 *   1. Read the alias layer (`../Client/src/app/ui/tokens.css`).
 *   2. Collect every variable it references via `var(--xxx)` and resolve each one
 *      TRANSITIVELY against the host stylesheet's `:root` block (a snapshotted host value
 *      may itself contain `var(--x)` references).
 *   3. Emit `dist/tokens.css` = the alias layer verbatim, FOLLOWED by a documented
 *      `:root { … }` fallback block carrying the LIGHT-THEME snapshot values.
 *
 * ORDERING IS DELIBERATE: in the host, `tokens.css` is `@import`ed at the TOP of
 * `styles.css`, so the host's layer-1 `:root` block comes after the alias layer in the
 * cascade. For names defined in BOTH (the compatibility aliases `--muted`/`--accent`,
 * which shadow layer-1 names), the later layer-1 declaration wins in the host. The
 * emitted file mirrors that order — alias layer first, snapshot block after — so e.g.
 * `--studio-surface-muted: var(--muted)` resolves to the layer-1 muted SURFACE color,
 * not the compat alias's muted-TEXT color.
 *
 * The emitted values are a STATIC SNAPSHOT of the host's light theme, taken at build
 * time. Theming for external consumers = override the layer-1 variables (or the
 * `--studio-*` aliases) in a stylesheet loaded AFTER this one.
 *
 * Fails loudly (non-zero exit) if any referenced variable cannot be resolved from either
 * the alias layer or the host stylesheet, and re-verifies the final emitted file is
 * closed under `var()` lookups, rather than shipping a dangling `var()`.
 */
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const pkgRoot = resolve(__dirname, "..");

const ALIAS_LAYER_PATH = resolve(pkgRoot, "../Client/src/app/ui/tokens.css");
const HOST_STYLES_PATH = resolve(pkgRoot, "../Client/src/app/styles.css");
const OUT_PATH = resolve(pkgRoot, "dist/tokens.css");

/** Parse a `:root { … }` declaration block into an ordered [name, value] list of `--*` custom properties. */
function parseRootCustomProps(css) {
  const rootMatch = css.match(/:root\s*\{([\s\S]*?)\}/);
  if (!rootMatch) return [];
  const body = rootMatch[1];
  const props = [];
  // Match `--name: value;` declarations (value may itself contain `var(...)`, commas, etc.).
  const declRe = /(--[\w-]+)\s*:\s*([^;]+);/g;
  let m;
  while ((m = declRe.exec(body)) !== null) {
    props.push([m[1], m[2].trim()]);
  }
  return props;
}

/** Collect all custom-property NAMES defined anywhere in a stylesheet (any selector). */
function collectDefinedNames(css) {
  const names = new Set();
  const declRe = /(--[\w-]+)\s*:/g;
  let m;
  while ((m = declRe.exec(css)) !== null) names.add(m[1]);
  return names;
}

/** Collect all custom-property names REFERENCED via `var(--xxx)` in CSS text. */
function collectReferencedNames(css) {
  const names = new Set();
  const varRe = /var\(\s*(--[\w-]+)/g;
  let m;
  while ((m = varRe.exec(css)) !== null) names.add(m[1]);
  return names;
}

const aliasLayer = readFileSync(ALIAS_LAYER_PATH, "utf8");
const hostStyles = readFileSync(HOST_STYLES_PATH, "utf8");

const aliasDefined = collectDefinedNames(aliasLayer);

// Snapshot the host stylesheet's light-theme `:root` values.
const hostRootProps = parseRootCustomProps(hostStyles);
const hostRootMap = new Map(hostRootProps);

// Resolve every variable the alias layer references, TRANSITIVELY (a host value may
// itself reference further variables). Host `:root` values are snapshotted even for
// names the alias layer also defines (the compat aliases) — the snapshot block is
// emitted after the alias layer, so the host value wins, exactly as in the host cascade.
const resolvedMap = new Map();
const missing = new Set();
const worklist = [...collectReferencedNames(aliasLayer)];
while (worklist.length > 0) {
  const name = worklist.pop();
  if (resolvedMap.has(name) || missing.has(name)) continue;
  if (hostRootMap.has(name)) {
    const value = hostRootMap.get(name);
    resolvedMap.set(name, value);
    for (const dep of collectReferencedNames(value)) worklist.push(dep);
  } else if (!aliasDefined.has(name)) {
    // Defined in neither the alias layer nor the host root: unresolvable.
    missing.add(name);
  }
}

if (missing.size > 0) {
  console.error(
    `[build-tokens] Cannot resolve ${missing.size} variable(s) referenced (directly or ` +
      `transitively) by the alias layer from the host stylesheet (${HOST_STYLES_PATH}):\n` +
      [...missing].map((n) => `  - ${n}`).join("\n") +
      `\nEmitting a dangling var() would break external consumers; aborting.`
  );
  process.exit(1);
}

// Preserve the host stylesheet's declaration order for readability/diff-stability.
const resolved = [...resolvedMap.entries()];
const orderIndex = new Map(hostRootProps.map(([name], i) => [name, i]));
resolved.sort((a, b) => orderIndex.get(a[0]) - orderIndex.get(b[0]));

const header = `/*
 * @elsa-workflows/studio-ui — design tokens (self-contained).
 *
 * AUTO-GENERATED by scripts/build-tokens.mjs — do not edit by hand.
 *
 * Structure: the \`--studio-*\` alias layer (copied verbatim from the host app), followed
 * by a \`:root\` fallback block that is a STATIC SNAPSHOT of the host application's
 * LIGHT-THEME layer-1 variables (from Client/src/app/styles.css, taken at build time).
 * The snapshot exists so this file renders standalone: without it, the alias layer would
 * resolve to dangling var() references (the host stylesheet is not shipped in this
 * package). The snapshot comes AFTER the alias layer to mirror the host cascade, where
 * the layer-1 block follows the @imported alias layer and therefore wins for the
 * handful of names both define.
 *
 * THEMING: override the layer-1 variables (\`--background\`, \`--primary\`, \`--radius\`, …)
 * — or the \`--studio-*\` aliases directly — in a stylesheet loaded AFTER this one.
 * Because every \`--studio-*\` token aliases a layer-1 primitive, remapping layer 1
 * re-themes all primitives at once. Dark mode: set \`data-theme-mode="dark"\` on <html>
 * (the alias layer flips the chip foreground tokens for it) and supply dark layer-1
 * values.
 */`;

const fallbackHeader = `/*
 * Layer-1 light-theme snapshot (see file header). Override these to re-theme.
 */`;

const fallbackBlock =
  `${fallbackHeader}\n:root {\n` +
  resolved.map(([name, value]) => `  ${name}: ${value};`).join("\n") +
  `\n}\n`;

const output = `${header}\n\n${aliasLayer.trimStart()}\n${fallbackBlock}`;

// FINAL SELF-CONTAINMENT ASSERTION: parse the emitted file and verify every `var(--x)`
// it references is also defined within it. Belt-and-braces on top of the transitive
// resolution above — catches any future generator bug or host-stylesheet restructuring
// that would otherwise ship a dangling var() to external consumers.
const emittedDefined = collectDefinedNames(output);
const emittedDangling = [...collectReferencedNames(output)].filter(
  (name) => !emittedDefined.has(name)
);
if (emittedDangling.length > 0) {
  console.error(
    `[build-tokens] Emitted tokens.css would not be self-contained: ` +
      `${emittedDangling.length} var() reference(s) have no definition in the file:\n` +
      emittedDangling.map((n) => `  - ${n}`).join("\n") +
      `\nAborting without writing output.`
  );
  process.exit(1);
}

mkdirSync(dirname(OUT_PATH), { recursive: true });
writeFileSync(OUT_PATH, output, "utf8");

console.log(
  `[build-tokens] Wrote ${OUT_PATH} (alias layer + ${resolved.length} layer-1 snapshot values).`
);
