import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { homeHasRun, homeHasWorkflow, loadNeedsAttention, loadRecentRuns, loadRecentWorkflows } from "../homeData";
import { stubApi } from "../../__tests__/testSupport";

const definitionsPath = "/design/workflows/definitions?take=8";
const recentRunsPath = "/runtime/workflows/instances/page?take=8";
const attentionRunsPath = "/runtime/workflows/instances/page?incidentHealth=active&take=8";

afterEach(() => vi.restoreAllMocks());

function mockGetJson(api: ElsaStudioModuleApi, respond: (url: string) => unknown) {
  return vi.spyOn(api.backend.http, "getJson").mockImplementation(async <T,>(url: string) => respond(url) as T);
}

describe("home dashboard data", () => {
  it("sorts definition list items by modification time with a creation-time fallback", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const getJson = mockGetJson(api, () => ({
      items: [
        {
          id: "older",
          name: "Older",
          createdAt: "2026-10-16T10:00:00Z",
          lastModifiedAt: "2026-10-16T10:00:00Z"
        },
        {
          id: "created-fallback",
          name: "Created fallback",
          createdAt: "2026-10-17T12:00:00Z",
          lastModifiedAt: "invalid"
        },
        { id: "untimed", name: "Untimed", createdAt: "invalid", lastModifiedAt: "invalid" },
        {
          id: "definition-1",
          name: "Orders",
        draftId: "draft-orders",
          description: "Processes new orders.",
          createdAt: "2026-10-17T10:00:00Z",
          lastModifiedAt: "2026-10-18T11:30:00Z",
          latestVersion: "3",
          publicationState: "published"
        },
        { id: "", name: "Missing identifier" }
      ]
    }));

    const workflows = await loadRecentWorkflows(api, signal);
    expect(workflows.map(item => item.id)).toEqual(["definition-1", "created-fallback", "older", "untimed"]);
    expect(workflows[0]).toMatchObject({
      name: "Orders",
      description: "Processes new orders.",
      createdAt: "2026-10-17T10:00:00Z",
      lastModifiedAt: "2026-10-18T11:30:00Z",
      latestVersion: "3",
      publicationState: "published",
      draftId: "draft-orders"
    });
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith(definitionsPath, { signal });
  });

  it("retains draft identity for every returned definition, including candidates beyond the recent preview", async () => {
    const api = stubApi();
    const definitions = Array.from({ length: 9 }, (_, index) => ({
      id: `definition-${index + 1}`,
      name: `Workflow ${index + 1}`,
      draftId: index === 8 ? "draft-ninth" : index === 7 ? "  " : `draft-${index + 1}`,
      publicationState: "published"
    }));
    mockGetJson(api, () => ({ items: definitions }));

    const workflows = await loadRecentWorkflows(api, new AbortController().signal);

    expect(workflows).toHaveLength(9);
    expect(workflows.map(item => item.draftId)).toEqual([
      "draft-1", "draft-2", "draft-3", "draft-4", "draft-5", "draft-6", "draft-7", undefined, "draft-ninth"
    ]);
  });

  it("uses one bulk definition lookup to label runs and preserves explicit run names", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const getJson = mockGetJson(api, url => url === recentRunsPath
      ? {
        items: [
          {
            workflowExecutionId: "run-1",
            definitionId: "definition-1",
            artifactVersion: "3",
            status: "Running",
            createdAt: "2026-10-18T11:00:00Z",
            startedAt: "2026-10-18T11:00:02Z",
            updatedAt: "2026-10-18T11:02:00Z",
            activeIncidentCount: 2,
            blockingIncidentCount: 1
          },
          {
            workflowExecutionId: "run-2",
            definitionId: "definition-1",
            name: "Explicit run name",
            status: "Completed",
            artifactVersion: "2"
          },
          {
            workflowExecutionId: "run-3",
            definitionId: "definition-1",
            definitionName: "Explicit definition label",
            status: "Suspended"
          },
          { workflowExecutionId: "run-missing", definitionId: "missing-definition", status: "Running" },
          { workflowExecutionId: "run-deleted", definitionId: "deleted-definition", status: "Faulted" }
        ],
        nextCursor: null,
        hasNext: false,
        count: 5,
        totalCount: 5
      }
      : {
        items: [{ id: "definition-1", name: "Orders", createdAt: "2026-10-18T10:00:00Z", lastModifiedAt: "2026-10-18T11:00:00Z" }]
      });

    const runs = await loadRecentRuns(api, signal);
    expect(runs).toMatchObject([
      {
        id: "run-1",
        name: "Orders",
        definitionId: "definition-1",
        definitionVersion: "3",
        status: "Running",
        createdAt: "2026-10-18T11:00:00Z",
        startedAt: "2026-10-18T11:00:02Z",
        updatedAt: "2026-10-18T11:02:00Z",
        activeIncidentCount: 2,
        blockingIncidentCount: 1
      },
      { id: "run-2", name: "Explicit run name", definitionVersion: "2", status: "Completed" },
      { id: "run-3", name: "Explicit definition label", status: "Suspended" },
      { id: "run-missing", name: "run-missing" },
      { id: "run-deleted", name: "run-deleted" }
    ]);
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(getJson).toHaveBeenCalledWith(recentRunsPath, { signal });
    expect(getJson).toHaveBeenCalledWith(definitionsPath, { signal });
  });

  it("keeps runtime rows with ID labels when the definition list is forbidden", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const forbidden = Object.assign(new Error("Forbidden"), { status: 403 });
    const getJson = mockGetJson(api, url => {
      if (url === recentRunsPath) return { items: [{ workflowExecutionId: "run-1", definitionId: "definition-1", status: "Running" }] };
      throw forbidden;
    });

    await expect(loadRecentRuns(api, signal)).resolves.toMatchObject([{ id: "run-1", name: "run-1", status: "Running" }]);
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(getJson).toHaveBeenCalledWith(definitionsPath, { signal });
  });

  it("uses the same bulk definition lookup for attention rows", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const getJson = mockGetJson(api, url => url === attentionRunsPath
      ? { items: [{ workflowExecutionId: "run-1", definitionId: "definition-1", status: "Running", activeIncidentCount: 1 }] }
      : { items: [{ id: "definition-1", name: "Orders" }] });

    await expect(loadNeedsAttention(api, signal)).resolves.toMatchObject([
      { id: "run-1", name: "Orders", status: "Running", activeIncidentCount: 1 }
    ]);
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(getJson).toHaveBeenCalledWith(attentionRunsPath, { signal });
    expect(getJson).toHaveBeenCalledWith(definitionsPath, { signal });
  });

  it("uses the advertised active-health page and lets runtime failures reach the widget", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const denied = new Error("Forbidden");
    const getJson = mockGetJson(api, () => { throw denied; });

    await expect(loadNeedsAttention(api, signal)).rejects.toBe(denied);
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith(attentionRunsPath, { signal });
  });

  it("reports an all-clear only after a successful empty attention page", async () => {
    const api = stubApi();
    const signal = new AbortController().signal;
    const getJson = mockGetJson(api, () => ({ items: [], nextCursor: null, hasNext: false, count: 0, totalCount: 0 }));

    await expect(loadNeedsAttention(api, signal)).resolves.toEqual([]);
    expect(getJson).toHaveBeenCalledTimes(1);
    expect(getJson).toHaveBeenCalledWith(attentionRunsPath, { signal });
  });

  it("preserves aborts from both the runtime page and the definition lookup", async () => {
    const api = stubApi();
    const controller = new AbortController();
    const aborted = new DOMException("The operation was aborted.", "AbortError");
    const getJson = mockGetJson(api, url => {
      if (url === recentRunsPath) {
        controller.abort(aborted);
        return { items: [{ workflowExecutionId: "run-1", definitionId: "definition-1" }] };
      }
      throw aborted;
    });

    await expect(loadRecentRuns(api, controller.signal)).rejects.toBe(aborted);
    expect(getJson).toHaveBeenCalledTimes(2);
    expect(getJson).toHaveBeenLastCalledWith(definitionsPath, { signal: controller.signal });
  });

  it("keeps onboarding existence checks tolerant of unavailable list endpoints", async () => {
    const api = stubApi();
    mockGetJson(api, () => { throw new Error("Unavailable"); });
    const signal = new AbortController().signal;

    await expect(homeHasWorkflow(api, signal)).resolves.toBe(false);
    await expect(homeHasRun(api, signal)).resolves.toBe(false);
  });
});
