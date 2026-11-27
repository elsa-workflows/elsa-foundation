import { describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  analyzeElsa3ReusableCollection,
  applyElsa3ReusableImport,
  expandElsa3ReusableSelection,
  getElsa3ReusableImportReceipt,
  uploadElsa3ReusableCollection
} from "../api/elsa3ReusableImport";

describe("Elsa 3 reusable import API contract", () => {
  it("sends JSON and ZIP files together with their original names", async () => {
    const context = endpoint();
    const signal = new AbortController().signal;
    const files = [new File(["[]"], "definitions.json"), new File(["zip"], "workflow-definitions.zip")];

    await uploadElsa3ReusableCollection(context, files, signal);

    expect(context.http.postForm).toHaveBeenCalledWith(
      "migration/elsa3/reusable-activities/collections", expect.any(FormData), { signal }
    );
    const body = vi.mocked(context.http.postForm).mock.calls[0][1];
    expect(body.getAll("files").map(file => (file as File).name)).toEqual(files.map(file => file.name));
  });

  it("uses escaped handles, bounded analysis pages, exact closure, apply, and receipt identities", async () => {
    const context = endpoint();
    const signal = new AbortController().signal;

    await analyzeElsa3ReusableCollection(context, "collection/one", 100, 100, signal);
    await expandElsa3ReusableSelection(context, "collection/one", "plan", ["source-v2"], signal);
    await applyElsa3ReusableImport(context, "collection/one", "plan", ["source-v1", "source-v2"], "operation/key", signal);
    await getElsa3ReusableImportReceipt(context, "operation/key", signal);

    expect(context.http.getJson).toHaveBeenNthCalledWith(
      1,
      "migration/elsa3/reusable-activities/collections/collection%2Fone/analysis?offset=100&limit=100",
      { signal }
    );
    expect(context.http.postJson).toHaveBeenNthCalledWith(
      1,
      "migration/elsa3/reusable-activities/collections/collection%2Fone/selection",
      { planId: "plan", selectedSourceVersionIds: ["source-v2"] },
      { signal }
    );
    expect(context.http.postJson).toHaveBeenNthCalledWith(
      2,
      "migration/elsa3/reusable-activities/collections/collection%2Fone/apply",
      {
        planId: "plan",
        selectedSourceVersionIds: ["source-v1", "source-v2"],
        idempotencyKey: "operation/key"
      },
      { signal }
    );
    expect(context.http.getJson).toHaveBeenNthCalledWith(
      2,
      "migration/elsa3/reusable-activities/imports/operation%2Fkey",
      { signal }
    );
  });
});

function endpoint(): StudioEndpointContext {
  return {
    baseUrl: "",
    http: {
      requestJson: vi.fn(async <T,>() => ({} as T)),
      getJson: vi.fn(async <T,>() => ({} as T)),
      postJson: vi.fn(async <T,>() => ({} as T)),
      putJson: vi.fn(async <T,>() => ({} as T)),
      deleteJson: vi.fn(async <T,>() => ({} as T)),
      postForm: vi.fn(async <T,>() => ({} as T))
    } as unknown as StudioEndpointContext["http"]
  };
}
