# Data Model: Contributed Runtime Intent Handlers

## Runtime intent handler

- **HandlerType**: concrete contributed handler identity.
- **Lifetime**: resolved within the scoped post-commit delivery operation.
- **Operation**: handles one already-committed `RuntimePostCommitIntent` asynchronously.

## Handler contribution

- **IntentKind**: non-empty stable ordinal identifier supplied at registration; one logical owner per runtime composition.
- **HandlerType**: concrete handler type resolved in the delivery scope.

### Validation

- Blank kinds are invalid.
- Repeated `(IntentKind, HandlerType)` contributions collapse to one logical registration.
- One `IntentKind` mapped to multiple distinct handler types is a deterministic composition conflict.

## Handler map

- Immutable per resolved dispatcher scope.
- Key comparison is `StringComparer.Ordinal`.
- Values contain exactly one handler per key after validation.

## Existing durable entities (unchanged)

### RuntimePostCommitIntent

The durable intent remains unchanged: intent ID, workflow execution ID, kind, payload, and existing wait/failure metadata. Handler registration does not become persisted state.

### RuntimePostCommitOutboxItem

Status transitions remain the existing delivery model and are selected by the outbox item's retry policy:

```text
Pending / retry-eligible failure → Delivered
                                ↘ policy-selected failed state
```

No new status, retry promise, or persistence field is introduced by #675.
