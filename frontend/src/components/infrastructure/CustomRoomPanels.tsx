import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCheck } from '@fortawesome/free-solid-svg-icons'
import { resolveCustomRoleIcon } from './customRoleIcons'
import { infrastructureApi } from '../../api/infrastructureApi'
import { useAuth } from '../../context/AuthContext'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from '../../constants/permissions'
import type { ClaimableCustomRole } from '../../types/infrastructure'
import type { ChatRoomDetail } from '../../types/chat'

interface CustomInfoRoomPanelProps {
  room: ChatRoomDetail
  onUpdated: (content: string) => void
}

export function CustomInfoRoomPanel({ room, onUpdated }: CustomInfoRoomPanelProps) {
  const { hasPermission } = useAuth()
  const [content, setContent] = useState(room.infoContent ?? '')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)

  const canEdit = hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT) && room.canEditInfo

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!room.customChannelId) return
    setSaving(true)
    setError('')
    try {
      const { data } = await infrastructureApi.updateChannel(room.customChannelId, { infoContent: content })
      onUpdated(data.infoContent ?? '')
    } catch {
      setError('Could not save info content.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="custom-info-panel">
      {canEdit ? (
        <form onSubmit={handleSave}>
          <textarea
            className="custom-info-editor"
            rows={12}
            value={content}
            onChange={(e) => setContent(e.target.value)}
          />
          {error && <p className="error">{error}</p>}
          <button type="submit" className="btn-primary" disabled={saving}>
            {saving ? 'Saving…' : 'Save info'}
          </button>
        </form>
      ) : (
        <div className="custom-info-content">{room.infoContent || 'No content yet.'}</div>
      )}
      {hasPermission(MANAGE_SERVER_INFRASTRUCTURE_BIT) && !room.canEditInfo && (
        <p className="dashboard-hint">
          This info room is older than three days. Only Owner or System Administrator can edit it now.
        </p>
      )}
    </div>
  )
}

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
