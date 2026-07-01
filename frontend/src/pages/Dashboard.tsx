import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faComments } from '@fortawesome/free-solid-svg-icons'
import { useAuth } from '../context/AuthContext'

export function Dashboard() {
  const { user } = useAuth()

  return (
    <div className="dashboard-content">
      <h2>Welcome, {user?.username}!</h2>
      <p className="dashboard-hint">
        Open the <strong>Chats</strong> menu on the left to browse subject and staff rooms you can access.
      </p>

      <section className="dashboard-card">
        <div className="dashboard-card-icon">
          <FontAwesomeIcon icon={faComments} />
        </div>
        <div>
          <h3>Chat rooms</h3>
          <p>Use the sliding panel to pick a room — for example Calculus under Mathematics or Biology under Science.</p>
        </div>
      </section>

      <section className="roles-section">
        <h3>Your roles</h3>
        {user?.roles?.length ? (
          <ul>
            {user.roles.map((r) => (
              <li key={r}>{r}</li>
            ))}
          </ul>
        ) : (
          <p>No roles assigned.</p>
        )}
      </section>

      <p className="dashboard-footer-link">
        <Link to="/chat">Browse chats</Link>
      </p>
    </div>
  )
}
