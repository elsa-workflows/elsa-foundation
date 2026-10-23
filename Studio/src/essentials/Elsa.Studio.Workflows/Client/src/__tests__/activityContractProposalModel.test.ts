import { describe, expect, it } from "vitest";
import {
  activityContractProposalBinding,
  proposalMatchesBinding,
  proposalRequest
} from "../activityContractProposalBinding";
import {
  contractMemberFacts,
  reviewProposalChange
} from "../activityContractProposalModel";
import type {
  ActivityInputContract,
  ActivityContractProposalChange,
  ActivityDefinitionDraftView
} from "../activityDefinitionTypes";

describe("activity contract proposal model", () => {
  it("binds generation to the exact draft revision, provider schema, and implementation fingerprint", () => {
    const draft = exactDraft();
    const binding = activityContractProposalBinding(draft);

    expect(proposalRequest(binding)).toEqual({
      expectedRevision: 7,
      expectedProviderKey: "elsa.activity-graph",
      expectedProviderSchemaVersion: "1",
      expectedManifestFingerprint: "sha256:implementation-7"
    });
    expect(proposalMatchesBinding({
      ...binding,
      proposalFingerprint: "sha256:proposal",
      changes: [],
      diagnostics: []
    }, binding)).toBe(true);
    expect(proposalMatchesBinding({
      ...binding,
      revision: 8,
      proposalFingerprint: "sha256:proposal",
      changes: [],
      diagnostics: []
    }, binding)).toBe(false);
  });

  it("selects additive-safe changes while classifying removals and tightening replacements as destructive", () => {
    const contract = exactDraft().contract;
    const addition = reviewProposalChange(contract, change({
      changeId: "input:add:currency",
      operation: "Add",
      referenceKey: "currency",
      input: input("currency", false, true)
    }));
    const removal = reviewProposalChange(contract, change({
      changeId: "input:remove:order",
      operation: "Remove",
      referenceKey: "order"
    }));
    const tightening = reviewProposalChange(contract, change({
      changeId: "input:replace:order",
      operation: "Replace",
      referenceKey: "order",
      input: input("order", true, false)
    }));

    expect(addition).toMatchObject({ supported: true, destructive: false, impact: "Additive" });
    expect(removal).toMatchObject({ supported: true, destructive: true, impact: "Breaking" });
    expect(tightening).toMatchObject({ supported: true, destructive: true, impact: "Breaking" });
    expect(tightening.impactReasons.join(" ")).toContain("Tightening nullability");
  });

  it("degrades unknown or malformed kinds without reading opaque provider payloads", () => {
    const review = reviewProposalChange(exactDraft().contract, {
      changeId: "future:change",
      operation: "MergeProviderPayload",
      memberKind: "FutureMember",
      referenceKey: "hidden",
      futureOpaquePayload: { secret: "must-not-render" }
    });

    expect(review).toEqual(expect.objectContaining({
      supported: false,
      destructive: true,
      impact: "Unknown",
      current: null,
      proposed: null
    }));
    expect(JSON.stringify(review)).not.toContain("must-not-render");
  });

  it("summarizes structured member facts without exposing literal or expression default content", () => {
    const member = input("order", false, true);
    member.default = { syntax: "Expression", value: "secret.expression.source" };

    const facts = contractMemberFacts(member);

    expect(facts).toContain("Configured Expression default");
    expect(facts.join(" ")).not.toContain("secret.expression.source");
  });
});

function exactDraft(): ActivityDefinitionDraftView {
  return {
    draftId: "draft-7",
    definitionId: "definition-1",
    revision: 7,
    sourceVersionId: "version-1",
    status: "Active",
    contract: {
      contractSchemaVersion: "1",
      inputs: [input("order", false, true)],
      outputs: [],
      outcomes: []
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: "sha256:implementation-7",
      payload: {}
    },
    layout: [],
    createdAt: "2026-07-18T09:00:00Z",
    updatedAt: "2026-07-18T09:00:00Z"
  };
}

function input(referenceKey: string, isRequired: boolean, isNullable: boolean): ActivityInputContract {
  return {
    referenceKey,
    name: referenceKey === "order" ? "Order" : "Currency",
    displayName: referenceKey === "order" ? "Order" : "Currency",
    description: null,
    category: null,
    order: 0,
    uiHint: null,
    uiSpecifications: null,
    type: { alias: "String", collectionKind: "Single" },
    isRequired,
    isNullable,
    default: null,
    storageDriverKey: "elsa.json",
    durability: "Required"
  };
}

function change(overrides: Partial<ActivityContractProposalChange>): ActivityContractProposalChange {
  return {
    changeId: "change-1",
    operation: "Add",
    memberKind: "Input",
    referenceKey: "order",
    input: null,
    output: null,
    outcome: null,
    ...overrides
  };
}
