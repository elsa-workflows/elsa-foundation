import type {
  ActivityContract,
  ActivityContractProposalChange,
  ActivityInputContract,
  ActivityOutcomeContract,
  ActivityOutputContract
} from "./activityDefinitionTypes";
import {
  classifyBreakingContractTransition,
  findActivityContractMember,
  type ActivityContractMember,
  type ActivityContractMemberKind
} from "./activityContractComparison";

export interface ActivityContractProposalChangeReview {
  changeId: string;
  referenceKey: string;
  operation: "Add" | "Replace" | "Remove" | "Unknown";
  memberKind: ActivityContractMemberKind | "unknown";
  supported: boolean;
  destructive: boolean;
  impact: "Additive" | "Breaking" | "Compatible" | "Unknown";
  impactReasons: string[];
  current: ActivityContractMember | null;
  proposed: ActivityContractMember | null;
}

export function reviewProposalChange(
  contract: ActivityContract,
  change: ActivityContractProposalChange
): ActivityContractProposalChangeReview {
  const memberKind = normalizeMemberKind(change.memberKind);
  const operation = normalizeOperation(change.operation);
  if (!memberKind || !operation || !safeIdentity(change.changeId) || !safeReferenceKey(change.referenceKey)) {
    return unsupportedChange(change);
  }

  const current = findActivityContractMember(contract, memberKind, change.referenceKey) ?? null;
  const proposed = replacementFor(change, memberKind);
  const payloadCount = [change.input, change.output, change.outcome].filter(value => value !== null && value !== undefined).length;
  const validShape = operation === "Remove"
    ? current !== null && payloadCount === 0
    : proposed !== null && proposed.referenceKey === change.referenceKey && payloadCount === 1 &&
      (operation === "Add" ? current === null : current !== null);
  if (!validShape) return unsupportedChange(change);

  if (operation === "Add") return {
    changeId: change.changeId,
    referenceKey: change.referenceKey,
    operation,
    memberKind,
    supported: true,
    destructive: false,
    impact: "Additive",
    impactReasons: ["Adds a new provider-neutral public contract member."],
    current,
    proposed
  };
  if (operation === "Remove") return {
    changeId: change.changeId,
    referenceKey: change.referenceKey,
    operation,
    memberKind,
    supported: true,
    destructive: true,
    impact: "Breaking",
    impactReasons: ["Removing a public contract member is a breaking compatibility change."],
    current,
    proposed
  };

  const reasons = classifyBreakingContractTransition(memberKind, current!, proposed!);
  return {
    changeId: change.changeId,
    referenceKey: change.referenceKey,
    operation,
    memberKind,
    supported: true,
    destructive: reasons.length > 0,
    impact: reasons.length > 0 ? "Breaking" : "Compatible",
    impactReasons: reasons.length > 0 ? reasons : ["No breaking provider-neutral contract transition was detected."],
    current,
    proposed
  };
}

export function safeProposalChangeId(changeId: string, index: number) {
  return safeIdentity(changeId) ? changeId : `provider-change-${index + 1}`;
}

export function contractMemberFacts(member: ActivityContractMember | null) {
  if (!member) return ["Not present"];
  const facts = [`Name: ${member.name}`, `Reference key: ${member.referenceKey}`];
  if (isTypedMember(member)) {
    facts.push(`Type: ${member.type.alias} · ${member.type.collectionKind}`);
    facts.push(`${member.isRequired ? "Required" : "Optional"} · ${member.isNullable ? "Allows null" : "Non-null"}`);
    facts.push(`Durability: ${member.durability} · ${member.storageDriverKey}`);
    if (isInputMember(member)) facts.push(member.default === null ? "No configured default" : `Configured ${member.default.syntax} default`);
  } else {
    facts.push(member.isEmitted ? "Emitted outcome" : "Non-emitted outcome");
  }
  return facts;
}

function unsupportedChange(change: ActivityContractProposalChange): ActivityContractProposalChangeReview {
  return {
    changeId: change.changeId,
    referenceKey: safeReferenceKey(change.referenceKey) ? change.referenceKey : "Unavailable",
    operation: "Unknown",
    memberKind: "unknown",
    supported: false,
    destructive: true,
    impact: "Unknown",
    impactReasons: ["Studio does not recognize this structured proposal kind. Its opaque payload is withheld and cannot be selected."],
    current: null,
    proposed: null
  };
}

function replacementFor(
  change: ActivityContractProposalChange,
  memberKind: ActivityContractMemberKind
): ActivityInputContract | ActivityOutputContract | ActivityOutcomeContract | null {
  if (memberKind === "input") return isRecord(change.input) ? change.input : null;
  if (memberKind === "output") return isRecord(change.output) ? change.output : null;
  return isRecord(change.outcome) ? change.outcome : null;
}

function normalizeOperation(value: string) {
  return value === "Add" || value === "Replace" || value === "Remove" ? value : null;
}

function normalizeMemberKind(value: string): ActivityContractMemberKind | null {
  if (value === "Input") return "input";
  if (value === "Output") return "output";
  if (value === "Outcome") return "outcome";
  return null;
}

function safeIdentity(value: string) {
  return typeof value === "string" && /^[A-Za-z0-9][A-Za-z0-9:._/-]{0,255}$/.test(value);
}

function safeReferenceKey(value: string) {
  return typeof value === "string" &&
    value.length > 0 &&
    value.length <= 256 &&
    ![...value].some(character => {
      const code = character.charCodeAt(0);
      return code <= 31 || code === 127;
    });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value && typeof value === "object" && !Array.isArray(value));
}

function isTypedMember(member: ActivityContractMember): member is ActivityInputContract | ActivityOutputContract {
  return isRecord(member.type) && typeof member.type.alias === "string" && typeof member.type.collectionKind === "string";
}

function isInputMember(member: ActivityInputContract | ActivityOutputContract): member is ActivityInputContract {
  return Object.prototype.hasOwnProperty.call(member, "default");
}
