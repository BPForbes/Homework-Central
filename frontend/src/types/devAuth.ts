/** Types for the localhost-only /devlogin developer bypass flow. */

export interface DevUserOption {
  userId: string
  username: string
  tenantDatabaseName: string
}

/** Subject-area developer account with personas scoped to that account. */
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
  tenantDatabaseName?: string | null
}
