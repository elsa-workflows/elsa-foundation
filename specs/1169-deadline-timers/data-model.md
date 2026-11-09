# Data model

Persisted DurableTimer, claims and bookmarks are unchanged. The optional query returns one earliest eligible instant in the current persistence partition, or null. The local wakeup hint retains one instant and a generation. A scheduled execution owns one TimeProvider timer, one running action and disposal state. No new persisted entity or migration.
