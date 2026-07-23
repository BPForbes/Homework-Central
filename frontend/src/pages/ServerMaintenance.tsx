import { useEffect, useMemo, useState } from 'react'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faGlobe, faLock, faXmark } from '@fortawesome/free-solid-svg-icons'
import { infrastructureApi } from '../api/infrastructureApi'
import { chatApi } from '../api/chatApi'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { ModerationRiskModal } from '../components/infrastructure/ModerationRiskModal'
import { CustomRoomList } from '../components/infrastructure/CustomRoomList'
import {
  RoomAccessRulesField,
  RoomCategoryFields,
  RoomIconField,
  RoomPrivacyField,
} from '../components/infrastructure/RoomEditorFields'
import { byPrefixAndName } from '../icons/byPrefixAndName'
import { defaultCustomRoomIcon } from '../components/infrastructure/customRoomIcons'
import {
  type CustomChannel,
  type CustomChannelAccessRuleInput,
  type CustomRole,
  type CustomRoomType,
} from '../types/infrastructure'
import type { ChatNavCategory } from '../types/chat'
import {
  roomTypeToServerSection,
  serverSectionToRoomType,
  useServerNavSection,
} from '../hooks/useInfrastructureNav'


function rulesFromChannel(channel: CustomChannel): CustomChannelAccessRuleInput[] {
  return channel.accessRules.map((r) => ({
    customRoleId: r.customRoleId ?? undefined,
    platformRoleBit: r.platformRoleBit ?? undefined,
  }))
}

export function ServerMaintenance() {
  const [navSection, setNavSection] = useServerNavSection()
  const serverSection = serverSectionToRoomType(navSection)
  const [channels, setChannels] = useState<CustomChannel[]>([])
  const [customRoles, setCustomRoles] = useState<CustomRole[]>([])
  const [navCategories, setNavCategories] = useState<ChatNavCategory[]>([])
  const [error, setError] = useState('')
  const [saving, setSaving] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)

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

  useEffect(() => {
    if (editingId)
      return

    if (navSection === 'rooms') {
      resetForm()
      return
    }

    const type = serverSectionToRoomType(navSection)
    if (type === 'rooms')
      return

    resetForm()
    setRoomType(type)
    setIconName(defaultCustomRoomIcon(type))
  }, [navSection, editingId])

  const stats = useMemo(
    () => ({
      total: channels.length,
      private: channels.filter((c) => c.isPrivate).length,
      public: channels.filter((c) => !c.isPrivate).length,
    }),
    [channels],
  )

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
    setNavSection(roomTypeToServerSection(channel.roomType))
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
    return accessRules.flatMap((rule): CustomChannelAccessRuleInput[] => {
      if (rule.customRoleId)
        return [{ customRoleId: rule.customRoleId }]
      if (rule.platformRoleBit != null)
        return [{ platformRoleBit: rule.platformRoleBit }]
      return []
    })
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
      <ServerMaintenanceNav title="Server Maintenance" />

      <header className="sm-hero">
        <div className="sm-hero-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.server} />
        </div>
        <div className="sm-hero-copy">
          <h2>Server Maintenance</h2>
          <p className="server-page-subtitle">
            Design custom chat, info, ticket, and role-claim rooms. Every room type opens via{' '}
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

            <RoomIconField
              iconName={iconName}
              onChange={setIconName}
            />

            <RoomCategoryFields
              categoryKey={categoryKey}
              categoryDisplayName={categoryDisplayName}
              isNewCategory={isNewCategory}
              categories={navCategories}
              onCategoryPick={pickCategory}
              onCategoryKeyChange={setCategoryKey}
              onCategoryDisplayNameChange={setCategoryDisplayName}
            />

            <RoomPrivacyField
              isPrivate={isPrivate}
              onChange={(nextPrivate) => {
                setIsPrivate(nextPrivate)
                if (!nextPrivate) setAccessRules([])
              }}
            />

            {roomType === 'Info' && !editingId && (
              <p className="dashboard-hint">
                Info content is authored as dated entries after the room is created — open it from the list below
                to add the first entry.
              </p>
            )}

            {roomType === 'Ticket' && !editingId && (
              <p className="dashboard-hint">
                Ticket portal settings — intake questions, staff access, and tracking — are configured in Channel
                Builder after the room is created.
              </p>
            )}

            {isPrivate && (
              <RoomAccessRulesField
                accessRules={accessRules}
                customRoles={customRoles}
                selectedCustomRoleId={selectedCustomRoleId}
                selectedPlatformBit={selectedPlatformBit}
                onCustomRoleChange={(roleId) => {
                  setSelectedCustomRoleId(roleId)
                  setSelectedPlatformBit('')
                }}
                onPlatformRoleChange={(roleBit) => {
                  setSelectedPlatformBit(roleBit)
                  setSelectedCustomRoleId('')
                }}
                onAdd={addAccessRule}
                onRemove={(index) => setAccessRules((prev) => prev.filter((_, itemIndex) => itemIndex !== index))}
              />
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
          <CustomRoomList
            channels={channels}
            editingId={editingId}
            onEdit={startEdit}
            onArchive={(channelId) => void handleArchive(channelId)}
          />
        )}
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
