import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import type { IconDefinition } from '@fortawesome/fontawesome-svg-core'
import {
  faCircleInfo,
  faComments,
  faGlobe,
  faIdBadge,
  faKey,
  faLayerGroup,
  faLock,
  faPlus,
  faTicket,
  faUserShield,
  faXmark,
} from '@fortawesome/free-solid-svg-icons'
import {
  CUSTOM_ROOM_ICON_OPTIONS,
  defaultCustomRoomIcon,
} from './customRoomIcons'
import {
  PLATFORM_ROLES,
  type CustomChannelAccessRuleInput,
  type CustomRole,
  type CustomRoomType,
} from '../../types/infrastructure'
import type { ChatNavCategory } from '../../types/chat'

interface RoomTypeOption {
  value: CustomRoomType
  label: string
  hint: string
  icon: IconDefinition
}

const ROOM_TYPES: RoomTypeOption[] = [
  { value: 'Chat', label: 'Chat', hint: 'Live messaging', icon: faComments },
  { value: 'Info', label: 'Info', hint: 'Static page', icon: faCircleInfo },
  { value: 'RoleClaim', label: 'Role claim', hint: 'Self-service roles', icon: faIdBadge },
  { value: 'Ticket', label: 'Ticket', hint: 'Intake portal', icon: faTicket },
]

export function RoomTypeField({
  roomType,
  editing,
  onChange,
}: {
  roomType: CustomRoomType
  editing: boolean
  onChange: (roomType: CustomRoomType, iconName: string) => void
}) {
  if (editing) {
    return (
      <div className="sm-static-chip">
        <FontAwesomeIcon icon={ROOM_TYPES.find((type) => type.value === roomType)?.icon ?? faComments} />
        {roomType} room <span className="sm-static-chip-hint">(type locked after creation)</span>
      </div>
    )
  }

  return (
    <div className="sm-field">
      <span className="sm-label">Room type</span>
      <div className="sm-segmented">
        {ROOM_TYPES.map((type) => (
          <button
            key={type.value}
            type="button"
            className={`sm-segmented-option ${roomType === type.value ? 'active' : ''}`}
            aria-pressed={roomType === type.value}
            onClick={() => onChange(type.value, defaultCustomRoomIcon(type.value))}
          >
            <FontAwesomeIcon icon={type.icon} />
            <span>
              {type.label}
              <small>{type.hint}</small>
            </span>
          </button>
        ))}
      </div>
    </div>
  )
}

export function RoomIconField({
  iconName,
  onChange,
}: {
  iconName: string
  onChange: (iconName: string) => void
}) {
  return (
    <div className="sm-field">
      <span className="sm-label">Icon</span>
      <div className="sm-icon-grid">
        {CUSTOM_ROOM_ICON_OPTIONS.map((option) => (
          <button
            key={option.id}
            type="button"
            className={`sm-icon-option ${iconName === option.id ? 'selected' : ''}`}
            aria-label={option.label}
            aria-pressed={iconName === option.id}
            onClick={() => onChange(option.id)}
            title={option.label}
          >
            <FontAwesomeIcon icon={option.icon} />
          </button>
        ))}
      </div>
    </div>
  )
}

export function RoomCategoryFields({
  categoryKey,
  categoryDisplayName,
  isNewCategory,
  categories,
  onCategoryPick,
  onCategoryKeyChange,
  onCategoryDisplayNameChange,
}: {
  categoryKey: string
  categoryDisplayName: string
  isNewCategory: boolean
  categories: ChatNavCategory[]
  onCategoryPick: (key: string) => void
  onCategoryKeyChange: (key: string) => void
  onCategoryDisplayNameChange: (name: string) => void
}) {
  return (
    <div className="sm-field">
      <label htmlFor="room-category" className="sm-label">
        <FontAwesomeIcon icon={faLayerGroup} /> Category
      </label>
      <select
        id="room-category"
        className="sm-input"
        value={isNewCategory ? '' : categoryKey}
        onChange={(event) => onCategoryPick(event.target.value)}
      >
        <option value="">New / custom category…</option>
        {categories.map((category) => (
          <option key={category.key} value={category.key}>{category.name}</option>
        ))}
      </select>
      <input
        id="custom-cat-name"
        type="text"
        className="sm-input"
        value={categoryDisplayName}
        onChange={(event) => onCategoryDisplayNameChange(event.target.value)}
        placeholder="Display name (e.g. Mathematics)"
        aria-label="Category display name"
      />
      {isNewCategory && (
        <input
          id="custom-cat-key"
          type="text"
          className="sm-input"
          value={categoryKey}
          onChange={(event) => onCategoryKeyChange(event.target.value)}
          placeholder="Category key (e.g. mathematics)"
          aria-label="Category key"
        />
      )}
    </div>
  )
}

export function RoomPrivacyField({
  isPrivate,
  onChange,
}: {
  isPrivate: boolean
  onChange: (isPrivate: boolean) => void
}) {
  return (
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
          aria-label="Restrict room access by role"
          aria-checked={isPrivate}
          className={`sm-switch ${isPrivate ? 'on' : ''}`}
          onClick={() => onChange(!isPrivate)}
        >
          <span className="sm-switch-thumb" />
        </button>
      </div>
    </div>
  )
}

export function RoomAccessRulesField({
  accessRules,
  customRoles,
  selectedCustomRoleId,
  selectedPlatformBit,
  onCustomRoleChange,
  onPlatformRoleChange,
  onAdd,
  onRemove,
}: {
  accessRules: CustomChannelAccessRuleInput[]
  customRoles: CustomRole[]
  selectedCustomRoleId: string
  selectedPlatformBit: string
  onCustomRoleChange: (roleId: string) => void
  onPlatformRoleChange: (roleBit: string) => void
  onAdd: () => void
  onRemove: (index: number) => void
}) {
  return (
    <div className="sm-field">
      <span className="sm-label">
        <FontAwesomeIcon icon={faUserShield} /> Access roles <span className="sm-required">required</span>
      </span>
      <div className="sm-rule-add">
        <select
          className="sm-input"
          value={selectedCustomRoleId}
          onChange={(event) => onCustomRoleChange(event.target.value)}
          aria-label="Custom access role"
        >
          <option value="">Custom role…</option>
          {customRoles.map((role) => (
            <option key={role.roleId} value={role.roleId}>{role.name}</option>
          ))}
        </select>
        <select
          className="sm-input"
          value={selectedPlatformBit}
          onChange={(event) => onPlatformRoleChange(event.target.value)}
          aria-label="Platform access role"
        >
          <option value="">Platform role…</option>
          {PLATFORM_ROLES.map((role) => (
            <option key={role.bit} value={role.bit}>{role.name}</option>
          ))}
        </select>
        <button
          type="button"
          className="sm-icon-btn sm-icon-btn--primary"
          onClick={onAdd}
          disabled={!selectedCustomRoleId && !selectedPlatformBit}
          title="Add access rule"
          aria-label="Add access rule"
        >
          <FontAwesomeIcon icon={faPlus} />
        </button>
      </div>
      <div className="sm-rule-chips">
        {accessRules.length === 0 && <span className="sm-rule-empty">No access roles yet.</span>}
        {accessRules.map((rule, index) => (
          <span key={`${rule.customRoleId ?? rule.platformRoleBit}-${index}`} className="sm-rule-chip">
            <FontAwesomeIcon icon={rule.customRoleId ? faUserShield : faKey} />
            {rule.customRoleId
              ? customRoles.find((role) => role.roleId === rule.customRoleId)?.name ?? 'Unknown role'
              : PLATFORM_ROLES.find((role) => role.bit === rule.platformRoleBit)?.name}
            <button
              type="button"
              className="sm-rule-chip-remove"
              onClick={() => onRemove(index)}
              aria-label="Remove access rule"
            >
              <FontAwesomeIcon icon={faXmark} />
            </button>
          </span>
        ))}
      </div>
    </div>
  )
}
