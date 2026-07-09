import { useMemo } from 'react'
import { renderRichContent, type MentionStyleLookup } from './render'

interface RichContentProps {
  content: string
  mentionStyles?: MentionStyleLookup
  className?: string
}

/**
 * Renders raw Markdown+LaTeX source through the shared pipeline (markdown-it -> KaTeX ->
 * DOMPurify). Used for chat messages (real and mock preview), info entries, and their previews —
 * the same component everywhere so formatting behaves identically across the app.
 */
export function RichContent({ content, mentionStyles, className }: RichContentProps) {
  const html = useMemo(() => renderRichContent(content, mentionStyles), [content, mentionStyles])
  return (
    <div className={`rich-content${className ? ` ${className}` : ''}`} dangerouslySetInnerHTML={{ __html: html }} />
  )
}
