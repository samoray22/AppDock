namespace AppBox.Core;

public static class AppLabel
{
    private static readonly string[] Prefixes =
    [
        "Microsoft ",
        "Adobe ",
        "Google ",
        "Mozilla ",
        "JetBrains "
    ];

    public static string Shorten(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name ?? string.Empty;

        foreach (var prefix in Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = name[prefix.Length..].TrimStart();
                if (candidate.Length >= 3)
                    return candidate;
            }
        }

        return name;
    }
}
