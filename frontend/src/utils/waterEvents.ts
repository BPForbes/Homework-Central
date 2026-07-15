/**
 * Decoupled signal from the API layer to the water background scene: every
 * mutating request ("sending data to the server") raises a droplet ripple on
 * the water. The scene canvas sits behind all page content, and API-triggered
 * droplets additionally spawn in the outer band of the viewport, so they never
 * sit on top of the form inputs the user is typing into.
 */
export const WATER_DROPLET_EVENT = 'hc:water-droplet'

export function emitWaterDroplet(): void {
  window.dispatchEvent(new CustomEvent(WATER_DROPLET_EVENT))
}
