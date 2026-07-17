import {
  faArrowDown,
  faDownload,
  faEnvelope,
  faExclamation,
  faFileCircleExclamation,
  faServer,
  faUsersGear,
} from '@fortawesome/free-solid-svg-icons'
import { faComments } from '@fortawesome/free-regular-svg-icons'

/** Icon lookup matching Font Awesome's byPrefixAndName pattern. */
export const byPrefixAndName = {
  fas: {
    envelope: faEnvelope,
    server: faServer,
    'users-gear': faUsersGear,
    'arrow-down': faArrowDown,
    download: faDownload,
    exclamation: faExclamation,
    'file-circle-exclamation': faFileCircleExclamation,
  },
  far: {
    comments: faComments,
  },
} as const
