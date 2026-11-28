import { expect, type Page } from "@playwright/test";
import type { RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";

type Frame = { page: RuntimeHistoryPage | null; status: number | null };
type Grants = { revision: string; directPermissions: string[] };

export async function verifyLiveHistoryRevocation(admin: Page, backend: string, startRoute: string, resumeRoute: string, sibling: string) {
  const authority = `${backend}/__qualification/principals/0`;
  const before = await admin.request.get(`${authority}/authority`);
  expect(before.ok(), await before.text()).toBe(true);
  const initial = await before.json() as Grants;
  expect(initial.directPermissions).toContain("workflows/runtime:read");

  let revoked!: Grants;
  const routeGrant = await probe(async () => {
    const update = await admin.request.post(`${authority}/grant-profile`, { data: { profile: "history-no-route", revision: initial.revision } });
    expect(update.ok(), await update.text()).toBe(true);
    revoked = await update.json() as Grants;
    expect(revoked.revision).not.toBe(initial.revision);
    expect(revoked.directPermissions).toContain("workflows/activity-executions:inspect");
    expect(revoked.directPermissions).not.toContain("workflows/runtime:read");
  });

  const restored = await admin.request.post(`${authority}/grant-profile`, { data: { profile: "history-reader", revision: revoked.revision } });
  expect(restored.ok(), await restored.text()).toBe(true);
  const exactRunPolicy = await probe(async run => {
    const update = await admin.request.post(`${backend}/__qualification/history/${encodeURIComponent(run)}/deny-live-inspection`);
    expect(update.status(), await update.text()).toBe(204);
  }, sibling);
  return { routeGrant, retainedGrants: revoked.directPermissions, exactRunPolicy };

  async function probe(revoke: (run: string) => Promise<void>, unaffected?: string) {
    const start = await admin.request.post(`${backend}/workflows/http/${startRoute}`, { data: {} });
    expect(start.status(), await start.text()).toBe(201);
    const run = start.headers()["x-elsa-workflow-instance-id"];
    expect(run).toBeTruthy();
    const context = await admin.context().browser()!.newContext();
    try {
      const reader = await context.newPage();
      await reader.goto(`${backend}/health/ready`);
      expect(await reader.evaluate(async () => {
        const login = await fetch("/_elsa/identity/login", { method: "POST", credentials: "include",
          headers: { "content-type": "application/json" }, body: JSON.stringify({ username: "core-history-reader", password: "Password123!" }) });
        return login.status;
      })).toBe(200);
      const connection = await openStream(run);
      expect(await connection.evaluate(value => value.status)).toBe(200);
      const admitted = await connection.evaluate(value => value.next());
      expect(admitted?.page?.items).toEqual(expect.arrayContaining([
        expect.objectContaining({ workflowExecutionId: run, executableNodeId: "wait", name: "ActivityStarted" })
      ]));

      await revoke(run);
      const committed = await admin.request.post(`${backend}/workflows/http/${resumeRoute}`, { data: {} });
      expect(committed.status(), await committed.text()).toBe(200);
      expect(await committed.text()).toBe("timeline continued");
      expect(await connection.evaluate(value => value.next())).toEqual({ page: null, status: 403 });
      expect(await connection.evaluate(value => value.next())).toBeNull();
      if (unaffected) {
        const other = await openStream(unaffected);
        expect(await other.evaluate(value => value.status)).toBe(200);
        const initialSibling = await other.evaluate(value => value.next());
        expect(initialSibling?.page?.items.length).toBeGreaterThan(0);
        expect(initialSibling?.page?.items.every(item => item.workflowExecutionId === unaffected)).toBe(true);
      }
      return { run, initialUpperSequence: admitted?.page?.upperSequence, committedResponse: committed.status(), streamRefusal: 403,
        streamClosed: true, unaffected };

      function openStream(execution: string) {
        return reader.evaluateHandle(async url => {
          const response = await fetch(url, { headers: { Accept: "application/x-ndjson" }, credentials: "include" });
          const body = response.body!.pipeThrough(new TextDecoderStream()).getReader();
          let buffer = "";
          return { status: response.status, async next(): Promise<Frame | null> {
            while (!buffer.includes("\n")) {
              const chunk = await body.read();
              if (chunk.done) return null;
              buffer += chunk.value;
            }
            const newline = buffer.indexOf("\n");
            const frame = JSON.parse(buffer.slice(0, newline)) as Frame;
            buffer = buffer.slice(newline + 1);
            return frame;
          } };
        }, `/runtime/workflows/instances/${encodeURIComponent(execution)}/history/live`);
      }
    } finally {
      await context.close();
    }
  }
}
