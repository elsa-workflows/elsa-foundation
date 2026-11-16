import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";
import process from "node:process";

const require = createRequire(new URL("../../Studio/package.json", import.meta.url));
const ts = require("typescript");
const path = process.env.CAPTURE_SOURCE ?? fileURLToPath(new URL("../../Studio/tests/acceptance/journeys.spec.ts", import.meta.url));
const toastControls = {};
const toastSource = readFileSync(new URL("../../Studio/tests/acceptance/toastPlacementControl.ts", import.meta.url), "utf8");
new Function("exports", ts.transpileModule(toastSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
}).outputText)(toastControls);
const source = ts.createSourceFile(path, readFileSync(path, "utf8"), ts.ScriptTarget.Latest, true);
assert.equal(source.parseDiagnostics.length, 0);
const capture = source.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "capture");
assert.ok(capture, "Exercise the actual shared capture function");
const compiled = ts.transpileModule(`exports.capture = ${capture.getText(source)};`, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
});

async function exercise(projectWidth, actualWidth, boxes, noFixedViewport = false) {
  const exports = {};
  const files = new Map();
  const expect = (actual, message) => ({ toEqual: expected => assert.deepEqual(actual, expected, message) });
  expect.poll = callback => ({ async toBe(expected) { assert.equal(await callback(), expected); } });
  const run = new Function("expect", "writeFileSync", "document", "innerWidth", "exports", "assertToastPlacementHolds",
    `${compiled.outputText}\nreturn exports.capture;`)(expect, (file, value) => files.set(file, value),
      { documentElement: { scrollWidth: actualWidth } }, actualWidth, exports, toastControls.assertToastPlacementHolds);
  let screenshots = 0;
  const attached = [];
  const page = {
    viewportSize: () => noFixedViewport ? null : { width: actualWidth, height: 900 },
    evaluate: callback => callback(),
    locator: selector => selector === "body" ? { ariaSnapshot: async () => "navigation" } : {
      evaluateAll: callback => callback(boxes.map((box, index) => ({ textContent: `Target ${index}`, getBoundingClientRect: () => box })))
    },
    async screenshot() { screenshots++; }
  };
  const info = {
    project: { use: { viewport: { width: projectWidth, height: 900 } } },
    outputPath: name => `/owned/${name}`,
    async attach(name) { attached.push(name); }
  };
  await run(page, info, "actual-size");
  assert.equal(screenshots, 1);
  assert.deepEqual(attached, ["actual-size.png", "actual-size.aria.yml"]);
  assert.equal(files.get("/owned/actual-size.aria.yml"), "navigation");
}

const vertical = [{ left: 0, right: 180, top: 0, bottom: 44, width: 180, height: 44 }, { left: 0, right: 180, top: 44, bottom: 88, width: 180, height: 44 }];
const separated = [{ left: 0, right: 100, width: 100, height: 44 }, { left: 100, right: 200, width: 100, height: 44 }];
const overlap = [{ left: 0, right: 120, width: 120, height: 44 }, { left: 100, right: 200, width: 100, height: 44 }];

test("intentional desktop captures inside a narrow project keep the vertical sidebar valid", async () => {
  await exercise(360, 1440, vertical);
});

test("a narrow viewport inside a desktop project still rejects actual horizontal overlap", async () => {
  await assert.rejects(exercise(1440, 390, overlap), /Horizontal navigation destinations must not overlap/);
});

test("actual narrow strips retain the non-overlap requirement", async () => {
  await exercise(390, 390, separated);
  await assert.rejects(exercise(390, 390, overlap), /Horizontal navigation destinations must not overlap/);
});

test("a browser window without a fixed viewport uses its actual inner width", async () => {
  await exercise(360, 1440, vertical, true);
  await assert.rejects(exercise(1440, 390, overlap, true), /Horizontal navigation destinations must not overlap/);
});
