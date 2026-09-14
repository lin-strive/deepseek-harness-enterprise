namespace CompanyHarness.Desktop.Core.Storage;

public enum ShellThemeMode
{
    FollowHarness,
    FollowSystem,
    Light,
    Dark,
}

public sealed record ShellPreferences(ShellThemeMode ThemeMode)
{
    public static ShellPreferences Default { get; } = new(ShellThemeMode.FollowHarness);
}
