using System.Collections.Frozen;

namespace HomeworkCentral.Api.Authorization;

/// <summary>Default message colors for platform roles when none is stored in the database.</summary>
public static class RoleAppearanceDefaults
{
    public const string FallbackColor = "#64748b";

    private static readonly FrozenDictionary<string, string> PlatformRoleColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Guest"] = "#94a3b8",
            ["VerifiedUser"] = "#38bdf8",
            ["Student"] = "#22c55e",
            ["Staff"] = "#a855f7",
            ["Tutor"] = "#3b82f6",
            ["TrialTutor"] = "#60a5fa",
            ["SeniorTutor"] = "#2563eb",
            ["HeadTutor"] = "#1d4ed8",
            ["Moderator"] = "#f59e0b",
            ["SeniorModerator"] = "#d97706",
            ["CommunityManager"] = "#ec4899",
            ["EventOrganizer"] = "#14b8a6",
            ["SeminarHost"] = "#06b6d4",
            ["VerifiedEducator"] = "#10b981",
            ["Developer"] = "#8b5cf6",
            ["BetaTester"] = "#6366f1",
            ["Administrator"] = "#ef4444",
            ["SystemAdministrator"] = "#dc2626",
            ["BoardMember"] = "#f97316",
            ["Owner"] = "#eab308",
            ["Founder"] = "#ca8a04",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string ResolvePlatformRoleColor(string roleName, string? storedColor) =>
        !string.IsNullOrWhiteSpace(storedColor)
            ? storedColor
            : PlatformRoleColors.TryGetValue(roleName, out string? defaultColor)
                ? defaultColor
                : FallbackColor;

    public static string ResolveCustomRoleColor(string? storedColor) =>
        !string.IsNullOrWhiteSpace(storedColor) ? storedColor : FallbackColor;
}
