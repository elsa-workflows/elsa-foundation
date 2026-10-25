import { AdvancedSection, CopyableIdentifier } from "@elsa-workflows/studio-ui";
import "./workflowListIdentity.css";

export interface WorkflowListIdentifier {
  label: string;
  value: string;
}

export function WorkflowListIdentity({
  primaryLabel,
  primaryValue,
  secondaryIdentifiers = [],
  primaryClassName,
  secondaryClassName,
  detailsClassName,
  detailsAriaLabel
}: {
  primaryLabel: string;
  primaryValue: string;
  secondaryIdentifiers?: readonly WorkflowListIdentifier[];
  primaryClassName?: string;
  secondaryClassName?: string;
  detailsClassName?: string;
  detailsAriaLabel?: string;
}) {
  return <>
    <div className={["wf-list-identity-primary", primaryClassName].filter(Boolean).join(" ")}>
      <CopyableIdentifier label={primaryLabel} value={primaryValue} />
    </div>
    {secondaryIdentifiers.length ? (
      <div className={["wf-list-identity-secondary", secondaryClassName].filter(Boolean).join(" ")}>
        <AdvancedSection label="More identifiers" summary={`${secondaryIdentifiers.length} exact IDs`}>
          <div className={["wf-list-identity-details", detailsClassName].filter(Boolean).join(" ")} aria-label={detailsAriaLabel}>
            {secondaryIdentifiers.map(identifier => <CopyableIdentifier key={identifier.label} label={identifier.label} value={identifier.value} />)}
          </div>
        </AdvancedSection>
      </div>
    ) : null}
  </>;
}
