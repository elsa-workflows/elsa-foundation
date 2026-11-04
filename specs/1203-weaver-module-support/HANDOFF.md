
## CodeRabbit findings on #3757, still to address

- **Major ([thread 4465270153](https://github.com/elsa-workflows/elsa-foundation/pull/3757#discussion_r4465270153), spec around line 247).** The secret consumer check and the secret delete are not atomic. A connection can bind the secret after the consumer answer is compared and before the delete. Make PD3 require one of:
  - an atomic cross-module check;
  - a lock or version token;
  - or state the remaining window explicitly as a recorded non-atomic window, the way spec 1193 does.

  Add a negative case for a binding created in that window. Then reply on the thread and resolve it.
- **Minor (review summary).** In `docs/program-goals/README.md`, link spec 1203 from the Weaver row's current-focus entry.
