import { describe, expect, it } from "vitest";
import { StudioHttpError } from "@elsa-workflows/studio-sdk";
import { graphPublicationValidationFailure } from "../activityPublicationValidationFailure";

const diagnostic = {
  code: "ActivityDataSelection/activity.graph.variable.initial-value-invalid", severity: "Error", message: "PRIVATE",
  subject: { kind: "ActivityDraft", id: "draft-1" }, location: { providerKey: "elsa.activity-graph", referenceKey: "PRIVATE" },
  metadata: { authoredPath: "/variables/2/initialValue", secret: "PRIVATE" }
};
const refusal = (item: unknown = diagnostic, status = 400, code = "activity-data.invalid") => new StudioHttpError(status, "PRIVATE", null, { errorCode: code, diagnostics: [item] });

describe("public graph publication validation refusal", () => {
  it("replaces every untrusted field and binds the numeric location to the reviewed draft", () => {
    const result = graphPublicationValidationFailure(refusal(), "draft-1", 3)!;
    expect(result[0]?.location).toEqual({ providerKey: "elsa.activity-graph", jsonPointer: "/variables/2/initialValue" });
    expect(result[0]?.subject).toEqual({ kind: "ActivityDraft", id: "draft-1", revision: 3 });
    expect(JSON.stringify(result)).not.toContain("PRIVATE");
  });
  it.each(["/variables/02/initialValue", "/variables/10000/initialValue", "/variables/PRIVATE/initialValue", "/outputMappings/2/source", "/variables/2/initialValue/secret"])("fails closed for mismatched or unsafe location %s", authoredPath => {
    expect(graphPublicationValidationFailure(refusal({ ...diagnostic, metadata: { authoredPath } }), "draft-1", 3)).toBeNull();
  });
  it("keeps unknown codes, hidden identities, other providers, stale revisions and server failures as service failures", () => {
    for (const item of [
      { ...diagnostic, code: "PRIVATE" },
      { ...diagnostic, subject: { kind: "ActivityDraft", id: "hidden" } },
      { ...diagnostic, subject: { kind: "ActivityDraft", id: "draft-1", revision: 2 } },
      { ...diagnostic, location: { providerKey: "PRIVATE" } }
    ]) expect(graphPublicationValidationFailure(refusal(item), "draft-1", 3)).toBeNull();
    expect(graphPublicationValidationFailure(new StudioHttpError(400, "PRIVATE", null, {
      errorCode: "activity-data.invalid", diagnostics: [diagnostic, { ...diagnostic, code: "PRIVATE" }]
    }), "draft-1", 3)).toBeNull();
    expect(graphPublicationValidationFailure(refusal({ ...diagnostic, authoredPath: "/variables/1/initialValue" }), "draft-1", 3)).toBeNull();
    expect(graphPublicationValidationFailure(refusal(diagnostic, 500), "draft-1", 3)).toBeNull();
    expect(graphPublicationValidationFailure(refusal(diagnostic, 400, "PRIVATE"), "draft-1", 3)).toBeNull();
  });
});
