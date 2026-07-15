import {
  faAward,
  faBolt,
  faBook,
  faCrown,
  faGraduationCap,
  faHeart,
  faIdBadge,
  faPalette,
  faShieldHalved,
  faStar,
  faUser,
  faUserGraduate,
  faUsers,
  faWrench,
} from '@fortawesome/free-solid-svg-icons'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'

export interface CustomRoleIconOption {
  id: string
  label: string
  icon: IconDefinition
}

/** Curated icons admins can attach to custom roles. Stored as the option id (e.g. fas:shield-halved). */
export const CUSTOM_ROLE_ICON_OPTIONS: CustomRoleIconOption[] = [
  { id: 'fas:shield-halved', label: 'Shield', icon: faShieldHalved },
  { id: 'fas:id-badge', label: 'Badge', icon: faIdBadge },
  { id: 'fas:crown', label: 'Crown', icon: faCrown },
  { id: 'fas:star', label: 'Star', icon: faStar },
  { id: 'fas:award', label: 'Award', icon: faAward },
  { id: 'fas:graduation-cap', label: 'Graduate', icon: faGraduationCap },
  { id: 'fas:user-graduate', label: 'Student', icon: faUserGraduate },
  { id: 'fas:book', label: 'Book', icon: faBook },
  { id: 'fas:palette', label: 'Art', icon: faPalette },
  { id: 'fas:heart', label: 'Heart', icon: faHeart },
  { id: 'fas:bolt', label: 'Bolt', icon: faBolt },
  { id: 'fas:users', label: 'Users', icon: faUsers },
  { id: 'fas:user', label: 'User', icon: faUser },
  { id: 'fas:wrench', label: 'Tools', icon: faWrench },
]

const iconById = new Map(CUSTOM_ROLE_ICON_OPTIONS.map((option) => [option.id, option.icon]))

export function resolveCustomRoleIcon(iconName?: string | null): IconDefinition {
  if (!iconName)
    return faIdBadge

  return iconById.get(iconName) ?? faIdBadge
}
