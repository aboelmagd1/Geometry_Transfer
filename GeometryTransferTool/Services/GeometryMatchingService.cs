using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Pure, read-only matching service executing the Matching Phase (§5–§8, §34).
    /// Extracts selected geometries, applies spatial reprojections, evaluates overlap candidate pairs,
    /// converts Polyline drawing layers to Polygons (via Feature to Polygon GP tool or in-memory SDK assembly),
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
            string srcName = sourceLayer?.Name ?? "Source";
            string tgtName = targetLayer?.Name ?? "Target";
            Logger.Info($"Starting Geometry Matching Phase: Source='{srcName}', Target='{tgtName}', IgnoreThreshold={settings.IgnoreThreshold}, Threshold={settings.OverlapThreshold}%, AmbiguityTol={settings.AmbiguityTolerance}%");

            if (sourceLayer == null || targetLayer == null)
            {
                throw new InvalidOperationException("Source or Target layer is null.");
            }

            // 1. Inspect source & target selection metadata on MCT
            var (sourceSr, targetSr, sourceOids, targetOids, isPolylineSource) = await QueuedTask.Run(() =>
            {
                var srcSr = sourceLayer.GetSpatialReference();
                var tgtSr = targetLayer.GetSpatialReference();

                using var srcSel = sourceLayer.GetSelection();
                using var tgtSel = targetLayer.GetSelection();

                var sOids = srcSel.GetObjectIDs().ToList();
                var tOids = tgtSel.GetObjectIDs().ToList();

                bool isPoly = sourceLayer.ShapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolyline;
                return (srcSr, tgtSr, sOids, tOids, isPoly);
            });

            Logger.Info($"Selected counts: Source={sourceOids.Count}, Target={targetOids.Count}, IsPolylineSource={isPolylineSource}");

            var commonSr = GeometryHelper.GetCommonProjectedSpatialReference(sourceSr, targetSr);
            var gpConvertedPolygons = new List<Polygon>();

            // 2. If source layer is Polyline, attempt conversion via FeatureToPolygon Geoprocessing Tool (§34)
            if (isPolylineSource && sourceOids.Count > 0)
            {
                string? gdbPath = Project.Current?.DefaultGeodatabasePath;
                if (!string.IsNullOrEmpty(gdbPath))
                {
                    string tempFcName = $"TempLineToPoly_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    string tempFcPath = System.IO.Path.Combine(gdbPath, tempFcName);

                    try
                    {
                        Logger.Info($"Attempting 'management.FeatureToPolygon' geoprocessing tool on '{sourceLayer.Name}' (count={sourceOids.Count}) -> '{tempFcPath}'...");
                        var gpArgs = Geoprocessing.MakeValueArray(sourceLayer, tempFcPath, "", "NO_ATTRIBUTES");
                        var gpResult = await Geoprocessing.ExecuteToolAsync("management.FeatureToPolygon", gpArgs, null, cancellationToken, null, GPExecuteToolFlags.None);

                        if (!gpResult.IsFailed)
                        {
                            await QueuedTask.Run(() =>
                            {
                                try
                                {
                                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                                    using var fc = gdb.OpenDataset<FeatureClass>(tempFcName);
                                    using var cursor = fc.Search();
                                    while (cursor.MoveNext())
                                    {
                                        using var feat = (Feature)cursor.Current;
                                        if (feat.GetShape() is Polygon poly && !poly.IsEmpty)
                                        {
                                            var prepPoly = GeometryHelper.ValidateAndPreparePolygon(poly, commonSr);
                                            if (prepPoly != null && !prepPoly.IsEmpty && Math.Abs(prepPoly.Area) > 1e-4)
                                            {
                                                gpConvertedPolygons.Add(prepPoly);
                                            }
                                        }
                                    }
                                    Logger.Info($"FeatureToPolygon GP tool generated {gpConvertedPolygons.Count} valid polygon(s).");
                                }
                                catch (Exception readEx)
                                {
                                    Logger.Warn($"Failed to read polygons from GP output '{tempFcPath}': {readEx.Message}");
                                }
                            });
                        }
                        else
                        {
                            string err = string.Join("; ", gpResult.ErrorMessages.Select(m => m.Text));
                            Logger.Warn($"FeatureToPolygon GP tool returned failed: {err}. Proceeding with SDK segment assembly fallback.");
                        }
                    }
                    catch (Exception gpEx)
                    {
                        Logger.Warn($"FeatureToPolygon GP invocation exception: {gpEx.Message}. Proceeding with SDK segment assembly fallback.");
                    }
                    finally
                    {
                        // Clean up temporary feature class silently without map events
                        try
                        {
                            await Geoprocessing.ExecuteToolAsync("management.Delete", Geoprocessing.MakeValueArray(tempFcPath), null, default, null, GPExecuteToolFlags.None);
                        }
                        catch { }
                    }
                }
            }

            // 3. Complete matching, candidate evaluation, and conflict resolution on QueuedTask
            return await QueuedTask.Run(() =>
            {
                var sourceGeometries = new Dictionary<long, Polygon>();
                var targetGeometries = new Dictionary<long, Polygon>();
                var failedSourceOids = new HashSet<long>();
                var sourceFailureDetails = new Dictionary<long, string>();

                string sourceGeometryType = isPolylineSource ? "Polyline" : "Polygon";

                if (isPolylineSource)
                {
                    // Read source polyline geometries
                    var sourceLineShapes = new Dictionary<long, Polyline>();
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

                                if (shape is Polyline pl && !pl.IsEmpty)
                                {
                                    sourceLineShapes[oid] = pl;
                                }
                                else
                                {
                                    failedSourceOids.Add(oid);
                                    sourceFailureDetails[oid] = "Source polyline geometry is empty or null.";
                                }
                            }
                        }
                    }

                    var finalConvertedPolygons = new List<Polygon>(gpConvertedPolygons);

                    // Fallback to in-memory SDK assembly if GP tool returned 0 polygons
                    if (finalConvertedPolygons.Count == 0 && sourceLineShapes.Count > 0)
                    {
                        Logger.Info("FeatureToPolygon GP produced 0 polygons. Running in-memory SDK segment assembly...");
                        var assembled = GeometryHelper.AssemblePolylinesToPolygons(sourceLineShapes.Values, commonSr);
                        finalConvertedPolygons.AddRange(assembled);
                        Logger.Info($"SDK segment assembly produced {assembled.Count} valid polygon(s).");
                    }

                    // Map converted polygons to source lines
                    if (finalConvertedPolygons.Count > 0)
                    {
                        var usedOids = new HashSet<long>();
                        for (int i = 0; i < finalConvertedPolygons.Count; i++)
                        {
                            var poly = finalConvertedPolygons[i];
                            var contributingOids = new List<long>();

                            foreach (var (lineOid, lineShape) in sourceLineShapes)
                            {
                                try
                                {
                                    if (GeometryEngine.Instance.Intersects(poly, lineShape) ||
                                        GeometryEngine.Instance.Touches(poly, lineShape))
                                    {
                                        contributingOids.Add(lineOid);
                                    }
                                }
                                catch { }
                            }

                            // Pick an unused contributing OID as primary ID
                            long primaryOid = contributingOids.FirstOrDefault(oid => !usedOids.Contains(oid));
                            if (primaryOid == 0)
                            {
                                primaryOid = contributingOids.FirstOrDefault();
                            }
                            if (primaryOid == 0)
                            {
                                primaryOid = sourceOids.ElementAtOrDefault(i);
                            }
                            if (primaryOid == 0)
                            {
                                primaryOid = i + 1;
                            }

                            usedOids.Add(primaryOid);
                            sourceGeometries[primaryOid] = poly;

                            string details = contributingOids.Count > 1
                                ? $"Converted from {contributingOids.Count} lines [{string.Join(", ", contributingOids)}] via Feature to Polygon"
                                : "Converted from closed line via Feature to Polygon";
                            sourceFailureDetails[primaryOid] = details;
                        }

                        // Mark any line OIDs that did not contribute to any converted polygon
                        foreach (var oid in sourceOids)
                        {
                            if (!sourceGeometries.ContainsKey(oid) && !usedOids.Contains(oid))
                            {
                                failedSourceOids.Add(oid);
                                sourceFailureDetails[oid] = "Line does not form a closed boundary with other selected lines.";
                            }
                        }
                    }

                    // §34 Validation Rule: Reject only if all selected lines fail conversion
                    if (sourceGeometries.Count == 0 && sourceOids.Count > 0)
                    {
                        string rejectMsg = "None of the selected line features could be converted into valid polygons. Please ensure the selected lines form closed boundaries.";
                        Logger.Error(rejectMsg);
                        throw new InvalidOperationException(rejectMsg);
                    }
                }
                else
                {
                    // Standard Polygon Source Layer reading
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
                                    Logger.Warn($"Source polygon feature OID {oid} has invalid or empty geometry.");
                                    failedSourceOids.Add(oid);
                                    sourceFailureDetails[oid] = "Source polygon has invalid geometry and could not be simplified.";
                                }
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

                // For polyline source, only consider successfully converted polygons
                var effectiveSourceOids = isPolylineSource
                    ? sourceGeometries.Keys.Distinct().ToList()
                    : sourceOids;

                // Resolve global conflicts and ambiguity, attaching working geometries and conversion status
                var results = ConflictResolutionService.ResolveMatches(
                    effectiveSourceOids,
                    targetOids,
                    candidates,
                    settings.OverlapThreshold,
                    settings.AmbiguityTolerance,
                    failedSourceOids,
                    settings.IgnoreThreshold,
                    null,
                    sourceGeometryType,
                    sourceGeometries,
                    sourceFailureDetails);

                // Delete/exclude features that have Failed or InvalidGeometry status (§User Request)
                results.RemoveAll(r => r.MatchStatus == MatchStatus.Failed || 
                                       r.MatchStatus == MatchStatus.InvalidGeometry || 
                                       r.ConversionStatus == "Failed" ||
                                       r.TransferStatus == TransferStatus.Failed);

                // Aggregate summary
                var summary = new TransferSummary
                {
                    TotalSourceFeatures = results.Count,
                    TotalTargetFeatures = targetOids.Count,
                    MatchedCount = results.Count(r => r.MatchStatus == MatchStatus.Matched),
                    BelowThresholdCount = results.Count(r => r.MatchStatus == MatchStatus.BelowThreshold),
                    AmbiguousCount = results.Count(r => r.MatchStatus == MatchStatus.Ambiguous),
                    TargetAlreadyMatchedCount = results.Count(r => r.MatchStatus == MatchStatus.TargetAlreadyMatched),
                    NoIntersectionCount = results.Count(r => r.MatchStatus == MatchStatus.NoIntersection),
                    FailedCount = 0
                };

                Logger.Info($"Matching Phase Complete. {summary}");
                return (results, summary);
            });
        }
    }
}

