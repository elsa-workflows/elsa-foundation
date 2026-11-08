import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioActivityPropertyEditorContribution, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import {
  collectContributionDraftValidation,
  contributionDraftValidationCollectors,
  contributionDraftValidationSignature
} from "./contributionDraftValidation";
import type { ActivityCatalogItem, ActivityNode, ValidationError } from "./workflowTypes";

export function useContributionDraftValidation(
  root: ActivityNode | null | undefined,
  propertyEditors: StudioActivityPropertyEditorContribution[],
  expressionEditors: StudioExpressionEditorContribution[],
  catalogByVersion?: Map<string, ActivityCatalogItem>
): { errors: ValidationError[]; pending: boolean } {
  const [errors, setErrors] = useState<ValidationError[]>([]);
  const [pending, setPending] = useState(false);
  const collectors = useMemo(
    () => contributionDraftValidationCollectors(propertyEditors, expressionEditors),
    [propertyEditors, expressionEditors]
  );
  const collectorsRef = useRef(collectors);
  collectorsRef.current = collectors;
  const catalogRef = useRef(catalogByVersion);
  catalogRef.current = catalogByVersion;
  const signature = useMemo(() => contributionDraftValidationSignature(root), [root]);
  const [foregroundGeneration, setForegroundGeneration] = useState(0);

  useEffect(() => {
    const bump = () => setForegroundGeneration(value => value + 1);
    const onVisibility = () => {
      if (document.visibilityState === "visible") bump();
    };
    window.addEventListener("focus", bump);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      window.removeEventListener("focus", bump);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, []);

  useEffect(() => {
    const current = collectorsRef.current;
    if (!root || current.length === 0) {
      setErrors([]);
      setPending(false);
      return;
    }
    let active = true;
    setPending(true);
    void collectContributionDraftValidation(root, current, catalogRef.current).then(result => {
      if (!active) return;
      setErrors(result);
      setPending(false);
    }, () => {
      if (!active) return;
      setPending(false);
    });
    return () => { active = false; };
  }, [root, signature, catalogByVersion, foregroundGeneration]);

  return { errors, pending };
}
