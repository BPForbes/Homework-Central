export function shouldShowInfrastructureSidebar(pathname: string): boolean {
  return pathname.startsWith('/user-config') || pathname.startsWith('/server')
}

export function shouldShowChatSidebar(pathname: string): boolean {
  return pathname.startsWith('/chat')
    || pathname.startsWith('/inbox')
    || pathname === '/dashboard'
    || pathname.startsWith('/get-roles')
}
