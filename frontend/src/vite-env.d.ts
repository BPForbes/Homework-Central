/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_HC_DEV_BYPASS: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
