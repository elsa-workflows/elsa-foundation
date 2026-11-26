# Studio Expression Tooling Contract

This contract is public to Studio modules but independent of CodeMirror, Monaco, or any other editor engine.

## Expression editor context

`StudioExpressionEditorContext` gains:

- `document`: stable `ExpressionDocument` identity and current source version;
- `authoringContext`: current language-neutral context or a typed loading/error state;
- `tooling`: cancellation-aware context, symbol, completion, hover, and validation operations;
- `surface`: remains `inline` or `expanded`;
- existing descriptor, activity, expression descriptor, and read-only fields remain compatible.

Every new field is additive and may be absent for older hosts. A language module must retain generic/local editing when host tooling is absent.

## Tooling operations

```text
getAuthoringContext(document, workflowState, signal)
getCatalog(document, authoringContext, query?, cursor?, signal)
getCompletions(document, authoringContext, position, signal)
getHover(document, authoringContext, position, signal)
validate(document, authoringContext, signal)
```

Every asynchronous result includes:

- tooling contract version;
- Expression Type and backend module version;
- document/source/context/catalog versions where applicable;
- one explicit state: ready, supported-empty, unavailable, unauthorized, incompatible, stale, or canceled;
- bounded results and continuation cursor where applicable.

The host cancels obsolete calls. The recipient independently verifies result versions before applying them.

## Editor-neutral code intelligence

`StudioCodeEditor` accepts:

- `profile: compact | expanded`;
- `sessionKey`;
- neutral completion, hover, signature, formatting, and diagnostic providers;
- lifecycle callbacks for focus, blur, expand, newline insertion, and explicit actions;
- capability values, including unsupported;
- accessible status/escape descriptions.

The CodeMirror engine translates those inputs into engine extensions internally. Public consumers never import CodeMirror state, view, completion, lint, or language types.

## Surface behavior

Compact:

- one visual line;
- Tab/Shift+Tab indent/outdent;
- Enter accepts an active completion, otherwise requests expansion;
- Escape dismisses editor UI and temporarily enables Tab to move focus out;
- pasted/inserted newline is retained and requests expansion;
- compact diagnostics show markers and one highest-priority message.

Expanded:

- multiline;
- Tab/Shift+Tab indent/outdent;
- Escape then Tab and Tab-focus mode allow leaving the editor;
- full diagnostics list;
- explicit format action only when supported.

Both profiles use one `sessionKey` and therefore one source, selection, undo history, diagnostic set, and tooling-version state.

## Security and caching

- Never pass source, prefixes, symbol names, or diagnostic messages to telemetry.
- Sanitize Markdown and disable arbitrary HTML/executable links.
- Keep symbol metadata memory-only.
- Cache only by backend, subject, tenant, Expression Type, context revision, and query/page identity.
- On authorization invalidation, remove protected metadata and source from active views before attempting reload.
