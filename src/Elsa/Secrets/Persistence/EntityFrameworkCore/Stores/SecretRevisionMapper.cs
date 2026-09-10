using Elsa.Secrets.Core.Contracts;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;

public static class SecretRevisionMapper
{
    public const string Prefix = "ef:";

    public static string Revision(byte[] token) => Prefix + Convert.ToHexString(token);

    public static bool TryParse(string? revision, out byte[]? token)
    {
        token = null;
        if (revision is null)
            return true;

        if (!revision.StartsWith(Prefix, StringComparison.Ordinal) || revision.Length != Prefix.Length + 32)
            return false;

        try
        {
            token = Convert.FromHexString(revision.AsSpan(Prefix.Length));
            return token.Length == 16;
        }
        catch (FormatException)
        {
            token = null;
            return false;
        }
    }

    public static SecretRevisionSaveResult InvalidRevision() =>
        new(SecretRevisionSaveStatus.Conflict);

    public static bool SameToken(byte[] left, byte[] right) => left.AsSpan().SequenceEqual(right);
}
