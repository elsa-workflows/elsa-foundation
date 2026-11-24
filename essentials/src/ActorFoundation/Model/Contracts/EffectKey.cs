namespace Kernel.Model.Contracts;

/// <summary>Derives an activity attempt's stable effect key from its committed identity and iteration path.</summary>
public static class EffectKey
{
    /// <summary>The iteration path of an activation outside repeating scopes.</summary>
    public const string RootPath = "-";

    /// <summary><paramref name="origin"/> is the string of the exact reference issued for this actor's life.</summary>
    public static string For(string origin, string node, string iterationPath, int attempt) =>
        FormattableString.Invariant($"{origin}/{node}/{(iterationPath.Length == 0 ? RootPath : iterationPath)}/{attempt}");
}
