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
} from '../types/infrastructure'

export function UserConfig() {
  const [roles, setRoles] = useState<CustomRole[]>([])
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [error, setError] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [permissionIds, setPermissionIds] = useState<number[]>([])
  const [saving, setSaving] = useState(false)
  const [placementRoleId, setPlacementRoleId] = useState<string | null>(null)
  const [placementRoomId, setPlacementRoomId] = useState('')

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

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError('')
    try {
      await infrastructureApi.createRole({ name, description, permissionIds })
      setName('')
      setDescription('')
      setPermissionIds([])
      await load()
    } catch {
      setError('Could not create that role.')
    } finally {
      setSaving(false)
    }
  }

  async function savePlacement(roleId: string) {
    setError('')
    try {
      await infrastructureApi.setRolePlacement(roleId, {
        claimHostRoomId: placementRoomId || null,
      })
      setPlacementRoleId(null)
      setPlacementRoomId('')
      await load()
    } catch (err: unknown) {
      const msg =
        axiosMessage(err) ??
        'Could not assign role to a claim room. Check for self-referential access loops.'
      setError(msg)
    }
  }

  async function handleDelete(roleId: string) {
    if (!window.confirm('Delete this custom role?')) return
    try {
      await infrastructureApi.deleteRole(roleId)
      await load()
    } catch {
      setError('Could not delete that role.')
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
            Create custom roles and assign permissions. Roles are not added to any room automatically —
            use placement below or Server Maintenance to connect them to claim pages.
          </p>
        </div>
      </header>

      {error && <p className="error">{error}</p>}

      <section className="server-page-card">
        <h3>Create custom role</h3>
        <form className="server-stub-form" onSubmit={handleCreate}>
          <label htmlFor="role-name">Role name</label>
          <input
            id="role-name"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Event Organizer"
            required
          />
          <label htmlFor="role-description">Description</label>
          <textarea
            id="role-description"
            rows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="What members with this role can do…"
          />
          <fieldset className="permission-grid">
            <legend>Permissions</legend>
            {MODERATION_PERMISSIONS.map((perm) => (
              <label key={perm.id} className="permission-check">
                <input
                  type="checkbox"
                  checked={permissionIds.includes(perm.id)}
                  onChange={() => togglePermission(perm.id)}
                />
                {perm.label}
              </label>
            ))}
          </fieldset>
          <button type="submit" className="btn-primary" disabled={saving}>
            {saving ? 'Creating…' : 'Create role'}
          </button>
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
                    ? claimRoomOptions.find((o) => o.id === role.claimHostRoomId)?.label ??
                      role.claimHostRoomId
                    : 'Not assigned'}
                </p>
              </div>
              <div className="infra-list-actions">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => {
                    setPlacementRoleId(role.roleId)
                    setPlacementRoomId(role.claimHostRoomId ?? '')
                  }}
                >
                  Set claim room
                </button>
                <button type="button" className="btn-secondary" onClick={() => void handleDelete(role.roleId)}>
                  Delete
                </button>
              </div>
              {placementRoleId === role.roleId && (
                <div className="infra-inline-form">
                  <label htmlFor={`placement-${role.roleId}`}>Claim room</label>
                  <select
                    id={`placement-${role.roleId}`}
                    value={placementRoomId}
                    onChange={(e) => setPlacementRoomId(e.target.value)}
                  >
                    <option value="">Not on any claim page</option>
                    {claimRoomOptions.map((opt) => (
                      <option key={opt.id} value={opt.id}>
                        {opt.label}
                      </option>
                    ))}
                  </select>
                  <button type="button" className="btn-primary" onClick={() => void savePlacement(role.roleId)}>
                    Save placement
                  </button>
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
    const data = (err as { response?: { data?: { message?: string } } }).response?.data
    return data?.message
  }
  return undefined
}
