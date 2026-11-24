# Contract: porting catalog entry and sample regions

## Entry shape (`porting-catalog.md`)

```markdown
## Case NN: <title>

**Verdict**: port | redesign | port (<part>); redesign (<part>)

**Applies when**: <what in the Elsa 3 source triggers it>

**Search**: <the focused search>

**Before (Elsa 3)**
```csharp
<Elsa 3 example>
```

**After (Elsa 4)** <!-- sample: case-NN -->
```csharp
<verbatim text of the sample region case-NN>
```

**Steps**: <numbered patch steps>

**Redesign**: <alternatives; only when the verdict has a redesign part>
```

Cases appear in order 01–19. The marker comment `<!-- sample: case-NN -->` appears only on *port* parts.

## Sample regions

In `extensions/Elsa3/tests/Migration/**/*.cs` (the sample and its tests):

```csharp
// catalog:case-05 begin
...
// catalog:case-05 end
```

A case has exactly one region. Regions may nest or overlap (cases 1–5 share one activity); marker lines of other
regions inside a region are removed before comparing. The region text is compared after removing the common
leading indentation.

## Check (`tools/skills/check_migration_skill.py`)

Fails when:
1. a catalog block marked `sample: case-NN` differs from region `case-NN`;
2. a region has no catalog block, or a block has no region;
3. a case number is missing, duplicated or out of order;
4. any file in the skill folder contains an Elsa 3 conversion or value-flow code literal: a token starting with
   `ELS3-` or `VF-` (other code families are left to review; examples use `<code>`);
5. any file in the skill folder names an AI vendor or model.

`--self-test` runs the check against built-in good and bad inputs.
