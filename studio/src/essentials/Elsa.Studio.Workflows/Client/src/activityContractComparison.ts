import type {
  ActivityContract,
  ActivityInputContract,
  ActivityOutcomeContract,
  ActivityOutputContract
} from "./activityDefinitionTypes";

export type ActivityContractMemberKind = "input" | "output" | "outcome";
export type ActivityContractMember = ActivityInputContract | ActivityOutputContract | ActivityOutcomeContract;

export function findActivityContractMember(
  contract: ActivityContract,
  kind: ActivityContractMemberKind,
  referenceKey: string
) {
  return contract[`${kind}s`].find(member => member.referenceKey === referenceKey);
}

export function classifyBreakingContractTransition(
  kind: ActivityContractMemberKind,
  current: ActivityContractMember,
  next: ActivityContractMember
) {
  const reasons: string[] = [];
  if (current.name !== next.name) reasons.push("Changing the technical name is classified as breaking; edit Display name for presentation-only copy.");
  if (kind !== "outcome") {
    const currentBoundary = current as ActivityInputContract | ActivityOutputContract;
    const nextBoundary = next as ActivityInputContract | ActivityOutputContract;
    if (currentBoundary.type.alias !== nextBoundary.type.alias || currentBoundary.type.collectionKind !== nextBoundary.type.collectionKind)
      reasons.push("Changing the capability type or collection kind is classified as breaking.");
    if (currentBoundary.storageDriverKey !== nextBoundary.storageDriverKey)
      reasons.push("Changing the durable storage driver is classified as breaking.");
    if (currentBoundary.isNullable && !nextBoundary.isNullable)
      reasons.push("Tightening nullability is classified as breaking.");
  }
  if (kind === "input" && "default" in current && "default" in next) {
    if (!current.isRequired && next.isRequired && next.default === null)
      reasons.push("Requiring an effective value without a default is classified as breaking.");
    if (current.default !== null && next.default === null)
      reasons.push("Removing a configured default is classified as breaking.");
    if (current.default !== null && next.default !== null && JSON.stringify(current.default) !== JSON.stringify(next.default))
      reasons.push("Changing an existing default is classified as breaking.");
  }
  if (kind === "output" && "isRequired" in current && "isRequired" in next && current.isRequired && !next.isRequired)
    reasons.push("Changing this published output production obligation is classified as breaking by the authoritative compatibility policy.");
  if (kind === "outcome" && "isEmitted" in current && "isEmitted" in next && !current.isEmitted && next.isEmitted)
    reasons.push("Changing a published outcome to emitted is classified as breaking.");
  return reasons;
}
