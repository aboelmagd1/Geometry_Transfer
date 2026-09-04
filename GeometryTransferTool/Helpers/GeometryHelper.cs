using System;
using System.Collections.Generic;
using ArcGIS.Core.Geometry;

namespace GeometryTransferTool.Helpers
{
    /// <summary>
    /// Utility methods for geometric validation, coordinate projection, envelope checks, and planar area computation.
    /// All methods are designed to be safe against nulls, missing spatial references, and native geometry engine exceptions.
    /// </summary>
    public static class GeometryHelper
    {
        /// <summary>
        /// Determines an appropriate common projected spatial reference for metric/planar area calculation.
        /// </summary>
        public static SpatialReference GetCommonProjectedSpatialReference(SpatialReference? sourceSr, SpatialReference? targetSr)
        {
            if (targetSr != null && targetSr.IsProjected)
            {
                return targetSr;
            }

            if (sourceSr != null && sourceSr.IsProjected)
            {
                return sourceSr;
            }

            if (targetSr != null)
            {
                return targetSr;
            }

            if (sourceSr != null)
            {
                return sourceSr;
            }

            return SpatialReferences.WebMercator;
        }

        /// <summary>
        /// Normalizes, simplifies, and projects a polygon geometry into the target spatial reference (§14).
        /// </summary>
        public static Polygon? NormalizeAndValidatePolygon(Geometry? geometry, SpatialReference targetSr, SpatialReference? defaultLayerSr = null)
        {
            return ValidateAndPreparePolygon(geometry, targetSr, defaultLayerSr);
        }

        /// <summary>
        /// Validates if a polyline forms a closed ring, is non-empty, and has sufficient vertices to form a polygon (§34).
        /// </summary>
        public static bool IsClosedPolyline(Polyline? polyline)
        {
            if (polyline == null || polyline.IsEmpty)
            {
                return false;
            }

            // Must have at least 3 points/vertices (or 4 when closed: start == end)
            if (polyline.PointCount < 3)
            {
                return false;
            }

            // Verify part by part: each part (ReadOnlySegmentCollection) must have its start point equal to its end point
            var parts = polyline.Parts;
            if (parts == null || parts.Count == 0)
            {
                return false;
            }

            foreach (var part in parts)
            {
                if (part == null || part.Count == 0)
                {
                    return false;
                }

                // In ArcGIS Pro SDK, a part is a ReadOnlySegmentCollection.
                // The part's start point is the start of the first segment, and its end point is the end of the last segment.
                var firstSegment = part[0];
                var lastSegment = part[part.Count - 1];
                if (firstSegment == null || lastSegment == null)
                {
                    return false;
                }

                var startPt = firstSegment.StartPoint;
                var endPt = lastSegment.EndPoint;
                if (startPt == null || endPt == null)
                {
                    return false;
                }

                // Check coordinate equality within geometric tolerance
                double dx = Math.Abs(startPt.X - endPt.X);
                double dy = Math.Abs(startPt.Y - endPt.Y);
                if (dx > 1e-4 || dy > 1e-4)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Converts a closed or auto-closable polyline geometry into a valid, simplified polygon object (§34).
        /// If the polyline is not explicitly closed but has >= 3 vertices, automatically closes the ring by connecting the last point to the first.
        /// Returns null if the polyline is invalid, has fewer than 3 vertices, or produces a degenerate polygon.
        /// </summary>
        public static Polygon? ConvertPolylineToPolygon(Polyline? polyline, SpatialReference? targetSr = null, SpatialReference? defaultLayerSr = null)
        {
            if (polyline == null || polyline.IsEmpty || polyline.PointCount < 3)
            {
                return null;
            }

            try
            {
                var lineSr = polyline.SpatialReference ?? defaultLayerSr ?? targetSr;

                // Build polygon using ArcGIS Pro SDK PolygonBuilderEx, ensuring closure
                var polyBuilder = new PolygonBuilderEx(lineSr);
                foreach (var part in polyline.Parts)
                {
                    var pts = new List<MapPoint>();
                    foreach (var seg in part)
                    {
                        pts.Add(seg.StartPoint);
                    }
                    if (part.Count > 0)
                    {
                        pts.Add(part[part.Count - 1].EndPoint);
                    }

                    if (pts.Count >= 3)
                    {
                        // Auto-close if start and end points are not identical
                        var first = pts[0];
                        var last = pts[pts.Count - 1];
                        if (Math.Abs(first.X - last.X) > 1e-6 || Math.Abs(first.Y - last.Y) > 1e-6)
                        {
                            pts.Add(first);
                        }
                        polyBuilder.AddPart(pts);
                    }
                }

                var polygon = polyBuilder.ToGeometry() as Polygon;
                if (polygon == null || polygon.IsEmpty)
                {
                    return null;
                }

                // Ensure spatial reference is attached
                if (polygon.SpatialReference == null && lineSr != null)
                {
                    polygon = PolygonBuilderEx.CreatePolygon(polygon, lineSr);
                }

                // Simplify and fix topological orientation (rings, holes)
                if (!GeometryEngine.Instance.IsSimpleAsFeature(polygon))
                {
                    try
                    {
                        var simplified = GeometryEngine.Instance.SimplifyAsFeature(polygon) as Polygon;
                        if (simplified != null && !simplified.IsEmpty)
                        {
                            polygon = simplified;
                        }
                    }
                    catch (Exception simEx)
                    {
                        Logger.Warn($"SimplifyAsFeature on converted polygon failed: {simEx.Message}");
                    }
                }

                // Verify planar/geodesic area is positive and non-zero
                if (polygon == null || polygon.IsEmpty || Math.Abs(polygon.Area) < 1e-8)
                {
                    Logger.Warn("Converted polygon is empty or has zero area.");
                    return null;
                }

                // Reproject to target spatial reference if requested and different
                if (targetSr != null && polygon.SpatialReference != null && !SpatialReference.AreEqual(polygon.SpatialReference, targetSr))
                {
                    try
                    {
                        var projected = GeometryEngine.Instance.Project(polygon, targetSr) as Polygon;
                        if (projected != null && !projected.IsEmpty)
                        {
                            polygon = projected;
                        }
                    }
                    catch (Exception projEx)
                    {
                        Logger.Error($"Projection failed during polyline-to-polygon conversion: {projEx.Message}", projEx);
                        return null;
                    }
                }

                return polygon;
            }
            catch (Exception ex)
            {
                Logger.Error($"ConvertPolylineToPolygon encountered unexpected error: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Assembles a collection of polylines (which may be individual line segments or multiple touching lines)
        /// into valid simplified polygon geometries in memory (§34).
        /// Emulates the Feature to Polygon geoprocessing behavior completely within the ArcGIS Pro SDK geometry engine.
        /// </summary>
        public static List<Polygon> AssemblePolylinesToPolygons(
            IEnumerable<Polyline> polylines,
            SpatialReference? targetSr = null,
            double snapTolerance = 0.1)
        {
            var resultPolygons = new List<Polygon>();
            if (polylines == null) return resultPolygons;

            var polylineList = polylines.Where(p => p != null && !p.IsEmpty).ToList();
            if (polylineList.Count == 0) return resultPolygons;

            var sr = targetSr ?? polylineList[0].SpatialReference ?? SpatialReferences.WebMercator;

            // 1. Try converting any polylines that have >= 3 points individually
            var remainingLines = new List<Polyline>();
            foreach (var pl in polylineList)
            {
                if (pl.PointCount >= 3)
                {
                    var poly = ConvertPolylineToPolygon(pl, sr);
                    if (poly != null && !poly.IsEmpty && Math.Abs(poly.Area) > 1e-4)
                    {
                        resultPolygons.Add(poly);
                        continue;
                    }
                }
                remainingLines.Add(pl);
            }

            if (remainingLines.Count == 0)
            {
                return resultPolygons;
            }

            // 2. Multi-segment assembly for remaining line segments
            try
            {
                // Extract all individual segments
                var segments = new List<(MapPoint Start, MapPoint End)>();
                foreach (var pl in remainingLines)
                {
                    foreach (var part in pl.Parts)
                    {
                        foreach (var seg in part)
                        {
                            segments.Add((seg.StartPoint, seg.EndPoint));
                        }
                    }
                }

                if (segments.Count < 3)
                {
                    return resultPolygons;
                }

                // Snap helper
                bool PointsEqual(MapPoint p1, MapPoint p2)
                {
                    double dx = p1.X - p2.X;
                    double dy = p1.Y - p2.Y;
                    return (dx * dx + dy * dy) <= (snapTolerance * snapTolerance);
                }

                // Chain segments into closed loops
                var unused = new List<(MapPoint Start, MapPoint End)>(segments);
                while (unused.Count >= 3)
                {
                    var chain = new List<MapPoint>();
                    var firstSeg = unused[0];
                    unused.RemoveAt(0);

                    chain.Add(firstSeg.Start);
                    chain.Add(firstSeg.End);

                    bool foundNext = true;
                    while (foundNext)
                    {
                        foundNext = false;
                        var currentEnd = chain[chain.Count - 1];

                        // Check if loop is closed back to start
                        if (chain.Count >= 4 && PointsEqual(currentEnd, chain[0]))
                        {
                            break;
                        }

                        // Look for an unused segment that connects to currentEnd
                        for (int i = 0; i < unused.Count; i++)
                        {
                            var seg = unused[i];
                            if (PointsEqual(currentEnd, seg.Start))
                            {
                                chain.Add(seg.End);
                                unused.RemoveAt(i);
                                foundNext = true;
                                break;
                            }
                            else if (PointsEqual(currentEnd, seg.End))
                            {
                                chain.Add(seg.Start);
                                unused.RemoveAt(i);
                                foundNext = true;
                                break;
                            }
                        }
                    }

                    // If chain closed back to start
                    if (chain.Count >= 4 && PointsEqual(chain[chain.Count - 1], chain[0]))
                    {
                        chain[chain.Count - 1] = chain[0];

                        try
                        {
                            var builder = new PolygonBuilderEx(sr);
                            builder.AddPart(chain);
                            var raw = builder.ToGeometry() as Polygon;
                            if (raw != null)
                            {
                                var simple = GeometryEngine.Instance.SimplifyAsFeature(raw) as Polygon;
                                if (simple != null && !simple.IsEmpty && Math.Abs(simple.Area) > 1e-4)
                                {
                                    resultPolygons.Add(simple);
                                }
                            }
                        }
                        catch (Exception bldEx)
                        {
                            Logger.Warn($"Failed to build polygon from segment chain: {bldEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"AssemblePolylinesToPolygons error: {ex.Message}");
            }

            return resultPolygons;
        }

        /// <summary>
        /// Validates, repairs, converts (if Polyline), and projects a geometry into the target spatial reference (§14, §34).
        /// Returns null if geometry is empty, null, or unrepairable.
        /// </summary>
        public static Polygon? ValidateAndPreparePolygon(Geometry? geometry, SpatialReference targetSr, SpatialReference? defaultLayerSr = null)
        {
            if (geometry == null || geometry.IsEmpty)
            {
                return null;
            }

            // Handle Polyline input automatically (§34)
            if (geometry.GeometryType == GeometryType.Polyline)
            {
                return ConvertPolylineToPolygon(geometry as Polyline, targetSr, defaultLayerSr);
            }

            if (geometry.GeometryType != GeometryType.Polygon)
            {
                return null;
            }

            var polygon = geometry as Polygon;
            if (polygon == null || polygon.IsEmpty)
            {
                return null;
            }

            try
            {
                // If polygon has no spatial reference, assign the layer SR or target SR
                if (polygon.SpatialReference == null)
                {
                    var fallbackSr = defaultLayerSr ?? targetSr;
                    if (fallbackSr != null)
                    {
                        polygon = PolygonBuilderEx.CreatePolygon(polygon, fallbackSr);
                    }
                }

                // Attempt repair if not simple
                if (!GeometryEngine.Instance.IsSimpleAsFeature(polygon))
                {
                    try
                    {
                        var repaired = GeometryEngine.Instance.SimplifyAsFeature(polygon) as Polygon;
                        if (repaired != null && !repaired.IsEmpty)
                        {
                            polygon = repaired;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to simplify polygon: {ex.Message}");
                    }
                }

                if (polygon == null || polygon.IsEmpty)
                {
                    return null;
                }

                // Project to common spatial reference if needed
                var currentSr = polygon.SpatialReference;
                if (currentSr != null && targetSr != null && !SpatialReference.AreEqual(currentSr, targetSr))
                {
                    try
                    {
                        var projected = GeometryEngine.Instance.Project(polygon, targetSr) as Polygon;
                        if (projected != null && !projected.IsEmpty)
                        {
                            polygon = projected;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Projection failed from SR {currentSr.Wkid} to {targetSr.Wkid}: {ex.Message}", ex);
                        return null;
                    }
                }

                return polygon;
            }
            catch (Exception ex)
            {
                Logger.Error($"ValidateAndPreparePolygon error: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Performs a fast 2D axis-aligned bounding box (envelope) intersection pre-check.
        /// </summary>
        public static bool EnvelopesIntersect(Envelope? env1, Envelope? env2)
        {
            if (env1 == null || env2 == null || env1.IsEmpty || env2.IsEmpty)
            {
                return false;
            }

            return !(env1.XMax < env2.XMin ||
                     env1.XMin > env2.XMax ||
                     env1.YMax < env2.YMin ||
                     env1.YMin > env2.YMax);
        }

        /// <summary>
        /// Computes the intersection geometry of two polygons.
        /// Returns null if they do not intersect or result is empty.
        /// </summary>
        public static Polygon? ComputeIntersection(Polygon poly1, Polygon poly2)
        {
            if (poly1 == null || poly2 == null || poly1.IsEmpty || poly2.IsEmpty)
            {
                return null;
            }

            try
            {
                if (!GeometryEngine.Instance.Intersects(poly1, poly2))
                {
                    return null;
                }

                var intersection = GeometryEngine.Instance.Intersection(poly1, poly2) as Polygon;
                if (intersection == null || intersection.IsEmpty)
                {
                    return null;
                }

                return intersection;
            }
            catch (Exception ex)
            {
                Logger.Warn($"ComputeIntersection failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Computes the planar or geodesic area of a polygon in square units.
        /// </summary>
        public static double GetPlanarArea(Polygon? polygon)
        {
            if (polygon == null || polygon.IsEmpty)
            {
                return 0.0;
            }

            try
            {
                if (polygon.SpatialReference != null && polygon.SpatialReference.IsGeographic)
                {
                    return Math.Abs(GeometryEngine.Instance.GeodesicArea(polygon, AreaUnit.SquareMeters));
                }
                return Math.Abs(polygon.Area);
            }
            catch
            {
                return Math.Abs(polygon.Area);
            }
        }
    }
}
