import { useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCheck } from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../../api/infrastructureApi'
import { useAuth } from '../../context/AuthContext'
import type { ClaimableCustomRole, CustomChannel } from '../../types/infrastructure'

interface CustomInfoRoomPanelProps {
  roomId: string
  canEdit: boolean
}

export function CustomInfoRoomPanel({ roomId, canEdit }: CustomInfoRoomPanelProps) {
  const [channel, setChannel] = useState<CustomChannel | null>(null)
  const [content, setContent] = useState('')
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    void infrastructureApi
      .getChannelByRoom(roomId)
      .then(({ data }) => {
        setChannel(data)
        setContent(data.infoContent ?? '')
      })
      .catch(() => setError('Could not load info page.'))
  }, [roomId])

  async function handleSave(e: React.FormEvent) {
    e.preventDefault()
    if (!channel) return
    setSaving(true)
    setError('')
    try {
      const { data } = await infrastructureApi.updateChannel(channel.channelId, { infoContent: content })
      setChannel(data)
    } catch (err: unknown) {
      const msg =
        typeof err === 'object' && err !== null && 'response' in err
          ? (err as { response?: { data?: { message?: string } } }).response?.data?.message
          : undefined
      setError(msg ?? 'Could not save info content.')
    } finally {
      setSaving(false)
    }
  }

  if (error && !channel) return <p className="chat-room-error">{error}</p>
  if (!channel) return <p className="chat-room-status">Loading info…</p>

  return (
    <div className="custom-info-panel">
      {canEdit && channel.canEditInfo ? (
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
        <div className="custom-info-content">{channel.infoContent || 'No content yet.'}</div>
      )}
      {!channel.canEditInfo && canEdit && (
        <p className="dashboard-hint">
          This info room is older than three days. Only Owner or System Administrator can edit it now.
        </p>
      )}
    </div>
  )
}

interface CustomRoleClaimPanelProps {
  roomId: string
}

export function CustomRoleClaimPanel({ roomId }: CustomRoleClaimPanelProps) {
  const { refreshUser } = useAuth()
  const [roles, setRoles] = useState<ClaimableCustomRole[]>([])
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [roomId])

  async function load() {
    try {
      const { data } = await infrastructureApi.getClaimableRoles(roomId)
      setRoles(data)
    } catch {
      setError('Could not load claimable roles.')
    }
  }

  async function toggle(role: ClaimableCustomRole) {
    setPending(role.roleId)
    setError('')
    try {
      if (role.claimed) {
        await infrastructureApi.unclaimRole(role.roleId)
      } else {
        await infrastructureApi.claimRole(role.roleId, roomId)
      }
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
      {roles.length === 0 && <p className="chat-room-status">No custom roles are hosted on this claim page yet.</p>}
      <div className="get-roles-grid">
        {roles.map((role) => (
          <button
            key={role.roleId}
            type="button"
            className={`get-roles-button ${role.claimed ? 'claimed' : ''}`}
            onClick={() => void toggle(role)}
            disabled={pending !== null}
          >
            <span>{role.name}</span>
            {role.description && <small>{role.description}</small>}
            {role.claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
          </button>
        ))}
      </div>
    </div>
  )
}
