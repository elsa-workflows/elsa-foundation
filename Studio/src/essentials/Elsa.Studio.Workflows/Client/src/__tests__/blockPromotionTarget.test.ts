import { expect, it } from "vitest";
import type { ActivityDefinitionVersionView } from "../activityDefinitionTypes";
import { planBlockPromotion } from "../graph-authoring/blockPromotion";
import { promotionMaterialJson, reviewPromotionTarget } from "../graph-authoring/blockPromotionTarget";
import type { ReadyBlockPromotion } from "../graph-authoring/blockPromotionTypes";
import { context } from "./blockPromotionFixtures";

const plan = (): ReadyBlockPromotion => { const result = planBlockPromotion(context(), "block"); if (result.status !== "ready") throw new Error(JSON.stringify(result)); return result; };
// The server answers with sorted keys and null members for omitted optional record members.
const reorder = (value: unknown): unknown => Array.isArray(value) ? value.map(reorder) : value && typeof value === "object"
  ? Object.fromEntries(Object.entries(value).reverse().map(([key, member]) => [key, reorder(member)]).concat([["description", null]])) : value;
const published = (ready: ReadyBlockPromotion, change: Partial<ActivityDefinitionVersionView> = {}): ActivityDefinitionVersionView => ({
  definition: { definitionId: "definition", activityTypeKey: "Reusable.Prepare", category: "Reusable activities", displayName: "Prepare shipment", contentAuthority: "Design" as never },
  versionId: "published-version", version: "1.0.0", lifecycle: "Active", publishedAt: "2026-11-16T00:00:00Z",
  contract: reorder(ready.candidate.contract) as never,
  provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "sha256:x", payload: reorder(ready.candidate.provider.payload) },
  ...change
});

it("accepts the exact reviewed material whatever its key order and omitted-member form", () => {
  const ready = plan();
  expect(reviewPromotionTarget(ready, "published-version", published(ready))).toEqual({ status: "matches" });
  expect(promotionMaterialJson({ b: 1, a: { d: null, c: [2] } })).toBe(promotionMaterialJson({ a: { c: [2] }, b: 1 }));
});

it.each([
  ["retired", { lifecycle: "Retired" }, "retired"],
  ["other version", { versionId: "other-version" }, "different version"],
  ["other provider", { provider: { providerKey: "elsa.json-activity", schemaVersion: "2", manifestFingerprint: "x", payload: {} } }, "different implementation provider"],
  ["unreadable implementation", { provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "x" } }, "cannot read"]
])("refuses a published version with %s", (_, change, reason) => {
  const ready = plan();
  const review = reviewPromotionTarget(ready, "published-version", published(ready, change as Partial<ActivityDefinitionVersionView>));
  expect(review.status).toBe("refused");
  expect(review.status === "refused" && review.reasons.join(" ")).toContain(reason);
});

it("refuses a version whose implementation or public contract differs from the block", () => {
  const ready = plan(), payload = structuredClone(ready.candidate.provider.payload) as { rootActivity: { nodeId: string } };
  payload.rootActivity.nodeId = "edited";
  const implementation = reviewPromotionTarget(ready, "published-version", published(ready, { provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", manifestFingerprint: "x", payload } }));
  expect(implementation.status === "refused" && implementation.reasons).toEqual(["This version's implementation differs from the block as it is now."]);
  const contract = structuredClone(ready.candidate.contract);
  contract.inputs[0].name = "Renamed";
  const signature = reviewPromotionTarget(ready, "published-version", published(ready, { contract }));
  expect(signature.status === "refused" && signature.reasons).toEqual(["This version's public inputs, outputs or outcomes differ from the reviewed proposal."]);
});
