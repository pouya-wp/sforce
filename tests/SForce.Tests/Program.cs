using SForce.Core;

var failures = new List<string>();

Run("safe aliases", () =>
{
    var profile = new ForceProfile("SF Pro Text", "SF Arabic", "SFArabic.ttf", false);
    var aliases = FontProfiles.BuildAliases(profile);
    Assert(aliases.Any(x => x.Source == "Segoe UI" && x.Target == "SF Pro Text"));
    Assert(aliases.Any(x => x.Source == "Segoe UI Arabic" && x.Target == "SF Arabic"));
    Assert(aliases.All(x => x.Source != "Inter"));
});

Run("aggressive aliases", () =>
{
    var profile = new ForceProfile("SF Pro Text", "SF Arabic", null, true);
    var aliases = FontProfiles.BuildAliases(profile);
    Assert(aliases.Any(x => x.Source == "Inter" && x.IsAggressive));
    Assert(aliases.Any(x => x.Source == "Arial" && x.IsAggressive));
    Assert(aliases.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == aliases.Count);
});

Run("force css", () =>
{
    var css = ForceCssBuilder.Build(new ForceProfile("SF Pro Text", "SF Arabic", null, true));
    Assert(css.Contains(":lang(fa)", StringComparison.Ordinal));
    Assert(css.Contains("SF Arabic", StringComparison.Ordinal));
    Assert(css.Contains("SF Mono", StringComparison.Ordinal));
    Assert(css.Contains("material-icons", StringComparison.Ordinal));
});

Run("medium weight default", () =>
{
    var profile = new ForceProfile("SF Pro", "SF Arabic", null, true);
    Assert(profile.MinimumWeight == 500);
    Assert((profile with { MinimumWeight = 600 }).MinimumWeight == 600);
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("All SFORCE tests passed.");
return 0;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {name}: {ex.Message}");
    }
}

static void Assert(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed.");
    }
}
