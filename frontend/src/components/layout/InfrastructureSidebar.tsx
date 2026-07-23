import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faBrain,
  faCircleInfo,
  faComments,
  faIdBadge,
  faLayerGroup,
  faMagnifyingGlass,
  faPlus,
  faServer,
  faShieldHalved,
  faTicket,
  faUsersGear,
} from '@fortawesome/free-solid-svg-icons'
import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import {
  useServerNavSection,
  useUserConfigNavSection,
  type ServerNavSection,
  type UserConfigNavSection,
} from '../../hooks/useInfrastructureNav'
import { SidebarSkeleton } from './SidebarSkeleton'

function SidebarSectionLink({
  active,
  label,
  icon,
  onClick,
}: {
  active: boolean
  label: string
  icon: typeof faComments
  onClick: () => void
}) {
  return (
    <li>
      <button
        type="button"
        className={`chat-room-link infra-sidebar-link ${active ? 'active' : ''}`}
        onClick={onClick}
      >
        <FontAwesomeIcon icon={icon} className="chat-room-icon" />
        <span className="chat-room-name">{label}</span>
      </button>
    </li>
  )
}

interface SidebarItem<T extends string> {
  id: T
  label: string
  icon: typeof faComments
}

interface ConfigurationSidebarProps<T extends string> {
  ariaLabel: string
  title: string
  titleIcon: typeof faComments
  items: SidebarItem<T>[]
  section: T
  setSection: (section: T) => void
  loading: boolean
}

function ConfigurationSidebar<T extends string>({
  ariaLabel,
  title,
  titleIcon,
  items,
  section,
  setSection,
  loading,
}: ConfigurationSidebarProps<T>) {
  return (
    <aside className="chat-sidebar" aria-label={ariaLabel}>
      <div className="chat-sidebar-header">
        <h2>
          <FontAwesomeIcon icon={titleIcon} className="chat-sidebar-title-icon" />
          {title}
        </h2>
      </div>
      <div className="chat-sidebar-body">
        {loading ? (
          <SidebarSkeleton label={`Loading ${title} navigation`} />
        ) : (
          <section className="chat-category chat-category--public">
            <div className="chat-category-label-row">
              <span className="chat-category-label">
                <FontAwesomeIcon icon={titleIcon} className="chat-category-icon" />
                Configuration
              </span>
            </div>
            <ul className="chat-room-list">
              {items.map((item) => (
                <SidebarSectionLink
                  key={item.id}
                  active={section === item.id}
                  label={item.label}
                  icon={item.icon}
                  onClick={() => setSection(item.id)}
                />
              ))}
            </ul>
          </section>
        )}
      </div>
    </aside>
  )
}

function ServerSidebar({ loading }: { loading: boolean }) {
  const [serverSection] = useServerNavSection()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  type ServerConfigurationSection = ServerNavSection | 'neuralTraining' | 'neuralFeedback' | 'neuralData' | 'neuralVisualizer'
  const items: SidebarItem<ServerConfigurationSection>[] = [
    { id: 'chat', label: 'Create Chat Room', icon: faComments },
    { id: 'roleclaim', label: 'Create Role Claim', icon: faIdBadge },
    { id: 'info', label: 'Create Info Page', icon: faCircleInfo },
    { id: 'ticket', label: 'Create Ticket Room', icon: faTicket },
    { id: 'rooms', label: 'All Rooms', icon: faLayerGroup },
    { id: 'neuralTraining', label: 'Neural Net · Training', icon: faBrain },
    { id: 'neuralFeedback', label: 'Neural Net · Feedback', icon: faBrain },
    { id: 'neuralData', label: 'Neural Net · Data', icon: faBrain },
    { id: 'neuralVisualizer', label: 'Neural Net · Visualizer', icon: faBrain },
  ]
  const routes: Record<Exclude<ServerConfigurationSection, ServerNavSection>, string> = {
    neuralTraining: '/server/NeuralNet/Training', neuralFeedback: '/server/NeuralNet/TrainingFeedback',
    neuralData: '/server/NeuralNet/DataManagement', neuralVisualizer: '/server/NeuralNet/Visualizer',
  }
  const active: ServerConfigurationSection = pathname.includes('/server/NeuralNet/')
    ? (Object.entries(routes).find(([, path]) => path === pathname)?.[0] as ServerConfigurationSection ?? 'neuralTraining')
    : serverSection
  return <ConfigurationSidebar ariaLabel="Server configuration" title="Server" titleIcon={faServer} items={items} section={active} setSection={(next) => {
    if (next in routes) navigate(routes[next as keyof typeof routes])
    else navigate(`/server?section=${next}`)
  }} loading={loading} />
}
function UserConfigSidebar({ loading }: { loading: boolean }) {
  const [section, setSection] = useUserConfigNavSection()

  const items: { id: UserConfigNavSection; label: string; icon: typeof faComments }[] = [
    { id: 'create', label: 'Create Roles', icon: faPlus },
    { id: 'manage', label: 'Manage Roles', icon: faUsersGear },
    { id: 'permissions', label: 'Roles & Permissions', icon: faShieldHalved },
    { id: 'users', label: 'User Search', icon: faMagnifyingGlass },
  ]

  return (
    <ConfigurationSidebar
      ariaLabel="User configuration"
      title="User Config"
      titleIcon={faUsersGear}
      items={items}
      section={section}
      setSection={setSection}
      loading={loading}
    />
  )
}

export function InfrastructureSidebar() {
  const { pathname } = useLocation()
  const [loadedPath, setLoadedPath] = useState<string | null>(null)
  const loading = loadedPath !== pathname

  useEffect(() => {
    const frameId = window.requestAnimationFrame(() => setLoadedPath(pathname))
    return () => window.cancelAnimationFrame(frameId)
  }, [pathname])

  if (pathname.startsWith('/user-config'))
    return <UserConfigSidebar loading={loading} />
  if (pathname.startsWith('/server'))
    return <ServerSidebar loading={loading} />

  return null
}
