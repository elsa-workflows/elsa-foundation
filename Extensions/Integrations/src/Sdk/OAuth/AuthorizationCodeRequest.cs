namespace Elsa.Integrations.Sdk.OAuth;

internal sealed record AuthorizationCodeRequest(
    string AuthorizationUrl,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod = "S256");

internal sealed record AuthorizationCodeCallback(string Code, string State);
