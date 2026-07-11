import { useCallback, useEffect, useRef, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faMagnifyingGlass, faPalette } from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../api/infrastructureApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import { useUserConfigNavSection } from '../hooks/useInfrastructureNav'
import {
  CUSTOM_ROLE_ICON_OPTIONS,
  resolveCustomRoleIcon,
} from '../components/infrastructure/customRoleIcons'
import { ColorWheelPicker } from '../components/infrastructure/ColorWheelPicker'
import {
  GET_ROLES_ROOM_ID,
  MODERATION_PERMISSIONS,
  type AssignableUser,
  type CustomChannel,
  type CustomRole,
  type InfrastructureUserLookup,
  type RoleAppearance,
} from '../types/infrastructure'

const ROLE_COLOR_SWATCHES = [
  '#e63946', '#f3722c', '#f8961e', '#f9c74f', '#90be6d', '#43aa8b',
  '#4d908e', '#277da1', '#577590', '#3a86ff', '#8338ec', '#c9184a',
  '#d62828', '#7209b7', '#2b2118', '#6c757d',
]

const HEX_COLOR_PATTERN = /^#[0-9A-Fa-f]{6}$/

type ViewMode = 'form' | 'assign'

function userSelectionKey(user: AssignableUser): string {
  return `${user.userId}:${user.tenantDatabaseName ?? 'master'}`
}

interface DialogState {
  title: string
  message: string
  actions: Array<{
    label: string
    variant: 'primary' | 'secondary'
    onClick: () => void
    disabled?: boolean
  }>
}

export function UserConfig() {
  const [activeSection, setActiveSection] = useUserConfigNavSection()
  const [roles, setRoles] = useState<CustomRole[]>([])
  const [roleAppearance, setRoleAppearance] = useState<RoleAppearance[]>([])
  const [appearanceSavingId, setAppearanceSavingId] = useState<string | null>(null)
  const [colorWheelRoleId, setColorWheelRoleId] = useState<string | null>(null)
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [dialog, setDialog] = useState<DialogState | null>(null)
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
  const [selectedUserKeys, setSelectedUserKeys] = useState<Set<string>>(new Set())
  const [assignLoading, setAssignLoading] = useState(false)
  const [confirmAssignOpen, setConfirmAssignOpen] = useState(false)
  const [userQuery, setUserQuery] = useState('')
  const [userResults, setUserResults] = useState<InfrastructureUserLookup[]>([])
  const [selectedUser, setSelectedUser] = useState<InfrastructureUserLookup | null>(null)
  const [userSearchLoading, setUserSearchLoading] = useState(false)
  const [userRoleSaving, setUserRoleSaving] = useState<string | null>(null)
  const assignRequestInFlight = useRef(false)

  const showError = useCallback((message: string) => {
    setDialog({
      title: 'Something went wrong',
      message,
      actions: [{ label: 'OK', variant: 'primary', onClick: () => setDialog(null) }],
    })
  }, [])

  const showInfo = useCallback((title: string, message: string, onClose?: () => void) => {
    setDialog({
      title,
      message,
      actions: [{
        label: 'OK',
        variant: 'primary',
        onClick: () => {
          setDialog(null)
          onClose?.()
        },
      }],
    })
  }, [])

  const load = useCallback(async () => {
    try {
      const [rolesRes, channelsRes, appearanceRes] = await Promise.all([
        infrastructureApi.listRoles(),
        infrastructureApi.listChannels(),
        infrastructureApi.listRoleAppearance(),
      ])
      setRoles(rolesRes.data)
      setRoleAppearance(appearanceRes.data)
      setChannels(channelsRes.data.filter((c: CustomChannel) => c.roomType === 'RoleClaim'))
    } catch {
      showError('Could not load custom roles.')
    }
  }, [showError])

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
    setSelectedUserKeys(new Set())
    setConfirmAssignOpen(false)
  }

  function startEditRole(role: CustomRole) {
    setActiveSection('create')
    setEditingRoleId(role.roleId)
    setName(role.name)
    setDescription(role.description ?? '')
    setIconName(role.iconName ?? 'fas:id-badge')
    setPermissionIds(role.permissionIds)
    setViewMode('form')
    setAssignRole(null)
  }

  async function openAssignFlow(role: CustomRole) {
    if (assignRequestInFlight.current)
      return
    assignRequestInFlight.current = true
    setActiveSection('manage')
    setDialog(null)
    setAssignLoading(true)
    setAssignRole(role)
    setViewMode('assign')
    setSelectedUserKeys(new Set())
    try {
      const { data } = await infrastructureApi.listAssignableUsers(role.roleId)
      setAssignableUsers(data)
      const selectable = data.filter((user) => user.canAssign && !user.alreadyAssigned)
      if (selectable.length === 0) {
        setViewMode('form')
        setAssignRole(null)
        showInfo(
          'No eligible users',
          'No users are available for assignment right now. You can only assign to users whose highest platform role is below yours.',
        )
      }
    } catch {
      showError('Could not load users for assignment.')
      resetRoleForm()
    } finally {
      assignRequestInFlight.current = false
      setAssignLoading(false)
    }
  }

  async function handleSaveRole(e: React.FormEvent) {
    e.preventDefault()
    setSaving(true)
    setDialog(null)
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
    } catch {
      showError('Could not save that role.')
    } finally {
      setSaving(false)
    }
  }

  async function savePlacement(roleId: string) {
    setDialog(null)
    try {
      await infrastructureApi.setRolePlacement(roleId, { claimHostRoomId: placementRoomId || null })
      setPlacementRoleId(null)
      setPlacementRoomId('')
      await load()
    } catch {
      showError('Could not assign role to a claim room.')
    }
  }

  function openDeleteConfirm(role: CustomRole) {
    setDialog({
      title: 'Delete custom role',
      message: `Delete the role “${role.name}”? This cannot be undone.`,
      actions: [
        {
          label: 'No',
          variant: 'secondary',
          onClick: () => setDialog(null),
        },
        {
          label: 'Yes',
          variant: 'primary',
          onClick: () => void handleDelete(role.roleId),
        },
      ],
    })
  }

  async function handleDelete(roleId: string) {
    setDialog(null)
    try {
      await infrastructureApi.deleteRole(roleId)
      if (editingRoleId === roleId || assignRole?.roleId === roleId) resetRoleForm()
      await load()
    } catch {
      showError('Could not delete that role.')
    }
  }

  function toggleUserSelection(key: string) {
    setSelectedUserKeys((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  async function confirmAssignments() {
    if (!assignRole || selectedUserKeys.size === 0) return
    setAssignLoading(true)
    setConfirmAssignOpen(false)
    setDialog(null)
    try {
      const users = assignableUsers
        .filter((user) => selectedUserKeys.has(userSelectionKey(user)))
        .map((user) => ({
          userId: user.userId,
          tenantDatabaseName: user.tenantDatabaseName ?? undefined,
        }))

      const { data } = await infrastructureApi.bulkAssignRole(assignRole.roleId, users)
      showInfo(
        'Roles assigned',
        `Assigned “${assignRole.name}” to ${data.assigned} user${data.assigned === 1 ? '' : 's'}.`,
        () => {
          resetRoleForm()
          void load()
        },
      )
    } catch {
      showError('Could not assign the selected roles.')
    } finally {
      setAssignLoading(false)
    }
  }

  function openPlacementModal(role: CustomRole) {
    setPlacementRoleId(role.roleId)
    setPlacementRoomId(role.claimHostRoomId ?? '')
  }

  function closePlacementModal() {
    setPlacementRoleId(null)
    setPlacementRoomId('')
  }

  function updateAppearanceDraft(roleId: string, patch: Partial<Pick<RoleAppearance, 'messageColor' | 'isMentionableByUsers'>>) {
    setRoleAppearance((prev) =>
      prev.map((role) => (role.roleId === roleId ? { ...role, ...patch } : role)),
    )
  }

  async function saveAppearance(role: RoleAppearance) {
    setAppearanceSavingId(role.roleId)
    setDialog(null)
    try {
      const { data } = await infrastructureApi.updateRoleAppearance(role.roleId, {
        messageColor: role.messageColor,
        isMentionableByUsers: role.isMentionableByUsers,
      })
      setRoleAppearance((prev) => prev.map((item) => (item.roleId === data.roleId ? data : item)))
    } catch {
      showError('Could not save role appearance.')
    } finally {
      setAppearanceSavingId(null)
    }
  }

  async function searchUsers() {
    if (userQuery.trim().length < 2)
      return
    setUserSearchLoading(true)
    setSelectedUser(null)
    try {
      const { data } = await infrastructureApi.searchUsers(userQuery.trim())
      setUserResults(data)
    } catch {
      showError('Could not search users.')
    } finally {
      setUserSearchLoading(false)
    }
  }

  async function selectUser(user: InfrastructureUserLookup) {
    setUserSearchLoading(true)
    try {
      const { data } = await infrastructureApi.getUserRoleManagement(user.userId, user.tenantDatabaseName)
      setSelectedUser(data)
    } catch {
      showError('Could not load that user’s roles and permissions.')
    } finally {
      setUserSearchLoading(false)
    }
  }

  async function changePlatformRole(roleName: string, grant: boolean) {
    if (!selectedUser)
      return
    setUserRoleSaving(`platform:${roleName}`)
    try {
      if (grant)
        await infrastructureApi.assignPlatformRole(selectedUser.userId, roleName, selectedUser.tenantDatabaseName)
      else
        await infrastructureApi.revokePlatformRole(selectedUser.userId, roleName, selectedUser.tenantDatabaseName)
      await selectUser(selectedUser)
    } catch {
      showError(`Could not ${grant ? 'grant' : 'revoke'} that platform role.`)
    } finally {
      setUserRoleSaving(null)
    }
  }

  async function changeCustomRole(role: CustomRole, grant: boolean) {
    if (!selectedUser)
      return
    setUserRoleSaving(`custom:${role.roleId}`)
    try {
      if (grant)
        await infrastructureApi.assignRoleToUser(selectedUser.userId, role.roleId, selectedUser.tenantDatabaseName)
      else
        await infrastructureApi.revokeRoleFromUser(selectedUser.userId, role.roleId, selectedUser.tenantDatabaseName)
      await selectUser(selectedUser)
    } catch {
      showError(`Could not ${grant ? 'grant' : 'revoke'} that custom role.`)
    } finally {
      setUserRoleSaving(null)
    }
  }

  const claimRoomOptions = [
    { id: GET_ROLES_ROOM_ID, label: 'Get Roles (built-in)' },
    ...channels.map((c) => ({ id: c.roomId, label: c.displayName })),
  ]

  const selectableUsers = assignableUsers.filter((user) => user.canAssign && !user.alreadyAssigned)
  const placementRole = placementRoleId ? roles.find((role) => role.roleId === placementRoleId) ?? null : null

  useEffect(() => {
    if (!dialog && !confirmAssignOpen && !placementRole)
      return

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape')
        return

      setDialog(null)
      setConfirmAssignOpen(false)
      setPlacementRoleId(null)
      setPlacementRoomId('')
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [dialog, confirmAssignOpen, placementRole])

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

      {activeSection === 'manage' && viewMode === 'assign' && assignRole && (
        <section className="server-page-card">
          <h3>Assign “{assignRole.name}”</h3>
          <p className="server-page-subtitle">
            Select users who may receive this role. You can only assign to users whose highest platform role is below yours.
          </p>

          {assignLoading && <p className="chat-sidebar-status">Loading users…</p>}

          {!assignLoading && selectableUsers.length > 0 && (
            <div className="assignable-user-scroll">
              <ul className="assignable-user-list">
                {selectableUsers.map((user) => {
                  const key = userSelectionKey(user)
                  return (
                    <li key={key}>
                      <label className="assignable-user-row">
                        <input
                          type="checkbox"
                          checked={selectedUserKeys.has(key)}
                          onChange={() => toggleUserSelection(key)}
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
                  )
                })}
              </ul>
            </div>
          )}

          <div className="infra-list-actions">
            <button
              type="button"
              className="btn-primary"
              disabled={selectedUserKeys.size === 0 || assignLoading}
              onClick={() => setConfirmAssignOpen(true)}
            >
              Confirm selection ({selectedUserKeys.size})
            </button>
            <button type="button" className="btn-secondary" onClick={resetRoleForm}>
              Back to roles
            </button>
          </div>
        </section>
      )}

      {activeSection === 'create' && viewMode === 'form' && (
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

      {activeSection === 'permissions' && <section className="server-page-card">
        <h3>Role appearance &amp; mentions</h3>
        <p className="server-page-subtitle">
          Set chat message colors and choose which roles standard users can @mention.
        </p>
        {roleAppearance.length === 0 && <p className="server-page-subtitle">No roles loaded.</p>}
        <ul className="infra-list role-appearance-list">
          {roleAppearance.map((role) => (
            <li key={role.roleId} className="infra-list-item role-appearance-row">
              <div className="role-appearance-name">
                <strong style={{ color: role.messageColor }}>{role.name}</strong>
                <span className="infra-meta">{role.isCustom ? 'Custom role' : 'Platform role'}</span>
              </div>
              <div className="role-appearance-controls">
                <div className="role-appearance-color">
                  <span className="role-appearance-color-label">Color</span>
                  <span
                    className="role-appearance-swatch-preview"
                    style={{ backgroundColor: HEX_COLOR_PATTERN.test(role.messageColor) ? role.messageColor : undefined }}
                    aria-hidden="true"
                  />
                  <input
                    type="text"
                    className="sm-input role-appearance-hex"
                    value={role.messageColor}
                    onChange={(e) => updateAppearanceDraft(role.roleId, { messageColor: e.target.value })}
                    pattern="^#[0-9A-Fa-f]{6}$"
                    placeholder="#RRGGBB"
                  />
                  <div className="role-appearance-swatch-grid" role="group" aria-label={`Preset colors for ${role.name}`}>
                    {ROLE_COLOR_SWATCHES.map((swatch) => (
                      <button
                        key={swatch}
                        type="button"
                        className={`role-appearance-swatch ${role.messageColor.toLowerCase() === swatch ? 'selected' : ''}`}
                        style={{ backgroundColor: swatch }}
                        title={swatch}
                        aria-label={`Use color ${swatch}`}
                        onClick={() => updateAppearanceDraft(role.roleId, { messageColor: swatch })}
                      />
                    ))}
                  </div>
                  <button
                    type="button"
                    className="btn-secondary role-appearance-wheel-toggle"
                    onClick={() => setColorWheelRoleId((prev) => (prev === role.roleId ? null : role.roleId))}
                  >
                    <FontAwesomeIcon icon={faPalette} />
                    {colorWheelRoleId === role.roleId ? 'Hide color wheel' : 'More colors'}
                  </button>
                  {colorWheelRoleId === role.roleId && (
                    <ColorWheelPicker
                      value={HEX_COLOR_PATTERN.test(role.messageColor) ? role.messageColor : '#000000'}
                      onChange={(hex) => updateAppearanceDraft(role.roleId, { messageColor: hex })}
                    />
                  )}
                </div>
                <label className="role-appearance-mentionable">
                  <input
                    type="checkbox"
                    checked={role.isMentionableByUsers}
                    onChange={(e) => updateAppearanceDraft(role.roleId, { isMentionableByUsers: e.target.checked })}
                  />
                  Mentionable by users
                </label>
                <button
                  type="button"
                  className="btn-secondary"
                  disabled={appearanceSavingId === role.roleId}
                  onClick={() => void saveAppearance(role)}
                >
                  {appearanceSavingId === role.roleId ? 'Saving…' : 'Save'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>}

      {activeSection === 'manage' && viewMode === 'form' && <section className="server-page-card">
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
                <button type="button" className="btn-secondary" onClick={() => openPlacementModal(role)}>
                  Set claim room
                </button>
                <button type="button" className="btn-secondary" onClick={() => openDeleteConfirm(role)}>Delete</button>
              </div>
            </li>
          ))}
        </ul>
      </section>}

      {activeSection === 'users' && (
        <section className="server-page-card user-role-management">
          <h3>User roles &amp; collective permissions</h3>
          <p className="server-page-subtitle">
            Search for a user, then grant or revoke roles below your own highest platform role.
          </p>
          <form
            className="user-role-search"
            onSubmit={(event) => {
              event.preventDefault()
              void searchUsers()
            }}
          >
            <input
              type="search"
              className="sm-input"
              value={userQuery}
              onChange={(event) => setUserQuery(event.target.value)}
              placeholder="Username or email"
              minLength={2}
            />
            <button type="submit" className="btn-primary" disabled={userSearchLoading || userQuery.trim().length < 2}>
              <FontAwesomeIcon icon={faMagnifyingGlass} /> Search
            </button>
          </form>

          {userResults.length > 0 && (
            <ul className="user-role-search-results">
              {userResults.map((user) => (
                <li key={`${user.userId}:${user.tenantDatabaseName ?? 'master'}`}>
                  <button type="button" onClick={() => void selectUser(user)}>
                    <strong>{user.username}</strong>
                    <span>{user.email} · {user.highestPlatformRoleName}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}

          {selectedUser && (
            <div className="user-role-detail">
              <header>
                <div>
                  <h4>{selectedUser.username}</h4>
                  <p>{selectedUser.email}</p>
                </div>
                <span className="sm-badge sm-badge--public">{selectedUser.highestPlatformRoleName}</span>
              </header>

              <h4>Platform roles</h4>
              <ul className="user-role-grid">
                {selectedUser.platformRoles.map((role) => (
                  <li key={role.name} className={role.isAssigned ? 'assigned' : ''}>
                    <span>{role.name} <small>bit {role.bit}</small></span>
                    {role.isAssigned ? (
                      <button
                        type="button"
                        className="btn-secondary"
                        disabled={!role.canRevoke || userRoleSaving === `platform:${role.name}`}
                        onClick={() => void changePlatformRole(role.name, false)}
                      >
                        Revoke
                      </button>
                    ) : (
                      <button
                        type="button"
                        className="btn-secondary"
                        disabled={!role.canGrant || userRoleSaving === `platform:${role.name}`}
                        onClick={() => void changePlatformRole(role.name, true)}
                      >
                        Grant
                      </button>
                    )}
                  </li>
                ))}
              </ul>

              <h4>Custom roles</h4>
              <ul className="user-role-grid">
                {roles.map((role) => {
                  const assigned = selectedUser.customRoles.some((item) => item.roleId === role.roleId)
                  return (
                    <li key={role.roleId} className={assigned ? 'assigned' : ''}>
                      <span>{role.name}</span>
                      <button
                        type="button"
                        className="btn-secondary"
                        disabled={userRoleSaving === `custom:${role.roleId}`}
                        onClick={() => void changeCustomRole(role, !assigned)}
                      >
                        {assigned ? 'Revoke' : 'Grant'}
                      </button>
                    </li>
                  )
                })}
              </ul>

              <h4>Effective permissions from all roles</h4>
              <div className="permission-grid user-effective-permissions">
                {MODERATION_PERMISSIONS.map((permission) => (
                  <span key={permission.id} className={selectedUser.effectivePermissionIds.includes(permission.id) ? 'active' : ''}>
                    {permission.label}
                  </span>
                ))}
              </div>
            </div>
          )}
        </section>
      )}

      {confirmAssignOpen && assignRole && (
        <div className="confirm-modal-backdrop" role="presentation" onClick={() => setConfirmAssignOpen(false)}>
          <div
            className="confirm-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="confirm-assign-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h4 id="confirm-assign-title" className="confirm-modal-title">Confirm assignment</h4>
            <p>
              Assign <strong>{assignRole.name}</strong> to {selectedUserKeys.size} user
              {selectedUserKeys.size === 1 ? '' : 's'}?
            </p>
            <div className="confirm-modal-actions">
              <button
                type="button"
                className="btn-secondary confirm-modal-button"
                disabled={assignLoading}
                onClick={() => setConfirmAssignOpen(false)}
              >
                No
              </button>
              <button
                type="button"
                className="btn-primary confirm-modal-button"
                disabled={assignLoading}
                onClick={() => void confirmAssignments()}
              >
                Yes
              </button>
            </div>
          </div>
        </div>
      )}

      {placementRole && (
        <div className="confirm-modal-backdrop" role="presentation" onClick={closePlacementModal}>
          <div
            className="confirm-modal confirm-modal-wide"
            role="dialog"
            aria-modal="true"
            aria-labelledby="placement-modal-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h4 id="placement-modal-title" className="confirm-modal-title">Set claim room</h4>
            <p className="server-page-subtitle">
              Choose where users can claim “{placementRole.name}”.
            </p>
            <select
              className="confirm-modal-select"
              value={placementRoomId}
              onChange={(e) => setPlacementRoomId(e.target.value)}
            >
              <option value="">Not on any claim page</option>
              {claimRoomOptions.map((opt) => (
                <option key={opt.id} value={opt.id}>{opt.label}</option>
              ))}
            </select>
            <div className="confirm-modal-actions">
              <button type="button" className="btn-secondary confirm-modal-button" onClick={closePlacementModal}>
                Cancel
              </button>
              <button
                type="button"
                className="btn-primary confirm-modal-button"
                onClick={() => void savePlacement(placementRole.roleId)}
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {dialog && (
        <div className="confirm-modal-backdrop" role="presentation" onClick={() => setDialog(null)}>
          <div
            className="confirm-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="dialog-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h4 id="dialog-title" className="confirm-modal-title">{dialog.title}</h4>
            <p>{dialog.message}</p>
            <div className={`confirm-modal-actions ${dialog.actions.length > 1 ? '' : 'confirm-modal-actions-single'}`}>
              {dialog.actions.map((action) => (
                <button
                  key={action.label}
                  type="button"
                  className={`${action.variant === 'primary' ? 'btn-primary' : 'btn-secondary'} confirm-modal-button`}
                  disabled={action.disabled}
                  onClick={action.onClick}
                >
                  {action.label}
                </button>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
