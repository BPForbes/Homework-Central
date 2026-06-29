import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { ProtectedRoute } from './components/ProtectedRoute'
import { Login } from './pages/Login'
import { DevLogin } from './pages/DevLogin'
import { Register } from './pages/Register'
import { Dashboard } from './pages/Dashboard'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          {/* Root redirects authenticated users to dashboard, others to login */}
          <Route
            path="/"
            element={
              <ProtectedRoute>
                <Navigate to="/dashboard" replace />
              </ProtectedRoute>
            }
          />
          <Route path="/login" element={<Login />} />
          {DEV_BYPASS_ENABLED && <Route path="/devlogin" element={<DevLogin />} />}
          <Route path="/register" element={<Register />} />
          <Route
            path="/dashboard"
            element={
              <ProtectedRoute>
                <Dashboard />
              </ProtectedRoute>
            }
          />
          <Route path="*" element={<Navigate to="/login" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  )
}
