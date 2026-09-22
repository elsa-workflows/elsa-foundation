import { describe, expect, it } from "vitest";
import {
  computeEffectiveAvailability,
  createAvailabilityDraft,
  findActivityAvailabilityDiagnostic,
  getAvailabilityActivityEntries,
  getAvailabilityLayerLabel,
  getAvailabilityModePayload,
  getAvailabilityStateLabel,
  getAvailabilityStateName,
  groupAvailabilityEntriesByCategory,
  isAvailabilityDraftDirty,
  isRuleTargetEnabled,
  normalizeActivityAvailabilityDiagnostics,
  normalizeActivityAvailabilitySettings,
  normalizeAvailabilityMode,
  setRuleTargetEnabled,
  summarizeAvailabilityArguments,
  toggleListValue
} from "../activityAvailability";
import type { ActivityAvailabilityDiagnostics } from "../workflowTypes";

const diagnostics: ActivityAvailabilityDiagnostics = {
  items: [
    { activityDefinitionId: "def-1", activityTypeKey: "Elsa.WriteLine", displayName: "Write Line", category: "Primitives", state: "Available", layer: "Catalog", referenceKind: "ActivityType", referenceName: null, reason: "ok" },
    { activityDefinitionId: "def-2", activityTypeKey: "Elsa.HttpRequest", displayName: "HTTP Request", category: "Http", state: "HiddenByManagementSettings", layer: "ManagementSettings", referenceKind: "ActivityType", referenceName: null, reason: "hidden" },
    { activityDefinitionId: null, activityTypeKey: null, displayName: null, category: null, state: "UnresolvedReference", layer: "ManagementSettings", referenceKind: "ActivitySet", referenceName: "CustomSet", reason: "unresolved" }
  ],
  sets: [{ name: "Core", activityTypeKeys: ["Elsa.WriteLine"] }]
};

describe("getAvailabilityStateName", () => {
  it("maps numeric and string states to canonical names", () => {
    expect(getAvailabilityStateName(0)).toBe("Available");
    expect(getAvailabilityStateName(2)).toBe("HiddenByManagementSettings");
    expect(getAvailabilityStateName("RemovedFromCatalog")).toBe("RemovedFromCatalog");
    expect(getAvailabilityStateName("blockedByHostBaseline")).toBe("BlockedByHostBaseline");
    expect(getAvailabilityStateName(undefined)).toBe("Available");
  });
});

describe("getAvailabilityStateLabel", () => {
  it("renders human labels", () => {
    expect(getAvailabilityStateLabel(1)).toBe("Host blocked");
    expect(getAvailabilityStateLabel("HiddenByManagementSettings")).toBe("Management hidden");
  });
});

describe("findActivityAvailabilityDiagnostic", () => {
  it("matches an unavailable activity by type key", () => {
    const result = findActivityAvailabilityDiagnostic(["Elsa.HttpRequest"], diagnostics);
    expect(result?.activityDefinitionId).toBe("def-2");
  });

  it("matches by definition id and reference name", () => {
    expect(findActivityAvailabilityDiagnostic(["def-2"], diagnostics)?.activityTypeKey).toBe("Elsa.HttpRequest");
    expect(findActivityAvailabilityDiagnostic(["CustomSet"], diagnostics)?.referenceName).toBe("CustomSet");
  });

  it("ignores available activities and unknown candidates", () => {
    expect(findActivityAvailabilityDiagnostic(["Elsa.WriteLine"], diagnostics)).toBeNull();
    expect(findActivityAvailabilityDiagnostic(["Elsa.DoesNotExist"], diagnostics)).toBeNull();
    expect(findActivityAvailabilityDiagnostic([null, undefined], diagnostics)).toBeNull();
  });
});

describe("getAvailabilityActivityEntries", () => {
  it("keeps resolved activity-type rows sorted by display name", () => {
    const entries = getAvailabilityActivityEntries(diagnostics);
    expect(entries.map(entry => entry.activityTypeKey)).toEqual(["Elsa.HttpRequest", "Elsa.WriteLine"]);
  });
});

describe("draft + mode helpers", () => {
  it("normalizes modes and payloads", () => {
    expect(normalizeAvailabilityMode(1)).toBe("Only");
    expect(normalizeAvailabilityMode("AllExcept")).toBe("AllExcept");
    expect(normalizeAvailabilityMode("only")).toBe("Only");
    expect(getAvailabilityModePayload("Only")).toBe(1);
    expect(getAvailabilityModePayload("AllExcept")).toBe(0);
  });

  it("builds a draft from settings", () => {
    const draft = createAvailabilityDraft({ scope: "host-default", mode: 1, rules: { activityTypes: ["a"], sets: ["Core"] } });
    expect(draft).toEqual({ mode: "Only", activityTypes: ["a"], sets: ["Core"] });
  });

  it("toggles list values and keeps them sorted", () => {
    expect(toggleListValue(["b"], "a")).toEqual(["a", "b"]);
    expect(toggleListValue(["a", "b"], "a")).toEqual(["b"]);
  });
});

describe("activity availability wire normalization", () => {
  it("canonicalizes camel-case settings and diagnostics enums", () => {
    const settings = normalizeActivityAvailabilitySettings({
      scope: "host-default",
      mode: "allExcept",
      rules: { activityTypes: [], sets: [] }
    });
    const result = normalizeActivityAvailabilityDiagnostics({
      items: [{
        activityDefinitionId: "def-1",
        activityTypeKey: "Elsa.WriteLine",
        displayName: "Write Line",
        category: "Primitives",
        state: "blockedByHostBaseline",
        layer: "hostBaseline",
        referenceKind: "activityType",
        referenceName: "Elsa.WriteLine",
        reason: "Blocked"
      }],
      sets: []
    });

    expect(settings.mode).toBe("AllExcept");
    expect(result.items[0]).toEqual(expect.objectContaining({
      state: "BlockedByHostBaseline",
      layer: "HostBaseline",
      referenceKind: "ActivityType"
    }));
    expect(getAvailabilityActivityEntries(result)).toHaveLength(1);
  });
});

describe("groupAvailabilityEntriesByCategory", () => {
  it("buckets entries into alphabetical category groups", () => {
    const groups = groupAvailabilityEntriesByCategory(getAvailabilityActivityEntries(diagnostics));
    expect(groups.map(group => group.category)).toEqual(["Http", "Primitives"]);
    expect(groups[0].entries.map(entry => entry.activityTypeKey)).toEqual(["Elsa.HttpRequest"]);
  });

  it("falls back to Uncategorized for blank categories", () => {
    const groups = groupAvailabilityEntriesByCategory([
      { activityDefinitionId: "def-3", activityTypeKey: "Elsa.Custom", displayName: "Custom", category: "  ", state: "Available", layer: "Catalog", referenceKind: "ActivityType", referenceName: null, reason: "ok" }
    ]);
    expect(groups.map(group => group.category)).toEqual(["Uncategorized"]);
  });
});

describe("effective availability of rule targets", () => {
  const draft = { mode: "AllExcept" as const, activityTypes: ["Elsa.HttpRequest"], sets: [] };

  it("treats listed entries as exclusions in AllExcept mode and inclusions in Only mode", () => {
    expect(isRuleTargetEnabled(draft, "activityTypes", "Elsa.HttpRequest")).toBe(false);
    expect(isRuleTargetEnabled(draft, "activityTypes", "Elsa.WriteLine")).toBe(true);
    const only = { ...draft, mode: "Only" as const };
    expect(isRuleTargetEnabled(only, "activityTypes", "Elsa.HttpRequest")).toBe(true);
    expect(isRuleTargetEnabled(only, "activityTypes", "Elsa.WriteLine")).toBe(false);
  });

  it("updates list membership so the effective state matches", () => {
    const enabled = setRuleTargetEnabled(draft, "activityTypes", "Elsa.HttpRequest", true);
    expect(enabled.activityTypes).toEqual([]);
    const disabled = setRuleTargetEnabled(draft, "activityTypes", "Elsa.WriteLine", false);
    expect(disabled.activityTypes).toEqual(["Elsa.HttpRequest", "Elsa.WriteLine"]);
    // No-op when the target is already in the requested state.
    expect(setRuleTargetEnabled(draft, "activityTypes", "Elsa.WriteLine", true)).toBe(draft);
  });
});

describe("computeEffectiveAvailability", () => {
  const entries = getAvailabilityActivityEntries(diagnostics);
  const sets = [{ name: "Core", activityTypeKeys: ["Elsa.WriteLine"] }];

  it("locks host-blocked activities off regardless of rules", () => {
    const blocked = [{
      activityDefinitionId: "def-4",
      activityTypeKey: "Elsa.Blocked",
      displayName: "Blocked",
      category: "X",
      state: "BlockedByHostBaseline" as const,
      layer: "HostBaseline" as const,
      referenceKind: "ActivityType" as const,
      referenceName: null,
      reason: "blocked"
    }];
    const result = computeEffectiveAvailability(blocked, [], { mode: "Only", activityTypes: ["Elsa.Blocked"], sets: [] });
    expect(result.get("Elsa.Blocked")).toEqual({ enabled: false, lockedBy: "host-baseline" });
  });

  it("locks members of a selected set to the set's contribution", () => {
    const excluded = computeEffectiveAvailability(entries, sets, { mode: "AllExcept", activityTypes: [], sets: ["Core"] });
    expect(excluded.get("Elsa.WriteLine")).toEqual({ enabled: false, lockedBy: "set-rule", governingSet: "Core" });
    expect(excluded.get("Elsa.HttpRequest")).toEqual({ enabled: true, lockedBy: null });

    const included = computeEffectiveAvailability(entries, sets, { mode: "Only", activityTypes: [], sets: ["Core"] });
    expect(included.get("Elsa.WriteLine")).toEqual({ enabled: true, lockedBy: "set-rule", governingSet: "Core" });
    expect(included.get("Elsa.HttpRequest")).toEqual({ enabled: false, lockedBy: null });
  });

  it("applies individual rules when no set governs the activity", () => {
    const result = computeEffectiveAvailability(entries, sets, { mode: "AllExcept", activityTypes: ["Elsa.HttpRequest"], sets: [] });
    expect(result.get("Elsa.HttpRequest")).toEqual({ enabled: false, lockedBy: null });
    expect(result.get("Elsa.WriteLine")).toEqual({ enabled: true, lockedBy: null });
  });
});

describe("isAvailabilityDraftDirty", () => {
  const settings = { scope: "host-default", mode: "AllExcept" as const, rules: { activityTypes: ["b", "a"], sets: [] } };

  it("ignores list order but detects mode and membership changes", () => {
    expect(isAvailabilityDraftDirty({ mode: "AllExcept", activityTypes: ["a", "b"], sets: [] }, settings)).toBe(false);
    expect(isAvailabilityDraftDirty({ mode: "Only", activityTypes: ["a", "b"], sets: [] }, settings)).toBe(true);
    expect(isAvailabilityDraftDirty({ mode: "AllExcept", activityTypes: ["a"], sets: [] }, settings)).toBe(true);
    expect(isAvailabilityDraftDirty({ mode: "AllExcept", activityTypes: ["a", "b"], sets: ["Core"] }, settings)).toBe(true);
  });
});

describe("details panel helpers", () => {
  it("labels policy layers from numeric and string forms", () => {
    expect(getAvailabilityLayerLabel(1)).toBe("Host baseline");
    expect(getAvailabilityLayerLabel("ManagementSettings")).toBe("Management settings");
    expect(getAvailabilityLayerLabel(null)).toBe("");
  });

  it("summarizes catalog arguments defensively", () => {
    const summaries = summarizeAvailabilityArguments([
      { name: "condition", displayName: "Condition", type: { typeName: "Boolean" }, description: "The condition." },
      { name: "body", type: "String" },
      { noName: true },
      null
    ]);
    expect(summaries).toEqual([
      { name: "Condition", typeName: "Boolean", description: "The condition." },
      { name: "body", typeName: "String", description: "" }
    ]);
  });
});
