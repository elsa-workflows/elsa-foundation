export type StructureDesignMode = "sequence" | "flowchart" | "bpmn" | "compensation" | "generic";

export interface StructureDesignSlotDescriptor {
  name: string;
  property: string;
  displayName: string;
  cardinality: "single" | "many";
  collectionProperty?: string;
  childProperty?: string;
  labelProperty?: string;
  slotNameTemplate?: string;
  itemIdProperty?: string;
  conditionProperty?: string;
}

export type FixedStructureDesignSlotDescriptor = StructureDesignSlotDescriptor & {
  collectionProperty?: never;
  childProperty?: never;
  labelProperty?: never;
  slotNameTemplate?: never;
  itemIdProperty?: never;
  conditionProperty?: never;
};

export type CollectionStructureDesignSlotDescriptor = StructureDesignSlotDescriptor & {
  cardinality: "single";
  collectionProperty: string;
  childProperty: string;
  labelProperty: string;
  slotNameTemplate: string;
};

export interface StructureDesignFacetPayload {
  mode: StructureDesignMode;
  supportsScopedVariables: boolean;
  slots: StructureDesignSlotDescriptor[];
  initialPayload: Record<string, unknown>;
}

export interface StructureDesignFacet {
  kind: string;
  schemaVersion: string;
  payload: StructureDesignFacetPayload;
}

export interface StructureDesignSlotMatch {
  descriptor: StructureDesignSlotDescriptor;
  itemLabel?: string;
}

export interface StructureDesignSlotMatchIssue {
  slotName: string;
  code: "unmatched" | "ambiguous" | "duplicate-fixed-slot" | "single-cardinality-overflow";
}

export function fixedStructureSlot<T extends FixedStructureDesignSlotDescriptor>(descriptor: T): T {
  return descriptor;
}

export function collectionStructureSlot<T extends CollectionStructureDesignSlotDescriptor>(descriptor: T): T {
  return descriptor;
}

export function isCollectionStructureDesignSlot(
  descriptor: StructureDesignSlotDescriptor
): descriptor is StructureDesignSlotDescriptor & Required<Pick<StructureDesignSlotDescriptor,
  "collectionProperty" | "childProperty" | "labelProperty" | "slotNameTemplate">> {
  return typeof descriptor.collectionProperty === "string"
    && typeof descriptor.childProperty === "string"
    && typeof descriptor.labelProperty === "string"
    && typeof descriptor.slotNameTemplate === "string";
}

export function validateStructureDesignSlotDescriptors(descriptors: StructureDesignSlotDescriptor[]): string[] {
  const errors: string[] = [];
  const names = new Set<string>();
  const addresses = new Set<string>();
  const storageKinds = new Map<string, "fixed" | "collection">();
  const templates: { descriptor: StructureDesignSlotDescriptor; prefix: string; suffix: string }[] = [];
  const collectionKeys: (keyof StructureDesignSlotDescriptor)[] = [
    "collectionProperty", "childProperty", "labelProperty", "slotNameTemplate", "itemIdProperty", "conditionProperty"
  ];

  for (const [index, descriptor] of descriptors.entries()) {
    const location = `slots[${index}]`;
    if (!nonblank(descriptor.name) || !nonblank(descriptor.property) || !nonblank(descriptor.displayName)) {
      errors.push(`${location} requires non-empty name, property, and displayName values.`);
    }
    if (names.has(descriptor.name)) errors.push(`${location} duplicates slot name '${descriptor.name}'.`);
    names.add(descriptor.name);

    const hasCollectionMetadata = collectionKeys.some(key => descriptor[key] !== undefined);
    const collection = isCollectionStructureDesignSlot(descriptor);
    if (hasCollectionMetadata && !collection) {
      errors.push(`${location} has incomplete collection slot metadata.`);
      continue;
    }
    if (collection) {
      if (!nonblank(descriptor.collectionProperty) || !nonblank(descriptor.childProperty) || !nonblank(descriptor.labelProperty)) {
        errors.push(`${location} has incomplete collection slot metadata.`);
        continue;
      }
      if (descriptor.cardinality !== "single") errors.push(`${location} collection items must have single cardinality.`);
      if ((descriptor.itemIdProperty !== undefined && !nonblank(descriptor.itemIdProperty))
        || (descriptor.conditionProperty !== undefined && !nonblank(descriptor.conditionProperty))) {
        errors.push(`${location} has an invalid optional collection property.`);
      }
      const address = `collection:${descriptor.collectionProperty}:${descriptor.childProperty}`;
      if (addresses.has(address)) errors.push(`${location} duplicates physical slot address '${address}'.`);
      addresses.add(address);
      const storageKind = storageKinds.get(descriptor.collectionProperty);
      if (storageKind === "fixed") errors.push(`${location} conflicts with a fixed slot using '${descriptor.collectionProperty}'.`);
      storageKinds.set(descriptor.collectionProperty, "collection");

      const template = parseSlotNameTemplate(descriptor);
      if (!template) errors.push(`${location} slotNameTemplate must contain exactly one '{${descriptor.labelProperty}}' placeholder and no other placeholders.`);
      else templates.push({ descriptor, ...template });
    } else {
      const address = `fixed:${descriptor.property}`;
      if (addresses.has(address)) errors.push(`${location} duplicates physical slot address '${address}'.`);
      addresses.add(address);
      const storageKind = storageKinds.get(descriptor.property);
      if (storageKind === "collection") errors.push(`${location} conflicts with a collection slot using '${descriptor.property}'.`);
      storageKinds.set(descriptor.property, "fixed");
    }
  }

  for (let left = 0; left < templates.length; left++) {
    for (let right = left + 1; right < templates.length; right++) {
      if (templatesOverlap(templates[left], templates[right])) {
        errors.push(`Collection slot templates '${templates[left].descriptor.slotNameTemplate}' and '${templates[right].descriptor.slotNameTemplate}' are ambiguous.`);
      }
    }
  }
  return errors;
}

export function parseStructureDesignFacetPayload(payload: Record<string, unknown>): { payload: StructureDesignFacetPayload | null; errors: string[] } {
  const mode = payload.mode;
  if (mode !== "sequence" && mode !== "flowchart" && mode !== "bpmn" && mode !== "compensation" && mode !== "generic") {
    return { payload: null, errors: ["Structure facet mode is missing or invalid."] };
  }
  if (typeof payload.supportsScopedVariables !== "boolean") return { payload: null, errors: ["Structure facet supportsScopedVariables must be boolean."] };
  if (!Array.isArray(payload.slots) || !isRecord(payload.initialPayload)) return { payload: null, errors: ["Structure facet requires slots and an initialPayload object."] };

  const slots: StructureDesignSlotDescriptor[] = [];
  const errors: string[] = [];
  payload.slots.forEach((value, index) => {
    const descriptor = parseStructureDesignSlotDescriptor(value);
    if (!descriptor) errors.push(`slots[${index}] is incomplete or contains a non-string property.`);
    else slots.push(descriptor);
  });
  if (errors.length) return { payload: null, errors };
  errors.push(...validateStructureDesignSlotDescriptors(slots));
  if (errors.length) return { payload: null, errors };
  return { payload: { mode, supportsScopedVariables: payload.supportsScopedVariables, slots, initialPayload: payload.initialPayload }, errors: [] };
}

export function matchStructureDesignSlots(
  slots: { name: string; activities: unknown[] }[],
  descriptors: StructureDesignSlotDescriptor[]
): { matches: { slotIndex: number; match: StructureDesignSlotMatch }[]; issues: StructureDesignSlotMatchIssue[] } {
  const matches: { slotIndex: number; match: StructureDesignSlotMatch }[] = [];
  const issues: StructureDesignSlotMatchIssue[] = [];
  const fixedMatches = new Set<StructureDesignSlotDescriptor>();

  slots.forEach((slot, slotIndex) => {
    const candidates: StructureDesignSlotMatch[] = [];
    for (const descriptor of descriptors) {
      if (!isCollectionStructureDesignSlot(descriptor)) {
        if (descriptor.name === slot.name) candidates.push({ descriptor });
        continue;
      }
      const template = parseSlotNameTemplate(descriptor);
      if (!template || !slot.name.startsWith(template.prefix) || !slot.name.endsWith(template.suffix)
        || slot.name.length < template.prefix.length + template.suffix.length) continue;
      candidates.push({ descriptor, itemLabel: slot.name.slice(template.prefix.length, slot.name.length - template.suffix.length) });
    }

    if (candidates.length === 0) {
      issues.push({ slotName: slot.name, code: "unmatched" });
      return;
    }
    if (candidates.length !== 1) {
      issues.push({ slotName: slot.name, code: "ambiguous" });
      return;
    }
    const match = candidates[0];
    if (!isCollectionStructureDesignSlot(match.descriptor) && fixedMatches.has(match.descriptor)) {
      issues.push({ slotName: slot.name, code: "duplicate-fixed-slot" });
      return;
    }
    if (!isCollectionStructureDesignSlot(match.descriptor)) fixedMatches.add(match.descriptor);
    if (match.descriptor.cardinality === "single" && slot.activities.length > 1) {
      issues.push({ slotName: slot.name, code: "single-cardinality-overflow" });
      return;
    }
    matches.push({ slotIndex, match });
  });
  return { matches, issues };
}

function parseStructureDesignSlotDescriptor(value: unknown): StructureDesignSlotDescriptor | null {
  if (!isRecord(value)) return null;
  const name = readRequiredString(value.name);
  const property = readRequiredString(value.property);
  const displayName = readRequiredString(value.displayName);
  const cardinality = value.cardinality;
  if (!name || !property || !displayName || (cardinality !== "single" && cardinality !== "many")) return null;

  const optionalKeys = ["collectionProperty", "childProperty", "labelProperty", "slotNameTemplate", "itemIdProperty", "conditionProperty"];
  for (const key of optionalKeys) if (key in value && value[key] !== null && typeof value[key] !== "string") return null;
  return {
    name,
    property,
    displayName,
    cardinality,
    ...(typeof value.collectionProperty === "string" ? { collectionProperty: value.collectionProperty } : {}),
    ...(typeof value.childProperty === "string" ? { childProperty: value.childProperty } : {}),
    ...(typeof value.labelProperty === "string" ? { labelProperty: value.labelProperty } : {}),
    ...(typeof value.slotNameTemplate === "string" ? { slotNameTemplate: value.slotNameTemplate } : {}),
    ...(typeof value.itemIdProperty === "string" ? { itemIdProperty: value.itemIdProperty } : {}),
    ...(typeof value.conditionProperty === "string" ? { conditionProperty: value.conditionProperty } : {})
  };
}

function parseSlotNameTemplate(descriptor: StructureDesignSlotDescriptor & Required<Pick<StructureDesignSlotDescriptor,
  "collectionProperty" | "childProperty" | "labelProperty" | "slotNameTemplate">>) {
  const token = `{${descriptor.labelProperty}}`;
  const start = descriptor.slotNameTemplate.indexOf(token);
  if (start < 0 || descriptor.slotNameTemplate.indexOf(token, start + token.length) >= 0) return null;
  const prefix = descriptor.slotNameTemplate.slice(0, start);
  const suffix = descriptor.slotNameTemplate.slice(start + token.length);
  if (/[{}]/.test(prefix + suffix)) return null;
  return { prefix, suffix };
}

function templatesOverlap(left: { prefix: string; suffix: string }, right: { prefix: string; suffix: string }) {
  return (left.prefix.startsWith(right.prefix) || right.prefix.startsWith(left.prefix))
    && (left.suffix.endsWith(right.suffix) || right.suffix.endsWith(left.suffix));
}

function readRequiredString(value: unknown) {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function nonblank(value: string) {
  return value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}