import { Navigate } from 'react-router-dom'
import { GET_ROLES_ROOM_ID } from '../types/infrastructure'

/** Legacy route — all role claim pages including built-in Get Roles use /chat/{roomId}. */
export function GetRoles() {
  return <Navigate to={`/chat/${encodeURIComponent(GET_ROLES_ROOM_ID)}`} replace />
}
