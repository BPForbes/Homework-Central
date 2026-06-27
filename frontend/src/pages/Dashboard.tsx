import { useAuth } from '../context/AuthContext'
import { useNavigate } from 'react-router-dom'

export function Dashboard() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()

  async function handleLogout() {
    await logout()
    navigate('/login')
  }

  return (
    <div className="dashboard">
      <header className="dashboard-header">
        <h1>Homework Central</h1>
        <div className="user-info">
          <span>
            {user?.username} ({user?.email})
          </span>
          <button onClick={handleLogout} className="btn-secondary">
            Sign out
          </button>
        </div>
      </header>
      <main className="dashboard-main">
        <h2>Welcome, {user?.username}!</h2>
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
      </main>
    </div>
  )
}
