import { parseModelJson } from '../lib/modelJson';
import { QueryClient } from '@tanstack/react-query';
import createClient from 'openapi-fetch';
import type { paths } from './generated-types';

export const BASE_URL = import.meta.env.VITE_API_URL ?? '';

/**
 * Typed fetch client generated from the OpenAPI contract.
 * Every request and response is type-checked — no hand-written request types.
 */
export const apiClient = createClient<paths>({
  baseUrl: BASE_URL,
});

// Validate raw saved-model responses before openapi-fetch converts JSON numbers.
apiClient.use({ async onResponse({ request, response }) {
  if (response.ok && /\/api\/configs(?:\/|$)/.test(new URL(request.url).pathname) && response.status !== 204) {
    const text = await response.clone().text();
    if (text.trim().startsWith('{')) parseModelJson(text);
  }
  return response;
} });

/**
 * TanStack Query client with sensible defaults.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});
