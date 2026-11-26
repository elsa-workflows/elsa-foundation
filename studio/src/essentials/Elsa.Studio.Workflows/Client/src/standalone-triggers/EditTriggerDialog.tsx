import { useState } from "react";
import type { ElsaStudioModuleApi, StudioTriggerConfigurationEditorContribution } from "@elsa-workflows/studio-sdk";
import { StudioDialog, StudioStepper } from "@elsa-workflows/studio-ui";
import type { TriggerClient } from "./api";
import { ChangeReview } from "./ChangeReview";
import { ConfigurationForm, type ConfigurationDraft } from "./ConfigurationForm";
import type { ResolvedTriggerTypePresentation } from "./triggerTypePresentation";
import type { TriggerTypeDescriptor, TriggerView } from "./types";

const editSteps = [
  { id: "configure", label: "Configure" },
  { id: "review", label: "Review & apply" }
] as const;

export function EditTriggerDialog({
  api,
  client,
  trigger,
  descriptor,
  editors,
  typePresentation,
  onClose,
  onApplied
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  trigger: TriggerView;
  descriptor: TriggerTypeDescriptor;
  editors: readonly StudioTriggerConfigurationEditorContribution[];
  typePresentation?: ResolvedTriggerTypePresentation;
  onClose(): void;
  onApplied(): void;
}) {
  const [draft, setDraft] = useState<ConfigurationDraft>();
  const [serverErrors, setServerErrors] = useState<Record<string, string[]>>({});
  const [reviewing, setReviewing] = useState(false);

  const TypeIcon = typePresentation?.icon;

  return (
    <StudioDialog
      open
      title="Edit trigger configuration"
      description="Update this live trigger's settings and inspect the projected impact before applying."
      className="wf-trigger-create-dialog"
      dismissOnOutsideClick={false}
      onClose={onClose}
    >
      <div className="wf-trigger-dialog-header-bar">
        <StudioStepper
          label="Edit configuration steps"
          steps={editSteps}
          current={reviewing ? "review" : "configure"}
        />
        {typePresentation && TypeIcon ? (
          <span className="wf-trigger-type-badge" data-accent={typePresentation.accent}>
            <TypeIcon size={14} aria-hidden />
            <span>{typePresentation.displayName}</span>
          </span>
        ) : null}
      </div>
      {reviewing && draft ? (
        <ChangeReview
          api={api}
          client={client}
          trigger={trigger}
          command={{ kind: "SaveConfiguration", ...draft }}
          embedded
          onCancel={errors => {
            setServerErrors(errors ?? {});
            setReviewing(false);
          }}
          onApplied={onApplied}
        />
      ) : (
        <ConfigurationForm
          descriptor={descriptor}
          trigger={trigger}
          initialDraft={draft}
          serverErrors={serverErrors}
          editors={editors}
          hideDescriptorDescription
          actionsClassName="studio-dialog-footer"
          onCancel={onClose}
          onSubmit={nextDraft => {
            setDraft(nextDraft);
            setServerErrors({});
            setReviewing(true);
          }}
        />
      )}
    </StudioDialog>
  );
}
