using System;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using GeometryTransferTool.Helpers;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Validates layer editability, geometry type, and schema compatibility.
    /// </summary>
    public static class LayerValidationService
    {
        /// <summary>
        /// Validates layer geometry types (polygon required) and optional target editability (§6).
        /// </summary>
        public static ValidationResult ValidateLayers(FeatureLayer? sourceLayer, FeatureLayer? targetLayer, bool requireTargetEditable = false)
        {
            if (sourceLayer == null || targetLayer == null)
            {
                return ValidationResult.Fail("Source and Target layers must both be specified.");
            }

            // Verify Geometry Types (§34: Source can be Polygon or Polyline; Target must be Polygon)
            var srcShapeType = sourceLayer.ShapeType;
            if (srcShapeType != esriGeometryType.esriGeometryPolygon && srcShapeType != esriGeometryType.esriGeometryPolyline)
            {
                return ValidationResult.Fail($"Source layer '{sourceLayer.Name}' has unsupported geometry type ({srcShapeType}). Only Polygon and Polyline layers are supported as Source.");
            }

            var tgtShapeType = targetLayer.ShapeType;
            if (tgtShapeType != esriGeometryType.esriGeometryPolygon)
            {
                return ValidationResult.Fail($"Target layer '{targetLayer.Name}' is not a polygon layer ({tgtShapeType}). Target layer must strictly be Polygon.");
            }

            // Verify Target Editability (required for Transfer, optional for Preview)
            if (requireTargetEditable && !targetLayer.CanEditData())
            {
                return ValidationResult.Fail("The Target Layer is not editable. Please verify permissions and editing capabilities.");
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Inspects the layer data connection to determine if it originates from an HTTP/HTTPS web service (§4).
        /// Checks layer URI, CIM data connection, and datastore connector.
        /// </summary>
        public static async Task<bool> IsHttpServiceLayerAsync(FeatureLayer? layer)
        {
            if (layer == null) return false;

            // 1. Quick check on Layer URI
            if (!string.IsNullOrEmpty(layer.URI) && ContainsHttp(layer.URI))
            {
                return true;
            }

            // 2. Deep inspection on MCT (QueuedTask)
            return await ArcGIS.Desktop.Framework.Threading.Tasks.QueuedTask.Run(() =>
            {
                try
                {
                    // Inspect CIM Definition and FeatureTable DataConnection
                    if (layer.GetDefinition() is CIMFeatureLayer cimLayer)
                    {
                        var dataConn = cimLayer.FeatureTable?.DataConnection;
                        if (dataConn != null)
                        {
                            if (dataConn is CIMStandardDataConnection stdConn && !string.IsNullOrEmpty(stdConn.WorkspaceConnectionString))
                            {
                                if (ContainsHttp(stdConn.WorkspaceConnectionString)) return true;
                            }

                            string dataConnStr = dataConn.ToString() ?? string.Empty;
                            if (ContainsHttp(dataConnStr)) return true;
                        }
                    }

                    // Inspect Table Datastore and Connector
                    using var table = layer.GetTable();
                    if (table != null)
                    {
                        using var datastore = table.GetDatastore();
                        if (datastore != null)
                        {
                            string dsType = datastore.GetType().Name;
                            if (dsType.IndexOf("Service", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }

                            var connector = datastore.GetConnector();
                            if (connector != null)
                            {
                                string connStr = connector.ToString() ?? string.Empty;
                                if (ContainsHttp(connStr)) return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error inspecting data source for HTTP service check on layer '{layer.Name}': {ex.Message}");
                }

                return false;
            });
        }

        /// <summary>
        /// Validates that an HTTP/HTTPS layer (Source or Target) is allowed for transfer.
        /// Covers both From (Source) and To (Target) web services.
        /// </summary>
        public static async Task<ValidationResult> ValidateTransferWebServiceAsync(
            FeatureLayer? sourceLayer,
            FeatureLayer? targetLayer,
            bool allowWebServiceTransfer)
        {
            bool isSourceHttp = await IsHttpServiceLayerAsync(sourceLayer);
            bool isTargetHttp = await IsHttpServiceLayerAsync(targetLayer);

            if ((isSourceHttp || isTargetHttp) && !allowWebServiceTransfer)
            {
                string which = (isSourceHttp && isTargetHttp)
                    ? "Both Source and Target layers are connected to an HTTP/HTTPS web service"
                    : (isSourceHttp
                        ? "The Source Layer is connected to an HTTP/HTTPS web service"
                        : "The Target Layer is connected to an HTTP/HTTPS web service");

                return ValidationResult.Fail(
                    $"Transfer blocked: {which}.\n" +
                    "For safety, transfers involving web services (From or To) are blocked by default.\n" +
                    "Enable \"Allow Transfer to / from Web Service (HTTP Source or Target)\" if you intentionally want to proceed."
                );
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Backward-compatibility validation for HTTP source layer.
        /// </summary>
        public static async Task<ValidationResult> ValidateTransferHttpSourceAsync(FeatureLayer? sourceLayer, bool allowWebServiceSourceTransfer)
        {
            return await ValidateTransferWebServiceAsync(sourceLayer, null, allowWebServiceSourceTransfer);
        }

        private static bool ContainsHttp(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.IndexOf("http://", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("https://", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
