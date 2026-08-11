using System;
using System.Threading;

namespace DocumentIDE.Infrastructure.Diagnostics
{
    /// <summary>
    /// Provides a per‑async‑flow CorrelationId that can be set at the start of a logical operation
    /// (e.g., a batch job, API request, or background task) and is automatically included in log events.
    /// </summary>
    public static class CorrelationIdProvider
    {
        private static readonly AsyncLocal<Guid?> _current = new();

        public static Guid? CurrentId => _current.Value;

        /// <summary>
        /// Generates a new CorrelationId and stores it in the async context.
        /// </summary>
        public static Guid NewId()
        {
            var id = Guid.NewGuid();
            _current.Value = id;
            return id;
        }

        /// <summary>
        /// Sets a specific CorrelationId for the current async context.
        /// </summary>
        public static void SetId(Guid id)
        {
            _current.Value = id;
        }

        /// <summary>
        /// Creates an IDisposable correlation scope that restores the prior ID when disposed.
        /// </summary>
        public static IDisposable BeginScope(Guid? id = null)
        {
            var previous = _current.Value;
            _current.Value = id ?? Guid.NewGuid();
            return new CorrelationScope(previous);
        }

        /// <summary>
        /// Clears the CorrelationId from the current context.
        /// </summary>
        public static void Clear() => _current.Value = null;

        private sealed class CorrelationScope : IDisposable
        {
            private readonly Guid? _previous;
            private bool _disposed;

            public CorrelationScope(Guid? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _current.Value = _previous;
                    _disposed = true;
                }
            }
        }
    }
}
