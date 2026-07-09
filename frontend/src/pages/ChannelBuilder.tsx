import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faComments, faEye, faPen } from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../api/infrastructureApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { ChatRoomIcon } from '../components/chat/ChatRoomIcon'
import { resolveCustomRoomIcon } from '../components/infrastructure/customRoomIcons'
import { InfoEntriesFeed } from '../components/infrastructure/InfoEntriesFeed'
import { RoleClaimBuilder } from '../components/infrastructure/RoleClaimBuilder'
import { ChatPreviewPanel } from '../components/infrastructure/ChatPreviewPanel'
import { MockAccountBar } from '../components/infrastructure/MockAccountBar'
import { useMockAccounts } from '../components/infrastructure/mockAccounts'
import type { CustomChannel } from '../types/infrastructure'

type BuilderMode = 'edit' | 'preview'

export function ChannelBuilder() {
  const { channelId } = useParams<{ channelId: string }>()
  const [channel, setChannel] = useState<CustomChannel | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [mode, setMode] = useState<BuilderMode>('edit')
  const mockAccounts = useMockAccounts()

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setError('')
      try {
        const { data } = await infrastructureApi.listChannels()
        const found = data.find((c) => c.channelId === channelId) ?? null
        if (!cancelled) {
          setChannel(found)
          if (!found) setError('That room could not be found.')
        }
      } catch {
        if (!cancelled) setError('Could not load this room.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [channelId])

  const showMockAccountBar = channel && mode === 'preview' && (channel.roomType === 'RoleClaim' || channel.roomType === 'Chat')

  return (
    <div className="server-page sm-page channel-builder-page">
      <ServerMaintenanceNav title="Server Maintenance" />

      <Link to="/server" className="channel-builder-back">
        <FontAwesomeIcon icon={faArrowLeft} /> Back to Server Maintenance
      </Link>

      {loading && <p className="chat-room-status">Loading room…</p>}
      {error && <p className="error">{error}</p>}

      {channel && (
        <>
          <header className="sm-hero channel-builder-hero">
            <div className="sm-hero-icon">
              <ChatRoomIcon
                icon={resolveCustomRoomIcon(channel.iconName, channel.roomType)}
                isPrivate={channel.isPrivate}
                layeredClassName="chat-room-icon-layered"
              />
            </div>
            <div className="sm-hero-copy">
              <h2>{channel.displayName}</h2>
              <p className="server-page-subtitle">
                {channel.roomType} room · {channel.categoryDisplayName}
              </p>
            </div>
            <div className="sm-segmented channel-builder-mode-toggle">
              <button
                type="button"
                className={`sm-segmented-option ${mode === 'edit' ? 'active' : ''}`}
                onClick={() => setMode('edit')}
              >
                <FontAwesomeIcon icon={faPen} />
                <span>
                  Edit
                  <small>Change configuration</small>
                </span>
              </button>
              <button
                type="button"
                className={`sm-segmented-option ${mode === 'preview' ? 'active' : ''}`}
                onClick={() => setMode('preview')}
              >
                <FontAwesomeIcon icon={faEye} />
                <span>
                  Preview
                  <small>Try it with mock accounts</small>
                </span>
              </button>
            </div>
          </header>

          {showMockAccountBar && <MockAccountBar controller={mockAccounts} />}

          <section className="sm-panel channel-builder-panel">
            {channel.roomType === 'RoleClaim' && (
              <RoleClaimBuilder
                roomId={channel.roomId}
                mode={mode}
                mockAccounts={mockAccounts.accounts}
                activeMockId={mockAccounts.activeId}
              />
            )}

            {channel.roomType === 'Chat' && mode === 'edit' && (
              <p className="server-page-subtitle channel-builder-chat-edit-hint">
                <FontAwesomeIcon icon={faComments} /> Chat rooms don't have editable content ahead of time — switch
                to Preview to try a simulated conversation between mock accounts, or edit this room's name, icon,
                and access rules from the Server Maintenance list.
              </p>
            )}

            {channel.roomType === 'Chat' && mode === 'preview' && (
              <ChatPreviewPanel
                channelDisplayName={channel.displayName}
                mockAccounts={mockAccounts.accounts}
                activeMockId={mockAccounts.activeId}
              />
            )}

            {channel.roomType === 'Info' && (
              <InfoEntriesFeed roomId={channel.roomId} readOnly={mode === 'preview'} />
            )}
          </section>
        </>
      )}
    </div>
  )
}
