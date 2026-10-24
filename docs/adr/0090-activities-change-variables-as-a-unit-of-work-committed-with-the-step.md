# Activities Change Variables As A Unit Of Work Committed With The Step

Status: proposed (2026-10-24). Sipke Schoorstra, the decision holder, chose this design in a brainstorming session
on 2026-10-24, started from the review of spec 266
([#3286](https://github.com/elsa-workflows/elsa-foundation/pull/3286)). Acceptance needs agreement with Joey Barten
(@j03y-nxxbz) and Frans van Ek (@FransVanEk), because it would overturn parts of an accepted ADR (0045 §4, §5 and
§6) and changes what spec 266 analyses.

Part of epic #3052. Implementing it needs an active program whose scope covers runtime value flow
([program-goal registry](../program-goals/README.md#registry)). If accepted, it amends
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §4, §5 and §6 and
[ADR 0089](0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md) (reads and writes in
RunJavaScript), and partly reverses ADR 0045's supersession of ADR 0030's JavaScript write-back. Builds on
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) (scoped frames and
captures), [ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md) (read-only iteration entries) and
[ADR 0083](0083-javascript-binding-grammar-is-pinned-at-publish.md) (statement bodies).

## Context

Today a workflow variable is written by an output capture (ADR 0045 §4, issue #1004), by the Set, Merge and Reduce
intrinsics, and, outside workflow logic, by an operator alteration (`ModifyVariable`). ADR 0045 made that deliberate:
"Activity completion and expression evaluation do not silently write variables", "explicit `Set` nodes remain the
only normal variable-write mechanism", and stateful scripts "return typed results" that "can feed an explicit
`Set`". On `main` this holds:

- Ordinary activity code (`ActivityExecutionContext`) has no variable API. Only structural activities that carry the
  `IRuntimeScopedVariableReader` marker can read committed variables by name, and nothing can write them.
- RunJavaScript receives only `args` and cannot read or write workflow variables.
- The JavaScript engine (`IsolatedJintEngine`) is a closed JSON value world with no .NET interop.

The model made every write visible in the graph, which keeps replay, value-flow validation and spec 266's ordering
analysis exact. It also costs authors:

- A script or activity that updates several variables must return an object and add a capture or a `Set` per
  variable.
- Reading an object and changing one property, which is ordinary in C#, is impossible: there is nothing to change.
- Elsa 3's JavaScript `setVariable` and generated `set{Name}()` are refused by the converter (spec 247), and Elsa 3's
  C# `context.SetVariable` has no Elsa 4 equivalent.
- Module authors cannot hand a script a live .NET object, such as a service or a client, to call methods on.

Elsa's principle is to provide guardrails and diagnostics, not to restrict what the engineer builds (review of
spec 266, 2026-10-23). The question is how activities can change variables without losing what made the write model
safe: atomic commits, replay, and a graph that shows who writes what.

## Terms

- **Step**: one invocation attempt of one activity, from its invoke (or resume) to its next transition: complete,
  suspend, fault or cancel.
- **Variable session**: what a step holds for each variable it touches: the value it read (its *base*) and, unless the
  read was read-only, a *tracked* instance.
- **May-write set**: the variables publish determines a step can change.

## Decision

### 1. Activities may change variables; input-binding expressions stay pure

RunJavaScript and C# activities may read and change workflow variables while they execute. Input-binding
expressions (the JavaScript or Liquid that computes an activity input) stay pure, exactly as ADR 0045 §5 says.

*Why.* A step's effects can commit together with the step, as captures already do. An input-binding expression can
be evaluated more than once: on replay, on each `While` pass (#977), in preflight or inspection. A write from an
expression would run a variable number of times. That is the hazard ADR 0045 §5 exists to prevent, and it does not
apply to a step.

### 2. A step is a unit of work over the variables it touches

The promise to authors, the same for scripts and C#: **change what you read, or assign what you did not read; the
change is saved when the step commits, and nothing is saved if the step faults or is cancelled.**

- A read returns the variable's committed value at the moment of the read, during the step's invoke or resume. It
  is never the step's pinned input snapshot, which can be older. This is the rule `IRuntimeScopedVariableReader`
  already follows.
- A read returns a tracked instance. Reading the same variable twice in one step returns the same instance, so
  aliasing behaves as it does in C#.
- Assigning a new value and mutating the tracked instance (`order.total = 1`, including through aliases and helper
  functions) are both changes.
- Later reads in the same step see the step's own changes (read-your-writes).
- A *read-only read* skips tracking, like EF Core's `AsNoTracking()`, for large values the step does not change.
- Loop items and indexes (ADR 0028) and withheld secret values are never tracked. Loop items are frozen, so mutating
  one throws. A withheld value is refused with `VF-ACT-010`, as `IRuntimeScopedVariableReader` refuses it.

*Why.* This is what a .NET developer already expects from EF Core's `DbContext` and `SaveChanges`, so there is no new
rule to learn, and it gives "change now, apply at the right time" without partial writes. It is feasible because a
durable variable is serializable by definition (that is how it is stored), so "did it change?" is a comparison of
serialized forms, which is how EF Core's snapshot change tracking works. Reading committed values at invoke, not the
pinned input snapshot, keeps a step from building on a base another branch has already replaced.

### 3. RunJavaScript reads and writes variables by name, resolved at publish

This gives RunJavaScript a variable surface it does not have today. `variables.x`, `variables['x']` (a string
literal) and `getVariable('x')` read; assigning to them, `setVariable('x', v)` and mutating a read value write.
Compound forms (`variables.n += 1`, `variables.n++`) are a read and a write. The runtime does not provide generated
`set{Name}()`; the converter rewrites it to `variables.Name = v`, as ADR 0089 decision 6 does for `get{Name}()`.

At publish, the same parser and nearest-scope rule as ADR 0089 resolves every touched name to its declaring scope and
pins that target (scope and key). The value itself comes from the session at run time (decision 2), not from a
pinned parameter snapshot. Publish refuses:

- a computed name in any form (`variables[k]`, `getVariable(k)`, `setVariable(k, v)`, or passing `variables`,
  `getVariable` or `setVariable` around as a value), as ADR 0089 decision 5 does;
- an undeclared name (decision 5);
- an assignment to, or a direct nested assignment into, a read-only target (a loop item or index).

The may-write set is every variable the script assigns, mutates, or lets escape into a local, a function argument or
a closure, because each of those can mutate it. Read-only targets are frozen, so their escapes are not writes.

*Why.* Resolving at publish keeps one resolver for reads and writes, keeps "which nodes touch this variable?" exact,
and gives spec 266 a sound write set. Counting escapes as possible writes is conservative: it can add warnings for a
script that only passes a variable to a helper, but it never misses a write.

### 4. C# activities use bound variable references by default and free names by opt-in

- **Bound references (default).** An activity declares an input of type `VariableReference`, which the workflow
  author binds in Studio as they bind a capture target. `context.Variables.Get<T>(target)` returns a tracked copy;
  `Set(target, value)` assigns; `GetReadOnly<T>(target)` is the read-only read. Publish refuses a reference that is
  unbound, bound to an undeclared variable, or used for writing while bound to a read-only target.
- **Free names (opt-in).** `Get<T>("name")`, `GetReadOnly<T>("name")` and `Set("name", value)` work only on an
  activity type marked `[AccessesUndeclaredVariables]`, as by-name reads today work only for activities marked
  `IRuntimeScopedVariableReader`. The marker is part of the activity descriptor, so publish, Studio and inspection
  show it. Names resolve at run time to the nearest visible scope. On an unmarked activity a free-name call throws,
  which faults the step. Spec 266's rule must treat a marked activity as touching every visible variable.

*Why.* Bound references keep the workflow author in control of what an activity reads and writes, and keep both sets
static, like captures. Free-name reads hide the read set just as free-name writes hide the write set, and spec 266
and policy taint need both, so reads and writes follow one rule. Free names serve Elsa 3 habits and generic module
activities; the marker makes the loss of static knowledge visible instead of hidden. *Rejected:* free names only
(static analysis would be blind to every C# access); bound references only (unlike Elsa 3, and too strict for
generic module activities).

### 5. Undeclared variables are refused

A script that names an undeclared variable fails publication. A free-name C# access to an undeclared name faults the
step before its commit. Nothing is created implicitly.

*Why.* An Elsa 4 variable has a declared type, storage driver and policy. Elsa 3's `setVariable` created variables
on first write; an auto-created Elsa 4 variable would have none of these.

### 6. Changes apply in the step's checkpoint commit

When a step completes or suspends, each tracked variable is serialized with its storage driver and compared with the
serialized form of its base. Each changed variable becomes a whole-value write intent. Intents apply in the same
checkpoint commit as the step's transition, outputs and captures, with the scoped frame resolution and assignment
validation that ADR 0048 asks `Set` and captures to share.

- **Fault and cancel.** A failed type check, an undeclared free name or a serialization error faults the step before
  its commit. A faulted or cancelled step saves nothing.
- **Retries.** A new attempt is a new step with a new session, so it reads committed values again.
- **Replay.** ADR 0045 §3 says "a committed invocation is not re-executed merely because downstream scheduling was
  interrupted". A coalesced burst that is re-driven after a crash re-runs only steps whose commit never became
  durable, and their intents were lost with that commit, so intents never apply twice.
- **Suspension.** Changes up to the suspension commit with it. The resumed step is a new step with a new session.
  Pending intents are never persisted.
- **Intents and captures on one variable.** Within one commit, intents apply first and captures last, so the capture
  wins. Publish warns when a step's may-write set and one of its own captures name the same variable.

*Why.* Applying intents with the step's own commit means there is no moment where the step has completed but its
changes are missing, or the reverse. A capture is an explicit edge the workflow author drew, so it states the
author's final intent for that variable and takes precedence over what the activity code decided. *Rejected:*
turning each intent into a `Set` scheduled after the step (a separate commit, so another branch can commit in between
and a write can fail after the step succeeded); a post-commit effect journal (eventually consistent, so the next
activity can read stale values).

### 7. Steps that commit outside their read's work item check read-then-write conflicts

An inline step reads and commits within one drain work item (its invoke or resume), and the drain applies one work
item at a time, so nothing else commits between its reads and its commit. A step that reads and commits in different
places, such as one placed on a background worker (`ActivityExecutionPlacement.Background`), can see other branches
commit in between. For such a step, a variable it read (tracked or read-only) and then changed is checked at commit:
if the variable's committed serialized value no longer equals the step's base, that is a conflict. The default is to
fault the step with a message naming the variable. An activity type may declare a different default
(`[VariableConflictPolicy(LastWins)]`), and the workflow author may override it per node.

A blind write (assigned without being read) is never a conflict. It follows the same concurrent-write rule as every
other write: ADR 0045 §4 refuses potentially concurrent writes at validation today, and spec 266 (#3286) proposes a
warning with last-commit-wins instead.

*Why.* A stale read-modify-write (`count++` on two background branches) loses data silently, which is worse than an
undefined order. Comparing the variable's own value, not the frame revision, avoids false conflicts: the revision
counts every write to every variable in the frame, so a write to an unrelated workflow variable would fault the
step. Faulting is EF Core's default (`DbUpdateConcurrencyException`). Some activities, such as aggregators, tolerate
last-wins, so the choice is per activity. *Rejected:* re-running the step automatically, which is unsafe for
activities with external side effects; the frame revision as the token (frame-wide false conflicts).

### 8. Live .NET objects reach scripts only through opt-in host interop

By default RunJavaScript keeps today's closed JSON world. A host may switch on host-interop mode, which is off by
default. In that mode a RunJavaScript activity can read transient variables as live .NET objects and call their
methods. Such an activity carries a distinct host-interop mark in its descriptor, which publish and Studio show, and
runs under ADR 0045's transient execution policy: it "cannot suspend, migrate, or depend on durable retry". ADR 0089
decision 5's publish refusal of transient reads does not apply to it. Transient variables are never tracked or saved:
a live object is changed in place and nothing is committed.

*Why.* The closed world exists for good reasons: sandbox limits (timeout, memory, statements) do not bound a .NET
call; live objects do not survive suspension; and method calls have side effects the engine must know about.
Forbidding the case outright would restrict a legitimate need. Opt-in interop, with the activity marked for what it
is, tells the truth about the consequences instead. A dedicated mark is needed because `External`, the default
side-effect profile of every unmarked activity, does not distinguish it. *Rejected:* closed world only (restricts a
legitimate need); curated host functions only (useful later for portable functions, but they cannot call methods on
the author's own objects).

### 9. What does not change

Output captures, the Set, Merge and Reduce intrinsics, operator alterations, pure input bindings, and outputs as
typed results.

## Consequences

- **ADR 0045.**
  - §5 keeps input-binding purity. Its "Stateful scripts … return typed results" stays true; a script may now also
    change variables. Its supersession of ADR 0030's "JavaScript variable write-back" is partly reversed for
    RunJavaScript only, now as a declared write form committed with the step rather than ambient write-back.
  - §4's "activity completion … do[es] not silently write variables" and §6's "explicit `Set` nodes remain the only
    normal variable-write mechanism" are superseded: a step's changes are a third write form beside captures and
    `Set`. They are declared, not silent: the may-write set is published, and a marked free-name activity is shown as
    touching every variable.
  - §6's "does not publish successful values or outcomes through mutable execution-context `Set` methods" still holds
    for outputs and outcomes. `context.Variables.Set` is the one sanctioned exception for variables: it records an
    intent, and the transition still returns its typed result. `context.Variables` is not a service locator.
  - §4's concurrent-write rule applies to step writes as to every other write (decision 7).
- **ADR 0089.** Its "scripts do not write variables" and its open question on writes would be superseded. Its read
  model covers input-binding expressions; RunJavaScript gets the session reads of decision 2 instead of pinned
  parameter snapshots. Its decision 5 transient refusal has the host-interop exception of decision 8.
- **Spec 095.** Its expression-and-import contract says stateful scripts "use a separate graph-visible `Set` intrinsic
  when a variable must change". That stays true for input bindings and becomes optional for activities.
- **Spec 266 (#3286).** Its rule must add step writers through the may-write set and treat a marked free-name
  activity as touching every visible variable. Its rationale becomes precise: static analysis sees every write except
  those of marked free-name activities. Decision 7 uses value comparison, not the frame revision, so spec 266's
  ADR 0048 §4 amendment does not conflict with it.
- **Converter (spec 247 follow-up).** Inside RunJavaScript, Elsa 3 `setVariable`, generated `set{Name}()` and
  mutation of a read value (`getVariable('o').x = 1`) become convertible. In expressions they stay refused.
- **Inspection.** Each commit records which variables a step changed, which is the data #3290 (variable write
  history and the winning write) needs.
- **Cost.** One serialization per tracked variable per commit, and one more comparison per conflict-checked variable.
  Read-only reads avoid the first. The implementation spec measures it against a large payload.

## Alternatives considered

**Keep the current model (captures and `Set` only).** Exact and simple, but it keeps the costs listed in Context.
Rejected as the only path; it stays the right choice for most activities.

**Values: variables are immutable values, changed only by assigning.** Cheaper than tracking, and scripts would get
a clear error on mutation. Rejected because C# authors expect to mutate what they read, and a rule they must learn
is a source of confusion.

**Live store, as in Elsa 3.** Variables are shared live objects, written immediately. Impossible with durable,
distributed execution: the next step may run on another node from the serialized value.

**Field-level JSON patches instead of whole-value intents.** Would let two branches change different fields of one
variable. Rejected for now: array patches by index land in the wrong place after another branch changed the array.

## Required evidence for the implementing spec

- A faulted or cancelled step saves nothing; a retry reads committed values again; a re-driven burst never applies
  an intent twice.
- A step reads committed values at invoke, not its pinned input snapshot.
- Read-your-writes in scripts and C#, including nested mutation through an alias.
- An unchanged value writes nothing; a changed nested field writes the whole value; a read-only read never writes.
- Publish refuses computed, undeclared and read-only targets and unbound references, and the may-write set feeds
  spec 266.
- A background read-then-write conflict faults by default; a write to an unrelated variable in the same frame is not
  a conflict; the type default and the node override work; blind writes follow the concurrent-write rule.
- Host interop is off by default; when on, the activity carries the host-interop mark, cannot suspend, and is not
  retried durably. ADR 0045's transient execution policy has no implementation yet, so the spec builds it.
- Elsa 3 `setVariable`, `set{Name}()` and nested mutation convert inside RunJavaScript and stay refused in
  expressions.
- Serialization cost is measured against a large payload.

## Open questions

1. **Script read-only read.** The JavaScript form of `GetReadOnly` (for example `readVariable('x')`).
2. **Studio.** Whether nodes show their may-write set, and how a marked free-name activity is flagged.
3. **Interop scope.** Whether host-interop mode is switched per host or per workflow, and whether it needs per-type
   allow-lists.
4. **Assigning a transient variable.** Whether a host-interop script may assign a new live object to a transient
   variable (in memory only), or only use the one it was given.
5. **Liquid.** Liquid templates stay read-only; whether Liquid needs any write form is out of scope.
