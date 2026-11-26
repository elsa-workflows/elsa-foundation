import { describe, expect, it } from "vitest";
import type { StudioActivityDefinitionImplementationEditorContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityAuthoringCapabilities } from "../activityDefinitionTypes";
import { hasMigrationProviderChoice, migrationProviderChoices } from "../activityDefinitionDraftManagementShared";

const editor = (providerKey: string, schemaVersion: string) => ({
  providerKey,
  providerSchemaVersion: schemaVersion
}) as StudioActivityDefinitionImplementationEditorContribution;

function capabilities(providers: ActivityAuthoringCapabilities["providers"]): ActivityAuthoringCapabilities {
  return { providers } as ActivityAuthoringCapabilities;
}

function provider(
  providerKey: string,
  migratableFromSchemaVersions: string[],
  migratableFrom?: Array<{ providerKey: string; schemaVersion: string }>
): ActivityAuthoringCapabilities["providers"][number] {
  return {
    providerKey,
    displayName: providerKey,
    requiredOutcomes: [],
    manifestSchemas: [{ schemaVersion: "1", isAuthorable: true, migratableFromSchemaVersions, migratableFrom }]
  };
}

describe("activity definition migration targets", () => {
  it("keeps legacy schema declarations within the same provider", () => {
    const choices = migrationProviderChoices(
      capabilities([provider("graph", ["1"])]),
      [editor("graph", "1")],
      "graph",
      "1"
    );

    expect(choices.map(choice => `${choice.providerKey}|${choice.schemaVersion}`)).toEqual(["graph|1"]);
  });

  it("rejects schema-number collisions across providers without an exact source binding", () => {
    const oldBackend = capabilities([provider("graph", ["1"])]);

    expect(migrationProviderChoices(oldBackend, [editor("graph", "1")], "clr-sequence", "1")).toEqual([]);
    expect(hasMigrationProviderChoice(oldBackend, [editor("graph", "1")], "clr-sequence", "1")).toBe(false);
  });

  it("accepts an explicitly declared cross-provider source and requires its exact editor", () => {
    const current = capabilities([provider("graph", [], [{ providerKey: "clr-sequence", schemaVersion: "1" }])]);

    expect(migrationProviderChoices(current, [editor("graph", "1")], "clr-sequence", "1")).toHaveLength(1);
    expect(hasMigrationProviderChoice(current, [editor("graph", "1")], "clr-sequence", "1")).toBe(true);
    expect(hasMigrationProviderChoice(current, [], "clr-sequence", "1")).toBe(false);
    expect(migrationProviderChoices(current, [editor("graph", "1")], undefined, "1")).toEqual([]);
  });
});
