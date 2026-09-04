import {
  faBook,
  faBullhorn,
  faCircleInfo,
  faComments,
  faDoorOpen,
  faFlask,
  faGamepad,
  faGlobe,
  faGraduationCap,
  faHashtag,
  faHeart,
  faIdBadge,
  faLock,
  faPalette,
  faStar,
  faTicket,
  faUsers,
  faWrench,
} from '@fortawesome/free-solid-svg-icons'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import type { CustomRoomType } from '../../types/infrastructure'

export interface CustomRoomIconOption {
  id: string
  label: string
  icon: IconDefinition
}

/** Curated icons admins can attach to custom rooms. Stored as the option id (e.g. fas:comments). */
export const CUSTOM_ROOM_ICON_OPTIONS: CustomRoomIconOption[] = [
  { id: 'fas:comments', label: 'Chat', icon: faComments },
  { id: 'fas:hashtag', label: 'Channel', icon: faHashtag },
  { id: 'fas:door-open', label: 'Room', icon: faDoorOpen },
  { id: 'fas:circle-info', label: 'Info', icon: faCircleInfo },
  { id: 'fas:id-badge', label: 'Roles', icon: faIdBadge },
  { id: 'fas:users', label: 'Group', icon: faUsers },
  { id: 'fas:bullhorn', label: 'Announce', icon: faBullhorn },
  { id: 'fas:book', label: 'Guide', icon: faBook },
  { id: 'fas:graduation-cap', label: 'Study', icon: faGraduationCap },
  { id: 'fas:palette', label: 'Creative', icon: faPalette },
  { id: 'fas:flask', label: 'Lab', icon: faFlask },
  { id: 'fas:gamepad', label: 'Fun', icon: faGamepad },
  { id: 'fas:star', label: 'Featured', icon: faStar },
  { id: 'fas:globe', label: 'Public', icon: faGlobe },
  { id: 'fas:lock', label: 'Private', icon: faLock },
  { id: 'fas:heart', label: 'Community', icon: faHeart },
  { id: 'fas:ticket', label: 'Ticket', icon: faTicket },
  { id: 'fas:wrench', label: 'Tools', icon: faWrench },
]

const iconById = new Map(CUSTOM_ROOM_ICON_OPTIONS.map((option) => [option.id, option.icon]))

export function defaultCustomRoomIcon(roomType: CustomRoomType): string {
  switch (roomType) {
    case 'Info':
      return 'fas:circle-info'
    case 'RoleClaim':
      return 'fas:id-badge'
    case 'Ticket':
      return 'fas:ticket'
    default:
      return 'fas:comments'
  }
}

export function resolveCustomRoomIcon(iconName?: string | null, roomType?: CustomRoomType): IconDefinition {
  if (iconName) {
    const resolved = iconById.get(iconName)
    if (resolved)
      return resolved
  }

  if (roomType)
    return iconById.get(defaultCustomRoomIcon(roomType)) ?? faComments

  return faComments
}
