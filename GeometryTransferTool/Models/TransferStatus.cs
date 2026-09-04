namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Status representing the outcome of the geometry transfer operation (§13).
    /// </summary>
    public enum TransferStatus
    {
        /// <summary>
        /// Transfer has not been attempted yet (Preview phase).
        /// </summary>
        NotAttempted,

        /// <summary>
        /// Geometry transfer succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// Geometry transfer failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Transfer was skipped (e.g. BelowThreshold, Ambiguous, etc.).
        /// </summary>
        Skipped
    }
}
