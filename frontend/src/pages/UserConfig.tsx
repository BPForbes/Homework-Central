import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { infrastructureApi } from '../api/infrastructureApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import {
  GET_ROLES_ROOM_ID,
  MODERATION_PERMISSIONS,
  type CustomChannel,
  type CustomRole,
  type InfrastructureUserLookup,
} from '../types/infrastructure'

export function UserConfig() {
  const [roles, setRoles] = useState<CustomRole[]>([])
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [permissionIds, setPermissionIds] = useState<number[]>([])
  const [saving, setSaving] = useState(false)
  const [editingRoleId, setEditingRoleId] = useState<string | null>(null)
  const [placementRoleId, setPlacementRoleId] = useState<string | null>(null)
  const [placementRoomId, setPlacementRoomId] = useState('')

  const [userQuery, setUserQuery] = useState('')
  const [userResults, setUserResults] = useState<InfrastructureUserLookup[]>([])
  const [selectedUser, setSelectedUser] = useState<InfrastructureUserLookup | null>(null)
  const [assignRoleId, setAssignRoleId] = useState('')
  const [userSearchLoading, setUserSearchLoading] = useState(false)

  const load = useCallback(async () => {
    try {
      const [rolesRes, channelsRes] = await Promise.all([
        infrastructureApi.listRoles(),
        infrastructureApi.listChannels(),
      ])
      setRoles(rolesRes.data)
      setChannels(channelsRes.data.filter((c) => c.roomType === 'RoleClaim'))
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
    setPermissionIds([])
  }

  function startEditRole(role: CustomRole) {
    setEditingRoleId(role.roleId)
    setName(role.name)
    setDescription(role.description ?? '')
    setPermissionIds(role.permissionIds)
  }

  async function handleSaveRole(e: React.FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      if (editingRoleId) {
        await infrastructureApi.updateRole(editingRoleId, { name, description, permissionIds })
      } else {
        await infrastructureApi.createRole({ name, description, permissionIds })
      }
      resetRoleForm()
      await load()
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
      if (editingRoleId === roleId) resetRoleForm()
      await load()
    } catch {
      setError('Could not delete that role.')
    }
  }

  async function searchUsers() {
    if (userQuery.trim().length < 2) {
      setUserResults([])
      return
    }
    setUserSearchLoading(true)
    setError('')
    try {
      const { data } = await infrastructureApi.searchUsers(userQuery.trim())
      setUserResults(data)
    } catch {
      setError('Could not search users.')
    } finally {
      setUserSearchLoading(false)
    }
  }

  async function selectUser(user: InfrastructureUserLookup) {
    setSelectedUser(user)
    setUserResults([])
    setUserQuery(user.username)
  }

  async function assignRoleToUser() {
    if (!selectedUser || !assignRoleId) return
    setError('')
    try {
      await infrastructureApi.assignRoleToUser(selectedUser.userId, assignRoleId)
      const { data } = await infrastructureApi.getUser(selectedUser.userId)
      setSelectedUser(data)
      setAssignRoleId('')
    } catch {
      setError('Could not assign that role.')
    }
  }

  async function revokeRoleFromUser(roleId: string) {
    if (!selectedUser) return
    setError('')
    try {
      await infrastructureApi.revokeRoleFromUser(selectedUser.userId, roleId)
      const { data } = await infrastructureApi.getUser(selectedUser.userId)
      setSelectedUser(data)
    } catch {
      setError('Could not remove that role.')
    }
  }

  const claimRoomOptions = [
    { id: GET_ROLES_ROOM_ID, label: 'Get Roles (built-in)' },
    ...channels.map((c) => ({ id: c.roomId, label: c.displayName })),
  ]

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
            Manage custom roles, assign them to users, and configure claim-page placement.
          </p>
        </div>
      </header>

      {error && <p className="error">{error}</p>}

      <section className="server-page-card">
        <h3>Look up user & assign role</h3>
        <div className="user-lookup-form">
          <label htmlFor="user-search">Search by username or email</label>
          <div className="user-lookup-row">
            <input
              id="user-search"
              type="text"
              value={userQuery}
              onChange={(e) => setUserQuery(e.target.value)}
              placeholder="e.g. alice or alice@example.com"
            />
            <button type="button" className="btn-secondary" onClick={() => void searchUsers()} disabled={userSearchLoading}>
              {userSearchLoading ? 'Searching…' : 'Search'}
            </button>
          </div>
          {userResults.length > 0 && (
            <ul className="user-lookup-results">
              {userResults.map((u) => (
                <li key={u.userId}>
                  <button type="button" className="user-lookup-result" onClick={() => void selectUser(u)}>
                    <strong>{u.username}</strong> · {u.email}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        {selectedUser && (
          <div className="user-lookup-selected">
            <p>
              <strong>{selectedUser.username}</strong> ({selectedUser.email})
            </p>
            <h4>Custom roles</h4>
            {selectedUser.customRoles.length === 0 ? (
              <p className="infra-meta">No custom roles assigned.</p>
            ) : (
              <ul className="access-rule-list">
                {selectedUser.customRoles.map((r) => (
                  <li key={r.roleId}>
                    {r.name}
                    <button type="button" className="btn-link" onClick={() => void revokeRoleFromUser(r.roleId)}>
                      Remove
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <div className="infra-inline-form">
              <select value={assignRoleId} onChange={(e) => setAssignRoleId(e.target.value)}>
                <option value="">Add custom role…</option>
                {roles.map((r) => (
                  <option key={r.roleId} value={r.roleId}>{r.name}</option>
                ))}
              </select>
              <button type="button" className="btn-primary" disabled={!assignRoleId} onClick={() => void assignRoleToUser()}>
                Assign role
              </button>
            </div>
          </div>
        )}
      </section>

      <section className="server-page-card">
        <h3>{editingRoleId ? 'Edit custom role' : 'Create custom role'}</h3>
        <form className="server-stub-form" onSubmit={handleSaveRole}>
          <label htmlFor="role-name">Role name</label>
          <input id="role-name" type="text" value={name} onChange={(e) => setName(e.target.value)} required />
          <label htmlFor="role-description">Description</label>
          <textarea id="role-description" rows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
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
              {saving ? 'Saving…' : editingRoleId ? 'Save changes' : 'Create role'}
            </button>
            {editingRoleId && (
              <button type="button" className="btn-secondary" onClick={resetRoleForm}>Cancel edit</button>
            )}
          </div>
        </form>
      </section>

      <section className="server-page-card">
        <h3>Custom roles</h3>
        {roles.length === 0 && <p className="server-page-subtitle">No custom roles yet.</p>}
        <ul className="infra-list">
          {roles.map((role) => (
            <li key={role.roleId} className="infra-list-item">
              <div>
                <strong>{role.name}</strong>
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
