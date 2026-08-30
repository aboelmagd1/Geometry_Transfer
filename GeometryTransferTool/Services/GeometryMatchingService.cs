using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Pure, read-only matching service executing the Matching Phase (§5–§8).
    /// Extracts selected geometries, applies spatial reprojections, evaluates overlap candidate pairs,
    /// and resolves global conflicts and ambiguities entirely on the MCT thread.
    /// </summary>
    public class GeometryMatchingService
    {
        private readonly IMatchingStrategy _matchingStrategy;

        public GeometryMatchingService(IMatchingStrategy? matchingStrategy = null)
        {
            _matchingStrategy = matchingStrategy ?? new OverlapPercentageMatchingStrategy();
        }

        public async Task<(List<MatchResult> Results, TransferSummary Summary)> PerformMatchingAsync(
            FeatureLayer sourceLayer,
            FeatureLayer targetLayer,
            TransferSettings settings,
            CancellationToken cancellationToken = default)
        {
            return await QueuedTask.Run(() =>
            {
                string srcName = sourceLayer?.Name ?? "Source";
                string tgtName = targetLayer?.Name ?? "Target";
                Logger.Info($"Starting Geometry Matching Phase: Source='{srcName}', Target='{tgtName}', IgnoreThreshold={settings.IgnoreThreshold}, Threshold={settings.OverlapThreshold}%, AmbiguityTol={settings.AmbiguityTolerance}%");

                if (sourceLayer == null || targetLayer == null)
                {
                    throw new InvalidOperationException("Source or Target layer is null.");
                }

                var sourceSr = sourceLayer.GetSpatialReference();
                var targetSr = targetLayer.GetSpatialReference();

                using var sourceSelection = sourceLayer.GetSelection();
                using var targetSelection = targetLayer.GetSelection();

                var sourceOids = sourceSelection.GetObjectIDs().ToList();
                var targetOids = targetSelection.GetObjectIDs().ToList();

                Logger.Info($"Selected counts: Source={sourceOids.Count}, Target={targetOids.Count}");

                // Determine common projected spatial reference
                var commonSr = GeometryHelper.GetCommonProjectedSpatialReference(sourceSr, targetSr);

                var sourceGeometries = new Dictionary<long, Polygon>();
                var targetGeometries = new Dictionary<long, Polygon>();
                var failedSourceOids = new HashSet<long>();

                // Read and prepare Source Geometries
                using (var srcTable = sourceLayer.GetTable())
                {
                    if (srcTable != null && sourceOids.Count > 0)
                    {
                        var qf = new QueryFilter { ObjectIDs = sourceOids };
                        using var cursor = srcTable.Search(qf);
                        while (cursor.MoveNext())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using var feature = (Feature)cursor.Current;
                            long oid = feature.GetObjectID();
                            var shape = feature.GetShape();

                            var poly = GeometryHelper.ValidateAndPreparePolygon(shape, commonSr, sourceSr);
                            if (poly != null && !poly.IsEmpty)
                            {
                                sourceGeometries[oid] = poly;
                            }
                            else
                            {
                                Logger.Warn($"Source feature OID {oid} has invalid or empty geometry.");
                                failedSourceOids.Add(oid);
                            }
                        }
                    }
                }

                // Read and prepare Target Geometries
                using (var tgtTable = targetLayer.GetTable())
                {
                    if (tgtTable != null && targetOids.Count > 0)
                    {
                        var qf = new QueryFilter { ObjectIDs = targetOids };
                        using var cursor = tgtTable.Search(qf);
                        while (cursor.MoveNext())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using var feature = (Feature)cursor.Current;
                            long oid = feature.GetObjectID();
                            var shape = feature.GetShape();

                            var poly = GeometryHelper.ValidateAndPreparePolygon(shape, commonSr, targetSr);
                            if (poly != null && !poly.IsEmpty)
                            {
                                targetGeometries[oid] = poly;
                            }
                            else
                            {
                                Logger.Warn($"Target feature OID {oid} has invalid or empty geometry.");
                            }
                        }
                    }
                }

                // Evaluate candidate pairs synchronously on MCT
                double evalThreshold = settings.IgnoreThreshold ? 0.0 : settings.OverlapThreshold;
                var candidates = _matchingStrategy.EvaluateCandidates(
                    sourceGeometries,
                    targetGeometries,
                    evalThreshold,
                    cancellationToken);

                Logger.Info($"Evaluated {candidates.Count} overlapping candidate pairs.");

                // Resolve global conflicts and ambiguity
                var results = ConflictResolutionService.ResolveMatches(
                    sourceOids,
                    targetOids,
                    candidates,
                    settings.OverlapThreshold,
                    settings.AmbiguityTolerance,
                    failedSourceOids,
                    settings.IgnoreThreshold);

                // Aggregate summary
                var summary = new TransferSummary
                {
                    TotalSourceFeatures = sourceOids.Count,
                    TotalTargetFeatures = targetOids.Count,
                    MatchedCount = results.Count(r => r.Status == MatchStatus.Transferred),
                    BelowThresholdCount = results.Count(r => r.Status == MatchStatus.BelowThreshold),
                    AmbiguousCount = results.Count(r => r.Status == MatchStatus.Ambiguous),
                    TargetAlreadyMatchedCount = results.Count(r => r.Status == MatchStatus.TargetAlreadyMatched),
                    FailedCount = results.Count(r => r.Status == MatchStatus.Failed)
                };

                Logger.Info($"Matching Phase Complete. {summary}");
                return (results, summary);
            });
        }
    }
}
