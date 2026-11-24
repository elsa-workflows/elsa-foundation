# Contract: migration plan template

`migration-plan-template.md` has these sections, in this order. Field rules are in [data-model.md](../data-model.md).

```markdown
# Elsa 3 to Elsa 4 migration plan

Essentials image tag: <tag> (the engine you go live on; the migration image uses the same tag)
Stage: inventory | prepare | test-migration | handoff
Export: <path>
Last report: <path> (<command>, <date>)

## Elsa 3 source
- <folder or repository>

## JavaScript prefixes
| Prefix | Functions |

## Elsa 4 features and packages
| Feature | Package | Components | In appsettings.json |

## Components
| Kind | Elsa 3 name | Uses | Source | Elsa 4 target | Cases | Tests to port | Status |

## Accepted refusals
| Definition | Finding | Reason | Date |

## Notes
```

The template carries one example row per table, marked as an example, using the synthetic `Acme` names.
