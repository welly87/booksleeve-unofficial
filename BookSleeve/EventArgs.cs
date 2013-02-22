using System;

namespace BookSleeve
{
    /// <summary>
    ///     Event data relating to an exception in Redis
    /// </summary>
    public sealed class ErrorEventArgs : EventArgs
    {
        internal ErrorEventArgs(Exception exception, string cause, bool isFatal)
        {
            Exception = exception;
            Cause = cause;
            IsFatal = isFatal;
        }

        /// <summary>
        ///     The exception that occurred
        /// </summary>
        public Exception Exception { get; private set; }

        /// <summary>
        ///     What the system was doing when this error occurred
        /// </summary>
        public string Cause { get; private set; }

        /// <summary>
        ///     True if this error has rendered the connection unusable
        /// </summary>
        public bool IsFatal { get; private set; }
    }
}