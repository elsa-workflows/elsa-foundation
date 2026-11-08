# #3994 QA R2 composed reproduction

Use this file against the **new collector + freshness head** (SHA is the PR tip after the additive bot commit). Do not treat helper-only tests as composed proof.

`{}` still reseeds amount `0`. Untouched-default policy is unchanged and still a product question on #3976.

## Automated isolated routes (must pass; not composed-host proof)

From `Studio/` with the decision-inputs Playwright project:

| Item | URL | What must happen |
|---|---|---|
| 9 | `/?mode=decision-inputs&theme=light` | Version 2 amount Text. Fill `951` in the **amount** textbox. Authored JSON `kind` becomes `"string"`. Review Ready. |
| 11 | same | `{}` reseeds `0`; invalid `[` is `aria-invalid`; blur restores last valid JSON. |
| 14 | `/?mode=decision-inputs&theme=light&unpublished=1` then Publish decision | After **window focus**, picker drops “no published version” and lists Version 1. |
| 14 | same, then pin `@latest` and **Publish table with region** | After **window focus**, picker lists **Version 3**, `region` is visible, authored JSON has no `region`, amount stays `kind:number`, footer + Review **MissingInput**. |
| 15 | `/?mode=decision-inputs&theme=light&failOnce=1` | Review blocked on lookup; Recover lookup; Review Ready. |
| 16 | `/?mode=decision-inputs&theme=light&unrelated=1` | Notify activity, `ops@example.com`, Review Ready, no `/ops` lookup. |
| 17 | `/?mode=decision-inputs&theme=light` | Empty amount → **Open expanded Inputs editor** (visible labeled row button) opens `role=dialog` named **Inputs**. Close. Inline + footer + Review stay InvalidValue. |

Suite: `pnpm --dir Studio exec playwright test tests/browser/decision-inputs.spec.ts`

## 9 — Text fill must rewrite stored `kind:number`

1. Bind Evaluate Decision to a published table whose **Version 2** amount is Text.
2. Confirm inspector + footer + Review show **InputTypeMismatch**, Advanced JSON still `"kind": "number"`.
3. Type `951` in the **amount** field (the labeled text box, not Store as table types).
4. Advanced JSON must become `"kind": "string", "value": "951"`. Review must be Ready once extras are removed.

Do not use Store as table types for this check. Store is a separate explicit action.

## 11 — `{}` / invalid JSON

Unchanged: `{}` reseeds `0`; invalid `[` is aria-invalid; blur restores last valid JSON.

## 14 — same-session `@latest` freshness (editor + picker + Review)

Stay on the workflow. Publish in a **second tab**. Bring the workflow tab forward (window focus / tab visible).

- Unpublished `@latest`: picker, footer, and Review must drop **no published version**.
- Published table A `@latest`: after a new version is published, the version dropdown must list it without remounting the workflow. Amount stays Number with **InputTypeMismatch** if the new table changed amount to Text, until you edit or Store as table types. Review must re-resolve `@latest` (MissingInput if the new table added a clause you did not author). No silent kind conversion and no silent seed of the new clause.

## 16 — unrelated `Decision=ops@example.com`

**Automated (must pass):** Playwright `/?mode=decision-inputs&unrelated=1`

Notify activity, authored `ops@example.com`, Review Ready, no `/ops` lookup.

**Composed host (Code/Apply monaco is not the supported route):**

1. Activity definitions → **Create activity** named `Notify mailbox`.
2. Add one input: name `Decision`, type String. Do **not** pick a decision-version editor / ui hint.
3. Publish that activity.
4. New flowchart workflow → add **Notify mailbox** → set Decision to `ops@example.com`.
5. Review & publish. It must not mention InputMembership / Unavailable and must not GET `/decisions/ops`.

Optional Code tab: copy `unrelated-decision-mailbox.workflow.json`, replace `__FLOWCHART_ACTIVITY_VERSION_ID__` and `__NOTIFY_MAILBOX_ACTIVITY_VERSION_ID__` with ids from an exported workflow that already contains those activities, then **Apply**. Do not use monaco if the document is blank.

## 17 — expanded Inputs dialog (actual unmount)

Supported control (visible text, not the 13px overlay icon that previously covered the fields):

- Accessible name: **Open expanded Inputs editor**
- Visible label: **Open expanded editor**
- Under the Inputs field on Evaluate Decision (`button.wf-property-expand-row`)

Click it. A modal **dialog** named **Inputs** must appear (`role="dialog"`, heading Inputs). InvalidValue / InputTypeMismatch inside the dialog counts. **Close Inputs editor**. The inline row and canvas footer must still show the same blocking error. Review must stay blocked. Escape on a control that never opened a dialog is not this test.
