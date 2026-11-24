import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { join, resolve, sep } from "node:path";
import { scanJsonHashReferences } from "../lib/jsonHashReferences.mjs";
import { createJsonHashReceipt, shippingTypeScriptSources, sourceDocument, validateJsonHashReview } from "../lib/jsonHashReceipt.mjs";
import { physicalModuleInputs, studioHashBuildInputs } from "../lib/jsonHashBuildInputs.mjs";

const parent = resolve(import.meta.dirname, "../../../.agent-prefs");
function fixture(run) {
  const root = mkdtempSync(join(parent, "s9-ts-recipe-"));
  const files = {
    "gateway.ts": "export const hashJson = (value: unknown) => String(value);",
    "payload.ts": "export class Payload { get value() { return 41; } }",
    "caller.ts": 'import { hashJson } from "./gateway"; import { Payload } from "./payload"; export const run = () => hashJson(new Payload());'
  };
  const write = (file, text) => { files[file] = text; writeFileSync(join(root, file), text); };
  for (const [file, text] of Object.entries(files)) write(file, text);
  write("golden.txt", "Independent actual-owner fixed pin.");
  const scan = () => {
    const rootNames = Object.keys(files).filter(file => file.endsWith(".ts")).map(file => join(root, file));
    const result = scanJsonHashReferences({ repoRoot: root, rootNames, gateways: [{ file: join(root, "gateway.ts"), exports: ["hashJson"] }] });
    return createJsonHashReceipt({ repoRoot: root, rootNames, ...result, authorities: ["gateway.ts"] });
  };
  const review = receipt => ({ ...receipt, owners: receipt.owners.map(owner => ({ ...owner, classification: "json_routed", reason: "Owned Payload projection through the canonical gateway.", goldenEvidence: ["golden.txt"] })) });
  try { run({ root, files, write, scan, review }); }
  finally { assert.ok(resolve(root).startsWith(parent + sep)); rmSync(root, { recursive: true }); }
}

test("unchanged reviewed inputs pass and a separate-file getter cannot borrow their receipt", () => fixture(({ root, files, write, scan, review }) => {
  const original = scan(), register = review(original);
  assert.deepEqual(validateJsonHashReview(original, register, root), []);
  write("payload.ts", files["payload.ts"].replace("41", "42"));
  assert.ok(validateJsonHashReview(scan(), register, root).some(finding => finding.includes("sourceSignature")));
}));

test("a new owner and a second site inside an existing owner both require review", () => fixture(({ root, files, write, scan, review }) => {
  const original = scan(), register = review(original);
  write("caller.ts", files["caller.ts"] + " export const later = () => hashJson(3);");
  assert.ok(validateJsonHashReview(scan(), register, root).some(finding => finding.includes("Unreviewed")));
  write("caller.ts", files["caller.ts"].replace("hashJson(new Payload())", "[hashJson(new Payload()), hashJson(4)]"));
  assert.ok(validateJsonHashReview(scan(), register, root).some(finding => finding.includes("Changed TypeScript hash references")));
}));

test("a primitive added to an existing JSON owner cannot use its routed classification", () => fixture(({ root, files, write, scan, review }) => {
  const register = review(scan());
  write("caller.ts", files["caller.ts"].replace("hashJson(new Payload())", '(crypto.subtle.digest("SHA-256", new Uint8Array()), hashJson(new Payload()))'));
  assert.ok(validateJsonHashReview(scan(), register, root).some(finding => finding.includes("primitive cannot borrow")));
}));

test("authority claims, empty reasons, duplicate owners and missing golden links refuse", () => fixture(({ root, scan, review }) => {
  const receipt = scan(), register = review(receipt);
  for (const patch of [{ classification: "shared_json_authority" }, { reason: "" }, { goldenEvidence: [] }, { goldenEvidence: ["absent.txt"] }, { goldenEvidence: ["."] }]) {
    const changed = { ...register, owners: register.owners.map(owner => ({ ...owner, ...patch })) };
    assert.notDeepEqual(validateJsonHashReview(receipt, changed, root), []);
  }
  assert.ok(validateJsonHashReview(receipt, { ...register, owners: [...register.owners, register.owners[0]] }, root).some(finding => finding.includes("Duplicate")));
}));

test("anonymous default-function owner identities are portable across checkout line endings", () => fixture(({ root, files, write, scan, review }) => {
  write("caller.ts", 'import { hashJson } from "./gateway";\nconst value = 1;\nexport default function(){return hashJson(value);}');
  const original = scan(), register = review(original);
  write("caller.ts", files["caller.ts"].replace(/\n/g, "\r\n"));
  assert.deepEqual(validateJsonHashReview(scan(), register, root), []);
}));

test("a first-party side-effect JavaScript import cannot escape the receipt", () => fixture(({ root, write }) => {
  write("caller.ts", 'import "./helper.js"; import { hashJson } from "./gateway"; export const run = () => hashJson(1);');
  const helper = join(root, "helper.js");
  writeFileSync(helper, 'JSON.stringify = () => "before";');
  const rootNames = [join(root, "gateway.ts"), join(root, "caller.ts")];
  const receipt = () => createJsonHashReceipt({ repoRoot: root, rootNames, authorities: ["gateway.ts"],
    ...scanJsonHashReferences({ repoRoot: root, rootNames, gateways: [{ file: rootNames[0], exports: ["hashJson"] }] }) });
  const original = receipt();
  assert.ok(original.sources.some(source => source.path === "helper.js"));
  writeFileSync(helper, 'JSON.stringify = () => "after";');
  assert.notEqual(original.sourceSignature, receipt().sourceSignature);
}));

test("shared build helpers, alternate configs, inherited tsconfig and HTML import maps are bound", () => fixture(({ root }) => {
  const studio = join(root, "Studio"), client = join(studio, "Client"), tools = join(root, "tools");
  mkdirSync(join(client, "src"), { recursive: true }); mkdirSync(tools);
  const put = (file, value) => writeFileSync(file, value);
  put(join(client, "src/entry.ts"), "export {};\n");
  put(join(client, "vite.config.ts"), 'import base from "../vite.module.base"; export default base;');
  put(join(client, "vite.vendor.config.ts"), 'import base from "../vite.module.base"; export default base;');
  put(join(studio, "vite.module.base.ts"), 'import helper from "../tools/shared-build"; export default helper;');
  put(join(tools, "shared-build.ts"), 'export default { define: { HASH_MODE: "before" } };');
  put(join(tools, "base.json"), '{"compilerOptions":{"target":"ES2022"}}');
  put(join(client, "tsconfig.json"), '{"extends":"../../tools/base.json","files":["src/entry.ts"]}');
  put(join(client, "index.html"), '<script type="importmap">{"imports":{"sdk":"/original.js"}}</script>');
  const receipt = () => createJsonHashReceipt({ repoRoot: root, rootNames: [], sourceFiles: [], references: [], moduleEdges: [], errors: [], authorities: [], settings: studioHashBuildInputs(root, [client]) });
  const original = receipt();
  for (const path of ["tools/shared-build.ts", "tools/base.json", "Studio/Client/vite.vendor.config.ts", "Studio/Client/index.html"])
    assert.ok(original.sources.some(source => source.path === path), path);
  put(join(tools, "shared-build.ts"), 'export default { define: { HASH_MODE: "after" } };');
  assert.notEqual(original.sourceSignature, receipt().sourceSignature);
  const changedHelper = receipt();
  put(join(client, "index.html"), '<script type="importmap">{"imports":{"sdk":"/redirected.js"}}</script>');
  assert.notEqual(changedHelper.sourceSignature, receipt().sourceSignature);
    const beforeScope = receipt();
    put(join(tools, "package.json"), '{"name":"@fixture/build-tools","type":"module"}');
    const withScope = receipt();
    assert.ok(withScope.sources.some(source => source.path === "tools/package.json"));
    assert.notEqual(beforeScope.sourceSignature, withScope.sourceSignature);
    put(join(tools, "package.json"), '{"name":"@fixture/build-tools","type":"commonjs"}');
    assert.notEqual(withScope.sourceSignature, receipt().sourceSignature);
  put(join(client, "tsconfig.json"), '{"extends":"../../missing.json"}');
  assert.throws(receipt, /Unresolved Studio build configuration|Cannot read file/);
}));

test("missing implementation sources and unresolved analysis cannot produce a receipt", () => fixture(({ root }) => {
  const input = { repoRoot: root, rootNames: [], sourceFiles: [], references: [], moduleEdges: [], errors: [], authorities: ["missing.ts"] };
  assert.throws(() => createJsonHashReceipt(input), /Missing exact hash implementation/);
  assert.throws(() => createJsonHashReceipt({ ...input, errors: [{ code: "unresolved" }] }), /Incomplete/);
}));

test("new shipping roots change admission and line-ending-only checkouts keep semantic token identities", () => fixture(({ root, files, write, scan, review }) => {
  write("caller.ts", files["caller.ts"].replace("; import", ";\nimport"));
  const original = scan(), register = review(original);
  for (const file of Object.keys(files)) if (file.endsWith(".ts")) write(file, files[file].replace(/\n/g, "\r\n"));
  assert.deepEqual(validateJsonHashReview(scan(), register, root), []);
  write("new.ts", "export class NewContract { value = 1; }");
  assert.ok(validateJsonHashReview(scan(), register, root).some(finding => finding.includes("rootSignature")));
}));

test("external const-enum contracts are authenticated even when they are not shipping roots", () => fixture(({ root, write }) => {
  const pkg = join(root, "node_modules", "recipe-enum");
  mkdirSync(pkg, { recursive: true });
  writeFileSync(join(pkg, "package.json"), JSON.stringify({ name: "recipe-enum", version: "1.0.0", types: "index.d.ts" }));
  const contract = join(pkg, "index.d.ts");
  writeFileSync(contract, "export declare const enum Tag { Value = 41 }");
  write("caller.ts", 'import { hashJson } from "./gateway"; import { Tag } from "recipe-enum"; export const run = () => hashJson(Tag.Value);');
  const rootNames = [join(root, "gateway.ts"), join(root, "caller.ts")];
  const receipt = () => createJsonHashReceipt({ repoRoot: root, rootNames, authorities: ["gateway.ts"],
    ...scanJsonHashReferences({ repoRoot: root, rootNames, gateways: [{ file: rootNames[0], exports: ["hashJson"] }] }) });
  const original = receipt();
  assert.ok(original.sources.some(source => source.path === "package:recipe-enum@1.0.0/index.d.ts"));
  writeFileSync(contract, "export declare const enum Tag { Value = 42 }");
  assert.notEqual(original.sourceSignature, receipt().sourceSignature);
}));

test("nested module scopes bind to their real package and an unowned external contract refuses", () => fixture(({ root }) => {
  const pkg = join(root, "node_modules", "scoped-contract"), types = join(pkg, "types");
  mkdirSync(types, { recursive: true });
  writeFileSync(join(pkg, "package.json"), JSON.stringify({ name: "scoped-contract", version: "1.2.3" }));
  const scope = join(types, "package.json"), contract = join(types, "value.d.ts");
  writeFileSync(scope, '{"type":"module"}');
  writeFileSync(contract, "export declare const value: number;");
  const original = sourceDocument(root, contract);
  assert.equal(original.path, "package:scoped-contract@1.2.3/types/value.d.ts");
  assert.equal(original.scopes[0].path, "types/package.json");
  writeFileSync(scope, '{"type":"commonjs"}');
  assert.notDeepEqual(original, sourceDocument(root, contract));
  const unowned = join(root, "node_modules", "unowned");
  mkdirSync(unowned); writeFileSync(join(unowned, "value.d.ts"), "export declare const value: number;");
  assert.throws(() => sourceDocument(root, join(unowned, "value.d.ts")), /no owning package/);
}));

test("computed active build modules and filesystem reads refuse instead of losing their inputs", () => fixture(({ root }) => {
  const studio = join(root, "Studio"), client = join(studio, "Client"), probes = join(client, "probes");
  mkdirSync(probes, { recursive: true });
  writeFileSync(join(probes, "hash-mode.mjs"), 'export default "before";');
  const config = join(client, "vite.config.ts");
  writeFileSync(config, 'const file="./probes/hash-mode.mjs"; export default await import(file);');
  assert.throws(() => studioHashBuildInputs(root, [client]), /Unresolved dynamic Studio build module/);
  writeFileSync(config, 'import value from "./probes/hash-mode.mjs"; export default value;');
  assert.ok(studioHashBuildInputs(root, [client]).includes(join(probes, "hash-mode.mjs")));
  writeFileSync(config, 'import { readFileSync as read } from "node:fs"; export default read(process.env.INPUT);');
  assert.throws(() => studioHashBuildInputs(root, [client]), /Unsupported Studio build loader\/read/);
  for (const script of [
    'import {readFileSync} from "node:fs"; const read=readFileSync; export default read(process.env.INPUT);',
    'import * as fs from "node:fs"; export default fs["readFileSync"](process.env.INPUT);',
    'import {createRequire} from "node:module"; const factory=createRequire; const load=factory(import.meta.url); export default load(process.env.INPUT);',
    'const fs=require("node:fs"); export default fs.readFileSync(process.env.INPUT);',
    'const fs=await import("node:fs"); export default fs.readFileSync(process.env.INPUT);',
    'import fs = require("node:fs"); export default fs.readFileSync(process.env.INPUT);',
    'import {promises} from "node:fs"; const io=promises; export default io.readFile(process.env.INPUT);',
    'import {createReadStream} from "node:fs"; export default createReadStream(process.env.INPUT);',
    'import {openSync,readSync} from "node:fs"; export default readSync(openSync(process.env.INPUT,"r"),new Uint8Array(8),0,8,0);'
  ]) {
    writeFileSync(config, script);
    assert.throws(() => studioHashBuildInputs(root, [client]), /Unsupported Studio build loader\/read/, script);
  }
  writeFileSync(config, 'export default {};');
  writeFileSync(config, 'import value from "#helper"; export default value;');
  assert.throws(() => studioHashBuildInputs(root, [client]), /build package-import alias/);
  for (const input of ["file:///outside/hash-build.mjs", "/outside/hash-build.mjs", "C:/outside/hash-build.mjs"]) {
    writeFileSync(config, 'import value from ' + JSON.stringify(input) + '; export default value;');
    assert.throws(() => studioHashBuildInputs(root, [client]), /build absolute\/URL input/);
  }
  writeFileSync(config, 'export default {};');
  writeFileSync(join(client, "package.json"), '{"name":"@fixture/client","exports":{"./build-helper":"./probes/hash-mode.mjs"}}');
  writeFileSync(config, 'import value from "@fixture/client/build-helper"; export default value;');
  assert.throws(() => studioHashBuildInputs(root, [client]), /build owned-package import/);
  writeFileSync(config, 'export default {};');
  writeFileSync(join(client, "package.json"), '{"scripts":{"build":"node ./build.mjs"}}');
  writeFileSync(join(client, "build.mjs"), 'const load=require; export default load(process.env.INPUT);');
  assert.throws(() => studioHashBuildInputs(root, [client]), /require alias/);
}));

test("binary and raw text module data retain exact byte differences", () => fixture(({ root, write }) => {
  const binary = join(root, "icon.png"), raw = join(root, "value.txt"), snippet = join(root, "snippet.ts");
  writeFileSync(binary, Buffer.from([0x80]));
  const firstBinary = sourceDocument(root, binary);
  writeFileSync(binary, Buffer.from([0x81]));
  assert.notDeepEqual(firstBinary, sourceDocument(root, binary));
  writeFileSync(raw, "before\r\nafter");
  const firstText = sourceDocument(root, raw);
  writeFileSync(raw, "before\nafter");
  assert.notDeepEqual(firstText, sourceDocument(root, raw));
  write("caller.ts", 'import image from "./icon.png?url"; import value from "./value.txt?raw"; import code from "./snippet.ts?raw";');
  writeFileSync(snippet, 'export const value = 1;\r\n');
  const physicalFiles = physicalModuleInputs(root, [join(root, "caller.ts")]);
  assert.deepEqual(new Set(physicalFiles), new Set([binary, raw, snippet]));
  const receipt = () => createJsonHashReceipt({ repoRoot: root, rootNames: [join(root, "caller.ts")], sourceFiles: [], references: [], moduleEdges: [], errors: [], authorities: [], physicalFiles });
  const original = receipt();
  writeFileSync(snippet, 'export const value = 1;\n');
  assert.notEqual(original.sourceSignature, receipt().sourceSignature);
    for (const input of ["/src/snippet.ts?raw", "#snippet?raw"]) {
      write("caller.ts", 'import data from ' + JSON.stringify(input) + ';');
      assert.throws(() => physicalModuleInputs(root, [join(root, "caller.ts")]), /non-relative Studio raw\/URL input/);
    }
}));

test("HTML entry JavaScript is an executable root without a TypeScript importer", () => fixture(({ root }) => {
  const client = join(root, "Studio/src/Example/Client");
  mkdirSync(join(client, "src"), { recursive: true }); mkdirSync(join(root, "extensions"));
  writeFileSync(join(client, "index.html"), '<script type="module" src="./src/entry.js"></script>');
  const entry = join(client, "src/entry.js");
  writeFileSync(entry, 'export const mode = "before";');
  const { rootNames } = shippingTypeScriptSources(root);
  assert.ok(rootNames.includes(entry));
  const first = sourceDocument(root, entry);
  writeFileSync(entry, 'export const mode = "after";');
  assert.notDeepEqual(first, sourceDocument(root, entry));
}));
