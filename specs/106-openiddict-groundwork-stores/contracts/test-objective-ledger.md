# OpenIddict Test-Objective Retention Ledger

This is the pre-migration retention baseline. An objective may move to a shared
or Groundwork fixture, but it may not disappear without recorded approval. The
EF fixture remains the behavior and #646 performance oracle until the exit gate.

## Reproducible inventory

```bash
{
  rg -n '\[(Fact|Theory)\]' \
    tests/Elsa/Foundation/Identity/Tests/OpenIddict \
    tests/Elsa/Foundation/Identity/Tests/Api/DevelopmentOrDemoGuardTests.cs \
    tests/Elsa/Foundation/Identity/Tests/Api/DevelopmentOrDemoGuardShellActivationTests.cs \
    tests/Elsa/Foundation/Identity/Tests/Api/EnabledShellCompositionTests.cs \
    tests/Elsa/Foundation/Identity/Tests/Api/TokenEndpointTests.cs \
    tests/Elsa/Foundation/Identity/Tests/AspNetCoreIdentity/Groundwork/AspNetCoreIdentityGroundworkHttpAcceptanceTests.cs
  rg -n 'FeatureClassesRegisterOwnedServices|OidcAndOpenIddictProvidersAreExposedThroughSameProviderManager|OpenIddictTokenServiceIssuesRefreshesValidatesAndRevokesContractTokens' \
    tests/Elsa/Foundation/Identity/Tests/IdentityProviderModuleTests.cs
}
```

The stale preparation baseline held 51 direct HTTP/token/shell objectives plus
three relevant registration/provider objectives, for **54**. Current-main
reconciliation found one additional shared-host objective (recorded below), so
this ledger retains **55** objectives: the 54-objective baseline plus one
addendum. Re-run the command and reconcile any source drift before T061; this
ledger records retention scope, not passed execution evidence.

## Direct OpenIddict objectives — 23

| Current file | Objective | Retained destination |
|---|---|---|
| `OpenIddict/OpenIddictSchemeCompositionTests.cs` | `Registers_The_Validation_Handler_And_Selector_Schemes` | Same behavior suite, provider-neutral fixture |
| same | `Feature_Registers_Cleanly_Without_Shells_Or_Server_Wiring` | Same behavior suite, Groundwork registration |
| same | `Selector_Becomes_Default_Scheme_Regardless_Of_Oidc_Registration_Order` | Same file; storage-independent |
| same | `Selector_Routes_Local_Bearer_Tokens_To_The_Validation_Handler` | Same file; storage-independent |
| same | `Selector_Routes_Opaque_Bearer_Tokens_To_The_Validation_Handler` | Same file; storage-independent |
| same | `Selector_Routes_Foreign_Bearer_Tokens_To_The_External_Scheme_When_Registered` | Same file; storage-independent |
| same | `Selector_Routes_Foreign_Bearer_Tokens_To_The_Validation_Handler_When_No_External_Scheme_Exists` | Same file; storage-independent |
| same | `Selector_Routes_Cookie_Requests_To_The_Interactive_Scheme_When_Registered` | Same file; storage-independent |
| same | `Selector_Routes_Anonymous_Requests_To_The_Validation_Handler_For_A_Clean_401` | Same file; storage-independent |
| `OpenIddict/OpenIddictBearerAuthenticationTests.cs` | `Protected_Endpoint_Accepts_A_Freshly_Issued_Token_And_Sees_Its_Claims` | Provider-neutral HTTP fixture, then four-provider host suite |
| same | `Protected_Endpoint_Rejects_Anonymous_Calls_With_401` | Same file; storage-independent |
| same | `Protected_Endpoint_Rejects_A_Tampered_Token` | Provider-neutral HTTP fixture |
| same | `Protected_Endpoint_Rejects_An_Expired_Token` | Provider-neutral HTTP fixture |
| same | `Protected_Endpoint_Rejects_A_Revoked_Token` | Provider-neutral HTTP fixture with Groundwork token store |
| `OpenIddict/OpenIddictTokenServiceTests.cs` | `Issue_Produces_Signed_Readable_Jwt_With_Subject_Tenant_And_Permission_Claims` | Provider-neutral token-service contract fixture |
| same | `Issued_Token_Validates_To_An_Authenticated_Principal_With_Claims` | Provider-neutral token-service contract fixture |
| same | `Expired_Token_Fails_Validation` | Provider-neutral token-service contract fixture |
| same | `Tampered_Token_Fails_Validation` | Provider-neutral token-service contract fixture |
| same | `Revoked_Access_Token_Fails_Validation_Immediately` | Provider-neutral token-service contract fixture |
| same | `Refresh_Rotates_Tokens_And_Preserves_Subject_Tenant_And_Scopes` | Provider-neutral token-service contract fixture plus restart |
| same | `Refresh_Token_Is_Single_Use` | Provider-neutral fixture plus 100-race suite |
| same | `Revoked_Refresh_Token_Cannot_Be_Used` | Provider-neutral token-service contract fixture |
| same | `Unknown_Refresh_Token_Is_Rejected` | Provider-neutral token-service contract fixture |

## Guard and enabled-shell objectives — 7

| Current file | Objective | Retained destination |
|---|---|---|
| `Api/DevelopmentOrDemoGuardTests.cs` | `DevDemo_Flag_In_Production_Hard_Fails_Startup_With_An_Actionable_Message` | Same host objective, Groundwork OpenIddict composition |
| same | `DevDemo_Flag_In_Development_Boots_And_Seeds` | Same host objective, Groundwork development storage |
| same | `Production_With_Real_Keys_And_Flag_False_Boots` | Same host objective, durable Groundwork storage |
| `Api/DevelopmentOrDemoGuardShellActivationTests.cs` | `DevDemo_Flag_In_Production_Aborts_Shell_Activation_With_The_Actionable_Message` | Same shell lifecycle test |
| same | `DevDemo_Flag_In_Development_Activates_The_Shell_Cleanly` | Same shell lifecycle test |
| `Api/EnabledShellCompositionTests.cs` | `Anonymous_Request_To_A_Permission_Secured_Endpoint_Is_Rejected_With_401` | Production-shaped Groundwork host fixture |
| same | `Login_Then_Token_Yields_A_Bearer_That_Satisfies_ConfigurePermissions` | Production-shaped Groundwork host fixture |

## Shared token-endpoint host objectives — 10

These tests do not name OpenIddict in their test bodies, but all ten reach its
EF-backed token service through `TokenEndpointFixture`. They are part of the
retention denominator; a token-only source scan would miss them.

| Current file | Objective | Retained destination |
|---|---|---|
| `Api/TokenEndpointTests.cs` | `Anonymous_Request_Gets_401_So_The_Client_Stays_Anonymous` | Same endpoint suite, Groundwork-backed shared host |
| same | `Authenticated_Cookie_Principal_Gets_200_With_A_Bearer_Whose_Claims_RoundTrip` | Same endpoint suite, Groundwork-backed shared host |
| same | `Login_Then_Token_Yields_A_Bearer_That_Authenticates_A_Protected_Endpoint` | Same endpoint suite, Groundwork-backed shared host |
| same | `Form_Login_Without_A_Csrf_Token_Is_Rejected_And_Issues_No_Session` | Same endpoint suite; storage-independent guard in the Groundwork-backed host |
| same | `Form_Login_Without_A_Csrf_Token_And_Without_A_ReturnUrl_Is_Rejected` | Same endpoint suite; storage-independent guard in the Groundwork-backed host |
| same | `Logout_Then_Token_Returns_401` | Same endpoint suite, Groundwork-backed shared host |
| same | `Logout_For_An_Unknown_Provider_Returns_204_And_Does_Not_500` | Same endpoint suite; storage-independent guard in the Groundwork-backed host |
| same | `Logout_On_The_Cookie_Provider_Clears_The_Session` | Same endpoint suite, Groundwork-backed shared host |
| same | `Refresh_With_A_Garbage_Token_Returns_401_Not_500` | Same endpoint suite, Groundwork-backed shared host |
| same | `Refresh_Without_A_Usable_Token_Returns_401_Not_500` | Current-main addendum: same endpoint suite, Groundwork-backed shared host |

## Mixed Groundwork Identity/EF OpenIddict HTTP objectives — 12

Current file:
`AspNetCoreIdentity/Groundwork/AspNetCoreIdentityGroundworkHttpAcceptanceTests.cs`.
The retained destination is the same production-shaped HTTP suite with the
OpenIddict persistence fixture replaced only after Groundwork store parity:

1. `Http_host_uses_Groundwork_for_framework_identity_and_EF_only_for_OpenIddict`
2. `Username_and_unique_email_login_issue_cookie_with_authority_claims_and_permission_access`
3. `Json_login_is_accepted_as_the_documented_non_browser_flow`
4. `Bad_unknown_and_wrong_tenant_credentials_fail_indistinguishably_and_lockout_is_enforced`
5. `Ambiguous_email_login_fails_with_the_same_generic_unauthorized_response`
6. `Cookie_exchanges_for_bearer_that_carries_claims_and_authorizes_identity_capabilities`
7. `Refresh_rotates_tokens_and_rejects_replay_through_the_HTTP_endpoint`
8. `Logout_invalidates_the_issued_session_cookie_instead_of_only_clearing_the_client_copy`
9. `Normal_session_cookie_remains_valid_across_immediate_stamp_checks`
10. `Cookie_validation_binds_the_tenant_claim_before_loading_the_user`
11. `Anonymous_and_non_session_providers_remain_idempotent_logout_no_ops`
12. `Invalidation_failure_is_truthful_and_does_not_clear_a_still_valid_cookie`

The first objective is renamed only when the EF oracle is retired; its
substantive assertion becomes “Groundwork owns both framework-identity and
OpenIddict stores.”

## Provider-module objectives — 3

| Current file | Objective | Retained destination |
|---|---|---|
| `IdentityProviderModuleTests.cs` | `FeatureClassesRegisterOwnedServices` | Same module test with four Groundwork stores resolvable |
| same | `OidcAndOpenIddictProvidersAreExposedThroughSameProviderManager` | Same storage-independent provider-manager test |
| same | `OpenIddictTokenServiceIssuesRefreshesValidatesAndRevokesContractTokens` | Provider-neutral token-service fixture plus durable restart |

## Retention status

All 55 current-head objectives are retained by this baseline plus addendum. None
is approved for deletion. T061 must record the final destination and passing
evidence for every row before EF fixtures are removed.
