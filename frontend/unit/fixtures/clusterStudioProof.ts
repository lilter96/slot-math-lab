import {Flow, gameGraph, Mechanics} from '../../src/games/shared/graph/flow';
import {type ClusterBoard, clusterPays, clusterState} from '../../src/games/shared/graph/mechanics/clusters';

/** Independent board: four adjacent A cells, a B separator, one isolated A. Weighted mode counts the second cell zero. */
export function clusterStudioProof(weighted = false): Record<string, unknown> {
    const spec: ClusterBoard = {
        columns: 6,
        rows: 1,
        symbolCount: 3,
        wild: 0,
        pays: {1: {5: 10, 6: 20}}, ...(weighted ? {weights: 'authoredCellCounts'} : {})
    };
    const mechanics = new Mechanics(),
        root = new Flow().use('clusters', 'Pay the authored board', mechanics.add(clusterPays(mechanics, spec))).sink('sink', 'Payout', 'clusterCoins', 100);
    return gameGraph({
        id: 'independent-cluster-proof',
        name: weighted ? 'Weighted cluster proof' : 'Cluster size proof',
        description: 'Expected ordinary payouts: 0, then 6 for size four, then 9 including the isolated A. Weighted: size three pays the four-cell component.',
        root,
        mechanics,
        initialState: {
            ...clusterState(spec),
            board: ['1', '1', '1', '1', '2', '1'],
            roundCost: 1,
            authoredCellCounts: [1, 0, 1, 1, 1, 1]
        },
        tables: {
            boardConfig: {columns: 6, rows: 1},
            symbols: [{id: '0', name: 'Wild', kind: 'Wild'}, {id: '1', name: 'A', kind: 'Standard'}, {
                id: '2',
                name: 'B',
                kind: 'Standard'
            }],
            paytables: [{id: 'private-clusters', entries: [{symbolId: '1', counts: [5, 6], payouts: ['10', '20']}]}],
            opaque: {retained: true}
        }
    });
}
