import { useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCheck } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../../context/AuthContext'
import { subjectsApi } from '../../api/subjectsApi'
import { infrastructureApi } from '../../api/infrastructureApi'
import { GET_ROLES_ROOM_ID } from '../../types/chat'
import type { ClaimableCustomRole } from '../../types/infrastructure'
import type { ClaimableSubject } from '../../types/subjects'
import { getCategoryIcon } from '../chat/chatIcons'

/** Built-in and custom role claim UI — used from /chat/general:get-roles and role-claim rooms. */
export function GetRolesPanel({ roomId = GET_ROLES_ROOM_ID }: { roomId?: string }) {
  const { refreshUser } = useAuth()
  const [subjects, setSubjects] = useState<ClaimableSubject[] | null>(null)
  const [customRoles, setCustomRoles] = useState<ClaimableCustomRole[]>([])
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)
  const isBuiltIn = roomId === GET_ROLES_ROOM_ID

  useEffect(() => {
    void load()
  }, [roomId])

  async function load() {
    try {
      if (isBuiltIn) {
        const [subjectsRes, customRes] = await Promise.all([
          subjectsApi.getGeneral(),
          infrastructureApi.getClaimableRoles(roomId),
        ])
        setSubjects(subjectsRes.data)
        setCustomRoles(customRes.data)
      } else {
        const { data } = await infrastructureApi.getClaimableRoles(roomId)
        setSubjects([])
        setCustomRoles(data)
      }
    } catch {
      setError('Could not load roles. Please try again.')
    }
  }

  async function toggleSubject(subject: ClaimableSubject) {
    setPending(subject.name)
    setError('')
    try {
      if (subject.claimed) await subjectsApi.unclaim(subject.name)
      else await subjectsApi.claim(subject.name)
      await load()
      try {
        await refreshUser()
      } catch {
        // Claim succeeded; JWT refresh can retry later.
      }
    } catch {
      setError('Could not update that subject.')
    } finally {
      setPending(null)
    }
  }

  async function toggleCustom(role: ClaimableCustomRole) {
    setPending(role.roleId)
    setError('')
    try {
      if (role.claimed) await infrastructureApi.unclaimRole(role.roleId)
      else await infrastructureApi.claimRole(role.roleId, roomId)
      await load()
      try {
        await refreshUser()
      } catch {
        // Claim succeeded; JWT refresh can retry later.
      }
    } catch {
      setError('Could not update that custom role.')
    } finally {
      setPending(null)
    }
  }

  return (
    <div className="get-roles-panel">
      {isBuiltIn && (
        <p className="get-roles-hint">
          Click a subject to claim it as one of your roles. Click a claimed subject to drop it.
        </p>
      )}
      {error && <p className="error">{error}</p>}

      {isBuiltIn && (
        <div className="get-roles-grid">
          {subjects?.map((subject) => (
            <button
              key={subject.name}
              type="button"
              className={`get-roles-button ${subject.claimed ? 'claimed' : ''}`}
              onClick={() => void toggleSubject(subject)}
              disabled={pending !== null}
            >
              <FontAwesomeIcon icon={getCategoryIcon(subject.name.replace(/\s/g, ''))} className="get-roles-button-icon" />
              <span>{subject.name}</span>
              {subject.claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
            </button>
          ))}
        </div>
      )}

      {customRoles.length > 0 && (
        <>
          {isBuiltIn && <h3 className="get-roles-section-title">Custom roles</h3>}
          <div className="get-roles-grid">
            {customRoles.map((role) => (
              <button
                key={role.roleId}
                type="button"
                className={`get-roles-button ${role.claimed ? 'claimed' : ''}`}
                onClick={() => void toggleCustom(role)}
                disabled={pending !== null}
              >
                <span>{role.name}</span>
                {role.claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
