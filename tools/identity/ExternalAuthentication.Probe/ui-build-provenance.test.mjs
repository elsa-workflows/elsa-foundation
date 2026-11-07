import test from "node:test";
import assert from "node:assert/strict";
import { assertBuildReceipt } from "./ui-build-provenance.mjs";

const identity = { sourceSha: "1".repeat(40), tree: "2".repeat(40) };
const outputs = [
  { file: "studio/bin/Elsa.Studio.Web.dll", sha256: "3".repeat(64) },
  { file: "external-module/studio/modules/external-authentication/module.js", sha256: "4".repeat(64) }
];
const executable = path => ({ path, realPath: path, sha256: "5".repeat(64) });
const toolchain = { node: { ...executable("/owned/node"), version: "v22.23.4" }, python: { ...executable("/owned/python3"), version: "3.14.0" },
  dotnet: { launcher: executable("/owned/dotnet"), host: { ...executable("/owned/dotnet"), version: "10.0.13" },
    sdk: { version: "10.0.402", basePath: "/owned/sdk/10.0.402", entry: executable("/owned/sdk/10.0.402/dotnet.dll"), msbuild: executable("/owned/sdk/10.0.402/MSBuild.dll") } } };
function receipt() {
  const argv = [toolchain.python.path, "tools/ux-acceptance/run.py", "--output", "/owned/output"];
  return { ...identity, kind: "successful-owned-ui-build", phase: "studio", exitCode: 0, standardOutput: "/owned/output",
    toolchain: structuredClone(toolchain), commands: [argv], executions: [{ argv: [...argv], executable: structuredClone(toolchain.python),
      exitCode: 0, processGroup: 12345, timeoutMs: 1000, cleanup: "owned-group-absent" }], outputs: structuredClone(outputs) };
}
function refused(value, phase = "studio") { assert.throws(() => assertBuildReceipt(value, phase, identity, outputs, toolchain)); }
test("a successful build is bound to the exact source commit, tree and tools", () => {
  assert.doesNotThrow(() => assertBuildReceipt(receipt(), "studio", identity, outputs, toolchain));
  for (const field of ["sourceSha", "tree"]) { const stale = receipt(); stale[field] = "0".repeat(40); refused(stale); }
  for (const change of [value => { value.node.version = "v22.99.0"; }, value => { value.dotnet.sdk.version = "10.0.999"; },
    value => { value.dotnet.sdk.entry.sha256 = "0".repeat(64); }, value => { delete value.python; }]) {
    const drifted = receipt(); change(drifted.toolchain); refused(drifted);
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
    refused({ ...receipt(), ...override });
});
test("empty, null and malformed command or executable records fail closed", () => {
  for (const command of [[], null, [""], [null], [123], "/owned/python3"]) {
    for (const producer of [{ sha256: "x" }, structuredClone(toolchain.python)]) {
      const malformed = receipt(); malformed.commands = [command]; malformed.executions[0].argv = command;
      // Include the supplied empty-array case and a well-formed executable to isolate command refusal.
      malformed.executions[0].executable = producer; refused(malformed);
    }
  }
  for (const change of [value => { delete value.path; }, value => { delete value.realPath; }, value => { value.path = "python3"; },
    value => { value.sha256 = "x"; }, value => { value.sha256 = "0".repeat(64); }]) {
    const malformed = receipt(); change(malformed.executions[0].executable); refused(malformed);
  }
  for (const execution of [null, [], {}]) { const malformed = receipt(); malformed.executions = [execution]; refused(malformed); }
  for (const change of [value => { delete value.processGroup; }, value => { value.timeoutMs = 0; }, value => { value.timeoutMs = 31 * 60_000; }]) {
    const malformed = receipt(); change(malformed.executions[0]); refused(malformed);
  }
});
test("a producer requires exactly its ordered phase commands and pinned executable", () => {
  const extra = receipt(); extra.commands.push([...extra.commands[0]]); extra.executions.push(structuredClone(extra.executions[0])); refused(extra);
  const substituted = receipt(); substituted.commands[0][0] = toolchain.node.path; substituted.executions[0].argv[0] = toolchain.node.path;
  substituted.executions[0].executable = structuredClone(toolchain.node); refused(substituted);
  const argument = receipt(); argument.commands[0].push("--unexpected"); argument.executions[0].argv.push("--unexpected"); refused(argument);
  const project = "tools/identity/ExternalAuthentication.Probe/ExternalAuthentication.Probe.csproj";
  const commands = [
    [toolchain.dotnet.launcher.path, "restore", project, "--locked-mode", "--disable-parallel", "--disable-build-servers", "-m:2", "-p:BuildInParallel=false", "-nodeReuse:false"],
    [toolchain.dotnet.launcher.path, "build", project, "--no-restore", "--disable-build-servers", "-m:2", "-p:BuildInParallel=false", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-p:WarningsNotAsErrors=NU1603"]];
  const probe = { ...receipt(), phase: "probe", standardOutput: null, commands, executions: commands.map(argv => ({ argv: [...argv],
    executable: structuredClone(toolchain.dotnet.launcher), exitCode: 0, processGroup: 12345, timeoutMs: 1000, cleanup: "owned-group-absent" })) };
  assert.doesNotThrow(() => assertBuildReceipt(probe, "probe", identity, outputs, toolchain));
  const missing = structuredClone(probe); missing.commands.pop(); missing.executions.pop(); refused(missing, "probe");
  const reversed = structuredClone(probe); reversed.commands.reverse(); reversed.executions.reverse(); refused(reversed, "probe");
  refused({ ...probe, phase: "controls" }, "controls"); refused(probe, "unknown");
});
