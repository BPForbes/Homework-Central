/** Matches ChatRoomBlueprint.GetRolesRoomId on the backend. Not a chat room — the sidebar routes
 * this id to the /get-roles page instead of the chat message UI. */
export const GET_ROLES_ROOM_ID = 'general:get-roles'

export interface ChatNavRoom {
  id: string
  name: string
  isPrivate: boolean
  categoryKey: string
  categoryKind: string
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

export interface ChatMessage {
  messageId: string
  roomId: string
  senderId: string
  senderUsername: string
  content: string
  createdAtUtc: string
}

export interface ChatTypingUser {
  userId: string
  username: string
}
