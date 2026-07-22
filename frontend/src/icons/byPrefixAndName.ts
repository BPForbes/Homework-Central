import {
  faArrowDown,
  faBackwardStep,
  faDownload,
  faEnvelope,
  faExclamation,
  faFileCircleExclamation,
  faPause,
  faPlay,
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
    'backward-step': faBackwardStep,
    download: faDownload,
    exclamation: faExclamation,
    'file-circle-exclamation': faFileCircleExclamation,
    pause: faPause,
    play: faPlay,
  },
  far: {
    comments: faComments,
  },
} as const
