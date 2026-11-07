// @vitest-environment jsdom
import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { JobDetail, attemptLabel, consumptionText } from "../JobDetail";
import { ScheduleEditor, SubmitPage } from "../Editors";
import { OperationAuditPage } from "../OperationAuditPage";
import { JobsPage } from "../JobsPage";
import { MutationNotice, ReadState } from "../Shared";
import { JobsFault } from "../jobsApi";
import { defaultPolicy, permissions, type Attempt, type Page, type Schedule } from "../types";
import { useJobsMutation, useJobsRead, type JobsCommand } from "../useJobs";
import { act, click, control, fixture, input, job, mount, schedule, submit } from "./support";

let view: ReturnType<typeof mount>;
beforeEach(() => { window.history.replaceState({}, "", "/jobs"); view = mount(); });
afterEach(async () => { await view.dispose(); });
function MutationHarness({ api, command, confirmed }: { api: ElsaStudioModuleApi; command: JobsCommand; confirmed(result: unknown): void }) {
  const mutation = useJobsMutation(api, confirmed);
  return <><button disabled={mutation.blocked} onClick={() => void mutation.run(command)}>Start operation</button><MutationNotice mutation={mutation} /></>;
}
function ReadHarness({ api, resource }: { api: ElsaStudioModuleApi; resource: string }) {
  const read = useJobsRead(api, resource, client => client.read<Page<{ id: string }>>("jobs", [resource]));
  return <ReadState {...read} retry={() => void read.refresh()}><p>{read.value?.items.map(value => value.id).join(",")}</p></ReadState>;
}
function detailFixture() {
  const f = fixture(); f.get.mockImplementation(async path => path === "/jobs/report-one" ? job : path.includes("/attempts") ? { items: [], next: null } : path.includes("/physical-receipts") ? { items: [], next: null } : path.startsWith("/jobs/report-one/holds") ? { gates: [], complete: true } : { items: [], next: null });
  return f;
}

describe("durable operation confirmation controls", () => {
  it.each(["pause", "release", "cancel", "retry", "schedule", "submit", "capacity"])("replays the unchanged %s request after ambiguous failure and a later refusal", async kind => {
    const f = fixture(); const confirmed = vi.fn();
    const operation = { operationId: "original-operation", reason: "Original reason" }; const gate = { kind: "queue", name: "reports" };
    const invocation = { handler: { jobType: "reports", version: "1" }, queue: "reports", input: { schema: { alias: "input", version: 1 }, value: { value: "ORIGINAL_INPUT" } }, resultSchema: { alias: "result", version: 1 }, policy: defaultPolicy };
    const shapes: Record<string, Omit<JobsCommand, "title">> = {
      pause: { method: "POST", relation: "holds", segments: [], permission: permissions.pause, body: { ...operation, gate, holdId: "original-hold" } },
      release: { method: "POST", relation: "holds", segments: ["original-hold", "release"], permission: permissions.pause, body: { ...operation, gate, expectedRevision: 1 } },
      cancel: { method: "POST", relation: "jobs", segments: ["report-one", "cancel"], permission: permissions.cancel, body: operation },
      retry: { method: "POST", relation: "jobs", segments: ["report-one", "retry"], permission: permissions.retry, body: { ...operation, expectedRevision: 3, idempotencyKey: "original-retry" } },
      schedule: { method: "PUT", relation: "schedules", segments: ["hourly"], permission: permissions.schedules, body: { ...operation, invocation, recurrence: { kind: "interval", expression: "PT1H" }, firstOccurrence: "2026-11-06T12:00:00Z", missedPolicy: "coalesceOne", expectedRevision: 0 } },
      submit: { method: "POST", relation: "jobs", segments: [], permission: permissions.submit, body: { idempotencyKey: "original-producer", invocation } },
      capacity: { method: "PUT", relation: "capacity", segments: [], permission: permissions.capacity, body: { ...operation, gate, limit: 2, expectedRevision: 0 } }
    };
    const command: JobsCommand = { ...shapes[kind], title: "Operation confirmed" };
    const write = command.method === "PUT" ? f.put : f.post;
    write.mockRejectedValueOnce(new StudioHttpError(0, "Lost reply")).mockRejectedValueOnce(new StudioHttpError(403, "Current refusal")).mockResolvedValue({ status: "replayed", jobId: "new-job" });
    await view.render(<MutationHarness api={f.api} command={command} confirmed={confirmed} />); await click(view.container, "Start operation");
    expect(view.container.textContent).toContain("unconfirmed");
    const original = structuredClone(command.body);
    command.segments.push("CALLER_CHANGED_ADDRESS");
    if (kind === "submit") (command.body as { invocation: typeof invocation }).invocation.input.value.value = "MUTATED_CALLER_INPUT";
    else (command.body as typeof operation).reason = "Changed caller reason";
    await click(view.container, "Retry original operation"); await click(view.container, "Retry original operation");
    expect(write).toHaveBeenCalledTimes(3); expect(write.mock.calls[0]).toEqual(write.mock.calls[1]); expect(write.mock.calls[0]).toEqual(write.mock.calls[2]);
    expect(write.mock.calls[2][1]).toEqual(original); expect(confirmed).toHaveBeenCalledTimes(1);
    expect(f.notify).toHaveBeenCalledWith(expect.objectContaining({ level: "success", link: { label: "Open job", path: "/jobs/new-job" } }));
  });
  it("checks current permission before writing even if a stale view exposed its button", async () => {
    const f = fixture(); f.identity.permissions = [permissions.read];
    await view.render(<MutationHarness api={f.api} command={{ method: "POST", relation: "holds", segments: [], permission: permissions.pause, title: "Paused", body: { operationId: "x", reason: "Reason" } }} confirmed={vi.fn()} />);
    await click(view.container, "Start operation"); expect(f.post).not.toHaveBeenCalled(); expect(view.container.textContent).toContain("current permissions");
  });
  it("ignores an older authorized page after a newer denied scope response", async () => {
    const f = fixture(); let release!: (value: unknown) => void;
    f.get.mockImplementation(path => path.endsWith("/old") ? new Promise(resolve => { release = resolve; }) : Promise.reject(new JobsFault(403)));
    await view.render(<ReadHarness api={f.api} resource="old" />);
    await view.render(<ReadHarness api={f.api} resource="new" />); await act(() => release({ items: [{ id: "DENIED_SCOPE_CANARY" }] }));
    expect(view.container.textContent).toContain("Jobs access denied"); expect(view.container.textContent).not.toContain("DENIED_SCOPE_CANARY");
  });
  it("offers allowed standalone submission to an actor without collection-read permission", async () => {
    const f = fixture(); f.identity.permissions = [permissions.submit];
    await view.render(<JobsPage api={f.api} navigate={vi.fn()} />);
    expect(view.container.textContent).toContain("Jobs access denied"); expect(view.container.querySelector('a[href$="/jobs/submit"]')).not.toBeNull(); expect(f.get).not.toHaveBeenCalled();
  });
  it("retains exact server filters and cursor on detail return and restores focus to the selected job", async () => {
    const f = fixture(); const navigate = vi.fn();
    window.history.replaceState({}, "", "/jobs?queue=reports&jobType=reports&state=Terminal&after=opaque%3D%3D&focus=report-one");
    f.get.mockResolvedValue({ items: [job], next: "next+/==" });
    await view.render(<JobsPage api={f.api} navigate={navigate} />);
    const row = view.container.querySelector<HTMLAnchorElement>('[data-job-id="report-one"]')!; expect(document.activeElement).toBe(row);
    const returnTo = new URL(row.href).searchParams.get("returnTo")!;
    expect(new URL(returnTo, window.location.origin).searchParams.get("after")).toBe("opaque==");
    expect(new URL(returnTo, window.location.origin).searchParams.get("queue")).toBe("reports");
    await click(view.container, "Next page");
    const next = new URL(navigate.mock.calls[0][0], window.location.origin); expect(next.searchParams.get("after")).toBe("next+/=="); expect(next.searchParams.get("jobType")).toBe("reports"); expect(next.searchParams.get("state")).toBe("Terminal");
  });
  it.each([403, 503])("keeps denied or unavailable collection state %s distinct from empty without displaying raw failures", async status => {
    const f = fixture(); f.get.mockRejectedValue(new StudioHttpError(status, "RAW_FAILURE_CANARY"));
    await view.render(<JobsPage api={f.api} navigate={vi.fn()} />);
    expect(view.container.textContent).toContain(status === 403 ? "Jobs access denied" : "Jobs state unavailable"); expect(view.container.textContent).not.toContain("RAW_FAILURE_CANARY"); expect(view.container.textContent).not.toContain("No matching items");
  });
});

describe("job evidence and hold controls", () => {
  it("refreshes hold transitions and late physical evidence even when the terminal job revision is unchanged", async () => {
    const f = detailFixture(); const gate = { kind: "queue" as const, name: "reports" }; let released = false; let late = false;
    const hold = () => ({ gate, holdId: "same-revision-hold", revision: released ? 2 : 1, actor: "alice", reason: "Current gate state", requestedAt: job.acceptedAt,
      ...(released ? { releasedAt: job.completedAt, releasedBy: "alice" } : {}) });
    f.get.mockImplementation(async path => path === "/jobs/report-one" ? job : path.startsWith("/jobs/report-one/holds") ? { gates: [{ gate, items: [hold()] }], complete: true } :
      path.startsWith("/jobs/holds?") ? { items: [hold()] } : path.includes("/physical-receipts") ? { items: late ? [{ receiptId: "LATE_PHYSICAL_RECEIPT", attemptId: "old-attempt", owner: "worker", fence: 1, outcome: "failed", retryable: false, reportedAt: job.completedAt }] : [] } : { items: [] });
    f.post.mockImplementation(async () => { released = true; return { status: "applied", revision: 2 }; });
    await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />); await click(view.container, "Release this hold…");
    expect(view.container.textContent).toContain("Released by alice");
    expect([...view.container.querySelectorAll("button")].some(button => button.textContent === "Release this hold…")).toBe(false);
    late = true; await click(view.container, "Refresh"); expect(view.container.textContent).toContain("LATE_PHYSICAL_RECEIPT");
    expect(f.get.mock.calls.filter(([path]) => path.includes("/physical-receipts")).length).toBeGreaterThan(1);
  });
  it.each(["input", "result"] as const)("renders and downloads exact retained %s JSON after fresh authorization and read", async kind => {
    const raw = '{"large":9007199254740993,"overflow":1e309,"precise":1.0000000000000001,"zero":-0}';
    const f = detailFixture();
    const payload = { schema: { alias: "numeric", version: 1 }, value: JSON.parse(raw) };
    f.get.mockImplementation(async path => path === `/jobs/report-one/${kind}` ?
      kind === "input" ? { state: "available", input: payload, valueJson: raw } : { state: "available", resultId: "result-one", payload, valueJson: raw } :
      path === "/jobs/report-one" ? job : path.startsWith("/jobs/report-one/holds") ? { gates: [], complete: true } : { items: [] });
    const previousCreate = Object.getOwnPropertyDescriptor(URL, "createObjectURL"); const previousRevoke = Object.getOwnPropertyDescriptor(URL, "revokeObjectURL");
    let exported: Blob | undefined;
    const create = vi.fn((blob: Blob) => { exported = blob; return "blob:owned"; }); const revoke = vi.fn();
    Object.defineProperty(URL, "createObjectURL", { configurable: true, value: create }); Object.defineProperty(URL, "revokeObjectURL", { configurable: true, value: revoke });
    const anchorClick = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
    try {
      await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />); await click(view.container, `Load permitted ${kind}`);
      expect(view.container.querySelector(`[aria-label="Permitted ${kind} JSON"]`)?.textContent).toContain('"value":' + raw);
      await click(view.container, `Export permitted ${kind} JSON`);
      const document = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsText(exported!); });
      expect(document).toContain('"value":' + raw); expect(document).not.toContain("9007199254740992");
      expect(f.get.mock.calls.filter(([path]) => path === `/jobs/report-one/${kind}`)).toHaveLength(2); expect(revoke).toHaveBeenCalledWith("blob:owned");
      f.identity.permissions = f.identity.permissions.filter(permission => permission !== (kind === "input" ? permissions.inputs : permissions.results));
      await click(view.container, `Export permitted ${kind} JSON`);
      expect(create).toHaveBeenCalledTimes(1); expect(view.container.textContent).not.toContain(raw);
    } finally {
      anchorClick.mockRestore();
      if (previousCreate) Object.defineProperty(URL, "createObjectURL", previousCreate); else Reflect.deleteProperty(URL, "createObjectURL");
      if (previousRevoke) Object.defineProperty(URL, "revokeObjectURL", previousRevoke); else Reflect.deleteProperty(URL, "revokeObjectURL");
    }
  });
  it("keeps Starting, Running, physical exit and saved-result delivery claims distinct", async () => {
    const permission = { attemptId: "attempt", number: 1, owner: "worker", implementationIdentity: "reports.v1", grantedAt: "2026-11-06T12:00:00Z", expiresAt: "2026-11-06T12:01:00Z", deadline: "2026-11-06T12:01:00Z" };
    const starting: Attempt = { permission, state: "permitted" }; const running: Attempt = { permission, state: "running", physicalStartedAt: "2026-11-06T12:00:01Z" };
    expect(attemptLabel(starting)).toBe("Starting"); expect(attemptLabel(running)).toBe("Running"); expect(consumptionText({ ...job, consumptionState: "settled" })).toContain("terminal-parent proof");
    const f = detailFixture(); f.identity.permissions = [permissions.read];
    f.get.mockImplementation(async path => path === "/jobs/report-one" ? job : path.includes("/attempts") ? { items: [starting, { ...running, permission: { ...permission, attemptId: "second", number: 2 } }] } : path.includes("/report-one/holds") ? { gates: [], complete: true } : { items: [] });
    await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />);
    expect(view.container.textContent).toContain("Starting"); expect(view.container.textContent).toContain("Running"); expect(view.container.textContent).toContain("Exit unobserved");
    expect(view.container.textContent).toContain("The result is saved"); expect(view.container.textContent).toContain("remains owed");
    expect(view.container.querySelector('a[href$="/workflows/bpmn/runs/bpmn-root"]')).not.toBeNull();
    expect(f.get.mock.calls.some(([path]) => path.endsWith("/input") || path.endsWith("/result"))).toBe(false);
  });
  it("offers all four exact pause scopes and never attaches a caller model association to the command", async () => {
    const f = detailFixture(); await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />);
    const buttons = [...view.container.querySelectorAll("button")].filter(button => button.textContent?.startsWith("Pause "));
    expect(buttons).toHaveLength(4); const definition = buttons.find(button => button.textContent?.includes("Workflow definition"))!;
    await act(() => definition.click());
    expect(f.post).toHaveBeenCalledWith("/jobs/holds", expect.objectContaining({ gate: { kind: "workflowDefinition", name: "order-fulfillment", modelKind: "elsa.bpmn" }, reason: "Operator reason" }));
    expect(f.post.mock.calls[0][1]).not.toHaveProperty("association"); expect(f.post.mock.calls[0][1]).not.toHaveProperty("actor"); expect(f.post.mock.calls[0][1]).not.toHaveProperty("scope");
    expect(view.container.textContent).toContain("The result is saved"); expect(f.get.mock.calls.some(([path]) => path.endsWith("/retry"))).toBe(false);
  });
  it("keeps unavailable execution metadata unknown instead of inventing an absent result or independent parent", async () => {
    const f = detailFixture(); f.identity.permissions = [permissions.read];
    f.get.mockImplementation(async path => path === "/jobs/report-one" ? { ...job, state: "attemptActive", outcome: null, resultId: null, association: null, consumptionState: "unknown", diagnostic: "jobs.record.unavailable" } : path.startsWith("/jobs/report-one/holds") ? { gates: [], complete: true } : { items: [] });
    await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />);
    expect(view.container.textContent).toContain("Execution metadata unavailable"); expect(view.container.textContent).toContain("Selected result metadata is unavailable"); expect(view.container.textContent).toContain("Continuation status is unknown");
    expect(view.container.textContent).not.toContain("No terminal result has been selected"); expect(view.container.textContent).not.toContain("No awaited parent continuation applies");
  });
  it("releases one hold at its exact revision and retains another overlapping reason", async () => {
    const f = detailFixture(); const gate = { kind: "queue" as const, name: "reports" };
    const holds = [{ gate, holdId: "first", revision: 4, actor: "alice", reason: "Deployment", requestedAt: job.acceptedAt }, { gate, holdId: "second", revision: 1, actor: "bob", reason: "Data maintenance", requestedAt: job.acceptedAt }];
    let released = false;
    f.get.mockImplementation(async path => path === "/jobs/report-one" ? job : path.startsWith("/jobs/report-one/holds") ? { gates: [{ gate, items: holds }], complete: true } : path.startsWith("/jobs/holds?") ? { items: holds.map((hold, index) => released && index === 0 ? { ...hold, releasedAt: "2026-11-06T12:05:00Z", releasedBy: "alice" } : hold) } : { items: [] });
    f.post.mockImplementation(async () => { released = true; return { status: "applied", revision: 5 }; });
    await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />); await click(view.container, "Release this hold…");
    expect(f.post).toHaveBeenCalledWith("/jobs/holds/first/release", expect.objectContaining({ gate, expectedRevision: 4 }));
    expect(view.container.textContent).toContain("Data maintenance"); expect(view.container.textContent).toContain("Active hold"); expect(view.container.textContent).not.toContain("eligible now");
  });
});

describe("schedule and submission editors", () => {
  it("keeps exact retained schedule numbers in the editor and refuses an unchanged unsafe payload before any save", async () => {
    const raw = '{"large":9007199254740993,"overflow":1e309,"precise":1.0000000000000001}';
    const f = fixture();
    f.get.mockImplementation(async path => path === "/jobs/schedules/hourly" ? schedule : { state: "available", input: { schema: schedule.inputSchema, value: JSON.parse(raw) }, valueJson: raw });
    await view.render(<ScheduleEditor api={f.api} id="hourly" navigate={vi.fn()} />);
    expect(control(view.container, "Input JSON").value).toBe(raw);
    await input(control(view.container, "Reason"), "Change schedule only"); await submit(view.container);
    expect(f.put).not.toHaveBeenCalled(); expect(view.container.textContent).toContain("without changing its value");
    expect(control(view.container, "Input JSON").value).toBe(raw);
    await input(control(view.container, "Input JSON"), '{"large":"9007199254740993","overflow":"1e309","precise":"1.0000000000000001"}'); await submit(view.container);
    expect(f.put).toHaveBeenCalledWith("/jobs/schedules/hourly", expect.objectContaining({ invocation: expect.objectContaining({ input: expect.objectContaining({ value: { large: "9007199254740993", overflow: "1e309", precise: "1.0000000000000001" } }) }) }));
  });
  it.each(["9007199254740993", "1e309"])("preserves and rejects entered standalone number %s before a transport write", async token => {
    const f = fixture(); await view.render(<SubmitPage api={f.api} navigate={vi.fn()} />);
    await input(control(view.container, "Registered job type"), "reports"); await input(control(view.container, "Input schema alias"), "input"); await input(control(view.container, "Result schema alias"), "result");
    const raw = `{"value":${token}}`; await input(control(view.container, "Input JSON"), raw); await submit(view.container);
    expect(f.post).not.toHaveBeenCalled(); expect(control(view.container, "Input JSON").value).toBe(raw); expect(view.container.textContent).toContain("without changing its value");
  });
  it("defaults to one catch-up run and preserves an edited null payload across conflict review before adopting a new revision", async () => {
    const f = fixture(); let current: Schedule = schedule;
    f.get.mockImplementation(async path => path === "/jobs/schedules/hourly" ? current : { state: "available", input: { schema: schedule.inputSchema, value: null }, valueJson: "null" });
    f.put.mockRejectedValueOnce(new StudioHttpError(409, "Revision changed")).mockResolvedValue({ status: "applied", revision: 5 }); const navigate = vi.fn();
    await view.render(<ScheduleEditor api={f.api} id="hourly" navigate={navigate} />);
    expect(control(view.container, "Missed occurrences").value).toBe("coalesceOne"); expect(control(view.container, "Input JSON").value).toBe("null");
    await input(control(view.container, "Queue"), "changed-queue"); await input(control(view.container, "Reason"), "Policy review"); await submit(view.container);
    expect(control(view.container, "Queue").value).toBe("changed-queue"); expect(control(view.container, "Input JSON").value).toBe("null");
    expect([...view.container.querySelectorAll("button")].find(button => button.textContent === "Review and save schedule")?.disabled).toBe(true);
    current = { ...schedule, revision: 4, recurrence: { kind: "interval", expression: "PT2H" } };
    await click(view.container, "Review current schedule"); await click(view.container, "Use reviewed revision 4"); await submit(view.container);
    expect(f.put.mock.calls[0][1]).toMatchObject({ expectedRevision: 3, missedPolicy: "coalesceOne" });
    expect(f.put.mock.calls[1][1]).toMatchObject({ expectedRevision: 4, invocation: { queue: "changed-queue", input: { value: null } } });
    expect(f.put.mock.calls[1][1]).not.toHaveProperty("actor"); expect(f.put.mock.calls[1][1]).not.toHaveProperty("scope"); expect(navigate).toHaveBeenCalledWith("/jobs/schedules");
  });
  it("reviews the backlog consequence before an explicitly selected replay-every-occurrence policy", async () => {
    const f = fixture(); await view.render(<ScheduleEditor api={f.api} navigate={vi.fn()} />);
    await input(control(view.container, "Schedule ID"), "new-hourly"); await input(control(view.container, "Registered job type"), "reports");
    await input(control(view.container, "Input schema alias"), "input"); await input(control(view.container, "Result schema alias"), "result");
    await input(control(view.container, "Missed occurrences"), "replayEveryOccurrence"); await input(control(view.container, "Reason"), "Replay required"); await submit(view.container);
    expect(f.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("large durable backlog") }));
    expect(f.put).toHaveBeenCalledWith("/jobs/schedules/new-hourly", expect.objectContaining({ missedPolicy: "replayEveryOccurrence", expectedRevision: 0 }));
  });
  it("clears an unsent protected draft after current access changes to another subject", async () => {
    const f = fixture(); await view.render(<SubmitPage api={f.api} navigate={vi.fn()} />);
    await input(control(view.container, "Registered job type"), "reports"); await input(control(view.container, "Input schema alias"), "input"); await input(control(view.container, "Result schema alias"), "result");
    await input(control(view.container, "Input JSON"), '{"protected":"OLD_ACTOR_INPUT_CANARY"}');
    f.identity.subjectId = "bob"; f.identity.permissions = [permissions.read]; await submit(view.container);
    expect(f.post).not.toHaveBeenCalled(); expect(view.container.textContent).not.toContain("OLD_ACTOR_INPUT_CANARY");
    f.identity.permissions = [permissions.submit]; await click(view.container, "Refresh access and state");
    expect(control(view.container, "Input JSON").value).toBe("{}"); expect(control(view.container, "Registered job type").value).toBe("");
  });
  it("keeps entered input when discard is cancelled and navigates only after confirmed discard", async () => {
    const f = fixture(); f.confirm.mockResolvedValueOnce(false).mockResolvedValueOnce(true); const navigate = vi.fn();
    await view.render(<SubmitPage api={f.api} navigate={navigate} />); await input(control(view.container, "Input JSON"), '{"report":42}');
    await click(view.container, "Cancel and return to Jobs"); expect(control(view.container, "Input JSON").value).toBe('{"report":42}'); expect(navigate).not.toHaveBeenCalled();
    await click(view.container, "Cancel and return to Jobs"); expect(navigate).toHaveBeenCalledWith("/jobs"); expect(f.post).not.toHaveBeenCalled();
  });
});


describe("operation audit inspection", () => {
  it("keeps legacy metadata explicit and clears retained actor/reason after current access is revoked", async () => {
    const f = fixture();
    f.get.mockResolvedValue({ items: [
      { operationId: "old", diagnostic: "jobs.audit.unavailable" },
      { operationId: "pause", kind: "pause", actor: "ORIGINAL_ACTOR_CANARY", reason: "ORIGINAL_REASON_CANARY",
        recordedAt: "2026-11-06T12:00:00Z", target: { gate: { kind: "queue", name: "reports" } }, confirmation: { status: "applied" } }
    ], next: null });
    await view.render(<OperationAuditPage api={f.api} navigate={vi.fn()} />); await act(() => {});
    expect(view.container.textContent).toContain("Original actor, reason and time unavailable");
    expect(view.container.textContent).toContain("ORIGINAL_ACTOR_CANARY");
    f.identity.permissions = f.identity.permissions.filter(value => value !== permissions.audit);
    await click(view.container, "Refresh"); await act(() => {});
    expect(view.container.textContent).toContain("Jobs access denied");
    expect(view.container.textContent).not.toContain("ORIGINAL_ACTOR_CANARY");
    expect(view.container.textContent).not.toContain("ORIGINAL_REASON_CANARY");
  });
});

describe("expired payload inspection", () => {
  it("keeps result identity visible, explains expired bytes and disables retry after input expiry", async () => {
    const f = detailFixture(); const original = f.get.getMockImplementation()!;
    const expired = { state: "expired", schema: { alias: "report", version: 1 }, payloadSha256: "a".repeat(64), expiredAt: "2026-11-06T12:02:00Z" };
    f.get.mockImplementation(async path => path === "/jobs/report-one" ? { ...job, consumptionState: "settled", inputPayloadState: "expired", resultPayloadState: "expired" }
      : path.endsWith("/input") ? expired : path.endsWith("/result") ? { ...expired, resultId: job.resultId, outcome: job.outcome, completedAt: job.completedAt }
      : original(path));
    await view.render(<JobDetail api={f.api} id="report-one" navigate={vi.fn()} />); await act(() => {});
    const retry = Array.from(view.container.querySelectorAll("button")).find(button => button.textContent === "Retry as new run…")!;
    expect(retry.disabled).toBe(true); expect(view.container.textContent).toContain("The accepted input payload has expired");
    expect(view.container.textContent).toContain("result-one");
    await click(view.container, "Load permitted input"); await click(view.container, "Load permitted result");
    expect(view.container.textContent).toContain("The input payload expired on"); expect(view.container.textContent).toContain("The result payload expired on");
    expect(view.container.textContent).toContain("Export retained input metadata"); expect(view.container.textContent).toContain("Export retained result metadata");
    for (const region of view.container.querySelectorAll("pre.jobs-json")) expect(region.textContent).not.toContain('"value"');
    expect(f.post).not.toHaveBeenCalled();
  });
});
