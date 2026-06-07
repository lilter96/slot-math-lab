import { Component, type ReactNode } from 'react';

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error?: Error;
  errorInfo?: string;
}

export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(error: Error): Partial<ErrorBoundaryState> {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('[ErrorBoundary]', error, errorInfo);
    this.setState({ errorInfo: errorInfo.componentStack ?? undefined });
  }

  handleReset = () => {
    this.setState({ hasError: false, error: undefined, errorInfo: undefined });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;

      return (
        <div style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100%',
          padding: 48,
          textAlign: 'center',
          color: 'var(--text)',
          fontFamily: 'var(--sans)',
        }}>
          <div style={{
            fontSize: 48,
            marginBottom: 16,
            opacity: 0.3,
          }}>⚠</div>
          <h2 style={{ margin: '0 0 8px', fontSize: 18, fontWeight: 600 }}>
            Something went wrong
          </h2>
          <p style={{
            color: 'var(--muted)',
            fontSize: 13,
            maxWidth: 400,
            margin: '0 0 24px',
          }}>
            {this.state.error?.message || 'An unexpected error occurred.'}
          </p>
          <button
            className="btn"
            onClick={this.handleReset}
            style={{ borderColor: 'var(--sampled)', color: 'var(--sampled)' }}
          >
            Try again
          </button>
          {this.state.errorInfo && (
            <details style={{
              marginTop: 24,
              textAlign: 'left',
              maxWidth: 600,
            }}>
              <summary style={{
                cursor: 'pointer',
                color: 'var(--faint)',
                fontSize: 11,
                fontFamily: 'var(--mono)',
              }}>
                Stack trace
              </summary>
              <pre style={{
                color: 'var(--faint)',
                fontSize: 10,
                fontFamily: 'var(--mono)',
                whiteSpace: 'pre-wrap',
                background: 'var(--bg-2)',
                padding: 12,
                borderRadius: 8,
                marginTop: 8,
                maxHeight: 300,
                overflow: 'auto',
              }}>
                {this.state.error?.stack}
                {this.state.errorInfo}
              </pre>
            </details>
          )}
        </div>
      );
    }

    return this.props.children;
  }
}
