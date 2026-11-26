import { expect, test, type Page } from "@playwright/test";

// Weaver session recovery journeys (#3441 T022) over a durable fake Weaver backend that survives reloads and is
// shared by tabs of one browser context. Requests go through the real Studio Weaver client.
type FixtureWindow = { weaverFixture: { seed(sessions: unknown[]): string[]; log(): string[]; release(): void; state(): { sessions: { id: string; continuedFrom?: string; messages: { id: string; status: string; statusReason?: string }[]; proposals: { id: string; status: string; ownerResolution?: { claim: string } }[] }[] }; target: unknown } };

async function open(page: Page, query = "") {
  await page.goto(`/weaver-recovery.html${query}`);
  await expect(dock(page).getByTestId("weaver-provider-status")).toContainText("Agentic");
}

const dock = (page: Page) => page.getByRole("region", { name: "Weaver assistant" });
const log = (page: Page) => page.evaluate(() => (window as unknown as FixtureWindow).weaverFixture.log());
const seed = (page: Page, sessions: unknown[]) => page.evaluate(items => (window as unknown as FixtureWindow).weaverFixture.seed(items), sessions);
const message = (page: Page, id: string) => dock(page).locator(`.weaver-message[data-message-id="${id}"]`);

async function send(page: Page, text: string) {
  await dock(page).getByLabel("Ask Weaver").fill(text);
  await dock(page).getByRole("button", { name: "Send message" }).click();
}

test.beforeEach(async ({ page }) => {
  await page.goto("/weaver-recovery.html");
  await page.evaluate(() => localStorage.clear());
});

test("reload returns to the same session with its pending review, decidable once", async ({ page }) => {
  await open(page);
  await send(page, "Rename the sample [review]");
  const proposal = dock(page).locator(".weaver-proposal");
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");

  await page.reload();
  await expect(dock(page).getByText("Rename the sample [review]")).toBeVisible();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  await expect(dock(page).locator(".weaver-auto-apply")).toHaveAttribute("data-autonomy-state", "acknowledged");
  await expect(dock(page).getByLabel("Ask Weaver")).toBeDisabled();
  expect(await log(page)).toEqual(["create", "message", "stream"]);

  await proposal.getByRole("button", { name: "Approve" }).click();
  await expect(proposal).toHaveAttribute("data-status", "approved");
  await page.reload();
  await expect(proposal).toHaveAttribute("data-status", "approved");
  expect((await log(page)).filter(entry => entry === "approve")).toHaveLength(1);
  expect((await log(page)).filter(entry => entry === "create")).toHaveLength(1);
});

test("closing and reopening the dock shows the same session without reopening its stream", async ({ page }) => {
  await open(page);
  await send(page, "Explain the sample");
  await expect(dock(page).getByText("Answer to: Explain the sample")).toBeVisible();
  await dock(page).getByRole("button", { name: "Close assistant" }).click();
  await expect(dock(page)).toHaveCount(0);
  await page.getByRole("button", { name: "Show Weaver" }).click();
  await expect(dock(page).getByText("Answer to: Explain the sample")).toBeVisible();
  await expect(dock(page).getByRole("button", { name: "New conversation" })).toBeVisible();
  expect(await log(page)).toEqual(["create", "message", "stream"]);
});

test("a dropped stream shows the turn as cancelled after return and resubmits nothing", async ({ page }) => {
  await open(page);
  await send(page, "Summarize the sample [drop]");
  await expect(dock(page).getByRole("alert")).toContainText("The connection to Weaver was lost.");
  await page.reload();
  const prompt = dock(page).locator(".weaver-message[data-role='user']");
  await expect(prompt).toHaveAttribute("data-status", "cancelled");
  await expect(prompt).toContainText("Cancelled. The turn's stream closed before it finished. Nothing was sent again.");
  await expect(dock(page).locator(".weaver-message[data-role='assistant']")).toContainText("Not completed. This partial answer was not accepted.");
  await expect(prompt.getByRole("button")).toHaveCount(0);
  expect(await log(page)).toEqual(["create", "message", "stream"]);
});

test("a not-started prompt is reached from the list, started once on request, or withdrawn", async ({ page }) => {
  const [first] = await seed(page, [{ messages: [{ id: "m1", role: "user", content: "Summarize the draft", status: "pending" }] }]);
  await open(page);
  // At most two interactions: choose the session, then decide.
  const offered = dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button");
  await expect(offered).toHaveCount(1);
  await offered.click();
  await expect(message(page, "m1")).toContainText("Not started.");
  expect(await log(page)).toEqual([]);
  await message(page, "m1").getByRole("button", { name: "Start" }).click();
  await expect(dock(page).getByText("Answer to: Summarize the draft")).toBeVisible();
  expect(await log(page)).toEqual(["stream"]);

  await seed(page, [{ messages: [{ id: "m2", role: "user", content: "Rename everything", status: "pending" }] }]);
  await page.reload();
  await expect(dock(page).getByText("Answer to: Summarize the draft")).toBeVisible();
  await dock(page).getByRole("button", { name: "New conversation" }).click();
  await dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button").first().click();
  await message(page, "m2").getByRole("button", { name: "Withdraw" }).click();
  await expect(message(page, "m2")).toHaveAttribute("data-status-reason", "withdrawn");
  await expect(message(page, "m2")).toContainText("Withdrawn before it started.");
  await page.reload();
  await expect(message(page, "m2")).toContainText("Withdrawn before it started.");
  expect(await log(page)).toEqual(["stream", "withdraw"]);
  expect(first).toBe("agt_1");
});

test("the owner resolves an uncertain client change, and the statement is kept as a statement", async ({ page }) => {
  await seed(page, [{ proposals: [{ id: "prop_u", title: "Move sample", summary: "Move the sample in the editor.", risk: "reviewRequired", status: "uncertain", baseRevision: "r1",
    resourceTarget: { moduleId: "Sample", resourceType: "sample", resourceId: "one", revision: "r1" },
    preparedInvocation: { invocationId: "inv_u", sessionId: "agt_1", turnId: "turn", toolCallId: "call", toolName: "sample_change", toolContractVersion: "1", moduleId: "Sample", actorId: "ada", tenantId: "tenant", target: null, arguments: {}, approvalDigest: "digest", risk: "reviewRequired", preview: { typeId: "sample.preview", schemaVersion: "1", data: {} }, executionLocation: "clientWorkingState", requiresApproval: true, client: { clientId: "closed-tab", generationId: "g" }, expiresAt: "2026-01-01T00:00:00Z" } }] }]);
  await open(page);
  await dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button").click();
  const proposal = dock(page).locator(".weaver-proposal");
  await expect(proposal).toHaveAttribute("data-status", "uncertain");
  await proposal.getByRole("group", { name: "Resolve uncertain change" }).getByRole("button", { name: "It was not applied" }).click();
  await expect(proposal).toContainText("You stated that this change was not applied. This is your statement, not a receipt");
  await page.reload();
  await expect(proposal).toContainText("You stated that this change was not applied.");
  await expect(proposal.getByRole("group", { name: "Resolve uncertain change" })).toHaveCount(0);
  expect(await log(page)).toEqual(["resolve:notApplied"]);
});

test("a second tab neither refuses nor marks the first tab's client delivery", async ({ page, context }) => {
  await open(page);
  await send(page, "Move the sample [client]");
  const proposalA = dock(page).locator(".weaver-proposal");
  await proposalA.getByRole("button", { name: "Approve" }).click();
  await proposalA.getByRole("button", { name: "Apply" }).click();
  await expect.poll(() => log(page)).toContain("execute");

  const tabB = await context.newPage();
  await open(tabB);
  const proposalB = dock(tabB).locator(".weaver-proposal");
  await expect(proposalB).toHaveAttribute("data-status", "awaiting-client");
  await expect(proposalB).toContainText("Waiting for the Studio tab or page that started this change.");
  for (const button of await proposalB.getByRole("button").all()) await expect(button).toBeDisabled();
  const beforeRelease = await log(tabB);
  expect(beforeRelease.filter(entry => entry.startsWith("ack") || entry.startsWith("resolve"))).toEqual([]);

  await page.evaluate(() => (window as unknown as FixtureWindow).weaverFixture.release());
  await expect(proposalA).toHaveAttribute("data-status", "executed");
  expect((await log(page)).filter(entry => entry.startsWith("ack"))).toEqual(["ack:applied"]);
  await tabB.reload();
  await expect(dock(tabB).locator(".weaver-proposal")).toHaveAttribute("data-status", "executed");
});

test("after a reload an approved client change whose page is gone is refused with zero effect", async ({ page }) => {
  await open(page);
  await send(page, "Move the sample [client]");
  await dock(page).locator(".weaver-proposal").getByRole("button", { name: "Approve" }).click();
  await expect(dock(page).locator(".weaver-proposal")).toHaveAttribute("data-status", "approved");
  await page.goto("/weaver-recovery.html?target=gone");
  const proposal = dock(page).locator(".weaver-proposal");
  await expect(proposal).toHaveAttribute("data-status", "approved");
  await proposal.getByRole("button", { name: "Apply" }).click();
  await expect(proposal).toHaveAttribute("data-status", "failed");
  expect((await log(page)).filter(entry => entry.startsWith("ack"))).toEqual(["ack:refused"]);
  const acknowledged = await page.evaluate(() => (window as unknown as FixtureWindow).weaverFixture.state().sessions[0].proposals[0].status);
  expect(acknowledged).toBe("failed");
});

test("another user's session is never listed or opened", async ({ page }) => {
  await seed(page, [{ owner: "grace", messages: [{ id: "g1", role: "user", content: "Grace's private question", status: "completed" }] }]);
  await open(page);
  await expect(dock(page).getByRole("list", { name: "Recent conversations" })).toHaveCount(0);
  await expect(dock(page).getByText("Grace's private question")).toHaveCount(0);

  await open(page, "?user=grace");
  await dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button").click();
  await expect(dock(page).getByText("Grace's private question")).toBeVisible();
  await open(page, "?user=ada");
  await expect(dock(page).getByText("Grace's private question")).toHaveCount(0);
  await expect(dock(page).getByRole("list", { name: "Recent conversations" })).toHaveCount(0);
});

test("a session limit is explained and continues in a new session that carries over only a link", async ({ page }) => {
  await open(page);
  await send(page, "Keep reading everything [limit]");
  const notice = dock(page).locator('[data-kind="limit"]');
  // The bound is the server's own words; Studio adds only what the continuation carries over.
  await expect(notice).toHaveAttribute("data-limit", "bytes");
  await expect(notice).toContainText("Session limit reached: this conversation has recorded 8 MiB of recorded disclosure. Continue in a new session; nothing recorded here was discarded. It stays readable");
  await notice.getByRole("button", { name: "Continue in a new session" }).click();
  await expect(dock(page).getByText("Only a link to it was carried over")).toBeVisible();
  await expect(dock(page).getByText("Keep reading everything [limit]")).toHaveCount(0);
  const sessions = await page.evaluate(() => (window as unknown as FixtureWindow).weaverFixture.state().sessions);
  expect(sessions.map(item => [item.id, item.continuedFrom ?? null, item.messages.length])).toEqual([["agt_1", null, 1], ["agt_2", "agt_1", 0]]);

  // The new session is the one a reload returns to; the limited one stays readable from the list.
  await page.reload();
  await expect(dock(page).getByRole("button", { name: "New conversation" })).toBeVisible();
  await expect(dock(page).getByText("Keep reading everything [limit]")).toHaveCount(0);
  await dock(page).getByRole("button", { name: "New conversation" }).click();
  await dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button").last().click();
  await expect(dock(page).getByText("Keep reading everything [limit]")).toBeVisible();
  expect((await log(page)).filter(entry => entry === "create")).toHaveLength(2);
});

test("the recovery list and outcomes fit a narrow viewport", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  await seed(page, [{ messages: [{ id: "m1", role: "user", content: "Summarize the draft", status: "cancelled", statusReason: "interrupted" }] }]);
  await open(page);
  await dock(page).getByRole("list", { name: "Recent conversations" }).getByRole("button").click();
  await expect(message(page, "m1")).toContainText("Interrupted.");
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
});
