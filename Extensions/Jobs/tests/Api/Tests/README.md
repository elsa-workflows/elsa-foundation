# Jobs API source controls

The suite composes the actual Jobs EF SQLite authority, its public migration/producer registration,
Foundation normalized-principal validation, a controlled current-subject authorization provider and the
actual MinimalEndpoints HTTP surface in TestServer. It adds no provider internals access or fake Jobs
state machine. Claims carry a stale wildcard so revocation must be enforced by the current directory.

Declared source cases cover tenant/partition/cursor isolation, canonical actor ownership, changing
resource/action decisions, payload permissions and safe metadata, forbidden model association,
idempotency, wire/page bounds, holds/capacity revisions, schedule preservation, linked retry and exact
delivery classification. The controlled identity provider exercises the trusted replacement contract;
full real identity-directory and independently hosted worker/secret/model/Studio journeys remain
separate acceptance obligations.

Regression controls discard a successful schedule-save reply, restart over the same SQLite store with
an absent or unavailable calculator, and require exact receipt replay while refusing new saves. A
malformed retained definition still refuses the original save. Cursor controls witness a denied queue
name in the provider's raw token and require its absence from the API response and decoded protected
bytes, current authorization on continuation, and rejection under changed actor, scope, filters,
collection, page size, tampering or excessive token length. Owned test key rings stay in the temporary
test directory, including across restart and alternate-partition hosts.

No build, discovery or execution has been performed for this source checkpoint.
