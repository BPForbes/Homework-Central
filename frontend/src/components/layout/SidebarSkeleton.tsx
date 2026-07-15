interface SidebarSkeletonProps {
  label: string
}

const SKELETON_GROUP_ROW_COUNTS = [3, 2]

/** Compact navigation placeholder used while a sidebar's role-aware entries load. */
export function SidebarSkeleton({ label }: SidebarSkeletonProps) {
  return (
    <div className="sidebar-skeleton" role="status" aria-label={label} aria-busy="true">
      {SKELETON_GROUP_ROW_COUNTS.map((rowCount) => (
        <div className="sidebar-skeleton-group" key={rowCount}>
          <span className="sidebar-skeleton-label" />
          {Array.from({ length: rowCount }, (_, rowIndex) => (
            <span
              className={`sidebar-skeleton-row ${rowIndex === rowCount - 1 ? 'sidebar-skeleton-row--short' : ''}`}
              key={rowIndex}
            />
          ))}
        </div>
      ))}
    </div>
  )
}
