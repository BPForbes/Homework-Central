import {
  faEnvelope,
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
  },
  far: {
    comments: faComments,
  },
} as const
