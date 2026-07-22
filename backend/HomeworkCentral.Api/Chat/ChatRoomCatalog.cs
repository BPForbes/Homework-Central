using System.Reflection;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// Canonical chat rooms derived from subject expertise and platform role bit indices.
/// Subject types are categories; expertise bits and staff roles are the actual rooms.
/// </summary>
public static class ChatRoomCatalog
{
    public const string StaffCategoryKey = "Staff";
    public const string StaffCategoryDisplayName = "Staff";

    private static readonly IReadOnlyDictionary<string, Type> ExpertiseTypesByCategory =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [SubjectMaskNames.Mathematics] = typeof(MathematicsExpertise),
            [SubjectMaskNames.Science] = typeof(ScienceExpertise),
            [SubjectMaskNames.ComputerScience] = typeof(ComputerScienceExpertise),
            [SubjectMaskNames.Languages] = typeof(LanguageExpertise),
            [SubjectMaskNames.History] = typeof(HistoryExpertise),
            [SubjectMaskNames.Business] = typeof(BusinessExpertise),
            [SubjectMaskNames.Art] = typeof(ArtExpertise),
            [SubjectMaskNames.Music] = typeof(MusicExpertise),
            [SubjectMaskNames.Engineering] = typeof(EngineeringExpertise),
            [SubjectMaskNames.Medicine] = typeof(MedicineExpertise),
            [SubjectMaskNames.Finance] = typeof(FinanceExpertise),
            [SubjectMaskNames.Economics] = typeof(EconomicsExpertise),
            [SubjectMaskNames.Education] = typeof(EducationExpertise),
        };

    private static readonly IReadOnlyDictionary<string, string> CategoryDisplayNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SubjectMaskNames.Mathematics] = "Mathematics",
            [SubjectMaskNames.Science] = "Science",
            [SubjectMaskNames.ComputerScience] = "Computer Science",
            [SubjectMaskNames.Languages] = "Languages",
            [SubjectMaskNames.History] = "History",
            [SubjectMaskNames.Business] = "Business",
            [SubjectMaskNames.Art] = "Art",
            [SubjectMaskNames.Music] = "Music",
            [SubjectMaskNames.Engineering] = "Engineering",
            [SubjectMaskNames.Medicine] = "Medicine",
            [SubjectMaskNames.Finance] = "Finance",
            [SubjectMaskNames.Economics] = "Economics",
            [SubjectMaskNames.Education] = "Education",
        };

    private static readonly IReadOnlyDictionary<string, string> ExpertiseDisplayNameOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CPlusPlus"] = "C++",
            ["CSharp"] = "C#",
            ["Css"] = "CSS",
            ["RestApis"] = "APIs",
            ["PostgreSql"] = "PostgreSQL",
            ["MySql"] = "MySQL",
        };

    public static readonly ChatRoomDefinition GeneralRoom = ChatRoomBlueprint.GeneralLobby();

    public static readonly ChatRoomDefinition GetRolesRoom = ChatRoomBlueprint.GetRolesLobby();

    public static readonly IReadOnlyList<ChatRoomDefinition> GeneralRooms = [GeneralRoom, GetRolesRoom];

    public static readonly IReadOnlyList<ChatRoomDefinition> SubjectRooms = BuildSubjectRooms();

    public static readonly IReadOnlyList<ChatRoomDefinition> StaffRooms =
    [
        ChatRoomBlueprint.StaffRole(PlatformRoles.Staff, "Staff"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.Tutor, "Tutors"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.SeniorTutor, "Senior Tutors"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.HeadTutor, "Head Tutors"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.Moderator, "Moderators"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.SeniorModerator, "Senior Moderators"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.Administrator, "Admins"),
        ChatRoomBlueprint.StaffRole(PlatformRoles.CommunityManager, "Community Managers"),
    ];

    public static readonly IReadOnlyList<ChatRoomDefinition> AllRooms =
        GeneralRooms.Concat(SubjectRooms).Concat(StaffRooms).ToList();

    private static readonly IReadOnlyDictionary<string, ChatRoomDefinition> RoomsById =
        AllRooms.ToDictionary(room => room.Id, StringComparer.Ordinal);

    public static ChatRoomDefinition? FindById(string roomId) =>
        RoomsById.TryGetValue(roomId, out ChatRoomDefinition? room) ? room : null;

    public static bool IsPrivateCategory(ChatCategoryKind categoryKind) =>
        categoryKind is ChatCategoryKind.Subject or ChatCategoryKind.Staff;

    private static IReadOnlyList<ChatRoomDefinition> BuildSubjectRooms()
    {
        List<ChatRoomDefinition> rooms = new();

        foreach (SubjectExpertiseCategory category in SubjectExpertiseCatalog.Categories)
        {
            // Both lookups are hand-maintained alongside SubjectExpertiseCatalog.Categories; if
            // that catalog ever gains a category without a matching entry here, fail loudly at
            // startup instead of silently dropping chat rooms for it.
            if (!ExpertiseTypesByCategory.TryGetValue(category.ExpertiseMaskName, out Type? expertiseType))
            {
                throw new InvalidOperationException(
                    $"ChatRoomCatalog.ExpertiseTypesByCategory has no entry for subject expertise category " +
                    $"'{category.ExpertiseMaskName}'. Add one alongside SubjectExpertiseCatalog.Categories.");
            }

            if (!CategoryDisplayNames.TryGetValue(category.ExpertiseMaskName, out string? categoryDisplayName))
            {
                throw new InvalidOperationException(
                    $"ChatRoomCatalog.CategoryDisplayNames has no entry for subject expertise category " +
                    $"'{category.ExpertiseMaskName}'. Add one alongside SubjectExpertiseCatalog.Categories.");
            }

            foreach (FieldInfo field in expertiseType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(short))
                    continue;

                short expertiseBit = (short)field.GetValue(null)!;
                string roomName = ExpertiseDisplayNameOverrides.TryGetValue(field.Name, out string? overrideName)
                    ? overrideName
                    : ToDisplayName(field.Name);

                rooms.Add(ChatRoomBlueprint.SubjectExpertise(
                    category.ExpertiseMaskName,
                    categoryDisplayName,
                    roomName,
                    expertiseBit));
            }
        }

        return rooms;
    }

    internal static string ToDisplayName(string pascalName) =>
        string.Concat(pascalName.Select((ch, index) =>
            index > 0 && char.IsUpper(ch) && !char.IsUpper(pascalName[index - 1])
                ? " " + ch
                : ch.ToString()));

    public sealed record CatalogCategoryTemplate(
        string Key,
        string DisplayName,
        ChatCategoryKind CategoryKind);

    /// <summary>
    /// Resolves built-in chat nav categories (General, Staff, subject expertise) so custom rooms
    /// can merge into an existing sidebar section instead of creating a duplicate category.
    /// </summary>
    public static bool TryGetCatalogCategoryTemplate(
        string categoryKey,
        out CatalogCategoryTemplate template)
    {
        if (string.Equals(categoryKey, ChatRoomBlueprint.GeneralCategoryKey, StringComparison.OrdinalIgnoreCase))
        {
            template = new CatalogCategoryTemplate(
                ChatRoomBlueprint.GeneralCategoryKey,
                ChatRoomBlueprint.GeneralCategoryDisplayName,
                ChatCategoryKind.General);
            return true;
        }

        if (string.Equals(categoryKey, StaffCategoryKey, StringComparison.OrdinalIgnoreCase))
        {
            template = new CatalogCategoryTemplate(
                StaffCategoryKey,
                StaffCategoryDisplayName,
                ChatCategoryKind.Staff);
            return true;
        }

        foreach ((string key, string displayName) in CategoryDisplayNames)
        {
            if (!string.Equals(categoryKey, key, StringComparison.OrdinalIgnoreCase))
                continue;

            template = new CatalogCategoryTemplate(key, displayName, ChatCategoryKind.Subject);
            return true;
        }

        template = null!;
        return false;
    }
}
