using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Service responsible for generating generic, schema-independent Results Tables
    /// and Results Feature Classes in a Geodatabase (§7–§20, §25, §26).
    /// </summary>
    public class TransferResultsTableService
    {
        public class TableCreationResult
        {
            public bool Success { get; set; }
            public string TableName { get; set; } = string.Empty;
            public string TablePath { get; set; } = string.Empty;
            public string? AttributeTableName { get; set; }
            public int RowCount { get; set; }
            public bool AddedToMap { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        public class FeatureClassCreationResult
        {
            public bool Success { get; set; }
            public string? DatasetPath { get; set; }
            public string? DatasetName { get; set; }
            public string? LayerUri { get; set; }
            public int FeatureCount { get; set; }
            public bool AddedToMap { get; set; }
            public string? ErrorMessage { get; set; }
        }

        public async Task<TableCreationResult> CreateAndPopulateResultsTableAsync(
            IReadOnlyList<MatchResult> matchResults,
            FeatureLayer? sourceLayer,
            FeatureLayer? targetLayer,
            TransferSettings settings)
        {
            if (matchResults == null || matchResults.Count == 0)
            {
                return new TableCreationResult
                {
                    Success = false,
                    Message = "No match results available to create a results table."
                };
            }

            // 1. Resolve Output Geodatabase Path (§25)
            string gdbPath = await ResolveGeodatabasePathAsync(targetLayer, settings);
            if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath))
            {
                return new TableCreationResult
                {
                    Success = false,
                    Message = $"Invalid or inaccessible Geodatabase path: {gdbPath}"
                };
            }

            Logger.Info($"Creating Results Table in Geodatabase: {gdbPath}");

            // 2. Determine Unique Table Names (§10, §27) - checks both Table and Feature Class definitions
            string baseTableName = "GeometryTransfer_Results";
            string tableName = await DetermineUniqueDatasetNameAsync(gdbPath, baseTableName);

            string? attrTableName = null;
            if (settings.IncludeAttributeSnapshot && settings.SelectedSnapshotFields.Count > 0)
            {
                string baseAttrTableName = "Transfer_Result_Attributes";
                attrTableName = await DetermineUniqueDatasetNameAsync(gdbPath, baseAttrTableName);
            }

            try
            {
                // 3. Create Main Results Table via Geoprocessing
                var createParams = Geoprocessing.MakeValueArray(gdbPath, tableName);
                var gpResult = await Geoprocessing.ExecuteToolAsync("management.CreateTable", createParams);
                if (gpResult.IsFailed)
                {
                    string err = string.Join("; ", gpResult.ErrorMessages.Select(e => e.Text));
                    throw new InvalidOperationException($"Failed to create table '{tableName}': {err}");
                }

                string tableFullPath = Path.Combine(gdbPath, tableName);

                // Add Core Audit Fields (§15, §19, §34) - No hard-coded business fields
                await AddFieldAsync(tableFullPath, "Match_ID", "TEXT", 50);
                await AddFieldAsync(tableFullPath, "Run_ID", "TEXT", 50);
                await AddFieldAsync(tableFullPath, "Source_OID", "LONG");
                await AddFieldAsync(tableFullPath, "Target_OID", "LONG");
                await AddFieldAsync(tableFullPath, "Match_Status", "TEXT", 50);
                await AddFieldAsync(tableFullPath, "Transfer_Status", "TEXT", 50);
                await AddFieldAsync(tableFullPath, "Match_Method", "TEXT", 50);
                await AddFieldAsync(tableFullPath, "Overlap_Pct", "DOUBLE");
                await AddFieldAsync(tableFullPath, "Threshold_Pct", "DOUBLE");
                await AddFieldAsync(tableFullPath, "Candidate_Count", "LONG");
                await AddFieldAsync(tableFullPath, "Second_Best_Pct", "DOUBLE");
                await AddFieldAsync(tableFullPath, "Source_Geometry_Type", "TEXT", 20);
                await AddFieldAsync(tableFullPath, "Conversion_Status", "TEXT", 20);
                await AddFieldAsync(tableFullPath, "Details", "TEXT", 255);
                await AddFieldAsync(tableFullPath, "Run_Date", "DATE");

                // 4. Create Optional Attribute Snapshot Table if requested (§20)
                string? attrTableFullPath = null;
                if (attrTableName != null)
                {
                    var createAttrParams = Geoprocessing.MakeValueArray(gdbPath, attrTableName);
                    var gpAttrResult = await Geoprocessing.ExecuteToolAsync("management.CreateTable", createAttrParams);
                    if (!gpAttrResult.IsFailed)
                    {
                        attrTableFullPath = Path.Combine(gdbPath, attrTableName);
                        await AddFieldAsync(attrTableFullPath, "Match_ID", "TEXT", 50);
                        await AddFieldAsync(attrTableFullPath, "Source_OID", "LONG");
                        await AddFieldAsync(attrTableFullPath, "Target_OID", "LONG");
                        await AddFieldAsync(attrTableFullPath, "Field_Name", "TEXT", 50);
                        await AddFieldAsync(attrTableFullPath, "Source_Value", "TEXT", 255);
                        await AddFieldAsync(attrTableFullPath, "Target_Value", "TEXT", 255);
                    }
                }

                // 5. Read source and target dynamic attributes if attribute snapshot is enabled
                var sourceAttrMap = new Dictionary<long, Dictionary<string, object>>();
                var targetAttrMap = new Dictionary<long, Dictionary<string, object>>();

                if (attrTableFullPath != null && sourceLayer != null)
                {
                    await QueuedTask.Run(() =>
                    {
                        var srcOids = matchResults.Select(r => r.SourceOid).Distinct().ToList();
                        using (var srcTable = sourceLayer.GetTable())
                        {
                            if (srcTable != null && srcOids.Count > 0)
                            {
                                var qf = new QueryFilter { ObjectIDs = srcOids };
                                using var cursor = srcTable.Search(qf);
                                while (cursor.MoveNext())
                                {
                                    using var feat = (Feature)cursor.Current;
                                    long oid = feat.GetObjectID();
                                    var dict = new Dictionary<string, object>();
                                    foreach (var fld in settings.SelectedSnapshotFields)
                                    {
                                        int idx = feat.FindField(fld);
                                        if (idx >= 0)
                                        {
                                            dict[fld] = feat[idx] ?? DBNull.Value;
                                        }
                                    }
                                    sourceAttrMap[oid] = dict;
                                }
                            }
                        }

                        if (targetLayer != null)
                        {
                            var tgtOids = matchResults.Where(r => r.TargetOid.HasValue).Select(r => r.TargetOid!.Value).Distinct().ToList();
                            using var tgtTable = targetLayer.GetTable();
                            if (tgtTable != null && tgtOids.Count > 0)
                            {
                                var qf = new QueryFilter { ObjectIDs = tgtOids };
                                using var cursor = tgtTable.Search(qf);
                                while (cursor.MoveNext())
                                {
                                    using var feat = (Feature)cursor.Current;
                                    long oid = feat.GetObjectID();
                                    var dict = new Dictionary<string, object>();
                                    foreach (var fld in settings.SelectedSnapshotFields)
                                    {
                                        int idx = feat.FindField(fld);
                                        if (idx >= 0)
                                        {
                                            dict[fld] = feat[idx] ?? DBNull.Value;
                                        }
                                    }
                                    targetAttrMap[oid] = dict;
                                }
                            }
                        }
                    });
                }

                // 6. Insert Rows into Main Results Table and Attribute Table via QueuedTask
                int insertedCount = await QueuedTask.Run(() =>
                {
                    int count = 0;
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var table = gdb.OpenDataset<Table>(tableName);
                    using var insertCursor = table.CreateInsertCursor();
                    var validTableResults = matchResults
                        .Where(r => r.MatchStatus != MatchStatus.Failed && 
                                    r.MatchStatus != MatchStatus.InvalidGeometry && 
                                    r.ConversionStatus != "Failed" &&
                                    r.TransferStatus != TransferStatus.Failed)
                        .ToList();

                    using var rowBuffer = table.CreateRowBuffer();
                    foreach (var result in validTableResults)
                    {
                        rowBuffer["Match_ID"] = result.MatchId;
                        rowBuffer["Run_ID"] = result.RunId;
                        rowBuffer["Source_OID"] = result.SourceOid;
                        rowBuffer["Target_OID"] = result.TargetOid.HasValue ? (object)result.TargetOid.Value : DBNull.Value;
                        rowBuffer["Match_Status"] = result.MatchStatus.ToString();
                        rowBuffer["Transfer_Status"] = result.TransferStatus.ToString();
                        rowBuffer["Match_Method"] = settings.MatchingMethod;
                        rowBuffer["Overlap_Pct"] = result.OverlapPct.HasValue ? (object)result.OverlapPct.Value : DBNull.Value;
                        rowBuffer["Threshold_Pct"] = result.ThresholdPct;
                        rowBuffer["Candidate_Count"] = result.CandidateCount;
                        rowBuffer["Second_Best_Pct"] = result.SecondBestOverlapPct.HasValue ? (object)result.SecondBestOverlapPct.Value : DBNull.Value;
                        rowBuffer["Source_Geometry_Type"] = string.IsNullOrEmpty(result.SourceGeometryType) ? "Polygon" : result.SourceGeometryType;
                        rowBuffer["Conversion_Status"] = string.IsNullOrEmpty(result.ConversionStatus) ? "None" : result.ConversionStatus;
                        rowBuffer["Details"] = result.Details ?? string.Empty;
                        rowBuffer["Run_Date"] = result.RunDate;

                        insertCursor.Insert(rowBuffer);
                        count++;
                    }

                    insertCursor.Flush();

                    // Insert into Attribute Snapshot Table if created
                    if (attrTableName != null)
                    {
                        try
                        {
                            using var attrTable = gdb.OpenDataset<Table>(attrTableName);
                            using var attrCursor = attrTable.CreateInsertCursor();
                            using var attrRowBuffer = attrTable.CreateRowBuffer();

                            foreach (var result in matchResults)
                            {
                                if (sourceAttrMap.TryGetValue(result.SourceOid, out var srcFields))
                                {
                                    Dictionary<string, object>? tgtFields = null;
                                    if (result.TargetOid.HasValue)
                                    {
                                        targetAttrMap.TryGetValue(result.TargetOid.Value, out tgtFields);
                                    }

                                    foreach (var fld in settings.SelectedSnapshotFields)
                                    {
                                        srcFields.TryGetValue(fld, out var srcVal);
                                        object? tgtVal = null;
                                        tgtFields?.TryGetValue(fld, out tgtVal);

                                        attrRowBuffer["Match_ID"] = result.MatchId;
                                        attrRowBuffer["Source_OID"] = result.SourceOid;
                                        attrRowBuffer["Target_OID"] = result.TargetOid.HasValue ? (object)result.TargetOid.Value : DBNull.Value;
                                        attrRowBuffer["Field_Name"] = fld;
                                        attrRowBuffer["Source_Value"] = srcVal?.ToString() ?? string.Empty;
                                        attrRowBuffer["Target_Value"] = tgtVal?.ToString() ?? string.Empty;

                                        attrCursor.Insert(attrRowBuffer);
                                    }
                                }
                            }

                            attrCursor.Flush();
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to populate attribute snapshot rows: {ex.Message}");
                        }
                    }

                    return count;
                });

                // 7. Layered Duplicate Detection & Safe Map Registration (§8, §9)
                bool addedToMap = await AddResultsTableToMapAsync(tableFullPath, tableName);
                if (attrTableFullPath != null && attrTableName != null)
                {
                    await AddResultsTableToMapAsync(attrTableFullPath, attrTableName);
                }

                string msg = $"Created Results Table '{tableName}' ({insertedCount} records) in {Path.GetFileName(gdbPath)}.";
                if (attrTableName != null)
                {
                    msg += $" Dynamic attribute table '{attrTableName}' also created.";
                }
                if (addedToMap)
                {
                    msg += " Registered in active Map Standalone Tables (single entry).";
                }

                Logger.Info(msg);

                return new TableCreationResult
                {
                    Success = true,
                    TableName = tableName,
                    TablePath = tableFullPath,
                    AttributeTableName = attrTableName,
                    RowCount = insertedCount,
                    AddedToMap = addedToMap,
                    Message = msg
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create or populate Results Table: {ex.Message}", ex);
                return new TableCreationResult
                {
                    Success = false,
                    Message = $"Error creating Results Table: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Creates and populates a polygon Results Feature Class in the Geodatabase (§11–§18, §24).
        /// Reuses existing MatchResult collection, normalizes geometries, and adds only generic audit fields.
        /// </summary>
        public async Task<FeatureClassCreationResult> CreateAndPopulateResultsFeatureClassAsync(
            IReadOnlyList<MatchResult> matchResults,
            FeatureLayer? sourceLayer,
            FeatureLayer? targetLayer,
            TransferSettings settings)
        {
            if (matchResults == null || matchResults.Count == 0)
            {
                return new FeatureClassCreationResult
                {
                    Success = false,
                    ErrorMessage = "No matching results are available. Run Preview first."
                };
            }

            if (sourceLayer == null)
            {
                return new FeatureClassCreationResult
                {
                    Success = false,
                    ErrorMessage = "Source Layer is required to extract polygon geometries for the Results Feature Class."
                };
            }

            // 1. Resolve Output Geodatabase Path
            string gdbPath = await ResolveGeodatabasePathAsync(targetLayer, settings);
            if (string.IsNullOrWhiteSpace(gdbPath) || !Directory.Exists(gdbPath))
            {
                return new FeatureClassCreationResult
                {
                    Success = false,
                    ErrorMessage = $"Invalid or inaccessible output Geodatabase path: {gdbPath}"
                };
            }

            // 2. Determine Unique Feature Class Name (§10)
            string baseFcName = "GeometryTransfer_Results_FC";
            string fcName = await DetermineUniqueDatasetNameAsync(gdbPath, baseFcName);
            string fcFullPath = Path.Combine(gdbPath, fcName);

            Logger.Info($"Creating Results Feature Class: '{fcName}' in {gdbPath}");

            try
            {
                // Resolve spatial references from layers on MCT
                var (sourceSr, targetSr) = await QueuedTask.Run(() =>
                {
                    var sSr = sourceLayer.GetSpatialReference();
                    var tSr = targetLayer?.GetSpatialReference();
                    return (sSr, tSr);
                });

                var effectiveSr = targetSr ?? sourceSr;
                string spatialRefArg = (effectiveSr != null && effectiveSr.Wkid > 0)
                    ? effectiveSr.Wkid.ToString()
                    : (targetLayer?.Name ?? sourceLayer.Name);

                // 3. Create Feature Class via Geoprocessing
                var createFcParams = Geoprocessing.MakeValueArray(
                    gdbPath,
                    fcName,
                    "POLYGON",
                    "",
                    "DISABLED",
                    "DISABLED",
                    spatialRefArg
                );

                var gpResult = await Geoprocessing.ExecuteToolAsync("management.CreateFeatureclass", createFcParams);
                if (gpResult.IsFailed)
                {
                    string err = string.Join("; ", gpResult.ErrorMessages.Select(e => e.Text));
                    throw new InvalidOperationException($"Failed to create feature class '{fcName}': {err}");
                }

                // 4. Add Core Generic Audit Fields (§15, §34) - Never schema-specific
                await AddFieldAsync(fcFullPath, "Match_ID", "TEXT", 50);
                await AddFieldAsync(fcFullPath, "Run_ID", "TEXT", 50);
                await AddFieldAsync(fcFullPath, "Source_OID", "LONG");
                await AddFieldAsync(fcFullPath, "Target_OID", "LONG");
                await AddFieldAsync(fcFullPath, "Match_Status", "TEXT", 50);
                await AddFieldAsync(fcFullPath, "Transfer_Status", "TEXT", 50);
                await AddFieldAsync(fcFullPath, "Match_Method", "TEXT", 50);
                await AddFieldAsync(fcFullPath, "Overlap_Pct", "DOUBLE");
                await AddFieldAsync(fcFullPath, "Threshold_Pct", "DOUBLE");
                await AddFieldAsync(fcFullPath, "Candidate_Count", "LONG");
                await AddFieldAsync(fcFullPath, "Second_Best_Pct", "DOUBLE");
                await AddFieldAsync(fcFullPath, "Source_Geometry_Type", "TEXT", 20);
                await AddFieldAsync(fcFullPath, "Conversion_Status", "TEXT", 20);
                await AddFieldAsync(fcFullPath, "Details", "TEXT", 255);
                await AddFieldAsync(fcFullPath, "Run_Date", "DATE");

                // 5. Populate Features and Geometries (§12, §13, §14, §16, §34)
                // Strictly exclude any Failed or InvalidGeometry features (§User Request)
                var resultsToInsert = matchResults
                    .Where(r => r.MatchStatus != MatchStatus.Failed && 
                                r.MatchStatus != MatchStatus.InvalidGeometry && 
                                r.ConversionStatus != "Failed" &&
                                r.TransferStatus != TransferStatus.Failed)
                    .ToList();

                int insertedCount = await QueuedTask.Run(() =>
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    using var fc = gdb.OpenDataset<FeatureClass>(fcName);
                    var fcDef = fc.GetDefinition();
                    string shapeFieldName = fcDef.GetShapeField();
                    var fcSr = fcDef.GetSpatialReference();

                    // Read source polygon geometries reusing matchResults
                    var srcOids = resultsToInsert.Select(r => r.SourceOid).Distinct().ToList();
                    var shapeMap = new Dictionary<long, Polygon>();

                    // 0. High-performance cache reuse (§34): use WorkingPolygon if available
                    foreach (var res in resultsToInsert)
                    {
                        if (res.WorkingPolygon != null && !res.WorkingPolygon.IsEmpty)
                        {
                            var cleanPoly = GeometryHelper.ValidateAndPreparePolygon(res.WorkingPolygon, fcSr, res.WorkingPolygon.SpatialReference);
                            if (cleanPoly != null && !cleanPoly.IsEmpty)
                            {
                                cleanPoly = EnsureZandMConsistency(cleanPoly, fcDef);
                                shapeMap[res.SourceOid] = cleanPoly;
                            }
                        }
                    }

                    // 1. Direct Search on source FeatureLayer for any missing geometries (e.g. if not cached)
                    if (shapeMap.Count < srcOids.Count)
                    {
                        try
                        {
                            var missingOids = srcOids.Where(o => !shapeMap.ContainsKey(o)).ToList();
                            var qf = new QueryFilter { ObjectIDs = missingOids };
                            using var cursor = sourceLayer.Search(qf);
                            while (cursor.MoveNext())
                            {
                                using var feat = (Feature)cursor.Current;
                                long oid = feat.GetObjectID();
                                var shape = feat.GetShape();
                                if (shape != null && !shape.IsEmpty)
                                {
                                    var cleanPoly = GeometryHelper.ValidateAndPreparePolygon(shape, fcSr, sourceSr);
                                    if (cleanPoly != null && !cleanPoly.IsEmpty)
                                    {
                                        cleanPoly = EnsureZandMConsistency(cleanPoly, fcDef);
                                        shapeMap[oid] = cleanPoly;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Direct sourceLayer.Search encountered issue: {ex.Message}");
                        }
                    }
                    // 2. Fallback to sourceLayer.GetTable() if any OIDs were missed
                    if (shapeMap.Count < srcOids.Count)
                    {
                        try
                        {
                            var missingOids = srcOids.Where(o => !shapeMap.ContainsKey(o)).ToList();
                            if (missingOids.Count > 0)
                            {
                                using var srcTable = sourceLayer.GetTable();
                                if (srcTable != null)
                                {
                                    var qf = new QueryFilter { ObjectIDs = missingOids };
                                    using var cursor = srcTable.Search(qf);
                                    while (cursor.MoveNext())
                                    {
                                        using var feat = (Feature)cursor.Current;
                                        long oid = feat.GetObjectID();
                                        if (!shapeMap.ContainsKey(oid))
                                        {
                                            var shape = feat.GetShape();
                                            if (shape != null && !shape.IsEmpty)
                                            {
                                                var cleanPoly = GeometryHelper.ValidateAndPreparePolygon(shape, fcSr, sourceSr);
                                                if (cleanPoly != null && !cleanPoly.IsEmpty)
                                                {
                                                    cleanPoly = EnsureZandMConsistency(cleanPoly, fcDef);
                                                    shapeMap[oid] = cleanPoly;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Layer Table fallback search encountered issue: {ex.Message}");
                        }
                    }

                    int totalCandidates = resultsToInsert.Count;
                    int withGeometryCount = resultsToInsert.Count(r => (r.ResultGeometry != null && !r.ResultGeometry.IsEmpty) || shapeMap.ContainsKey(r.SourceOid));
                    int withoutGeometryCount = totalCandidates - withGeometryCount;

                    Logger.Info($"Results FC Population Pre-check: TotalResults={totalCandidates}, WithGeometry={withGeometryCount}, WithoutGeometry={withoutGeometryCount}, ShapeMapEntries={shapeMap.Count}.");

                    using var insertCursor = fc.CreateInsertCursor();
                    using var rowBuffer = fc.CreateRowBuffer();
                    int count = 0;

                    foreach (var result in resultsToInsert)
                    {
                        if (result.MatchStatus == MatchStatus.Failed || 
                            result.MatchStatus == MatchStatus.InvalidGeometry || 
                            result.ConversionStatus == "Failed" || 
                            result.TransferStatus == TransferStatus.Failed)
                        {
                            continue;
                        }

                        // Geometry is mandatory for a Feature Class record (§34: reuse ResultGeometry / WorkingPolygon)
                        Polygon? polyShape = null;
                        if (result.ResultGeometry is Polygon directPoly && !directPoly.IsEmpty)
                        {
                            polyShape = GeometryHelper.ValidateAndPreparePolygon(directPoly, fcSr, directPoly.SpatialReference);
                            if (polyShape != null) polyShape = EnsureZandMConsistency(polyShape, fcDef);
                        }
                        else if (!shapeMap.TryGetValue(result.SourceOid, out polyShape) || polyShape == null || polyShape.IsEmpty)
                        {
                            Logger.Warn($"Skipping Feature Class row for Match_ID '{result.MatchId}' (Source OID {result.SourceOid}): No valid polygon geometry available (Status={result.MatchStatus}, Conversion={result.ConversionStatus}).");
                            continue;
                        }

                        rowBuffer[shapeFieldName] = polyShape;
                        rowBuffer["Match_ID"] = result.MatchId;
                        rowBuffer["Run_ID"] = result.RunId;
                        rowBuffer["Source_OID"] = result.SourceOid;
                        rowBuffer["Target_OID"] = result.TargetOid.HasValue ? (object)result.TargetOid.Value : DBNull.Value;
                        rowBuffer["Match_Status"] = result.MatchStatus.ToString();
                        rowBuffer["Transfer_Status"] = result.TransferStatus.ToString();
                        rowBuffer["Match_Method"] = settings.MatchingMethod;
                        rowBuffer["Overlap_Pct"] = result.OverlapPct.HasValue ? (object)result.OverlapPct.Value : DBNull.Value;
                        rowBuffer["Threshold_Pct"] = result.ThresholdPct;
                        rowBuffer["Candidate_Count"] = result.CandidateCount;
                        rowBuffer["Second_Best_Pct"] = result.SecondBestOverlapPct.HasValue ? (object)result.SecondBestOverlapPct.Value : DBNull.Value;
                        rowBuffer["Source_Geometry_Type"] = string.IsNullOrEmpty(result.SourceGeometryType) ? "Polygon" : result.SourceGeometryType;
                        rowBuffer["Conversion_Status"] = string.IsNullOrEmpty(result.ConversionStatus) ? "None" : result.ConversionStatus;
                        rowBuffer["Details"] = result.Details ?? string.Empty;
                        rowBuffer["Run_Date"] = result.RunDate;

                        insertCursor.Insert(rowBuffer);
                        count++;
                    }

                    insertCursor.Flush();
                    Logger.Info($"Results FC Population Finished: Successfully inserted and flushed {count} out of {totalCandidates} records into '{fcName}'.");
                    return count;
                });

                if (insertedCount == 0)
                {
                    string warnMsg = $"Results Feature Class '{fcName}' was created, but no features could be populated (0 geometries found from Source Layer).";
                    Logger.Warn(warnMsg);
                    return new FeatureClassCreationResult
                    {
                        Success = false,
                        DatasetName = fcName,
                        DatasetPath = fcFullPath,
                        ErrorMessage = warnMsg
                    };
                }

                // 6. Layered Duplicate Detection & Safe Map Registration (§17, §18)
                bool addedToMap = await AddFeatureClassToMapAsync(fcFullPath, fcName);

                Logger.Info($"Created Results Feature Class '{fcName}' with {insertedCount} features in {Path.GetFileName(gdbPath)}.");

                return new FeatureClassCreationResult
                {
                    Success = true,
                    DatasetName = fcName,
                    DatasetPath = fcFullPath,
                    LayerUri = new Uri(fcFullPath).AbsoluteUri,
                    FeatureCount = insertedCount,
                    AddedToMap = addedToMap
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create or populate Results Feature Class: {ex.Message}", ex);
                return new FeatureClassCreationResult
                {
                    Success = false,
                    ErrorMessage = $"Error creating Results Feature Class: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Layered duplicate detection and map registration for Standalone Tables (§8, §9).
        /// Order: Dataset URI / Path -> Normalized dataset name + workspace -> Display name.
        /// Prevents duplicate map entries.
        /// </summary>
        public static async Task<bool> AddResultsTableToMapAsync(string tablePath, string tableName)
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var activeMap = MapView.Active?.Map;
                    if (activeMap == null)
                    {
                        Logger.Warn("No active Map view available to add Standalone Table.");
                        return false;
                    }

                    var existingTables = activeMap.GetStandaloneTablesAsFlattenedList();
                    string normalizedTargetName = tableName.Trim();
                    string normalizedTargetPath = Path.GetFullPath(tablePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var targetUri = new Uri(tablePath);

                    foreach (var st in existingTables)
                    {
                        // 1. Compare dataset URI / path
                        if (!string.IsNullOrEmpty(st.URI))
                        {
                            if (string.Equals(st.URI, tablePath, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(st.URI, targetUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Info($"Standalone table '{tableName}' already registered in Map by URI. Skipping duplicate addition.");
                                return true;
                            }
                        }

                        try
                        {
                            using var underlyingTable = st.GetTable();
                            if (underlyingTable != null)
                            {
                                var tableUri = underlyingTable.GetPath();
                                if (tableUri != null)
                                {
                                    string localPath = tableUri.IsFile ? tableUri.LocalPath : tableUri.ToString();
                                    string normalizedLocalPath = Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                    if (string.Equals(normalizedLocalPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Logger.Info($"Standalone table '{tableName}' already registered in Map by underlying table path. Skipping duplicate addition.");
                                        return true;
                                    }
                                }

                                // 2. Compare dataset name in same workspace
                                string dsName = underlyingTable.GetName();
                                if (string.Equals(dsName, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    using var ds = underlyingTable.GetDatastore();
                                    if (ds is Geodatabase gdb && gdb.GetConnector() is FileGeodatabaseConnectionPath fgdb)
                                    {
                                        string tableGdbDir = fgdb.Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                        string targetGdbDir = Path.GetDirectoryName(normalizedTargetPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
                                        if (string.Equals(tableGdbDir, targetGdbDir, StringComparison.OrdinalIgnoreCase))
                                        {
                                            Logger.Info($"Standalone table '{tableName}' already registered in Map by dataset name & workspace. Skipping duplicate addition.");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Error reading standalone table properties during duplicate check: {ex.Message}");
                        }

                        // 3. Fallback: Compare display name
                        if (string.Equals(st.Name, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info($"Standalone table '{tableName}' already exists in Map by display name. Skipping duplicate addition.");
                            return true;
                        }
                    }

                    // Not found in map; safely create standalone table once
                    StandaloneTableFactory.Instance.CreateStandaloneTable(targetUri, activeMap);
                    Logger.Info($"Successfully registered standalone table '{tableName}' to active Map.");
                    return true;
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to add StandaloneTable to Map: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Layered duplicate detection and map registration for Feature Layers (§17, §18).
        /// Order: Dataset URI / Path -> Normalized dataset name + workspace -> Display name.
        /// Prevents duplicate map entries.
        /// </summary>
        public static async Task<bool> AddFeatureClassToMapAsync(string featureClassPath, string featureClassName)
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var activeMap = MapView.Active?.Map;
                    if (activeMap == null)
                    {
                        Logger.Warn("No active Map view available to add Feature Class.");
                        return false;
                    }

                    var existingFeatureLayers = activeMap.GetLayersAsFlattenedList().OfType<FeatureLayer>();
                    string normalizedTargetName = featureClassName.Trim();
                    string normalizedTargetPath = Path.GetFullPath(featureClassPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    var targetUri = new Uri(featureClassPath);

                    foreach (var fl in existingFeatureLayers)
                    {
                        // 1. Compare layer URI
                        if (!string.IsNullOrEmpty(fl.URI))
                        {
                            if (string.Equals(fl.URI, featureClassPath, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(fl.URI, targetUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Info($"Feature layer '{featureClassName}' already registered in Map by URI. Skipping duplicate addition.");
                                return true;
                            }
                        }

                        try
                        {
                            using var underlyingFc = fl.GetFeatureClass();
                            if (underlyingFc != null)
                            {
                                var fcUri = underlyingFc.GetPath();
                                if (fcUri != null)
                                {
                                    string localPath = fcUri.IsFile ? fcUri.LocalPath : fcUri.ToString();
                                    string normalizedLocalPath = Path.GetFullPath(localPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                    if (string.Equals(normalizedLocalPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Logger.Info($"Feature layer '{featureClassName}' already registered in Map by dataset path. Skipping duplicate addition.");
                                        return true;
                                    }
                                }

                                // 2. Compare dataset name in same workspace
                                string dsName = underlyingFc.GetName();
                                if (string.Equals(dsName, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                                {
                                    using var ds = underlyingFc.GetDatastore();
                                    if (ds is Geodatabase gdb && gdb.GetConnector() is FileGeodatabaseConnectionPath fgdb)
                                    {
                                        string fcGdbDir = fgdb.Path.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                        string targetGdbDir = Path.GetDirectoryName(normalizedTargetPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
                                        if (string.Equals(fcGdbDir, targetGdbDir, StringComparison.OrdinalIgnoreCase))
                                        {
                                            Logger.Info($"Feature layer '{featureClassName}' already registered in Map by dataset name & workspace. Skipping duplicate addition.");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Error reading feature layer properties during duplicate check: {ex.Message}");
                        }

                        // 3. Fallback: Compare display name
                        if (string.Equals(fl.Name, normalizedTargetName, StringComparison.OrdinalIgnoreCase))
                        {
                            Logger.Info($"Feature layer '{featureClassName}' already exists in Map by display name. Skipping duplicate addition.");
                            return true;
                        }
                    }

                    // Not found in map; safely create feature layer once
                    LayerFactory.Instance.CreateLayer(targetUri, activeMap);
                    Logger.Info($"Successfully registered feature layer '{featureClassName}' to active Map.");
                    return true;
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to add Feature Layer to Map: {ex.Message}");
                return false;
            }
        }

        private static async Task AddFieldAsync(string tableFullPath, string fieldName, string fieldType, int? length = null)
        {
            try
            {
                var args = length.HasValue
                    ? Geoprocessing.MakeValueArray(tableFullPath, fieldName, fieldType, "", "", length.Value)
                    : Geoprocessing.MakeValueArray(tableFullPath, fieldName, fieldType);

                var res = await Geoprocessing.ExecuteToolAsync("management.AddField", args);
                if (res.IsFailed)
                {
                    string err = string.Join("; ", res.ErrorMessages.Select(e => e.Text));
                    Logger.Warn($"Warning adding field '{fieldName}' to table: {err}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Exception adding field '{fieldName}': {ex.Message}");
            }
        }

        private static async Task<string> ResolveGeodatabasePathAsync(FeatureLayer? targetLayer, TransferSettings settings)
        {
            // Option 3: Custom GDB (§25)
            if (settings.OutputLocationType == "CustomGdb" && !string.IsNullOrWhiteSpace(settings.CustomGdbPath) && Directory.Exists(settings.CustomGdbPath))
            {
                return settings.CustomGdbPath;
            }

            // Option 1: Target Layer Workspace (§25)
            if (settings.OutputLocationType == "TargetWorkspace" && targetLayer != null)
            {
                string? targetGdb = await QueuedTask.Run(() =>
                {
                    try
                    {
                        using var table = targetLayer.GetTable();
                        using var datastore = table?.GetDatastore();
                        if (datastore is Geodatabase gdb)
                        {
                            var conn = gdb.GetConnector();
                            if (conn is FileGeodatabaseConnectionPath fgdbPath)
                            {
                                return fgdbPath.Path.LocalPath;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Could not determine target layer geodatabase: {ex.Message}");
                    }
                    return null;
                });

                if (!string.IsNullOrWhiteSpace(targetGdb) && Directory.Exists(targetGdb))
                {
                    return targetGdb;
                }
            }

            // Option 2 (Default fallback): Project Default GDB (§25)
            string? defaultGdb = Project.Current?.DefaultGeodatabasePath;
            if (!string.IsNullOrWhiteSpace(defaultGdb) && Directory.Exists(defaultGdb))
            {
                return defaultGdb;
            }

            // Fallback to temp directory if necessary
            return Path.GetTempPath();
        }

        /// <summary>
        /// Checks both Table and Feature Class definitions in the geodatabase to ensure
        /// collision-free unique dataset names (§10).
        /// </summary>
        private static async Task<string> DetermineUniqueDatasetNameAsync(string gdbPath, string baseName)
        {
            return await QueuedTask.Run(() =>
            {
                try
                {
                    using var gdb = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath)));
                    var existingTableDefs = gdb.GetDefinitions<TableDefinition>();
                    var existingFcDefs = gdb.GetDefinitions<FeatureClassDefinition>();

                    var existingNames = new HashSet<string>(
                        existingTableDefs.Select(d => d.GetName()).Concat(existingFcDefs.Select(d => d.GetName())),
                        StringComparer.OrdinalIgnoreCase);

                    if (!existingNames.Contains(baseName))
                    {
                        return baseName;
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string candidate = $"{baseName}_{timestamp}";
                    if (!existingNames.Contains(candidate))
                    {
                        return candidate;
                    }

                    int suffix = 1;
                    while (existingNames.Contains($"{candidate}_{suffix}"))
                    {
                        suffix++;
                    }
                    return $"{candidate}_{suffix}";
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error querying dataset definitions in GDB: {ex.Message}");
                    return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                }
            });
        }

        private static Polygon EnsureZandMConsistency(Polygon poly, FeatureClassDefinition fcDef)
        {
            try
            {
                bool fcHasZ = fcDef.HasZ();
                bool fcHasM = fcDef.HasM();

                if (poly.HasZ != fcHasZ || poly.HasM != fcHasM)
                {
                    var builder = new PolygonBuilderEx(poly)
                    {
                        HasZ = fcHasZ,
                        HasM = fcHasM
                    };
                    return builder.ToGeometry();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error ensuring Z/M consistency: {ex.Message}");
            }

            return poly;
        }
    }
}
