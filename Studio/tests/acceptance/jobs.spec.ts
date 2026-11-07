import { readFileSync, writeFileSync } from "node:fs";
import { expect, test, type Page } from "@playwright/test";
import { JobsUx, captureJobs, fillInvocation, reportInvocation, signInAdmin } from "./jobsSupport";
import { jobsModelJourney } from "./jobsModelJourney";
import type { Hold, Job, Page as JobPage } from "../../../Extensions/Jobs/studio/Elsa.Studio.Jobs/Client/src/types";

type Events = { errors: string[]; console: unknown[]; network: Array<{ path: string; status?: number; failure?: string; reason?: string }>; expected: Map<string, string>; outage?: boolean };
const events = new WeakMap<Page, Events>();
function allow(page: Page, path: string, status: number | "failed", reason: string) { events.get(page)!.expected.set(`${status}:${path}`, reason); }
function observe(page: Page, state: Events) {
  page.on("pageerror", error => state.errors.push(error.message));
  page.on("response", response => { if (response.status() >= 400) { const path = new URL(response.url()).pathname; state.network.push({ path, status: response.status(), reason: state.expected.get(`${response.status()}:${path}`) }); } });
  page.on("requestfailed", request => { const url = new URL(request.url()); const path = url.pathname; state.network.push({ path, failure: request.failure()?.errorText,
    reason: state.expected.get(`failed:${path}`) ?? (state.outage && url.origin === process.env.UX_BACKEND_URL ? "Actual owned Workbench process deliberately stopped; recovered through owned controller" : undefined) }); });
  page.on("console", message => {
    if (message.type() !== "error") return;
    const location = message.location().url; const path = location ? new URL(location, page.url()).pathname : "";
    const identifiedHttp = message.text().startsWith("Failed to load resource:") && [...state.expected.keys()].some(key => key.endsWith(":" + path))
      || Boolean(state.outage && /Failed to fetch|NetworkError|ERR_CONNECTION_REFUSED|Failed to load resource/i.test(message.text()));
    state.console.push({ text: message.text(), path, identifiedHttp });
    if (!identifiedHttp) state.errors.push(message.text());
  });
}
test.beforeEach(async ({ page }, info) => {
  await page.addInitScript(mode => { localStorage.removeItem("elsa-studio-theme"); localStorage.setItem("elsa-studio-workspace-style", "linen"); localStorage.setItem("elsa-studio-theme-mode", mode); }, info.project.use.colorScheme ?? "light");
  await signInAdmin(page);
  const state: Events = { errors: [], console: [], network: [], expected: new Map() }; events.set(page, state); observe(page, state);
  const source = info.outputPath("source.txt"); writeFileSync(source, process.env.UX_SOURCE_SHA ?? "unknown");
  await info.attach("source", { path: source, contentType: "text/plain" });
  const ux = new JobsUx(page, info); await ux.write("POST", "/_ux/jobs/setup", {});
  await captureJobs(page, info, "reference");
});
test.afterEach(async ({ page }, info) => {
  const state = events.get(page); if (!state) return;
  for (const [name, value] of [["console.json", state.console], ["network.json", state.network]] as const) {
    const path = info.outputPath(name); writeFileSync(path, JSON.stringify(value, null, 2)); await info.attach(name, { path, contentType: "application/json" });
  }
  expect(state.errors, "Uncaught or unexplained browser errors").toEqual([]);
  expect(state.network.filter(item => !item.reason), "Unexpected server/network failures; intentional adverse outcomes must be identified").toEqual([]);
});

test("jobs standalone reports", async ({ page }, info) => {
  const ux = new JobsUx(page, info);
  let release!: () => void; const responseDelay = new Promise<void>(resolve => { release = resolve; });
  info.annotations.push({ type: "adverse-control", description: "Loading capture delays one actual successful Jobs GET response after route.fetch; no data/status is fabricated." });
  await page.route(url => url.origin === new URL(ux.base).origin && url.pathname === "/jobs" && url.searchParams.get("queue") === ux.queue, async route => {
    const response = await route.fetch(); expect(response.status()).toBe(200); await responseDelay; await route.fulfill({ response });
  }, { times: 1 });
  try {
    await page.goto(`/jobs?queue=${ux.queue}`); await expect(page.getByLabel("Loading Jobs state", { exact: true })).toBeVisible(); await captureJobs(page, info, "jobs-loading");
  } finally { release(); }
  await expect(page.getByRole("heading", { name: "No matching items on this page", exact: true })).toBeVisible();
  await captureJobs(page, info, "jobs-empty");
  await page.getByRole("link", { name: "Submit job", exact: true }).press("Enter");
  const title = "Invoice totals for " + "international distribution / ".repeat(5) + ux.stamp;
  await fillInvocation(page, reportInvocation(ux.queue, title, { mode: "barrier", tag: ux.stamp }));
  await page.getByLabel(/^Input JSON/).fill('{"amounts":[9007199254740993]}');
  await page.getByRole("button", { name: "Submit independent job", exact: true }).press("Enter");
  await expect(page.getByRole("alert")).toContainText("without changing its value"); await captureJobs(page, info, "job-submit-validation");
  await page.getByLabel(/^Input JSON/).fill(JSON.stringify(reportInvocation(ux.queue, title, { mode: "barrier", tag: ux.stamp }).input.value));
  const accepted = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/jobs" && response.status() === 202);
  await page.getByRole("button", { name: "Submit independent job", exact: true }).press("Enter"); const id = (await (await accepted).json()).jobId as string;
  await expect(page.getByRole("heading", { name: "Job detail", exact: true })).toBeVisible();
  await expect.poll(async () => (await ux.get<{ items: Array<{ state: string }> }>(`/jobs/${id}/attempts`)).items.some(attempt => attempt.state === "running")).toBe(true);
  await ux.refresh(); await expect(page.getByText("Running", { exact: true })).toBeVisible(); await captureJobs(page, info, "job-running");
  await ux.write("POST", "/_ux/jobs/recover/" + encodeURIComponent(ux.stamp), {}); await ux.finish(id); await ux.refresh();
  await page.getByRole("button", { name: "Load permitted result", exact: true }).press("Enter");
  await expect(page.getByRole("region", { name: "Permitted result JSON", exact: true })).toContainText('"total":50');
  await captureJobs(page, info, "job-result");
  await page.reload(); await expect(page.getByRole("region", { name: "Job summary", exact: true })).toContainText(id);
  // A producer sends an exact JSON decimal through the real API. The typed decimal handler
  // persists that same useful total; Studio must render/download retained text without rounding.
  const exact = reportInvocation(ux.queue, `Exact decimal export ${ux.stamp}`, { amounts: ["OWNED_EXACT_DECIMAL"] });
  const raw = JSON.stringify({ idempotencyKey: crypto.randomUUID(), invocation: exact }).replace('"OWNED_EXACT_DECIMAL"', "9007199254740993");
  const exactResponse = await page.request.post(ux.base + "/jobs", { headers: { "Content-Type": "application/json" }, data: raw }); expect(exactResponse.status()).toBe(202);
  const exactId = (await exactResponse.json()).jobId as string; await ux.finish(exactId); await ux.open(exactId);
  await page.getByRole("button", { name: "Load permitted result", exact: true }).press("Enter");
  await expect(page.getByRole("region", { name: "Permitted result JSON", exact: true })).toContainText('"total":9007199254740993');
  const downloading = page.waitForEvent("download"); await page.getByRole("button", { name: "Export permitted result JSON", exact: true }).press("Enter");
  const exported = info.outputPath("report-export.json"); await (await downloading).saveAs(exported);
  const text = readFileSync(exported, "utf8"); expect(text).toContain('"total":9007199254740993'); expect(text).not.toContain('"total":9007199254740992');
  await info.attach("report-export.json", { path: exported, contentType: "application/json" }); await captureJobs(page, info, "job-result-precision");
  // Retained collection context and keyboard return focus use the actual workspace route.
  await page.goto(`/jobs?queue=${ux.queue}&jobType=ux.report&state=Terminal`);
  await page.getByRole("link", { name: id, exact: true }).press("Enter"); await page.getByRole("link", { name: "Back to Jobs", exact: true }).press("Enter");
  await expect(page.getByLabel("Queue", { exact: true })).toHaveValue(ux.queue); await expect(page.getByRole("link", { name: id, exact: true })).toBeFocused();
  await captureJobs(page, info, "jobs-filter-return");
  for (let index = 0; index < 26; index++) await ux.submit(`Paging ${ux.stamp} ${index}`, { delayMilliseconds: 0 });
  await page.goto(`/jobs?queue=${ux.queue}`); await expect(page.getByRole("button", { name: "Next page", exact: true })).toBeEnabled();
  await page.getByRole("button", { name: "Next page", exact: true }).press("Enter"); await expect(page).toHaveURL(/after=/); await captureJobs(page, info, "jobs-page");
});

test("jobs overlapping holds and restart", async ({ page }, info) => {
  const ux = new JobsUx(page, info); const seed = await ux.submit(`Hold control ${ux.stamp}`); await ux.finish(seed.jobId); await ux.open(seed.jobId);
  const type = page.getByRole("button", { name: "Pause Job type (all versions): ux.report…", exact: true });
  await type.press("Enter"); const dialog = page.getByRole("dialog", { name: "Pause jobs", exact: true });
  await expect(dialog.getByRole("textbox")).toBeFocused(); await dialog.getByRole("button", { name: "Cancel", exact: true }).press("Enter");
  await expect(type).toBeFocused(); await ux.prompt(type, "Pause jobs", `Type deployment ${ux.stamp}`, "Add hold"); await captureJobs(page, info, "type-hold");
  const held = await ux.submit(`Retained while paused ${ux.stamp}`); const unrelated = await ux.submit(`Unrelated ${ux.stamp}`, {}, "ux.unrelated");
  await ux.finish(unrelated.jobId); expect((await ux.job(held.jobId)).state).toBe("accepted");
  await ux.open(unrelated.jobId); await page.getByRole("button", { name: "Load permitted result", exact: true }).press("Enter"); await captureJobs(page, info, "unrelated-result");
  const queueHold = await ux.hold({ kind: "queue", name: ux.queue }, `Queue maintenance ${ux.stamp}`);
  await ux.open(held.jobId); await expect(page.getByText(`Type deployment ${ux.stamp}`, { exact: true })).toBeVisible(); await expect(page.getByText(`Queue maintenance ${ux.stamp}`, { exact: true })).toBeVisible();
  await captureJobs(page, info, "overlapping-holds");
  await page.goto(`/jobs/holds?kind=queue&name=${ux.queue}`);
  const row = page.getByRole("row").filter({ hasText: queueHold.holdId });
  await ux.prompt(row.getByRole("button", { name: "Release this hold…", exact: true }), "Release this hold", "Release only queue reason", "Release hold");
  await ux.open(held.jobId); await expect(page.getByText(`Type deployment ${ux.stamp}`, { exact: true })).toBeVisible(); expect((await ux.job(held.jobId)).state).toBe("accepted"); await captureJobs(page, info, "remaining-holds");
  await ux.restart(); await ux.open(held.jobId); await expect(page.getByText(`Type deployment ${ux.stamp}`, { exact: true })).toBeVisible();
  const holds = await ux.get<JobPage<Hold>>("/jobs/holds?kind=jobType&name=ux.report&limit=100");
  const active = holds.items.filter(item => item.releasedAt == null); expect(active).toHaveLength(1); await ux.release(active[0]); await ux.finish(held.jobId);
  await ux.refresh(); await expect(page.getByRole("region", { name: "Job summary", exact: true })).toContainText("succeeded"); await captureJobs(page, info, "after-restart");
});

test("jobs native and BPMN continuation", async ({ page }, info) => jobsModelJourney(page, info, captureJobs));

test("jobs recovery and operation audit", async ({ page }, info) => {
  const ux = new JobsUx(page, info);
  const backoff = await ux.submit(`Retry backoff ${ux.stamp}`, { mode: "fail-once" });
  await expect.poll(async () => { const job = await ux.job(backoff.jobId); return job.state === "accepted" && job.revision > 1; }).toBe(true);
  await ux.open(backoff.jobId); await expect(page.getByText(/retry backoff/)).toBeVisible(); await captureJobs(page, info, "backoff"); await ux.finish(backoff.jobId);
  const failed = await ux.submit(`Recover report ${ux.stamp}`, { mode: "recoverable", tag: ux.stamp }); const old = await ux.finish(failed.jobId, "failed"); await ux.open(failed.jobId); await captureJobs(page, info, "failed-history");
  const stale = await page.request.post(`${ux.base}/jobs/${failed.jobId}/retry`, { data: { expectedRevision: old.revision - 1, idempotencyKey: crypto.randomUUID(), operationId: crypto.randomUUID(), reason: "Stale revision control" } }); expect(stale.status()).toBe(409);
  await ux.write("POST", "/_ux/jobs/recover/" + encodeURIComponent(ux.stamp), {});
  await ux.prompt(page.getByRole("button", { name: "Retry as new run…", exact: true }), "Retry as new run", `Recover linked ${ux.stamp}`, "Create new run");
  const linked = page.getByRole("link", { name: /^Open new linked job / }); await expect(linked).toBeVisible();
  const newId = (await linked.getAttribute("href"))!.split("/").at(-1)!; await ux.finish(newId); expect((await ux.job(failed.jobId)).resultId).toBe(old.resultId);
  await linked.press("Enter"); await expect(page.getByRole("link", { name: "Open original job " + failed.jobId, exact: true })).toBeVisible(); await captureJobs(page, info, "retry-linked");
  const cancel = await ux.submit(`Cancellation ${ux.stamp}`, { mode: "barrier", tag: "cancel-" + ux.stamp });
  await expect.poll(async () => (await ux.job(cancel.jobId)).state).toBe("attemptActive"); await ux.open(cancel.jobId);
  await ux.prompt(page.getByRole("button", { name: "Request cancellation…", exact: true }), "Request job cancellation", "Cooperative stop", "Request cancellation");
  await ux.finish(cancel.jobId, "cancelled"); await ux.refresh(); await captureJobs(page, info, "cancellation");
  const invocation = reportInvocation(ux.queue, `Late receipt ${ux.stamp}`, { mode: "late", delayMilliseconds: 4000 }); invocation.policy.attemptTimeout = "00:00:01"; invocation.policy.retry.maximumAttempts = 1;
  const late = await ux.write<{ jobId: string }>("POST", "/jobs", { idempotencyKey: crypto.randomUUID(), invocation }); const timeout = await ux.finish(late.jobId, "timedOut");
  await expect.poll(async () => (await ux.get<{ items: unknown[] }>(`/jobs/${late.jobId}/physical-receipts`)).items.length, { timeout: 15_000 }).toBeGreaterThan(0);
  expect((await ux.job(late.jobId)).resultId).toBe(timeout.resultId); await ux.open(late.jobId); await captureJobs(page, info, "late-receipt");
  // A real successful backend write whose response is discarded, not a fabricated HTTP failure.
  let operationId = ""; allow(page, "/jobs/holds", "failed", "Intentional committed-response loss after route.fetch");
  await page.route(ux.base + "/jobs/holds", async route => { operationId = route.request().postDataJSON().operationId; const response = await route.fetch(); expect(response.status()).toBe(200); await route.abort("failed"); }, { times: 1 });
  await ux.prompt(page.getByRole("button", { name: `Pause Queue: ${ux.queue}…`, exact: true }), "Pause jobs", `Lost reply ${ux.stamp}`, "Add hold");
  await expect(page.getByText(/This outcome is unconfirmed/)).toBeVisible(); await captureJobs(page, info, "unconfirmed-operation");
  await page.getByRole("button", { name: "Retry original operation", exact: true }).press("Enter"); await expect(page.getByText(/This outcome is unconfirmed/)).toBeHidden();
  const audit = await ux.get<{ actor: string; reason: string }>("/jobs/operations/" + operationId); const access = await ux.get<{ subjectId: string }>("/jobs/access");
  expect(audit.actor).toBe(access.subjectId); expect(audit.reason).toBe(`Lost reply ${ux.stamp}`);
  const holds = await ux.get<JobPage<Hold>>(`/jobs/holds?kind=queue&name=${ux.queue}&limit=100`); const exact = holds.items.filter(item => item.reason === audit.reason); expect(exact).toHaveLength(1); await ux.release(exact[0]);
  await page.goto("/jobs/operations"); await expect(page.getByRole("region", { name: "Operation audit table", exact: true })).toBeVisible(); await captureJobs(page, info, "operation-audit");
  const observed = events.get(page)!; observed.outage = true;
  try {
    await ux.control("stop"); await page.goto("/jobs");
    await expect(page.getByText(/unavailable|unable to connect|not connected|connection failed/i).first()).toBeVisible({ timeout: 30_000 });
    await captureJobs(page, info, "backend-unavailable");
  } finally {
    await ux.control("start"); await signInAdmin(page); await ux.get("/jobs/access"); observed.outage = false;
  }
});

test("jobs recurring schedule policies", async ({ page }, info) => {
  const ux = new JobsUx(page, info); const before = new Set((await ux.allJobs()).map(job => job.jobId));
  let typeHold = await ux.hold({ kind: "jobType", name: "ux.report" }, `Hold schedule cursor ${ux.stamp}`);
  const id = "schedule-" + ux.stamp; await page.goto("/jobs/schedules/create"); await fillInvocation(page, reportInvocation(ux.queue, id));
  await page.getByLabel("Schedule ID", { exact: true }).fill(id); await page.getByLabel("Reason", { exact: true }).fill("Create one catch-up report");
  await page.getByLabel(/^First occurrence \(UTC\)/).fill("invalid"); await page.getByRole("button", { name: "Review and create schedule", exact: true }).press("Enter");
  await expect(page.getByRole("alert")).toContainText("exact UTC"); await captureJobs(page, info, "schedule-validation");
  const first = new Date(Date.now() - 48 * 3600000).toISOString(); await page.getByLabel(/^First occurrence \(UTC\)/).fill(first);
  await expect(page.getByLabel(/^Missed occurrences/)).toHaveValue("coalesceOne"); await captureJobs(page, info, "schedule-coalescing");
  await page.getByRole("button", { name: "Review and create schedule", exact: true }).press("Enter"); await page.getByRole("dialog", { name: "Create schedule?", exact: true }).getByRole("button", { name: "Create schedule", exact: true }).press("Enter");
  await expect.poll(async () => Date.parse((await ux.schedule(id)).firstOccurrence!)).toBe(Date.parse(first));
  await ux.release(typeHold); await expect.poll(async () => (await ux.allJobs()).filter(job => !before.has(job.jobId)).length, { timeout: 30_000 }).toBe(1); await ux.disable(id);
  const caught = (await ux.allJobs()).find(job => !before.has(job.jobId))!; await ux.finish(caught.jobId);
  await page.goto(`/jobs/schedules/${id}/edit`); await expect(page.getByLabel(/^Input JSON/)).toHaveValue(JSON.stringify(reportInvocation(ux.queue, id).input.value));
  await page.getByLabel(/^Recurrence expression/).fill("PT2H"); await page.getByLabel("Reason", { exact: true }).fill("Change future interval; retain history"); await captureJobs(page, info, "schedule-reopened");
  await page.getByRole("button", { name: "Review and save schedule", exact: true }).press("Enter"); await page.getByRole("dialog", { name: "Save schedule revision?", exact: true }).getByRole("button", { name: "Save schedule", exact: true }).press("Enter");
  expect((await ux.job(caught.jobId)).outcome).toBe("succeeded");
  typeHold = await ux.hold({ kind: "jobType", name: "ux.report" }, "Review replay backlog"); const replay = "replay-" + ux.stamp; const ids = new Set((await ux.allJobs()).map(job => job.jobId));
  await page.goto("/jobs/schedules/create"); await fillInvocation(page, reportInvocation(ux.queue, replay)); await page.getByLabel("Schedule ID", { exact: true }).fill(replay);
  await page.getByLabel(/^First occurrence \(UTC\)/).fill(new Date(Date.now() - 3 * 3600000).toISOString()); await page.getByLabel(/^Missed occurrences/).selectOption("replayEveryOccurrence");
  await page.getByLabel("Reason", { exact: true }).fill("Explicitly review four owed reports"); await page.getByRole("button", { name: "Review and create schedule", exact: true }).press("Enter");
  const dialog = page.getByRole("dialog", { name: "Create schedule?", exact: true }); await expect(dialog).toContainText("large durable backlog"); await captureJobs(page, info, "schedule-replay-review");
  await dialog.getByRole("button", { name: "Create schedule", exact: true }).press("Enter"); await ux.release(typeHold);
  await expect.poll(async () => (await ux.allJobs()).filter(job => !ids.has(job.jobId)).length, { timeout: 30_000 }).toBe(4); await ux.disable(replay);
  await page.goto(`/jobs/schedules/${replay}`); await expect(page.getByText(/Accepted jobs and owed occurrences/)).toBeVisible(); await captureJobs(page, info, "schedule-history");
  expect((await ux.job(caught.jobId)).outcome).toBe("succeeded");
});

test("jobs current permissions and tenants", async ({ page, browser }, info) => {
  const ux = new JobsUx(page, info); const own = await ux.submit(`PRIMARY_TENANT_PAYLOAD_${ux.stamp}`); await ux.finish(own.jobId);
  const cases = [["jobs-reader", "default", "read-only"], ["jobs-submitter", "default", "submit-only"], ["jobs-auditor", "default", "audit-only"], ["jobs-denied", "default", "denied"], ["jobs-other", "ux-jobs-secondary", "foreign-tenant"]] as const;
  for (const [username, tenantId, capture] of cases) {
    const context = await browser.newContext({ viewport: info.project.use.viewport, colorScheme: info.project.use.colorScheme });
    try {
      const login = await context.request.post(ux.base + "/_elsa/identity/login", { data: { username, password: "Password123!", tenantId } }); expect(login.status()).toBe(200);
      const other = await context.newPage(); await other.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), info.project.use.colorScheme ?? "light");
      const state = events.get(page)!; observe(other, state);
      const refused = username === "jobs-reader" ? ["/jobs/" + own.jobId + "/result", "/jobs/" + own.jobId + "/input"] : [];
      for (const path of refused) expect((await context.request.get(ux.base + path)).status()).toBe(403);
      if (username === "jobs-reader") {
        await other.goto(process.env.ELSA_STUDIO_URL + "/jobs"); await expect(other.getByRole("heading", { name: "Jobs", exact: true })).toBeVisible(); await expect(other.getByRole("link", { name: "Submit job", exact: true })).toHaveCount(0);
        await other.goto(process.env.ELSA_STUDIO_URL + "/jobs/" + own.jobId);
        await expect(other.getByRole("region", { name: "Job summary", exact: true })).toContainText(own.jobId);
        await expect(other.getByRole("button", { name: "Load permitted result", exact: true })).toHaveCount(0);
      } else if (username === "jobs-submitter") {
        state.expected.set("403:/jobs", "Actual submit-only collection denial"); await other.goto(process.env.ELSA_STUDIO_URL + "/jobs"); await expect(other.getByRole("heading", { name: "Jobs access denied", exact: true })).toBeVisible();
        await other.getByRole("link", { name: "Submit job", exact: true }).press("Enter"); await expect(other.getByRole("heading", { name: "Submit job", exact: true })).toBeVisible();
        const submitted = await ux.write<{ jobId: string }>("POST", "/jobs", { idempotencyKey: crypto.randomUUID(), invocation: reportInvocation(ux.queue, "Allowed submission without collection read") }, context.request);
        await ux.finish(submitted.jobId);
      } else if (username === "jobs-auditor") {
        await other.goto(process.env.ELSA_STUDIO_URL + "/jobs/operations"); await expect(other.getByRole("heading", { name: "Operation audit", exact: true })).toBeVisible();
      } else if (username === "jobs-denied") {
        state.expected.set("403:/jobs/access", "Actual principal without Jobs permissions"); await other.goto(process.env.ELSA_STUDIO_URL + "/jobs"); await expect(other.getByRole("heading", { name: "Jobs access denied", exact: true })).toBeVisible();
      } else {
        const inaccessible = await context.request.get(`${ux.base}/jobs/${own.jobId}`); expect(inaccessible.status()).toBe(404); expect(await inaccessible.text()).not.toContain(own.jobId);
        const foreign = await ux.write<{ jobId: string }>("POST", "/jobs", { idempotencyKey: crypto.randomUUID(), invocation: reportInvocation(ux.queue, `SECONDARY_TENANT_PAYLOAD_${ux.stamp}`) }, context.request);
        expect((await page.request.get(`${ux.base}/jobs/${foreign.jobId}`)).status()).toBe(404);
        const list = await ux.get<JobPage<Job>>("/jobs?limit=100", context.request); expect(list.items.some(job => job.jobId === own.jobId)).toBe(false);
        await other.goto(process.env.ELSA_STUDIO_URL + "/jobs"); await expect(other.locator("body")).not.toContainText(own.jobId);
        // Real cross-tenant direct reads and all mutation families refuse or address only the actual tenant.
        for (const suffix of ["input", "result", "attempts", "physical-receipts", "holds"]) {
          const response = await context.request.get(`${ux.base}/jobs/${own.jobId}/${suffix}`); expect(response.status()).toBe(404);
          const body = await response.text(); expect(body).not.toContain(own.jobId); expect(body).not.toContain("PRIMARY_TENANT_PAYLOAD_" + ux.stamp);
        }
        for (const suffix of ["cancel", "retry"]) expect((await context.request.post(`${ux.base}/jobs/${own.jobId}/${suffix}`, { data: { operationId: crypto.randomUUID(), reason: "Foreign resource must remain missing", ...(suffix === "retry" ? { expectedRevision: 1, idempotencyKey: crypto.randomUUID() } : {}) } })).status()).toBe(404);
      }
      if (username !== "jobs-other") {
        expect((await context.request.post(ux.base + "/_ux/jobs/setup", { data: {} })).status()).toBe(403);
        expect((await context.request.post(ux.base + "/_ux/jobs/recover/foreign-control", { data: {} })).status()).toBe(403);
        expect((await context.request.post(ux.base + "/_ux/jobs/revoke-reader", { data: {} })).status()).toBe(403);
        for (const [method, path, data] of [["POST", `/jobs/${own.jobId}/cancel`, { operationId: crypto.randomUUID(), reason: "Denied mutation" }], ["POST", `/jobs/${own.jobId}/retry`, { expectedRevision: 1, idempotencyKey: crypto.randomUUID(), operationId: crypto.randomUUID(), reason: "Denied retry" }], ["POST", "/jobs/holds", { gate: { kind: "queue", name: ux.queue }, holdId: crypto.randomUUID(), operationId: crypto.randomUUID(), reason: "Denied pause" }], ["PUT", "/jobs/capacity", { gate: { kind: "queue", name: ux.queue }, limit: 0, expectedRevision: 0, operationId: crypto.randomUUID(), reason: "Denied capacity" }], ["PUT", "/jobs/schedules/denied", { invocation: reportInvocation(ux.queue, "Denied schedule"), recurrence: { kind: "interval", expression: "PT1H" }, firstOccurrence: new Date().toISOString(), missedPolicy: "coalesceOne", expectedRevision: 0, operationId: crypto.randomUUID(), reason: "Denied schedule" }]] as const) {
          expect((await context.request.fetch(ux.base + path, { method, data })).status()).toBe(403);
        }
        for (const [path, data] of [["/jobs/holds/owned-denied/release", { gate: { kind: "queue", name: ux.queue }, expectedRevision: 1, operationId: crypto.randomUUID(), reason: "Denied release" }], ["/jobs/schedules/owned-denied/enabled", { enabled: false, expectedRevision: 1, operationId: crypto.randomUUID(), reason: "Denied schedule toggle" }]] as const)
          expect((await context.request.post(ux.base + path, { data })).status()).toBe(403);
        if (username !== "jobs-submitter") expect((await context.request.post(ux.base + "/jobs", { data: { idempotencyKey: crypto.randomUUID(), invocation: reportInvocation(ux.queue, "Denied submission") } })).status()).toBe(403);
      }
      await expect(other.locator("body")).not.toContainText("PRIMARY_TENANT_PAYLOAD_" + ux.stamp); await captureJobs(other, info, capture);
      if (username === "jobs-reader") {
        await ux.write("POST", "/_ux/jobs/revoke-reader", {});
        // Keep the original signed-in cookie: current directory denial must override its old read claim.
        expect((await context.request.get(`${ux.base}/jobs/${own.jobId}`)).status()).toBe(403);
        state.expected.set("403:/jobs", "Actual IAM reader permission revocation with the original cookie");
        await other.getByRole("button", { name: "Refresh", exact: true }).press("Enter");
        await expect(other.getByRole("heading", { name: "Jobs access denied", exact: true })).toBeVisible();
        await expect(other.locator("body")).not.toContainText(own.jobId); await captureJobs(other, info, "current-revoked");
      }
    } finally { await context.close(); }
  }
  expect((await ux.job(own.jobId)).outcome).toBe("succeeded");
});
