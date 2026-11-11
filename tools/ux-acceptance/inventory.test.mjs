import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";

// The Playwright projects, the spec titles and run.py's verdict inventory must describe the same cases.
// No browser, host or Playwright runner: the config is transpiled and evaluated with a stand-in defineConfig.
const require = createRequire(new URL("../../Studio/package.json", import.meta.url));
const ts = require("typescript");
const acceptance = new URL("../../Studio/tests/acceptance/", import.meta.url);
const configSource = readFileSync(new URL("playwright.config.ts", acceptance), "utf8");
const compiled = ts.transpileModule(configSource, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } });
const env = {
  UX_ACCEPTANCE_OWNED: "1", ELSA_STUDIO_URL: "http://combined.invalid", UX_ACCEPTANCE_OUT: "/owned",
  UX_RUNTIME_STUDIO_URL: "http://runtime.invalid"
};
const module = { exports: {} };
new Function("require", "module", "exports", "process", compiled.outputText)(
  name => { assert.equal(name, "@playwright/test"); return { defineConfig: value => value }; }, module, module.exports, { env });
const config = module.exports.default;

function titles(file) {
  const path = fileURLToPath(new URL(file, acceptance));
  const source = ts.createSourceFile(path, readFileSync(path, "utf8"), ts.ScriptTarget.Latest, true);
  const found = [];
  for (const statement of source.statements) {
    const call = ts.isExpressionStatement(statement) ? statement.expression : undefined;
    if (call && ts.isCallExpression(call) && call.expression.getText(source) === "test" && ts.isStringLiteral(call.arguments[0])) found.push(call.arguments[0].text);
  }
  return found;
}

const python = execFileSync("python3", ["-c", `
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("acceptance", sys.argv[1])
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
print(json.dumps({journey: sorted(projects) for journey, projects in module.JOURNEY_PROJECTS.items()}))
`, fileURLToPath(new URL("run.py", import.meta.url))], { encoding: "utf8" });
const expected = JSON.parse(python);

test("every configured project runs exactly the journeys run.py's verdict requires", () => {
  const actual = {};
  for (const project of config.projects) {
    const file = project.testMatch ?? config.testMatch;
    for (const title of titles(file)) {
      if (project.grep && !project.grep.test(["", project.name, file, title].join(" "))) continue;
      (actual[title] ??= []).push(project.name);
    }
  }
  for (const projects of Object.values(actual)) projects.sort();
  assert.deepEqual(actual, expected);
  assert.equal(Object.values(actual).flat().length, 93);
});

test("touch projects really emulate touch and only the supplementary subsets change hosts", () => {
  const byName = Object.fromEntries(config.projects.map(project => [project.name, project]));
  for (const width of [360, 390, 1440]) for (const mode of ["light", "dark"]) {
    const project = byName[`touch-${width}-${mode}`];
    assert.deepEqual(project.use, { viewport: { width, height: 900 }, colorScheme: mode, hasTouch: true, isMobile: width <= 1024 });
  }
  for (const [name, project] of Object.entries(byName)) {
    if (!name.startsWith("touch-")) assert.notEqual(project.use.hasTouch, true, name);
    const subset = { "runtime-api-subset-1440-light": env.UX_RUNTIME_STUDIO_URL }[name];
    assert.equal(project.use.baseURL, subset, name);
  }
  assert.equal(config.retries, 0);
  assert.equal(config.workers, 1);
});

test("supplementary journeys keep their own hooks and leave the owned-definition cleanup hook alone", () => {
  const supplements = readFileSync(new URL("supplements.spec.ts", acceptance), "utf8");
  assert.equal((supplements.match(/^test\.afterEach\(/gm) ?? []).length, 1);
  assert.doesNotMatch(supplements, /request\.(post|put|patch|delete)\(/, "Supplementary journeys create no store records to clean up");
  assert.deepEqual(titles("supplements.spec.ts").sort(), Object.keys(expected).filter(title => !titles("journeys.spec.ts").includes(title)).sort());
});
