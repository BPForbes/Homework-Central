/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Set to "true" by dev startup scripts to expose the /devlogin route. */
  readonly VITE_HC_DEV_BYPASS: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
