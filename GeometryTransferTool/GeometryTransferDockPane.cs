using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Mapping.Events;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;
using GeometryTransferTool.Services;

namespace GeometryTransferTool
{
    /// <summary>
    /// ViewModel for Geometry Transfer Tool DockPane.
    /// Manages UI state, layer bindings, two-phase preview/transfer workflow, and settings persistence.
    /// </summary>
    internal class GeometryTransferDockPaneViewModel : DockPane
    {
        private const string DockPaneId = "GeometryTransferDockPane";

        private readonly GeometryMatchingService _matchingService = new();
        private readonly GeometryTransferService _transferService = new();

        private ObservableCollection<LayerItem> _layers = new();
        private LayerItem? _selectedSourceLayerItem;
        private LayerItem? _selectedTargetLayerItem;

        private string _savedSourceLayerName = string.Empty;
        private string _savedTargetLayerName = string.Empty;
        private string _savedSourceLayerUri = string.Empty;
        private string _savedTargetLayerUri = string.Empty;

        private bool _ignoreThreshold = false;
        private double _overlapThreshold = 80.0;
        private double _ambiguityTolerance = 2.0;
        private string _selectedMatchingMethod = "Polygon Overlap Percentage";
        private bool _attributeMappingEnabled = false;
        private bool _skipPreview = false;

        private bool _isBusy = false;
        private string _statusMessage = "Ready. Select polygon features in Source and Target layers.";
        private bool _canConfirmTransfer = false;

        private TransferSummary? _summary;
        private ObservableCollection<MatchResult> _matchResults = new();
        private ObservableCollection<AttributeMappingItem> _attributeMappings = new();

        private ObservableCollection<string> _availableSourceFields = new();
        private ObservableCollection<string> _availableTargetFields = new();

        public GeometryTransferDockPaneViewModel()
        {
            PreviewMatchesCommand = new AppRelayCommand(async () => await ExecutePreviewAsync(), () => !IsBusy);
            ConfirmTransferCommand = new AppRelayCommand(async () => await ExecuteTransferAsync(), () => !IsBusy && CanConfirmTransfer);
            RefreshLayersCommand = new AppRelayCommand(async () => await RefreshLayersAsync(), () => !IsBusy);
            ClearResultsCommand = new AppRelayCommand(() => ClearResults(), () => !IsBusy);
            AddMappingRowCommand = new AppRelayCommand(() => AddMappingRow());
            RemoveMappingRowCommand = new AppRelayCommand(param => RemoveMappingRow(param as AttributeMappingItem));

            MatchingMethods = new ObservableCollection<string>
            {
                "Polygon Overlap Percentage"
            };

            LoadSavedSettings();
        }

        #region Properties

        public ObservableCollection<LayerItem> Layers
        {
            get => _layers;
            set => SetProperty(ref _layers, value);
        }

        public LayerItem? SelectedSourceLayerItem
        {
            get => _selectedSourceLayerItem;
            set
            {
                if (SetProperty(ref _selectedSourceLayerItem, value))
                {
                    if (value != null)
                    {
                        _savedSourceLayerName = value.Name;
                        _savedSourceLayerUri = value.LayerUri;
                    }
                    _ = LoadFieldsForLayerAsync(value?.Layer, isSource: true);
                    ClearResults();
                    SaveCurrentSettings();
                }
            }
        }

        public LayerItem? SelectedTargetLayerItem
        {
            get => _selectedTargetLayerItem;
            set
            {
                if (SetProperty(ref _selectedTargetLayerItem, value))
                {
                    if (value != null)
                    {
                        _savedTargetLayerName = value.Name;
                        _savedTargetLayerUri = value.LayerUri;
                    }
                    _ = LoadFieldsForLayerAsync(value?.Layer, isSource: false);
                    ClearResults();
                    SaveCurrentSettings();
                }
            }
        }

        public bool IgnoreThreshold
        {
            get => _ignoreThreshold;
            set
            {
                if (SetProperty(ref _ignoreThreshold, value))
                {
                    NotifyPropertyChanged(nameof(IsThresholdActive));
                    SaveCurrentSettings();
                }
            }
        }

        public bool IsThresholdActive => !IgnoreThreshold;

        public double OverlapThreshold
        {
            get => _overlapThreshold;
            set
            {
                if (SetProperty(ref _overlapThreshold, Math.Clamp(value, 1.0, 100.0)))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public double AmbiguityTolerance
        {
            get => _ambiguityTolerance;
            set
            {
                if (SetProperty(ref _ambiguityTolerance, Math.Clamp(value, 0.1, 20.0)))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public ObservableCollection<string> MatchingMethods { get; }

        public string SelectedMatchingMethod
        {
            get => _selectedMatchingMethod;
            set => SetProperty(ref _selectedMatchingMethod, value);
        }

        public bool AttributeMappingEnabled
        {
            get => _attributeMappingEnabled;
            set
            {
                if (SetProperty(ref _attributeMappingEnabled, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public bool SkipPreview
        {
            get => _skipPreview;
            set
            {
                if (SetProperty(ref _skipPreview, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public new bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool CanConfirmTransfer
        {
            get => _canConfirmTransfer;
            set => SetProperty(ref _canConfirmTransfer, value);
        }

        public TransferSummary? Summary
        {
            get => _summary;
            set
            {
                if (SetProperty(ref _summary, value))
                {
                    NotifyPropertyChanged(nameof(HasSummary));
                }
            }
        }

        public bool HasSummary => Summary != null;

        private MatchResult? _selectedMatchResult;

        public MatchResult? SelectedMatchResult
        {
            get => _selectedMatchResult;
            set
            {
                if (SetProperty(ref _selectedMatchResult, value) && value != null)
                {
                    _ = ZoomToMatchResultAsync(value);
                }
            }
        }

        public ObservableCollection<MatchResult> MatchResults
        {
            get => _matchResults;
            set => SetProperty(ref _matchResults, value);
        }

        public ObservableCollection<AttributeMappingItem> AttributeMappings
        {
            get => _attributeMappings;
            set => SetProperty(ref _attributeMappings, value);
        }

        public ObservableCollection<string> AvailableSourceFields
        {
            get => _availableSourceFields;
            set => SetProperty(ref _availableSourceFields, value);
        }

        public ObservableCollection<string> AvailableTargetFields
        {
            get => _availableTargetFields;
            set => SetProperty(ref _availableTargetFields, value);
        }

        #endregion

        #region Commands

        public ICommand PreviewMatchesCommand { get; }
        public ICommand ConfirmTransferCommand { get; }
        public ICommand RefreshLayersCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand AddMappingRowCommand { get; }
        public ICommand RemoveMappingRowCommand { get; }

        #endregion

        #region Lifecycle & Events

        protected override async void OnShow(bool isInitialShow)
        {
            base.OnShow(isInitialShow);
            try
            {
                SubscribeToArcGISEvents();
                await RefreshLayersAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Error in DockPane OnShow", ex);
            }
        }

        protected override void OnHidden()
        {
            try
            {
                UnsubscribeFromArcGISEvents();
                SaveCurrentSettings();
            }
            catch (Exception ex)
            {
                Logger.Error("Error in DockPane OnHidden", ex);
            }
            base.OnHidden();
        }

        private void SubscribeToArcGISEvents()
        {
            MapSelectionChangedEvent.Subscribe(OnMapSelectionChanged);
            ActiveMapViewChangedEvent.Subscribe(OnActiveMapViewChanged);
            LayersAddedEvent.Subscribe(OnLayersChanged);
            LayersRemovedEvent.Subscribe(OnLayersRemoved);
        }

        private void UnsubscribeFromArcGISEvents()
        {
            MapSelectionChangedEvent.Unsubscribe(OnMapSelectionChanged);
            ActiveMapViewChangedEvent.Unsubscribe(OnActiveMapViewChanged);
            LayersAddedEvent.Unsubscribe(OnLayersChanged);
            LayersRemovedEvent.Unsubscribe(OnLayersRemoved);
        }

        private async void OnMapSelectionChanged(MapSelectionChangedEventArgs args)
        {
            try
            {
                await UpdateLayerSelectionCountsAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnMapSelectionChanged error: {ex.Message}");
            }
        }

        private async void OnActiveMapViewChanged(ActiveMapViewChangedEventArgs args)
        {
            try
            {
                await RefreshLayersAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnActiveMapViewChanged error: {ex.Message}");
            }
        }

        private async void OnLayersChanged(LayerEventsArgs args)
        {
            try
            {
                await RefreshLayersAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnLayersChanged error: {ex.Message}");
            }
        }

        private async void OnLayersRemoved(LayerEventsArgs args)
        {
            try
            {
                await RefreshLayersAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnLayersRemoved error: {ex.Message}");
            }
        }

        #endregion

        #region Workflow Execution

        public async Task RefreshLayersAsync()
        {
            try
            {
                var map = MapView.Active?.Map;
                if (map == null)
                {
                    Layers = new ObservableCollection<LayerItem>();
                    SelectedSourceLayerItem = null;
                    SelectedTargetLayerItem = null;
                    return;
                }

                var polygonLayers = await QueuedTask.Run(() =>
                {
                    var list = new List<LayerItem>();
                    var allLayers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>();
                    foreach (var fl in allLayers)
                    {
                        if (fl.ShapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolygon)
                        {
                            int selCount = fl.SelectionCount;
                            list.Add(new LayerItem(fl, selCount));
                        }
                    }
                    return list;
                });

                string prevSourceUri = SelectedSourceLayerItem?.LayerUri ?? _savedSourceLayerUri;
                string prevSourceName = SelectedSourceLayerItem?.Name ?? _savedSourceLayerName;
                string prevTargetUri = SelectedTargetLayerItem?.LayerUri ?? _savedTargetLayerUri;
                string prevTargetName = SelectedTargetLayerItem?.Name ?? _savedTargetLayerName;

                Layers = new ObservableCollection<LayerItem>(polygonLayers);

                // Restore selections if still in list (by URI or Layer Name)
                SelectedSourceLayerItem = Layers.FirstOrDefault(l =>
                    (!string.IsNullOrEmpty(prevSourceUri) && l.LayerUri == prevSourceUri) ||
                    (!string.IsNullOrEmpty(prevSourceName) && l.Name.Equals(prevSourceName, StringComparison.OrdinalIgnoreCase)));

                SelectedTargetLayerItem = Layers.FirstOrDefault(l =>
                    (!string.IsNullOrEmpty(prevTargetUri) && l.LayerUri == prevTargetUri) ||
                    (!string.IsNullOrEmpty(prevTargetName) && l.Name.Equals(prevTargetName, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to refresh layers", ex);
            }
        }

        private async Task UpdateLayerSelectionCountsAsync()
        {
            try
            {
                await QueuedTask.Run(() =>
                {
                    foreach (var item in Layers)
                    {
                        if (item.Layer != null)
                        {
                            item.SelectionCount = item.Layer.SelectionCount;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to update selection counts: {ex.Message}");
            }
        }

        public async Task ExecutePreviewAsync()
        {
            if (IsBusy) return;

            try
            {
                var validation = await ValidateInputsAsync();
                if (!validation.IsValid)
                {
                    MessageHelper.ShowWarning(validation.ErrorMessage);
                    StatusMessage = validation.ErrorMessage;
                    return;
                }

                IsBusy = true;
                StatusMessage = "Computing polygon overlaps and resolving matches...";
                ClearResults();

                var settings = BuildCurrentSettings();
                var sourceLayer = SelectedSourceLayerItem!.Layer;
                var targetLayer = SelectedTargetLayerItem!.Layer;

                var (results, summary) = await _matchingService.PerformMatchingAsync(sourceLayer, targetLayer, settings);

                MatchResults = new ObservableCollection<MatchResult>(results);
                Summary = summary;
                CanConfirmTransfer = summary.MatchedCount > 0;

                StatusMessage = $"Preview complete. {summary.MatchedCount} feature(s) matched and ready to transfer.";

                if (SkipPreview && CanConfirmTransfer)
                {
                    await ExecuteTransferAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Preview Matching error", ex);
                MessageHelper.ShowError($"An error occurred during matching: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task ExecuteTransferAsync()
        {
            if (IsBusy || MatchResults.Count == 0) return;

            var validMatches = MatchResults.Where(r => r.CanTransfer).ToList();
            if (validMatches.Count == 0)
            {
                MessageHelper.ShowWarning("No confirmed matches are available to transfer.");
                return;
            }

            // If not skipping preview, ask user confirmation
            if (!SkipPreview)
            {
                bool confirmed = MessageHelper.ShowQuestion(
                    $"Transfer geometry for {validMatches.Count} matched target polygon feature(s)?\n\nExisting target attributes will remain unchanged.",
                    "Confirm Geometry Transfer");
                if (!confirmed) return;
            }

            IsBusy = true;
            StatusMessage = "Transferring polygon geometries...";

            try
            {
                var settings = BuildCurrentSettings();
                var sourceLayer = SelectedSourceLayerItem!.Layer;
                var targetLayer = SelectedTargetLayerItem!.Layer;

                int transferredCount = await _transferService.TransferGeometriesAsync(sourceLayer, targetLayer, MatchResults, settings);

                // Update result table statuses
                foreach (var r in MatchResults.Where(m => m.CanTransfer))
                {
                    r.Details = $"Transferred successfully at {DateTime.Now:HH:mm:ss}.";
                }

                CanConfirmTransfer = false;
                StatusMessage = $"Transfer completed successfully. Transferred {transferredCount} feature(s).";
                MessageHelper.ShowInfo($"Transfer completed successfully!\n\nTransferred: {transferredCount} polygon(s).\nTarget attributes preserved.", "Geometry Transfer Complete");
            }
            catch (Exception ex)
            {
                Logger.Error("Transfer Execution error", ex);
                MessageHelper.ShowError($"An error occurred during geometry transfer: {ex.Message}");
                StatusMessage = $"Transfer failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<ValidationResult> ValidateInputsAsync()
        {
            if (SelectedSourceLayerItem?.Layer == null)
            {
                return ValidationResult.Fail("Please select a Source / Drawing Layer.");
            }

            if (SelectedTargetLayerItem?.Layer == null)
            {
                return ValidationResult.Fail("Please select a Target / Master Layer.");
            }

            var srcLayer = SelectedSourceLayerItem.Layer;
            var tgtLayer = SelectedTargetLayerItem.Layer;

            try
            {
                return await QueuedTask.Run(() =>
                {
                    int srcCount = srcLayer.SelectionCount;
                    int tgtCount = tgtLayer.SelectionCount;

                    var selValidation = SelectionValidationService.ValidateSelections(srcLayer, tgtLayer, srcCount, tgtCount);
                    if (!selValidation.IsValid)
                    {
                        return selValidation;
                    }

                    var layerValidation = LayerValidationService.ValidateLayers(srcLayer, tgtLayer);
                    if (!layerValidation.IsValid)
                    {
                        return layerValidation;
                    }

                    return ValidationResult.Success();
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Validation check error", ex);
                return ValidationResult.Fail($"Validation failed: {ex.Message}");
            }
        }

        private async Task LoadFieldsForLayerAsync(FeatureLayer? layer, bool isSource)
        {
            if (layer == null)
            {
                if (isSource) AvailableSourceFields.Clear();
                else AvailableTargetFields.Clear();
                return;
            }

            try
            {
                var fields = await QueuedTask.Run(() =>
                {
                    var list = new List<string>();
                    using var table = layer.GetTable();
                    if (table != null)
                    {
                        var def = table.GetDefinition();
                        foreach (var fld in def.GetFields())
                        {
                            // Filter non-transferrable types
                            if (fld.FieldType == FieldType.Geometry ||
                                fld.FieldType == FieldType.Blob ||
                                fld.FieldType == FieldType.Raster)
                            {
                                continue;
                            }

                            // For target, exclude non-editable / system fields (§15)
                            if (!isSource)
                            {
                                if (!fld.IsEditable ||
                                    fld.FieldType == FieldType.OID ||
                                    fld.FieldType == FieldType.GlobalID ||
                                    fld.Name.Equals("Shape_Length", StringComparison.OrdinalIgnoreCase) ||
                                    fld.Name.Equals("Shape_Area", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                            }

                            list.Add(fld.Name);
                        }
                    }
                    return list;
                });

                if (isSource)
                {
                    AvailableSourceFields = new ObservableCollection<string>(fields);
                }
                else
                {
                    AvailableTargetFields = new ObservableCollection<string>(fields);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load fields for layer '{layer.Name}': {ex.Message}");
            }
        }

        private void AddMappingRow()
        {
            string defaultSrc = AvailableSourceFields.FirstOrDefault() ?? string.Empty;
            string defaultTgt = AvailableTargetFields.FirstOrDefault() ?? string.Empty;

            AttributeMappings.Add(new AttributeMappingItem
            {
                SourceField = defaultSrc,
                TargetField = defaultTgt,
                IsEnabled = true
            });
        }

        private void RemoveMappingRow(AttributeMappingItem? item)
        {
            if (item != null)
            {
                AttributeMappings.Remove(item);
            }
        }

        public async Task ZoomToMatchResultAsync(MatchResult? result)
        {
            if (result == null) return;

            var mapView = MapView.Active;
            if (mapView == null) return;

            var srcLayer = SelectedSourceLayerItem?.Layer;
            var tgtLayer = SelectedTargetLayerItem?.Layer;

            try
            {
                await QueuedTask.Run(() =>
                {
                    Envelope? combinedExtent = null;
                    var geometriesToFlash = new List<Geometry>();

                    // 1. Query Source Feature Geometry
                    if (srcLayer != null && result.SourceOid > 0)
                    {
                        using var srcTable = srcLayer.GetTable();
                        if (srcTable != null)
                        {
                            var qf = new QueryFilter { ObjectIDs = new[] { result.SourceOid } };
                            using var cursor = srcTable.Search(qf);
                            if (cursor.MoveNext())
                            {
                                using var feat = (Feature)cursor.Current;
                                var shape = feat.GetShape();
                                if (shape != null && !shape.IsEmpty)
                                {
                                    combinedExtent = shape.Extent;
                                    geometriesToFlash.Add(shape);
                                }
                            }
                        }
                    }

                    // 2. Query Target Feature Geometry if available
                    if (tgtLayer != null && result.TargetOid.HasValue && result.TargetOid.Value > 0)
                    {
                        using var tgtTable = tgtLayer.GetTable();
                        if (tgtTable != null)
                        {
                            var qf = new QueryFilter { ObjectIDs = new[] { result.TargetOid.Value } };
                            using var cursor = tgtTable.Search(qf);
                            if (cursor.MoveNext())
                            {
                                using var feat = (Feature)cursor.Current;
                                var shape = feat.GetShape();
                                if (shape != null && !shape.IsEmpty)
                                {
                                    combinedExtent = combinedExtent != null
                                        ? combinedExtent.Union(shape.Extent)
                                        : shape.Extent;
                                    geometriesToFlash.Add(shape);
                                }
                            }
                        }
                    }

                    // 3. Zoom MapView and Flash feature geometries
                    if (combinedExtent != null && !combinedExtent.IsEmpty)
                    {
                        var expanded = combinedExtent.Expand(1.4, 1.4, true);
                        mapView.ZoomTo(expanded, TimeSpan.FromMilliseconds(350));

                        if (srcLayer != null && result.SourceOid > 0)
                        {
                            mapView.FlashFeature(srcLayer, result.SourceOid);
                        }

                        if (tgtLayer != null && result.TargetOid.HasValue && result.TargetOid.Value > 0)
                        {
                            mapView.FlashFeature(tgtLayer, result.TargetOid.Value);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to zoom to match result: {ex.Message}");
            }
        }

        public void ClearResults()
        {
            SelectedMatchResult = null;
            MatchResults.Clear();
            Summary = null;
            CanConfirmTransfer = false;
            StatusMessage = "Ready. Configure options and click Preview Matches.";
        }

        private TransferSettings BuildCurrentSettings()
        {
            return new TransferSettings
            {
                SourceLayerName = SelectedSourceLayerItem?.Name ?? _savedSourceLayerName,
                TargetLayerName = SelectedTargetLayerItem?.Name ?? _savedTargetLayerName,
                SourceLayerUri = SelectedSourceLayerItem?.LayerUri ?? _savedSourceLayerUri,
                TargetLayerUri = SelectedTargetLayerItem?.LayerUri ?? _savedTargetLayerUri,
                IgnoreThreshold = IgnoreThreshold,
                OverlapThreshold = OverlapThreshold,
                AmbiguityTolerance = AmbiguityTolerance,
                MatchingMethod = SelectedMatchingMethod,
                AttributeMappingEnabled = AttributeMappingEnabled,
                AttributeMappings = new ObservableCollection<AttributeMappingItem>(AttributeMappings),
                SkipPreview = SkipPreview
            };
        }

        private void SaveCurrentSettings()
        {
            SettingsService.SaveSettings(BuildCurrentSettings());
        }

        private void LoadSavedSettings()
        {
            var s = SettingsService.LoadSettings();
            _savedSourceLayerName = s.SourceLayerName ?? string.Empty;
            _savedTargetLayerName = s.TargetLayerName ?? string.Empty;
            _savedSourceLayerUri = s.SourceLayerUri ?? string.Empty;
            _savedTargetLayerUri = s.TargetLayerUri ?? string.Empty;
            _ignoreThreshold = s.IgnoreThreshold;
            _overlapThreshold = s.OverlapThreshold;
            _ambiguityTolerance = s.AmbiguityTolerance;
            _selectedMatchingMethod = s.MatchingMethod;
            _attributeMappingEnabled = s.AttributeMappingEnabled;
            _skipPreview = s.SkipPreview;
            if (s.AttributeMappings != null)
            {
                _attributeMappings = new ObservableCollection<AttributeMappingItem>(s.AttributeMappings);
            }
        }

        #endregion
    }
}
