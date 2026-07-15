import axios from 'axios'
import { configureApiClient } from './configureApiClient'
import type {
  ClaimableCustomRole,
  CustomChannel,
  CustomChannelAccessRuleInput,
  CustomRole,
  InfoEntry,
  InfoEntryFeed,
  InfrastructureUserLookup,
  AssignableUser,
  ModerationRiskWarning,
  RoleAppearance,
} from '../types/infrastructure'

const api = axios.create({ baseURL: '/api/infrastructure', withCredentials: true })
configureApiClient(api)

export const infrastructureApi = {
  listRoles: () => api.get<CustomRole[]>('/roles'),
  createRole: (body: { name: string; description?: string; iconName?: string; permissionIds: number[] }) =>
    api.post<CustomRole>('/roles', body),
  updateRole: (roleId: string, body: { name?: string; description?: string; iconName?: string; permissionIds?: number[] }) =>
    api.put<CustomRole>(`/roles/${roleId}`, body),
  setRolePlacement: (roleId: string, body: { claimHostRoomId?: string | null; password?: string }) =>
    api.put(`/roles/${roleId}/placement`, body),
  deleteRole: (roleId: string) => api.delete(`/roles/${roleId}`),
  listRoleAppearance: () => api.get<RoleAppearance[]>('/role-appearance'),
  updateRoleAppearance: (roleId: string, body: { messageColor?: string; isMentionableByUsers?: boolean }) =>
    api.put<RoleAppearance>(`/roles/${roleId}/appearance`, body),
  previewAccessRisk: (roleId: string, isPublicRoom: boolean) =>
    api.get<ModerationRiskWarning>(`/roles/${roleId}/access-risk`, { params: { isPublicRoom } }),

  listChannels: () => api.get<CustomChannel[]>('/channels'),
  createChannel: (body: Record<string, unknown>) => api.post<CustomChannel>('/channels', body),
  updateChannel: (channelId: string, body: Record<string, unknown>) =>
    api.put<CustomChannel>(`/channels/${channelId}`, body),
  archiveChannel: (channelId: string) => api.delete(`/channels/${channelId}`),
  getChannelByRoom: (roomId: string) =>
    api.get<CustomChannel>(`/channels/by-room/${encodeURIComponent(roomId)}`),

  getClaimableRoles: (roomId: string) =>
    api.get<ClaimableCustomRole[]>(
      `/channels/by-room/${encodeURIComponent(roomId)}/claimable-roles`
    ),
  claimRole: (roleId: string, roomId: string) =>
    api.post(`/roles/${roleId}/claim`, null, { params: { roomId } }),
  unclaimRole: (roleId: string) => api.delete(`/roles/${roleId}/claim`),

  listClaimRolesForRoom: (roomId: string) =>
    api.get<CustomRole[]>(`/channels/by-room/${encodeURIComponent(roomId)}/claim-roles`),
  reorderClaimRoles: (roomId: string, orderedRoleIds: string[]) =>
    api.put(`/channels/by-room/${encodeURIComponent(roomId)}/claim-order`, { orderedRoleIds }),

  listInfoEntries: (roomId: string) =>
    api.get<InfoEntryFeed>(`/channels/by-room/${encodeURIComponent(roomId)}/info-entries`),
  createInfoEntry: (roomId: string, content: string) =>
    api.post<InfoEntry>(`/channels/by-room/${encodeURIComponent(roomId)}/info-entries`, { content }),
  updateInfoEntry: (entryId: string, content: string) =>
    api.put<InfoEntry>(`/info-entries/${entryId}`, { content }),

  searchUsers: (q: string) =>
    api.get<InfrastructureUserLookup[]>('/users/search', { params: { q } }),
  getUser: (userId: string, tenantDatabaseName?: string | null) =>
    api.get<InfrastructureUserLookup>(`/users/${userId}`, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
  getUserRoleManagement: (userId: string, tenantDatabaseName?: string | null) =>
    api.get<InfrastructureUserLookup>(`/users/${userId}/role-management`, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
  assignPlatformRole: (userId: string, roleName: string, tenantDatabaseName?: string | null) =>
    api.post(`/users/${userId}/platform-roles/${encodeURIComponent(roleName)}`, null, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
  revokePlatformRole: (userId: string, roleName: string, tenantDatabaseName?: string | null) =>
    api.delete(`/users/${userId}/platform-roles/${encodeURIComponent(roleName)}`, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
  listAssignableUsers: (roleId: string) =>
    api.get<AssignableUser[]>(`/roles/${roleId}/assignable-users`),
  bulkAssignRole: (roleId: string, users: { userId: string; tenantDatabaseName?: string | null }[]) =>
    api.post<{ assigned: number }>(`/roles/${roleId}/assignments`, { users }),
  assignRoleToUser: (userId: string, roleId: string, tenantDatabaseName?: string | null) =>
    api.post(`/users/${userId}/roles/${roleId}`, null, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
  revokeRoleFromUser: (userId: string, roleId: string, tenantDatabaseName?: string | null) =>
    api.delete(`/users/${userId}/roles/${roleId}`, {
      params: tenantDatabaseName ? { tenantDatabaseName } : undefined,
    }),
}

export type { CustomChannelAccessRuleInput }
