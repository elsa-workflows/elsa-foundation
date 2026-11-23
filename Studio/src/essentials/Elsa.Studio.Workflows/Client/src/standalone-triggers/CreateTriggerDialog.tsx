import { useRef, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioDialog, StudioDialogFooter, StudioResultState } from "@elsa-workflows/studio-ui";
import { operationId, type TriggerClient } from "./api";
import { ConfigurationForm, type ConfigurationDraft } from "./ConfigurationForm";
import { serializeTriggerPayload } from "./json";
import { ActionError, Loading, RequestError, useTriggerCompletion, useTriggerLoad, validationErrors } from "./shared";
import { resolveTriggerTypePresentation, sortTriggerTypes } from "./triggerTypePresentation";
import type { ActorView, TriggerTypeDescriptor, TriggerView } from "./types";

export function triggerTypeOptionValue(descriptor: Pick<TriggerTypeDescriptor, "typeKey" | "schemaVersion">) {
  return `${descriptor.typeKey}:${descriptor.schemaVersion}`;
}

export function CreateTriggerDialog({
  api,
  client,
  actor,
  onClose,
  onCreated
}: {
  api: ElsaStudioModuleApi;
  client: TriggerClient;
  actor: ActorView;
  onClose(): void;
  onCreated(trigger: TriggerView): void;
}) {
  const { result, reload } = useTriggerLoad(signal => client.types(signal), "types");
  const [typeKey, setTypeKey] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>();
  const request = useRef<{ fingerprint: string; id: string } | undefined>(undefined);
  const lock = useRef(false);
  const complete = useTriggerCompletion();

  const presentations = api.triggerTypePresentations?.list() ?? [];
  const editors = api.triggerConfigurationEditors?.list() ?? [];

  const sortedTypes = result.state === "loaded" ? sortTriggerTypes(result.value.items, presentations, editors) : [];

  const descriptor = sortedTypes.find(item => triggerTypeOptionValue(item) === typeKey);

  async function create(selected: TriggerTypeDescriptor, draft: ConfigurationDraft) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError(undefined);
    const fingerprint = serializeTriggerPayload({ typeKey: selected.typeKey, schemaVersion: selected.schemaVersion, ...draft });
    if (request.current?.fingerprint !== fingerprint) {
      request.current = { fingerprint, id: operationId() };
    }
    try {
      const saved = await client.create({
        ...draft,
        typeKey: selected.typeKey,
        schemaVersion: selected.schemaVersion,
        requestId: request.current.id
      });
      complete(() => {
        setBusy(false);
        api.notifications?.notify({ level: "success", title: `Created trigger ${saved.name}` });
        onCreated(saved);
      });
    } catch (caught) {
      complete(() => {
        setError(caught);
        setBusy(false);
      });
    } finally {
      lock.current = false;
    }
  }

  const canRenderPicker = actor.canManage && result.state === "loaded" && sortedTypes.length > 0;
  const showEmptyFooter = !canRenderPicker || !descriptor;

  return (
    <StudioDialog
      open
      title="Create trigger"
      description="Choose a trigger type and configure a live entry point independently of workflow graphs."
      className="wf-trigger-create-dialog"
      dismissDisabled={busy}
      dismissOnOutsideClick={false}
      onClose={onClose}
      footer={showEmptyFooter ? (
        <StudioDialogFooter
          onCancel={onClose}
          cancelLabel={canRenderPicker ? "Cancel" : "Close"}
          cancelDisabled={busy}
          confirmLabel="Create trigger"
          confirmType="button"
          confirmDisabled
          onConfirm={() => {}}
        />
      ) : undefined}
    >
      {!actor.canManage ? (
        <StudioResultState
          headingLevel={3}
          title="Creation is not allowed"
          description="You do not have permission to create triggers."
        />
      ) : result.state === "loading" ? (
        <Loading label="Loading trigger types" />
      ) : result.state === "error" ? (
        <RequestError error={result.error} retry={reload} />
      ) : !sortedTypes.length ? (
        <StudioResultState
          headingLevel={3}
          title="No trigger types installed"
          description="Install a source module before creating a trigger."
        />
      ) : (
        <>
          <fieldset className="wf-trigger-type-picker">
            <legend>Trigger type</legend>
            <div className="wf-trigger-type-cards" role="radiogroup" aria-label="Trigger type">
              {sortedTypes.map(type => {
                const value = triggerTypeOptionValue(type);
                const checked = typeKey === value;
                const presentation = resolveTriggerTypePresentation(type, presentations, editors);
                const Icon = presentation.icon;
                const disabled = !type.canCreate || busy;
                const accessibleLabel = type.canCreate ? presentation.displayName : `${presentation.displayName} (Unavailable)`;
                return (
                  <label
                    key={value}
                    className="wf-trigger-type-card"
                    data-checked={checked || undefined}
                    data-accent={presentation.accent}
                    data-type-key={type.typeKey}
                    data-schema-version={type.schemaVersion}
                    aria-disabled={disabled || undefined}
                  >
                    <input
                      type="radio"
                      name="wf-trigger-type"
                      value={value}
                      checked={checked}
                      disabled={disabled}
                      aria-label={accessibleLabel}
                      onChange={() => {
                        setTypeKey(value);
                        setError(undefined);
                      }}
                    />
                    <span className="wf-trigger-type-card-icon" aria-hidden="true">
                      <Icon size={18} />
                    </span>
                    <span className="wf-trigger-type-card-body">
                      <span className="wf-trigger-type-card-header">
                        <span className="wf-trigger-type-card-title">{presentation.displayName}</span>
                        {presentation.category ? <span className="wf-trigger-type-card-badge">{presentation.category}</span> : null}
                        {!type.canCreate ? <span className="wf-trigger-type-card-unavailable">Unavailable</span> : null}
                      </span>
                      {presentation.description ? (
                        <span className="wf-trigger-type-card-description">{presentation.description}</span>
                      ) : null}
                    </span>
                  </label>
                );
              })}
            </div>
          </fieldset>
          {descriptor ? (
            <div className="wf-trigger-create-config">
              <ConfigurationForm
                key={typeKey}
                descriptor={descriptor}
                editors={editors}
                serverErrors={validationErrors(error)}
                busy={busy}
                hideDescriptorDescription
                actionsClassName="studio-dialog-footer"
                afterFields={error ? <ActionError error={error} /> : undefined}
                onCancel={onClose}
                onSubmit={draft => void create(descriptor, draft)}
              />
            </div>
          ) : null}
        </>
      )}
    </StudioDialog>
  );
}
