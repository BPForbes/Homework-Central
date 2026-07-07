import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { infrastructureApi } from '../api/infrastructureApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import {
  CUSTOM_ROLE_ICON_OPTIONS,
  resolveCustomRoleIcon,
} from '../components/infrastructure/customRoleIcons'
import {
  GET_ROLES_ROOM_ID,
  MODERATION_PERMISSIONS,
  type AssignableUser,
  type CustomChannel,
  type CustomRole,
} from '../types/infrastructure'

type ViewMode = 'form' | 'assign'

export function UserConfig() {
  const [roles, setRoles] = useState<CustomRole[]>([])
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [iconName, setIconName] = useState('fas:id-badge')
  const [permissionIds, setPermissionIds] = useState<number[]>([])
  const [saving, setSaving] = useState(false)
  const [editingRoleId, setEditingRoleId] = useState<string | null>(null)
  const [placementRoleId, setPlacementRoleId] = useState<string | null>(null)
  const [placementRoomId, setPlacementRoomId] = useState('')

  const [viewMode, setViewMode] = useState<ViewMode>('form')
  const [assignRole, setAssignRole] = useState<CustomRole | null>(null)
  const [assignableUsers, setAssignableUsers] = useState<AssignableUser[]>([])
  const [selectedUserIds, setSelectedUserIds] = useState<Set<string>>(new Set())
  const [assignLoading, setAssignLoading] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const load = useCallback(async () => {
    try {
      const [rolesRes, channelsRes] = await Promise.all([
        infrastructureApi.listRoles(),
        infrastructureApi.listChannels(),
      ])
      setRoles(rolesRes.data)
      setChannels(channelsRes.data.filter((c: CustomChannel) => c.roomType === 'RoleClaim'))
    } catch {
      setError('Could not load custom roles.')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  function togglePermission(id: number) {
    setPermissionIds((prev) =>
      prev.includes(id) ? prev.filter((bit) => bit !== id) : [...prev, id]
    )
  }

  function resetRoleForm() {
    setEditingRoleId(null)
    setName('')
    setDescription('')
    setIconName('fas:id-badge')
    setPermissionIds([])
    setViewMode('form')
    setAssignRole(null)
    setAssignableUsers([])
    setSelectedUserIds(new Set())
    setConfirmOpen(false)
  }

  function startEditRole(role: CustomRole) {
    setEditingRoleId(role.roleId)
    setName(role.name)
    setDescription(role.description ?? '')
    setIconName(role.iconName ?? 'fas:id-badge')
    setPermissionIds(role.permissionIds)
    setViewMode('form')
    setAssignRole(null)
  }

  async function openAssignFlow(role: CustomRole) {
    setError('')
    setAssignLoading(true)
    setAssignRole(role)
    setViewMode('assign')
    setSelectedUserIds(new Set())
    try {
      const { data } = await infrastructureApi.listAssignableUsers(role.roleId)
      setAssignableUsers(data)
    } catch {
      setError('Could not load users for assignment.')
      setViewMode('form')
      setAssignRole(null)
    } finally {
      setAssignLoading(false)
    }
  }

  async function handleSaveRole(e: React.FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      const payload = {
        name,
        description,
        iconName,
        permissionIds,
      }

      if (editingRoleId) {
        await infrastructureApi.updateRole(editingRoleId, payload)
        resetRoleForm()
        await load()
      } else {
        const { data } = await infrastructureApi.createRole(payload)
        await load()
        await openAssignFlow(data)
        setEditingRoleId(null)
        setName('')
        setDescription('')
        setPermissionIds([])
      }
    } catch (err: unknown) {
      setError(axiosMessage(err) ?? 'Could not save that role.')
    } finally {
      setSaving(false)
    }
  }

  async function savePlacement(roleId: string) {
    setError('')
    try {
      await infrastructureApi.setRolePlacement(roleId, { claimHostRoomId: placementRoomId || null })
      setPlacementRoleId(null)
      setPlacementRoomId('')
      await load()
    } catch (err: unknown) {
      setError(axiosMessage(err) ?? 'Could not assign role to a claim room.')
    }
  }

  async function handleDelete(roleId: string) {
    if (!window.confirm('Delete this custom role?')) return
    try {
      await infrastructureApi.deleteRole(roleId)
      if (editingRoleId === roleId || assignRole?.roleId === roleId) resetRoleForm()
      await load()
    } catch {
      setError('Could not delete that role.')
    }
  }

  function toggleUserSelection(userId: string) {
    setSelectedUserIds((prev) => {
      const next = new Set(prev)
      if (next.has(userId)) next.delete(userId)
      else next.add(userId)
      return next
    })
  }

  async function confirmAssignments() {
    if (!assignRole || selectedUserIds.size === 0) return
    setAssignLoading(true)
    setError('')
    try {
      const users = assignableUsers
        .filter((user) => selectedUserIds.has(user.userId))
        .map((user) => ({
          userId: user.userId,
          tenantDatabaseName: user.tenantDatabaseName ?? undefined,
        }))

      await infrastructureApi.bulkAssignRole(assignRole.roleId, users)
      setConfirmOpen(false)
      resetRoleForm()
      await load()
    } catch (err: unknown) {
      setError(axiosMessage(err) ?? 'Could not assign the selected roles.')
    } finally {
      setAssignLoading(false)
    }
  }

  const claimRoomOptions = [
    { id: GET_ROLES_ROOM_ID, label: 'Get Roles (built-in)' },
    ...channels.map((c) => ({ id: c.roomId, label: c.displayName })),
  ]

  const selectableUsers = assignableUsers.filter((user) => user.canAssign && !user.alreadyAssigned)

  return (
    <div className="server-page">
      <ServerMaintenanceNav title="User Config" />

      <header className="server-page-header">
        <div className="server-page-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas['users-gear']} />
        </div>
        <div>
          <h2>User Config</h2>
          <p className="server-page-subtitle">
            Create custom roles with icons, then assign them to users below your platform role level.
          </p>
        </div>
      </header>

      {error && <p className="error">{error}</p>}

      {viewMode === 'assign' && assignRole && (
        <section className="server-page-card">
          <h3>Assign “{assignRole.name}”</h3>
          <p className="server-page-subtitle">
            Select users who may receive this role. You can only assign to users whose highest platform role is below yours.
          </p>

          {assignLoading && <p className="chat-sidebar-status">Loading users…</p>}

          {!assignLoading && selectableUsers.length === 0 && (
            <p className="infra-meta">No eligible users are available for assignment right now.</p>
          )}

          {!assignLoading && selectableUsers.length > 0 && (
            <ul className="assignable-user-list">
              {selectableUsers.map((user) => (
                <li key={`${user.userId}:${user.tenantDatabaseName ?? 'master'}`}>
                  <label className="assignable-user-row">
                    <input
                      type="checkbox"
                      checked={selectedUserIds.has(user.userId)}
                      onChange={() => toggleUserSelection(user.userId)}
                    />
                    <span>
                      <strong>{user.username}</strong>
                      <span className="infra-meta">
                        {' '}
                        · {user.highestPlatformRoleName}
                        {user.tenantDatabaseName ? ` · ${user.tenantDatabaseName}` : ''}
                      </span>
                    </span>
                  </label>
                </li>
              ))}
            </ul>
          )}

          <div className="infra-list-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={selectedUserIds.size === 0 || assignLoading}
              onClick={() => setConfirmOpen(true)}
            >
              Confirm selection ({selectedUserIds.size})
            </button>
            <button type="button" className="btn-secondary" onClick={resetRoleForm}>
              Back to roles
            </button>
          </div>

          {confirmOpen && (
            <div className="confirm-modal-backdrop" role="presentation">
              <div className="confirm-modal" role="dialog" aria-modal="true">
                <p>
                  Assign <strong>{assignRole.name}</strong> to {selectedUserIds.size} user
                  {selectedUserIds.size === 1 ? '' : 's'}?
                </p>
                <div className="infra-list-actions">
                  <button type="button" className="btn-primary" disabled={assignLoading} onClick={() => void confirmAssignments()}>
                    Yes
                  </button>
                  <button type="button" className="btn-secondary" onClick={() => setConfirmOpen(false)}>
                    No
                  </button>
                </div>
              </div>
            </div>
          )}
        </section>
      )}

      {viewMode === 'form' && (
        <section className="server-page-card">
          <h3>{editingRoleId ? 'Edit custom role' : 'Create custom role'}</h3>
          <form className="server-stub-form" onSubmit={handleSaveRole}>
            <label htmlFor="role-name">Role name</label>
            <input id="role-name" type="text" value={name} onChange={(e) => setName(e.target.value)} required />

            <label htmlFor="role-description">Description</label>
            <textarea id="role-description" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />

            <label htmlFor="role-icon">Icon</label>
            <div className="custom-role-icon-picker">
              {CUSTOM_ROLE_ICON_OPTIONS.map((option) => (
                <button
                  key={option.id}
                  type="button"
                  className={`custom-role-icon-option ${iconName === option.id ? 'selected' : ''}`}
                  onClick={() => setIconName(option.id)}
                  title={option.label}
                >
                  <FontAwesomeIcon icon={option.icon} />
                </button>
              ))}
            </div>

            <fieldset className="permission-grid">
              <legend>Permissions</legend>
              {MODERATION_PERMISSIONS.map((perm) => (
                <label key={perm.id} className="permission-check">
                  <input type="checkbox" checked={permissionIds.includes(perm.id)} onChange={() => togglePermission(perm.id)} />
                  {perm.label}
                </label>
              ))}
            </fieldset>
            <div className="infra-list-actions">
              <button type="submit" className="btn-primary" disabled={saving}>
                {saving ? 'Saving…' : editingRoleId ? 'Save changes' : 'Create role & assign users'}
              </button>
              {editingRoleId && (
                <button type="button" className="btn-secondary" onClick={resetRoleForm}>Cancel edit</button>
              )}
            </div>
          </form>
        </section>
      )}

      <section className="server-page-card">
        <h3>Custom roles</h3>
        {roles.length === 0 && <p className="server-page-subtitle">No custom roles yet.</p>}
        <ul className="infra-list">
          {roles.map((role) => (
            <li key={role.roleId} className="infra-list-item">
              <div>
                <strong>
                  <FontAwesomeIcon icon={resolveCustomRoleIcon(role.iconName)} className="custom-role-list-icon" />
                  {' '}
                  {role.name}
                </strong>
                {role.description && <p>{role.description}</p>}
                <p className="infra-meta">
                  Claim page:{' '}
                  {role.claimHostRoomId
                    ? claimRoomOptions.find((o) => o.id === role.claimHostRoomId)?.label ?? role.claimHostRoomId
                    : 'Not assigned'}
                </p>
              </div>
              <div className="infra-list-actions">
                <button type="button" className="btn-secondary" onClick={() => startEditRole(role)}>Edit</button>
                <button type="button" className="btn-secondary" onClick={() => void openAssignFlow(role)}>Assign users</button>
                <button type="button" className="btn-secondary" onClick={() => { setPlacementRoleId(role.roleId); setPlacementRoomId(role.claimHostRoomId ?? '') }}>
                  Set claim room
                </button>
                <button type="button" className="btn-secondary" onClick={() => void handleDelete(role.roleId)}>Delete</button>
              </div>
              {placementRoleId === role.roleId && (
                <div className="infra-inline-form">
                  <select value={placementRoomId} onChange={(e) => setPlacementRoomId(e.target.value)}>
                    <option value="">Not on any claim page</option>
                    {claimRoomOptions.map((opt) => (
                      <option key={opt.id} value={opt.id}>{opt.label}</option>
                    ))}
                  </select>
                  <button type="button" className="btn-primary" onClick={() => void savePlacement(role.roleId)}>Save placement</button>
                </div>
              )}
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}

function axiosMessage(err: unknown): string | undefined {
  if (typeof err === 'object' && err !== null && 'response' in err) {
    return (err as { response?: { data?: { message?: string } } }).response?.data?.message
  }
  return undefined
}
