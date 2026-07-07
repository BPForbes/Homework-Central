import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faCheck } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../context/AuthContext'
import { subjectsApi } from '../api/subjectsApi'
import { infrastructureApi } from '../api/infrastructureApi'
import type { ClaimableCustomRole } from '../types/infrastructure'
import { GET_ROLES_ROOM_ID } from '../types/chat'
import type { ClaimableSubject } from '../types/subjects'
import { getCategoryIcon } from '../components/chat/chatIcons'

/** Not a chat room — a button grid where any authenticated user can self-claim or drop general
 * subject roles (Math, Science, Computer Science, ...). Reached from the General category in the
 * chat sidebar via ChatSidebar's GET_ROLES_ROOM_ID special case. */
export function GetRoles() {
  const { refreshUser } = useAuth()
  const [subjects, setSubjects] = useState<ClaimableSubject[] | null>(null)
  const [customRoles, setCustomRoles] = useState<ClaimableCustomRole[]>([])
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)

  useEffect(() => {
    void load()
  }, [])

  async function load() {
    try {
      const [subjectsRes, customRes] = await Promise.all([
        subjectsApi.getGeneral(),
        infrastructureApi.getClaimableRoles(GET_ROLES_ROOM_ID),
      ])
      setSubjects(subjectsRes.data)
      setCustomRoles(customRes.data)
    } catch {
      setError('Could not load roles. Please try again.')
    }
  }

  async function toggle(subject: ClaimableSubject) {
    setPending(subject.name)
    setError('')
    try {
      if (subject.claimed) {
        await subjectsApi.unclaim(subject.name)
      } else {
        await subjectsApi.claim(subject.name)
      }
      await load()
      try {
        await refreshUser()
      } catch {
        // Claim succeeded; JWT refresh can be retried from the dashboard.
      }
    } catch {
      setError('Could not update that role. Please try again.')
    } finally {
      setPending(null)
    }
  }

  async function toggleCustom(role: ClaimableCustomRole) {
    setPending(role.roleId)
    setError('')
    try {
      if (role.claimed) {
        await infrastructureApi.unclaimRole(role.roleId)
      } else {
        await infrastructureApi.claimRole(role.roleId, GET_ROLES_ROOM_ID)
      }
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
    <div className="get-roles-page">
      <header className="get-roles-header">
        <Link to="/dashboard" className="chat-room-back">
          <FontAwesomeIcon icon={faArrowLeft} /> Back to dashboard
        </Link>
        <h2>Get Roles</h2>
        <p>Click a subject to claim it as one of your roles. Click a claimed subject to drop it.</p>
      </header>

      {error && <p className="error">{error}</p>}

      <div className="get-roles-grid">
        {subjects?.map((subject) => (
          <button
            key={subject.name}
            type="button"
            className={`get-roles-button ${subject.claimed ? 'claimed' : ''}`}
            onClick={() => toggle(subject)}
            disabled={pending !== null}
          >
            <FontAwesomeIcon icon={getCategoryIcon(subject.name.replace(/\s/g, ''))} className="get-roles-button-icon" />
            <span>{subject.name}</span>
            {subject.claimed && <FontAwesomeIcon icon={faCheck} className="get-roles-claimed-icon" />}
          </button>
        ))}
      </div>

      {customRoles.length > 0 && (
        <>
          <h3 className="get-roles-section-title">Custom roles</h3>
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
