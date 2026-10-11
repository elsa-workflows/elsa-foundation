import { autocompletion, closeCompletion, completionStatus, type CompletionSource } from "@codemirror/autocomplete";
import { StateEffect, StateField, type Extension } from "@codemirror/state";
import { EditorView, hoverTooltip, panels, showPanel, ViewPlugin, type ViewUpdate } from "@codemirror/view";
import { sanitizeStudioCodeMarkdown } from "../StudioCodeDocumentation";
import type {
  StudioCodeCompletion,
  StudioCodeCompletionProvider,
  StudioCodeDocument,
  StudioCodeHover,
  StudioCodeHoverProvider,
  StudioCodeSignature,
  StudioCodeSignatureProvider
} from "../types";

export interface StudioCodeIntelligenceOptions {
  document: StudioCodeDocument;
  completions?: StudioCodeCompletion[];
  completionProvider?: StudioCodeCompletionProvider;
  hoverProvider?: StudioCodeHoverProvider;
  signatureProvider?: StudioCodeSignatureProvider;
}

const completionRequestSets = new WeakMap<EditorView, Set<AbortController>>();

/** Translates the public, engine-neutral intelligence contract into CodeMirror extensions. */
export function createCodeMirrorCodeIntelligenceExtensions(options: StudioCodeIntelligenceOptions): Extension[] {
  const completionRequests = new Set<AbortController>();
  const completionSource = createCompletionSource(options, completionRequests);
  const extensions: Extension[] = [
    autocompletion({ override: [completionSource] }),
    ViewPlugin.fromClass(class {
      constructor(private readonly view: EditorView) {
        completionRequestSets.set(view, completionRequests);
      }

      destroy() {
        for (const controller of completionRequests) controller.abort();
        completionRequests.clear();
        completionRequestSets.delete(this.view);
      }
    })
  ];

  if (options.hoverProvider) {
    extensions.push(hoverTooltip((view, position) => {
      const controller = new AbortController();
      trackPointerHoverRequest(view, controller);
      const source = view.state.doc.toString();
      return Promise.resolve(options.hoverProvider!({ ...options.document, value: view.state.doc.toString() }, position, controller.signal))
        .then(hover => {
          if (!hover || controller.signal.aborted || view.state.doc.toString() !== source) return null;
          const dom = document.createElement("div");
          dom.className = "studio-code-editor-hover";
          dom.textContent = sanitizeStudioCodeMarkdown(hover.documentation.markdown);
          return {
            pos: hover.range?.from ?? position,
            end: hover.range?.to ?? position,
            above: true,
            create: () => ({ dom })
          };
        })
        .catch(() => null)
        .finally(() => untrackPointerHoverRequest(view, controller));
    }));
    extensions.push(...createKeyboardHoverExtensions());
  }

  if (options.signatureProvider) {
    extensions.push(...createSignatureHelpExtensions(options));
  }

  return extensions;
}

const setKeyboardHover = StateEffect.define<StudioCodeHover | null>();
const keyboardHoverField = StateField.define<StudioCodeHover | null>({
  create: () => null,
  update(value, transaction) {
    for (const effect of transaction.effects) if (effect.is(setKeyboardHover)) return effect.value;
    return transaction.docChanged || transaction.selection ? null : value;
  },
  provide: field => showPanel.from(field, hover => hover ? createKeyboardHoverPanel(hover) : null)
});
const keyboardHoverRequests = new WeakMap<EditorView, AbortController>();
const pointerHoverRequests = new WeakMap<EditorView, Set<AbortController>>();

function trackPointerHoverRequest(view: EditorView, controller: AbortController) {
  const requests = pointerHoverRequests.get(view) ?? new Set<AbortController>();
  requests.add(controller);
  pointerHoverRequests.set(view, requests);
}

function untrackPointerHoverRequest(view: EditorView, controller: AbortController) {
  const requests = pointerHoverRequests.get(view);
  if (!requests) return;
  requests.delete(controller);
  if (requests.size === 0) pointerHoverRequests.delete(view);
}

function createKeyboardHoverExtensions(): Extension[] {
  return [
    panels(),
    keyboardHoverField
  ];
}

export async function requestCodeMirrorKeyboardHover(
  view: EditorView,
  options: StudioCodeIntelligenceOptions
) {
  keyboardHoverRequests.get(view)?.abort();
  const controller = new AbortController();
  keyboardHoverRequests.set(view, controller);
  const source = view.state.doc.toString();
  try {
    const hover = await options.hoverProvider!(
      { ...options.document, value: source },
      view.state.selection.main.head,
      controller.signal
    );
    if (controller.signal.aborted || view.state.doc.toString() !== source) return;
    view.dispatch({ effects: setKeyboardHover.of(hover) });
  } catch {
    if (!controller.signal.aborted) {
      try {
        view.dispatch({ effects: setKeyboardHover.of(null) });
      } catch {
        // The editor was destroyed while the request was in flight.
      }
    }
  } finally {
    if (keyboardHoverRequests.get(view) === controller) keyboardHoverRequests.delete(view);
  }
}

function createKeyboardHoverPanel(hover: StudioCodeHover) {
  return () => {
    const dom = document.createElement("div");
    dom.className = "studio-code-editor-hover studio-code-editor-keyboard-hover";
    dom.setAttribute("role", "status");
    dom.setAttribute("aria-live", "polite");
    dom.setAttribute("aria-label", "Hover information");
    dom.textContent = sanitizeStudioCodeMarkdown(hover.documentation.markdown);
    return { dom, top: false };
  };
}

const setSignature = StateEffect.define<StudioCodeSignature | null>();
const signatureHelpRequests = new WeakMap<EditorView, AbortController>();
const signatureField = StateField.define<StudioCodeSignature | null>({
  create: () => null,
  update(value, transaction) {
    for (const effect of transaction.effects) if (effect.is(setSignature)) return effect.value;
    return transaction.docChanged ? null : value;
  },
  provide: field => showPanel.from(field, signature => signature ? createSignaturePanel(signature) : null)
});

function createSignatureHelpExtensions(options: StudioCodeIntelligenceOptions): Extension[] {
  return [
    panels(),
    signatureField,
    ViewPlugin.fromClass(class {
      constructor(private readonly view: EditorView) {
        void this.refresh(view);
      }

      update(update: ViewUpdate) {
        if (update.docChanged || update.selectionSet) void this.refresh(update.view);
      }

      destroy() {
        const controller = signatureHelpRequests.get(this.view);
        controller?.abort();
        signatureHelpRequests.delete(this.view);
      }

      private async refresh(view: EditorView) {
        signatureHelpRequests.get(view)?.abort();
        const controller = new AbortController();
        signatureHelpRequests.set(view, controller);
        try {
          const signature = await options.signatureProvider!(
            { ...options.document, value: view.state.doc.toString() },
            view.state.selection.main.head,
            controller.signal
          );
          if (!controller.signal.aborted) view.dispatch({ effects: setSignature.of(signature) });
        } catch {
          if (!controller.signal.aborted) view.dispatch({ effects: setSignature.of(null) });
        } finally {
          if (signatureHelpRequests.get(view) === controller) signatureHelpRequests.delete(view);
        }
      }
    })
  ];
}

function createSignaturePanel(signature: StudioCodeSignature) {
  return () => {
    const dom = document.createElement("div");
    dom.className = "studio-code-editor-signature";
    dom.setAttribute("role", "status");
    dom.setAttribute("aria-live", "polite");
    dom.textContent = signature.documentation
      ? `${signature.label} — ${sanitizeStudioCodeMarkdown(signature.documentation.markdown)}`
      : signature.label;
    return { dom, top: false };
  };
}

/** Dismisses completion, keyboard-hover, or signature UI before Escape arms Tab-focus escape. */
export function dismissCodeMirrorIntelligence(view: EditorView) {
  const completion = completionStatus(view.state);
  if (completion) closeCompletion(view);
  // A pending query has no visible UI to dismiss. Cancel it so it cannot reopen after Escape,
  // but still let this Escape arm keyboard focus exit.
  let dismissed = completion === "active";
  const keyboardHoverRequest = keyboardHoverRequests.get(view);
  if (keyboardHoverRequest) {
    keyboardHoverRequest.abort();
    keyboardHoverRequests.delete(view);
    dismissed = true;
  }
  const signatureHelpRequest = signatureHelpRequests.get(view);
  if (signatureHelpRequest) {
    signatureHelpRequest.abort();
    signatureHelpRequests.delete(view);
  }
  const effects: StateEffect<unknown>[] = [];
  if (view.state.field(keyboardHoverField, false)) {
    effects.push(setKeyboardHover.of(null));
    dismissed = true;
  }
  if (view.state.field(signatureField, false)) {
    effects.push(setSignature.of(null));
    dismissed = true;
  }
  if (effects.length > 0) view.dispatch({ effects });
  return dismissed;
}

/** Cancels every source-bearing request owned by a view before it is destroyed or revoked. */
export function cancelCodeMirrorIntelligence(view: EditorView) {
  for (const controller of completionRequestSets.get(view) ?? []) controller.abort();
  completionRequestSets.delete(view);
  if (completionStatus(view.state)) closeCompletion(view);
  keyboardHoverRequests.get(view)?.abort();
  keyboardHoverRequests.delete(view);
  signatureHelpRequests.get(view)?.abort();
  signatureHelpRequests.delete(view);
  for (const controller of pointerHoverRequests.get(view) ?? []) controller.abort();
  pointerHoverRequests.delete(view);
}

function createCompletionSource(
  options: StudioCodeIntelligenceOptions,
  completionRequests: Set<AbortController>
): CompletionSource {
  return context => {
    const word = context.matchBefore(/[\w$]*/);
    if (!word && !context.explicit) return null;
    if (!options.completionProvider) return toCompletionResult(options.completions, word?.from ?? context.pos);

    const controller = new AbortController();
    completionRequests.add(controller);
    context.addEventListener("abort", () => controller.abort(), { onDocChange: true });
    const document = { ...options.document, value: context.state.doc.toString() };
    return Promise.resolve(options.completionProvider({
      document,
      position: context.pos,
      explicit: context.explicit,
      signal: controller.signal
    }))
      .catch(() => options.completions)
      .then(supplied => controller.signal.aborted || context.aborted
        ? null
        : toCompletionResult(supplied, word?.from ?? context.pos))
      .finally(() => completionRequests.delete(controller));
  };
}

function toCompletionResult(supplied: StudioCodeCompletion[] | null | undefined, from: number) {
  if (!supplied?.length) return null;
  return {
    from,
    options: supplied.map(toCodeMirrorCompletion),
    validFor: /^[\w$]*$/
  };
}

function toCodeMirrorCompletion(completion: StudioCodeCompletion) {
  return {
    label: completion.label,
    detail: completion.detail,
    type: completion.kind,
    apply: completion.apply,
    boost: completion.boost,
    info: completion.documentation
      ? () => {
          const dom = document.createElement("div");
          dom.className = "studio-code-editor-completion-documentation";
          dom.textContent = sanitizeStudioCodeMarkdown(completion.documentation!.markdown);
          return dom;
        }
      : undefined
  };
}
