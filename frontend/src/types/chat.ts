/** Matches ChatRoomBlueprint.GetRolesRoomId on the backend. Not a chat room — routed via /chat. */
export const GET_ROLES_ROOM_ID = 'general:get-roles'

export interface ChatNavRoom {
  id: string
  name: string
  isPrivate: boolean
  categoryKey: string
  categoryKind: string
  roomType?: string
  iconName?: string | null
}

export interface ChatNavCategory {
  key: string
  name: string
  categoryKind: string
  isPrivateCategory: boolean
  rooms: ChatNavRoom[]
}

export interface ChatNav {
  categories: ChatNavCategory[]
}

export interface ChatRoomDetail {
  id: string
  name: string
  categoryKey: string
  categoryDisplayName: string
  categoryKind: string
  isPrivate: boolean
  roomType: string
  infoContent?: string | null
  canEditInfo: boolean
  customChannelId?: string | null
  iconName?: string | null
}

export interface ChatMessage {
  messageId: string
  roomId: string
  senderId: string
  senderUsername: string
  content: string
  createdAtUtc: string
  replyToMessageId?: string | null
  replyToSenderId?: string | null
  replyToSenderUsername?: string | null
  replyToContentSnippet?: string | null
}

export interface ChatTypingUser {
  userId: string
  username: string
}
