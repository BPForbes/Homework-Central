import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faUserGroup } from '@fortawesome/free-solid-svg-icons'
import { MAX_MOCK_ACCOUNTS, MIN_MOCK_ACCOUNTS, type MockAccountsController } from './mockAccounts'

interface MockAccountBarProps {
  controller: MockAccountsController
}

/**
 * Lets the real (manage-server-infrastructure) user size the mock-account pool and pick which
 * mock account is "active" — the active account acts as the current claimer/sender everywhere
 * else in the preview. Entirely client-side; nothing here is persisted.
 */
export function MockAccountBar({ controller }: MockAccountBarProps) {
  const { accounts, count, setCount, activeId, setActiveId } = controller

  return (
    <div className="mock-account-bar">
      <label className="mock-account-count">
        <FontAwesomeIcon icon={faUserGroup} />
        <span>Mock accounts</span>
        <input
          type="number"
          className="sm-input mock-account-count-input"
          min={MIN_MOCK_ACCOUNTS}
          max={MAX_MOCK_ACCOUNTS}
          value={count}
          onChange={(e) => setCount(Number(e.target.value))}
        />
      </label>

      <div className="mock-account-switcher" role="radiogroup" aria-label="Acting as">
        <span className="mock-account-switcher-label">Acting as</span>
        {accounts.map((account) => (
          <button
            key={account.id}
            type="button"
            role="radio"
            aria-checked={account.id === activeId}
            className={`mock-account-chip ${account.id === activeId ? 'active' : ''}`}
            style={{ borderColor: account.color, color: account.id === activeId ? account.color : undefined }}
            onClick={() => setActiveId(account.id)}
          >
            <span className="mock-account-chip-dot" style={{ backgroundColor: account.color }} />
            {account.label}
          </button>
        ))}
      </div>
    </div>
  )
}
