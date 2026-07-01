import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import {
  faAtom,
  faBook,
  faBriefcase,
  faCalculator,
  faChartLine,
  faComments,
  faGlobe,
  faFlask,
  faGraduationCap,
  faHeartPulse,
  faLandmark,
  faLanguage,
  faLaptopCode,
  faMicrochip,
  faMusic,
  faPalette,
  faShieldHalved,
  faUserGear,
  faUserGraduate,
  faUserShield,
  faUsers,
  faWrench,
} from '@fortawesome/free-solid-svg-icons'

/** Category keys from SubjectExpertiseCatalog / ChatRoomCatalog. */
const CATEGORY_ICONS: Record<string, IconDefinition> = {
  General: faGlobe,
  Mathematics: faCalculator,
  Science: faFlask,
  ComputerScience: faLaptopCode,
  Languages: faLanguage,
  History: faLandmark,
  Business: faBriefcase,
  Art: faPalette,
  Music: faMusic,
  Engineering: faWrench,
  Medicine: faHeartPulse,
  Finance: faChartLine,
  Economics: faChartLine,
  Education: faGraduationCap,
  Staff: faUserShield,
}

const ROOM_ICON_HINTS: Array<{ pattern: RegExp; icon: IconDefinition }> = [
  { pattern: /calculus|algebra|math|geometry|trigonometry|statistics|probability/i, icon: faCalculator },
  { pattern: /biology|chemistry|physics|science/i, icon: faAtom },
  { pattern: /python|java|javascript|typescript|react|programming|code|backend|frontend/i, icon: faLaptopCode },
  { pattern: /moderator|admin|staff|tutor|manager/i, icon: faUserGear },
  { pattern: /english|spanish|french|language|mandarin|japanese/i, icon: faLanguage },
  { pattern: /history/i, icon: faLandmark },
  { pattern: /music/i, icon: faMusic },
  { pattern: /art|drawing|painting/i, icon: faPalette },
  { pattern: /book|literature/i, icon: faBook },
  { pattern: /engineer|mechanical|electrical/i, icon: faMicrochip },
]

export function getCategoryIcon(categoryKey: string): IconDefinition {
  return CATEGORY_ICONS[categoryKey] ?? faComments
}

export function getRoomIcon(roomName: string, categoryKey: string): IconDefinition {
  for (const hint of ROOM_ICON_HINTS) {
    if (hint.pattern.test(roomName))
      return hint.icon
  }

  return getCategoryIcon(categoryKey)
}

export function getStaffRoomIcon(roomName: string): IconDefinition {
  if (/tutor/i.test(roomName))
    return faUserGraduate
  if (/moderator/i.test(roomName))
    return faShieldHalved
  if (/admin/i.test(roomName))
    return faUserGear
  if (/staff/i.test(roomName))
    return faUsers
  return faUserShield
}
