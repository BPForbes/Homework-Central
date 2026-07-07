import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'

export function ServerMaintenance() {
  return (
    <div className="server-page">
      <ServerMaintenanceNav title="Server Maintenance" />

      <header className="server-page-header">
        <div className="server-page-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas.server} />
        </div>
        <div>
          <h2>Server Maintenance</h2>
          <p className="server-page-subtitle">
            Configure custom chat rooms and server-wide settings, similar to Discord channel
            management.
          </p>
        </div>
      </header>

      <section className="server-page-card">
        <h3>Custom chat rooms</h3>
        <p>
          Users with <strong>Manage Server Infrastructure</strong> can create custom chat rooms
          outside the default subject and staff categories. Room creation APIs are coming next —
          this page is the home for that workflow.
        </p>
        <form className="server-stub-form" onSubmit={(e) => e.preventDefault()}>
          <label htmlFor="room-name">Room name</label>
          <input id="room-name" type="text" placeholder="e.g. project-alpha" disabled />
          <label htmlFor="room-category">Category</label>
          <input id="room-category" type="text" placeholder="e.g. Community Projects" disabled />
          <label htmlFor="room-private">
            <input id="room-private" type="checkbox" disabled />
            Private room (role-gated)
          </label>
          <button type="submit" className="btn-primary" disabled>
            Create room (coming soon)
          </button>
        </form>
      </section>
    </div>
  )
}
