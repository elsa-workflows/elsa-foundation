# Elsa 3 storage drivers: what to do after conversion

In Elsa 3, each workflow variable, input and output named a storage driver: where its value lived while the workflow
ran. Elsa 4 converts every one of them to its default storage, `elsa.json`. The value is then stored with the workflow
instance. For most workflows that is the same as before. It is not the same for two kinds of driver:

- **Memory**: Elsa 3 never persisted the value.
- **A custom driver**, for example one that reads and writes Azure blobs: Elsa 3 kept the value outside the workflow
  instance.

Somebody chose these drivers on purpose. The converter cannot know why, so it converts the member and reports a warning.
This guide is the conversation to have with the workflow's owner. Its goal is to find what they wanted the driver to
achieve, then reach that goal with what Elsa 4 offers today. It is written as questions because no list can predict
every reason. When the answer is a need Elsa 4 does not cover yet, the conversation ends with a plan or spec for the
owner's own solution, not a workaround.

## What the converter does

| Elsa 3 driver | Elsa 4 result | Diagnostic |
|---|---|---|
| None, `WorkflowInstanceStorageDriver` (the default) or the obsolete `WorkflowStorageDriver` | default storage (`elsa.json`) | none |
| `MemoryStorageDriver` | default storage (`elsa.json`) | `ELS3-BEHAVIOUR-MEMORY-STORAGE-PERSISTED` (warning) |
| any other driver | default storage (`elsa.json`) | `ELS3-BEHAVIOUR-CUSTOM-STORAGE-DRIVER-REPLACED` (warning) |

The same rule applies to workflow variables, container variables, inputs and outputs, in reusable workflows (usable as
activity) and in other workflows alike. A reusable workflow's contract publishes the member as `elsa.json`.

Each warning carries structured metadata, so tools never parse the message:

- `VariableName`, `InputName` or `OutputName`: the member;
- `StorageDriverType`: the Elsa 3 driver type as exported, trimmed (for example `Acme.BlobStorageDriver, Acme`);
- `ComponentKind`: `storage-driver`, and `ComponentName`: the same driver type. The migration report and the migration
  skill find storage drivers through these two keys, Memory and custom alike.

Both codes are `ELS3-BEHAVIOUR-*` warnings. The workflow converts and publishes, and the migration report files the
warning under *Reconsider*: the run goes on, but the owner has a decision to make before the workflow runs in
production.

## When to start the conversation

Start once per driver, not once per member: list every member that the report flags for the same `StorageDriverType`
and go through them together. Members on one driver usually share a reason.

## The conversation

### 1. What did the driver do for you?

Ask the owner why they chose this driver, in their own words, then match the answer to one of these goals. More than
one can apply.

- **A. Never persist a secret.** Example: an API token or password held in a variable for a few steps.
- **B. Never persist bulky or short-lived data.** Example: a downloaded document, a large API response, an
  intermediate result that only one step needs.
- **C. Keep the value in another store.** Example: a blob container, for size, retention, compliance or because other
  systems read it.
- **D. No particular reason.** Example: copied from another workflow, or the driver was a team default.

For goal D, the default storage is fine. Note the decision and stop.

### 2. Goal A: a secret

Elsa 4 has no variable that skips persistence. Do not keep the secret in a variable. Instead:

- Store the secret with the Secrets module and bind the activity input that needs it to a `Secret` reference. The
  `SecretsWorkflows` feature resolves the reference each time the activity is activated. The instance keeps only a
  withheld marker, never the value. See [Elsa.Secrets.Workflows](../../essentials/src/Secrets/Workflows/README.md) and
  [spec 188](../../specs/188-workflow-secret-safety/).
- A secret cannot be a variable's starting value. If several activities need it, bind each of them to the same
  reference.
- If a script built the secret from parts, move that logic into a custom activity input or host configuration. The
  script should not assemble it into a variable.

Marking an input `IsSensitive` hides the value in views and logs, but it is still persisted. On its own it does not
meet goal A.

### 3. Goal B: bulky or short-lived data

Elsa 4 persists what a variable or output holds. To keep the data out of the instance:

- Fetch, use and drop it inside one activity: a custom activity (or a JavaScript host function called from one step)
  that reads the data, does the work and returns only what later steps need, such as a status, an id or a summary.
  The instance persists what the workflow keeps in variables and outputs. It does not persist what stays inside the
  activity.
- If later steps need the data itself, keep a **reference** (a URL, a blob name, a document id) in the variable, and
  let each step read the data through it. This is goal C with a small value in the instance.

Ask how large the value gets and how often the workflow suspends. A value that stays small and short-lived may be fine
in the default storage after all. Record that decision.

### 4. Goal C: another store

Elsa 4 cannot do this for a converted workflow today:

- A workflow's own variables and container variables keep their values in the instance: publication refuses any other
  storage driver on a variable (VF-ACT-005). A workflow's own inputs and outputs always live in the instance too.
- Durable-value storage drivers (`IRuntimeDurableValueStorageDriver`, selected by key) apply only to a reusable
  activity's top-level inputs, outputs and graph variables. Only the `elsa.json` driver ships, and the driver contract
  says encoding must persist nothing outside the workflow's own commit. A blob driver would not meet that rule.
- Proper storage-driver support is tracked in
  [#5698](https://github.com/elsa-workflows/elsa-foundation/issues/5698).

So the owner needs their own solution. Help them write a plan or spec for it before the workflow goes live. It should
answer:

1. **What is stored**: which members, value types, typical and largest size.
2. **Who reads it**: only this workflow, other workflows, or other systems.
3. **Lifetime**: when a value is written, when it can be deleted, and the retention rules that apply.
4. **Consistency**: what happens to a value that was written to the store when the workflow later faults, retries or is
   cancelled. Elsa 4 commits instance state on its own; an external write is not part of that commit.
5. **Security**: credentials for the store (use a `Secret` reference, see goal A), encryption, and access rights.
6. **Shape in the workflow**: the usual design keeps a reference in the variable and uses activities that read and
   write the store explicitly, such as custom activities or HTTP calls to the store's API. The plan should name those
   activities, their inputs and outputs, and which existing steps change.
7. **Running Elsa 3 instances**: values that an Elsa 3 driver already wrote are not read by the converted workflow.
   Decide whether running instances finish on Elsa 3 or need a data move.
8. **Later**: whether to move to an Elsa 4 storage driver once #5698 lands, and what that move would take.

Until the solution is in place, the converted workflow stores the value in the instance. Decide whether it may run like
that in the meantime, for example in a test environment only.

### 5. Record the outcome

For each driver, record the goal, the decision and the follow-up (none, a change to the workflow, or a plan or spec to
write). Then convert the workflow again after any change to the Elsa 3 definition.
