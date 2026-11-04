# Delivery evidence

Date: 2026-11-04. Starting Foundation main: `73040cd7b1368818820d2a60902443a9f8a82c7f`. Own worktree/branch: `external-auth-3579/elsa-foundation`, `agent/codex/3579-external-auth`.

## State

Incomplete delivery. Design and independent identity-isolation implementation are in progress. No provider-management/broker/Studio implementation, real provider sign-in, restart/multi-node qualification, PR/merge or resulting-main acceptance is claimed. The mapped-role assignment choice remains open: the first card selection was withdrawn; the repeated card awaits a considered answer. Provisioning policies assign existing Elsa roles; upstream roles/permissions have no automatic authority.

Claims: #3579 comment 6627512912; prerequisite #2848 comment 6627557183. No competing claim/assignment/open PR found before this checkpoint. Owner authorization is recorded on #3579 comment 6627537005 and linked on #2771; #2771's Phase 2 description now names this scoped advancement without activating other phases. Baseline main CI 41790684918 passed; no open main-red CI issue was found at startup.

## Identity-isolation implementation

- Typed `TokenSubject`: tenant/user or tenant/provider/validated-issuer/opaque-subject.
- Domain-separated length-framed hash for token storage/revocation. External first-party JWT `sub` is scoped too; canonical users keep their Elsa user ID and explicit canonical user claim.
- Issuance, refresh and logout share typed scope. Malformed/legacy/mismatched refresh payloads are refused.
- Canonical projection replaces upstream NameIdentifier/sub/tenant rather than appending identifiers.
- External-bearer normalization strips forged origin/key claims and stamps validated provider/issuer.
- First-party bearer validation requires its signed full-scope key. This deliberately refuses old unscoped access tokens after upgrade, avoiding live old tokens that scoped logout cannot revoke.
- External subjects equal to local user IDs cannot rotate that local user's cookie stamp.
- Updated tracked public API and callers. This is a pre-GA source-breaking token contract change; no compatibility overload accepts a bare revocation string.

## Completed local runs

1. Focused Identity suites: **passed 128/128**, no skips (before later payload/validation additions).
2. Complete Identity suite: **passed 572/572**, then **577/577** after malformed/legacy payload coverage, no skips.
3. Focused token/access-scope validation: **passed 47/47**, no skips, including cryptographically valid tokens with missing/wrong scope keys.
4. Bite-proof mutation: temporarily replace only `TokenSubjectKey.Create` with `subject.SubjectId`. The scope-isolation and real HTTP logout/refresh regressions **both fail** because a different identity loses access. The exact scoped source was restored in `finally`; restored complete suite **passed 579/579**, no skips. This proves the tests detect the collision rather than only exercising token creation. Final complete run after adding explicit untrusted/multiple-identity resolution, scoped access-token validation and canonical-user cross-tenant isolation: **passed 581/581**, zero failed/skipped, 8 seconds reported test duration.

Commands (run through the normal build-slot PATH):

```sh
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --verbosity minimal
dotnet test Essentials/tests/Foundation/Identity/Tests/Elsa.Foundation.Identity.Tests.csproj --no-restore --filter 'FullyQualifiedName~Scoped_revocation_preserves_equal_subjects|FullyQualifiedName~Http_logout_of_external_subject' --verbosity minimal
```

The complete suite includes cookie/token exchange/logout behavior, real JWT-handler normalization, security and API registration tests. It is not an actual interactive provider sign-in journey. Initial fresh-worktree `--no-restore` failed for missing assets; restore fixed it. Intermediate compilation failures from the new contract's import/API baseline were corrected before passing runs; they are not counted as passed tests. Existing unrelated runtime API analyzer warnings remain visible in build output.

## Outstanding acceptance

All T010 onward identity/admission/catalog/protocol/preview/session/API/Studio work and actual qualification remain open, as do review/merge/current required CI/packed consumers/resulting-main proof. User disable/delete and durable selected-session enforcement must integrate the same typed identity; the existing low-level status write alone does not deliver that acceptance. #2848 and #3579 remain open.
