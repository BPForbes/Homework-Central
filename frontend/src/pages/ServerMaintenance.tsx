import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { infrastructureApi } from '../api/infrastructureApi'
import { chatApi } from '../api/chatApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { ModerationRiskModal } from '../components/infrastructure/ModerationRiskModal'
import { byPrefixAndName } from '../icons/byPrefixAndName'
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

export function ServerMaintenance() {
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [customRoles, setCustomRoles] = useState<CustomRole[]>([])
  const [navCategories, setNavCategories] = useState<ChatNavCategory[]>([])
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)

  const [displayName, setDisplayName] = useState('')
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
  const [pendingCreate, setPendingCreate] = useState<Record<string, unknown> | null>(null)

  const load = useCallback(async () => {
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
  }, [])

  useEffect(() => {
    void load()
  }, [load])

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
      setAccessRules((prev) => [
        ...prev,
        { platformRoleBit: Number(selectedPlatformBit) },
      ])
      setSelectedPlatformBit('')
    }
  }

  function buildCreatePayload(password?: string): Record<string, unknown> {
    return {
      displayName,
      categoryKey: categoryKey || 'Custom',
      categoryDisplayName: categoryDisplayName || 'Custom',
      roomType,
      isPrivate,
      infoContent: roomType === 'Info' ? infoContent : undefined,
      tieType: 'None',
      accessRules: isPrivate ? accessRules : accessRules,
      password,
    }
  }

  async function submitCreate(payload: Record<string, unknown>) {
    setSaving(true)
    setError('')
    try {
      await infrastructureApi.createChannel(payload)
      setDisplayName('')
      setInfoContent('')
      setAccessRules([])
      setIsPrivate(false)
      await load()
    } catch (err: unknown) {
      setError(extractMessage(err) ?? 'Could not create room.')
    } finally {
      setSaving(false)
      setPendingCreate(null)
      setRiskModalOpen(false)
    }
  }

  async function handleCreate(e: React.FormEvent) {
    e.preventDefault()
    if (isPrivate && accessRules.length === 0) {
      setError('Private rooms need at least one access role.')
      return
    }

    const payload = buildCreatePayload()
    if (!isPrivate && accessRules.some((r) => r.customRoleId)) {
      for (const rule of accessRules) {
        if (!rule.customRoleId) continue
        const { data } = await infrastructureApi.previewAccessRisk(rule.customRoleId, true)
        if (data.requiresPassword) {
          setRiskPermissions(data.riskyPermissions)
          setPendingCreate(payload)
          setRiskModalOpen(true)
          return
        }
      }
    }

    await submitCreate(payload)
  }

  async function handleArchive(channelId: string) {
    if (!window.confirm('Archive this room?')) return
    try {
      await infrastructureApi.archiveChannel(channelId)
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
            Create custom chat, info, or role-claim rooms. Public or private; private rooms require
            role access. Info rooms follow edit rules for Administrators vs Owner/System Admin.
          </p>
        </div>
      </header>

      {error && <p className="error">{error}</p>}

      <section className="server-page-card">
        <h3>Create room</h3>
        <form className="server-stub-form server-form-wide" onSubmit={handleCreate}>
          <label htmlFor="room-name">Room name</label>
          <input
            id="room-name"
            type="text"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
          />

          <label htmlFor="room-type">Room type</label>
          <select id="room-type" value={roomType} onChange={(e) => setRoomType(e.target.value as CustomRoomType)}>
            {ROOM_TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>

          <label htmlFor="room-category">Category (existing or new)</label>
          <select id="room-category" value={categoryKey} onChange={(e) => pickCategory(e.target.value)}>
            <option value="">Custom category…</option>
            {navCategories.map((cat) => (
              <option key={cat.key} value={cat.key}>
                {cat.name}
              </option>
            ))}
          </select>
          {!categoryKey && (
            <>
              <label htmlFor="custom-cat-key">New category key</label>
              <input
                id="custom-cat-key"
                type="text"
                value={categoryKey}
                onChange={(e) => setCategoryKey(e.target.value)}
                placeholder="e.g. Mathematics"
              />
              <label htmlFor="custom-cat-name">New category display name</label>
              <input
                id="custom-cat-name"
                type="text"
                value={categoryDisplayName}
                onChange={(e) => setCategoryDisplayName(e.target.value)}
                placeholder="e.g. Mathematics"
              />
            </>
          )}

          <label htmlFor="room-private">
            <input
              id="room-private"
              type="checkbox"
              checked={isPrivate}
              onChange={(e) => setIsPrivate(e.target.checked)}
            />
            Private room (role-gated access)
          </label>

          {roomType === 'Info' && (
            <>
              <label htmlFor="info-content">Info content</label>
              <textarea
                id="info-content"
                rows={5}
                value={infoContent}
                onChange={(e) => setInfoContent(e.target.value)}
                placeholder="Rules, welcome text, etc."
              />
            </>
          )}

          {(isPrivate || accessRules.length > 0) && (
            <fieldset className="access-rules-fieldset">
              <legend>Access roles {isPrivate ? '(required)' : '(optional)'}</legend>
              <div className="access-rule-add">
                <select value={selectedCustomRoleId} onChange={(e) => setSelectedCustomRoleId(e.target.value)}>
                  <option value="">Custom role…</option>
                  {customRoles.map((r) => (
                    <option key={r.roleId} value={r.roleId}>
                      {r.name}
                    </option>
                  ))}
                </select>
                <select value={selectedPlatformBit} onChange={(e) => setSelectedPlatformBit(e.target.value)}>
                  <option value="">Platform role…</option>
                  {PLATFORM_ROLES.map((r) => (
                    <option key={r.bit} value={r.bit}>
                      {r.name}
                    </option>
                  ))}
                </select>
                <button type="button" className="btn-secondary" onClick={addAccessRule}>
                  Add
                </button>
              </div>
              <ul className="access-rule-list">
                {accessRules.map((rule, idx) => (
                  <li key={idx}>
                    {rule.customRoleId
                      ? customRoles.find((r) => r.roleId === rule.customRoleId)?.name ?? rule.customRoleId
                      : PLATFORM_ROLES.find((r) => r.bit === rule.platformRoleBit)?.name}
                    <button
                      type="button"
                      className="btn-link"
                      onClick={() => setAccessRules((prev) => prev.filter((_, i) => i !== idx))}
                    >
                      Remove
                    </button>
                  </li>
                ))}
              </ul>
            </fieldset>
          )}

          <button type="submit" className="btn-primary" disabled={saving}>
            {saving ? 'Creating…' : 'Create room'}
          </button>
        </form>
      </section>

      <section className="server-page-card">
        <h3>Custom rooms</h3>
        {channels.length === 0 && <p className="server-page-subtitle">No custom rooms yet.</p>}
        <ul className="infra-list">
          {channels.map((channel) => (
            <li key={channel.channelId} className="infra-list-item">
              <div>
                <strong>{channel.displayName}</strong>
                <p className="infra-meta">
                  {channel.roomType} · {channel.categoryDisplayName} ·{' '}
                  {channel.isPrivate ? 'Private' : 'Public'}
                </p>
              </div>
              <div className="infra-list-actions">
                <Link to={`/chat/${encodeURIComponent(channel.roomId)}`} className="btn-secondary">
                  Open
                </Link>
                <button type="button" className="btn-secondary" onClick={() => void handleArchive(channel.channelId)}>
                  Archive
                </button>
              </div>
            </li>
          ))}
        </ul>
      </section>

      <ModerationRiskModal
        open={riskModalOpen}
        riskyPermissions={riskPermissions}
        onCancel={() => {
          setRiskModalOpen(false)
          setPendingCreate(null)
        }}
        onConfirm={async (password) => {
          if (pendingCreate) await submitCreate({ ...pendingCreate, password })
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
