export interface ModelNode { id: string; nodeType: string; mechanicName?: string; label?: string; [key: string]: unknown }
export interface ModelDefinition { nodes: ModelNode[]; description?: string; [key: string]: unknown }
export interface ModelGraph { nodes?: ModelNode[]; mechanics?: Record<string, ModelDefinition>; [key: string]: unknown }
export function mechanicLabel(id: string): string {
  const labels: Record<string, string> = { 'base-spin': 'Base spin', 'free-spin': 'Free spins', bonus: 'Free-spin bonus', 'cascade-step': 'Cascade step', 'paid-round': 'Paid round', 'round-entry': 'Round entry' };
  return labels[id] ?? (id.charAt(0).toUpperCase() + id.slice(1)).replaceAll('-', ' ');
}
export function referencedMechanics(nodes: ModelNode[] = []): string[] {
  return [...new Set(nodes.flatMap(node => node.mechanicName ? [node.mechanicName] : []))];
}
export function modelCapabilities(graph: ModelGraph) {
  const scopes = [graph.nodes ?? [], ...Object.values(graph.mechanics ?? {}).map(definition => definition.nodes)];
  return {
    weightedDraws: scopes.reduce((count, nodes) => count + nodes.filter(node => node.nodeType === 'draw' && Array.isArray(node.drawWeights) && !node.weightExpressionId).length, 0),
    reelStrips: Array.isArray(graph.reelStrips) && graph.reelStrips.length > 0,
    paytables: Array.isArray(graph.paytables) && graph.paytables.length > 0,
  };
}
