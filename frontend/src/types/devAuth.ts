export interface DevUserOption {
  userId: string
  username: string
}

export interface DevDeveloperOption {
  userId: string
  username: string
  users: DevUserOption[]
}

export interface DevLoginOptions {
  developers: DevDeveloperOption[]
}

export interface DevLoginRequest {
  developerUserId: string
  targetUserId?: string | null
}
