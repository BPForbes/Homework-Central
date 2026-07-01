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
  senderUsername: string | null
  content: string
  createdAtUtc: string
  isOwn: boolean
}

export interface ChatTypingUser {
  userId: string
  username: string
}
