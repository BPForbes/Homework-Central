export interface DevUserOption {
  userId: string
  username: string
  email: string
}

export interface DevLoginOptions {
  developers: DevUserOption[]
  users: DevUserOption[]
}

export interface DevLoginRequest {
  developerUserId: string
  targetUserId?: string | null
}
