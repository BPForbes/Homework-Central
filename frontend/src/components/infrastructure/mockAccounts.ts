import { useEffect, useState } from 'react'

/**
 * Client-only "mock account" identities used by the channel builder's interactive preview.
 * These never touch the backend — no accounts, roles, or messages created here are persisted.
 */
export interface MockAccount {
  id: string
  label: string
  color: string
}

export const MIN_MOCK_ACCOUNTS = 1
export const MAX_MOCK_ACCOUNTS = 12
const DEFAULT_MOCK_ACCOUNTS = 3

const MOCK_ACCOUNT_COLORS = [
  '#e63946', '#f3722c', '#f9c74f', '#43aa8b', '#277da1', '#8338ec', '#c9184a', '#3a86ff',
]

let mockAccountSeq = 0

function createMockAccount(index: number): MockAccount {
  mockAccountSeq += 1
  return {
    id: `mock-${mockAccountSeq}`,
    label: `Mock ${index + 1}`,
    color: MOCK_ACCOUNT_COLORS[index % MOCK_ACCOUNT_COLORS.length],
  }
}

export interface MockAccountsController {
  accounts: MockAccount[]
  count: number
  setCount: (next: number) => void
  activeId: string | null
  setActiveId: (id: string) => void
}

/** Generates and tracks the mock accounts used for a channel builder's interactive preview. */
export function useMockAccounts(initialCount = DEFAULT_MOCK_ACCOUNTS): MockAccountsController {
  const [accounts, setAccounts] = useState<MockAccount[]>(() =>
    Array.from({ length: initialCount }, (_, i) => createMockAccount(i)),
  )
  const [count, setCountState] = useState(initialCount)
  const [activeId, setActiveId] = useState<string | null>(null)

  useEffect(() => {
    setAccounts((prev) => {
      if (count === prev.length) return prev
      if (count < prev.length) return prev.slice(0, count)
      const additions = Array.from({ length: count - prev.length }, (_, i) => createMockAccount(prev.length + i))
      return [...prev, ...additions]
    })
  }, [count])

  useEffect(() => {
    if (accounts.length === 0) {
      if (activeId !== null) setActiveId(null)
      return
    }
    if (!activeId || !accounts.some((a) => a.id === activeId))
      setActiveId(accounts[0].id)
  }, [accounts, activeId])

  function setCount(next: number) {
    const rounded = Math.round(next)
    const clamped = Math.max(MIN_MOCK_ACCOUNTS, Math.min(MAX_MOCK_ACCOUNTS, Number.isFinite(rounded) ? rounded : MIN_MOCK_ACCOUNTS))
    setCountState(clamped)
  }

  return { accounts, count, setCount, activeId, setActiveId }
}
