export type paths = {
    "/api/ai/auto-tune": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AutoTuneRequest"];
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/explain": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ExplainRequest"];
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/generate-graph": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["GenerateGraphRequest"];
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ai/lint": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LintRequest"];
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
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
    "/api/auth/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/token": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["TokenRequest"];
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/configs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ConfigListResponse"][];
                    };
                };
            };
        };
        put?: never;
        post: {
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
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CreateConfigResponse"];
                    };
                };
            };
        };
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
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ConfigDetailResponse"];
                    };
                };
            };
        };
        put: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CreateConfigResponse"];
                    };
                };
            };
        };
        post?: never;
        delete: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
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
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
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
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/evaluate/graph": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["GraphEvaluationRequest"];
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
            };
        };
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
        post: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["EvaluateLightResponse"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/persisted/{projectId}/configs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": unknown;
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/persisted/{projectId}/configs/{version}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                    version: number;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/persisted/{projectId}/configs/latest": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    projectId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/persisted/cache/{configHash}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    configHash: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    configHash: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": unknown;
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
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/persisted/cache/{configHash}/recompute-count": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    configHash: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/play/round": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["PlayRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PlayResponse"];
                    };
                };
            };
        };
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
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post: {
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
                /** @description OK */
                200: {
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
    "/api/plugins/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
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
        get: {
            parameters: {
                query?: {
                    cursor?: string;
                    limit?: number;
                    search?: string;
                    status?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunPage"];
                    };
                };
            };
        };
        put?: never;
        post: {
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
                /** @description Accepted */
                202: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"];
                    };
                };
            };
        };
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
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunResponse"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
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
        get: {
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
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RunEvidence"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/calibration": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["MeasurementCalibrationRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MeasurementCalibrationReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/reference": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["EnumerationBudget"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["GraphMeasurementReference"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/reference/comparison": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["ExactLawComparisonRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RetainedReferenceOfExactLawComparisonReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/reference/distribution": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["FiniteModelRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RetainedReferenceOfFiniteModelReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/reference/markov": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["MarkovModelRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RetainedReferenceOfMarkovModelReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/{id}/measurements/replay": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
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
                    "application/json": components["schemas"]["MeasurementReplayRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MeasurementReplayReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/measurements/reference/comparison": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ExactLawComparisonRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExactLawComparisonReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/measurements/reference/distribution": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["FiniteModelRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FiniteModelReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/measurements/reference/markov": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MarkovModelRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MarkovModelReport"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/runs/measurements/reference/planning": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SamplePlanRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SamplePlanReport"];
                    };
                };
            };
        };
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
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["MeasurementSchemaRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MeasurementSchema"];
                    };
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
        post: {
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
                    content: {
                        "application/json": components["schemas"]["ValidateResponse"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/ready": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
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
        AssertionSummary: {
            /** Format: int64 */
            checked: number;
            kind: string;
            status: string;
            /** Format: int64 */
            violations: number;
        };
        AutoTuneRequest: {
            config: unknown;
            /** Format: int32 */
            maxIterations?: null | number;
            /** Format: double */
            maxWinMax?: null | number;
            /** Format: double */
            maxWinMin?: null | number;
            /** Format: double */
            targetRtp?: number;
            /** Format: double */
            volatilityMax?: null | number;
            /** Format: double */
            volatilityMin?: null | number;
        };
        ConfigDetailResponse: {
            config: unknown;
            /** Format: date-time */
            createdAt: string;
            id: string;
            /** Format: int32 */
            version: number;
        };
        ConfigListResponse: {
            id: string;
            /** Format: int32 */
            latestVersion: number;
            name: string;
            /** Format: date-time */
            updatedAt: string;
        };
        ContributionNormalization: {
            basis: string;
            /** Format: double */
            externalTurnover: null | number;
            /** Format: int64 */
            paidRounds: number;
        };
        CreateConfigRequest: {
            config: unknown;
        };
        CreateConfigResponse: {
            id: string;
            /** Format: int32 */
            version: number;
        };
        CreateRunRequest: {
            configId: string;
            /** Format: int32 */
            configVersion?: null | number;
            /** Format: int32 */
            degreeOfParallelism?: number;
            execution?: null | components["schemas"]["ExecutionOptions"];
            measurements?: components["schemas"]["MeasurementInput"][];
            /** Format: int32 */
            progressBatchSize?: null | number;
            /** Format: int32 */
            sampleSize?: null | number;
            /** Format: int64 */
            seed?: number;
        };
        DiagnosticArtifact: {
            configHash: null | string;
            /** Format: date-time */
            createdAt: string;
            id: string;
            input: components["schemas"]["JsonElement"];
            inputSha256: string;
            kind: string;
            measurementHash: null | string;
            output: components["schemas"]["JsonElement"];
            outputSha256: string;
            runtimeProvenance: components["schemas"]["RuntimeProvenance"];
        };
        DiagnosticRetention: {
            artifactId: null | string;
            note: string;
            retained: boolean;
        };
        DistributionBin: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            lower: null | number;
            /** Format: double */
            sum: number;
            /** Format: double */
            sumSquares: number;
            /** Format: double */
            upper: null | number;
        };
        DistributionComparison: {
            calibration: string;
            /** Format: double */
            cdfDistance: number;
            /** Format: double */
            chiSquare: null | number;
            /** Format: int32 */
            degreesOfFreedom: number;
            expectedCountsAdequate: boolean;
            /** Format: double */
            pValue: null | number;
            /** Format: double */
            totalVariation: number;
            /** Format: int64 */
            unexpectedObservations: number;
        };
        EnumeratedCohort: {
            conditionalMean: null | string;
            key: string;
            knownSumPerRound: string;
            pair: null | components["schemas"]["EnumeratedJointLaw"];
            support: components["schemas"]["EnumeratedValue"][];
            supportComplete: boolean;
            validPerRound: string;
        };
        EnumeratedJointLaw: {
            complete: boolean;
            covariance: null | string;
            meanX: null | string;
            meanY: null | string;
            pairedPerRound: string;
            support: components["schemas"]["EnumeratedJointValue"][];
            varianceDifference: null | string;
            varianceSum: null | string;
            varianceX: null | string;
            varianceY: null | string;
        };
        EnumeratedJointValue: {
            massPerPaidRound: string;
            /** Format: double */
            x: number;
            /** Format: double */
            y: number;
        };
        EnumeratedMeasurement: {
            assertionStatus?: null | string;
            conditionalMean: null | string;
            eligiblePerRound: string;
            excludedPerRound: string;
            firstError: null | string;
            groups?: components["schemas"]["EnumeratedCohort"][];
            groupsComplete?: boolean;
            id: string;
            invalidPerRound: string;
            knownAssertionViolationsPerRound?: null | string;
            knownSumPerRound: string;
            pair?: null | components["schemas"]["EnumeratedJointLaw"];
            support: components["schemas"]["EnumeratedValue"][];
            supportComplete: boolean;
            validPerRound: string;
            weighting?: string;
        };
        EnumeratedValue: {
            massPerPaidRound: string;
            /** Format: double */
            value: number;
        };
        EnumerationBudget: {
            /**
             * Format: int32
             * @default 2048
             */
            maximumFrontier: number;
            /**
             * Format: int32
             * @default 250000
             */
            maximumOperations: number;
            /**
             * Format: int32
             * @default 10000
             */
            maximumPaths: number;
        };
        EvaluateLightRequest: {
            config: unknown;
            /** Format: int32 */
            maxBranches?: null | number;
            /** Format: int32 */
            sampleSize?: null | number;
            /** Format: int64 */
            seed?: number;
        };
        EvaluateLightResponse: {
            ci95?: null | string;
            /** Format: double */
            elapsedMs?: null | number;
            errors?: components["schemas"]["ValidateErrorItem"][];
            /** Format: double */
            hi?: null | number;
            /** Format: double */
            hitFrequency?: null | number;
            /** Format: double */
            lo?: null | number;
            provenance?: null | string;
            /** Format: double */
            prunedMass?: null | number;
            /** Format: double */
            rtp?: null | number;
            /** Format: int32 */
            sampleCount?: null | number;
            /** Format: int64 */
            seed?: number;
            /** Format: double */
            stdErr?: null | number;
            strategy: string;
            /** Format: double */
            volatility?: null | number;
        };
        ExactLawComparisonReport: {
            algorithmVersion?: null | string;
            assumptions: string;
            authoredInputSha256?: null | string;
            cdfDistance: string;
            coreBinarySha256?: null | string;
            equal: boolean;
            leftMean: string;
            meanDifference: string;
            rightMean: string;
            support: components["schemas"]["RationalLawDifference"][];
            totalVariation: string;
            unit: string;
        };
        ExactLawComparisonRequest: {
            left: components["schemas"]["RationalOutcome"][];
            right: components["schemas"]["RationalOutcome"][];
            /** @default value units */
            unit: string;
        };
        ExecutionOptions: {
            featureMetricId?: null | string;
            /** Format: double */
            initialBankroll?: number;
            persistentKeys?: string[];
            regime?: string;
            samplingEngine?: string;
            /** Format: int32 */
            sessionLength?: number;
            streamScheme?: null | string;
            /** Format: double */
            wager?: number;
        };
        ExecutionSummary: {
            /** Format: int64 */
            attemptedRounds: number;
            /** Format: int64 */
            cancelledRounds: number;
            carriesState: boolean;
            /** Format: int64 */
            completedRounds: number;
            /** Format: int64 */
            completedSessions: number;
            /** Format: int64 */
            failedRounds: number;
            /** Format: int64 */
            interruptedRounds: number;
            /** Format: int64 */
            interruptedSessions: number;
            loopTerminations?: components["schemas"]["LoopTerminationSummary"][];
            loopTerminationsComplete?: boolean;
            monetaryAccounting?: null | string;
            regime: string;
            samplingEngine?: string;
            sessionMetrics: components["schemas"]["MeasurementSnapshot"][];
            sessionPolicy: string;
            stateResetPolicy: string;
        };
        ExplainRequest: {
            ci95?: null | string;
            config?: unknown;
            /** Format: double */
            hitFrequency?: number;
            /** Format: double */
            rtp?: number;
            /** Format: double */
            volatility?: number;
        };
        FiniteModelReport: {
            algorithmVersion?: null | string;
            assumptions: string;
            authoredInputSha256?: null | string;
            coreBinarySha256?: null | string;
            cost: string;
            excessKurtosis: null | string;
            hitProbability: components["schemas"]["RationalBounds"];
            houseEdge: null | string;
            maximumKnownOutcome: string;
            mean: components["schemas"]["RationalBounds"];
            provenance: string;
            provenMaximum: null | string;
            prunedMass: string;
            retainedMass: string;
            rtp: components["schemas"]["RationalBounds"];
            secondMoment: components["schemas"]["RationalBounds"];
            skewness: null | string;
            variance: components["schemas"]["RationalBounds"];
        };
        FiniteModelRequest: {
            /** @default 1 */
            cost: string;
            outcomes: components["schemas"]["RationalOutcome"][];
            provenMaximum?: null | string;
            /** @default 0 */
            prunedMass: string;
        };
        GenerateGraphRequest: {
            prompt: string;
        };
        GraphEnumerationReport: {
            /** Format: int32 */
            completedPaths: number;
            maximumKnownPayout?: null | string;
            measurements: components["schemas"]["EnumeratedMeasurement"][];
            numericalSemantics: string;
            /** Format: int32 */
            operations: number;
            reachableMaximum?: null | string;
            retainedRoundMass: string;
            roundMean: components["schemas"]["RationalBounds"];
            status: string;
            unresolvedRoundMass: string;
        };
        GraphEvaluationRequest: {
            config: components["schemas"]["JsonElement"];
            /**
             * Format: int32
             * @default 1
             */
            degreeOfParallelism: number;
            /**
             * Format: double
             * @default 0.00001
             */
            epsilon: number;
            /**
             * Format: int32
             * @default 10000
             */
            maxBranches: number;
            /** @default Sampled */
            mode: string;
            /**
             * Format: int32
             * @default 10000
             */
            samples: number;
            /**
             * Format: int64
             * @default 42
             */
            seed: number;
        };
        GraphMeasurementReference: {
            configHash: string;
            measurementHash: null | string;
            report: components["schemas"]["GraphEnumerationReport"];
            retention?: null | components["schemas"]["DiagnosticRetention"];
            runtimeProvenance: components["schemas"]["RuntimeProvenance"];
        };
        JointDiagnostics: {
            calibration: string;
            /** Format: double */
            chiSquare: null | number;
            complete: boolean;
            /** Format: int32 */
            degreesOfFreedom: number;
            expectedCountsAdequate: boolean;
            /** Format: double */
            pValue: null | number;
            support: components["schemas"]["JointFrequency"][];
        };
        JointFrequency: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            probability: number;
            /** Format: double */
            x: number;
            /** Format: double */
            y: number;
        };
        JsonElement: unknown;
        LintRequest: {
            config: unknown;
            /** Format: double */
            rtp?: null | number;
        };
        LoopTerminationSummary: {
            /** Format: int64 */
            completedInvocations: number;
            /** Format: int64 */
            conditionCompletions: number;
            /** Format: int32 */
            maximumIterations: number;
            /** Format: int32 */
            minimumIterations: number;
            /** Format: int64 */
            modelLimitCompletions: number;
            nodeId: string;
            /** Format: int64 */
            totalIterations: number;
        };
        MarkovModelReport: {
            absorptionProbability?: null | string;
            algorithmVersion?: null | string;
            authoredInputSha256?: null | string;
            coreBinarySha256?: null | string;
            detail: string;
            durationSecondMoment?: null | string;
            durationVariance?: null | string;
            expectedDuration: null | string;
            expectedReward: null | string;
            expectedVisits: string[];
            longRunCostPerStep?: null | string;
            longRunReturn?: null | string;
            longRunRewardPerStep?: null | string;
            reachableStates: number[];
            stationaryOccupancy?: null | string[];
            status: string;
        };
        MarkovModelRequest: {
            costs?: null | string[];
            /**
             * Format: int32
             * @default 0
             */
            initialState: number;
            rewards: string[];
            /** @default false */
            stationary: boolean;
            transientMatrix: string[][];
        };
        MeasurementAnalysis: {
            assertion?: null | components["schemas"]["AssertionSummary"];
            bins: components["schemas"]["DistributionBin"][];
            checks: components["schemas"]["VerificationCheck"][];
            clusteredMeanInterval: null | components["schemas"]["NumericInterval"];
            comparison: null | components["schemas"]["DistributionComparison"];
            /** Format: int64 */
            count: number;
            /** Format: int64 */
            distinctParents: number;
            /** Format: int64 */
            duplicateAwards: number;
            /** Format: int64 */
            entries: number;
            /** Format: int64 */
            exits: number;
            groups: {
                [key: string]: components["schemas"]["MeasurementAnalysis"];
            };
            groupsComplete?: boolean;
            /** Format: double */
            max: null | number;
            /** Format: double */
            mean: null | number;
            meanAbsoluteDeviationBounds: null | components["schemas"]["NumericInterval"];
            meanInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            meanStandardError: null | number;
            /** Format: double */
            min: null | number;
            moments: components["schemas"]["MomentSummary"];
            normalization?: null | components["schemas"]["ContributionNormalization"];
            pair: null | components["schemas"]["PairSummary"];
            probabilityInterval: null | components["schemas"]["NumericInterval"];
            quantiles: components["schemas"]["QuantileEstimate"][];
            reduction: string;
            /** Format: double */
            requiredSampleSize: null | number;
            sequence: null | components["schemas"]["SequenceSummary"];
            sequentialMeanInterval: null | components["schemas"]["NumericInterval"];
            subject: string;
            /** Format: double */
            sum: null | number;
            support: components["schemas"]["ValueFrequency"][];
            supportComplete: boolean;
            tails: components["schemas"]["TailSummary"][];
            transitions: components["schemas"]["TransitionFrequency"][];
            transitionsComplete: boolean;
            /** Format: int64 */
            unclosedEpisodes: number;
            /** Format: int64 */
            uniqueAwards: number;
            upperTails: components["schemas"]["UpperTailEstimate"][];
            weights: null | components["schemas"]["WeightedSummary"];
        };
        MeasurementCalibrationReport: {
            measurementHash: null | string;
            measurementId: string;
            report: components["schemas"]["NullCalibrationReport"];
            retention?: null | components["schemas"]["DiagnosticRetention"];
            runId: string;
        };
        MeasurementCalibrationRequest: {
            measurementId: string;
            /**
             * Format: int32
             * @default 2000
             */
            replicates: number;
            /**
             * Format: int64
             * @default 42
             */
            seed: number;
            /** @default cdf */
            statistic: string;
        };
        MeasurementField: {
            name: string;
            type: string;
        };
        MeasurementInput: {
            filter?: null | components["schemas"]["JsonElement"];
            id: string;
            name: string;
            nodeId?: null | string;
            options?: null | components["schemas"]["JsonElement"];
            unit?: string;
            value?: null | components["schemas"]["JsonElement"];
        };
        MeasurementPoint: {
            label: string;
            nodeId: string;
        };
        MeasurementReplayReport: {
            configHash: string;
            measurementHash: null | string;
            measurements: components["schemas"]["MeasurementSnapshot"][];
            reconstruction: string;
            retention?: null | components["schemas"]["DiagnosticRetention"];
            /** Format: int64 */
            roundIndex: number;
            runId: string;
            runtimeProvenance: components["schemas"]["RuntimeProvenance"];
        };
        MeasurementReplayRequest: {
            /** Format: int64 */
            roundIndex: number;
        };
        MeasurementSchema: {
            fields: components["schemas"]["MeasurementField"][];
            points: components["schemas"]["MeasurementPoint"][];
        };
        MeasurementSchemaRequest: {
            config: components["schemas"]["JsonElement"];
            measurements?: null | components["schemas"]["MeasurementInput"][];
        };
        MeasurementSnapshot: {
            analysis?: null | components["schemas"]["MeasurementAnalysis"];
            /** Format: int64 */
            count: number;
            /** Format: int64 */
            errors: number;
            /** Format: int64 */
            excluded: number;
            firstError: null | string;
            id: string;
            /** Format: double */
            max: null | number;
            /** Format: double */
            mean: null | number;
            /** Format: double */
            min: null | number;
            /** Format: int64 */
            observations: number;
            /** Format: double */
            stdDev: null | number;
            /** Format: double */
            sum: null | number;
            witnesses?: components["schemas"]["MeasurementWitness"][];
        };
        MeasurementWitness: {
            detail: null | string;
            group: null | string;
            kind: string;
            nodeId: null | string;
            /** Format: int64 */
            observationOrdinal: number;
            /** Format: double */
            pair: null | number;
            /** Format: int64 */
            roundIndex: number;
            /** Format: double */
            value: null | number;
        };
        MomentSummary: {
            /** Format: double */
            coefficientOfVariation: null | number;
            /** Format: double */
            excessKurtosis: null | number;
            /** Format: double */
            meanAbsoluteDeviation: null | number;
            /** Format: double */
            populationVariance: null | number;
            /** Format: double */
            sampleVariance: null | number;
            /** Format: double */
            secondMoment: null | number;
            /** Format: double */
            skewness: null | number;
        };
        NullCalibrationReport: {
            assumptions: string;
            authoredInputSha256?: null | string;
            coreBinarySha256?: null | string;
            /** Format: int64 */
            evaluations: number;
            /** Format: int64 */
            extremeEvaluations: number;
            method: string;
            /** Format: double */
            minimumResolvablePValue: null | number;
            monteCarloTailInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            observedStatistic: null | number;
            /** Format: double */
            pValue: number;
            /** Format: int64 */
            seed: number;
            statistic: string;
            /** Format: int64 */
            subjects: number;
        };
        NumericInterval: {
            assumptions: string;
            /** Format: double */
            lower: number;
            method: string;
            /** Format: double */
            upper: number;
        };
        PairSummary: {
            /** Format: double */
            correlation: null | number;
            /** Format: int64 */
            count: number;
            /** Format: double */
            covariance: null | number;
            differenceInterval?: null | components["schemas"]["NumericInterval"];
            joint?: null | components["schemas"]["JointDiagnostics"];
            /** Format: double */
            meanDifference: null | number;
            /** Format: double */
            meanY: null | number;
            /** Format: double */
            ratio: null | number;
            ratioInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            sampleVarianceDifference?: null | number;
            /** Format: double */
            sampleVarianceSum?: null | number;
            /** Format: double */
            sampleVarianceY?: null | number;
            /** Format: double */
            sumY: number;
        };
        PlayRequest: {
            config: components["schemas"]["JsonElement"];
            /**
             * Format: int64
             * @default 0
             */
            roundIndex: number;
            /**
             * Format: int64
             * @default 42
             */
            seed: number;
            /** @default false */
            trace: boolean;
        };
        PlayResponse: {
            configHash: string;
            /** Format: int64 */
            roundIndex: number;
            /** Format: int64 */
            seed: number;
            state: unknown;
            /** Format: double */
            win: number;
            winDenominator: string;
            winNumerator: string;
        };
        QuantileEstimate: {
            /** Format: double */
            lower: null | number;
            method: string;
            /** Format: double */
            probability: number;
            /** Format: double */
            upper: null | number;
            /** Format: double */
            value: null | number;
        };
        RationalBounds: {
            lower: string;
            upper: null | string;
        };
        RationalLawDifference: {
            difference: string;
            leftProbability: string;
            rightProbability: string;
            value: string;
        };
        RationalOutcome: {
            probability: string;
            value: string;
        };
        RegisterPluginRequest: {
            contract: string;
            pluginId: string;
            version?: null | string;
        };
        RetainedReferenceOfExactLawComparisonReport: {
            report: null | components["schemas"]["ExactLawComparisonReport"];
            retention: components["schemas"]["DiagnosticRetention"];
            runId: string;
        };
        RetainedReferenceOfFiniteModelReport: {
            report: null | components["schemas"]["FiniteModelReport"];
            retention: components["schemas"]["DiagnosticRetention"];
            runId: string;
        };
        RetainedReferenceOfMarkovModelReport: {
            report: null | components["schemas"]["MarkovModelReport"];
            retention: components["schemas"]["DiagnosticRetention"];
            runId: string;
        };
        RunEvidence: {
            computedConfigHash: null | string;
            diagnostics?: components["schemas"]["DiagnosticArtifact"][];
            inputVerified: boolean;
            model: components["schemas"]["RunModel"];
            pinnedConfig: null | components["schemas"]["JsonElement"];
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
            /** Format: int32 */
            active: number;
            /** Format: int32 */
            completed: number;
            /** Format: int32 */
            failed: number;
            items: components["schemas"]["RunSummary"][];
            nextCursor: null | string;
            /** Format: int32 */
            partial: number;
            /** Format: int32 */
            total: number;
        };
        RunProgressMessage: {
            /** Format: int64 */
            capHits?: number;
            /** Format: date-time */
            completedAt?: null | string;
            /** Format: int64 */
            elapsedMs: number;
            execution?: null | components["schemas"]["ExecutionSummary"];
            histogram?: components["schemas"]["RunHistogramBin"][];
            /** Format: double */
            hitFrequency?: number;
            /** Format: double */
            maxWin?: number;
            measurementHash?: null | string;
            measurements?: components["schemas"]["MeasurementSnapshot"][];
            /** Format: int64 */
            nonZeroCount?: number;
            resultJson?: null | string;
            runId: string;
            /** Format: double */
            runningRtp: number;
            /** Format: int64 */
            sampleCount: number;
            /** Format: int64 */
            sequence?: number;
            status: string;
            /** Format: double */
            stdErr: number;
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
            execution?: null | components["schemas"]["ExecutionOptions"];
            id: string;
            measurementHash?: null | string;
            measurements?: components["schemas"]["MeasurementInput"][];
            progress?: null | components["schemas"]["RunProgressMessage"];
            resultJson?: null | string;
            runtimeProvenance?: null | components["schemas"]["RuntimeProvenance"];
            /** Format: int64 */
            seed?: number;
            /** Format: int64 */
            sequence?: number;
            status: string;
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
        RuntimeProvenance: {
            apiBinarySha256: null | string;
            coreBinarySha256: null | string;
            framework: string;
            measurementContract: string;
            numericalMethods: string;
        };
        SamplePlanReport: {
            /** Format: double */
            allocatedAlpha: number;
            /** Format: double */
            approximateMeanRounds: null | number;
            assumptions: string;
            authoredInputSha256?: null | string;
            coreBinarySha256?: null | string;
            detectionBudgetSufficient: null | boolean;
            /** Format: double */
            eventDetectionRounds: null | number;
            meanBudgetSufficient: null | boolean;
            /** Format: double */
            normalCritical: number;
            /** Format: int64 */
            timeUniformBoundedMeanRounds: null | number;
            /** Format: double */
            zeroObservedEventUpper: number;
        };
        SamplePlanRequest: {
            /**
             * Format: double
             * @default 0.95
             */
            confidence: number;
            /**
             * Format: double
             * @default 0.95
             */
            detectionPower: number;
            /**
             * Format: int32
             * @default 1
             */
            errorFamilySize: number;
            /** Format: double */
            eventProbability?: null | number;
            /** Format: double */
            lowerBound?: null | number;
            /**
             * Format: double
             * @default 0.001
             */
            precision: number;
            /**
             * Format: int64
             * @default 100000
             */
            roundBudget: number;
            /** Format: double */
            upperBound?: null | number;
            /** Format: double */
            variance?: null | number;
        };
        SequenceSummary: {
            /** Format: int64 */
            adjacentPairs: number;
            autocorrelations: {
                [key: string]: null | number;
            };
            /** Format: int64 */
            completedGaps: number;
            /** Format: int64 */
            count: number;
            /** Format: int64 */
            equalAdjacentPairs: number;
            /** Format: int64 */
            events: number;
            /** Format: int64 */
            longestDrought: number;
            /** Format: int64 */
            longestEventStreak: number;
            /** Format: double */
            meanGap: null | number;
            ordered: boolean;
            stateDwell: components["schemas"]["StateDwell"][];
            stateDwellComplete: boolean;
        };
        StateDwell: {
            /** Format: int64 */
            completedRuns: number;
            /** Format: int64 */
            maximumLength: number;
            /** Format: double */
            meanLength: number;
            /** Format: int64 */
            observations: number;
            /** Format: double */
            state: number;
        };
        TailSummary: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            mean: null | number;
            /** Format: double */
            probability: null | number;
            /** Format: double */
            secondMoment: null | number;
            /** Format: double */
            sum: number;
            /** Format: double */
            threshold: number;
        };
        TokenRequest: {
            email: null | string;
            password?: null | string;
            userId: string;
        };
        TransitionFrequency: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            from: number;
            /** Format: int64 */
            fromExposure: number;
            /** Format: double */
            probability: number;
            /** Format: double */
            to: number;
        };
        UpdateConfigRequest: {
            config: unknown;
        };
        UpperTailEstimate: {
            /** Format: double */
            lowerMean: null | number;
            /** Format: double */
            lowerReturnShare: null | number;
            /** Format: double */
            mean: null | number;
            method: string;
            /** Format: double */
            quantile: number;
            /** Format: double */
            tailMass: number;
            /** Format: double */
            upperMean: null | number;
            /** Format: double */
            upperReturnShare: null | number;
        };
        ValidateErrorItem: {
            code: string;
            edgeId?: null | string;
            message: string;
            nodeId?: null | string;
        };
        ValidateRequest: {
            config: unknown;
        };
        ValidateResponse: {
            errors?: components["schemas"]["ValidateErrorItem"][];
            isValid: boolean;
        };
        ValueFrequency: {
            /** Format: int64 */
            count: number;
            /** Format: double */
            sum: number;
            /** Format: double */
            value: number;
        };
        VerificationCheck: {
            detail: string;
            /** Format: double */
            difference: null | number;
            id: string;
            /** Format: double */
            observed: null | number;
            /** Format: double */
            reference: null | number;
            status: string;
        };
        WeightedSummary: {
            /** Format: double */
            effectiveSampleSize: null | number;
            /** Format: double */
            eventEstimate: null | number;
            eventInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            maxWeight: number;
            /** Format: double */
            minWeight: number;
            /** Format: double */
            ordinaryEstimate: null | number;
            ordinaryInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            pairedRatio: null | number;
            pairedRatioInterval: null | components["schemas"]["NumericInterval"];
            /** Format: double */
            selfNormalizedEstimate: null | number;
            /** Format: double */
            weightSquares: number;
            /** Format: double */
            weightSum: number;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
};
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
