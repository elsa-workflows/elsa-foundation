import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { resolve, join, sep } from "node:path";
import { scanJsonHashReferences } from "../lib/jsonHashReferences.mjs";

const repoRoot = resolve(import.meta.dirname, "../../..");
const fixtureParent = resolve(repoRoot, ".agent-prefs");
function fixture(files, run) {
  const root = mkdtempSync(join(fixtureParent, "s9-ts-hash-"));
  try {
    for (const [name, source] of Object.entries(files)) { const path = join(root, name); mkdirSync(resolve(path, ".."), { recursive: true }); writeFileSync(path, source); }
    return run(root);
  } finally {
    const target = resolve(root);
    assert.ok(target.startsWith(fixtureParent + sep));
    rmSync(target, { recursive: true, force: true });
  }
}
const gateway = "export function hashJson(value: unknown): string { return String(value); }";
function scan(root, names, extra = {}) {
  return scanJsonHashReferences({ repoRoot: root, rootNames: names.map(name => join(root, name)), gateways: [{ file: join(root, "gateway.ts"), exports: ["hashJson"] }], ...extra });
}

test("renamed, namespace, local and destructured aliases resolve through barrels", () => fixture({
  "gateway.ts": gateway,
  "barrel.ts": 'export { hashJson } from "./gateway";',
  "caller.ts": `import { hashJson as imported } from "./barrel"; import * as sdk from "./barrel";
    const local = imported; const { hashJson: destructured } = sdk;
    export function run(value: unknown) { imported(value); sdk.hashJson(value); local(value); destructured(value); sdk["hashJson"](value); }`
}, root => {
  const result = scan(root, ["gateway.ts", "barrel.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  const calls = result.references.filter(item => item.file === "caller.ts" && item.kind === "call");
  assert.equal(calls.length, 5);
  assert.equal(new Set(calls.map(item => item.target)).size, 1);
  assert.ok(result.moduleEdges.some(edge => edge.file === "barrel.ts" && edge.exported === "hashJson"));
}));

test("assigned aliases and method groups remain visible while same-name functions stay ordinary", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway";
    let digest: typeof hashJson; digest = hashJson;
    function unrelated(hashJson: (x: unknown) => string) { return hashJson(1); }
    export function run(x: unknown) { const handlers = [hashJson]; return digest(x); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.ok(result.references.some(item => item.owner === "run" && item.kind === "call"));
  assert.ok(result.references.some(item => item.owner === "run" && item.kind === "reference"));
  assert.ok(!result.references.some(item => item.owner === "unrelated"));
}));

test("actual Web Crypto digest resolves independently of source spelling", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `export async function run(bytes: Uint8Array<ArrayBuffer>) {
    const { digest } = crypto.subtle;
    return [crypto.subtle.digest("SHA-256", bytes), globalThis.crypto.subtle["digest"]("SHA-256", bytes), digest("SHA-256", bytes)];
  }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 3);
}));

test("computed crypto access and missing module resolution fail explicitly", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { missing } from "./not-present";
    export function run(key: keyof SubtleCrypto) { return crypto.subtle[key]; }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.ok(result.errors.some(error => error.code === "dynamic-hash-access"));
  assert.ok(result.errors.some(error => error.code === "typescript-2307"));
}));

test("missing required export cannot disappear from the inventory", () => fixture({
  "gateway.ts": "export const renamed = 1;"
}, root => assert.ok(scan(root, ["gateway.ts"]).errors.some(error => error.code === "missing-hash-export"))));

test("an unresolved named import is a compiler finding even when the module itself exists", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": 'import { missingHash } from "./gateway"; export const run = () => missingHash({});'
}, root => assert.ok(scan(root, ["gateway.ts", "caller.ts"]).errors.some(error => error.code === "typescript-2305"))));

test("object-property and destructured function aliases retain their actual targets", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const box = { hash: hashJson }; const { hash: h } = box;
    export function run(x: unknown) { box.hash(x); h(x); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 2);
}));

test("type-erased crypto receivers retain origin for static calls and fail closed for dynamic keys", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
    const subtle: any = crypto.subtle;
    const cast = crypto.subtle as unknown as Record<string, (a: string, b: Uint8Array<ArrayBuffer>) => Promise<ArrayBuffer>>;
    subtle.digest("SHA-256", bytes); cast.digest("SHA-256", bytes);
    subtle[key]("SHA-256", bytes); cast[key]("SHA-256", bytes);
  }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("ordinary same-name local objects do not become crypto authorities", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const fake: any = { digest: async () => new ArrayBuffer(0) };
    export function run(key: string) { fake.digest("SHA-256", new Uint8Array()); return fake[key](); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.references, []);
}));

test("definition names are not references", () => fixture({
  "gateway.ts": gateway
}, root => assert.deepEqual(scan(root, ["gateway.ts"]).references, [])));

test("a genuine recursive call keeps its gateway reference", () => fixture({
  "gateway.ts": 'export function hashJson(value: unknown): string { return value ? hashJson(null) : ""; }'
}, root => {
  const result = scan(root, ["gateway.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.length, 1);
  assert.equal(result.references[0].kind, "call");
}));

test("global compiler errors cannot be hidden by an otherwise resolved hash export", () => fixture({
  "gateway.ts": "export function hashJson(value: number): number { return value; }",
  "caller.ts": 'import { hashJson } from "./gateway"; export const run = () => hashJson(1);'
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"], { options: { noLib: true, lib: undefined } });
  assert.ok(result.errors.some(error => error.code === "typescript-2318"));
}));

test("erased destructured crypto receivers retain their original authority", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const { subtle: erased } = crypto as any;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) { erased.digest("SHA-256", bytes); erased[key]("SHA-256", bytes); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 1);
  assert.ok(result.errors.some(error => error.code === "dynamic-hash-access"));
}));

test("shorthand, nested bindings and satisfies wrappers retain actual function targets", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const box = { hashJson }; const { hashJson: h } = box;
    const { nested: { hash: nested } } = { nested: { hash: hashJson } };
    const checked = (hashJson satisfies typeof hashJson);
    export function run(x: unknown) { box.hashJson(x); h(x); nested(x); checked(x); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 4);
}));

test("ordinary erased destructuring and shorthand do not inherit native or gateway names", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const { subtle: erased } = { subtle: { digest: () => 1 } } as any;
    function hashJson(x: unknown) { return x; } const box = { hashJson }; const { hashJson: h } = box;
    export function run(key: string) { erased.digest(); erased[key](); box.hashJson(1); h(1); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.references, []);
}));
