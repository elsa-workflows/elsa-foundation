import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, clearApiCapabilityCache, type ApiCapabilityLink } from "../api/capabilities";
import {
  activationSlotReadsUnavailableReason,
  getPublicationSlot,
  getPublicationSlotLifecycleSupport,
  listPublicationSlots,
  publicationRecordReadsUnavailableReason,
  restorePublicationSlot,
  unpublishSlot
} from "../api/publishing";
import { activationSlot, importedActivationSlot, publicationRecord } from "./fixtures/publicationSlots";

afterEach(clearApiCapabilityCache);

const runtimeSlotLinks: ApiCapabilityLink[] = [
  { rel: "workflow-activation-slots", href: "runtime/workflows/activation-slots/{definitionId}", templated: true },
  { rel: "workflow-activation-slot", href: "runtime/workflows/activation-slots/{definitionId}/{slotName}", templated: true }
];
const publicationRecordLink: ApiCapabilityLink = { rel: "publication-record", href: "publishing/publications/{publicationId}", templated: true };
const lifecycleLinks: ApiCapabilityLink[] = [
  { rel: "publication-slot-unpublish", href: "publishing/workflows/{definitionId}/slots/{slotName}", templated: true },
  { rel: "publication-slot-restore", href: "publishing/workflows/{definitionId}/slots/{slotName}/restore", templated: true }
];
const publishedDefault = activationSlot("default", { activeActivationId: "publication-default", sourceKind: "publishing" });
const slotsPath = "/runtime/workflows/activation-slots/definition-1";

function capabilities(links: { runtime?: ApiCapabilityLink[]; publishing?: ApiCapabilityLink[] } = {}) {
  return {
    capabilities: [
      { id: "elsa.api.publishing", contractVersion: "1", links: links.publishing ?? [publicationRecordLink] },
      { id: "elsa.api.runtime", contractVersion: "1", links: links.runtime ?? runtimeSlotLinks }
    ]
  };
}

/** A client whose GETs are served from `routes` (an Error value is thrown); any other GET is an unexpected probe. */
function client(document: ReturnType<typeof capabilities>, routes: Record<string, unknown> = {}) {
  const getJson = vi.fn(async (url: string) => {
    if (url === "/capabilities") return document;
    if (!(url in routes)) throw new Error(`Unexpected GET ${url}`);
    if (routes[url] instanceof Error) throw routes[url];
    return routes[url];
  });
  const deleteJson = vi.fn(async () => ({}));
  const postJson = vi.fn(async () => ({}));
  const context = { baseUrl: `test://publication-slot-client-${Math.random()}`, http: { getJson, deleteJson, postJson } } as unknown as StudioEndpointContext;
  return { context, getJson, deleteJson, postJson, requested: () => getJson.mock.calls.map(([url]) => url) };
}

describe("publication slot client", () => {
  it("joins Runtime activation slots to the publication records behind publishing-sourced activations", async () => {
    const { context, requested } = client(capabilities(), {
      "/runtime/workflows/activation-slots/definition%2F1": { items: [publishedDefault, importedActivationSlot(), activationSlot("canary")] },
      "/publishing/publications/publication-default": publicationRecord("default")
    });

    await expect(listPublicationSlots(context, "definition/1")).resolves.toEqual({
      available: true,
      slots: [
        { ...publishedDefault, publication: publicationRecord("default") },
        { ...importedActivationSlot(), publication: null },
        { ...activationSlot("canary"), publication: null }
      ]
    });
    expect(requested()).toEqual([
      "/capabilities",
      "/runtime/workflows/activation-slots/definition%2F1",
      "/publishing/publications/publication-default"
    ]);
  });

  it("reports slot reads as unavailable, without probing any slot route, when Runtime does not advertise them", async () => {
    const { context, requested } = client(capabilities({ runtime: [] }));

    await expect(listPublicationSlots(context, "definition-1")).resolves.toEqual({
      available: false,
      reason: activationSlotReadsUnavailableReason
    });
    expect(requested()).toEqual(["/capabilities"]);
  });

  it("reports the slots as unavailable when a publishing-sourced activation cannot be joined to its record", async () => {
    const { context, requested } = client(capabilities({ publishing: [] }), { [slotsPath]: { items: [publishedDefault] } });

    await expect(listPublicationSlots(context, "definition-1")).resolves.toEqual({
      available: false,
      reason: publicationRecordReadsUnavailableReason
    });
    expect(requested()).toEqual(["/capabilities", slotsPath]);
  });

  it("needs no publication record relation when no slot is occupied by a publication", async () => {
    const { context } = client(capabilities({ publishing: [] }), {
      [slotsPath]: { items: [importedActivationSlot(), activationSlot("canary")] }
    });

    await expect(listPublicationSlots(context, "definition-1")).resolves.toMatchObject({
      available: true,
      slots: [{ slotName: "imported", publication: null }, { slotName: "canary", publication: null }]
    });
  });

  it("rejects rather than reading an advertised but missing publication record as an empty slot", async () => {
    const { context } = client(capabilities(), {
      [slotsPath]: { items: [publishedDefault] },
      "/publishing/publications/publication-default": Object.assign(new Error("Publication 'publication-default' was not found."), { status: 404 })
    });

    const failure = await listPublicationSlots(context, "definition-1").catch((error: Error) => error);

    expect(failure).toBeInstanceOf(Error);
    expect((failure as Error).message).toBe(
      "Publication slot 'default' is occupied by publication 'publication-default', but its publication record could not be read: Publication 'publication-default' was not found.");
    expect((failure as Error).cause).toMatchObject({ status: 404 });
  });

  it("reads one slot through the advertised single-slot relation and joins its record", async () => {
    const canary = activationSlot("canary", { activeActivationId: "publication-canary", sourceKind: "publishing" });
    const { context } = client(capabilities(), {
      "/runtime/workflows/activation-slots/definition-1/canary": canary,
      "/publishing/publications/publication-canary": publicationRecord("canary")
    });

    await expect(getPublicationSlot(context, "definition-1", "canary")).resolves.toEqual({
      ...canary,
      publication: publicationRecord("canary")
    });
  });

  it("refuses slot lifecycle commands the backend does not advertise instead of guessing their routes", async () => {
    const { context, deleteJson, postJson } = client(capabilities());

    await expect(getPublicationSlotLifecycleSupport(context)).resolves.toEqual({ unpublish: false, restore: false });
    await expect(unpublishSlot(context, "definition-1", "default")).rejects.toBeInstanceOf(ApiCapabilityUnavailableError);
    await expect(restorePublicationSlot(context, "definition-1", "default")).rejects.toBeInstanceOf(ApiCapabilityUnavailableError);
    expect(deleteJson).not.toHaveBeenCalled();
    expect(postJson).not.toHaveBeenCalled();
  });

  it("sends slot lifecycle commands to their advertised relations", async () => {
    const { context, deleteJson, postJson } = client(capabilities({ publishing: [publicationRecordLink, ...lifecycleLinks] }));

    await expect(getPublicationSlotLifecycleSupport(context)).resolves.toEqual({ unpublish: true, restore: true });
    await unpublishSlot(context, "definition-1", "blue green");
    await restorePublicationSlot(context, "definition-1", "blue green");

    expect(deleteJson).toHaveBeenCalledWith("/publishing/workflows/definition-1/slots/blue%20green");
    expect(postJson).toHaveBeenCalledWith("/publishing/workflows/definition-1/slots/blue%20green/restore", {});
  });
});
