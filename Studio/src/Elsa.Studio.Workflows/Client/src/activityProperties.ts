import type {
  StudioActivityInputDescriptor,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "./workflowTypes";

export interface ActivityExpression {
  type: string;
  value: unknown;
}

export interface WrappedActivityInputValue {
  typeName: string;
  expression: ActivityExpression;
  memoryReference?: unknown;
}

export function camelize(value: string) {
  const trimmed = value.trim();
  return trimmed ? trimmed.charAt(0).toLowerCase() + trimmed.slice(1) : value;
}

export function getInputPropertyName(descriptor: StudioActivityInputDescriptor) {
  return camelize(descriptor.name);
}

export function readInputValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  const propertyName = getInputPropertyName(descriptor);
  const value = activity[propertyName];
  return descriptor.isWrapped === false ? value ?? descriptor.defaultValue ?? "" : readWrappedInputValue(value, descriptor);
}

export function readWrappedInput(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  return readWrappedInputValue(activity[getInputPropertyName(descriptor)], descriptor);
}

export function withLiteralValue(previous: WrappedActivityInputValue, value: unknown): WrappedActivityInputValue {
  return {
    ...previous,
    expression: {
      type: previous.expression.type || "Literal",
      value
    }
  };
}

export function withSyntax(previous: WrappedActivityInputValue, syntax: string): WrappedActivityInputValue {
  return {
    ...previous,
    expression: {
      type: syntax,
      value: previous.expression.value
    }
  };
}

export function withExpression(previous: WrappedActivityInputValue, syntax: string, value: unknown): WrappedActivityInputValue {
  return {
    ...previous,
    expression: { type: syntax, value }
  };
}

export interface ExpressionModeTransition {
  requiresConfirmation: boolean;
  nextValue: unknown;
}

/** Plans a syntax change without mutating the Activity Property Value. */
export function planExpressionModeTransition(
  sourceMode: StudioExpressionDescriptor["editingMode"],
  targetMode: StudioExpressionDescriptor["editingMode"],
  propertyTypeName: string,
  currentValue: unknown,
  targetDefaultValue: unknown
): ExpressionModeTransition {
  if (isEmptyExpressionValue(currentValue)) {
    return { requiresConfirmation: false, nextValue: targetDefaultValue };
  }

  if (sourceMode === "text" && targetMode === "text") {
    return { requiresConfirmation: false, nextValue: currentValue };
  }

  if (sourceMode === "literal" && targetMode === "text" && isPrimitiveValue(currentValue)) {
    return { requiresConfirmation: false, nextValue: String(currentValue) };
  }

  if (sourceMode === "text" && targetMode === "literal" && isStringType(propertyTypeName)) {
    return { requiresConfirmation: false, nextValue: currentValue };
  }

  if (sourceMode === targetMode && (sourceMode === "literal" || sourceMode === "text")) {
    return { requiresConfirmation: false, nextValue: currentValue };
  }

  return { requiresConfirmation: true, nextValue: targetDefaultValue };
}

export function isEmptyExpressionValue(value: unknown): boolean {
  if (value == null || value === "") return true;
  if (Array.isArray(value)) return value.length === 0;
  if (typeof value !== "object") return false;
  const prototype = Object.getPrototypeOf(value);
  return (prototype === Object.prototype || prototype === null) && Object.keys(value).length === 0;
}

export function getLiteralDefaultValue(descriptor: StudioActivityInputDescriptor): unknown {
  if (descriptor.defaultValue != null) return descriptor.defaultValue;
  if (describeDictionaryType(descriptor.typeName)) return {};
  if (describeCollectionType(descriptor.typeName)) return [];
  const typeName = descriptor.typeName.trim().toLowerCase();
  return typeName === "system.boolean" || typeName === "boolean" || typeName === "bool" ? false : "";
}

function isPrimitiveValue(value: unknown): boolean {
  return ["string", "number", "boolean", "bigint"].includes(typeof value);
}

function isStringType(typeName: string): boolean {
  const normalized = typeName.split(",", 1)[0]?.trim().toLowerCase();
  return normalized === "system.string" || normalized === "string";
}

export function writeInputValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor, value: unknown): ActivityNode {
  return {
    ...activity,
    [getInputPropertyName(descriptor)]: value
  };
}

export function getLiteralEditorValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  if (descriptor.isWrapped === false) return readInputValue(activity, descriptor);
  return readWrappedInput(activity, descriptor).expression.value;
}

export function readWrappedInputValue(value: unknown, descriptor: StudioActivityInputDescriptor): WrappedActivityInputValue {
  if (isWrappedInputValue(value)) {
    return {
      typeName: value.typeName || descriptor.typeName,
      expression: {
        type: value.expression.type || descriptor.defaultSyntax || "Literal",
        value: value.expression.value
      },
      ...(value.memoryReference ? { memoryReference: value.memoryReference } : {})
    };
  }

  return {
    typeName: descriptor.typeName,
    expression: {
      type: descriptor.defaultSyntax || "Literal",
      value: value ?? descriptor.defaultValue ?? ""
    }
  };
}

// --- collection (list) literal authoring ----------------------------------
//
// A Literal-typed input whose .NET type is a collection (List<T>, ICollection<T>, T[], …) is authored
// as a repeater of per-element editors instead of a single text box, so values are structured rather
// than jammed into one ambiguous delimited string. Collection element types come through `typeName`
// only when the backend includes generic arguments; when they don't, rows fall back to text editors.

const collectionTypeNames = new Set([
  "icollection", "ireadonlycollection", "ilist", "ireadonlylist", "list",
  "collection", "ienumerable", "iset", "hashset", "sortedset", "observablecollection", "array"
]);

const dictionaryTypeNames = new Set([
  "system.collections.generic.idictionary",
  "system.collections.generic.ireadonlydictionary",
  "system.collections.generic.dictionary",
  "system.collections.generic.sorteddictionary",
  "system.collections.concurrent.concurrentdictionary",
  "system.collections.immutable.immutabledictionary",
  "system.collections.immutable.immutablesorteddictionary"
]);

export interface DictionaryTypeInfo {
  valueTypeName: string;
}

/** Recognize only the supported CLR dictionary families when their key type is known to be string. */
export function describeDictionaryType(typeName: string): DictionaryTypeInfo | null {
  const core = stripAssemblyQualifier(typeName?.trim() ?? "");
  const backtick = core.indexOf("`");
  if (backtick < 0 || core.slice(backtick + 1).match(/^2(?:\[|$)/) == null) return null;
  const family = core.slice(0, backtick).toLowerCase();
  if (!dictionaryTypeNames.has(family)) return null;

  const arguments_ = parseRawGenericArguments(core.slice(backtick));
  if (arguments_.length !== 2 || !isClrStringType(arguments_[0])) return null;
  return { valueTypeName: arguments_[1] };
}

export interface CollectionTypeInfo {
  /** The element type name when it can be recovered from `typeName`, otherwise null (render text rows). */
  elementTypeName: string | null;
}

/** Recognize a collection-typed input from its .NET `typeName`; returns null for non-collection types. */
export function describeCollectionType(typeName: string): CollectionTypeInfo | null {
  const trimmed = typeName?.trim();
  if (!trimmed) return null;

  // Array form: "Namespace.Element[]" optionally followed by ", Assembly".
  const arrayMatch = /^([\w.+]+)\[\]/.exec(trimmed);
  if (arrayMatch) return { elementTypeName: arrayMatch[1] };

  // Generic form: "...Kind`N[...]" — the collection kind is the last segment before the arity backtick.
  const backtick = trimmed.indexOf("`");
  if (backtick < 0) return null;
  const base = trimmed.slice(0, backtick);
  const kind = (base.split(".").pop() ?? base).toLowerCase();
  if (!collectionTypeNames.has(kind)) return null;

  return { elementTypeName: extractFirstGenericArgument(trimmed.slice(backtick)) };
}

// Pull the first generic argument's type name from either the assembly-qualified "[[Type, Asm], …]"
// form or the short "[Type, …]" form; null when no arguments are present (e.g. a bare "ICollection`1").
function extractFirstGenericArgument(genericPart: string): string | null {
  const assemblyQualified = /\[\[([\w.+]+)/.exec(genericPart);
  if (assemblyQualified) return assemblyQualified[1];
  const shortForm = /\[([\w.+]+)/.exec(genericPart);
  return shortForm ? shortForm[1] : null;
}

/** Authors opt out of the repeater (back to a raw text/JSON box) via a hint or a uiSpecifications flag. */
export function isRepeaterOptOut(input: StudioActivityInputDescriptor): boolean {
  const hint = input.uiHint?.toLowerCase();
  if (hint === "json" || hint === "code") return true;
  return input.uiSpecifications?.repeater === false;
}

// Coerce a stored literal value into an array of elements without guessing delimiters of legacy strings.
// Deliberately parallel to `toValueArray` in the host's propertyEditors.tsx: the studio-sdk boundary is
// type-only (modules import no runtime SDK values), so sharing this would add load-time coupling for ~10
// lines. Keep the two in sync by hand rather than crossing that boundary.
export function toLiteralCollection(value: unknown): unknown[] {
  if (Array.isArray(value)) return value;
  if (value == null) return [];
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) return [];
    if (trimmed.startsWith("[")) {
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) return parsed;
      } catch {
        // Not JSON — preserve the original text as a single item rather than splitting on a guess.
      }
    }
    return [value];
  }
  return [value];
}

/** A new row's starting value, typed from the element type so booleans start unchecked rather than "". */
export function defaultCollectionItem(elementTypeName: string | null): unknown {
  const type = (elementTypeName ?? "").toLowerCase();
  return type === "system.boolean" || type === "boolean" || type === "bool" ? false : "";
}

/** A synthetic descriptor describing one element, so a repeater row resolves the right element editor. */
export function makeCollectionElementDescriptor(
  input: StudioActivityInputDescriptor,
  elementTypeName: string | null
): StudioActivityInputDescriptor {
  return {
    ...input,
    typeName: elementTypeName ?? "System.String",
    isWrapped: false,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}

/** Immutably move an item between positions; out-of-range or no-op moves return the original array. */
export function moveCollectionItem<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return items;
  const next = [...items];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

function isWrappedInputValue(value: unknown): value is WrappedActivityInputValue {
  if (!value || typeof value !== "object") return false;
  const record = value as Record<string, unknown>;
  const expression = record.expression;
  return typeof record.typeName === "string" &&
    !!expression &&
    typeof expression === "object" &&
    typeof (expression as Record<string, unknown>).type === "string";
}

// --- friendly type-name formatting (type badge) ----------------------------
//
// Render a .NET type name as a short, friendly label. Backend type names can be assembly-qualified and
// generic (e.g. "...ICollection`1[[System.String, System.Private.CoreLib, Version=8.0.0.0,
// Culture=neutral, PublicKeyToken=…]], System.Private.CoreLib, …"). A naive "text after the last dot"
// leaks fragments like "0, Culture=neutral, PublicKeyToken=…" into the UI, so parse the structure
// instead: drop assembly qualifiers, shorten namespaces, and rebuild generics/arrays as
// "ICollection<String>" / "Int32[]". Co-located with describeCollectionType, the other place that
// decodes generic type-name syntax in this module.
export function formatTypeName(typeName: string): string {
  return parseClrTypeName(typeName?.trim() ?? "") || typeName;
}

function parseClrTypeName(text: string): string {
  if (!text) return "";
  const core = stripAssemblyQualifier(text);
  if (!core) return "";

  // Trailing "[]" pairs are always array markers — a generic's brackets are never empty — so a greedy
  // match of them at the very end is safe even when the base type is itself generic (List`1[…][]).
  const array = /^(.*)((?:\[\])+)$/.exec(core);
  if (array) return `${parseClrTypeName(array[1])}${array[2]}`;

  const backtick = core.indexOf("`");
  if (backtick >= 0) {
    const name = simpleTypeName(core.slice(0, backtick));
    const args = parseGenericArguments(core.slice(backtick));
    return args.length > 0 ? `${name}<${args.join(", ")}>` : name;
  }

  return simpleTypeName(core);
}

// Cut the assembly-qualified tail: the first comma at bracket depth 0 (", System.Private.CoreLib, …").
function stripAssemblyQualifier(text: string): string {
  let depth = 0;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (char === "[") depth++;
    else if (char === "]") depth--;
    else if (char === "," && depth === 0) return text.slice(0, index).trim();
  }
  return text.trim();
}

// "System.Collections.Generic.ICollection" -> "ICollection"; also drops nested-type "+" prefixes.
function simpleTypeName(text: string): string {
  const segment = text.split(".").filter(Boolean).at(-1) ?? text;
  return segment.split("+").filter(Boolean).at(-1) ?? segment;
}

// Parse "`N[[Arg1, Asm…],[Arg2, Asm…]]" (or the short "`N[Arg1,Arg2]") into friendly argument labels.
function parseGenericArguments(genericPart: string): string[] {
  const open = genericPart.indexOf("[");
  if (open < 0) return [];
  const inner = innerBracketContent(genericPart, open);
  if (inner == null) return [];
  return splitTopLevel(inner)
    .map(arg => {
      const trimmed = arg.trim();
      const unwrapped = trimmed.startsWith("[") ? innerBracketContent(trimmed, 0) ?? trimmed : trimmed;
      return parseClrTypeName(unwrapped);
    })
    .filter(Boolean);
}

// Content between the bracket at `open` and its matching close, or null when unbalanced.
function innerBracketContent(text: string, open: number): string | null {
  let depth = 0;
  for (let index = open; index < text.length; index++) {
    if (text[index] === "[") depth++;
    else if (text[index] === "]" && --depth === 0) return text.slice(open + 1, index);
  }
  return null;
}

// Split on commas at bracket depth 0 (keeps assembly-qualified args like "[String, Asm]" intact).
function splitTopLevel(text: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let start = 0;
  for (let index = 0; index < text.length; index++) {
    const char = text[index];
    if (char === "[") depth++;
    else if (char === "]") depth--;
    else if (char === "," && depth === 0) {
      parts.push(text.slice(start, index));
      start = index + 1;
    }
  }
  parts.push(text.slice(start));
  return parts.map(part => part.trim()).filter(Boolean);
}

function parseRawGenericArguments(genericPart: string): string[] {
  const open = genericPart.indexOf("[");
  if (open < 0) return [];
  const inner = innerBracketContent(genericPart, open);
  if (inner == null) return [];
  return splitTopLevel(inner).map(argument => {
    const trimmed = argument.trim();
    const unwrapped = trimmed.startsWith("[") ? innerBracketContent(trimmed, 0) : trimmed;
    return stripAssemblyQualifier(unwrapped ?? trimmed);
  });
}

function isClrStringType(typeName: string): boolean {
  const normalized = stripAssemblyQualifier(typeName).trim().toLowerCase();
  return normalized === "system.string" || normalized === "string";
}
