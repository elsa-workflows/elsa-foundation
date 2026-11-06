import { writeFileSync } from "node:fs";
import { expect, type APIRequestContext, type Locator, type Page, type TestInfo } from "@playwright/test";
import type { Gate, Hold, Invocation, Job, Page as JobPage, Schedule } from "../../../Extensions/Jobs/studio/Elsa.Studio.Jobs/Client/src/types";

export type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
export const jobsPolicy = { retry: { maximumAttempts: 2, initialBackoff: "00:00:03", maximumBackoff: "00:00:03", backoffMultiplier: 1, jitterFraction: 0, retryTimeouts: false }, attemptTimeout: "00:00:30", minimumKeyRecognition: "30.00:00:00" };
export function reportInvocation(queue: string, title: string, overrides: Record<string, unknown> = {}, jobType = "ux.report"): Invocation {
  return { handler: { jobType, version: "1" }, queue, input: { schema: { alias: "ux.report.input", version: 1 }, value: { title, amounts: [12.5, 7.5, 30], delayMilliseconds: 250, mode: "success", tag: title, ...overrides } },
    resultSchema: { alias: "ux.report.result", version: 1 }, policy: structuredClone(jobsPolicy), notBefore: null };
}
export async function captureJobs(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const png = info.outputPath(`${name}.png`); await page.screenshot({ path: png, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: png, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`); writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}
export async function signInAdmin(page: Page) {
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await expect.poll(async () => await form.isVisible() || await page.getByRole("heading", { name: "Workflow definitions", exact: true }).isVisible(), { timeout: 60_000 }).toBe(true);
  if (await form.isVisible()) {
    await form.getByLabel(/username/i).fill("admin"); await form.getByLabel(/password/i).fill("Password123!");
    await form.getByRole("button", { name: /sign in/i }).press("Enter");
  }
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
}
export class JobsUx {
  readonly base: string;
  readonly queue: string;
  readonly stamp: string;
  constructor(readonly page: Page, readonly info: TestInfo, readonly capture: Capture = captureJobs) {
    if (!process.env.UX_BACKEND_URL || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Jobs backend is required.");
    this.base = process.env.UX_BACKEND_URL; this.queue = "reports-" + info.project.name; this.stamp = `${info.project.name}-${Date.now()}`;
  }
  async get<T>(path: string, request: APIRequestContext = this.page.request): Promise<T> {
    const response = await request.get(this.base + path); expect(response.status(), path).toBe(200); return response.json();
  }
  async write<T>(method: "POST" | "PUT", path: string, data: unknown, request: APIRequestContext = this.page.request): Promise<T> {
    const response = await request.fetch(this.base + path, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : response.json();
  }
  async submit(title = this.stamp, overrides: Record<string, unknown> = {}, jobType = "ux.report") {
    return this.write<{ jobId: string }>("POST", "/jobs", { idempotencyKey: crypto.randomUUID(), invocation: reportInvocation(this.queue, title, overrides, jobType) });
  }
  job(id: string) { return this.get<Job>("/jobs/" + encodeURIComponent(id)); }
  async finish(id: string, outcome = "succeeded") {
    await expect.poll(async () => (await this.job(id)).outcome, { timeout: 45_000 }).toBe(outcome); return this.job(id);
  }
  async open(id: string) {
    await this.page.goto("/jobs/" + encodeURIComponent(id)); await expect(this.page.getByRole("heading", { name: "Job detail", exact: true })).toBeVisible();
    await expect(this.page.getByRole("region", { name: "Job summary", exact: true })).toContainText(id);
  }
  async refresh() { await this.page.getByRole("button", { name: "Refresh", exact: true }).press("Enter"); }
  async hold(gate: Gate, reason: string): Promise<Hold> {
    const holdId = crypto.randomUUID(); await this.write("POST", "/jobs/holds", { gate, holdId, operationId: crypto.randomUUID(), reason });
    const query = new URLSearchParams({ kind: gate.kind, name: gate.name, ...(gate.modelKind ? { modelKind: gate.modelKind } : {}), limit: "100" });
    const holds = await this.get<JobPage<Hold>>("/jobs/holds?" + query); const hold = holds.items.find(item => item.holdId === holdId); expect(hold).toBeTruthy(); return hold!;
  }
  release(hold: Hold) { return this.write("POST", `/jobs/holds/${encodeURIComponent(hold.holdId)}/release`, { gate: hold.gate, expectedRevision: hold.revision, operationId: crypto.randomUUID(), reason: "Release owned UX hold" }); }
  async allJobs() {
    const items: Job[] = []; let after: string | null | undefined;
    do {
      const query = new URLSearchParams({ queue: this.queue, limit: "100", ...(after ? { after } : {}) });
      const page = await this.get<JobPage<Job>>("/jobs?" + query); items.push(...page.items); after = page.next;
      if (items.length > 1000) throw new Error("Owned Jobs acceptance exceeded its finite history bound.");
    } while (after);
    return items;
  }
  schedule(id: string) { return this.get<Schedule>("/jobs/schedules/" + encodeURIComponent(id)); }
  async disable(id: string) { const schedule = await this.schedule(id); return this.write("POST", `/jobs/schedules/${encodeURIComponent(id)}/enabled`, { enabled: false, expectedRevision: schedule.revision, operationId: crypto.randomUUID(), reason: "Preserve accepted history after UX schedule" }); }
  async prompt(button: Locator, title: string, reason: string, confirm: string) {
    await button.press("Enter"); const dialog = this.page.getByRole("dialog", { name: title, exact: true });
    await expect(dialog.getByRole("textbox")).toBeFocused(); await dialog.getByRole("textbox").fill(reason);
    await dialog.getByRole("button", { name: confirm, exact: true }).press("Enter"); await expect(dialog).toBeHidden();
  }
  async restart() {
    const before = await this.get<{ subjectId: string; tenantId: string }>("/jobs/access");
    await this.control("restart"); await signInAdmin(this.page);
    expect(await this.get("/jobs/access")).toMatchObject({ subjectId: before.subjectId, tenantId: before.tenantId });
  }
  async control(action: "restart" | "stop" | "start") {
    if (!process.env.UX_RESTART_URL || !process.env.UX_RESTART_TOKEN) throw new Error("Owned restart controller is unavailable.");
    // Node fetch, outside Playwright tracing: the controller credential never enters browser state/artifacts.
    const response = await fetch(process.env.UX_RESTART_URL.replace(/\/restart$/, "/" + action), { method: "POST", headers: { Authorization: "Bearer " + process.env.UX_RESTART_TOKEN } });
    expect(response.status()).toBe(200);
  }
}
export async function fillInvocation(page: Page, invocation: Invocation) {
  await page.getByLabel("Registered job type", { exact: true }).fill(invocation.handler.jobType);
  await page.getByLabel("Handler version", { exact: true }).fill(invocation.handler.version);
  await page.getByLabel("Queue", { exact: true }).fill(invocation.queue);
  await page.getByLabel("Input schema alias", { exact: true }).fill(invocation.input.schema.alias);
  await page.getByLabel("Result schema alias", { exact: true }).fill(invocation.resultSchema.alias);
  await page.getByLabel(/^Input JSON/).fill(JSON.stringify(invocation.input.value));
}
