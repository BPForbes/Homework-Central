/**
 * These markdown-it plugins ship no type declarations. Minimal ambient shapes so the shared
 * renderer in src/richtext can import them without `any`.
 */
declare module 'markdown-it-texmath' {
  import type MarkdownIt from 'markdown-it'

  interface TexmathOptions {
    engine: unknown
    delimiters?: string | string[]
    katexOptions?: Record<string, unknown>
  }

  const plugin: (md: MarkdownIt, options?: TexmathOptions) => void
  export default plugin
}

declare module 'markdown-it-sub' {
  import type MarkdownIt from 'markdown-it'
  const plugin: (md: MarkdownIt) => void
  export default plugin
}

declare module 'markdown-it-sup' {
  import type MarkdownIt from 'markdown-it'
  const plugin: (md: MarkdownIt) => void
  export default plugin
}

declare module 'markdown-it-mark' {
  import type MarkdownIt from 'markdown-it'
  const plugin: (md: MarkdownIt) => void
  export default plugin
}

declare module 'markdown-it-task-lists' {
  import type MarkdownIt from 'markdown-it'

  interface TaskListsOptions {
    enabled?: boolean
    label?: boolean
    labelAfter?: boolean
  }

  const plugin: (md: MarkdownIt, options?: TaskListsOptions) => void
  export default plugin
}
