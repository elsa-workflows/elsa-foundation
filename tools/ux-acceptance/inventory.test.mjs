import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";

// Compare the tests Playwright discovers with run.py's verdict inventory without starting browsers or hosts.
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

function discoveredProjects(composition) {
  const report = JSON.parse(execFileSync(process.execPath, [require.resolve("@playwright/test/cli"),
    "test", "--list", "--reporter=json", "--config", fileURLToPath(new URL("playwright.config.ts", acceptance))], {
    cwd: fileURLToPath(new URL("../../Studio/", import.meta.url)),
    env: { ...process.env, ...env, UX_ACTIVITY_DATA_COMPOSITION: composition },
    encoding: "utf8"
  }));
  const found = {};
  // The JSON reporter may nest suites by file and describe block.
  function collect(suite) {
    for (const spec of suite.specs ?? []) {
      (found[spec.title] ??= []).push(...spec.tests.map(testCase => testCase.projectName));
    }
    for (const child of suite.suites ?? []) collect(child);
  }
  collect(report);
  for (const projects of Object.values(found)) projects.sort();
  return found;
}

const python = execFileSync("python3", ["-c", `
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("acceptance", sys.argv[1])
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
print(json.dumps({
    "normal": {journey: sorted(module.JOURNEY_PROJECTS[journey]) for journey in module.NORMAL_JOURNEYS},
    "minimal": {journey: sorted(projects) for journey, projects in module.JOURNEY_PROJECTS.items()},
}))
`, fileURLToPath(new URL("run.py", import.meta.url))], { encoding: "utf8" });
const expected = JSON.parse(python);

for (const composition of ["normal", "minimal"]) {
  test(`${composition} composition discovers the expected projects for every journey`, () => {
    assert.deepEqual(discoveredProjects(composition), expected[composition]);
  });
}

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
