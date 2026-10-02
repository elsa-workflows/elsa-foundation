# Elsa.Samples.Nuplane.Notes.Activities

A sample workflow activity for an Elsa server that loads packages from a feed: "Add note" writes a note through the
`Elsa.Samples.Nuplane.Notes` module, and is released with it. Release 1.0.0 has a `Text` input; release 1.1.0 adds an optional
`Tags` input, which it refuses while the Notes module's schema version 2.0.0 is not finalized.

It is a demonstration, not a library. Its source and walkthrough are in the Elsa Foundation repository, under
`samples/Elsa.Samples.Nuplane.Notes.Activities`.
