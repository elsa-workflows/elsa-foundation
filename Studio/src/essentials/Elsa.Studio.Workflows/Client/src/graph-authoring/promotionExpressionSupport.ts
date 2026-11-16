import type { StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";

export interface PromotionExpressionSupport {
  revision: string;
  allows(expression: Readonly<Record<string, unknown>>): boolean;
}

const identities = new WeakMap<object, number>();
let nextIdentity = 0;
const identity = (value: object) => {
  let id = identities.get(value);
  if (id === undefined) { id = ++nextIdentity; identities.set(value, id); }
  return id;
};

/** The language owner proves closed dependencies. Workflows never parses or rewrites script source. */
export function composePromotionExpressionSupport(editors: readonly StudioExpressionEditorContribution[]): PromotionExpressionSupport {
  const owners = editors.flatMap(editor => editor.reusableBoundaryPreservation ? [{ editor, proof: editor.reusableBoundaryPreservation }] : []);
  return {
    revision: JSON.stringify(owners.map(({ editor, proof }) => [editor.id, identity(editor), proof.language, proof.revision, identity(proof.allows)]).sort()),
    allows(expression) {
      if (typeof expression.language !== "string") return false;
      const matches = owners.filter(({ proof }) => proof.language.toLowerCase() === (expression.language as string).toLowerCase());
      if (matches.length !== 1 || !matches[0].proof.revision) return false;
      try { return matches[0].proof.allows(expression) === true; }
      catch { return false; }
    }
  };
}
