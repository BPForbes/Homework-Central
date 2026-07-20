import type { NeuralNetReplay } from '../types/neuralNetReplay'

const maximumBytes = 20 * 1024 * 1024
const maximumFrames = 100_000
const maximumNodes = 512
const maximumEdges = 4096

export function parseReplayImport(text: string): NeuralNetReplay {
  if (text.length > maximumBytes) throw new Error('The replay file is too large.')
  const parsed = JSON.parse(text) as NeuralNetReplay
  if (parsed.schemaVersion !== '2.0' || !parsed.topology || !Array.isArray(parsed.frames)) throw new Error('Unsupported replay schema.')
  if (parsed.topology.nodes.length > maximumNodes || parsed.topology.edges.length > maximumEdges || parsed.frames.length > maximumFrames) throw new Error('Replay exceeds supported limits.')
  const nodeIds = new Set(parsed.topology.nodes.map(node => node.index))
  if (nodeIds.size !== parsed.topology.nodes.length || parsed.topology.edges.some(edge => !nodeIds.has(edge.sourceNodeIndex) || !nodeIds.has(edge.targetNodeIndex))) throw new Error('Replay topology references are invalid.')
  return parsed
}
