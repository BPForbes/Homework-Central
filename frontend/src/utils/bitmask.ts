export function hasMaskBit(maskBase64: string | undefined, bit: number): boolean {
  if (!maskBase64) return false
  const bytes = Uint8Array.from(atob(maskBase64), (c) => c.charCodeAt(0))
  const byteIndex = Math.floor(bit / 8)
  const bitIndex = bit % 8
  return (bytes[byteIndex] & (1 << bitIndex)) !== 0
}
