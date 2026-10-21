import "./PublishActionButton.css";
import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { Check, ChevronDown, GitBranch, Send } from "lucide-react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";

export type WorkflowPublishMode = "publish" | "review";

interface PublishActionButtonProps {
  busy: boolean;
  saving: boolean;
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

export function PublishActionButton({ busy, saving, onPublish }: PublishActionButtonProps) {
  const [mode, setMode] = useState<WorkflowPublishMode>(readRememberedMode);
  const [open, setOpen] = useState(false);
  const primaryRef = useRef<HTMLButtonElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const initialFocusIndexRef = useRef(0);
  const previousBusyRef = useRef(busy);
  const focusReturnTargetRef = useRef<"primary" | "options" | null>(null);
  const menuId = useId();

  const closeMenu = (restoreFocus = true) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  };

  useEffect(() => {
    if (!open) return;
    const frame = requestAnimationFrame(() => optionRefs.current[initialFocusIndexRef.current]?.focus());
    return () => cancelAnimationFrame(frame);
  }, [open]);

  useLayoutEffect(() => {
    const wasBusy = previousBusyRef.current;
    previousBusyRef.current = busy;
    if (wasBusy === busy) return;

    if (busy) {
      if (open) {
        focusReturnTargetRef.current ??= "options";
        setOpen(false);
      }
      return;
    }

    const target = focusReturnTargetRef.current;
    focusReturnTargetRef.current = null;
    if (!target) return;
    const frame = requestAnimationFrame(() => {
      const activeElement = document.activeElement;
      if (activeElement !== document.body && activeElement?.isConnected) return;
      const destination = target === "primary" ? primaryRef.current : triggerRef.current;
      if (destination?.isConnected && !destination.disabled) destination.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [busy, open]);

  const openMenu = (index: number) => {
    initialFocusIndexRef.current = index;
    setOpen(true);
  };

  const selectMode = (nextMode: WorkflowPublishMode, command: HTMLButtonElement) => {
    focusReturnTargetRef.current = document.activeElement === command ? "options" : null;
    setMode(nextMode);
    rememberMode(nextMode);
    closeMenu();
    onPublish(nextMode);
  };

  const submitFromPrimary = (nextMode: WorkflowPublishMode, command: HTMLButtonElement) => {
    focusReturnTargetRef.current = document.activeElement === command ? "primary" : null;
    onPublish(nextMode);
  };

  const handleMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape" || event.key === "Tab") {
      if (event.key === "Escape") event.preventDefault();
      closeMenu();
      return;
    }

    const options = optionRefs.current.filter((option): option is HTMLButtonElement => !!option && !option.disabled);
    if (options.length === 0) return;
    const currentIndex = options.indexOf(document.activeElement as HTMLButtonElement);
    if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
      event.preventDefault();
      const nextIndex = event.key === "Home" ? 0 : event.key === "End" ? options.length - 1
        : (currentIndex + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length;
      options[nextIndex]?.focus();
      return;
    }

    if (event.key === "Enter" || event.key === " ") {
      const selectedOption = optionRefs.current.indexOf(document.activeElement as HTMLButtonElement);
      const selectedMode = modes[selectedOption];
      if (selectedMode) {
        event.preventDefault();
        selectMode(selectedMode, document.activeElement as HTMLButtonElement);
      }
    }
  };

  const isReview = mode === "review";
  const queuedTitle = saving ? "Finishing the current save; this action will start once it settles." : undefined;

  return (
    <div className="wf-publish-actions" role="group" aria-label="Workflow publication actions">
      <button
        ref={primaryRef}
        type="button"
        className="wf-publish-primary"
        disabled={busy}
        title={queuedTitle}
        onClick={event => submitFromPrimary(mode, event.currentTarget)}
      >
        {isReview ? <GitBranch size={15} aria-hidden="true" /> : <Send size={15} aria-hidden="true" />}
        {isReview ? "Review & publish" : "Publish"}
      </button>
      <button
        ref={triggerRef}
        type="button"
        className="wf-publish-menu-trigger"
        aria-label="Publish options"
        title="Publish options"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        disabled={busy}
        onClick={() => open ? closeMenu(false) : openMenu(modes.indexOf(mode))}
        onKeyDown={event => {
          if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
          event.preventDefault();
          openMenu(event.key === "ArrowDown" ? 0 : modes.length - 1);
        }}
      >
        <ChevronDown size={15} aria-hidden="true" />
      </button>
      <AnchoredPopover
        anchorRef={triggerRef}
        open={open}
        className="wf-editor-actions-popover"
        minWidth={176}
        maxHeight={180}
        onDismiss={() => closeMenu(false)}
      >
        <div id={menuId} role="menu" aria-label="Publish action" onKeyDown={handleMenuKeyDown}>
          {modes.map((optionMode, index) => {
            const optionLabel = optionMode === "review" ? "Review & publish" : "Publish";
            return (
              <button
                ref={element => { optionRefs.current[index] = element; }}
                key={optionMode}
                type="button"
                role="menuitemradio"
                aria-checked={mode === optionMode}
                tabIndex={-1}
                disabled={busy}
                title={queuedTitle}
                onClick={event => selectMode(optionMode, event.currentTarget)}
              >
                <Check size={15} aria-hidden="true" style={{ visibility: mode === optionMode ? "visible" : "hidden" }} />
                {optionLabel}
              </button>
            );
          })}
        </div>
      </AnchoredPopover>
    </div>
  );
}
