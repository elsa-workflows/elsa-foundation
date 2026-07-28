# Data Model: Receive Event Correlation

This feature adds no entity, field, document kind, index, or migration. It supplies an optional
value through an existing metadata channel.

## Existing Entities and Relationships

| Entity | Relevant data | Relationship in this feature |
|---|---|---|
| Authored Event wait | Event name; optional correlation value | Produces one wait registration when the Event suspends. |
| Typed wait registration | Event stimulus identity; immutable metadata | Holds the correlation metadata only for a nonblank authored value. |
| Durable bookmark | Stimulus identity; immutable metadata; lifecycle timestamps | Receives the registration metadata unchanged and is the lookup candidate. |
| Event delivery | Event identity; optional correlation value | Selects matching bookmarks by event identity, then correlation when supplied. |

## Metadata Rule

| Condition at Event wait registration | Retained correlation metadata | Delivery result |
|---|---|---|
| Nonblank value after trimming | Existing runtime correlation key with the trimmed value | A same-named correlated delivery resumes only an exactly matching bookmark. |
| Null, empty, or whitespace-only value | Key absent | An unscoped same-named delivery retains broadcast eligibility; a correlated delivery excludes the wait. |

## Lifecycle and Compatibility

1. An Event suspends and emits one typed wait registration.
2. Existing runtime processing persists the registration metadata with the bookmark.
3. A delivery without correlation selects every eligible same-named bookmark, including correlated and uncorrelated waits.
4. A delivery with correlation selects only eligible same-named bookmarks retaining the identical value.
5. On resume, the normal bookmark-consumption lifecycle applies unchanged.

Existing persisted bookmarks without the correlation key remain compatible: they are eligible for
unscoped delivery and excluded from a correlated delivery. No backfill is necessary because the
feature is opt-in and waiting bookmarks are transient runtime state.
