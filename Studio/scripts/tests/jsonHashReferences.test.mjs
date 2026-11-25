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

test("cyclic erased bindings terminate and preserve a reachable crypto origin", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `let holder: any = {}; const { next } = holder; holder = next;
    let tracked: any = { next: crypto.subtle }; const { next: actual } = tracked; tracked = actual;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      actual.digest("SHA-256", bytes); actual[key]("SHA-256", bytes); return next;
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 1);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 1);
}));

test("array bindings, erased shorthand copies and defaults retain crypto origins", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const [array] = [crypto.subtle] as any;
    const { subtle: erased } = crypto as any; const box = { erased };
    const { subtle: fallback = crypto.subtle } = {} as any;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      array.digest("SHA-256", bytes); array[key]("SHA-256", bytes);
      box.erased.digest("SHA-256", bytes); box.erased[key]("SHA-256", bytes);
      fallback.digest("SHA-256", bytes); fallback[key]("SHA-256", bytes);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 3);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 3);
}));

test("array and default function bindings preserve gateway identity", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const [array] = [hashJson];
    const { hash: fallback = hashJson } = {} as { hash?: typeof hashJson };
    export function run(x: unknown) { array(x); fallback(x); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 2);
}));

test("numeric receiver access and assigned erased members preserve crypto authority", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const bucket: any = [crypto.subtle]; const box: any = {}; const alias = box;
    box.receiver = crypto.subtle;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      bucket[0].digest("SHA-256", bytes); bucket[0][key]("SHA-256", bytes);
      alias.receiver.digest("SHA-256", bytes); alias.receiver[key]("SHA-256", bytes);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("ordinary numeric and assigned receiver members remain ordinary", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const bucket: any = [{ digest: () => 1 }]; const box: any = {}; box.receiver = bucket[0];
    export function run(key: string) { bucket[0].digest(); bucket[0][key](); box.receiver.digest(); box.receiver[key](); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.references, []);
}));

test("array access and erased function aliases preserve their caller edges", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const list = [hashJson]; const erased: any = hashJson;
    const box: any = {}; box.hash = hashJson;
    export function run(x: unknown) { list[0](x); list["0"](x); erased(x); box.hash(x); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 4);
}));

test("destructured receiver paths retain writes in both directions", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const box: any = { nested: {} }; const { nested } = box; nested.receiver = crypto.subtle;
    const reverse: any = { nested: {} }; reverse.nested.receiver = crypto.subtle; const { nested: copy } = reverse;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      box.nested.receiver.digest("SHA-256", bytes); box.nested.receiver[key]("SHA-256", bytes);
      copy.receiver.digest("SHA-256", bytes); copy.receiver[key]("SHA-256", bytes);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("dynamic selection fails closed for crypto arrays and leaves ordinary arrays alone", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const bucket: any = [crypto.subtle]; const ordinary: any = [{ digest: () => 1 }];
    export function run(index: number, key: string, bytes: Uint8Array<ArrayBuffer>) {
      bucket[index].digest("SHA-256", bytes); bucket[index][key]("SHA-256", bytes);
      ordinary[index].digest(); ordinary[index][key]();
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
  assert.ok(result.errors.every(item => item.code === "dynamic-hash-access"));
}));

test("contained and conditional receiver aliases preserve object identity", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const inner: any = {}; const box: any = { inner }; box.inner.receiver = crypto.subtle;
    const reverse: any = {}; const list: any = [reverse]; const [nested] = list; nested.receiver = crypto.subtle;
    const first: any = {}, second: any = {}; const alias = Date.now() ? first : second; alias.receiver = crypto.subtle;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      inner.receiver.digest("SHA-256", bytes); inner.receiver[key]("SHA-256", bytes);
      reverse.receiver.digest("SHA-256", bytes); reverse.receiver[key]("SHA-256", bytes);
      first.receiver.digest("SHA-256", bytes); first.receiver[key]("SHA-256", bytes);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 3);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 3);
}));

test("destructuring and logical assignments retain crypto and function origins", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const box: any = { nested: crypto.subtle };
    let nested: any; ({ nested } = box); let receiver: any; receiver ??= crypto.subtle;
    let f: any; [f] = [hashJson]; let h: any; h ||= hashJson;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      nested.digest("SHA-256", bytes); nested[key]("SHA-256", bytes);
      receiver.digest("SHA-256", bytes); receiver[key]("SHA-256", bytes); f(1); h(1);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 2);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("logical values and shorthand assignment defaults preserve crypto origins", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `let fallback: any; ({ fallback = crypto.subtle } = {} as any);
    export function run(maybe: any, bytes: Uint8Array<ArrayBuffer>, key: string) {
      const receiver: any = maybe ?? crypto.subtle;
      receiver.digest("SHA-256", bytes); receiver[key]("SHA-256", bytes);
      fallback.digest("SHA-256", bytes); fallback[key]("SHA-256", bytes);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("nested contained bindings and object or array spreads retain origins", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; const inner: any = {}; const box: any = { middle: { inner } };
    const { middle: { inner: copy } } = box; copy.receiver = crypto.subtle;
    const source: any = { receiver: crypto.subtle }; const spread: any = { ...source };
    const list = [...[hashJson]];
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      inner.receiver.digest("SHA-256", bytes); inner.receiver[key]("SHA-256", bytes);
      spread.receiver.digest("SHA-256", bytes); spread.receiver[key]("SHA-256", bytes); list[0](1);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 2);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 1);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 2);
}));

test("nested binding cycles terminate without inventing a hash origin", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `let holder: any = { next: {} }; const { next: { receiver } } = holder;
    holder = receiver; export const run = () => receiver;`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.references, []);
}));

test("a later definite spread overrides earlier crypto members", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const real = { digest: crypto.subtle.digest }; const fake = { digest: () => 1 };
    const box = { ...real, ...fake };
    const actual = { receiver: crypto.subtle }; const ordinary = { receiver: { digest: () => 1 } };
    const replaced: any = { ...actual, ...ordinary };
    export function run(key: string) { box.digest(); replaced.receiver.digest(); replaced.receiver[key](); }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.ok(!result.references.some(item => item.owner === "run"));
}));

test("an exhausted resolution budget fails closed without a partial accepted inventory", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; export const run = () => hashJson({ value: 1 });`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"], { maxResolutionSteps: 1 });
  assert.equal(result.errors.length, 1);
  assert.equal(result.errors[0].code, "hash-analysis-budget");
  assert.deepEqual(result.references, []);
  assert.deepEqual(result.moduleEdges, []);
}));

test("typed receiver writes do not spread authority to a different instance", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `interface Box { receiver?: any; } const real: Box = {}, ordinary: Box = {};
    real.receiver = crypto.subtle; ordinary.receiver = { digest: () => 1 };
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      real.receiver.digest("SHA-256", bytes); real.receiver[key]("SHA-256", bytes);
      ordinary.receiver.digest(); ordinary.receiver[key]();
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 1);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 1);
}));

test("class field and method replacements remain scoped to their receiver", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; class Box { receiver?: any; hash(x: unknown): any { return x; } }
    const real = new Box(), ordinary = new Box(); real.receiver = crypto.subtle; ordinary.receiver = { digest: () => 1 };
    real.hash = hashJson; ordinary.hash = x => x;
    export function run(bytes: Uint8Array<ArrayBuffer>, key: string) {
      real.receiver.digest("SHA-256", bytes); real.receiver[key]("SHA-256", bytes);
      ordinary.receiver.digest(); ordinary.receiver[key](); real.hash(1); ordinary.hash(1);
    }`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 1);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 1);
  assert.equal(result.errors.filter(item => item.code === "dynamic-hash-access").length, 1);
}));

test("an unbound class receiver write fails closed", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `class Box { receiver: any; constructor() { this.receiver = crypto.subtle; } }
    const box = new Box(); export const run = () => box.receiver.digest("SHA-256", new Uint8Array());`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.ok(result.errors.some(error => error.code === "unresolved-hash-receiver"));
}));

test("unbound receiver checks use complete member origins and retain this through aliases", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `const holder: any = {};
    class Late { receiver: any; constructor() { this.receiver = holder.receiver; } }
    class Aliased { receiver: any; constructor() { const self = this; self.receiver = crypto.subtle; } }
    holder.receiver = crypto.subtle;
    const late = new Late(), aliased = new Aliased();
    export const run = () => [late.receiver.digest("SHA-256", new Uint8Array()), aliased.receiver.digest("SHA-256", new Uint8Array())];`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.errors.filter(error => error.code === "unresolved-hash-receiver").length, 2);
}));

test("diamond alias graphs preserve their authority within a bounded work budget", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway"; declare const flag: boolean; const h0 = hashJson;
    ${Array.from({ length: 16 }, (_, index) => `const h${index + 1} = flag ? h${index} : h${index};`).join("\n")}
    export const run = () => h16(1);`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"], { maxResolutionSteps: 20_000 });
  assert.deepEqual(result.errors, []);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 1);
}));

test("parameter defaults retain crypto, gateway and unresolved this origins", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `import { hashJson } from "./gateway";
    export function run(key: string, receiver: any = crypto.subtle, h: any = hashJson) {
      receiver.digest("SHA-256", new Uint8Array()); receiver[key]("SHA-256", new Uint8Array()); h(1);
    }
    export function ordinary(key: string, receiver: any = { digest: () => 1 }) { receiver.digest(); receiver[key](); }
    class Box { receiver: any; constructor(self = this) { self.receiver = crypto.subtle; } }
    export const instance = new Box();`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.equal(result.references.filter(item => item.kind === "call" && item.role === "digest-primitive").length, 1);
  assert.equal(result.references.filter(item => item.owner === "run" && item.kind === "call" && item.role === "json-hash-input").length, 1);
  assert.equal(result.errors.filter(error => error.code === "dynamic-hash-access").length, 1);
  assert.equal(result.errors.filter(error => error.code === "unresolved-hash-receiver").length, 1);
  assert.ok(!result.references.some(item => item.owner === "ordinary"));
}));

test("native crypto ingress remains visible through an unresolved returned value", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `function opaque(): any { return crypto.subtle; }
    export const run = () => opaque().digest("SHA-256", new Uint8Array());`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.ok(result.references.some(item => item.owner === "opaque" && item.role === "crypto-authority"));
  // The inventory retains the authority owner; it does not pretend to infer arbitrary return-value flow.
  assert.ok(!result.references.some(item => item.owner === "run" && item.role === "digest-primitive"));
}));

test("type-only crypto aliases, interfaces and queries are not value ingress", () => fixture({
  "gateway.ts": gateway,
  "caller.ts": `export type Only = Crypto; export interface Holder { value: SubtleCrypto; }
    export type Query = typeof globalThis.crypto; export class Implemented implements Partial<Crypto> {}
    declare const typed: Crypto; export const run = () => 1;`
}, root => {
  const result = scan(root, ["gateway.ts", "caller.ts"]);
  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.references, []);
}));
