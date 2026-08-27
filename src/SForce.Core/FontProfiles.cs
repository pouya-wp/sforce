namespace SForce.Core;

public static class FontProfiles
{
    private static readonly string[] SafeLatinSources =
    [
        "Segoe UI",
        "Segoe UI Variable",
        "Segoe UI Variable Text",
        "Segoe UI Variable Display",
        "MS Shell Dlg",
        "MS Shell Dlg 2"
    ];

    private static readonly string[] AggressiveLatinSources =
    [
        "Inter",
        "Arial",
        "Helvetica",
        "Helvetica Neue",
        "Roboto",
        "Tahoma",
        "Verdana",
        "Open Sans",
        "Noto Sans"
    ];

    private static readonly string[] ArabicSources =
    [
        "Segoe UI Arabic",
        "Arial Arabic",
        "Tahoma Arabic",
        "Noto Sans Arabic",
        "Noto Kufi Arabic"
    ];

    public static IReadOnlyList<FontAlias> BuildAliases(ForceProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.LatinFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ArabicFamily);

        var aliases = SafeLatinSources
            .Where(source => !source.Equals(profile.LatinFamily, StringComparison.OrdinalIgnoreCase))
            .Select(source => new FontAlias(source, profile.LatinFamily, false))
            .ToList();

        aliases.AddRange(ArabicSources
            .Where(source => !source.Equals(profile.ArabicFamily, StringComparison.OrdinalIgnoreCase))
            .Select(source => new FontAlias(source, profile.ArabicFamily, false)));

        if (profile.AggressiveAliases)
        {
            aliases.AddRange(AggressiveLatinSources
                .Where(source => !source.Equals(profile.LatinFamily, StringComparison.OrdinalIgnoreCase))
                .Select(source => new FontAlias(source, profile.LatinFamily, true)));
        }

        return aliases
            .DistinctBy(alias => alias.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
