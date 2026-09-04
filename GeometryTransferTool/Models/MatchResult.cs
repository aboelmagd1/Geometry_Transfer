using System;

namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Central data model representing a Source polygon match result (§12).
    /// Used across Matching, Preview, Transfer, and Reporting.
    /// </summary>
    public class MatchResult
    {
        public string MatchId { get; set; } = string.Empty;

        public string RunId { get; set; } = string.Empty;

        public long SourceOid { get; set; }

        public long? TargetOid { get; set; }

        public double? OverlapPct { get; set; }

        public double ThresholdPct { get; set; }

        public int CandidateCount { get; set; }

        public double? SecondBestOverlapPct { get; set; }

        public MatchStatus MatchStatus { get; set; }

        public TransferStatus TransferStatus { get; set; } = TransferStatus.NotAttempted;

        public string Details { get; set; } = string.Empty;
 
        public DateTime RunDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Original geometry type of the source feature ("Polygon" or "Polyline") (§34).
        /// </summary>
        public string SourceGeometryType { get; set; } = "Polygon";

        /// <summary>
        /// Conversion status for polyline features ("None", "Converted", "Failed") (§34).
        /// </summary>
        public string ConversionStatus { get; set; } = "None";

        /// <summary>
        /// Cached working polygon geometry converted/prepared in memory once during matching (§34).
        /// Reused across Transfer, Results Table, and Results Feature Class without reconversion.
        /// </summary>
        public ArcGIS.Core.Geometry.Polygon? WorkingPolygon { get; set; }

        /// <summary>
        /// Alias/general accessor for WorkingPolygon to represent the candidate geometry to be inserted into Results Feature Class.
        /// </summary>
        public ArcGIS.Core.Geometry.Geometry? ResultGeometry
        {
            get => WorkingPolygon;
            set => WorkingPolygon = value as ArcGIS.Core.Geometry.Polygon;
        }

        // Backward compatibility properties
        public MatchStatus Status
        {
            get => MatchStatus;
            set => MatchStatus = value;
        }

        public double? OverlapPercentage
        {
            get => OverlapPct;
            set => OverlapPct = value;
        }

        // Display formatting properties for UI and DataGrid
        private string? _targetOidDisplay;
        public string TargetOidDisplay
        {
            get => _targetOidDisplay ?? (TargetOid.HasValue ? TargetOid.Value.ToString() : "-");
            set => _targetOidDisplay = value;
        }

        public string OverlapDisplay => OverlapPct.HasValue ? $"{OverlapPct.Value:F1}%" : "-";

        public string StatusDisplay => GetStatusDescription();

        public string TransferStatusDisplay => TransferStatus switch
        {
            TransferStatus.NotAttempted => "Not Attempted",
            TransferStatus.Success => "Success",
            TransferStatus.Failed => "Failed",
            TransferStatus.Skipped => "Skipped",
            _ => TransferStatus.ToString()
        };

        /// <summary>
        /// Gets whether this result is confirmed and valid for geometry transfer (§12, §15).
        /// </summary>
        public bool CanTransfer => MatchStatus == MatchStatus.Matched && TargetOid.HasValue && TransferStatus != TransferStatus.Success;

        private string GetStatusDescription()
        {
            return MatchStatus switch
            {
                MatchStatus.Matched => "Matched",
                MatchStatus.BelowThreshold => "Below Threshold",
                MatchStatus.Ambiguous => "Ambiguous",
                MatchStatus.TargetAlreadyMatched => "Target Already Matched",
                MatchStatus.NoIntersection => "No Intersection",
                MatchStatus.InvalidGeometry => "Invalid Geometry",
                MatchStatus.Failed => "Failed",
                MatchStatus.Skipped => "Skipped",
                _ => MatchStatus.ToString()
            };
        }
    }
}
