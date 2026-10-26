# In-progress validation

This is a partial #3360 checkpoint, not delivery/completion evidence.

The optional plain SDK package compiles with zero warnings/errors. Its public options/composition/features are tracked; endpoints/models/directory reader/writer remain internal. It references Hosting/Api/Core and ASP.NET Core, with no Web/Nuplane dependency.

Whole Studio project: **528 passed, 0 failed, 0 skipped**, `studio3360-composition-fixed.trx` (1m08s). The original 511 cases remain, plus17 public-API composition cases covering independent features/core dependencies, optional absence, host authorization, alternate ContentRoot-relative paths, callback precedence and retained-object mutation, typed startup validation and duplicate/after-Build guards.

The first run passed521 and failed7 invalid-startup cases because fixture cleanup called Host.StopAsync before startup constructed its hosted-service list, masking the expected options validation exception. The fixture now stops only successfully started hosts; all7 pass in the full rerun. Successful fixture hosts stop before disposal and unused console logging is cleared.

Web still uses its existing theme code. The copied addon implementation still requires managed/read projection separation, source-removal/shadow preservation tests, final asset hardening, Web adoption/obsolete removal and existing fixture migration. Frontend/runtime, causal controls, inventory/locks/package/CI/reviews and actual resulting-main verification remain open. No PR/push/release has occurred. #3359 stays stopped and unchanged at its saved checkpoint on its separate branch.

## Managed persistence checkpoint

The private managed model/read snapshot separation is implemented in the addon. Each theme/default/visibility operation writes only managed state; directory enrichment remains in reads. Explicit source-owned deletion is refused instead of falsely reporting removal. Existing shadowed managed definitions and stored defaults excluded by the source mode are preserved.

Whole Studio project **539 passed, 0 failed/skipped**, `studio3360-managed-state.trx` (1m12s). Eleven additional public-host cases cover all three source modes, directory removal, complete shadowed-state preservation, intended save/import/duplicate/default/delete changes, deliberate adoption through an explicit save, source-owned refusal and preservation of a projected-out default.

Causal control: reconstructing managed state from the merged view in the visibility writer reproduces the old defect. Whole-project negative check: **536 passed / exactly3 failed**, the no-copy regression under BuiltInsOnly, DirectoryOnly and AddToBuiltIns. The source was restored byte-for-byte; whole-project restored check **539 passed, 0 failed/skipped**, `studio3360-persistence-restored.trx` (1m07s). This proves the regression tests detect the removed behavior. No unrelated source was changed by the control.

Concurrency/assets/cancellation qualification, migration of old Web fixtures and Web adoption, frontend/runtime changes and delivery gates remain outstanding.
