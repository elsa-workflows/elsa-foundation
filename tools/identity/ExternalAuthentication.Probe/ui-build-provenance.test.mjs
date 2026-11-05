import test from "node:test";
import assert from "node:assert/strict";
import { assertBuildReceipt } from "./ui-build-provenance.mjs";

const identity = { sourceSha: "1".repeat(40), tree: "2".repeat(40) };
const outputs = [
  { file: "studio/bin/Elsa.Studio.Web.dll", sha256: "3".repeat(64) },
  { file: "external-module/studio/modules/external-authentication/module.js", sha256: "4".repeat(64) }
];
const toolchain = { node: { version: "v22.23.4", sha256: "5".repeat(64) },
  dotnet: { sdk: { version: "10.0.402", entry: { sha256: "6".repeat(64) } } } };
const receipt = () => ({ ...identity, kind: "successful-owned-ui-build", phase: "studio", exitCode: 0,
  toolchain: structuredClone(toolchain), commands: [["/owned/python3", "tools/ux-acceptance/run.py", "--output", "/owned/output"]],
  executions: [{ argv: ["/owned/python3", "tools/ux-acceptance/run.py", "--output", "/owned/output"], executable: { path: "/owned/python3", sha256: "7".repeat(64) },
    exitCode: 0, timeoutMs: 1000, cleanup: "owned-group-absent" }], outputs: structuredClone(outputs) });
test("a successful build is bound to both the exact source commit and tree", () => {
  assert.doesNotThrow(() => assertBuildReceipt(receipt(), "studio", identity, outputs, toolchain));
  for (const field of ["sourceSha", "tree"]) {
    const stale = receipt(); stale[field] = "0".repeat(40);
    assert.throws(() => assertBuildReceipt(stale, "studio", identity, outputs, toolchain));
  }
  for (const change of [value => { value.node.version = "v22.99.0"; }, value => { value.dotnet.sdk.version = "10.0.999"; },
    value => { value.dotnet.sdk.entry.sha256 = "0".repeat(64); }]) {
    const drifted = receipt(); change(drifted.toolchain);
    assert.throws(() => assertBuildReceipt(drifted, "studio", identity, outputs, toolchain));
  }
});
test("replacement of either a host DLL or module bytes invalidates the build receipt", () => {
  for (const index of [0, 1]) {
    const changed = structuredClone(outputs); changed[index].sha256 = "0".repeat(64);
    assert.throws(() => assertBuildReceipt(receipt(), "studio", identity, changed, toolchain));
  }
  assert.throws(() => assertBuildReceipt(receipt(), "studio", identity, outputs.slice(0, 1), toolchain));
});
test("failed, missing-command and wrong-producer receipts cannot certify a launch", () => {
  for (const override of [{ exitCode: 1 }, { executions: [] }, { commands: [] }, { phase: "probe" }, { outputs: [] }, { kind: "head-label-only" }])
    assert.throws(() => assertBuildReceipt({ ...receipt(), ...override }, "studio", identity, outputs, toolchain));
});
