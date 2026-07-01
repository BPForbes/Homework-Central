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
  accountClass: string
  permissionMask: string
  roleMask: string
  featureMask: string
  generalSubjectMask: string
  subjectExpertiseMasks: Record<string, string>
  statusMask: string
}

export type CoreMaskField =
  | 'permissionMask'
  | 'roleMask'
  | 'featureMask'
  | 'generalSubjectMask'
  | 'statusMask'
