import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { ServerMaintenanceNav } from '../components/layout/ServerMaintenanceNav'
import { byPrefixAndName } from '../icons/byPrefixAndName'

export function UserConfig() {
  return (
    <div className="server-page">
      <ServerMaintenanceNav title="User Config" />

      <header className="server-page-header">
        <div className="server-page-header-icon">
          <FontAwesomeIcon icon={byPrefixAndName.fas['users-gear']} />
        </div>
        <div>
          <h2>User Config</h2>
          <p className="server-page-subtitle">
            Create and manage custom roles for your server, similar to Discord role settings.
          </p>
        </div>
      </header>

      <section className="server-page-card">
        <h3>Custom roles</h3>
        <p>
          Users with <strong>Manage Server Infrastructure</strong> can define custom roles with
          tailored permission sets. Role creation and assignment APIs are coming next — this page
          is the home for that workflow.
        </p>
        <form className="server-stub-form" onSubmit={(e) => e.preventDefault()}>
          <label htmlFor="role-name">Role name</label>
          <input id="role-name" type="text" placeholder="e.g. Study Group Lead" disabled />
          <label htmlFor="role-description">Description</label>
          <textarea
            id="role-description"
            rows={3}
            placeholder="What members with this role can do…"
            disabled
          />
          <button type="submit" className="btn-primary" disabled>
            Create role (coming soon)
          </button>
        </form>
      </section>
    </div>
  )
}
