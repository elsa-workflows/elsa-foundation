# Runtime Wait Registration And Post-Commit Intent Contract

This slice introduces runtime contracts for wait registrations that are causally paired with post-commit intents.

Required guarantees:

- The runtime records a wait/correlation before delivering Elsa-caused side effects.
- Reserved waits can match early signals by correlation without a global unmatched inbox.
- Wait-dependent post-commit intents reference wait registration IDs and carry failure policy.
- Terminal wait registrations are not matchable.
- Compensation is a named wait failure policy option.
