using System.Collections.ObjectModel;

namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Configuration settings for polygon matching and geometry transfer.
    /// </summary>
    public class TransferSettings
    {
        public string SourceLayerName { get; set; } = string.Empty;
        public string TargetLayerName { get; set; } = string.Empty;
        public string SourceLayerUri { get; set; } = string.Empty;
        public string TargetLayerUri { get; set; } = string.Empty;

        /// <summary>
        /// Whether to ignore minimum overlap threshold and transfer any positive overlap (>0%).
        /// </summary>
        public bool IgnoreThreshold { get; set; } = false;

        /// <summary>
        /// Minimum required overlap percentage (1–100%). Default is 80%.
        /// </summary>
        public double OverlapThreshold { get; set; } = 80.0;

        /// <summary>
        /// Ambiguity tolerance difference percentage (0.1–20%). Default is 2%.
        /// </summary>
        public double AmbiguityTolerance { get; set; } = 2.0;

        /// <summary>
        /// Matching mode identifier (default "Polygon Overlap Percentage").
        /// </summary>
        public string MatchingMethod { get; set; } = "Polygon Overlap Percentage";

        /// <summary>
        /// Whether optional attribute mapping is enabled. Default false (Geometry Only).
        /// </summary>
        public bool AttributeMappingEnabled { get; set; } = false;

        /// <summary>
        /// Collection of field mappings.
        /// </summary>
        public ObservableCollection<AttributeMappingItem> AttributeMappings { get; set; } = new();

        /// <summary>
        /// Safety safeguard: Allow geometry transfer when Source or Target layer is an HTTP/Web Feature Service (From / To).
        /// Default is false (blocked).
        /// </summary>
        public bool AllowWebServiceTransfer { get; set; } = false;

        /// <summary>
        /// Backward-compatibility alias for AllowWebServiceTransfer.
        /// </summary>
        public bool AllowWebServiceSourceTransfer
        {
            get => AllowWebServiceTransfer;
            set => AllowWebServiceTransfer = value;
        }

        /// <summary>
        /// Whether to automatically create Results Table upon Transfer (§25, §40). Default true.
        /// </summary>
        public bool CreateResultsTable { get; set; } = true;

        /// <summary>
        /// Whether to automatically create polygon Results Feature Class upon Transfer. Default false.
        /// </summary>
        public bool CreateResultsFeatureClass { get; set; } = false;

        /// <summary>
        /// Output Location choice: "ProjectDefaultGdb", "TargetWorkspace", "CustomGdb" (§25).
        /// </summary>
        public string OutputLocationType { get; set; } = "ProjectDefaultGdb";

        /// <summary>
        /// Custom geodatabase path when OutputLocationType is CustomGdb.
        /// </summary>
        public string CustomGdbPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether to create a dynamic attribute snapshot table (§20). Default false.
        /// </summary>
        public bool IncludeAttributeSnapshot { get; set; } = false;

        /// <summary>
        /// Dynamic fields selected from Source Layer to snapshot.
        /// </summary>
        public List<string> SelectedSnapshotFields { get; set; } = new();

        /// <summary>
        /// Whether to immediately execute transfer upon clicking Run. Default false.
        /// </summary>
        public bool SkipPreview { get; set; } = false;
    }
}
