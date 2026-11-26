import { useLayoutEffect, useRef, useState, type RefObject } from "react";
import { GitBranch, Send } from "lucide-react";
import { StudioSplitButton } from "@elsa-workflows/studio-ui";

export type WorkflowPublishMode = "publish" | "review";

interface PublishActionButtonProps {
  busy: boolean;
  saving: boolean;
  unavailableReason?: string;
  ariaLabel?: string;
  preferenceKey?: string;
  primaryButtonRef?: RefObject<HTMLButtonElement | null>;
  onPublish(mode: WorkflowPublishMode): void;
}

const rememberedModeKey = "elsa-studio-workflow-publish-mode";
const modes: WorkflowPublishMode[] = ["review", "publish"];

function readRememberedMode(key: string): WorkflowPublishMode {
  try {
    const stored = window.localStorage.getItem(key);
    return stored === "publish" || stored === "review" ? stored : "review";
  } catch {
    return "review";
  }
}

function rememberMode(key: string, mode: WorkflowPublishMode) {
  try {
    window.localStorage.setItem(key, mode);
  } catch {
    // The current choice still works for this editor session when storage is unavailable.
  }
}

export function PublishActionButton({
  busy: pending,
  saving,
  unavailableReason,
  ariaLabel = "Workflow publication actions",
  preferenceKey = rememberedModeKey,
  primaryButtonRef,
  onPublish
}: PublishActionButtonProps) {
  const busy = pending || !!unavailableReason;
  const [mode, setMode] = useState<WorkflowPublishMode>(() => readRememberedMode(preferenceKey));
  const hostRef = useRef<HTMLSpanElement>(null);
  const isReview = mode === "review";
  const queuedTitle = unavailableReason ?? (saving ? "Finishing the current save; this action will start once it settles." : undefined);

  // The shared split button owns its primary control; expose it to callers that restore focus to it.
  useLayoutEffect(() => {
    if (!primaryButtonRef) return;
    primaryButtonRef.current = hostRef.current?.querySelector<HTMLButtonElement>(".studio-split-button-primary") ?? null;
  });

  const splitButton = (
    <StudioSplitButton ariaLabel="Publish options" menuLabel="Publish action" triggerTitle={unavailableReason ?? "Publish options"}
      popoverClassName="wf-editor-actions-popover" maxHeight={180} disabled={busy}
      primaryAction={{
        groupLabel: ariaLabel, title: queuedTitle,
        label: isReview ? <><GitBranch size={15} aria-hidden="true" />Review &amp; publish</>
          : <><Send size={15} aria-hidden="true" />Publish</>,
        onSelect: () => onPublish(mode)
      }}
      items={modes.map(option => ({
        id: option, label: option === "review" ? "Review & publish" : "Publish", title: queuedTitle,
        selection: { type: "radio", checked: option === mode },
        onSelect() {
          setMode(option);
          rememberMode(preferenceKey, option);
          onPublish(option);
        }
      }))} />
  );

  return primaryButtonRef ? <span ref={hostRef} style={{ display: "contents" }}>{splitButton}</span> : splitButton;
}
