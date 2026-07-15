import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faComments,
  faGlobe,
  faLock,
  faMagnifyingGlass,
  faPen,
  faTrashCan,
  faWandMagicSparkles,
} from '@fortawesome/free-solid-svg-icons'
import { ChatRoomIcon } from '../chat/ChatRoomIcon'
import { resolveCustomRoomIcon } from './customRoomIcons'
import type { CustomChannel } from '../../types/infrastructure'

interface CustomRoomListProps {
  channels: CustomChannel[]
  totalChannelCount: number
  editingId: string | null
  search: string
  onSearchChange: (search: string) => void
  onEdit: (channel: CustomChannel) => void
  onArchive: (channelId: string) => void
}

export function CustomRoomList({
  channels,
  totalChannelCount,
  editingId,
  search,
  onSearchChange,
  onEdit,
  onArchive,
}: CustomRoomListProps) {
  return (
    <section className="sm-panel sm-list-panel">
      <div className="sm-list-toolbar">
        <h3>Custom rooms</h3>
        <div className="sm-search">
          <FontAwesomeIcon icon={faMagnifyingGlass} className="sm-search-icon" />
          <input
            type="search"
            aria-label="Search custom rooms"
            placeholder="Search rooms…"
            value={search}
            onChange={(event) => onSearchChange(event.target.value)}
          />
        </div>
      </div>

      {channels.length === 0 && (
        <p className="server-page-subtitle sm-empty-state">
          {totalChannelCount === 0 ? 'No custom rooms yet — create one to get started.' : 'No rooms match your search.'}
        </p>
      )}

      <ul className="sm-room-grid">
        {channels.map((channel) => (
          <li
            key={channel.channelId}
            className={`sm-room-card ${editingId === channel.channelId ? 'sm-room-card--editing' : ''}`}
          >
            <div className="sm-room-card-top">
              <div className="sm-room-card-icon">
                <ChatRoomIcon
                  icon={resolveCustomRoomIcon(channel.iconName, channel.roomType)}
                  isPrivate={channel.isPrivate}
                  layeredClassName="chat-room-icon-layered"
                />
              </div>
              <div className="sm-room-card-title">
                <strong>{channel.displayName}</strong>
                <span className="sm-room-card-category">{channel.categoryDisplayName}</span>
              </div>
              <span className={`sm-badge ${channel.isPrivate ? 'sm-badge--private' : 'sm-badge--public'}`}>
                <FontAwesomeIcon icon={channel.isPrivate ? faLock : faGlobe} />
                {channel.isPrivate ? 'Private' : 'Public'}
              </span>
            </div>

            <div className="sm-room-card-meta">
              <span className="sm-room-card-type">{channel.roomType}</span>
              <code className="sm-room-card-id">{channel.roomId}</code>
            </div>

            <div className="sm-room-card-actions">
              <Link
                to={`/chat/${encodeURIComponent(channel.roomId)}`}
                className="sm-icon-btn"
                title="Open room"
                aria-label={`Open ${channel.displayName}`}
              >
                <FontAwesomeIcon icon={faComments} />
              </Link>
              <Link
                to={`/server/channels/${channel.channelId}`}
                className="sm-icon-btn"
                title="Build & preview"
                aria-label={`Build and preview ${channel.displayName}`}
              >
                <FontAwesomeIcon icon={faWandMagicSparkles} />
              </Link>
              <button
                type="button"
                className="sm-icon-btn"
                onClick={() => onEdit(channel)}
                title="Edit room"
                aria-label={`Edit ${channel.displayName}`}
              >
                <FontAwesomeIcon icon={faPen} />
              </button>
              <button
                type="button"
                className="sm-icon-btn sm-icon-btn--danger"
                onClick={() => onArchive(channel.channelId)}
                title="Archive room"
                aria-label={`Archive ${channel.displayName}`}
              >
                <FontAwesomeIcon icon={faTrashCan} />
              </button>
            </div>
          </li>
        ))}
      </ul>
    </section>
  )
}
