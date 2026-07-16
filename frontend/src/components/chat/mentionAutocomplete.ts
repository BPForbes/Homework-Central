import type { ChatMessage, MentionRoleOption } from '../../types/chat'
import type { MentionStyleLookup } from '../../richtext/markdown'

export type MentionCandidate =
  | { kind: 'user'; name: string; color: string }
  | { kind: 'role'; name: string; color: string; isCustom: boolean }

const MENTION_QUERY_PATTERN = /@([\p{L}\p{N}_][\p{L}\p{N}_.-]*)$/u

export function getActiveMentionQuery(draft: string, cursorPos: number): { query: string; start: number } | null {
  const before = draft.slice(0, cursorPos)
  const match = before.match(MENTION_QUERY_PATTERN)
  if (!match)
    return null

  return {
    query: match[1],
    start: before.length - match[0].length,
  }
}

export function buildMentionCandidates(
  messages: ChatMessage[],
  mentionRoles: MentionRoleOption[],
  filter: string,
): MentionCandidate[] {
  const lowerFilter = filter.toLowerCase()
  const recentUsers = new Map<string, { name: string; color: string; lastSeen: number }>()

  for (let index = messages.length - 1; index >= 0; index -= 1) {
    const message = messages[index]
    const key = message.senderUsername.toLowerCase()
    if (!recentUsers.has(key)) {
      recentUsers.set(key, {
        name: message.senderUsername,
        color: message.senderMessageColor ?? 'var(--color-ink-secondary)',
        lastSeen: new Date(message.createdAtUtc).getTime(),
      })
    }
  }

  const users = [...recentUsers.values()]
    .filter((user) => user.name.toLowerCase().startsWith(lowerFilter))
    .sort((left, right) => right.lastSeen - left.lastSeen)
    .map((user): MentionCandidate => ({
      kind: 'user',
      name: user.name,
      color: user.color,
    }))

  const roleNames = new Set(users.map((user) => user.name.toLowerCase()))
  const roles = mentionRoles
    .filter((role) => role.name.toLowerCase().startsWith(lowerFilter))
    .filter((role) => !roleNames.has(role.name.toLowerCase()) || lowerFilter.length > 0)
    .map((role): MentionCandidate => ({
      kind: 'role',
      name: role.name,
      color: role.messageColor,
      isCustom: role.isCustom,
    }))

  return [...users, ...roles]
}

/** Colors for @mention highlighting inside rendered Markdown — shared by the message list and composer preview. */
export function buildMentionStyleLookup(messages: ChatMessage[], mentionRoles: MentionRoleOption[]): MentionStyleLookup {
  const userColors: Record<string, string> = {}
  const roleColors: Record<string, string> = {}

  for (const message of messages) {
    const key = message.senderUsername.toLowerCase()
    if (!userColors[key] && message.senderMessageColor)
      userColors[key] = message.senderMessageColor
  }

  for (const role of mentionRoles)
    roleColors[role.name.toLowerCase()] = role.messageColor

  return { userColors, roleColors }
}

export function insertMention(
  draft: string,
  mentionStart: number,
  cursorPos: number,
  candidate: MentionCandidate,
): { nextDraft: string; nextCursor: number } {
  const before = draft.slice(0, mentionStart)
  const after = draft.slice(cursorPos)
  const mentionText = `@${candidate.name}`
  const nextDraft = `${before}${mentionText} ${after}`
  const nextCursor = before.length + mentionText.length + 1
  return { nextDraft, nextCursor }
}
