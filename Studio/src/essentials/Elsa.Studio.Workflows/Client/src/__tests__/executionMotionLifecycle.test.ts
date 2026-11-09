import { expect, it } from "vitest";

interface ProbeProcess {
  execPath: string;
  env: Record<string, string | undefined>;
  getBuiltinModule(name: "url"): { fileURLToPath(url: URL): string };
  getBuiltinModule(name: "child_process"): { execFileSync(file: string, args: string[], options: {
    env: Record<string, string | undefined>; encoding: "utf8"; timeout: number; stdio: string[];
  }): string };
}
// Keep Node-only probe types local to this test; the client compilation remains browser scoped.
const nodeProcess = (globalThis as typeof globalThis & { process: ProbeProcess }).process;
const testModuleUrl = import.meta.url;
const probe = nodeProcess.getBuiltinModule("url").fileURLToPath(new URL("../../scripts/probes/execution-motion-lifecycle.mjs", testModuleUrl));
it.each(["strict-remount", "delayed-expiry"])("expires presentation cues under development React: %s", mode => {
  const output = nodeProcess.getBuiltinModule("child_process").execFileSync(nodeProcess.execPath, ["--experimental-strip-types", probe, mode], {
    env: { ...nodeProcess.env, NODE_ENV: "development" }, encoding: "utf8", timeout: 10000, stdio: ["ignore", "pipe", "pipe"]
  });
  expect(JSON.parse(output)).toEqual({ mode, expired: true });
}, 10000);
