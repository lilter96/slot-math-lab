import {expect, test} from '@playwright/test';
import {clusterState, type ClusterBoard} from '../src/games/shared/graph/mechanics/clusters';

const board = (columns: number, rows: number): ClusterBoard => ({
    columns, rows, wild: 0, symbolCount: 2, pays: {1: {'3': 10}}
});

test('Column-major orthogonal adjacency keeps boundaries and rectangular geometry', () => {
    expect(clusterState(board(3, 2)).clusterAdjacency).toEqual([6, 9, 25, 38, 36, 24]);
    expect(clusterState(board(1, 3)).clusterAdjacency).toEqual([2, 5, 2]);
    expect(clusterState(board(3, 1)).clusterAdjacency).toEqual([2, 5, 2]);
    expect(clusterState(board(1, 1)).clusterAdjacency).toEqual([0]);
});

test('Numeric JSON adjacency never rounds masks above JavaScript integer precision', () => {
    const adjacency = clusterState(board(1, 53)).clusterAdjacency!;
    expect(adjacency).toHaveLength(53);
    expect(adjacency.every(Number.isSafeInteger)).toBe(true);
    expect(adjacency[51]).toBe(2 ** 50 + 2 ** 52);
    expect(clusterState(board(1, 54))).not.toHaveProperty('clusterAdjacency');
    expect(clusterState(board(8, 8))).not.toHaveProperty('clusterAdjacency');
});
