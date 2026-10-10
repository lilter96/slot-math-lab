import * as signalR from '@microsoft/signalr';
import type { HubPort } from './RunConnection';
import { HttpFailure, retryAfterMilliseconds } from './httpFailure';

/** Browser-only SignalR HTTP port. Its public HttpResponse type omits headers,
 * and negotiation wraps errors without retaining their status. Keep the
 * rejection from this start attempt for the supervisor, including Retry-After.
 * Browser fetch/WebSocket continue to own the same-origin authentication cookie. */
export class RunHubHttpClient extends signalR.HttpClient {
  failure: HttpFailure | null = null;
  private readonly fetchRequest: typeof fetch;
  constructor(fetchRequest: typeof fetch = globalThis.fetch.bind(globalThis)) { super(); this.fetchRequest = fetchRequest; }

  async send(request: signalR.HttpRequest): Promise<signalR.HttpResponse> {
    this.failure = null;
    if (request.abortSignal?.aborted) throw new signalR.AbortError();
    if (!request.method || !request.url) throw new Error('Connection request requires a method and URL.');
    const controller = new AbortController();
    let cancellation: Error | null = null;
    const abort = () => { cancellation = new signalR.AbortError(); controller.abort(); };
    if (request.abortSignal) request.abortSignal.onabort = abort;
    const timer = request.timeout && request.timeout > 0 ? setTimeout(() => {
      cancellation = new signalR.TimeoutError(); controller.abort();
    }, request.timeout) : null;
    const headers = new Headers({ 'X-Requested-With': 'XMLHttpRequest', ...request.headers });
    const body = request.content === '' ? undefined : request.content;
    if (body !== undefined && !headers.has('Content-Type')) headers.set('Content-Type',
      typeof body === 'string' ? 'text/plain;charset=UTF-8' : 'application/octet-stream');
    try {
      const response = await this.fetchRequest(request.url, { method: request.method, body, headers,
        cache: 'no-cache', credentials: request.withCredentials === true ? 'include' : 'same-origin',
        mode: 'cors', redirect: 'follow', signal: controller.signal });
      if (!response.ok) {
        this.failure = new HttpFailure(`Connection request rejected (${response.status}).`, response.status,
          retryAfterMilliseconds(response.headers.get('Retry-After')), 'connection');
        // A rejection needs no unbounded error-body read before its retry policy
        // takes effect. Release the body while retaining the actual HTTP metadata.
        void response.body?.cancel().catch(() => {});
        throw this.failure;
      }
      if (request.responseType && !['text', 'arraybuffer'].includes(request.responseType))
        throw new Error(`Unsupported connection response type: ${request.responseType}`);
      const content = request.responseType === 'arraybuffer' ? await response.arrayBuffer() : await response.text();
      return new signalR.HttpResponse(response.status, response.statusText, content);
    } catch (error) {
      throw cancellation ?? error;
    } finally {
      if (timer !== null) clearTimeout(timer);
      if (request.abortSignal?.onabort === abort) request.abortSignal.onabort = null;
    }
  }
}

export function createRunHub(url = '/hubs/runs', fetchRequest?: typeof fetch): HubPort {
  const http = new RunHubHttpClient(fetchRequest);
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(url, { transport: signalR.HttpTransportType.WebSockets, timeout: 8000, httpClient: http })
    .withServerTimeout(20000).withKeepAliveInterval(5000)
    .configureLogging(signalR.LogLevel.Warning).build();
  return {
    start: async () => {
      http.failure = null;
      try { await connection.start(); } catch (error) { throw http.failure ?? error; }
    },
    stop: () => connection.stop(),
    invoke: (method, id) => connection.invoke(method, id),
    on: (method, listener) => connection.on(method, listener),
    onclose: listener => connection.onclose(listener),
  };
}
