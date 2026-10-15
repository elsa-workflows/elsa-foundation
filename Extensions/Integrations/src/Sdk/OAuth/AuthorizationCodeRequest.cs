namespace Elsa.Integrations.Sdk.OAuth;

public sealed record AuthorizationCodeRequest(
    string AuthorizationUrl,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod = "S256");

public sealed record AuthorizationCodeCallback(string Code, string State);
