import { useCallback } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { CustomRoomType } from '../types/infrastructure'

export type ServerNavSection = 'chat' | 'roleclaim' | 'info' | 'ticket' | 'rooms'
export type UserConfigNavSection = 'create' | 'manage' | 'permissions' | 'users'

const SERVER_SECTIONS = new Set<ServerNavSection>(['chat', 'roleclaim', 'info', 'ticket', 'rooms'])
const USER_CONFIG_SECTIONS = new Set<UserConfigNavSection>(['create', 'manage', 'permissions', 'users'])

export function serverSectionToRoomType(section: ServerNavSection): CustomRoomType | 'rooms' {
  switch (section) {
    case 'roleclaim':
      return 'RoleClaim'
    case 'info':
      return 'Info'
    case 'ticket':
      return 'Ticket'
    case 'rooms':
      return 'rooms'
    default:
      return 'Chat'
  }
}

export function roomTypeToServerSection(type: CustomRoomType | 'rooms'): ServerNavSection {
  switch (type) {
    case 'RoleClaim':
      return 'roleclaim'
    case 'Info':
      return 'info'
    case 'Ticket':
      return 'ticket'
    case 'rooms':
      return 'rooms'
    default:
      return 'chat'
  }
}

export function useServerNavSection(defaultSection: ServerNavSection = 'chat') {
  const [searchParams, setSearchParams] = useSearchParams()
  const raw = searchParams.get('section')
  const section: ServerNavSection =
    raw && SERVER_SECTIONS.has(raw as ServerNavSection) ? (raw as ServerNavSection) : defaultSection

  const setSection = useCallback((next: ServerNavSection) => {
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev)
      params.set('section', next)
      return params
    }, { replace: true })
  }, [setSearchParams])

  return [section, setSection] as const
}

export function useUserConfigNavSection(defaultSection: UserConfigNavSection = 'create') {
  const [searchParams, setSearchParams] = useSearchParams()
  const raw = searchParams.get('section')
  const section: UserConfigNavSection =
    raw && USER_CONFIG_SECTIONS.has(raw as UserConfigNavSection)
      ? (raw as UserConfigNavSection)
      : defaultSection

  const setSection = useCallback((next: UserConfigNavSection) => {
    setSearchParams((prev) => {
      const params = new URLSearchParams(prev)
      params.set('section', next)
      return params
    }, { replace: true })
  }, [setSearchParams])

  return [section, setSection] as const
}
