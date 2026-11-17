# Tenancy in the kernel

> **Proposed.** The kernel design awaits owner approval. This page answers finding M2 of the gate 2 architecture
> review ([review comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6949612272)); it
> does not authorize product implementation.

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
  `BoundScope`. The contracts package references no Elsa persistence package; only the adapter references both. A
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

**Under SQL Server and MySQL collations.** Those engines compare a stored name with the column's collation, which may
ignore case, accents, width and trailing spaces. Two qualified names of different tenants differ inside the tenant part,
or at the position where the shorter tenant has `_` and the longer one a grammar character. Case and accent folding map
only upper case to lower case and accented letters to their base letters; the grammar has neither, so its letters and
digits stay distinct from each other and from `_`. Trailing-space padding and expansions such as `ß` = `ss` can only
equate characters inside `n`, which is within one tenant, as today. The punctuation `-` and `_` is where collations
differ most (SQL Server's Windows collations give the hyphen a special sort weight), so case T15 checks equality of
names built to collide if either were ignored, on both engines.

**`_` keeps the effect key fit for its receivers.** An effect key carries its instance origin, so a caller-key
instance's key contains `Q`. `_` is RFC 5322 atext, so the key still fits the local part of the e-mail activity's
`Message-ID` (`SendEmail.MessageId` in `models/Native/Activities.cs`), and a grammar tenant is printable ASCII, so it
fits the `Idempotency-Key` header value too. A caller name with characters outside atext already needs encoding there
today (`EffectKey.cs` states the fit holds "when those ids do"); the tenant part adds no such character. The receiver
does see the tenant id: tenant ids are not secret, and a host that must not show tenant names to receivers gives its
tenants opaque ids, which the grammar allows.

**Key widths (from the EF model, not measured).** `Q` adds at most 65 characters, all single-byte ASCII:

| Provider | Limit | Left for the caller's name |
|---|---|---|
| SQL Server | `Messages.Id` `nvarchar(450)` (`EfStore.cs`); clustered composite keys of `Subscriptions (Key, Instance, Wait)` and `Events (Key, Id)` within 900 bytes, 450 UTF-16 units in all | 385 for a message id; 380 for an actor id `{vp}/Q`; `Subscriptions` 385 for raw key, instance id and wait together, 315 when the instance has a caller key; `Events` 385 for raw key and event id together |
| MySQL | `Messages.Id` 255 characters (`EfStore.cs`) | 190 for a message id; 185 for an actor id. The composite keys use the provider's default key width, recorded at K4 |
| PostgreSQL | No column limit; a B-tree index entry is at most about 2,704 bytes | Composite keys stay within that, 65 bytes less for each qualified column |
| SQLite, MongoDB | None that `Q` reaches | Not limited by `Q` |

| Name | Chosen by | Stored as | Unique within |
|---|---|---|---|
| Caller-key actor id | Caller | `{vp}/Q(t, key)`, with `vp` the hash of `Q(t, key)` over 1,024 | The store, so the tenant |
| Generated actor id (child, event start, job and plan actors) | Kernel | As today (issuer partition, epoch and ordinal; event sequence) | The store, by construction |
| Job queue actor id | Kernel, from caller names | `queue/Q(t, name)/{shard}` (job workers' `queue/{tenant}/{name}/{shard}`, made injective) | The store |
| Outside message id, operator operation id | Caller | `Q(t, id)` in ordinary scope zero | The store, so the tenant |
| Keyed request wire id | Caller | `key/` + SHA-256 over `t`, key and operation | The store |
| Message id a turn sends | Kernel | Derived from sender id and revision, as today | The store, since sender ids are |
| Correlation key (subscription and event) | Caller or model | `Q(t, key)`; a sensitive key is `Q(t, "h:" + HMAC)` with the per-tenant key (data lifecycle) | The store |
| Outside event id | Caller | `(Q(t, key), id)` | The qualified key |
| Value row | Kernel | `(instance, digest)`; the digest is keyed per tenant where erasure needs it | The instance |
| Effect key | Kernel | `{instance origin}/{node}/{iteration path}/{attempt}`, as decided | The store, since the origin is the store-unique id with its creation |

**The kernel's amendment to ADR 0097 (a) (decided, 17 November).** 0097 (a) puts the bound scope in every
tenant-keyed key. For the kernel: names a caller chooses are tenant-qualified, so the scope is in their keys; ids the
kernel generates (children, event starts, job and plan actors), journal positions and value rows stay unique across
the store, carry a `tenant` column, and the kernel checks that column (next section) in place of a scope in the key.

Two consequences need no further rule:

- **Effects.** Tenants A and B starting caller key `invoice-9` get ids `{vp}/a_invoice-9` and
  `{vp'}/b_invoice-9`, so their effect keys differ. A deduplicating receiver never drops B's request as A's repeat.
- **Placement.** The hash runs over the qualified name, so a tenant's caller keys spread over the virtual partitions
  independently of another tenant's.

Host-wide records carry no tenant (ADR 0097 (e): cluster membership is host-wide): `partition` rows (fence, lease,
position, backlog), `node` rows and the projector's `projection` cursor. A virtual partition's journal holds rows of
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
| Keyed start or command | Bound scope | `{vp}/Q(t, key)`; outside id `Q(t, id)` | The command's permission in the tenant | Duplicate: the tenant's own earlier answer. Another tenant's equal key is a different name and never answers |
| HTTP trigger | Host or route (ADR 0097 (c)) | The route table is per tenant | The route's own rule | No route for this tenant: 404 |
| Ingress adapter (broker, webhook, schedule) | The adapter's binding, one scope per configured source, or a trusted header verified against the adapter's authenticated identity | `Q(t, source event id)` | The adapter's grant for that tenant | Unbound or mismatched: refused and logged host-side, not inserted |
| Outside publish | Bound scope | Event `(Q(t, key), id)` | Publish permission | Matches only `Q(t, key)` waits and the tenant's own event-start subscriptions |
| A turn's send, start, publish, subscribe, job, timer | The instance's row | Derived ids; qualified names for caller-chosen keys | Kernel-internal | A target under another tenant is a missing target (above) |
| Event match | The event's qualified key | The match tuple, whose key names the tenant | Kernel-internal | None: other tenants' waits are never candidates |
| Reply to a synchronous request | The request's bound scope, recorded in the reply address | One reply address per request | The owner sends output only to reply addresses on messages in its instance's inbox; the receiving node checks the reply's tenant against the waiting request's | A mismatch is dropped and counted, never written to a response |
| Instance list, get, viewer (rows and journal tail), parked work, plan outcomes, inbox inspection | Bound scope | Read-model rows keyed by tenant | Read permissions; payloads need their own (observability) | Another tenant's id: 404, the same status and body as a missing id |
| Recover, retry, discard, suspend, resume, terminate, delete, migrate, plans | Bound scope | Operation id `Q(t, op)` | Separate operator permissions per tenant, audited (observability) | Another tenant's instance: not found, as above |
| Admission (rates, `InboxFull`) | Bound scope | Token buckets per tenant | None | `RateLimited` is per tenant. `InboxFull` is per virtual partition and tenant-blind; it names no instance, key or other tenant |
| Metrics and traces | Host-wide | Not applicable | Host monitoring | Tenant appears as a capped attribute only where a row says so; no payload, key or id |
| Host administration: release a partition, drain a node, force a handover, initialize or migrate the schema | Host-wide, no scope | Not applicable | Host-admin permissions, distinct from any tenant role: the kernel's proposal; ADR 0097 (d) defers that distinction to its Phase 3 | Touches `partition` and `node` rows only; never reads or changes tenant rows' content |
| Cross-tenant support | ADR 0097's support context (Phase 3): target-bound, expiring, audited, one tenant at a time | As the target tenant | The support profile's grants for that tenant | No wildcard, `*` tenant or permission that implies every tenant. Before Phase 3 there is none |

**Global operator exceptions** are exactly the host administration row: it acts on infrastructure records, under host
permissions, and sees no tenant data. Every other operator action is a tenant operation in a bound scope. A host
administrator who must inspect a tenant's work enters that tenant through the support context once it exists.

## Release 1

- Release 1 implements this contract for the kernel whatever the timing of ADR 0097's own phases. When the product
  resolver is not ready, the fixed-tenant scope source binds one tenant, and every check above still runs.
- **Storage:** the shared-store discriminator, ADR 0097's v1 baseline: one database, tenants sharing the 1,024 virtual
  partitions, the tenant in every tenant-keyed name and as a column. Database per tenant (ADR 0097's opt-in hybrid,
  its Phase 5) maps to one kernel per tenant store, each with its own partitions and ownership; see "Decisions".
- **Changes for K3:** the tenant column on every tenant-keyed record, `Q` at every ingress and in the keyed-request
  hash, qualified correlation keys, the tenant check on load and consumption, read-model indexes led by tenant, and
  the conformance cases below. Fairness's tenant column on the instance row is part of this.

## Acceptance cases

Each runs in the store conformance suite on the in-memory store first (K3), then on PostgreSQL, SQL Server, SQLite,
MySQL and MongoDB (K4), unless the milestone says otherwise. "A" and "B" are two tenants of one database.

| Case | Scenario | Pass when | Milestone |
|---|---|---|---|
| T1 | The contracts compile against `BoundScope` | No ingress method takes a tenant string; `ITurn` exposes the tenant read-only and `Send` has no tenant parameter; the architecture guard fails a layer 3 reference to `Q` or the tenant column | K2 |
| T2 | A and B add an outside message with id `op-1`; then A retries | Both are delivered once; A's retry gets A's duplicate-key answer; B's row is untouched | K3, K4 |
| T3 | A and B subscribe to `order-17`; A publishes, then B publishes; one wait asks for kept events | A's event reaches only A's waits and B's only B's, kept events included; the match tuples differ | K3, K4 |
| T4 | A and B each have an event-start subscription on `order-17` | Each publish starts one instance, in its own tenant | K3, K4 |
| T5 | A and B start caller key `invoice-9`; each sends an HTTP request and an e-mail through the effect-key activities, against one deduplicating fake receiver | Two instances with different ids; the fake receives two distinct `Idempotency-Key` values and two `Message-ID`s, and applies both | K6 |
| T6 | B lists, gets, views (rows and tail), inspects the inbox of, and reads parked work of A's instance by its id | Every call answers as for a missing id: same status and body; no row of A is returned | K9 |
| T7 | A turn of A sends to, and starts with, a generated id that belongs to B | The message is dropped and journalled in A's tenant; B's row, inbox and journal are unchanged | K3, K6 |
| T8 | Two routes with the same path, resolved by host to A and B; a resume for each owned by another node | Each response reaches its own caller; a reply address altered to the other tenant is dropped and counted | K8 |
| T9 | A and B send `kernel.recover`, `discard` and `terminate` with operation id `op-7`; an operator of B targets A's instance | Both tenants' commands apply once each; B's attempt on A's instance answers not found and changes nothing | K11 |
| T10 | A bursts until `RateLimited` and its virtual partitions pass the threshold | B's rate is unaffected; every refusal B sees names no key, id or tenant of A | K11 |
| T11 | An ingress call without a bound scope; a claimed tenant that differs from the bound one; a message row and an instance row with no tenant | Refused (none bound), 403 (mismatch), refused and reported by the owner and the sweeper (no tenant) | K3, K11 |
| T12 | Host administration forces a handover and drains a node while A and B have work | Only `partition` and `node` rows change; both tenants' work continues under the new owner | K5, K11 |
| T13 | Simulation: two tenants with colliding operation ids, caller keys and correlation keys in the mix; mutations `TenantlessOutsideId`, `TenantlessCorrelationKey` and `SkipTenantCheckOnLoad` | The invariant "no delivery, match, read or effect crosses tenants" holds; each mutation is caught | K3 |
| T14 | T2 to T11 in Embedded with the fixed tenant, and in Cluster with two nodes and two tenants | All pass on both profiles | K12 |
| T15 | The binder is given `Acme`, `acmé`, `acme_x` and a 65-character id; then T2 and T3 run on SQL Server (default case- and accent-insensitive collation) and MySQL (`utf8mb4_0900_ai_ci`) with tenant pairs `ab`/`a-b` and `a`/`a-b`, and names built to meet if `-` or `_` were ignored (`Q(a, b_x)` = `a_b_x` against `Q(a-b, x)` = `a-b_x`) | The four ids are refused at the binder; every pair stays two rows, two matches and two answers | K4 |

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

ADR 0097's own questions (Q3 tenancy model, Q4 no agnostic tenant, Q7 support context) are pending owner there. This
page follows their recommended defaults; decisions 4 and 5 touch 0097's section (a) and its tenant catalog, and 0097's
author carries them into that ADR.

## Contract and evidence

ADR 0097 ([PR #4156](https://github.com/elsa-workflows/elsa-foundation/pull/4156)) is the product contract this page
follows. The [storage model](storage-model.md#inbox-identity-and-public-names) gives today's identity domains,
[fairness and backpressure](fairness-and-backpressure.md) the per-tenant admission and turn selection,
[observability](observability.md#recover) the operator actions, and [data lifecycle](data-lifecycle.md) the per-tenant
keys for erasure. Nothing on this page is built or measured.
