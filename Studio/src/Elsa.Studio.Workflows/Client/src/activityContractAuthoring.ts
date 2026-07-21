import type {
  ActivityAuthoringCapabilities,
  ActivityContract,
  ActivityContractTypeCapability,
  ActivityInputContract,
  ActivityInputDefault,
  ActivityOutcomeContract,
  ActivityOutputContract
} from "./activityDefinitionTypes";

export type ActivityContractMemberKind = "input" | "output" | "outcome";
export type ActivityDefaultMode = "none" | "literal" | "expression";

export function authorableContractTypes(capabilities: ActivityAuthoringCapabilities | undefined) {
  return [...(capabilities?.types ?? [])]
    .filter(type => type.supportsDurability && type.supportedCollectionKinds.length > 0 && activatedStorageDrivers(type, capabilities).length > 0)
    .sort((left, right) => left.category.localeCompare(right.category) || left.displayName.localeCompare(right.displayName) || left.alias.localeCompare(right.alias));
}

export function activatedStorageDrivers(
  type: ActivityContractTypeCapability | undefined,
  capabilities: ActivityAuthoringCapabilities | undefined
) {
  if (!type || !capabilities) return [];
  const activated = new Set(capabilities.storageDriverKeys);
  return type.compatibleStorageDriverKeys.filter(driver => activated.has(driver));
}

export function findContractType(
  capabilities: ActivityAuthoringCapabilities | undefined,
  alias: string
) {
  return capabilities?.types.find(type => type.alias === alias);
}

export function isContractTypeSelectionAuthorable(
  type: ActivityContractTypeCapability | undefined,
  collectionKind: string,
  storageDriverKey: string,
  capabilities: ActivityAuthoringCapabilities | undefined
) {
  return Boolean(
    type?.supportsDurability &&
    type.supportedCollectionKinds.includes(collectionKind) &&
    activatedStorageDrivers(type, capabilities).includes(storageDriverKey)
  );
}

export function createInputContract(
  name: string,
  contract: ActivityContract,
  capabilities: ActivityAuthoringCapabilities
): ActivityInputContract | null {
  const selection = initialTypeSelection(capabilities);
  if (!selection) return null;
  const referenceKey = generateReferenceKey(name, contract.inputs.map(member => member.referenceKey));
  return {
    referenceKey,
    name: name.trim(),
    displayName: name.trim(),
    description: null,
    category: null,
    order: contract.inputs.length,
    uiHint: null,
    uiSpecifications: null,
    type: { alias: selection.type.alias, collectionKind: selection.collectionKind },
    isRequired: false,
    isNullable: false,
    default: null,
    storageDriverKey: selection.storageDriverKey,
    durability: "Required"
  };
}

export function createOutputContract(
  name: string,
  contract: ActivityContract,
  capabilities: ActivityAuthoringCapabilities
): ActivityOutputContract | null {
  const selection = initialTypeSelection(capabilities);
  if (!selection) return null;
  const referenceKey = generateReferenceKey(name, contract.outputs.map(member => member.referenceKey));
  return {
    referenceKey,
    name: name.trim(),
    displayName: name.trim(),
    description: null,
    category: null,
    order: contract.outputs.length,
    uiHint: null,
    uiSpecifications: null,
    type: { alias: selection.type.alias, collectionKind: selection.collectionKind },
    isRequired: false,
    isNullable: false,
    storageDriverKey: selection.storageDriverKey,
    durability: "Required"
  };
}

export function createOutcomeContract(name: string, contract: ActivityContract): ActivityOutcomeContract {
  return {
    referenceKey: generateReferenceKey(name, contract.outcomes.map(member => member.referenceKey)),
    name: name.trim(),
    isEmitted: false,
    description: null
  };
}

export function generateReferenceKey(name: string, existingKeys: Iterable<string>, currentKey?: string) {
  const base = name
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .trim()
    .toLocaleLowerCase("en-US")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "") || "member";
  const existing = new Set([...existingKeys].filter(key => key !== currentKey));
  if (!existing.has(base)) return base;
  for (let suffix = 2; ; suffix += 1) {
    const candidate = `${base}-${suffix}`;
    if (!existing.has(candidate)) return candidate;
  }
}

export function defaultMode(value: ActivityInputDefault | null | undefined): ActivityDefaultMode {
  if (!value) return "none";
  return value.syntax === "Literal" ? "literal" : "expression";
}

export function serializeDefaultValue(value: unknown) {
  return JSON.stringify(value, null, 2);
}

export function parseLiteralDefault(source: string): { ok: true; value: unknown } | { ok: false } {
  try {
    return { ok: true, value: JSON.parse(source) as unknown };
  } catch {
    return { ok: false };
  }
}

export function selectTypeForMember<T extends ActivityInputContract | ActivityOutputContract>(
  member: T,
  type: ActivityContractTypeCapability,
  capabilities: ActivityAuthoringCapabilities
): T {
  const drivers = activatedStorageDrivers(type, capabilities);
  const collectionKind = type.supportedCollectionKinds.includes(member.type.collectionKind)
    ? member.type.collectionKind
    : type.supportedCollectionKinds[0] ?? "Single";
  const storageDriverKey = drivers.includes(member.storageDriverKey)
    ? member.storageDriverKey
    : drivers[0] ?? "";
  return {
    ...member,
    type: { ...member.type, alias: type.alias, collectionKind },
    isNullable: type.supportsNull ? member.isNullable : false,
    storageDriverKey
  };
}

function initialTypeSelection(capabilities: ActivityAuthoringCapabilities) {
  const authorable = authorableContractTypes(capabilities);
  // Prefer a String capability type over the alphabetically-first authorable type (which would otherwise
  // default new members to Boolean); fall back to the first authorable type when String is unavailable.
  const type = authorable.find(candidate => candidate.alias.toLowerCase() === "string") ?? authorable[0];
  if (!type) return null;
  const drivers = activatedStorageDrivers(type, capabilities);
  return {
    type,
    // Prefer a Single (scalar) collection kind over the first advertised kind (which would otherwise
    // default new members to Array).
    collectionKind: type.supportedCollectionKinds.includes("Single") ? "Single" : type.supportedCollectionKinds[0],
    storageDriverKey: drivers[0]
  };
}
