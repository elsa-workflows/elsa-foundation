# Size and identity probes for the fourth gate 2 review (#4229)

A small console probe for the fourth Astra High review's size and identity findings
([review comment](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-6967291576)). It drives
the unchanged prototype code and measures what the design pages quote; it changes no default and implements none of
the proposed transitions.

| Section | What it runs | Finding |
|---|---|---|
| Event match | The real `EventMatch.Encode` (`src/Kernel.Durable/Store.cs`) for names at `N` = 1,024 WTF-8 bytes in the widest JSON escape, today's `evq:` delivery id, and, beside them, the proposed fixed digests and the length-prefixed delivery header, with its payload presence flag, and its strip (both written in this probe only) | R4-M1 |
| Subscription write | The prototype's MongoDB subscription document shape (`mongo/Kernel.Mongo/MongoStore.cs` 648–654) with the proposed tenant, creation and keep fields, at the name limits, encoded by MongoDB.Bson 3.9.0 (the version the prototype's driver locks): the document, a bulk-write insert entry and today's replace-upsert entry | R4-M2 |
| Placement | The real `InstanceRef.Spread` (`../../essentials/src/ActorFoundation/BuiltIn/Runtime/Addresses.cs`) over spellings that the pinned SQL Server and MySQL collation calls equal | R4-M6 |
| Native job arm | The real `NativeModel` through a `DispatchProxy` `ITurn` host: what `WaitEntry` and the ticket's `BackgroundArgs` hold after a background arm whose bound variable a sibling then changes, and what the real `ExecuteJobAsync` does with the arm-time arguments, fresh ones, none, and none without an effect key | R4-M3 |

The `ITurn` host follows the reviewer's probe: it records sends and job starts and answers the calls Native makes in
one turn. `EffectEcho` is a test background activity that returns its input and `ActivityContext.EffectKey`, so the
effect key the real job path passes is observable.

## Run

```sh
dotnet restore --locked-mode
dotnet run -c Release --no-restore
```

The process exits 0 when every check passes. The output of the run recorded on the PR, on .NET SDK 10.0.402:

```text
R4-M1 event match (existing EventMatch.Encode = Json.Write with DurableKeyJson, Store.cs 230-239)
  reviewer-shaped witness (wait = 800 x U+0001, short key/event/instance): Match 4868 bytes
  four names of 1024 WTF-8 bytes of U+0001: Match 24787 bytes; evq wire id 3179 bytes
  four names of 1024 WTF-8 bytes of '<': Match 24787 bytes; evq wire id 3179 bytes
  four names of 1023 WTF-8 bytes of U+754C: Match 8395 bytes; evq wire id 3174 bytes
  four names of 1024 WTF-8 bytes of U+1F310: Match 12499 bytes; evq wire id 3177 bytes
  proposed Match digest: 64 bytes; proposed delivery id: 67 bytes, whatever the inputs
  delivery header for key, event id and wait of 1024 bytes of ASCII: 3155 bytes (6310 in UTF-16)
  delivery header for key, event id and wait of 1024 bytes of U+0001: 3155 bytes (6310 in UTF-16)
  delivery header for key, event id and wait of 1024 bytes of '<': 3155 bytes (6310 in UTF-16)
  null payload: stripped null, Values.Parse -> null; empty payload: stripped "", Values.Parse -> ""
R4-M2 subscription write on MongoDB (MongoDB.Bson 3.9.0; shape of MongoStore.ToDocument, MongoStore.cs 648-654)
  stored document 6613 bytes; as a bulkWrite insert entry 6640 bytes; as today's replace-upsert entry 9905 bytes
  subscriptions in B_plan at 7,168 bytes each: 3510
R4-M6 placement (existing InstanceRef.Spread, Addresses.cs 22-27, over UTF-16 code units)
  Spread('a_Invoice', 1024) = 460, Spread('a_invoice', 1024) = 300
  Spread('a_café', 1024) = 1000, Spread('a_cafe', 1024) = 796
  Spread('a_INVOICE-9', 1024) = 254, Spread('a_invoice-9', 1024) = 926
R4-M3 Native background job (existing NativeModel.cs 129, 185-195, 347-355)
  WaitEntry for the job: {"Frame":"f4","Kind":"job","Key":"w6","Creation":0}
  ticket arguments (BackgroundArgs): {"Workflow":"job","Node":"c","Frame":"f4","Inputs":{"Service":"alpha"},"IterationPath":"-","EffectKey":"c9_0/subject_1/c/-/0"}
  the real background activity, run from the arm-time arguments: "alpha|c9_0/subject_1/c/-/0"
  a payload-free marker (no arguments): ArgumentNullException; arguments without an effect key: InvalidOperationException
  encoded sizes: WaitEntry 51 bytes, BackgroundArgs 126 bytes
ALL CHECKS PASSED
```

(The `[ok]` check lines are omitted above; the run prints 21 of them.)

## What it does not show

It runs no store: no provider insert of a digest, no key-binding row, no concurrent first use, no restart, no drain
and no catch-up. Those are the proposed cases EM-1 to EM-3, KB-1 to KB-3, SB-1 to SB-3 and JS-1 to JS-3 on the
design pages, qualified at K3, K4, K6, K9, K11 and K12.
