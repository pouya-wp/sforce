namespace SForce.Core;

public sealed record FontFaceInfo(
    string FilePath,
    string Family,
    string Face,
    bool IsArabic,
    bool IsVariable);

public sealed record ForceProfile(
    string LatinFamily,
    string ArabicFamily,
    string? ArabicFontFile,
    bool AggressiveAliases)
{
    // 500 is deliberately the default: Windows' Segoe UI Variable UI is often
    // requested at 400, which makes the available SF Pro variable face look too thin.
    public int MinimumWeight { get; init; } = 500;
}

public sealed record FontAlias(string Source, string Target, bool IsAggressive);

public sealed record RegistryValueSnapshot(
    string Hive,
    string KeyPath,
    string ValueName,
    bool Existed,
    int Kind,
    string[] Data);

public sealed class BackupManifest
{
    public required string Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required ForceProfile Profile { get; init; }
    public required List<RegistryValueSnapshot> RegistryValues { get; init; }
    public string? NonClientMetrics { get; init; }
    public string? IconTitleFont { get; init; }
}

public sealed record ApplyResult(bool Success, string Message, string? BackupId = null);

public sealed record SystemStatus(
    bool IsApplied,
    bool IsAdministrator,
    string? ActiveLatinFamily,
    string? ActiveArabicFamily,
    DateTimeOffset? LastBackupAt);
