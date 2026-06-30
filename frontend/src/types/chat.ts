export interface ChatNavRoom {
  id: string
  name: string
}

export interface ChatNavCategory {
  key: string
  name: string
  rooms: ChatNavRoom[]
}

export interface ChatNav {
  categories: ChatNavCategory[]
}
