# Data Model

No persisted entities, schema, identifiers or serialization contracts change. A command service scope owns its default Coalesced drain factory and the existing scoped committer/inner persistence collaborators. The factory still creates one working session per drain and disposes only its ambient session handle at drain end; service scope disposal owns the collaborators. Shared immutable options/policy and the AsyncLocal session accessor keep their current lifetimes. No new lifecycle state is introduced.
