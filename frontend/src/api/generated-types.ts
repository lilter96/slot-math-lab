export type paths = {
    "/api/configs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List all configs */
        get: operations["listConfigs"];
        put?: never;
        /** Create a new config */
        post: operations["createConfig"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/configs/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Get a config by ID */
        get: operations["getConfig"];
        /** Update a config */
        put: operations["updateConfig"];
        post?: never;
        /** Delete a config */
        delete: operations["deleteConfig"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/configs/{id}/versions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List all versions of a config */
        get: operations["listConfigVersions"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/configs/{id}/versions/{version}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Get a specific config version */
        get: operations["getConfigVersion"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/evaluate/light": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Fast regime-aware evaluation */
        post: operations["evaluateLight"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/plugins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List all registered plugins */
        get: operations["listPlugins"];
        put?: never;
        /** Register a new plugin */
        post: operations["registerPlugin"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/plugins/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Get a plugin by ID */
        get: operations["getPlugin"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Cursor-paginated server run archive without result JSON or histograms */
        get: operations["listRunArchive"];
        put?: never;
        /** Create a new evaluation run */
        post: operations["createRun"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Get run status and progress */
        get: operations["getRun"];
        put?: never;
        post?: never;
        /** Cancel a running job */
        delete: operations["cancelRun"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/evidence": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Retrieve original pinned config and provenance for a run */
        get: operations["getRunEvidence"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/measurements/schema": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Discover flattened observation points and state fields; optionally validate a measurement plan */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": {
                        config: {
                            [key: string]: unknown;
                        };
                        measurements?: components["schemas"]["MeasurementDefinition"][];
                    };
                };
            };
            responses: {
                /** @description Compiled measurement schema */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MeasurementSchema"];
                    };
                };
                /** @description Invalid graph or measurement plan */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Compute limit; honor Retry-After */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/validate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Validate a graph config */
        post: operations["validateConfig"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
};
export type webhooks = Record<string, never>;
export type components = {
    schemas: {
        ConfigDetail: {
            config?: {
                [key: string]: unknown;
            };
            createdAt?: string;
            id?: string;
            version?: number;
        };
        ConfigSummary: {
            id?: string;
            latestVersion?: number;
            name?: string;
            updatedAt?: string;
        };
        CreateConfigRequest: {
            config: {
                [key: string]: unknown;
            };
        };
        CreateConfigResponse: {
            id?: string;
            version?: number;
        };
        CreateRunRequest: {
            configId: string;
            /** @description Replay a pinned version; omitted selects latest at creation. */
            configVersion?: null | number;
            /** Format: int32 */
            degreeOfParallelism?: number;
            measurements?: components["schemas"]["MeasurementDefinition"][];
            /** Format: int32 */
            progressBatchSize?: null | number;
            /** Format: int32 */
            sampleSize?: null | number;
            /** Format: int64 */
            seed?: number;
        };
        EvaluateLightRequest: {
            config: {
                [key: string]: unknown;
            };
            maxBranches?: number;
            sampleSize?: number;
        };
        EvaluateLightResponse: {
            ci95?: string;
            elapsedMs?: number;
            hitFrequency?: number;
            provenance?: string;
            rtp?: number;
            sampleCount?: number;
            stdErr?: number;
            strategy?: string;
            volatility?: number;
        };
        MeasurementDefinition: {
            /** @description Boolean constructor AST; null includes every observation. */
            filter?: ({
                exprType: string;
            } & {
                [key: string]: unknown;
            }) | null;
            id: string;
            name: string;
            /** @description Null observes a settled paid round; node ID observes state before each visit to the flattened graph node. */
            nodeId?: string | null;
            unit?: string;
            /** @description Numeric constructor AST; null means settled capped payout at round completion. */
            value?: ({
                exprType: string;
            } & {
                [key: string]: unknown;
            }) | null;
        };
        MeasurementSchema: {
            fields: {
                name: string;
                type: string;
            }[];
            points: {
                label: string;
                nodeId: string;
            }[];
        };
        MeasurementSnapshot: {
            /** Format: int64 */
            count: number;
            /** Format: int64 */
            errors: number;
            /** Format: int64 */
            excluded: number;
            firstError: string | null;
            id: string;
            /** Format: double */
            max: number | null;
            /** Format: double */
            mean: number | null;
            /** Format: double */
            min: number | null;
            /** Format: int64 */
            observations: number;
            /** Format: double */
            stdDev: number | null;
            /** Format: double */
            sum: number | null;
        };
        PluginEntry: {
            contract?: string;
            isConformant?: boolean;
            pluginId?: string;
            version?: string;
        };
        RegisterPluginRequest: {
            pluginId: string;
        };
        RunEvidence: {
            computedConfigHash: null | string;
            inputVerified: boolean;
            model: components["schemas"]["RunModel"];
            pinnedConfig: null | {
                [key: string]: unknown;
            };
            run: components["schemas"]["RunResponse"];
        };
        RunHistogramBin: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            hi: null | number;
            /** Format: double */
            lo: number;
        };
        RunModel: {
            modelHash: null | string;
            name: string;
            /** Format: double */
            targetRtp: null | number;
            /** Format: int64 */
            winCap: null | number;
        };
        RunPage: {
            /** Format: int64 */
            active: number;
            /** Format: int64 */
            completed: number;
            /** Format: int64 */
            failed: number;
            items: components["schemas"]["RunSummary"][];
            nextCursor: null | string;
            /** Format: int64 */
            partial: number;
            /** Format: int64 */
            total: number;
        };
        RunProgressMessage: {
            /** Format: int64 */
            capHits?: number;
            /** Format: date-time */
            completedAt?: null | string;
            /** Format: int64 */
            elapsedMs: number;
            histogram?: components["schemas"]["RunHistogramBin"][];
            /** Format: double */
            hitFrequency?: number;
            /** Format: double */
            maxWin?: number;
            measurementHash?: string | null;
            measurements?: components["schemas"]["MeasurementSnapshot"][];
            /** Format: int64 */
            nonZeroCount?: number;
            /** @description Complete persisted result carried by terminal snapshots. */
            resultJson?: null | string;
            runId: string;
            /** Format: double */
            runningRtp: number;
            /** Format: int64 */
            sampleCount: number;
            /**
             * Format: int64
             * @description Monotonic revision within streamEpoch, including status transitions.
             */
            sequence?: number;
            status: string;
            /** Format: double */
            stdErr: number;
            /** @description Opaque stream generation. A crash recovery checkpoint starts a new generation. */
            streamEpoch?: string;
            /** Format: int64 */
            totalSamples: number;
            /** Format: double */
            volatility?: number;
        };
        RunResponse: {
            /** Format: date-time */
            completedAt?: null | string;
            configHash?: null | string;
            configId: string;
            /** Format: int32 */
            configVersion?: number;
            /** Format: date-time */
            createdAt?: string;
            /** Format: int32 */
            degreeOfParallelism?: number;
            id: string;
            measurementHash?: string | null;
            measurements?: components["schemas"]["MeasurementDefinition"][];
            progress?: null | components["schemas"]["RunProgressMessage"];
            resultJson?: null | string;
            /** Format: int64 */
            seed?: number;
            /**
             * Format: int64
             * @description Monotonic revision within streamEpoch, including status transitions.
             */
            sequence?: number;
            status: string;
            /** @description Opaque stream generation. A crash recovery checkpoint starts a new generation. */
            streamEpoch?: string;
            streamScheme?: string;
        };
        RunSummary: {
            /** Format: date-time */
            completedAt: null | string;
            configHash: null | string;
            configId: string;
            /** Format: int32 */
            configVersion: number;
            /** Format: date-time */
            createdAt: string;
            /** Format: int32 */
            degreeOfParallelism: number;
            /** Format: int64 */
            elapsedMs: number;
            id: string;
            model: components["schemas"]["RunModel"];
            /** Format: double */
            rtp: null | number;
            /** Format: int64 */
            sampleCount: number;
            /** Format: int64 */
            seed: number;
            status: string;
            /** Format: double */
            stdErr: null | number;
            streamScheme: string;
            /** Format: int64 */
            totalSamples: number;
        };
        UpdateConfigRequest: {
            config: {
                [key: string]: unknown;
            };
        };
        ValidateRequest: {
            config: {
                [key: string]: unknown;
            };
        };
        VersionEntry: {
            createdAt?: string;
            id?: string;
            version?: number;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
};
export type $defs = Record<string, never>;
export interface operations {
    listConfigs: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Config list */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ConfigSummary"][];
                };
            };
        };
    };
    createConfig: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateConfigRequest"];
            };
        };
        responses: {
            /** @description Config created */
            201: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CreateConfigResponse"];
                };
            };
            /** @description Invalid JSON */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    getConfig: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Config detail */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ConfigDetail"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    updateConfig: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["UpdateConfigRequest"];
            };
        };
        responses: {
            /** @description Updated */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CreateConfigResponse"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    deleteConfig: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Deleted */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Config is pinned by saved run evidence */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    listConfigVersions: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Version list */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["VersionEntry"][];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    getConfigVersion: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
                version: number;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Config version detail */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ConfigDetail"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    evaluateLight: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["EvaluateLightRequest"];
            };
        };
        responses: {
            /** @description Evaluation result */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["EvaluateLightResponse"];
                };
            };
            /** @description Invalid JSON */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    listPlugins: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Plugin list */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PluginEntry"][];
                };
            };
        };
    };
    registerPlugin: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RegisterPluginRequest"];
            };
        };
        responses: {
            /** @description Registered */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Already registered */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    getPlugin: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Plugin detail */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PluginEntry"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    listRunArchive: {
        parameters: {
            query?: {
                cursor?: string;
                limit?: number;
                search?: string;
                status?: "all" | "completed" | "partial" | "failed" | "active";
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Saved run evidence */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RunPage"];
                };
            };
            /** @description Invalid query or cursor */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Authentication required in production */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    createRun: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CreateRunRequest"];
            };
        };
        responses: {
            /** @description Run enqueued */
            202: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RunResponse"];
                };
            };
            /** @description Invalid version, seed, workers or round budget */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Config not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    getRun: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Run status */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RunResponse"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    cancelRun: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Authoritative cancellation snapshot; completion may race with cancellation. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RunResponse"];
                };
            };
            /** @description Not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Already completed/failed/cancelled */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    getRunEvidence: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Saved run evidence */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RunEvidence"];
                };
            };
            /** @description Authentication required in production */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Run not found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    validateConfig: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ValidateRequest"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Invalid JSON */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
}
