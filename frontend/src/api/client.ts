import { QueryClient } from '@tanstack/react-query';
import createClient from 'openapi-fetch';
import type { paths } from './generated-types';

const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5152';

/**
 * Typed fetch client generated from the OpenAPI contract.
 * Every request and response is type-checked — no hand-written request types.
 */
export const apiClient = createClient<paths>({
  baseUrl: BASE_URL,
});

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
