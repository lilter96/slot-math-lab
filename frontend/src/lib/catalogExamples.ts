export const catalogExamples = Object.fromEntries(Object.entries(import.meta.glob('../../../backend/SlotMath.Core.Tests/TestData/AuditCatalog/*.json', {
    eager: true,
    import: 'default'
})).map(([path, config]) => [path.split('/').pop()!.replace('.json', ''), config as Record<string, unknown>]));
