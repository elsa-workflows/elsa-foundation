import { describe, expect, it } from "vitest";
import { createActivityExpressionDocument } from "../activityExpressionDocument";

describe("createActivityExpressionDocument", () => {
  it("keys identity by draft, activity, property, and Expression Type", () => {
    const first = createActivityExpressionDocument({
      draftId: "draft-1",
      activityId: "activity-a",
      propertyKey: "text",
      expressionType: "JavaScript",
      source: "return 1;",
      sourceVersion: 3
    });
    const otherActivity = createActivityExpressionDocument({
      draftId: "draft-1",
      activityId: "activity-b",
      propertyKey: "text",
      expressionType: "JavaScript",
      source: "return 1;",
      sourceVersion: 3
    });
    const otherType = createActivityExpressionDocument({
      draftId: "draft-1",
      activityId: "activity-a",
      propertyKey: "text",
      expressionType: "Liquid",
      source: "return 1;",
      sourceVersion: 3
    });

    expect(first.id).not.toBe(otherActivity.id);
    expect(first.id).not.toBe(otherType.id);
    expect(first.uri).toBe(first.id);
  });

  it("keeps source out of identity while retaining exact authored text", () => {
    const source = "secretCustomerToken\nline two";
    const document = createActivityExpressionDocument({
      draftId: "draft/1",
      activityId: "activity a",
      propertyKey: "customer.value",
      expressionType: "JavaScript",
      source,
      sourceVersion: 7
    });

    expect(document.source).toBe(source);
    expect(document.id).not.toContain("secretCustomerToken");
    expect(document).toMatchObject({
      draftId: "draft/1",
      activityId: "activity a",
      propertyKey: "customer.value",
      expressionType: "JavaScript",
      sourceVersion: 7
    });
  });
});
