import type {
  StudioCodeToolingAuthoringContext,
  StudioCodeToolingLanguageProjection,
  StudioCodeToolingSymbol
} from "@elsa-workflows/studio-code-editor";

const identifierPattern = String.raw`[\p{L}_$][\p{L}\p{Nd}_$]*`;
const memberPathPattern = new RegExp(
  `(${identifierPattern}(?:\\(\\))?(?:\\.(?:${identifierPattern})?)+)$`,
  "u"
);

/** JavaScript owns the spelling of workflow facts; the shared editor only traverses projected symbols. */
export const javaScriptToolingProjection: StudioCodeToolingLanguageProjection = {
  projectContext: context => {
    const contextual = contextualSymbols(context);
    const contextualKeys = symbolKeys(contextual);
    const engineRoots = (context?.rootSymbols ?? []).filter(symbol => !contextualKeys.has(symbol.id) && !contextualKeys.has(symbol.name));
    const values = mergeSymbols(context?.workflowInputs ?? [], context?.visibleActivityOutputs ?? []);
    const variables = [...(context?.visibleVariables ?? [])];
    const getterSymbols = variables.filter(symbol => isIdentifier(symbol.name)).map(symbol => ({
      id: `javascript:getter:${symbol.id ?? symbol.name}`,
      name: getterName(symbol.name),
      kind: "function",
      documentation: symbol.documentation,
      shapeId: symbol.shapeId,
      signatures: [{ label: `${getterName(symbol.name)}()` }]
    }));
    return mergeSymbols(
      context?.target ? [context.target] : [],
      engineRoots,
      values.length > 0 ? [{
        id: "javascript:args",
        name: "args",
        kind: "variable",
        documentation: "Workflow inputs and visible activity outputs.",
        children: values
      }] : [],
      variables.length > 0 ? [{
        id: "javascript:variables",
        name: "variables",
        kind: "variable",
        documentation: "Visible workflow variables.",
        children: variables
      }, {
        id: "javascript:getVariable",
        name: "getVariable",
        kind: "function",
        documentation: "Gets a visible workflow variable by name.",
        signatures: [{ label: "getVariable(name)" }]
      }, ...getterSymbols] : []
    );
  },
  projectCatalog: (symbols, context) => {
    const contextualKeys = symbolKeys(contextualSymbols(context));
    return symbols.filter(symbol => !contextualKeys.has(symbol.id) && !contextualKeys.has(symbol.name));
  },
  memberPathAt: (source, position, includeCurrentWord) => {
    const prefix = source.slice(0, Math.min(Math.max(0, position), source.length));
    const match = prefix.match(memberPathPattern);
    if (!match) return undefined;
    const segments = match[1]!.split(".").map(segment => segment.endsWith("()") ? segment.slice(0, -2) : segment);
    if (segments.some(segment => segment !== "" && !isIdentifier(segment))) return undefined;
    if (!includeCurrentWord && segments.at(-1) !== "") return undefined;
    return segments;
  }
};

function contextualSymbols(context?: StudioCodeToolingAuthoringContext) {
  return mergeSymbols(
    context?.workflowInputs ?? [],
    context?.visibleVariables ?? [],
    context?.visibleActivityOutputs ?? []
  );
}

function symbolKeys(symbols: readonly StudioCodeToolingSymbol[]) {
  return new Set(symbols.flatMap(symbol => [symbol.id, symbol.name].filter(Boolean)));
}

function getterName(name: string) {
  const first = name.charAt(0);
  const upper = first.toUpperCase();
  return `get${upper.length === 1 ? upper : first}${name.slice(1)}`;
}

function isIdentifier(value: string) {
  return !/[\uD800-\uDFFF]/.test(value) &&
    /^[\p{L}_$][\p{L}\p{Nd}_$]*$/u.test(value);
}

function mergeSymbols(...groups: readonly (readonly StudioCodeToolingSymbol[])[]) {
  const symbols = new Map<string, StudioCodeToolingSymbol>();
  for (const symbol of groups.flat()) symbols.set(symbol.id ?? symbol.name, symbol);
  return [...symbols.values()];
}
