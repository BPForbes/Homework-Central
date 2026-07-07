import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { infrastructureApi } from '../api/infrastructureApi'
import { chatApi } from '../api/chatApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { ModerationRiskModal } from '../components/infrastructure/ModerationRiskModal'
import { ChatRoomIcon } from '../components/chat/ChatRoomIcon'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import {
  CUSTOM_ROOM_ICON_OPTIONS,
  defaultCustomRoomIcon,
  resolveCustomRoomIcon,
} from '../components/infrastructure/customRoomIcons'
import {
  PLATFORM_ROLES,
  type CustomChannel,
  type CustomChannelAccessRuleInput,
  type CustomRole,
  type CustomRoomType,
} from '../types/infrastructure'
import type { ChatNavCategory } from '../types/chat'

const ROOM_TYPES: { value: CustomRoomType; label: string }[] = [
  { value: 'Chat', label: 'Chat (messages)' },
  { value: 'Info', label: 'Info page' },
  { value: 'RoleClaim', label: 'Role claim page' },
]

function rulesFromChannel(channel: CustomChannel): CustomChannelAccessRuleInput[] {
  return channel.accessRules.map((r) => ({
    customRoleId: r.customRoleId ?? undefined,
    platformRoleBit: r.platformRoleBit ?? undefined,
  }))
}

export function ServerMaintenance() {
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [customRoles, setCustomRoles] = useState<CustomRole[]>([])
  const [navCategories, setNavCategories] = useState<ChatNavCategory[]>([])
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingChannel, setEditingChannel] = useState<CustomChannel | null>(null)

  const [displayName, setDisplayName] = useState('')
  const [iconName, setIconName] = useState(defaultCustomRoomIcon('Chat'))
  const [categoryKey, setCategoryKey] = useState('')
  const [categoryDisplayName, setCategoryDisplayName] = useState('')
  const [roomType, setRoomType] = useState<CustomRoomType>('Chat')
  const [isPrivate, setIsPrivate] = useState(false)
  const [infoContent, setInfoContent] = useState('')
  const [accessRules, setAccessRules] = useState<CustomChannelAccessRuleInput[]>([])
  const [selectedCustomRoleId, setSelectedCustomRoleId] = useState('')
  const [selectedPlatformBit, setSelectedPlatformBit] = useState('')

  const [riskModalOpen, setRiskModalOpen] = useState(false)
  const [riskPermissions, setRiskPermissions] = useState<string[]>([])
  const [pendingPayload, setPendingPayload] = useState<{ mode: 'create' | 'update'; body: Record<string, unknown>; channelId?: string } | null>(null)

  const load = async () => {
    try {
      const [channelsRes, rolesRes, navRes] = await Promise.all([
        infrastructureApi.listChannels(),
        infrastructureApi.listRoles(),
        chatApi.getNav(),
      ])
      setChannels(channelsRes.data)
      setCustomRoles(rolesRes.data)
      setNavCategories(navRes.data.categories)
    } catch {
      setError('Could not load server configuration.')
    }
  }

  useEffect(() => {
    void load()
  }, [])

  function resetForm() {
    setEditingId(null)
    setEditingChannel(null)
    setDisplayName('')
    setIconName(defaultCustomRoomIcon('Chat'))
    setCategoryKey('')
    setCategoryDisplayName('')
    setRoomType('Chat')
    setIsPrivate(false)
    setInfoContent('')
    setAccessRules([])
  }

  function startEdit(channel: CustomChannel) {
    setEditingId(channel.channelId)
    setEditingChannel(channel)
    setDisplayName(channel.displayName)
    setIconName(channel.iconName ?? defaultCustomRoomIcon(channel.roomType))
    setCategoryKey(channel.categoryKey)
    setCategoryDisplayName(channel.categoryDisplayName)
    setRoomType(channel.roomType)
    setIsPrivate(channel.isPrivate)
    setInfoContent(channel.infoContent ?? '')
    setAccessRules(rulesFromChannel(channel))
  }

  function pickCategory(key: string) {
    const cat = navCategories.find((c) => c.key === key)
    if (cat) {
      setCategoryKey(cat.key)
      setCategoryDisplayName(cat.name)
    }
  }

  function addAccessRule() {
    if (selectedCustomRoleId) {
      setAccessRules((prev) => [...prev, { customRoleId: selectedCustomRoleId }])
      setSelectedCustomRoleId('')
      return
    }
    if (selectedPlatformBit) {
      setAccessRules((prev) => [...prev, { platformRoleBit: Number(selectedPlatformBit) }])
      setSelectedPlatformBit('')
    }
  }

  function buildAccessRulesPayload(): CustomChannelAccessRuleInput[] {
    const rules: CustomChannelAccessRuleInput[] = []
    for (const rule of accessRules) {
      if (rule.customRoleId) rules.push({ customRoleId: rule.customRoleId })
      else if (rule.platformRoleBit != null) rules.push({ platformRoleBit: rule.platformRoleBit })
    }
    return rules
  }

  function buildPayload(password?: string): Record<string, unknown> {
    const payload: Record<string, unknown> = {
      displayName,
      iconName,
      categoryKey: categoryKey || 'Custom',
      categoryDisplayName: categoryDisplayName || 'Custom',
      isPrivate,
    }
    if (isPrivate) {
      payload.accessRules = buildAccessRulesPayload()
    }
    if (roomType === 'Info') {
      const originalContent = editingChannel?.infoContent ?? ''
      if (!editingId || infoContent !== originalContent) {
        payload.infoContent = infoContent
      }
    }
    if (password) payload.password = password
    return payload
  }

  async function submitPayload(payload: { mode: 'create' | 'update'; body: Record<string, unknown>; channelId?: string }) {
    setSaving(true)
    setError('')
    try {
      if (payload.mode === 'create') {
        await infrastructureApi.createChannel({ ...payload.body, roomType, tieType: 'None' })
      } else if (payload.channelId) {
        await infrastructureApi.updateChannel(payload.channelId, payload.body)
      }
      resetForm()
      await load()
    } catch (err: unknown) {
      setError(extractMessage(err) ?? 'Could not save room.')
    } finally {
      setSaving(false)
      setPendingPayload(null)
      setRiskModalOpen(false)
    }
  }

  async function maybeConfirmAndSubmit(mode: 'create' | 'update', channelId?: string) {
    if (isPrivate && accessRules.length === 0) {
      setError('Private rooms need at least one access role.')
      return
    }

    const body = buildPayload()
    if (!isPrivate && accessRules.some((r) => r.customRoleId)) {
      for (const rule of accessRules) {
        if (!rule.customRoleId) continue
        const { data } = await infrastructureApi.previewAccessRisk(rule.customRoleId, true)
        if (data.requiresPassword) {
          setRiskPermissions(data.riskyPermissions)
          setPendingPayload({ mode, body, channelId })
          setRiskModalOpen(true)
          return
        }
      }
    }

    await submitPayload({ mode, body, channelId })
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (editingId) await maybeConfirmAndSubmit('update', editingId)
    else await maybeConfirmAndSubmit('create')
  }

  async function handleArchive(channelId: string) {
    if (!window.confirm('Archive this room?')) return
    try {
      await infrastructureApi.archiveChannel(channelId)
      if (editingId === channelId) resetForm()
      await load()
    } catch {
      setError('Could not archive room.')
    }
  }

  return (
    <div className="server-page">
      <ServerMaintenanceNav title="Server Maintenance" />

      <header className="server-page-header">
        <div className="server-page-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.server} />
        </div>
        <div>
          <h2>Server Maintenance</h2>
          <p className="server-page-subtitle">
            Create or update custom chat, info, and role-claim rooms. All room types are opened via{' '}
            <code>/chat/&#123;roomId&#125;</code> in the sidebar.
          </p>
        </div>
      </header>

      {error && <p className="error">{error}</p>}

      <section className="server-page-card">
        <h3>{editingId ? 'Edit room' : 'Create room'}</h3>
        <form className="server-stub-form server-form-wide" onSubmit={handleSubmit}>
          <label htmlFor="room-name">Room name</label>
          <input id="room-name" type="text" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />

          {!editingId && (
            <>
              <label htmlFor="room-type">Room type</label>
              <select
                id="room-type"
                value={roomType}
                onChange={(e) => {
                  const nextType = e.target.value as CustomRoomType
                  setRoomType(nextType)
                  setIconName(defaultCustomRoomIcon(nextType))
                }}
              >
                {ROOM_TYPES.map((t) => (
                  <option key={t.value} value={t.value}>{t.label}</option>
                ))}
              </select>
            </>
          )}
          {editingId && <p className="infra-meta">Room type: {roomType} (cannot change after creation)</p>}

          <label>Icon</label>
          <div className="custom-role-icon-picker">
            {CUSTOM_ROOM_ICON_OPTIONS.map((option) => (
              <button
                key={option.id}
                type="button"
                className={`custom-role-icon-option ${iconName === option.id ? 'selected' : ''}`}
                onClick={() => setIconName(option.id)}
                title={option.label}
              >
                <FontAwesomeIcon icon={option.icon} />
              </button>
            ))}
          </div>

          <label htmlFor="room-category">Category</label>
          <select id="room-category" value={categoryKey} onChange={(e) => pickCategory(e.target.value)}>
            <option value="">Custom category…</option>
            {navCategories.map((cat) => (
              <option key={cat.key} value={cat.key}>{cat.name}</option>
            ))}
          </select>
          <label htmlFor="custom-cat-name">Category display name</label>
          <input
            id="custom-cat-name"
            type="text"
            value={categoryDisplayName}
            onChange={(e) => setCategoryDisplayName(e.target.value)}
            placeholder="e.g. Mathematics"
          />
          {!categoryKey && (
            <>
              <label htmlFor="custom-cat-key">Category key</label>
              <input
                id="custom-cat-key"
                type="text"
                value={categoryKey}
                onChange={(e) => setCategoryKey(e.target.value)}
                placeholder="e.g. Mathematics"
              />
            </>
          )}

          <label htmlFor="room-private">
            <input
              id="room-private"
              type="checkbox"
              checked={isPrivate}
              onChange={(e) => {
                const nextPrivate = e.target.checked
                setIsPrivate(nextPrivate)
                if (!nextPrivate)
                  setAccessRules([])
              }}
            />
            Private room (role-gated access)
          </label>

          {roomType === 'Info' && (
            <>
              <label htmlFor="info-content">Info content</label>
              <textarea id="info-content" rows={5} value={infoContent} onChange={(e) => setInfoContent(e.target.value)} />
            </>
          )}

          {isPrivate && (
          <fieldset className="access-rules-fieldset">
            <legend>Access roles (required)</legend>
            <div className="access-rule-add">
              <select value={selectedCustomRoleId} onChange={(e) => setSelectedCustomRoleId(e.target.value)}>
                <option value="">Custom role…</option>
                {customRoles.map((r) => (
                  <option key={r.roleId} value={r.roleId}>{r.name}</option>
                ))}
              </select>
              <select value={selectedPlatformBit} onChange={(e) => setSelectedPlatformBit(e.target.value)}>
                <option value="">Platform role…</option>
                {PLATFORM_ROLES.map((r) => (
                  <option key={r.bit} value={r.bit}>{r.name}</option>
                ))}
              </select>
              <button type="button" className="btn-secondary" onClick={addAccessRule}>Add</button>
            </div>
            <ul className="access-rule-list">
              {accessRules.map((rule, idx) => (
                <li key={idx}>
                  {rule.customRoleId
                    ? customRoles.find((r) => r.roleId === rule.customRoleId)?.name ?? rule.customRoleId
                    : PLATFORM_ROLES.find((r) => r.bit === rule.platformRoleBit)?.name}
                  <button type="button" className="btn-link" onClick={() => setAccessRules((prev) => prev.filter((_, i) => i !== idx))}>
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          </fieldset>
          )}

          <div className="infra-list-actions">
            <button type="submit" className="btn-primary" disabled={saving}>
              {saving ? 'Saving…' : editingId ? 'Save changes' : 'Create room'}
            </button>
            {editingId && (
              <button type="button" className="btn-secondary" onClick={resetForm}>Cancel edit</button>
            )}
          </div>
        </form>
      </section>

      <section className="server-page-card">
        <h3>Custom rooms</h3>
        {channels.length === 0 && <p className="server-page-subtitle">No custom rooms yet.</p>}
        <ul className="infra-list">
          {channels.map((channel) => (
            <li key={channel.channelId} className="infra-list-item">
              <div>
                <strong>
                  <ChatRoomIcon
                    icon={resolveCustomRoomIcon(channel.iconName, channel.roomType)}
                    isPrivate={channel.isPrivate}
                    className="custom-role-list-icon"
                    layeredClassName="chat-room-icon-layered"
                  />
                  {' '}
                  {channel.displayName}
                </strong>
                <p className="infra-meta">
                  {channel.roomType} · {channel.categoryDisplayName} · {channel.isPrivate ? 'Private' : 'Public'}
                </p>
                <p className="infra-meta"><code>{channel.roomId}</code></p>
              </div>
              <div className="infra-list-actions">
                <Link to={`/chat/${encodeURIComponent(channel.roomId)}`} className="btn-secondary">Open</Link>
                <button type="button" className="btn-secondary" onClick={() => startEdit(channel)}>Edit</button>
                <button type="button" className="btn-secondary" onClick={() => void handleArchive(channel.channelId)}>Archive</button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <ModerationRiskModal
        open={riskModalOpen}
        riskyPermissions={riskPermissions}
        onCancel={() => { setRiskModalOpen(false); setPendingPayload(null) }}
        onConfirm={async (password) => {
          if (pendingPayload) await submitPayload({ ...pendingPayload, body: { ...pendingPayload.body, password } })
        }}
      />
    </div>
  )
}

function extractMessage(err: unknown): string | undefined {
  if (typeof err === 'object' && err !== null && 'response' in err) {
    return (err as { response?: { data?: { message?: string } } }).response?.data?.message
  }
  return undefined
}
