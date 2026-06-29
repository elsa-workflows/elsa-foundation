import { describe, expect, it } from "vitest";
import {
  createAvailabilityDraft,
  findActivityAvailabilityDiagnostic,
  getAvailabilityActivityEntries,
  getAvailabilityModePayload,
  getAvailabilityStateLabel,
  getAvailabilityStateName,
  normalizeAvailabilityMode,
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
