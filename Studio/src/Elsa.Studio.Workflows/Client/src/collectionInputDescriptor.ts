import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import {
  describeCollectionType,
  describeDictionaryType,
  type CollectionTypeInfo,
  type DictionaryTypeInfo
} from "./activityProperties";

// --- descriptor-aware collection/dictionary detection (elsa-foundation#945) ----------------------
//
// The authoring catalog now reports a truthful `collectionKind` (Single/Array/List/HashSet/Dictionary)
// alongside `typeName` — for a collection input `typeName` is the ELEMENT-type alias (#924's scalar-lie
// fix). When present it authoritatively selects the list/dictionary editor; when absent (older backend)
// we fall back to parsing the CLR `typeName`, preserving the pre-#945 behavior exactly.
//
// Kept in a dedicated module imported only by the deferred inspector surface so these helpers stay out
// of the eager module chunk (and therefore off the authenticated landing bundles).

const listCollectionKinds = new Set(["array", "list", "hashset"]);

function readCollectionKind(input: StudioActivityInputDescriptor): string | null {
  const kind = input.collectionKind;
  return typeof kind === "string" && kind.trim() ? kind.trim().toLowerCase() : null;
}

/** Collection detection preferring the descriptor's `collectionKind`, else the `typeName` parse. */
export function describeCollectionForInput(input: StudioActivityInputDescriptor): CollectionTypeInfo | null {
  const kind = readCollectionKind(input);
  if (kind === "dictionary" || kind === "single") return null;
  if (kind && listCollectionKinds.has(kind)) {
    // Prefer a full collection type name when the backend still sends one; otherwise `typeName` is the
    // element alias itself (the #945 contract).
    return describeCollectionType(input.typeName) ?? { elementTypeName: input.typeName?.trim() || null };
  }
  return describeCollectionType(input.typeName);
}

/** Dictionary detection preferring the descriptor's `collectionKind`, else the `typeName` parse. */
export function describeDictionaryForInput(input: StudioActivityInputDescriptor): DictionaryTypeInfo | null {
  const kind = readCollectionKind(input);
  if (kind === "dictionary") {
    // A full dictionary type name yields the value type directly; otherwise treat `typeName` as the value
    // alias (dictionary keys are string-typed in every supported family).
    return describeDictionaryType(input.typeName) ?? { valueTypeName: input.typeName?.trim() || "System.String" };
  }
  if (kind && (kind === "single" || listCollectionKinds.has(kind))) return null;
  return describeDictionaryType(input.typeName);
}
