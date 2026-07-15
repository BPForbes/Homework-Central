import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faArrowDown,
  faArrowUp,
  faCheck,
  faFloppyDisk,
  faGripVertical,
} from '@fortawesome/free-solid-svg-icons'
import { resolveCustomRoleIcon } from './customRoleIcons'
import { infrastructureApi } from '../../api/infrastructureApi'
import type { CustomRole } from '../../types/infrastructure'
import type { MockAccount } from './mockAccounts'

interface RoleClaimBuilderProps {
  roomId: string
  mode: 'edit' | 'preview'
  mockAccounts: MockAccount[]
  activeMockId: string | null
}

function sameOrder(a: CustomRole[], b: CustomRole[]): boolean {
  return a.length === b.length && a.every((role, i) => role.roleId === b[i].roleId)
}

export function RoleClaimBuilder({ roomId, mode, mockAccounts, activeMockId }: RoleClaimBuilderProps) {
  const [savedRoles, setSavedRoles] = useState<CustomRole[]>([])
  const [roles, setRoles] = useState<CustomRole[]>([])
  const [dragIndex, setDragIndex] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [claimsByAccount, setClaimsByAccount] = useState<Record<string, Set<string>>>({})

  const load = useCallback(async () => {
    try {
      const { data } = await infrastructureApi.listClaimRolesForRoom(roomId)
      setSavedRoles(data)
      setRoles(data)
    } catch {
      setError('Could not load the roles claimable in this room.')
    }
  }, [roomId])

  useEffect(() => {
    void load()
  }, [load])

  function moveRole(from: number, to: number) {
    if (to < 0 || to >= roles.length || from === to) return
    setRoles((prev) => {
      const next = [...prev]
      const [moved] = next.splice(from, 1)
      next.splice(to, 0, moved)
      return next
    })
  }

  async function saveOrder() {
    setSaving(true)
    setError('')
    try {
      await infrastructureApi.reorderClaimRoles(roomId, roles.map((r) => r.roleId))
      setSavedRoles(roles)
    } catch {
      setError('Could not save the new role order.')
    } finally {
      setSaving(false)
    }
  }

  function toggleClaim(roleId: string) {
    if (!activeMockId) return
    setClaimsByAccount((prev) => {
      const current = new Set(prev[activeMockId] ?? [])
      if (current.has(roleId)) current.delete(roleId)
      else current.add(roleId)
      return { ...prev, [activeMockId]: current }
    })
  }

  const activeAccount = mockAccounts.find((a) => a.id === activeMockId) ?? null
  const activeClaims = activeMockId ? claimsByAccount[activeMockId] ?? new Set<string>() : new Set<string>()
  const dirty = !sameOrder(roles, savedRoles)

  if (error) return <p className="error">{error}</p>

  if (roles.length === 0)
    return <p className="chat-room-status">No custom roles are hosted on this claim page yet.</p>

  if (mode === 'edit') {
    return (
      <div className="role-claim-builder">
        <p className="server-page-subtitle">
          Drag role buttons to set the order members will see them in, or use the arrows. Save when you're happy
          with the arrangement.
        </p>
        <ul className="role-claim-order-list">
          {roles.map((role, index) => (
            <li
              key={role.roleId}
              className={`role-claim-order-item ${dragIndex === index ? 'dragging' : ''}`}
              draggable
              onDragStart={() => setDragIndex(index)}
              onDragOver={(e) => e.preventDefault()}
              onDrop={() => {
                if (dragIndex !== null) moveRole(dragIndex, index)
                setDragIndex(null)
              }}
              onDragEnd={() => setDragIndex(null)}
            >
              <span className="role-claim-order-handle" aria-hidden="true">
                <FontAwesomeIcon icon={faGripVertical} />
              </span>
              <FontAwesomeIcon icon={resolveCustomRoleIcon(role.iconName)} className="role-claim-order-icon" />
              <span className="role-claim-order-name">{role.name}</span>
              <div className="role-claim-order-controls">
                <button
                  type="button"
                  className="sm-icon-btn"
                  onClick={() => moveRole(index, index - 1)}
                  disabled={index === 0}
                  aria-label={`Move ${role.name} up`}
                >
                  <FontAwesomeIcon icon={faArrowUp} />
                </button>
                <button
                  type="button"
                  className="sm-icon-btn"
                  onClick={() => moveRole(index, index + 1)}
                  disabled={index === roles.length - 1}
                  aria-label={`Move ${role.name} down`}
                >
                  <FontAwesomeIcon icon={faArrowDown} />
                </button>
              </div>
            </li>
          ))}
        </ul>
        <button type="button" className="btn-primary" disabled={!dirty || saving} onClick={() => void saveOrder()}>
          <FontAwesomeIcon icon={faFloppyDisk} /> {saving ? 'Saving…' : 'Save order'}
        </button>
      </div>
    )
  }

  return (
    <div className="custom-role-claim-panel">
      {!activeAccount && <p className="chat-room-status">Add a mock account above to try claiming roles.</p>}
      {activeAccount && (
        <p className="server-page-subtitle">
          Previewing as <strong style={{ color: activeAccount.color }}>{activeAccount.label}</strong>. Clicks below
          only affect this mock account — nothing is saved.
        </p>
      )}
      <div className="get-roles-grid">
        {roles.map((role) => {
          const claimed = activeClaims.has(role.roleId)
          return (
            <button
              key={role.roleId}
              type="button"
              className={`get-roles-button ${claimed ? 'claimed' : ''}`}
              onClick={() => toggleClaim(role.roleId)}
              disabled={!activeAccount}
            >
              <FontAwesomeIcon icon={resolveCustomRoleIcon(role.iconName)} className="get-roles-button-icon" />
              <span>{role.name}</span>
              {role.description && <small>{role.description}</small>}
              {claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
            </button>
          )
        })}
      </div>
    </div>
  )
}
