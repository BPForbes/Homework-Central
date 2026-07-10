import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faCircleInfo,
  faComments,
  faGlobe,
  faIdBadge,
  faKey,
  faLayerGroup,
  faLock,
  faMagnifyingGlass,
  faPen,
  faPlus,
  faTrashCan,
  faUserShield,
  faWandMagicSparkles,
  faXmark,
} from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../api/infrastructureApi'
import { chatApi } from '../api/chatApi'
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

const ROOM_TYPES: { value: CustomRoomType; label: string; hint: string; icon: typeof faComments }[] = [
  { value: 'Chat', label: 'Chat', hint: 'Live messaging', icon: faComments },
  { value: 'Info', label: 'Info', hint: 'Static page', icon: faCircleInfo },
  { value: 'RoleClaim', label: 'Role claim', hint: 'Self-service roles', icon: faIdBadge },
]

function rulesFromChannel(channel: CustomChannel): CustomChannelAccessRuleInput[] {
  return channel.accessRules.map((r) => ({
    customRoleId: r.customRoleId ?? undefined,
    platformRoleBit: r.platformRoleBit ?? undefined,
  }))
}

export function ServerMaintenance() {
  const [serverSection, setServerSection] = useState<CustomRoomType | 'rooms'>('Chat')
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [customRoles, setCustomRoles] = useState<CustomRole[]>([])
  const [navCategories, setNavCategories] = useState<ChatNavCategory[]>([])
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const [displayName, setDisplayName] = useState('')
  const [iconName, setIconName] = useState(defaultCustomRoomIcon('Chat'))
  const [categoryKey, setCategoryKey] = useState('')
  const [categoryDisplayName, setCategoryDisplayName] = useState('')
  const [isNewCategory, setIsNewCategory] = useState(false)
  const [roomType, setRoomType] = useState<CustomRoomType>('Chat')
  const [isPrivate, setIsPrivate] = useState(false)
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

  const stats = useMemo(
    () => ({
      total: channels.length,
      private: channels.filter((c) => c.isPrivate).length,
      public: channels.filter((c) => !c.isPrivate).length,
    }),
    [channels],
  )

  const filteredChannels = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return channels
    return channels.filter(
      (c) =>
        c.displayName.toLowerCase().includes(q) ||
        c.categoryDisplayName.toLowerCase().includes(q) ||
        c.roomId.toLowerCase().includes(q),
    )
  }, [channels, search])

  function resetForm() {
    setEditingId(null)
    setDisplayName('')
    setIconName(defaultCustomRoomIcon('Chat'))
    setCategoryKey('')
    setCategoryDisplayName('')
    setIsNewCategory(false)
    setRoomType('Chat')
    setIsPrivate(false)
    setAccessRules([])
  }

  function startEdit(channel: CustomChannel) {
    setServerSection(channel.roomType)
    setEditingId(channel.channelId)
    setDisplayName(channel.displayName)
    setIconName(channel.iconName ?? defaultCustomRoomIcon(channel.roomType))
    setCategoryKey(channel.categoryKey)
    setCategoryDisplayName(channel.categoryDisplayName)
    setIsNewCategory(false)
    setRoomType(channel.roomType)
    setIsPrivate(channel.isPrivate)
    setAccessRules(rulesFromChannel(channel))
  }

  function selectCreationSection(type: CustomRoomType) {
    resetForm()
    setRoomType(type)
    setIconName(defaultCustomRoomIcon(type))
    setServerSection(type)
  }

  function pickCategory(key: string) {
    const cat = navCategories.find((c) => c.key === key)
    if (cat) {
      setCategoryKey(cat.key)
      setCategoryDisplayName(cat.name)
      setIsNewCategory(false)
    } else {
      setCategoryKey('')
      setCategoryDisplayName('')
      setIsNewCategory(true)
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
    <div className="server-page sm-page">
      <header className="sm-hero">
        <div className="sm-hero-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.server} />
        </div>
        <div className="sm-hero-copy">
          <h2>Server Maintenance</h2>
          <p className="server-page-subtitle">
            Design custom chat, info, and role-claim rooms. Every room type opens via{' '}
            <code>/chat/&#123;roomId&#125;</code> in the sidebar.
          </p>
        </div>
        <div className="sm-stat-row">
          <div className="sm-stat sm-stat--total">
            <span className="sm-stat-value">{stats.total}</span>
            <span className="sm-stat-label">Rooms</span>
          </div>
          <div className="sm-stat sm-stat--public">
            <FontAwesomeIcon icon={faGlobe} className="sm-stat-icon" />
            <span className="sm-stat-value">{stats.public}</span>
            <span className="sm-stat-label">Public</span>
          </div>
          <div className="sm-stat sm-stat--private">
            <FontAwesomeIcon icon={faLock} className="sm-stat-icon" />
            <span className="sm-stat-value">{stats.private}</span>
            <span className="sm-stat-label">Private</span>
          </div>
        </div>
      </header>

      {error && <p className="error sm-error">{error}</p>}

      <div className="infra-area-layout">
        <aside className="infra-area-sidebar" aria-label="Server configuration sections">
          <button type="button" className={serverSection === 'Chat' ? 'active' : ''} onClick={() => selectCreationSection('Chat')}>
            <FontAwesomeIcon icon={faComments} /> Create Chat Room
          </button>
          <button type="button" className={serverSection === 'RoleClaim' ? 'active' : ''} onClick={() => selectCreationSection('RoleClaim')}>
            <FontAwesomeIcon icon={faIdBadge} /> Create Role Claim
          </button>
          <button type="button" className={serverSection === 'Info' ? 'active' : ''} onClick={() => selectCreationSection('Info')}>
            <FontAwesomeIcon icon={faCircleInfo} /> Create Info Page
          </button>
          <button type="button" className={serverSection === 'rooms' ? 'active' : ''} onClick={() => { resetForm(); setServerSection('rooms') }}>
            <FontAwesomeIcon icon={faLayerGroup} /> All Rooms
          </button>
        </aside>

        <div className="infra-area-content">
      <div className="sm-layout sm-layout--single">
        {serverSection !== 'rooms' && (
        <section className="sm-panel sm-form-panel">
          <div className="sm-panel-header">
            <h3>{editingId ? 'Edit room' : 'Create room'}</h3>
            {editingId && (
              <button type="button" className="sm-panel-close" onClick={resetForm} title="Cancel edit">
                <FontAwesomeIcon icon={faXmark} />
              </button>
            )}
          </div>

          <form className="sm-form" onSubmit={handleSubmit}>
            <div className="sm-field">
              <label htmlFor="room-name" className="sm-label">Room name</label>
              <input
                id="room-name"
                type="text"
                className="sm-input"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                placeholder="e.g. Study Hall"
                required
              />
            </div>

            {!editingId ? (
              <div className="sm-field">
                <span className="sm-label">Room type</span>
                <div className="sm-segmented">
                  {ROOM_TYPES.map((t) => (
                    <button
                      key={t.value}
                      type="button"
                      className={`sm-segmented-option ${roomType === t.value ? 'active' : ''}`}
                      onClick={() => {
                        setRoomType(t.value)
                        setIconName(defaultCustomRoomIcon(t.value))
                        setServerSection(t.value)
                      }}
                    >
                      <FontAwesomeIcon icon={t.icon} />
                      <span>
                        {t.label}
                        <small>{t.hint}</small>
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            ) : (
              <div className="sm-static-chip">
                <FontAwesomeIcon icon={ROOM_TYPES.find((t) => t.value === roomType)?.icon ?? faComments} />
                {roomType} room <span className="sm-static-chip-hint">(type locked after creation)</span>
              </div>
            )}

            <div className="sm-field">
              <span className="sm-label">Icon</span>
              <div className="sm-icon-grid">
                {CUSTOM_ROOM_ICON_OPTIONS.map((option) => (
                  <button
                    key={option.id}
                    type="button"
                    className={`sm-icon-option ${iconName === option.id ? 'selected' : ''}`}
                    onClick={() => setIconName(option.id)}
                    title={option.label}
                  >
                    <FontAwesomeIcon icon={option.icon} />
                  </button>
                ))}
              </div>
            </div>

            <div className="sm-field">
              <label htmlFor="custom-cat-name" className="sm-label">
                <FontAwesomeIcon icon={faLayerGroup} /> Category
              </label>
              <select
                id="room-category"
                className="sm-input"
                value={isNewCategory ? '' : categoryKey}
                onChange={(e) => pickCategory(e.target.value)}
              >
                <option value="">New / custom category…</option>
                {navCategories.map((cat) => (
                  <option key={cat.key} value={cat.key}>{cat.name}</option>
                ))}
              </select>
              <input
                id="custom-cat-name"
                type="text"
                className="sm-input"
                value={categoryDisplayName}
                onChange={(e) => setCategoryDisplayName(e.target.value)}
                placeholder="Display name (e.g. Mathematics)"
              />
              {isNewCategory && (
                <input
                  id="custom-cat-key"
                  type="text"
                  className="sm-input"
                  value={categoryKey}
                  onChange={(e) => setCategoryKey(e.target.value)}
                  placeholder="Category key (e.g. mathematics)"
                />
              )}
            </div>

            <div className="sm-field sm-privacy-field">
              <div className="sm-privacy-row">
                <div className="sm-privacy-copy">
                  <span className="sm-label sm-label--inline">
                    <FontAwesomeIcon icon={isPrivate ? faLock : faGlobe} />
                    {isPrivate ? 'Private room' : 'Public room'}
                  </span>
                  <span className="sm-privacy-hint">
                    {isPrivate ? 'Only members with a matching role can view this room.' : 'Anyone on the server can view this room.'}
                  </span>
                </div>
                <button
                  type="button"
                  role="switch"
                  aria-checked={isPrivate}
                  className={`sm-switch ${isPrivate ? 'on' : ''}`}
                  onClick={() => {
                    const nextPrivate = !isPrivate
                    setIsPrivate(nextPrivate)
                    if (!nextPrivate) setAccessRules([])
                  }}
                >
                  <span className="sm-switch-thumb" />
                </button>
              </div>
            </div>

            {roomType === 'Info' && !editingId && (
              <p className="dashboard-hint">
                Info content is authored as dated entries after the room is created — open it from the list below
                to add the first entry.
              </p>
            )}

            {isPrivate && (
              <div className="sm-field">
                <span className="sm-label">
                  <FontAwesomeIcon icon={faUserShield} /> Access roles <span className="sm-required">required</span>
                </span>
                <div className="sm-rule-add">
                  <select
                    className="sm-input"
                    value={selectedCustomRoleId}
                    onChange={(e) => { setSelectedCustomRoleId(e.target.value); setSelectedPlatformBit('') }}
                  >
                    <option value="">Custom role…</option>
                    {customRoles.map((r) => (
                      <option key={r.roleId} value={r.roleId}>{r.name}</option>
                    ))}
                  </select>
                  <select
                    className="sm-input"
                    value={selectedPlatformBit}
                    onChange={(e) => { setSelectedPlatformBit(e.target.value); setSelectedCustomRoleId('') }}
                  >
                    <option value="">Platform role…</option>
                    {PLATFORM_ROLES.map((r) => (
                      <option key={r.bit} value={r.bit}>{r.name}</option>
                    ))}
                  </select>
                  <button
                    type="button"
                    className="sm-icon-btn sm-icon-btn--primary"
                    onClick={addAccessRule}
                    disabled={!selectedCustomRoleId && !selectedPlatformBit}
                    title="Add access rule"
                  >
                    <FontAwesomeIcon icon={faPlus} />
                  </button>
                </div>
                <div className="sm-rule-chips">
                  {accessRules.length === 0 && <span className="sm-rule-empty">No access roles yet.</span>}
                  {accessRules.map((rule, idx) => (
                    <span key={idx} className="sm-rule-chip">
                      <FontAwesomeIcon icon={rule.customRoleId ? faUserShield : faKey} />
                      {rule.customRoleId
                        ? customRoles.find((r) => r.roleId === rule.customRoleId)?.name ?? 'Unknown role'
                        : PLATFORM_ROLES.find((r) => r.bit === rule.platformRoleBit)?.name}
                      <button
                        type="button"
                        className="sm-rule-chip-remove"
                        onClick={() => setAccessRules((prev) => prev.filter((_, i) => i !== idx))}
                        aria-label="Remove access rule"
                      >
                        <FontAwesomeIcon icon={faXmark} />
                      </button>
                    </span>
                  ))}
                </div>
              </div>
            )}

            <div className="sm-form-actions">
              <button type="submit" className="btn-primary sm-submit" disabled={saving}>
                {saving ? 'Saving…' : editingId ? 'Save changes' : 'Create room'}
              </button>
              {editingId && (
                <button type="button" className="btn-secondary" onClick={resetForm}>Cancel</button>
              )}
            </div>
          </form>
        </section>
        )}

        {serverSection === 'rooms' && (
        <section className="sm-panel sm-list-panel">
          <div className="sm-list-toolbar">
            <h3>Custom rooms</h3>
            <div className="sm-search">
              <FontAwesomeIcon icon={faMagnifyingGlass} className="sm-search-icon" />
              <input
                type="search"
                placeholder="Search rooms…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </div>
          </div>

          {filteredChannels.length === 0 && (
            <p className="server-page-subtitle sm-empty-state">
              {channels.length === 0 ? 'No custom rooms yet — create one to get started.' : 'No rooms match your search.'}
            </p>
          )}

          <ul className="sm-room-grid">
            {filteredChannels.map((channel) => (
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
                  <Link to={`/chat/${encodeURIComponent(channel.roomId)}`} className="sm-icon-btn" title="Open room">
                    <FontAwesomeIcon icon={faComments} />
                  </Link>
                  <Link
                    to={`/server/channels/${channel.channelId}`}
                    className="sm-icon-btn"
                    title="Build & preview"
                  >
                    <FontAwesomeIcon icon={faWandMagicSparkles} />
                  </Link>
                  <button type="button" className="sm-icon-btn" onClick={() => startEdit(channel)} title="Edit room">
                    <FontAwesomeIcon icon={faPen} />
                  </button>
                  <button
                    type="button"
                    className="sm-icon-btn sm-icon-btn--danger"
                    onClick={() => void handleArchive(channel.channelId)}
                    title="Archive room"
                  >
                    <FontAwesomeIcon icon={faTrashCan} />
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
        )}
      </div>
        </div>
      </div>

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
