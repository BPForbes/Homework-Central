export type CustomRoomType = 'Chat' | 'Info' | 'RoleClaim'
export type ChannelTieType = 'None' | 'GeneralSubject' | 'SubjectExpertise' | 'PlatformRole'

export interface CustomRole {
  roleId: string
  name: string
  description?: string
  iconName?: string | null
  claimHostRoomId?: string | null
  permissionIds: number[]
  createdAtUtc: string
}

export interface CustomChannelAccessRule {
  customRoleId?: string | null
  customRoleName?: string | null
  platformRoleBit?: number | null
  platformRoleName?: string | null
}

export interface CustomChannel {
  channelId: string
  roomId: string
  displayName: string
  categoryKey: string
  categoryDisplayName: string
  roomType: CustomRoomType
  isPrivate: boolean
  infoContent?: string | null
  tieType: ChannelTieType
  tieSubjectMask?: string | null
  tieSubjectBitIndex?: number | null
  tiePlatformRoleBit?: number | null
  createdAtUtc: string
  updatedAtUtc: string
  accessRules: CustomChannelAccessRule[]
  canEditInfo: boolean
}

export interface ClaimableCustomRole {
  roleId: string
  name: string
  description?: string
  iconName?: string | null
  claimed: boolean
}

export interface ModerationRiskWarning {
  requiresPassword: boolean
  riskyPermissions: string[]
}

export interface CustomChannelAccessRuleInput {
  customRoleId?: string | null
  platformRoleBit?: number | null
}

export const MODERATION_PERMISSIONS: { id: number; name: string; label: string }[] = [
  { id: 0, name: 'ViewReports', label: 'View reports' },
  { id: 1, name: 'ResolveReports', label: 'Resolve reports' },
  { id: 2, name: 'WarnUser', label: 'Warn users' },
  { id: 3, name: 'TimeoutUser', label: 'Timeout users' },
  { id: 4, name: 'MuteMembers', label: 'Mute members' },
  { id: 5, name: 'KickUser', label: 'Kick users' },
  { id: 6, name: 'BanMembers', label: 'Ban members' },
  { id: 7, name: 'DeleteMessages', label: 'Delete messages' },
  { id: 8, name: 'EditMessages', label: 'Edit messages' },
  { id: 9, name: 'PinMessages', label: 'Pin messages' },
  { id: 10, name: 'LockChannels', label: 'Lock channels' },
  { id: 11, name: 'ManageChannels', label: 'Manage channels' },
  { id: 12, name: 'ManageRoles', label: 'Manage roles' },
  { id: 13, name: 'ManagePermissions', label: 'Manage permissions' },
  { id: 14, name: 'ViewAuditLogs', label: 'View audit logs' },
  { id: 15, name: 'ManageEvents', label: 'Manage events' },
  { id: 16, name: 'ManageSeminars', label: 'Manage seminars' },
  { id: 17, name: 'ModerateResources', label: 'Moderate resources' },
  { id: 18, name: 'SuspendAccounts', label: 'Suspend accounts' },
  { id: 19, name: 'HandleAppeals', label: 'Handle appeals' },
  { id: 20, name: 'ManageServerInfrastructure', label: 'Manage server infrastructure' },
]

export const PLATFORM_ROLES: { bit: number; name: string }[] = [
  { bit: 0, name: 'Guest' },
  { bit: 1, name: 'VerifiedUser' },
  { bit: 2, name: 'Student' },
  { bit: 3, name: 'Staff' },
  { bit: 4, name: 'Tutor' },
  { bit: 7, name: 'Moderator' },
  { bit: 15, name: 'Administrator' },
  { bit: 16, name: 'SystemAdministrator' },
  { bit: 18, name: 'Owner' },
]

export const GET_ROLES_ROOM_ID = 'general:get-roles'

export interface InfrastructureUserLookup {
  userId: string
  username: string
  email: string
  tenantDatabaseName?: string | null
  highestPlatformRoleBit: number
  highestPlatformRoleName: string
  customRoles: CustomRole[]
}

export interface AssignableUser {
  userId: string
  username: string
  email: string
  tenantDatabaseName?: string | null
  highestPlatformRoleBit: number
  highestPlatformRoleName: string
  alreadyAssigned: boolean
  canAssign: boolean
}

