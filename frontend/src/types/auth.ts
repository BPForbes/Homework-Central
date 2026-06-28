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
  roleMask: string
  featureMask: string
  generalSubjectMask: string
  computerScienceMask: string
  mathematicsMask: string
  scienceMask: string
  statusMask: string
}

export type MaskField =
  | 'permissionMask'
  | 'roleMask'
  | 'featureMask'
  | 'generalSubjectMask'
  | 'computerScienceMask'
  | 'mathematicsMask'
  | 'languageMask'
  | 'scienceMask'
  | 'statusMask'
