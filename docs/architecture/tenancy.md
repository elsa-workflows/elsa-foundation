# Tenancy in the kernel

> **Approved design, with conditions.** Gate 2 granted design approval on 18 November 2026 at
> `addffea7c850063d1ebe710b07866ff1640a93cb` ([verdict](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6990560103)). The implementation evidence is owed at K1 to K12,
> and the [gate 2 conditions](../program-goals/clean-slate-kernel.md#gate-2-conditions) stay open until it lands.
> This page answers finding M2 of the gate 2 architecture
> review ([review comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272)).

Several tenants share one Cluster database, its 1,024 virtual partitions and its owners. This page defines how the
kernel learns a tenant, how every name a caller can choose is kept apart per tenant, and what each ingress, match, read
and administrative operation checks. It follows the product's tenancy proposal, ADR 0097
([PR #4156](https://github.com/elsa-workflows/elsa-foundation/pull/4156), open, read at `8c63d086`): the bound
persistence scope is authoritative, it travels explicitly across every asynchronous boundary, there is no nullable or
wildcard tenant, and a missing scope fails closed. 0097 (a) also puts the scope in every tenant-keyed key; the kernel
amends that rule for the keys it generates itself (decision 4, below).

Terms: **tenant id** and **persistence scope** have ADR 0097's meaning (the Elsa glossary). The **bound scope** is the
scope a request's binder set from ADR 0097's resolver pipeline. A **tenant-qualified name** is `Q(t, n)`, defined below.

## Counterexample and answer

**The trace (M2).** Tenants A and B share a Cluster database.

1. A and B each submit an operator command with client operation id `op-1`.
2. The command is an outside add whose id is the operation id.
3. Ordinary scope-zero ids deduplicate across the whole store, so B's add meets A's row and gets a duplicate-key
   answer before B's owner sees it.
4. In the same way, A and B each subscribe to correlation key `order-17`. The event-match tuple
   `(subscription key, event ID, instance, wait, creation)` names no tenant, so A's publish can reach B's wait.
5. A tenant check on sends alone says nothing about publication matching, list and inspection queries, or replies.

**The answer, step by step:**

1. The ingress takes the tenant only from the bound scope (section "Where the tenant comes from"). A has tenant id
   `a`, B has `b`.
2. The ingress stores an outside message id as `Q(t, op-1)`. A's row has id `a_op-1` and B's has `b_op-1`.
   Store-wide deduplication compares the whole id, so the two never meet. A's retry meets A's row and gets the
   duplicate-key answer; B is unaffected.
3. Subscriptions and events store their key as `Q(t, order-17)`. A's publish looks up subscriptions by
   `a_order-17` and finds only A's waits. The match tuple's first field is the qualified key, so it names the
   tenant. B's wait is never matched by A's event.
4. Every query, reply and operator action is covered by the table in "Every operation", each with its uniqueness rule,
   authorization and refusal shape. An id that belongs to another tenant answers exactly as an id that does not exist.
5. Every tenant-keyed row also carries a `tenant` column. The owner checks it on load and on consumption as a
   second line: a message whose tenant differs from its target row's is refused as if the target did not exist.

## What is built

Nothing tenant-aware is built. The prototype's `InstanceRow` and `Message` have no tenant field (`src/Kernel.Durable`),
and its stores have no tenant column. Two pieces this design reuses are built:

- **Store-wide ordinary deduplication** under scope zero, and separate identity domains for issued sends, job tickets,
  child creators and event matches ([storage model](storage-model.md#inbox-identity-and-public-names),
  `Messages.cs`, `IssuedMailKey.cs`).
- **Hashed keyed requests:** `KeyRequest.WireId` is `key/` plus SHA-256 over the exact key and operation
  (`src/Kernel.Durable/KeyAdmission.cs`), a memory-only ingress experiment.

Everything below is **proposed and unverified**.

## Where the tenant comes from

- **At ingress, only from the bound scope.** The kernel's ingress API (the delivery seam's add, starts, publishes,
  operator commands, HTTP triggers, ingress adapters) takes a `BoundScope` value. It has no tenant string parameter.
  `BoundScope` is defined in the kernel's contracts package (K2) with an internal constructor, visible only to the
  kernel's Elsa hosting adapter package. That adapter reads the scope ADR 0097's binder bound with
  `PersistenceAccessContext.Scoped(scope)` in the request's DI scope, checks the tenant-id grammar below, and issues the
  `BoundScope`. The contracts package references no Elsa persistence package; only the adapter references both. The
  internal constructor is also visible (`InternalsVisibleTo`) to non-packable test support for store conformance and
  simulation. The Embedded hosting adapter binds one configured,
  grammar-checked tenant id for the process, the kernel's equivalent of ADR 0097 (g)'s fixed-tenant scope source. A
  tenant named in a payload, a header or a domain object is verified against the bound scope and never selects it; a
  mismatch is refused (403) without naming the other tenant (ADR 0097 (a), (c)). No bound scope means refusal; the
  kernel never defaults a tenant.
- **Resolution is ADR 0097's.** Token claim, then host, then a trusted header from an authenticated caller allowed to
  select a tenant, then route prefix. Anonymous HTTP triggers resolve by host or route only.
- **Embedded:** ADR 0097 (g)'s fixed-tenant scope source binds one ordinary scope (`"default"` unless the host sets
  another). The same contract and checks run; the host simply has one tenant.
- **Inside the kernel, from the row.** A turn runs with its instance's tenant. Everything a turn produces (sends,
  starts, timers, jobs, publishes, subscriptions, journal entries, values) takes the instance's tenant; `ITurn` has no
  way to name another. Layer 3 reads it as `ITurn.Instance.Tenant` and gets that scope bound for the turn's services,
  so an activity that reads secrets or connections reads its own tenant's.
- **Kernel pumps run per row.** The owner, the sweeper, the projector, the retention worker and plan actors act on
  rows that each carry a tenant, and bind that row's tenant for any tenant-keyed call they make. A row with no tenant
  is refused and reported, never processed under a default (ADR 0097's fail-closed pumps).

## Names: one namespace per tenant

**The tenant-id grammar (decided, 17 November).** A tenant id is 1 to 64 characters, each a lowercase ASCII letter, a
digit or `-`. It is enforced where the bound scope is created: the hosting adapter refuses a scope whose tenant id does
not match, so `Acme`, `acmé` and `acme_x` never reach the kernel. ADR 0097's tenant catalog must therefore issue only
ids in this grammar; its embedded default, `default`, already is one.

**The rule.** Every name a caller can choose is stored tenant-qualified:

```text
Q(t, n) = t "_" n          e.g. Q("acme", "op-1") = "acme_op-1"
```

`_` is outside the grammar, so the first `_` of a stored name ends its tenant, and `Q` is injective: two tenants never
produce one stored name.

**Under database collations: the grammar plus a pinned collation (decided, 17 November).** SQL Server and MySQL
compare a stored name with its column's collation, and the EF model pins none, so the kernel's tables would inherit
whatever the database administrator chose. The grammar alone does not make `Q` injective there: locale collations
contract and expand letter sequences. The independent reviewer's runs on SQL Server 2022 and MySQL 8.4, recorded in
[a comment on PR #4732](https://github.com/elsa-workflows/elsa-foundation/pull/4732#issuecomment-6956951060),
found grammar-valid ids that compare equal: `ccs_x` = `cscs_x` under SQL Server's `Hungarian_CI_AS` and
`Hungarian_100_CI_AS` and MySQL's `utf8mb4_hu_0900_ai_ci`, and `ssz_x` = `szsz_x` under `Hungarian_CI_AS` (run 1,
which returned 1, equal) and under `Hungarian_100_CI_AS` and `utf8mb4_hu_0900_ai_ci` (run 3). The same runs found `-` and `_` never ignored and never equal to each other in every collation
tested.

So every column that holds a tenant id, an actor id or a tenant-qualified name gets one fixed collation with no locale
rules, case-insensitive so that matching within a tenant stays as it is:

| Engine | Pinned collation |
|---|---|
| SQL Server | `Latin1_General_100_CI_AS_SC_UTF8` on the existing `nvarchar` columns; widths are unchanged |
| MySQL | `utf8mb4_0900_ai_ci` |
| PostgreSQL | The default deterministic collation. ICU nondeterministic collations (`deterministic = false`) are not used on these columns and must not be |
| SQLite | `BINARY`, the default |
| MongoDB | Binary comparison: no collection or index collation on these fields |

The rule is the authority: every column that holds a tenant id, an actor id or a tenant-qualified name is pinned.
The pinned columns are at least: `Instances.Id` and `Instances.Parent`, and the sketch's `root` and `correlation`
instance columns when they are added; `Messages.Id`, `Messages.Target` and `Messages.Sender`; `Subscriptions.Key` and
`Subscriptions.Instance`; `Events.Key` and `Events.Id`; `Journal.Instance`; the instance column of `Views`, of value
rows and of every read model, and the read models' `key` column; and every `tenant` column.

Under these collations the grammar's letters, digits and `-` and the separator `_` each compare equal only to
themselves, and neither collation has a contraction or expansion among those characters, so two tenants' qualified
names never compare equal. Folding inside the caller's name (case on both engines, accents on MySQL) stays within one
tenant, as it is today. The K3 store change and the K4 migrations create the pin; case T16 checks that they do.

**Measured on the pin.** The reviewer's confirmation runs
([recorded on PR #4732](https://github.com/elsa-workflows/elsa-foundation/pull/4732#issuecomment-6956951060)), in databases whose
default is Hungarian on both engines: the seven names `ccs_x`, `cscs_x`, `ssz_x`, `szsz_x`, `ab_x`, `a-b_x` and
`a_b_x` insert as seven distinct primary-key rows, and each `=` or parameter lookup finds exactly one. In unpinned
columns the Hungarian pairs collide (`ccs_x`/`cscs_x` is a duplicate key on both engines, and `ssz_x`/`szsz_x` compares
equal); the punctuation names `ab_x`, `a-b_x` and `a_b_x` are distinct with or without the pin. Joining an unpinned
value to a pinned column is an error, never a silent match. T15 makes this a standing case.

**Every expression uses the pin.** A query-level `COLLATE` overrides a column's collation, so the pin holds only if
every SQL expression that compares with, joins to, partitions by or hashes a pinned column uses the pinned collation
and never `DATABASE_DEFAULT`. The rule covers the owned SQL and the EF path, table-valued inputs such as `OPENJSON`
and `JSON_TABLE` whose values meet a pinned column, and the routine parameters, local variables and procedure bodies
of kernel routines: each string parameter or variable that carries a pinned value declares the pinned collation. Three
prototype sites break it today. They are evidence, unchanged here, and the kernel build must not carry them over:

- **The actor-alias lookup,** `EfStore.LoadActorsAsync` on SQL Server, joins `OPENJSON` values cast `COLLATE
  DATABASE_DEFAULT` to `Instances.Id`, and partitions by that value for the alias canonical spelling and
  `FirstEquivalent`. In a Hungarian-default database the input `ccs_x` matched the stored `cscs_x` (measured, one
  match; zero with the pinned collation). The load-time tenant check stops anything crossing tenants; tenant `ccs`'s
  work is still resolved to, or dropped because of, tenant `cscs`'s row. MySQL's `JSON_TABLE` shape returned zero matches,
  because its columns come back `utf8mb4_0900_ai_ci`.
- **The event-insertion stripe,** `EventInsert.cs` on SQL Server, takes its application-lock resource from
  `CHECKSUM(... COLLATE DATABASE_DEFAULT)`. Under a case-sensitive default, `Q(a, X)` and `Q(a, x)` are equal under the
  pin and can checksum differently, so two concurrent inserts of one match would take different locks and are not
  serialized. This is reasoned and not run.
- **The MySQL event-insert procedure,** `kernel_insert_event` in `EventInsert.cs` (`InstallMySql`), declares
  `IN wire VARCHAR(255)` with no `COLLATE`, so in a Hungarian-default database the parameter takes
  `utf8mb4_hu_0900_ai_ci`.
  [Measured by the reviewer](https://github.com/elsa-workflows/elsa-foundation/pull/4732#issuecomment-6956951060):
  `WHERE Id=wire` raises ERROR 1267 against a pinned `Messages.Id`, so event-start insertion fails loudly on a pinned,
  locale-default MySQL database; and the stripe `MOD(CRC32(WEIGHT_STRING(wire)),64)` is computed under the locale
  collation, so `ccs_x` and `cscs_x` share stripe 10.

**`_` is a `LIKE` wildcard.** Selecting a tenant's rows uses the `tenant` column, or a prefix match with `_` escaped;
an unescaped `LIKE 'acme_%'` would also match `acmex...`, so no query uses one.

**`_` keeps the effect key fit for its receivers.** An effect key carries its instance origin, so a caller-key
instance's key contains `Q`. `_` is RFC 5322 atext, so the key still fits the local part of the e-mail activity's
`Message-ID` (`SendEmail.MessageId` in [Activities.cs](../../essentials/src/Workflows/Runtime/Native/Activities/Activities.cs)), and a grammar tenant is printable ASCII, so it
fits the `Idempotency-Key` header value too. A caller name with characters outside atext already needs encoding there
today (`EffectKey.cs` states the fit holds "when those ids do"); the tenant part adds no such character. The receiver
does see the tenant id: tenant ids are not secret, and a host that must not show tenant names to receivers gives its
tenants opaque ids, which the grammar allows.

**Key widths (from the EF model, not measured).** `Q` adds at most 65 characters, all single-byte ASCII:

| Provider | Limit | Left for the caller's name |
|---|---|---|
| SQL Server | `Messages.Id` `nvarchar(450)` (`EfStore.cs`); clustered composite keys of `Subscriptions (Key, Instance, Wait)` and `Events (Key, Id)` within 900 bytes, 450 UTF-16 units in all | 385 for a message id; 380 for an actor id `{vp}/Q`, and 322 for a caller key, since every id the instance issues is its caller key plus 128 characters (`Q` and `{vp}/`, 70, and the issued id's prefix and suffix, 58: `c`, a 3-digit unit count, since ids here stay under 1,000 units, four separators, a 20-digit creation and revision and a 10-digit ordinal; observability's `N` + 129 counts a 4-digit unit count at the admission limit for an issued message id, and a turn's event id, with its reserved `~`, is `N` + 130 there (seventh review R7-M1)), so caller-key admission applies that bound (derived, unmeasured, K4); `Subscriptions` 385 for raw key, instance id and wait together, 315 when the instance has a caller key; `Events` 385 for raw key and event id together; a turn's generated event id is its actor id plus at most 59 units of prefix and suffix, the reserved `~` included (seventh review R7-M1), so a turn's `Publish` leaves 256 for raw key and caller key together (derived, unmeasured, measured at K4; fifth review R5-M1, [observability](observability.md#resolving-a-park-a-bounded-drain)) |
| MySQL | `Messages.Id` 255 characters (`EfStore.cs`) | 190 for a message id; 185 for an actor id, and 127 for a caller key, by the same issued-id bound (a 3-digit unit count) (derived, unmeasured, K4). The composite keys use the provider's default key width, recorded at K4 |
| PostgreSQL | No column limit; a B-tree index entry is at most about 2,704 bytes | Composite keys stay within that, 65 bytes less for each qualified column |
| SQLite, MongoDB | None that `Q` reaches | Not limited by `Q` |

| Name | Chosen by | Stored as | Unique within |
|---|---|---|---|
| Caller-key actor id | Caller | `{vp}/Q(t, key)` of the first spelling to commit the key's binding row, with `vp` the hash of that `Q(t, key)` over 1,024; every equivalent spelling reads the binding ("Caller keys resolve through a binding row", below) | The store, so the tenant |
| Caller-key binding (proposed) | Kernel, at the key's first use | A row `(tenant, Q(t, key))` holding the actor id and the reuse window, the key in a pinned column; never deleted | The tenant, under the pinned collation |
| Generated actor id (child, event start, job and plan actors) | Kernel | As today (issuer partition, epoch and ordinal; event sequence) | The store, by construction |
| Job queue actor id | Kernel, from caller names | `queue/Q(t, name)/{shard}` (job workers' `queue/{tenant}/{name}/{shard}`, made injective) | The store |
| Outside message id, operator operation id | Caller | `Q(t, id)` in ordinary scope zero | The store, so the tenant |
| Keyed request wire id | Caller | `key/` + SHA-256 over the binding's actor id and the operation's exact code units (below) | The store |
| Message id a turn sends | Kernel | Derived from sender id and revision, as today | The store, since sender ids are |
| Correlation key (subscription and event) | Caller or model | `Q(t, key)`; a sensitive key is `Q(t, "h:" + HMAC)` with the per-tenant key (data lifecycle) | The store |
| Outside event id | Caller | `(Q(t, key), id)`; an id provider-equal, under the pinned equality of `Events.Id`, to one starting with `~` is refused `InvalidEventId` with nothing written (owner decision, 18 November (seventh review), R7-M1) | The qualified key |
| Turn event id (proposed) | Kernel | `(Q(t, key), ~{origin}#{revision}.{ordinal})`, the reserved `~` ahead of the publish ticket's issued id ([observability](observability.md#recover), "Turn and caller event ids are disjoint") | The qualified key, and disjoint from every outside event id |
| Value row | Kernel | `(instance, digest)`; the digest is keyed per tenant where erasure needs it | The instance |
| Effect key | Kernel | `{instance origin}/{node}/{iteration path}/{attempt}`, as decided | The store, since the origin is the store-unique id with its creation |

**Caller keys resolve through a binding row (proposed, fourth review R4-M6).** The actor id of a caller key carries
a placement prefix computed outside the database, and the placement hash runs over the spelling's UTF-16 code units
(`InstanceRef.Spread`, `Addresses.cs` 22–27), while the pinned collation on SQL Server and MySQL folds case, and on
MySQL accents too. Executed in this repair, the size probe ran the real hash
([experiments/Astra4.Sizes](../../prototypes/kernel-model/experiments/Astra4.Sizes/README.md)): `Spread("a_Invoice", 1024)` is 460 and
`Spread("a_invoice", 1024)` is 300, and `a_café` and `a_cafe` give 1000 and 796. Two first uses of one key under two
spellings would get two actor ids with different prefixes, reach two partitions and two owners, and no comparison
under the pin could merge them. So a keyed request resolves its actor id before it is routed:

- **The binding row.** Each caller key has one binding row `(tenant, Q(t, key))`, holding its actor id and its reuse
  window, with the key in a pinned column that is unique under the pinned collation. Key admission writes it by a
  store-side insert-or-read: in one transaction it inserts the row, with the actor id `{vp}/Q(t, key)` computed from
  the request's own spelling, unless an equivalent row exists, and then reads the row. The first spelling to commit
  wins. Every equivalent spelling reads the same row, so it routes to the same actor id, the same virtual partition
  (from the id's own `{vp}`) and the same owner, and concurrent first uses on two nodes converge on one actor and one
  creation, since the unique key serializes their inserts.
- **Never deleted.** The binding row is kept like the creation high-water mark at the retirement marker ([data
  lifecycle](data-lifecycle.md#proposed-the-qualified-delete)), which same-id addressing already needs: a key reused
  after its instance is deleted reads its binding and starts the same actor id under a higher creation, whichever
  equivalent spelling the new start uses.
- **The key is a name; the operation is a token.** This covers a keyed request's operation id. An operator command's
  operation id is its outside message id, `Q(t, operation)` in ordinary scope zero (the table above), an ordinary `Id`
  compared under the pinned collation, in which admission refuses an unpaired unit ("Where names enter" in
  [observability](observability.md#resolving-a-park-a-bounded-drain)). The caller key is compared under the pinned collation, through
  the binding row. A keyed request's operation id is an opaque token, compared exactly, by its UTF-16 code units, and stored only as
  its digest, as today's wire id already holds it only hashed and the receipt only base64-encoded (`KeyAdmission.cs`
  14–17). Admission accepts a NUL or an unpaired unit in an operation id ("Where names enter" in
  [observability](observability.md#resolving-a-park-a-bounded-drain), and S-6), which a `utf8mb4` column cannot store
  and PostgreSQL text refuses, so no column holds the operation's spelling. A receipt is keyed by the binding's actor
  id and the SHA-256 of the operation's code units, and written by the same insert-or-read in the admission's
  transaction. An equivalent key with the same operation finds that receipt and is a duplicate: the earlier answer
  when the rest of the request is equal, and `conflict` when it differs, as the prototype's receipt rule answers today
  (`KeyAdmission.cs` 54–56). Two operation ids that differ in case, or in any unit, are two operations, and the
  existing rules apply to the second: a second start on a live binding answers `in-use` (`KeyAdmission.cs` 62), and
  a command is accepted as a new request. The wire id is `key/` and the SHA-256 over the canonical encoding of the
  binding's actor id and the operation's code units, so every equivalent spelling of the key, with the same
  operation, produces one message id.
- **A start while the predecessor's cascade still pages** (proposed, fifth review R5-B2). A keyed root deleted while
  its own terminate's cascade is `Discharging` keeps that obligation on its retirement marker until the last page
  ([observability](observability.md#every-park-operation-names-its-park), "A termination cascade is paged and
  idempotent"). Admission decides a start against it, in the admission transaction: after reading the binding it
  reads the marker of the binding's actor id, and while the marker's cascade is `Discharging` it answers
  `in-progress` with reason `ObligationOutstanding`, a retryable refusal (HTTP 503 with `Retry-After` at the API),
  and writes no receipt and no message. A retry with the same operation finds no receipt and is decided again, as a
  retry with a new operation is; both are refused while the cascade pages and admitted after its last page, at
  most ⌈n / `P_cascade`⌉ page turns after the delete. Deciding it here keeps an acknowledged start from being lost:
  admission otherwise answers `accepted` and writes its receipt before the start is routed (`KeyAdmission.cs`
  76–81), the start it writes carries no reply address (71–72), so a refusal at the worker would reach nobody, and a
  same-operation retry would get the stored `accepted` receipt (54–56). The read is the same kind of check
  admission already makes when it tests whether the bound instance is retired (`KeyAdmission.cs` 57–58 and 62). The
  worker's `ObligationOutstanding` refusal of a successor creation stays as a defence in depth below ingress. Cost:
  one more point read, only on a start whose binding exists, measured at K4.
- **Case-sensitive providers.** PostgreSQL, SQLite and MongoDB compare these columns in binary, so two spellings are two
  keys with two bindings, as their own equality says.
- **Cost.** One store read per keyed request, and an insert on its first use: a coordinator design choice, measured at
  K4. It keeps the owner's case-insensitive pin (decision 6), so it needs no owner decision.
- **What is built.** The prototype's key admission is memory-only: it keeps a binding per key under one lock with
  ordinal comparison, replaces the binding's run id on reuse and hashes the raw key for placement
  (`KeyAdmission.cs` 42–83, existing prototype behaviour). The actor-alias lookup resolves spellings of an id that is
  already stored ([storage model](storage-model.md#inbox-identity-and-public-names)); it cannot serialize two first
  uses. Cases KB-1 to KB-3.

**The kernel's amendment to ADR 0097 (a) (decided, 17 November).** 0097 (a) puts the bound scope in every
tenant-keyed key. For the kernel: names a caller chooses are tenant-qualified, so the scope is in their keys; ids the
kernel generates (children, event starts, job and plan actors), journal positions and value rows stay unique across
the store, carry a `tenant` column, and the kernel checks that column (next section) in place of a scope in the key.

Two consequences need no further rule:

- **Effects.** Tenants A and B starting caller key `invoice-9` get ids `{vp}/a_invoice-9` and
  `{vp'}/b_invoice-9`, so their effect keys differ. A deduplicating receiver never drops B's request as A's repeat.
- **Placement.** The hash runs over the qualified name, so a tenant's caller keys spread over the virtual partitions
  independently of another tenant's.

Host-wide records carry no tenant (ADR 0097 (e): cluster membership is host-wide): `partition` rows (fence,
position, backlog), `node` rows (membership and, under the node lease amendment, the lease) and the projector's `projection` cursor. A virtual partition's journal holds rows of
many tenants; each journal row carries its instance's tenant.

**Layer 3 stays host-neutral.** Ids stay opaque strings to layer 3; only layer 2 forms and parses `Q` and `{vp}/`.
The API shows a caller its own key, from the read model's `key` column; it never shows another tenant's prefix.

## The tenant column

Every tenant-keyed record gets `tenant`, not null: `instance`, `message`, `subscription`, `event`, `journal`, view
rows, value rows and the read models (instance list, parked work, plan outcomes). Every read-model index leads with
`tenant`. The column is written from the bound scope at ingress and from the sender's row inside the kernel, and it is
checked:

- **On load and consumption.** A message is delivered only to a target row with the same tenant. A message whose
  target exists under another tenant is treated as a message to a missing target: dropped and journalled in the
  message's own tenant. A message with a start spec that names such an id is refused the same way. The target row is
  not touched and nothing about it is reported.
- **At commit.** The owned commit SQL and the EF path write the tenant column with each row; the store conformance
  suite checks that no batch writes a row whose tenant differs from its instance's.

MongoDB stores `tenant` as a top-level field and leads the same indexes with it; nothing here needs a relational
feature.

## Every operation

| Operation | Tenant from | Uniqueness | Authorization | Refusal, and what it reveals |
|---|---|---|---|---|
| Start without a caller key | Bound scope | Generated id | Start permission; the definition is looked up in the tenant's publications | Unknown definition: 404, as for a definition that does not exist |
| Keyed start or command | Bound scope | `{vp}/Q(t, key)`, read from the key's binding row before routing, so every spelling equal under the pinned collation reaches one actor; outside id `Q(t, id)` | The command's permission in the tenant | Duplicate: the tenant's own earlier answer, for a key equivalent under the pinned collation and the exact same operation id. Another tenant's equal key is a different name and never answers |
| HTTP trigger | Host or route (ADR 0097 (c)) | The route table is per tenant | The route's own rule | No route for this tenant: 404 |
| Ingress adapter (broker, webhook, schedule) | The adapter's binding, one scope per configured source, or a trusted header verified against the adapter's authenticated identity | `Q(t, source event id)` | The adapter's grant for that tenant | Unbound or mismatched: refused and logged host-side, not inserted |
| Outside publish | Bound scope | Event `(Q(t, key), id)`; proposed (fifth review R5-M2): a kernel-issued publish ticket `pub:` + SHA-256 of `(Q(t, key), id, incarnation)`, 68 ASCII bytes, in the virtual partition its digest places, the incarnation being the event row's (sixth review R6-B2, [observability](observability.md#recover)) | Publish permission, and the ticket's partition high-water rule; a caller event id provider-equal to one starting with the reserved `~` is refused `InvalidEventId`, nothing written (owner decision, 18 November (seventh review), R7-M1) | Matches only `Q(t, key)` waits and the tenant's own event-start subscriptions |
| A turn's send, start, publish, subscribe, job, timer | The instance's row | Derived ids; qualified names for caller-chosen keys | Kernel-internal | A target under another tenant is a missing target (above) |
| Event match | The event's qualified key | The match digest over the stored qualified key, event id and publication incarnation ([observability](observability.md#resolving-a-park-a-bounded-drain)), so two tenants' matches never share a digest | Kernel-internal | None: other tenants' waits are never candidates |
| Reply to a synchronous request | The request's bound scope, recorded in the reply address | One reply address per request | The owner sends output only to reply addresses on messages in its instance's inbox; the receiving node checks the reply's tenant against the waiting request's | A mismatch is dropped and counted, never written to a response |
| Instance list, get, viewer (rows and journal tail), parked work, plan outcomes, inbox inspection | Bound scope | Read-model rows keyed by tenant | Read permissions; payloads need their own (observability) | Another tenant's id: 404, the same status and body as a missing id |
| Recover, retry, discard, suspend, resume, terminate, delete, migrate, plans | Bound scope | Operation id `Q(t, op)` | Separate operator permissions per tenant, audited (observability) | Another tenant's instance: not found, as above |
| Admission (rates, `InboxFull`) | Bound scope | Token buckets per tenant | None | `RateLimited` is per tenant. `InboxFull` is per virtual partition and tenant-blind; it names no instance, key or other tenant |
| Metrics and traces | Host-wide | Not applicable | Host monitoring | Tenant appears as a capped attribute only where a row says so; no payload, key or id |
| Host administration of ownership: release a partition, drain a node (release 1); force a handover (after release 1, [observability](observability.md#operator-actions)) | Host-wide, no scope | Not applicable | Host-admin permissions, distinct from any tenant role: the kernel's proposal; ADR 0097 (d) defers that distinction to its Phase 3 | Writes `partition` and `node` rows; reads no tenant payload and changes no tenant row's tenant, ids or payload. The ownership protocol's own bookkeeping still runs: a take by another node after the previous owner's lease expired adds one attempt to each message on trial at the old epoch, and a same-node retake or a take after a release adds none (`InMemoryStore.TakeAsync`), the new owner marks what it polls `Seen` and puts suspects on trial, and its ordinary commits go on with each tenant's work |
| Host administration of the schema: initialize or migrate the kernel's tables | Host-wide, no scope | Not applicable | Host-admin permissions, as above | Creates or alters tables and indexes, the collation pin among them. A migration may add, drop or retype columns and indexes, and rewrite stored values into a new format with the same meaning (a codec or key-format change). It keeps every row's tenant and the tenant-qualified form of its names, and never moves a row to another tenant or merges rows of two tenants. A key-format rewrite preserves every id that effect keys, callers or read models depend on (actor ids, so the instance origin inside effect keys; outside message and operation ids; read-model keys), because an in-flight activation would otherwise derive a new effect key and a receiver would see a repeat. Each listed migration is a schema-family version change under ADR 0077 and the plan's [compatibility rule](../program-goals/clean-slate-kernel.md#kernel-schema-and-protocol-compatibility-proposed) whenever a running version cannot read its result, and a codec or key-format rewrite always is; in release 1 each such change is a coordinated stop |
| Cross-tenant support | ADR 0097's support context (Phase 3): target-bound, expiring, audited, one tenant at a time | As the target tenant | The support profile's grants for that tenant | No wildcard, `*` tenant or permission that implies every tenant. Before Phase 3 there is none |

**Global operator exceptions** are exactly the two host administration rows: they act on infrastructure records and
the schema under host permissions, and neither shows a tenant payload to the operator. Every other operator action is
a tenant operation in a bound scope. A host administrator who must inspect a tenant's work enters that tenant through
the support context once it exists.

## Release 1

- Release 1 implements this contract for the kernel whatever the timing of ADR 0097's own phases. When the product
  resolver is not ready, the fixed-tenant scope source binds one tenant, and every check above still runs.
- **Storage:** the shared-store discriminator, ADR 0097's v1 baseline: one database, tenants sharing the 1,024 virtual
  partitions, the tenant in every tenant-keyed name and as a column. Database per tenant (ADR 0097's opt-in hybrid,
  its Phase 5) maps to one kernel per tenant store, each with its own partitions and ownership; see "Decisions".
- **Changes for K3:** the tenant column on every tenant-keyed record, `Q` at every ingress and in the keyed-request
  hash, the caller-key binding row with its insert-or-read before routing and its receipts under the pinned
  collation (fourth review R4-M6), qualified correlation keys, the tenant check on load and consumption, read-model indexes led by tenant, and
  the conformance cases below. Fairness's tenant column on the instance row is part of this.
- **Kernel-behaviour store change for K3 (two reviews):** the collation pin (decision 6) on every pinned column, created
  by the schema and the migrations of every relational provider, and the pinned collation in every expression that
  compares with, joins to, partitions by or hashes a pinned column, and in every kernel routine's string parameters
  and variables, with T16 and T17. The three known prototype sites are not carried over: the actor-alias lookup in
  `EfStore.LoadActorsAsync` and the SQL Server stripe in `EventInsert`, both `DATABASE_DEFAULT`, and the MySQL
  procedure `kernel_insert_event`, whose `wire` parameter has no `COLLATE`.

## Fourth review: counterexamples and answers

**R4-M6: equivalent caller keys could route to different owners**
([fourth gate 2 review](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6967291576)).

1. Trace: on SQL Server and MySQL the pinned collation makes tenant a's `Invoice` and `invoice` one name. The
   caller-key actor id `{vp}/Q(t, key)` takes its prefix from a hash of the spelling's code units, measured in the
   size probe at 460 and 300, so two first uses under the two spellings get two complete actor ids, reach two
   partitions and can reach two owners. A spelling taken from an already stored actor id, or the first spelling in
   one batch, cannot serialize two requests that arrive on different nodes, and a wire id hashed over the raw key
   and operation does not say which spellings are one receipt.
2. Revised: a keyed request reads its actor id from the key's binding row before routing, and the row is written by
   a store-side insert-or-read, unique under the pinned collation ("Caller keys resolve through a binding row"
   above). The first spelling to commit wins; the second request's insert finds the equivalent row and reads it, so
   both route to the winner's actor id and partition, and the instance gets one creation.
3. The receipt is keyed by the binding's actor id and the digest of the operation's exact code units: the key is a
   name compared under the pin, and the operation an opaque token compared exactly, since admission lets it hold
   units no pinned column can store. A retry under an equivalent spelling of the key with the same operation is a
   duplicate, a different payload under them is a `conflict`, and an operation that differs only in case is another
   operation (a second start answers `in-use`). The wire id hashes the actor id and the operation's units, so the
   message row is one. The binding is never deleted, so a reuse after a delete gets the same actor id under a higher
   creation. A case-sensitive provider keeps two bindings, by its own equality. Cases KB-1 to KB-3; the size probe
   measured the placement hash only, and no concurrent first use has been run.

## Acceptance cases

Each runs in the store conformance suite on the in-memory store first (K3), then on PostgreSQL, SQL Server, SQLite,
MySQL and MongoDB (K4), unless the milestone says otherwise. "A" and "B" are two tenants of one database.

| Case | Scenario | Pass when | Milestone |
|---|---|---|---|
| T1 | The contracts compile against `BoundScope` | No ingress method takes a tenant string; `ITurn` exposes the tenant read-only and `Send` has no tenant parameter; the architecture guard fails a layer 3 reference to `Q` or the tenant column. The built-in contract boundary passed this case, including setter and vocabulary mutations ([#4958](https://github.com/elsa-workflows/elsa-foundation/pull/4958)). | K2 |
| T2 | A and B add an outside message with id `op-1`; then A retries | Both are delivered once; A's retry gets A's duplicate-key answer; B's row is untouched | K3, K4 |
| T3 | A and B subscribe to `order-17`; A publishes, then B publishes; one wait asks for kept events | A's event reaches only A's waits and B's only B's, kept events included; the match tuples differ | K3, K4 |
| T4 | A and B each have an event-start subscription on `order-17` | Each publish starts one instance, in its own tenant | K3, K4 |
| T5 | A and B start caller key `invoice-9`; each sends an HTTP request and an e-mail through the effect-key activities, against one deduplicating fake receiver | Two instances with different ids; the fake receives two distinct `Idempotency-Key` values and two `Message-ID`s, and applies both | K6 |
| T6 | B lists, gets, views (rows and tail), inspects the inbox of, and reads parked work of A's instance by its id | Every call answers as for a missing id: same status and body; no row of A is returned | K9 |
| T7 | A turn of A sends to, and starts with, a generated id that belongs to B | The message is dropped and journalled in A's tenant; B's row, inbox and journal are unchanged | K3, K6 |
| T8 | Two routes with the same path, resolved by host to A and B; a resume for each owned by another node | Each response reaches its own caller; a reply address altered to the other tenant is dropped and counted | K8 |
| T9 | A and B send `kernel.recover`, `discard` and `terminate` with operation id `op-7`; an operator of B targets A's instance. A second run in tenant A: a caller with the terminate permission and no delete permission sends `kernel.terminate` with `ThenDelete` to an instance of A, and a caller with both sends the same | Both tenants' commands apply once each; B's attempt on A's instance answers not found and changes nothing. The caller without the delete permission is refused at the entry point with reason "permission": no message row, the instance unchanged, and only the audited refusal recorded; the caller with both permissions terminates and deletes ([observability](observability.md#every-park-operation-names-its-park)). Mutation: `ThenDelete` accepted with the terminate permission only; the first caller's command deletes the instance | K11 |
| T10 | A bursts until `RateLimited` and its virtual partitions pass the threshold | B's rate is unaffected; every refusal B sees names no key, id or tenant of A | K11 |
| T11 | An ingress call without a bound scope; a claimed tenant that differs from the bound one; a message row and an instance row with no tenant | Refused (none bound), 403 (mismatch), refused and reported by the owner and the sweeper (no tenant). At K3, `--foundation-tenant-check` refuses an unbound send and injects missing and foreign stored tenants on the real in-memory paths. A one-row poll still offers valid work. Load and a fenced commit refuse a damaged checkpoint, and the sweep retains damaged rows while expiring valid ones. The host mismatch and production report qualify at K11. | K3, K11 |
| T12 | While A and B have work, including messages on trial: host administration releases a partition and drains a node; then a node that holds A's and B's partitions is killed, and another node takes them once their leases expire | Release and drain need host-admin permission, and a tenant operator of A or B is refused; no host-admin step reads a tenant payload; every tenant row keeps its tenant, ids and payload, with only the protocol's specified bookkeeping on message rows (`Seen`, `Trial`, and one more attempt on the messages that were on trial at the crash); both tenants' work continues under the new owner, and every later commit lands in its own tenant | K5, K11 |
| T12b | The same, with a forced handover from an unresponsive owner in place of the crash | As T12, and the old owner's next commit fails the fence | After release 1, with the forced handover |
| T13 | Simulation: two tenants with colliding operation ids, caller keys and correlation keys in the mix; mutations `TenantlessOutsideId`, `TenantlessCorrelationKey` and `SkipTenantCheckOnLoad` | The invariant "no delivery, match, read or effect crosses tenants" holds. `Kernel.Sim --foundation-tenant-check` exercises the bound routes, committed reads, live and kept broadcasts, event starts and jobs with the real in-memory store and a Worker takeover. It checks both tenants' histories and exact effect targets. The three mutations are witnessed by the outside-root receipt, event history and point-read assertions respectively | K3 |
| T14 | T2 to T11 in Embedded with the fixed tenant, and in Cluster with two nodes and two tenants | All pass on both profiles | K12 |
| T15 | The binder is given `Acme`, `acmé`, `acme_x` and a 65-character id. Then T2 and T3 run against the pinned schema on SQL Server 2022 in databases whose default collation is `SQL_Latin1_General_CP1_CI_AS` (SQL Server's default, accent-sensitive) and `Hungarian_100_CI_AS`, and on MySQL 8.4 with database defaults `utf8mb4_0900_ai_ci` and `utf8mb4_hu_0900_ai_ci`. Tenant pairs: `ab`/`a-b`, `a`/`a-b` (`a_b_x` against `a-b_x`), `ccs`/`cscs` and `ssz`/`szsz` | The four ids are refused at the binder; every pair stays two rows, two matches and two answers, including `ccs_x` and `cscs_x` in the Hungarian-default databases | K4 |
| T16 | Inspect the schema the K3 store change and the K4 migrations create, on SQL Server and MySQL, in a database with a locale default collation | Every pinned column has the pinned collation; a column created without it fails the check | K3, K4 |
| T17 | In a Hungarian-default database on SQL Server and MySQL, run T2 and T3 through the actor-alias lookup (keyed commands to `ccs_x` and `cscs_x`, each with its own row) and through event-start insertion; on SQL Server, also in a case-sensitive-default database, insert one event match concurrently under two spellings equal under the pin. A guard over the owned and EF SQL flags `COLLATE DATABASE_DEFAULT` applied to a pinned value, and any string parameter or local variable of a kernel routine declared without the pinned `COLLATE` | Each lookup resolves to its own tenant's row; MySQL event-start insertion succeeds, and its stripe is the hash of the value under the pinned collation; the two concurrent inserts are serialized and land one match; the guard flags nothing | K3, K4 |
| KB-1 | **Concurrent first uses under equivalent spellings** (the fourth review's R4-M6). On SQL Server and MySQL under the pinned collation, two nodes accept a keyed start for tenant A at the same time, one under `Invoice` and one under `invoice`, with the fixture holding both past routing until both have read the binding; on MySQL also `café` and `cafe`; on PostgreSQL, SQLite and MongoDB the same pairs as the case-sensitive control; at K3 the in-memory store with an equality hook that folds case, and once with ordinal equality | Where the pin folds the pair: exactly one binding row, one actor id (the winner's spelling and its `{vp}`), one instance at creation 1; the other start answers `in-use`, or, with the same operation id, the winner's answer when the two requests are equal and `conflict` when they differ. On the control providers and under ordinal equality: two bindings and two instances. Mutation: route by the request's own spelling (`Spread` over its code units, 460 and 300 as measured); two actor ids and two instances exist for one key. K3 runs both independent feeders against the same store admission lock, then drives the winning creators through their Workers. The folded cases cover repeat, duplicate and conflict. For conflict, the losing public send returns `Refused` with reason `Conflict`, and the request replay answers `conflict`. The ordinal control creates two actors. The spelling mutation fails all three folded races. K4 covers the provider transaction and its concurrent binding inserts | K3, K4 |
| KB-2 | **Retry, deletion and reuse; the operation as a token** (R4-M6). Tenant A starts `Invoice` with operation `op-1`. Then: a retry as `INVOICE` with `op-1` and the same request; the same with another payload; a start as `invoice` with `OP-1`; a start as `invoice` with `op-2` while the instance lives; a keyed command as `invoice` with `op-9`, its retry as `INVOICE` with `op-9`, and a command as `invoice` with `OP-9`; a command whose operation holds an unpaired unit, and one whose operation holds a NUL, on every provider; the instance is deleted, and a start as `iNvOiCe` with `op-3` after its reuse window; a sweep, a retention run and the delete itself between the steps | The start's retry is a duplicate with the original answer and adds no message; the other payload answers `conflict`; `OP-1` is another operation and answers `in-use`, as does `op-2`; the `op-9` command is applied once and its retry is a duplicate, while `OP-9` is a second command, accepted and applied; the operations holding an unpaired unit or a NUL are admitted and deduplicated by their digest on every provider, MySQL included, with no column holding their spelling; the reuse gets the binding's actor id, unchanged, under creation 2; the binding row survives every sweep, retention run and delete. Mutation: delete the binding with the instance; the reuse computes its id from its own spelling, a different `{vp}`, and gets creation 1 again under a new id. Mutation: key the receipt by the request's key spelling in place of the binding; the `INVOICE` retry of `op-9` misses the receipt and the command is applied twice. Mutation: store the operation in a pinned column and compare it there; `OP-9` is taken as a duplicate of `op-9`, and on MySQL the operation with an unpaired unit cannot be stored, so an admitted request fails at the store | K3, K4 |
| KB-3 | **A Park addressed through an equivalent spelling** (R4-M6). On SQL Server and MySQL, A is parked under key `Invoice`; an operator's discard addressed by key `INVOICE`, carrying A's `ParkSelection`, whose `Instance` is the binding's actor id, is added and delivered; on PostgreSQL the same as a control | The key resolves through the binding to A's actor id, which equals the selection's `Instance`, so the target check passes; the store pins `TargetCreation` to A's creation; A is discarded once, with its parent's one Faulted notice; the parked-work read model lists A under its stored key spelling. On PostgreSQL `INVOICE` is another key and the command answers not found. Mutation: resolve the key by the request's spelling; on SQL Server the command reaches no instance and answers not found, and A stays parked | K9, K11 |

The two-tenant fairness run of K12 measures latency and refusals; it does not replace these cases.

## Decisions (owner, 17 November 2026, by card)

1. **Identities: tenant-qualified names plus the tenant column.** Names are store-unique by construction, so
   store-wide deduplication, the match tuple and the decided effect-key shape stay correct even if a query forgets its
   tenant predicate; such a mistake could still disclose a row, which the column check and T6 catch. Rejected:
   composite `(tenant, id)` keys with ids unique only per tenant.
2. **Storage: the shared store only in release 1,** ADR 0097's v1 baseline. A kernel per tenant store comes later,
   with its cost per store (1,024 fence rows, beats and a projector each, or fewer partitions for small tenants)
   measured before it is offered.
3. **Tenant id length: at most 64 UTF-16 units.** The key widths above follow from it.
4. **The kernel's amendment to ADR 0097 (a):** caller-chosen names are tenant-qualified; generated ids, journal
   positions and value rows stay unique store-wide with a checked tenant column.
5. **A canonical tenant-id grammar:** lowercase ASCII letters, digits and `-`, enforced where the bound scope is
   created.
6. **A pinned collation on every column that holds a tenant-qualified name:** one fixed, non-locale, case-insensitive
   collation, `Latin1_General_100_CI_AS_SC_UTF8` on SQL Server and `utf8mb4_0900_ai_ci` on MySQL, so `Q` stays
   injective whatever the database default is, with the pinned collation in every expression (T17), and matching
   within a tenant stays case-insensitive.

ADR 0097's own questions (Q3 tenancy model, Q4 no agnostic tenant, Q7 support context) are pending owner there. This
page follows their recommended defaults; decisions 4 and 5 touch 0097's section (a) and its tenant catalog, and 0097's
author carries them into that ADR.

## Contract and evidence

ADR 0097 ([PR #4156](https://github.com/elsa-workflows/elsa-foundation/pull/4156)) is the product contract this page
follows. The [storage model](storage-model.md#inbox-identity-and-public-names) gives today's identity domains,
[fairness and backpressure](fairness-and-backpressure.md) the per-tenant admission and turn selection,
[observability](observability.md#recover) the operator actions, and [data lifecycle](data-lifecycle.md) the per-tenant
keys for erasure. What exists today is listed under [What is built](#what-is-built); the contract this page proposes
is not built, and no run has measured it. The measurements the page reports are earlier runs against the engines and
today's prototype: the reviewer's collation confirmation runs under "Measured on the pin", and the actor-alias and
MySQL procedure results under "Every expression uses the pin". They show the problem and the pin's effect on single
statements. They qualify no part of the proposed implementation, which T15 to T17 test once it is built.
