import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, symlinkSync, unlinkSync } from "node:fs";
import { join, resolve, sep } from "node:path";
import { scanJsonHashReferences } from "../lib/jsonHashReferences.mjs";
import { createJsonHashReceipt, readJsonHashGoldenEvidence, shippingTypeScriptSources, sourceDocument, validateJsonHashReview } from "../lib/jsonHashReceipt.mjs";
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
  const review = receipt => {
    const register = { ...receipt, owners: receipt.owners.map(owner => ({ ...owner, classification: "json_routed", reason: "Owned Payload projection through the canonical gateway.", goldenEvidence: ["golden.txt"] })) };
    return { ...register, goldenSignature: readJsonHashGoldenEvidence(root, register.owners).signature };
  };
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

test("golden assertion content cannot change while its shipping source receipt remains current", () => fixture(({ root, scan, review }) => {
  const receipt = scan(), register = review(receipt);
  const original = readFileSync(join(root, "golden.txt"));
  writeFileSync(join(root, "golden.txt"), "The actual-owner assertion was removed.");
  assert.equal(scan().sourceSignature, receipt.sourceSignature, "The negative must exercise proof content outside shipping source selection.");
  assert.ok(validateJsonHashReview(receipt, register, root).some(finding => finding.includes("goldenSignature")));
  writeFileSync(join(root, "golden.txt"), original);
  assert.deepEqual(validateJsonHashReview(receipt, register, root), []);
  const { goldenSignature, ...unbound } = register;
  assert.ok(goldenSignature);
  assert.ok(validateJsonHashReview(receipt, unbound, root).some(finding => finding.includes("goldenSignature")));
}));

test("golden receipts bind owner mappings and complete file paths even when bytes match", () => fixture(({ root, scan, review }) => {
  const receipt = scan(), register = review(receipt);
  writeFileSync(join(root, "copied-golden.txt"), readFileSync(join(root, "golden.txt")));
  const remapped = { ...register, owners: register.owners.map(owner => ({ ...owner, goldenEvidence: ["copied-golden.txt"] })) };
  assert.equal(scan().sourceSignature, receipt.sourceSignature);
  assert.ok(validateJsonHashReview(receipt, remapped, root).some(finding => finding.includes("goldenSignature")));
  const renamed = register.owners.map(owner => ({ ...owner, identity: owner.identity + "-another-owner" }));
  assert.notEqual(readJsonHashGoldenEvidence(root, renamed).signature, register.goldenSignature);
  assert.equal(readJsonHashGoldenEvidence(root, [...register.owners].reverse()).signature, register.goldenSignature);
}));

test("golden files cannot be deleted, empty, BOM-only or malformed text", () => fixture(({ root, scan, review }) => {
  const register = review(scan());
  const variants = [
    ["empty.ts", Buffer.alloc(0)], ["empty.bin", Buffer.alloc(0)],
    ["utf8.json", Buffer.from([0xef, 0xbb, 0xbf])], ["utf16le.txt", Buffer.from([0xff, 0xfe])],
    ["utf16be.txt", Buffer.from([0xfe, 0xff])], ["utf32le.txt", Buffer.from([0xff, 0xfe, 0, 0])],
    ["utf32be.txt", Buffer.from([0, 0, 0xfe, 0xff])], ["invalid-utf8.txt", Buffer.from([0xff])],
    ["invalid-utf16.txt", Buffer.from([0xff, 0xfe, 0])],
    ["invalid-utf32.txt", Buffer.from([0xff, 0xfe, 0, 0, 0, 0xd8, 0, 0])]
  ];
  for (const [file, bytes] of variants) {
    writeFileSync(join(root, file), bytes);
    const result = readJsonHashGoldenEvidence(root, register.owners.map(owner => ({ ...owner, goldenEvidence: [file] })));
    assert.equal(result.signature, "", file);
    assert.ok(result.findings.length, file);
  }
  unlinkSync(join(root, "golden.txt"));
  assert.ok(readJsonHashGoldenEvidence(root, register.owners).findings.some(finding => finding.includes("golden.txt")));
}));

test("golden paths reject traversal, URIs, absolute names, directories, duplicates and the register itself", () => fixture(({ root, scan, review }) => {
  const register = review(scan());
  mkdirSync(join(root, "nested")); writeFileSync(join(root, "nested/proof.txt"), "pin");
  const baseline = "Studio/scripts/baselines/json-hash-review.json";
  mkdirSync(join(root, "Studio/scripts/baselines"), { recursive: true }); writeFileSync(join(root, baseline), "{}");
  for (const evidence of [[], ["missing.txt"], ["../outside.txt"], ["./golden.txt"], ["nested/../golden.txt"],
    ["nested//proof.txt"], [""], [" "], [" golden.txt"], ["golden.txt "], ["nested"], ["golden.txt\0"],
    ["file:///outside.txt"], ["https://example.com/proof.txt"], ["C:/outside.txt"], ["\\outside.txt"], [resolve(root, "golden.txt")],
    ["golden.txt", "golden.txt"], ["nested\\proof.txt", "nested/proof.txt"], [baseline]]) {
    const result = readJsonHashGoldenEvidence(root, register.owners.map(owner => ({ ...owner, goldenEvidence: evidence })));
    assert.equal(result.signature, "", JSON.stringify(evidence));
    assert.ok(result.findings.length, JSON.stringify(evidence));
  }
  const valid = readJsonHashGoldenEvidence(root, register.owners.map(owner => ({ ...owner, goldenEvidence: ["nested\\proof.txt"] })));
  assert.deepEqual(valid.findings, []);
}));

function encodedGolden(text, encoding) {
  if (encoding.startsWith("utf32")) {
    const points = [...text].map(character => character.codePointAt(0));
    const bytes = Buffer.alloc(4 + points.length * 4), little = encoding === "utf32le";
    (little ? Buffer.from([0xff, 0xfe, 0, 0]) : Buffer.from([0, 0, 0xfe, 0xff])).copy(bytes);
    points.forEach((point, index) => little ? bytes.writeUInt32LE(point, 4 + index * 4) : bytes.writeUInt32BE(point, 4 + index * 4));
    return bytes;
  }
  if (encoding.startsWith("utf16")) {
    const bytes = Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(text, "utf16le")]);
    return encoding === "utf16be" ? bytes.swap16() : bytes;
  }
  return Buffer.concat([encoding === "utf8bom" ? Buffer.from([0xef, 0xbb, 0xbf]) : Buffer.alloc(0), Buffer.from(text)]);
}

test("golden text BOM and CRLF portability retains escaped literals while binary evidence stays exact", () => fixture(({ root, scan, review }) => {
  const owners = review(scan()).owners;
  const text = 'Expected "\\r\\n" and René 🚀.\nIndependent assertion.\n';
  writeFileSync(join(root, "golden.txt"), text);
  const original = readJsonHashGoldenEvidence(root, owners).signature;
  for (const encoding of ["utf8", "utf8bom", "utf16le", "utf16be", "utf32le", "utf32be"]) {
    writeFileSync(join(root, "golden.txt"), encodedGolden(text.replace(/\n/g, "\r\n"), encoding));
    assert.equal(readJsonHashGoldenEvidence(root, owners).signature, original, encoding);
  }
  writeFileSync(join(root, "golden.txt"), text.replace('"\\r\\n"', '"\\n"'));
  assert.notEqual(readJsonHashGoldenEvidence(root, owners).signature, original);
  writeFileSync(join(root, "golden.txt"), Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), encodedGolden(text, "utf8bom")]));
  assert.notEqual(readJsonHashGoldenEvidence(root, owners).signature, original, "Only one initial BOM is encoding metadata.");
  writeFileSync(join(root, "golden.txt"), " \t\n");
  assert.deepEqual(readJsonHashGoldenEvidence(root, owners).findings, [], "Nonempty whitespace is not rejected by a semantic-content heuristic.");
  const binary = owners.map(owner => ({ ...owner, goldenEvidence: ["golden.bin"] }));
  writeFileSync(join(root, "golden.bin"), Buffer.from([0]));
  assert.deepEqual(readJsonHashGoldenEvidence(root, binary).findings, []);
  writeFileSync(join(root, "golden.bin"), Buffer.from([13, 10]));
  const first = readJsonHashGoldenEvidence(root, binary).signature;
  writeFileSync(join(root, "golden.bin"), Buffer.from([10]));
  assert.notEqual(readJsonHashGoldenEvidence(root, binary).signature, first);
}));

test("golden evidence refuses file symlinks both inside and outside the repository", t => fixture(({ root, scan, review }) => {
  const owners = review(scan()).owners;
  const outside = join(parent, "s9-ts-golden-outside-" + root.split(sep).at(-1) + ".txt");
  writeFileSync(outside, "outside pin");
  try {
    for (const [name, target] of [["inside-link.txt", join(root, "golden.txt")], ["outside-link.txt", outside]]) {
      try { symlinkSync(target, join(root, name), "file"); }
      catch (error) { if (process.platform === "win32" && ["EPERM", "EACCES"].includes(error.code)) { t.skip("Windows file symlink privilege unavailable; mandatory controls run on Linux."); return; } throw error; }
      const result = readJsonHashGoldenEvidence(root, owners.map(owner => ({ ...owner, goldenEvidence: [name] })));
      assert.equal(result.signature, ""); assert.ok(result.findings.length);
    }
  } finally { unlinkSync(outside); }
}));

test("golden evidence refuses linked directories even when their target is inside the repository", t => fixture(({ root, scan, review }) => {
  const owners = review(scan()).owners;
  mkdirSync(join(root, "real")); writeFileSync(join(root, "real/proof.txt"), "pin");
  try { symlinkSync(join(root, "real"), join(root, "linked"), process.platform === "win32" ? "junction" : "dir"); }
  catch (error) { if (process.platform === "win32" && ["EPERM", "EACCES"].includes(error.code)) { t.skip("Windows junction privilege unavailable; mandatory controls run on Linux."); return; } throw error; }
  const result = readJsonHashGoldenEvidence(root, owners.map(owner => ({ ...owner, goldenEvidence: ["linked/proof.txt"] })));
  assert.equal(result.signature, ""); assert.ok(result.findings.some(finding => finding.includes("linked ancestor")));
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
