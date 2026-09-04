using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Executes the Modification Phase (§9, §15, §18).
    /// Performs atomic, undoable geometry and optional attribute transfers within a single EditOperation.
    /// </summary>
    public class GeometryTransferService
    {
        public async Task<int> TransferGeometriesAsync(
            FeatureLayer sourceLayer,
            FeatureLayer targetLayer,
            IReadOnlyList<MatchResult> matchResults,
            TransferSettings settings)
        {
            var validMatches = matchResults.Where(r => r.CanTransfer &&
                                                       r.MatchStatus != MatchStatus.Failed &&
                                                       r.MatchStatus != MatchStatus.InvalidGeometry &&
                                                       r.ConversionStatus != "Failed").ToList();
            if (validMatches.Count == 0)
            {
                Logger.Warn("No confirmed matches to transfer.");
                return 0;
            }

            Logger.Info($"Starting Modification Phase for {validMatches.Count} confirmed feature matches.");

            return await QueuedTask.Run(() =>
            {
                var sourceSr = sourceLayer.GetSpatialReference();
                var targetSr = targetLayer.GetSpatialReference() ?? sourceSr ?? SpatialReferences.WebMercator;
                var sourceOids = validMatches.Select(m => m.SourceOid).Distinct().ToList();

                // Read source geometries and attributes into memory
                var sourceShapes = new Dictionary<long, Geometry>();
                var sourceAttributes = new Dictionary<long, Dictionary<string, object>>();

                // Reuse cached working polygon geometries from matching phase (§34 performance rule)
                foreach (var match in validMatches)
                {
                    if (match.WorkingPolygon != null && !match.WorkingPolygon.IsEmpty)
                    {
                        var poly = match.WorkingPolygon;
                        if (targetSr != null && poly.SpatialReference != null && !SpatialReference.AreEqual(poly.SpatialReference, targetSr))
                        {
                            try
                            {
                                poly = GeometryEngine.Instance.Project(poly, targetSr) as Polygon ?? poly;
                            }
                            catch (Exception prjEx)
                            {
                                Logger.Warn($"Failed to project cached working polygon to target SR: {prjEx.Message}");
                            }
                        }
                        sourceShapes[match.SourceOid] = poly;
                    }
                }

                // If attributes need to be mapped OR any shapes were not in cache, query the source table
                bool needQuerySource = (settings.AttributeMappingEnabled && settings.AttributeMappings.Count > 0) ||
                                       sourceShapes.Count < sourceOids.Count;

                if (needQuerySource)
                {
                    using (var srcTable = sourceLayer.GetTable())
                    {
                        if (srcTable != null && sourceOids.Count > 0)
                        {
                            var qf = new QueryFilter { ObjectIDs = sourceOids };
                            using var cursor = srcTable.Search(qf);
                            while (cursor.MoveNext())
                            {
                                using var feature = (Feature)cursor.Current;
                                long oid = feature.GetObjectID();

                                if (!sourceShapes.ContainsKey(oid))
                                {
                                    var shape = feature.GetShape();
                                    var preparedPoly = GeometryHelper.ValidateAndPreparePolygon(shape, targetSr!, sourceSr);
                                    if (preparedPoly != null && !preparedPoly.IsEmpty)
                                    {
                                        sourceShapes[oid] = preparedPoly;
                                    }
                                }

                                // Read mapped source attributes if attribute mapping is active
                                if (settings.AttributeMappingEnabled && settings.AttributeMappings.Count > 0)
                                {
                                    var rowMap = new Dictionary<string, object>();
                                    foreach (var map in settings.AttributeMappings.Where(m => m.IsEnabled && !string.IsNullOrWhiteSpace(m.SourceField) && !string.IsNullOrWhiteSpace(m.TargetField)))
                                    {
                                        try
                                        {
                                            int fldIdx = feature.FindField(map.SourceField);
                                            if (fldIdx >= 0)
                                            {
                                                object val = feature[fldIdx];
                                                rowMap[map.SourceField] = val;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Logger.Warn($"Failed to read field '{map.SourceField}' for source OID {oid}: {ex.Message}");
                                        }
                                    }
                                    sourceAttributes[oid] = rowMap;
                                }
                            }
                        }
                    }
                }

                // Create atomic EditOperation
                var editOp = new EditOperation
                {
                    Name = "Transfer Polygon Geometry",
                    ProgressMessage = "Transferring polygon geometries...",
                    ShowProgressor = true
                };

                var transferredTargetOids = new List<long>();

                foreach (var match in validMatches)
                {
                    if (!match.TargetOid.HasValue) continue;
                    long tgtOid = match.TargetOid.Value;

                    if (!sourceShapes.TryGetValue(match.SourceOid, out var newShape) || newShape == null || newShape.IsEmpty)
                    {
                        Logger.Warn($"Source geometry not found for OID {match.SourceOid}. Skipping.");
                        continue;
                    }

                    // Prepare mapped attributes if enabled
                    Dictionary<string, object>? attrDict = null;
                    if (settings.AttributeMappingEnabled && sourceAttributes.TryGetValue(match.SourceOid, out var srcRowVals))
                    {
                        attrDict = new Dictionary<string, object>();
                        foreach (var map in settings.AttributeMappings.Where(m => m.IsEnabled && !string.IsNullOrWhiteSpace(m.SourceField) && !string.IsNullOrWhiteSpace(m.TargetField)))
                        {
                            if (srcRowVals.TryGetValue(map.SourceField, out var fieldVal))
                            {
                                attrDict[map.TargetField] = fieldVal;
                            }
                        }
                    }

                    // Execute Modify
                    if (attrDict != null && attrDict.Count > 0)
                    {
                        editOp.Modify(targetLayer, tgtOid, newShape, attrDict);
                    }
                    else
                    {
                        editOp.Modify(targetLayer, tgtOid, newShape);
                    }

                    transferredTargetOids.Add(tgtOid);
                }

                if (transferredTargetOids.Count == 0)
                {
                    Logger.Warn("No valid features to submit to EditOperation.");
                    return 0;
                }

                // Execute the EditOperation
                bool opSuccess = editOp.Execute();
                if (!opSuccess)
                {
                    string errorMsg = editOp.ErrorMessage ?? "Unknown EditOperation failure.";
                    Logger.Error($"EditOperation failed: {errorMsg}");

                    foreach (var match in validMatches)
                    {
                        match.TransferStatus = TransferStatus.Failed;
                        match.Details = $"Transfer failed: {errorMsg}";
                    }

                    throw new InvalidOperationException($"Geometry Transfer failed during execution: {errorMsg}");
                }

                // Update transfer statuses accurately (§13, §38)
                foreach (var match in matchResults)
                {
                    if (match.TargetOid.HasValue && transferredTargetOids.Contains(match.TargetOid.Value))
                    {
                        match.TransferStatus = TransferStatus.Success;
                        match.Details = $"Transferred successfully at {DateTime.Now:HH:mm:ss}.";
                    }
                    else if (match.MatchStatus == MatchStatus.Matched)
                    {
                        match.TransferStatus = TransferStatus.Failed;
                        match.Details = "Transfer skipped or geometry could not be prepared.";
                    }
                    else
                    {
                        match.TransferStatus = TransferStatus.Skipped;
                    }
                }

                Logger.Info($"Successfully transferred geometry for {transferredTargetOids.Count} target features.");

                // Visual Feedback: Select transferred target features on the map
                try
                {
                    targetLayer.Select(new QueryFilter { ObjectIDs = transferredTargetOids }, SelectionCombinationMethod.New);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Failed to update map selection: {ex.Message}");
                }

                return transferredTargetOids.Count;
            });
        }
    }
}
