/**
 * Application routes. Registers /devlogin only when VITE_HC_DEV_BYPASS is set by dev scripts.
 */
import { lazy, Suspense, type ComponentType } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './context/AuthContext'
import { ThemeProvider } from './context/ThemeContext'
import { WaterBackground } from './components/background/WaterBackground'
import { BackendConnectingLoader } from './components/BackendConnectingLoader'
import { BackendGate } from './components/BackendGate'
import { ProtectedRoute } from './components/ProtectedRoute'
import { AppLayout } from './components/layout/AppLayout'
import { PermissionRoute } from './components/PermissionRoute'
import { MANAGE_SERVER_INFRASTRUCTURE_BIT } from './constants/permissions'

const DEV_BYPASS_ENABLED = import.meta.env.VITE_HC_DEV_BYPASS === 'true'

function lazyPage(load: () => Promise<Record<string, unknown>>, exportName: string) {
  return lazy(async () => {
    const module = await load()
    return { default: module[exportName] as ComponentType }
  })
}

const Login = lazyPage(() => import('./pages/Login'), 'Login')
const DevLogin = lazyPage(() => import('./pages/DevLogin'), 'DevLogin')
const Register = lazyPage(() => import('./pages/Register'), 'Register')
const Dashboard = lazyPage(() => import('./pages/Dashboard'), 'Dashboard')
const ChatRoom = lazyPage(() => import('./pages/ChatRoom'), 'ChatRoom')
const ChatIndex = lazyPage(() => import('./pages/ChatIndex'), 'ChatIndex')
const GetRoles = lazyPage(() => import('./pages/GetRoles'), 'GetRoles')
const Inbox = lazyPage(() => import('./pages/Inbox'), 'Inbox')
const UserConfig = lazyPage(() => import('./pages/UserConfig'), 'UserConfig')
const ServerMaintenance = lazyPage(() => import('./pages/ServerMaintenance'), 'ServerMaintenance')
const ChannelBuilder = lazyPage(() => import('./pages/ChannelBuilder'), 'ChannelBuilder')
const NeuralNet = lazyPage(() => import('./pages/NeuralNet'), 'NeuralNet')

export default function App() {
  return (
    <ThemeProvider>
      <WaterBackground />
      <BackendGate>
        <BrowserRouter>
          <AuthProvider>
            <Suspense fallback={<BackendConnectingLoader message="Loading page…" />}>
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
                <Route path="/inbox" element={<Inbox />} />
                <Route path="/chat" element={<ChatIndex />} />
                <Route path="/chat/:roomId" element={<ChatRoom />} />
                <Route path="/get-roles" element={<GetRoles />} />
                <Route
                  path="/user-config"
                  element={
                    <PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}>
                      <UserConfig />
                    </PermissionRoute>
                  }
                />
                <Route
                  path="/server"
                  element={
                    <PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}>
                      <ServerMaintenance />
                    </PermissionRoute>
                  }
                />
                <Route
                  path="/server/channels/:channelId"
                  element={
                    <PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}>
                      <ChannelBuilder />
                    </PermissionRoute>
                  }
                />
                <Route
                  path="/server/NeuralNet/Training"`n                  element={<PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}><NeuralNet /></PermissionRoute>} `n                />`n                <Route`n                  path="/server/NeuralNet/TrainingFeedback"
                  element={<PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}><NeuralNet /></PermissionRoute>}
                />
                <Route
                  path="/server/NeuralNet/DataManagement"
                  element={<PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}><NeuralNet /></PermissionRoute>}
                />
                <Route
                  path="/server/NeuralNet/Visualizer"
                  element={<PermissionRoute permissionBit={MANAGE_SERVER_INFRASTRUCTURE_BIT}><NeuralNet /></PermissionRoute>}
                />
              </Route>
              <Route path="*" element={<Navigate to="/login" replace />} />
              </Routes>
            </Suspense>
          </AuthProvider>
        </BrowserRouter>
      </BackendGate>
    </ThemeProvider>
  )
}
