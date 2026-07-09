import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCheck } from '@fortawesome/free-solid-svg-icons'
import { resolveCustomRoleIcon } from './customRoleIcons'
import { infrastructureApi } from '../../api/infrastructureApi'
import { useAuth } from '../../context/AuthContext'
import type { ClaimableCustomRole } from '../../types/infrastructure'

export function CustomRoleClaimPanel({ roomId }: { roomId: string }) {
  const { refreshUser } = useAuth()
  const [roles, setRoles] = useState<ClaimableCustomRole[]>([])
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const { data } = await infrastructureApi.getClaimableRoles(roomId)
      setRoles(data)
    } catch {
      setError('Could not load claimable roles.')
    }
  }, [roomId])

  useEffect(() => {
    void load()
  }, [load])

  async function toggle(role: ClaimableCustomRole) {
    setPending(role.roleId)
    setError('')
    try {
      if (role.claimed) await infrastructureApi.unclaimRole(role.roleId)
      else await infrastructureApi.claimRole(role.roleId, roomId)
      await load()
      try {
        await refreshUser()
      } catch {
        // Claim succeeded; token refresh can retry later.
      }
    } catch {
      setError('Could not update that role.')
    } finally {
      setPending(null)
    }
  }

  return (
    <div className="custom-role-claim-panel">
      {error && <p className="error">{error}</p>}
      {roles.length === 0 && (
        <p className="chat-room-status">No custom roles are hosted on this claim page yet.</p>
      )}
      <div className="get-roles-grid">
        {roles.map((role) => (
          <button
            key={role.roleId}
            type="button"
            className={`get-roles-button ${role.claimed ? 'claimed' : ''}`}
            onClick={() => void toggle(role)}
            disabled={pending !== null}
          >
            <FontAwesomeIcon icon={resolveCustomRoleIcon(role.iconName)} className="get-roles-button-icon" />
            <span>{role.name}</span>
            {role.description && <small>{role.description}</small>}
            {role.claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
          </button>
        ))}
      </div>
    </div>
  )
}
