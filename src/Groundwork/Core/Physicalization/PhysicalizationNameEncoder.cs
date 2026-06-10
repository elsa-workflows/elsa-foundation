namespace Groundwork.Core.Physicalization;

public static class PhysicalizationNameEncoder
{
    public static string Encode(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "empty";

        return string.Concat(value.Select(Encode));
    }

    private static string Encode(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9'
            ? character.ToString()
            : $"_x{(int)character:x4}_";
}
