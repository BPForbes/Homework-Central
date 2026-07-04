using System.Reflection;
using HomeworkCentral.Api.Authorization;

namespace HomeworkCentral.Api.Chat;

/// <summary>
/// Blueprint constructors for chat rooms. Private rooms require role or expertise access;
/// public rooms (e.g. General) are open to any authenticated chatter.
/// </summary>
public static class ChatRoomBlueprint
{
    public const string GeneralCategoryKey = "General";
    public const string GeneralCategoryDisplayName = "General";
    public const string GeneralRoomId = "general:lobby";
    public const string GetRolesRoomId = "general:get-roles";

    public static ChatRoomDefinition GeneralLobby() =>
        Public(
            id: GeneralRoomId,
            kind: ChatRoomKind.General,
            categoryKey: GeneralCategoryKey,
            categoryDisplayName: GeneralCategoryDisplayName,
            categoryKind: ChatCategoryKind.General,
            roomDisplayName: "General");

    /// <summary>
    /// Not a chat room — a button-based page under the General category where any authenticated
    /// user can self-claim general subject "roles" (Math, Science, Computer Science, ...). Reuses
    /// ChatRoomKind.General because access is identical (always public, no expertise/role gate);
    /// the frontend routes this room id to a dedicated page instead of the chat UI.
    /// </summary>
    public static ChatRoomDefinition GetRolesLobby() =>
        Public(
            id: GetRolesRoomId,
            kind: ChatRoomKind.General,
            categoryKey: GeneralCategoryKey,
            categoryDisplayName: GeneralCategoryDisplayName,
            categoryKind: ChatCategoryKind.General,
            roomDisplayName: "Get Roles");

    public static ChatRoomDefinition SubjectExpertise(
        string categoryKey,
        string categoryDisplayName,
        string roomDisplayName,
        short expertiseBit) =>
        Private(
            id: $"subject:{categoryKey}:{expertiseBit}",
            kind: ChatRoomKind.SubjectExpertise,
            categoryKey: categoryKey,
            categoryDisplayName: categoryDisplayName,
            categoryKind: ChatCategoryKind.Subject,
            roomDisplayName: roomDisplayName,
            expertiseCategory: categoryKey,
            expertiseBit: expertiseBit);

    public static ChatRoomDefinition StaffRole(short roleBit, string roomDisplayName) =>
        Private(
            id: $"staff:{roleBit}",
            kind: ChatRoomKind.StaffRole,
            categoryKey: ChatRoomCatalog.StaffCategoryKey,
            categoryDisplayName: ChatRoomCatalog.StaffCategoryDisplayName,
            categoryKind: ChatCategoryKind.Staff,
            roomDisplayName: roomDisplayName,
            requiredRoleBit: roleBit);

    private static ChatRoomDefinition Public(
        string id,
        ChatRoomKind kind,
        string categoryKey,
        string categoryDisplayName,
        ChatCategoryKind categoryKind,
        string roomDisplayName,
        string? expertiseCategory = null,
        short? expertiseBit = null,
        short? requiredRoleBit = null) =>
        new(
            id,
            kind,
            categoryKey,
            categoryDisplayName,
            categoryKind,
            roomDisplayName,
            IsPrivate: false,
            expertiseCategory,
            expertiseBit,
            requiredRoleBit);

    private static ChatRoomDefinition Private(
        string id,
        ChatRoomKind kind,
        string categoryKey,
        string categoryDisplayName,
        ChatCategoryKind categoryKind,
        string roomDisplayName,
        string? expertiseCategory = null,
        short? expertiseBit = null,
        short? requiredRoleBit = null) =>
        new(
            id,
            kind,
            categoryKey,
            categoryDisplayName,
            categoryKind,
            roomDisplayName,
            IsPrivate: true,
            expertiseCategory,
            expertiseBit,
            requiredRoleBit);
}
