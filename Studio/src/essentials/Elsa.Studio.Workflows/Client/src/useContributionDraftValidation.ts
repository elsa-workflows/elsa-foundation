import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioActivityPropertyEditorContribution, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import {
  collectContributionDraftValidation,
  contributionDraftValidationCollectors,
  contributionDraftValidationSignature
} from "./contributionDraftValidation";
import type { ActivityNode, ValidationError } from "./workflowTypes";

export function useContributionDraftValidation(
  root: ActivityNode | null | undefined,
  propertyEditors: StudioActivityPropertyEditorContribution[],
  expressionEditors: StudioExpressionEditorContribution[]
): { errors: ValidationError[]; pending: boolean } {
  const [errors, setErrors] = useState<ValidationError[]>([]);
  const [pending, setPending] = useState(false);
  const collectors = useMemo(
    () => contributionDraftValidationCollectors(propertyEditors, expressionEditors),
    [propertyEditors, expressionEditors]
  );
  const collectorsRef = useRef(collectors);
  collectorsRef.current = collectors;
  const signature = useMemo(() => contributionDraftValidationSignature(root), [root]);

  useEffect(() => {
    const current = collectorsRef.current;
    if (!root || current.length === 0) {
      setErrors([]);
      setPending(false);
      return;
    }
    let active = true;
    setPending(true);
    void collectContributionDraftValidation(root, current).then(result => {
      if (!active) return;
      setErrors(result);
      setPending(false);
    }, () => {
      if (!active) return;
      setPending(false);
    });
    return () => { active = false; };
  }, [root, signature]);

  return { errors, pending };
}
