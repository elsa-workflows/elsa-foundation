import { describe, expect, it } from "vitest";
import type { ActivityTypeKeyRules } from "../activityDefinitionTypes";
import { describeActivityTypeKeyRules, validateActivityTypeKeyOverride } from "../activityTypeKeyRules";

const defaultPattern = "^elsa\\.user\\.[a-z0-9]+(?:-[a-z0-9]+)*\\.[a-z0-9]+(?:-[a-z0-9]+)*$";

describe("Activity Type Key rules", () => {
  it("describes the default namespace and format", () => {
    expect(describeActivityTypeKeyRules(keyRules({ pattern: defaultPattern }))).toEqual([
      "Starts with elsa.user.",
      "Use two segments after the prefix; each segment uses lowercase letters and digits, with words separated by single hyphens.",
      "At most 160 characters.",
      "Must be unique within tenantId + activityTypeKey; Studio never adds a suffix.",
      "Example: elsa.user.team-name.activity-name"
    ]);
  });

  it("accepts a valid key after applying the server normalization", () => {
    expect(validateActivityTypeKeyOverride(" ELSA.USER.Team-Name.Activity-Name ", keyRules({ pattern: defaultPattern }))).toBeNull();
  });

  it("leaves an empty override for the server to generate", () => {
    expect(validateActivityTypeKeyOverride("  ", keyRules())).toBeNull();
  });

  it("reports a prefix mismatch", () => {
    expect(validateActivityTypeKeyOverride("elsa.custom.greeter", keyRules())).toBe("Must start with elsa.user.");
  });

  it("checks maximum length before the prefix and pattern", () => {
    expect(validateActivityTypeKeyOverride("x".repeat(161), keyRules())).toBe("Use at most 160 characters.");
  });

  it("rejects invalid segment characters after normalization", () => {
    const rules = keyRules({ pattern: defaultPattern });

    expect(validateActivityTypeKeyOverride("ELSA.USER.Team_Name.Greeter", rules)).toBe("Match the displayed key format.");
    expect(validateActivityTypeKeyOverride("ELSA.USER.Team-Name.Greeter", rules)).toBeNull();
  });

  it("describes custom patterns and fails open when a pattern cannot be parsed", () => {
    const customRules = keyRules({ pattern: "^elsa\\.user\\..+$" });
    expect(describeActivityTypeKeyRules(customRules)).toContain(`Matches pattern ${customRules.pattern}`);
    expect(validateActivityTypeKeyOverride("elsa.user.greeter", keyRules({ pattern: "[" }))).toBeNull();
  });
});

function keyRules(overrides: Partial<ActivityTypeKeyRules> = {}): ActivityTypeKeyRules {
  return {
    serverGenerated: true,
    allowsPreCreationOverride: true,
    immutable: true,
    prefix: "elsa.user",
    pattern: "^elsa\\.user\\..+$",
    maximumLength: 160,
    collisionScope: "tenantId + activityTypeKey",
    ...overrides
  };
}