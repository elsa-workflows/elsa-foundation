# FastEndpoints Transition Exception Contract

## Registry

`tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` is the canonical bounded inventory of first-party FastEndpoints registrations allowed during migration.

Each entry must contain:

- exact registration identity;
- endpoint owner;
- normalized routes and methods;
- removal owner;
- open follow-up issue;
- an explicit assertion that the surface is not dynamically unloadable.

## Reconciliation

The source scanner and runtime manifest jointly prove:

- every discovered first-party FastEndpoints registration matches one exception;
- every exception matches one discovered registration;
- the routes, methods, and owner match exactly;
- no dynamically unloadable module uses FastEndpoints;
- a new, expanded, ambiguous, or stale entry fails deterministically.

The registry is transitional evidence, not an authorization to add legacy endpoints. Each production migration removes its matching entries.
