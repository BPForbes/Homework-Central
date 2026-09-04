import { fileTypeFromBuffer } from 'file-type'

const SCRIPT_SHEBANG = /^#!\s*\/(?:usr\/bin\/)?(?:env\s+)?(python[23]?|node|bash|sh|zsh|ruby|perl)\b/i

function isPreviewableMime(mime: string): boolean {
  return (
    mime.startsWith('image/') ||
    mime.startsWith('video/') ||
    mime.startsWith('audio/') ||
    mime === 'application/pdf'
  )
}

/** Rough pre-upload hint only — server isHazard wins after upload. */
export async function estimateUploadHazard(file: File): Promise<boolean> {
  const head = new Uint8Array(await file.slice(0, 4100).arrayBuffer())
  const detected = await fileTypeFromBuffer(head)
  if (detected)
    return !isPreviewableMime(detected.mime)

  return SCRIPT_SHEBANG.test(new TextDecoder().decode(head))
}
