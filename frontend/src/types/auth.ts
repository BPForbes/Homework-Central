export interface LoginRequest {
  email: string
  password: string
}

export interface RegisterRequest {
  email: string
  username: string
  password: string
}

export interface AuthResponse {
  accessToken: string
  expiresIn: number
  user: UserInfo
}

export interface UserInfo {
  userId: string
  email: string
  username: string
  roles: string[]
  permissionMask: string
}
