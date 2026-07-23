import { useMemo, useState } from 'react'
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
import type { CustomChannel, CustomRoomType } from '../../types/infrastructure'

const ROOM_TYPES: CustomRoomType[] = ['Chat', 'Info', 'RoleClaim', 'Ticket']

interface CustomRoomListProps {
  channels: CustomChannel[]
  editingId: string | null
  onEdit: (channel: CustomChannel) => void
  onArchive: (channelId: string) => void
}

function RoomCard({
  channel,
  editing,
  onEdit,
  onArchive,
}: {
  channel: CustomChannel
  editing: boolean
  onEdit: (channel: CustomChannel) => void
  onArchive: (channelId: string) => void
}) {
  const origin = channel.isPreconfigured ? 'Preconfigured' : 'Custom'
  return (
    <li
      className={`sm-room-card ${editing ? 'sm-room-card--editing' : ''} ${
        channel.isPreconfigured ? 'sm-room-card--preconfigured' : 'sm-room-card--custom'
      }`}
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
        <div className="sm-room-card-badges">
          <span
            className={`sm-badge ${
              channel.isPreconfigured ? 'sm-badge--preconfigured' : 'sm-badge--custom'
            }`}
          >
            {origin}
          </span>
          <span className={`sm-badge ${channel.isPrivate ? 'sm-badge--private' : 'sm-badge--public'}`}>
            <FontAwesomeIcon icon={channel.isPrivate ? faLock : faGlobe} />
            {channel.isPrivate ? 'Private' : 'Public'}
          </span>
        </div>
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
  )
}

function RoomSection({
  title,
  channels,
  emptyMessage,
  editingId,
  onEdit,
  onArchive,
}: {
  title: string
  channels: CustomChannel[]
  emptyMessage: string
  editingId: string | null
  onEdit: (channel: CustomChannel) => void
  onArchive: (channelId: string) => void
}) {
  return (
    <div className="sm-room-section">
      <h4 className="sm-room-section-title">
        {title}
        <span className="sm-room-section-count">{channels.length}</span>
      </h4>
      {channels.length === 0 ? (
        <p className="server-page-subtitle sm-empty-state sm-empty-state--compact">{emptyMessage}</p>
      ) : (
        <ul className="sm-room-grid">
          {channels.map((channel) => (
            <RoomCard
              key={channel.channelId}
              channel={channel}
              editing={editingId === channel.channelId}
              onEdit={onEdit}
              onArchive={onArchive}
            />
          ))}
        </ul>
      )}
    </div>
  )
}

export function CustomRoomList({ channels, editingId, onEdit, onArchive }: CustomRoomListProps) {
  const [search, setSearch] = useState('')
  const [roomTypeFilter, setRoomTypeFilter] = useState<'all' | CustomRoomType>('all')
  const [categoryFilter, setCategoryFilter] = useState('all')

  const categoryOptions = useMemo(() => {
    const seen = new Map<string, string>()
    for (const channel of channels) {
      if (!seen.has(channel.categoryKey)) {
        seen.set(channel.categoryKey, channel.categoryDisplayName)
      }
    }
    return [...seen.entries()]
      .map(([key, label]) => ({ key, label }))
      .sort((a, b) => a.label.localeCompare(b.label))
  }, [channels])

  const filteredChannels = useMemo(() => {
    const q = search.trim().toLowerCase()
    return channels.filter((channel) => {
      if (roomTypeFilter !== 'all' && channel.roomType !== roomTypeFilter) return false
      if (categoryFilter !== 'all' && channel.categoryKey !== categoryFilter) return false
      if (!q) return true
      return (
        channel.displayName.toLowerCase().includes(q) ||
        channel.categoryDisplayName.toLowerCase().includes(q) ||
        channel.roomId.toLowerCase().includes(q)
      )
    })
  }, [channels, search, roomTypeFilter, categoryFilter])

  const preconfigured = useMemo(
    () => filteredChannels.filter((channel) => channel.isPreconfigured),
    [filteredChannels],
  )
  const custom = useMemo(
    () => filteredChannels.filter((channel) => !channel.isPreconfigured),
    [filteredChannels],
  )

  const hasActiveFilters =
    search.trim() !== '' || roomTypeFilter !== 'all' || categoryFilter !== 'all'

  return (
    <section className="sm-panel sm-list-panel">
      <div className="sm-list-toolbar">
        <h3>All rooms</h3>
        <div className="sm-list-filters">
          <label className="sm-filter">
            <span className="sm-filter-label">Type</span>
            <select
              aria-label="Filter by room type"
              value={roomTypeFilter}
              onChange={(event) => setRoomTypeFilter(event.target.value as 'all' | CustomRoomType)}
            >
              <option value="all">All types</option>
              {ROOM_TYPES.map((type) => (
                <option key={type} value={type}>
                  {type}
                </option>
              ))}
            </select>
          </label>
          <label className="sm-filter">
            <span className="sm-filter-label">Subject</span>
            <select
              aria-label="Filter by subject or category"
              value={categoryFilter}
              onChange={(event) => setCategoryFilter(event.target.value)}
            >
              <option value="all">All subjects</option>
              {categoryOptions.map((option) => (
                <option key={option.key} value={option.key}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
          <div className="sm-search">
            <FontAwesomeIcon icon={faMagnifyingGlass} className="sm-search-icon" />
            <input
              type="search"
              aria-label="Search rooms"
              placeholder="Search rooms…"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          </div>
        </div>
      </div>

      {channels.length === 0 ? (
        <p className="server-page-subtitle sm-empty-state">
          No rooms yet — create one to get started.
        </p>
      ) : filteredChannels.length === 0 ? (
        <p className="server-page-subtitle sm-empty-state">
          {hasActiveFilters ? 'No rooms match your filters.' : 'No rooms to show.'}
        </p>
      ) : (
        <div className="sm-room-sections">
          <RoomSection
            title="Preconfigured"
            channels={preconfigured}
            emptyMessage={
              hasActiveFilters
                ? 'No preconfigured rooms match your filters.'
                : 'No preconfigured rooms.'
            }
            editingId={editingId}
            onEdit={onEdit}
            onArchive={onArchive}
          />
          <RoomSection
            title="Custom"
            channels={custom}
            emptyMessage={
              hasActiveFilters ? 'No custom rooms match your filters.' : 'No custom rooms yet.'
            }
            editingId={editingId}
            onEdit={onEdit}
            onArchive={onArchive}
          />
        </div>
      )}
    </section>
  )
}
