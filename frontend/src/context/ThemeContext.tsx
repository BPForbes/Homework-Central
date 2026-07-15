import { createContext, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'

type Theme = 'light' | 'dark'

interface ThemeContextValue {
  theme: Theme
  toggleTheme: () => void
}

const STORAGE_KEY = 'hc-theme'

export const ThemeContext = createContext<ThemeContextValue | undefined>(undefined)

function readInitialTheme(): Theme {
  const attr = document.documentElement.getAttribute('data-theme')
  return attr === 'dark' ? 'dark' : 'light'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(readInitialTheme)
  const transitionTimerRef = useRef<number | null>(null)

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
    localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  useEffect(() => () => {
    if (transitionTimerRef.current !== null)
      window.clearTimeout(transitionTimerRef.current)
  }, [])

  const toggleTheme = useCallback(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      setTheme((current) => (current === 'dark' ? 'light' : 'dark'))
      return
    }

    document.documentElement.classList.add('theme-transitioning')
    if (transitionTimerRef.current !== null)
      window.clearTimeout(transitionTimerRef.current)
    setTheme((current) => (current === 'dark' ? 'light' : 'dark'))
    transitionTimerRef.current = window.setTimeout(() => {
      document.documentElement.classList.remove('theme-transitioning')
      transitionTimerRef.current = null
    }, 750)
  }, [])

  const value = useMemo(() => ({ theme, toggleTheme }), [theme, toggleTheme])

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
}
