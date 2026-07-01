/**
 * Application routes. Registers /devlogin only when VITE_HC_DEV_BYPASS is set by dev scripts.
 */
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { BackendGate } from './components/BackendGate'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppLayout } from './components/layout/AppLayout'
import { Login } from './pages/Login'
import { DevLogin } from './pages/DevLogin'
import { Register } from './pages/Register'
import { Dashboard } from './pages/Dashboard'
import { ChatRoom } from './pages/ChatRoom'
import { ChatIndex } from './pages/ChatIndex'
import { GetRoles } from './pages/GetRoles'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

export default function App() {
  return (
    <BackendGate>
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
            element={
              <ProtectedRoute>
                <AppLayout />
              </ProtectedRoute>
            }
          >
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/chat" element={<ChatIndex />} />
            <Route path="/chat/:roomId" element={<ChatRoom />} />
            <Route path="/get-roles" element={<GetRoles />} />
          </Route>
          <Route path="*" element={<Navigate to="/login" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </BackendGate>
  )
}
