import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";

// The Core Preview suite discovers exactly core_preview.py's verdict inventory, without starting browsers or hosts.
const require = createRequire(new URL("../../Studio/package.json", import.meta.url));

test("the core-preview suite discovers each Core Preview journey once, on its desktop project", () => {
  const report = JSON.parse(execFileSync(process.execPath, [require.resolve("@playwright/test/cli"), "test", "--list", "--reporter=json",
    "--config", fileURLToPath(new URL("../../Studio/tests/acceptance/playwright.config.ts", import.meta.url))], {
    cwd: fileURLToPath(new URL("../../Studio/", import.meta.url)),
    env: { ...process.env, UX_ACCEPTANCE_OWNED: "1", ELSA_STUDIO_URL: "http://studio.invalid", UX_ACCEPTANCE_OUT: "/owned",
      UX_ACCEPTANCE_SUITE: "core-preview", UX_BACKEND_URL: "http://backend.invalid", UX_HTTP_STUB_URL: "http://stub.invalid" },
    encoding: "utf8"
  }));
  const found = {};
  (function collect(suite) {
    for (const spec of suite.specs ?? []) (found[spec.title] ??= []).push(...spec.tests.map(testCase => testCase.projectName));
    for (const child of suite.suites ?? []) collect(child);
  })(report);
  const expected = JSON.parse(execFileSync("python3", ["-c", `
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("core_preview", sys.argv[1])
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
print(json.dumps({journey: [module.PROJECT] for journey in sorted(module.JOURNEYS)}))
`, fileURLToPath(new URL("core_preview.py", import.meta.url))], { encoding: "utf8" }));
  assert.deepEqual(found, expected);
});
