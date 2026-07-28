import type {
  StudioCodeToolingAuthoringContext,
  StudioCodeToolingLanguageProjection,
  StudioCodeToolingSymbol
} from "@elsa-workflows/studio-code-editor";

/** JavaScript owns the spelling of workflow facts; the shared editor only traverses projected symbols. */
export const javaScriptToolingProjection: StudioCodeToolingLanguageProjection = {
  projectContext: context => {
    const contextual = contextualSymbols(context);
    const contextualKeys = symbolKeys(contextual);
    const engineRoots = (context?.rootSymbols ?? []).filter(symbol => !contextualKeys.has(symbol.id) && !contextualKeys.has(symbol.name));
    const getterSymbols = contextual.map(symbol => ({
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
      [{
        id: "javascript:args",
        name: "args",
        kind: "variable",
        documentation: "Workflow inputs and visible activity outputs.",
        children: mergeSymbols(context?.workflowInputs ?? [], context?.visibleActivityOutputs ?? [])
      }],
      [{
        id: "javascript:variables",
        name: "variables",
        kind: "variable",
        documentation: "Visible workflow variables.",
        children: [...(context?.visibleVariables ?? [])]
      }],
      [{
        id: "javascript:getVariable",
        name: "getVariable",
        kind: "function",
        documentation: "Gets a visible workflow variable by name.",
        signatures: [{ label: "getVariable(name)" }]
      }],
      getterSymbols
    );
  },
  projectCatalog: (symbols, context) => {
    const contextualKeys = symbolKeys(contextualSymbols(context));
    return symbols.filter(symbol => !contextualKeys.has(symbol.id) && !contextualKeys.has(symbol.name));
  },
  memberPathAt: (source, position, includeCurrentWord) => {
    const prefix = source.slice(0, Math.min(Math.max(0, position), source.length));
    const match = prefix.match(/([\w$]+(?:\(\))?(?:\.[\w$]*)+)$/);
    if (!match) return undefined;
    const segments = match[1]!.split(".").map(segment => segment.endsWith("()") ? segment.slice(0, -2) : segment);
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
  return `get${name.charAt(0).toUpperCase()}${name.slice(1)}`;
}

function mergeSymbols(...groups: readonly (readonly StudioCodeToolingSymbol[])[]) {
  const symbols = new Map<string, StudioCodeToolingSymbol>();
  for (const symbol of groups.flat()) symbols.set(symbol.id ?? symbol.name, symbol);
  return [...symbols.values()];
}
