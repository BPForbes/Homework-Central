import { useCallback, useEffect, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faCheck } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../../context/AuthContext'
import { subjectsApi } from '../../api/subjectsApi'
import { infrastructureApi } from '../../api/infrastructureApi'
import { GET_ROLES_ROOM_ID } from '../../types/chat'
import type { ClaimableCustomRole } from '../../types/infrastructure'
import type { ClaimableSubject } from '../../types/subjects'
import { getCategoryIcon } from '../chat/chatIcons'
import { resolveCustomRoleIcon } from './customRoleIcons'

/** Built-in and custom role claim UI — used from /chat/general:get-roles and role-claim rooms. */
export function GetRolesPanel({ roomId = GET_ROLES_ROOM_ID }: { roomId?: string }) {
  const { refreshUser } = useAuth()
  const [subjects, setSubjects] = useState<ClaimableSubject[] | null>(null)
  const [customRoles, setCustomRoles] = useState<ClaimableCustomRole[]>([])
  const [error, setError] = useState('')
  const [pending, setPending] = useState<string | null>(null)
  const isBuiltIn = roomId === GET_ROLES_ROOM_ID

  const load = useCallback(async () => {
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
  }, [isBuiltIn, roomId])

  useEffect(() => {
    void load()
  }, [load])

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
    <div>
      {isBuiltIn && (
        <p className="text-sm text-muted-foreground mb-6">
          Click a subject to claim it as one of your roles. Click a claimed subject to drop it.
        </p>
      )}
      {error && <p className="text-sm text-destructive mb-4">{error}</p>}

      {isBuiltIn && (
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3">
          {subjects?.map((subject) => (
            <button
              key={subject.name}
              type="button"
              className={`flex flex-col items-center justify-center gap-3 px-4 py-6 rounded-2xl border transition-all text-sm font-semibold select-none ${
                subject.claimed
                  ? 'border-primary bg-primary/8 text-primary shadow-sm'
                  : 'border-border bg-card text-foreground hover:border-primary/40 hover:bg-secondary/40'
              }`}
              onClick={() => void toggleSubject(subject)}
              disabled={pending !== null}
            >
              <FontAwesomeIcon icon={getCategoryIcon(subject.name.replace(/\s/g, ''))} className="text-primary text-2xl" />
              <span>{subject.name}</span>
              {subject.claimed && <FontAwesomeIcon icon={faCheck} className="text-green-600 text-sm" />}
            </button>
          ))}
        </div>
      )}

      {customRoles.length > 0 && (
        <>
          {isBuiltIn && <h3 className="text-base font-semibold text-foreground mt-6 mb-3">Custom roles</h3>}
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 gap-3">
            {customRoles.map((role) => (
              <button
                key={role.roleId}
                type="button"
                className={`flex flex-col items-center justify-center gap-3 px-4 py-6 rounded-2xl border transition-all text-sm font-semibold select-none ${
                  role.claimed
                    ? 'border-primary bg-primary/8 text-primary shadow-sm'
                    : 'border-border bg-card text-foreground hover:border-primary/40 hover:bg-secondary/40'
                }`}
                onClick={() => void toggleCustom(role)}
                disabled={pending !== null}
              >
                <FontAwesomeIcon icon={resolveCustomRoleIcon(role.iconName)} className="text-primary text-2xl" />
                <span>{role.name}</span>
                {role.claimed && <FontAwesomeIcon icon={faCheck} className="text-green-600 text-sm" />}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
