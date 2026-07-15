export function shouldShowInfrastructureSidebar(pathname: string): boolean {
  return pathname.startsWith('/user-config') || pathname.startsWith('/server')
}

export function shouldShowInboxSidebar(pathname: string): boolean {
  return pathname.startsWith('/inbox')
}

export function shouldShowChatSidebar(pathname: string): boolean {
  return pathname.startsWith('/chat')
    || pathname === '/dashboard'
    || pathname.startsWith('/get-roles')
}
