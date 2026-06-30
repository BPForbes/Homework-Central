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

    public static readonly IReadOnlyList<ChatRoomDefinition> SubjectRooms = BuildSubjectRooms();

    public static readonly IReadOnlyList<ChatRoomDefinition> StaffRooms =
    [
        CreateStaffRoom(PlatformRoles.Staff, "Staff"),
        CreateStaffRoom(PlatformRoles.Tutor, "Tutors"),
        CreateStaffRoom(PlatformRoles.SeniorTutor, "Senior Tutors"),
        CreateStaffRoom(PlatformRoles.HeadTutor, "Head Tutors"),
        CreateStaffRoom(PlatformRoles.Moderator, "Moderators"),
        CreateStaffRoom(PlatformRoles.SeniorModerator, "Senior Moderators"),
        CreateStaffRoom(PlatformRoles.Administrator, "Admins"),
        CreateStaffRoom(PlatformRoles.CommunityManager, "Community Managers"),
    ];

    public static readonly IReadOnlyList<ChatRoomDefinition> AllRooms =
        SubjectRooms.Concat(StaffRooms).ToList();

    public static ChatRoomDefinition? FindById(string roomId) =>
        AllRooms.FirstOrDefault(room => string.Equals(room.Id, roomId, StringComparison.Ordinal));

    private static IReadOnlyList<ChatRoomDefinition> BuildSubjectRooms()
    {
        List<ChatRoomDefinition> rooms = new();

        foreach (SubjectExpertiseCategory category in SubjectExpertiseCatalog.Categories)
        {
            if (!ExpertiseTypesByCategory.TryGetValue(category.ExpertiseMaskName, out Type? expertiseType))
                continue;

            string categoryDisplayName = CategoryDisplayNames[category.ExpertiseMaskName];

            foreach (FieldInfo field in expertiseType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(short))
                    continue;

                short expertiseBit = (short)field.GetValue(null)!;
                string roomName = ToDisplayName(field.Name);
                string roomId = $"subject:{category.ExpertiseMaskName}:{expertiseBit}";

                rooms.Add(new ChatRoomDefinition(
                    roomId,
                    ChatRoomKind.SubjectExpertise,
                    category.ExpertiseMaskName,
                    categoryDisplayName,
                    roomName,
                    category.ExpertiseMaskName,
                    expertiseBit,
                    null));
            }
        }

        return rooms;
    }

    private static ChatRoomDefinition CreateStaffRoom(short roleBit, string roomDisplayName) =>
        new(
            $"staff:{roleBit}",
            ChatRoomKind.StaffRole,
            StaffCategoryKey,
            StaffCategoryDisplayName,
            roomDisplayName,
            null,
            null,
            roleBit);

    internal static string ToDisplayName(string pascalName) =>
        string.Concat(pascalName.Select((ch, index) =>
            index > 0 && char.IsUpper(ch) && !char.IsUpper(pascalName[index - 1])
                ? " " + ch
                : ch.ToString()));
}
