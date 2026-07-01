using System.Collections;
using HomeworkCentral.Api.Models;
using HomeworkCentral.Api.Utilities;

namespace HomeworkCentral.Api.Authorization;

/// <summary>Builds role identity, permission, and feature bitmasks for catalog seeding.</summary>
public static class RoleMaskBuilder
{
    public sealed record RoleMaskSet(BitArray RoleMask, BitArray PermissionMask, BitArray FeatureMask);

    public static RoleMaskSet Build(string roleName, IEnumerable<short> permissionIds)
    {
        BitArray permissionMask = BuildPermissionMask(permissionIds);
        BitArray roleMask = BuildRoleIdentityMask(roleName);
        BitArray featureMask = BuildFeatureMask(roleName);
        return new RoleMaskSet(roleMask, permissionMask, featureMask);
    }

    public static BitArray BuildPermissionMask(IEnumerable<short> permissionIds)
    {
        BitArray mask = BitMask.Create(256);
        foreach (short permissionId in permissionIds)
            BitMask.SetBit(mask, permissionId);
        return mask;
    }

    public static BitArray BuildRoleIdentityMask(string roleName)
    {
        if (!PlatformRoleCatalog.TryGetRoleBit(roleName, out short bit))
            throw new InvalidOperationException($"Role '{roleName}' is not defined in PlatformRoleCatalog.");

        return SetSingleBit(64, bit);
    }

    public static BitArray BuildFeatureMask(string roleName)
    {
        BitArray mask = BitMask.Create(256);

        switch (roleName)
        {
            case "Guest":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PublicProfile);
                break;
            case "VerifiedUser":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads);
                break;
            case "Student":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.ProfileCustomization,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads,
                    PlatformFeatures.EventCalendar);
                break;
            case "Staff":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.EventCalendar);
                break;
            case "Tutor":
                SetFeatures(mask, TutorFeatures());
                break;
            case "SeniorTutor":
            case "HeadTutor":
                SetFeatures(mask, TutorFeatures());
                SetFeatures(mask,
                    PlatformFeatures.SeminarHosting,
                    PlatformFeatures.SeminarUpload,
                    PlatformFeatures.EventPosting,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.CommunityPolls);
                break;
            case "SeminarHost":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.SeminarHosting,
                    PlatformFeatures.SeminarUpload,
                    PlatformFeatures.ScreenSharing,
                    PlatformFeatures.PublicProfile);
                break;
            case "Moderator":
            case "SeniorModerator":
            case "CommunityManager":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.GroupMessages,
                    PlatformFeatures.ResourceSharing,
                    PlatformFeatures.WikiEditing,
                    PlatformFeatures.CommunityAnnouncements,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.AnalyticsDashboard);
                break;
            case "EventOrganizer":
                SetFeatures(mask,
                    PlatformFeatures.EventPosting,
                    PlatformFeatures.EventCalendar,
                    PlatformFeatures.CommunityPolls,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.GroupMessages);
                break;
            case "BetaTester":
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PrivateMessages,
                    PlatformFeatures.PublicProfile,
                    PlatformFeatures.FileUploads,
                    PlatformFeatures.ImageUploads,
                    PlatformFeatures.BetaFeatures);
                break;
            case "Administrator":
            case "SystemAdministrator":
            case "BoardMember":
            case "Owner":
            case "Founder":
                EnableAllFeatures(mask);
                break;
            default:
                SetFeatures(mask,
                    PlatformFeatures.PublicMessages,
                    PlatformFeatures.PublicProfile);
                break;
        }

        return mask;
    }

    private static short[] TutorFeatures() =>
    [
        PlatformFeatures.PublicMessages,
        PlatformFeatures.PrivateMessages,
        PlatformFeatures.GroupMessages,
        PlatformFeatures.VoiceRooms,
        PlatformFeatures.VideoRooms,
        PlatformFeatures.ScreenSharing,
        PlatformFeatures.ResourceSharing,
        PlatformFeatures.PublicProfile,
        PlatformFeatures.ProfileCustomization,
        PlatformFeatures.FileUploads,
        PlatformFeatures.ImageUploads,
        PlatformFeatures.EventCalendar,
    ];

    private static void EnableAllFeatures(BitArray mask)
    {
        for (int i = 0; i <= PlatformFeatures.BetaFeatures; i++)
            BitMask.SetBit(mask, i);
    }

    private static void SetFeatures(BitArray mask, params short[] features)
    {
        foreach (short feature in features)
            BitMask.SetBit(mask, feature);
    }

    private static BitArray SetSingleBit(int length, short bit)
    {
        BitArray mask = BitMask.Create(length);
        BitMask.SetBit(mask, bit);
        return mask;
    }
}
