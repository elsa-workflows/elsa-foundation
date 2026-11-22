import { useState } from "react";
import { GitBranch, Send } from "lucide-react";
import { StudioSplitButton } from "@elsa-workflows/studio-ui";

export type WorkflowPublishMode = "publish" | "review";

interface PublishActionButtonProps {
  busy: boolean;
  saving: boolean;
  unavailableReason?: string;
  onPublish(mode: WorkflowPublishMode): void;
}

const rememberedModeKey = "elsa-studio-workflow-publish-mode";
const modes: WorkflowPublishMode[] = ["review", "publish"];

function readRememberedMode(): WorkflowPublishMode {
  try {
    const stored = window.localStorage.getItem(rememberedModeKey);
    return stored === "publish" || stored === "review" ? stored : "review";
  } catch {
    return "review";
  }
}

function rememberMode(mode: WorkflowPublishMode) {
  try {
    window.localStorage.setItem(rememberedModeKey, mode);
  } catch {
    // The current choice still works for this editor session when storage is unavailable.
  }
}

export function PublishActionButton({ busy: pending, saving, unavailableReason, onPublish }: PublishActionButtonProps) {
  const busy = pending || !!unavailableReason;
  const [mode, setMode] = useState<WorkflowPublishMode>(readRememberedMode);
  const isReview = mode === "review";
  const queuedTitle = unavailableReason ?? (saving ? "Finishing the current save; this action will start once it settles." : undefined);

  return (
    <StudioSplitButton ariaLabel="Publish options" menuLabel="Publish action" triggerTitle={unavailableReason ?? "Publish options"}
      popoverClassName="wf-editor-actions-popover" maxHeight={180} disabled={busy}
      primaryAction={{
        groupLabel: "Workflow publication actions", title: queuedTitle,
        label: isReview ? <><GitBranch size={15} aria-hidden="true" />Review &amp; publish</>
          : <><Send size={15} aria-hidden="true" />Publish</>,
        onSelect: () => onPublish(mode)
      }}
      items={modes.map(option => ({
        id: option, label: option === "review" ? "Review & publish" : "Publish", title: queuedTitle,
        selection: { type: "radio", checked: option === mode },
        onSelect() {
          setMode(option);
          rememberMode(option);
          onPublish(option);
        }
      }))} />
  );
}
