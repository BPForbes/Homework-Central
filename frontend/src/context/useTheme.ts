import { useContext } from 'react'
import { ThemeContext } from './ThemeContext'

interface ThemeContextValue {
  theme: 'light' | 'dark'
  toggleTheme: () => void
}

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) {
    throw new Error('useTheme must be used within a ThemeProvider')
  }
  return ctx
}
