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
        private readonly TransferResultsTableService _resultsTableService = new();

        private ObservableCollection<LayerItem> _layers = new();
        private ObservableCollection<LayerItem> _sourceLayers = new();
        private ObservableCollection<LayerItem> _targetLayers = new();
        private LayerItem? _selectedSourceLayerItem;
        private LayerItem? _selectedTargetLayerItem;

        private string _savedSourceLayerName = string.Empty;
        private string _savedTargetLayerName = string.Empty;
        private string _savedSourceLayerUri = string.Empty;
        private string _savedTargetLayerUri = string.Empty;
        private FeatureLayer? _savedSourceLayer;
        private FeatureLayer? _savedTargetLayer;
        private bool _isRefreshingLayers = false;

        private bool _ignoreThreshold = false;
        private double _overlapThreshold = 80.0;
        private double _ambiguityTolerance = 2.0;
        private string _selectedMatchingMethod = "Polygon Overlap Percentage";
        private bool _attributeMappingEnabled = false;
        private bool _skipPreview = false;

        private bool _allowWebServiceTransfer = false;
        private bool _createResultsTable = true;
        private bool _createResultsFeatureClass = false;
        private bool _isSourceWebService = false;
        private bool _isTargetWebService = false;
        private bool _isWebServiceDetected = false;
        private string _selectedOutputLocationOption = "Project Default Geodatabase";
        private string _customGdbPath = string.Empty;
        private bool _includeAttributeSnapshot = false;

        private bool _isBusy = false;
        private string _statusMessage = "Ready. Select polygon features in Source and Target layers.";
        private bool _canConfirmTransfer = false;

        private TransferSummary? _summary;
        private ObservableCollection<MatchResult> _matchResults = new();
        private ObservableCollection<AttributeMappingItem> _attributeMappings = new();

        private ObservableCollection<string> _availableSourceFields = new();
        private ObservableCollection<string> _availableTargetFields = new();
        private ObservableCollection<DynamicFieldItem> _dynamicSourceFields = new();

        public GeometryTransferDockPaneViewModel()
        {
            PreviewMatchesCommand = new AppRelayCommand(async () => await ExecutePreviewAsync(), () => !IsBusy);
            ConfirmTransferCommand = new AppRelayCommand(async () => await ExecuteTransferAsync(), () => !IsBusy && CanConfirmTransfer);
            CreateResultsTableCommand = new AppRelayCommand(async () => await CreateResultsTableAsync(), () => !IsBusy && CanCreateResultsTable);
            CreateResultsFeatureClassCommand = new AppRelayCommand(async () => await CreateResultsFeatureClassAsync(), () => !IsBusy && CanCreateResultsTable);
            RefreshLayersCommand = new AppRelayCommand(async () => await RefreshLayersAsync(), () => !IsBusy);
            ClearResultsCommand = new AppRelayCommand(() => ClearResults(), () => !IsBusy);
            AddMappingRowCommand = new AppRelayCommand(() => AddMappingRow());
            RemoveMappingRowCommand = new AppRelayCommand(param => RemoveMappingRow(param as AttributeMappingItem));

            MatchingMethods = new ObservableCollection<string>
            {
                "Polygon Overlap Percentage"
            };

            OutputLocationOptions = new ObservableCollection<string>
            {
                "Project Default Geodatabase",
                "Target Layer Workspace",
                "Custom Geodatabase"
            };

            LoadSavedSettings();
        }

        #region Properties

        public ObservableCollection<LayerItem> Layers
        {
            get => _layers;
            set => SetProperty(ref _layers, value);
        }

        /// <summary>
        /// Layers eligible as Source Layer: Polygon or Polyline (§34).
        /// </summary>
        public ObservableCollection<LayerItem> SourceLayers
        {
            get => _sourceLayers;
            set => SetProperty(ref _sourceLayers, value);
        }

        /// <summary>
        /// Layers eligible as Target Layer: strictly Polygon only (§34).
        /// </summary>
        public ObservableCollection<LayerItem> TargetLayers
        {
            get => _targetLayers;
            set => SetProperty(ref _targetLayers, value);
        }

        public LayerItem? SelectedSourceLayerItem
        {
            get => _selectedSourceLayerItem;
            set
            {
                if (SetProperty(ref _selectedSourceLayerItem, value))
                {
                    if (_isRefreshingLayers)
                    {
                        return;
                    }

                    if (value != null)
                    {
                        _savedSourceLayer = value.Layer;
                        _savedSourceLayerName = value.Name;
                        _savedSourceLayerUri = value.LayerUri;
                    }
                    _ = UpdateWebServicesStatusAsync();
                    _ = LoadFieldsForLayerAsync(value?.Layer, isSource: true);
                    _ = UpdateLayerSelectionCountsAsync();
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
                    if (_isRefreshingLayers)
                    {
                        return;
                    }

                    if (value != null)
                    {
                        _savedTargetLayer = value.Layer;
                        _savedTargetLayerName = value.Name;
                        _savedTargetLayerUri = value.LayerUri;
                    }
                    _ = UpdateWebServicesStatusAsync();
                    _ = LoadFieldsForLayerAsync(value?.Layer, isSource: false);
                    _ = UpdateLayerSelectionCountsAsync();
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

        public bool AllowWebServiceTransfer
        {
            get => _allowWebServiceTransfer;
            set
            {
                if (SetProperty(ref _allowWebServiceTransfer, value))
                {
                    NotifyPropertyChanged(nameof(CanConfirmTransfer));
                }
            }
        }

        public bool AllowWebServiceSourceTransfer
        {
            get => AllowWebServiceTransfer;
            set => AllowWebServiceTransfer = value;
        }

        public bool IsSourceWebService
        {
            get => _isSourceWebService;
            private set => SetProperty(ref _isSourceWebService, value);
        }

        public bool IsTargetWebService
        {
            get => _isTargetWebService;
            private set => SetProperty(ref _isTargetWebService, value);
        }

        public bool IsWebServiceDetected
        {
            get => _isWebServiceDetected;
            private set => SetProperty(ref _isWebServiceDetected, value);
        }

        public bool CreateResultsTable
        {
            get => _createResultsTable;
            set
            {
                if (SetProperty(ref _createResultsTable, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public bool CreateResultsFeatureClass
        {
            get => _createResultsFeatureClass;
            set
            {
                if (SetProperty(ref _createResultsFeatureClass, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public ObservableCollection<string> OutputLocationOptions { get; }

        public string SelectedOutputLocationOption
        {
            get => _selectedOutputLocationOption;
            set
            {
                if (SetProperty(ref _selectedOutputLocationOption, value))
                {
                    NotifyPropertyChanged(nameof(IsCustomGdbSelected));
                    SaveCurrentSettings();
                }
            }
        }

        public bool IsCustomGdbSelected => SelectedOutputLocationOption == "Custom Geodatabase";

        public string CustomGdbPath
        {
            get => _customGdbPath;
            set
            {
                if (SetProperty(ref _customGdbPath, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public bool IncludeAttributeSnapshot
        {
            get => _includeAttributeSnapshot;
            set
            {
                if (SetProperty(ref _includeAttributeSnapshot, value))
                {
                    SaveCurrentSettings();
                }
            }
        }

        public ObservableCollection<DynamicFieldItem> DynamicSourceFields
        {
            get => _dynamicSourceFields;
            set => SetProperty(ref _dynamicSourceFields, value);
        }

        public bool CanCreateResultsTable => MatchResults.Count > 0 && !IsBusy;

        public new bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    NotifyPropertyChanged(nameof(CanCreateResultsTable));
                }
            }
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
            set
            {
                if (SetProperty(ref _matchResults, value))
                {
                    NotifyPropertyChanged(nameof(CanCreateResultsTable));
                }
            }
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
        public ICommand CreateResultsTableCommand { get; }
        public ICommand CreateResultsFeatureClassCommand { get; }
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
            if (IsBusy) return;
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
            if (IsBusy) return;
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
            if (IsBusy) return;

            try
            {
                _isRefreshingLayers = true;
                var map = MapView.Active?.Map;
                if (map == null)
                {
                    // Do NOT clear collections or selections if MapView was just temporarily inactive!
                    return;
                }

                var (srcList, tgtList) = await QueuedTask.Run(() =>
                {
                    var sources = new List<LayerItem>();
                    var targets = new List<LayerItem>();
                    var allLayers = map.GetLayersAsFlattenedList().OfType<FeatureLayer>();
                    foreach (var fl in allLayers)
                    {
                        var shapeType = fl.ShapeType;
                        int selCount = fl.SelectionCount;

                        // Target must strictly be Polygon (§34)
                        if (shapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolygon)
                        {
                            var targetItem = new LayerItem(fl, selCount);
                            targets.Add(targetItem);
                            sources.Add(new LayerItem(fl, selCount));
                        }
                        // Source can also be Polyline (§34)
                        else if (shapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolyline)
                        {
                            sources.Add(new LayerItem(fl, selCount));
                        }
                    }
                    return (sources, targets);
                });

                var currentSourceLayer = SelectedSourceLayerItem?.Layer ?? _savedSourceLayer;
                string prevSourceUri = SelectedSourceLayerItem?.LayerUri ?? _savedSourceLayerUri;
                string prevSourceName = SelectedSourceLayerItem?.Name ?? _savedSourceLayerName;

                var currentTargetLayer = SelectedTargetLayerItem?.Layer ?? _savedTargetLayer;
                string prevTargetUri = SelectedTargetLayerItem?.LayerUri ?? _savedTargetLayerUri;
                string prevTargetName = SelectedTargetLayerItem?.Name ?? _savedTargetLayerName;

                // Match against current selection before replacing collections
                var matchedSource = srcList.FirstOrDefault(l =>
                    (currentSourceLayer != null && l.Layer == currentSourceLayer) ||
                    (!string.IsNullOrEmpty(prevSourceUri) && l.LayerUri.Equals(prevSourceUri, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(prevSourceName) && l.Name.Equals(prevSourceName, StringComparison.OrdinalIgnoreCase)));

                var matchedTarget = tgtList.FirstOrDefault(l =>
                    (currentTargetLayer != null && l.Layer == currentTargetLayer) ||
                    (!string.IsNullOrEmpty(prevTargetUri) && l.LayerUri.Equals(prevTargetUri, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(prevTargetName) && l.Name.Equals(prevTargetName, StringComparison.OrdinalIgnoreCase)));

                SourceLayers = new ObservableCollection<LayerItem>(srcList);
                TargetLayers = new ObservableCollection<LayerItem>(tgtList);
                Layers = new ObservableCollection<LayerItem>(tgtList); // keep backwards compatible

                // Restore selections reliably
                _selectedSourceLayerItem = matchedSource;
                NotifyPropertyChanged(nameof(SelectedSourceLayerItem));

                _selectedTargetLayerItem = matchedTarget;
                NotifyPropertyChanged(nameof(SelectedTargetLayerItem));

                if (matchedSource != null)
                {
                    _savedSourceLayer = matchedSource.Layer;
                    _savedSourceLayerName = matchedSource.Name;
                    _savedSourceLayerUri = matchedSource.LayerUri;
                }
                if (matchedTarget != null)
                {
                    _savedTargetLayer = matchedTarget.Layer;
                    _savedTargetLayerName = matchedTarget.Name;
                    _savedTargetLayerUri = matchedTarget.LayerUri;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to refresh layers", ex);
            }
            finally
            {
                _isRefreshingLayers = false;
            }
        }

        public async Task UpdateLayerSelectionCountsAsync()
        {
            try
            {
                // Query live selection counts on MCT
                var layerCounts = await QueuedTask.Run(() =>
                {
                    var counts = new Dictionary<string, int>();
                    var allLayers = SourceLayers.Select(s => s.Layer)
                        .Concat(TargetLayers.Select(t => t.Layer))
                        .Where(l => l != null);

                    foreach (var fl in allLayers)
                    {
                        try
                        {
                            string key = !string.IsNullOrEmpty(fl.URI) ? fl.URI : fl.Name;
                            counts[key] = fl.SelectionCount;
                        }
                        catch { }
                    }
                    return counts;
                });

                // Apply counts to all LayerItem instances on the UI thread
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    foreach (var item in SourceLayers)
                    {
                        if (item.Layer != null)
                        {
                            string key = !string.IsNullOrEmpty(item.Layer.URI) ? item.Layer.URI : item.Name;
                            if (layerCounts.TryGetValue(key, out int count))
                            {
                                item.SelectionCount = count;
                            }
                        }
                    }

                    foreach (var item in TargetLayers)
                    {
                        if (item.Layer != null)
                        {
                            string key = !string.IsNullOrEmpty(item.Layer.URI) ? item.Layer.URI : item.Name;
                            if (layerCounts.TryGetValue(key, out int count))
                            {
                                item.SelectionCount = count;
                            }
                        }
                    }

                    if (SelectedSourceLayerItem?.Layer != null)
                    {
                        string key = !string.IsNullOrEmpty(SelectedSourceLayerItem.Layer.URI) ? SelectedSourceLayerItem.Layer.URI : SelectedSourceLayerItem.Name;
                        if (layerCounts.TryGetValue(key, out int count))
                        {
                            SelectedSourceLayerItem.SelectionCount = count;
                        }
                    }

                    if (SelectedTargetLayerItem?.Layer != null)
                    {
                        string key = !string.IsNullOrEmpty(SelectedTargetLayerItem.Layer.URI) ? SelectedTargetLayerItem.Layer.URI : SelectedTargetLayerItem.Name;
                        if (layerCounts.TryGetValue(key, out int count))
                        {
                            SelectedTargetLayerItem.SelectionCount = count;
                        }
                    }

                    NotifyPropertyChanged(nameof(SelectedSourceLayerItem));
                    NotifyPropertyChanged(nameof(SelectedTargetLayerItem));
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
                var validation = await ValidateInputsAsync(requireTargetEditable: false);
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

                // Strictly filter out any Failed or InvalidGeometry features (§User Request)
                var filteredResults = results
                    .Where(r => r.MatchStatus != MatchStatus.Failed && 
                                r.MatchStatus != MatchStatus.InvalidGeometry && 
                                r.ConversionStatus != "Failed" &&
                                r.TransferStatus != TransferStatus.Failed)
                    .ToList();

                MatchResults = new ObservableCollection<MatchResult>(filteredResults);
                Summary = summary;
                CanConfirmTransfer = summary.MatchedCount > 0;
                NotifyPropertyChanged(nameof(CanCreateResultsTable));

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

            var validMatches = MatchResults.Where(r => r.CanTransfer && 
                                                       r.MatchStatus != MatchStatus.Failed && 
                                                       r.MatchStatus != MatchStatus.InvalidGeometry && 
                                                       r.ConversionStatus != "Failed").ToList();
            if (validMatches.Count == 0)
            {
                MessageHelper.ShowWarning("No confirmed matches are available to transfer.");
                return;
            }

            var validation = await ValidateInputsAsync(requireTargetEditable: true);
            if (!validation.IsValid)
            {
                MessageHelper.ShowWarning(validation.ErrorMessage);
                StatusMessage = validation.ErrorMessage;
                return;
            }

            // If not skipping preview, ask user confirmation
            if (!SkipPreview)
            {
                bool confirmed = MessageHelper.ShowQuestion(
                    $"Transfer geometry for {validMatches.Count} matched target polygon feature(s)?\n\nExisting target attributes will remain unchanged.\nThe Source Layer is strictly read-only and will never be modified.",
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

                CanConfirmTransfer = false;
                NotifyPropertyChanged(nameof(CanCreateResultsTable));

                // Optional automated Results Table generation (§25, §37)
                string tableNotice = string.Empty;
                if (CreateResultsTable)
                {
                    StatusMessage = "Generating generic Results Table in Geodatabase...";
                    var tableRes = await _resultsTableService.CreateAndPopulateResultsTableAsync(MatchResults, sourceLayer, targetLayer, settings);
                    if (tableRes.Success)
                    {
                        tableNotice = $"\n\nResults Table: '{tableRes.TableName}' created with {tableRes.RowCount} records.\nAdded to Map under Standalone Tables.";
                    }
                    else
                    {
                        tableNotice = $"\n\nResults Table notice: {tableRes.Message}";
                    }
                }

                // Optional automated Results Feature Class generation (§22)
                string fcNotice = string.Empty;
                if (CreateResultsFeatureClass)
                {
                    StatusMessage = "Generating polygon Results Feature Class in Geodatabase...";
                    var fcRes = await _resultsTableService.CreateAndPopulateResultsFeatureClassAsync(MatchResults, sourceLayer, targetLayer, settings);
                    if (fcRes.Success)
                    {
                        fcNotice = $"\n\nResults Feature Class: '{fcRes.DatasetName}' created with {fcRes.FeatureCount} features.\nAdded to Map as Feature Layer.";
                    }
                    else
                    {
                        fcNotice = $"\n\nResults Feature Class notice: {fcRes.ErrorMessage}";
                    }
                }

                StatusMessage = $"Transfer completed successfully. Transferred {transferredCount} feature(s).";
                MessageHelper.ShowInfo($"Transfer completed successfully!\n\nTransferred: {transferredCount} polygon(s).\nTarget attributes preserved.\nSource Layer remained 100% untouched.{tableNotice}{fcNotice}", "Geometry Transfer Complete");
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

        public async Task CreateResultsTableAsync()
        {
            if (IsBusy || MatchResults.Count == 0) return;

            IsBusy = true;
            StatusMessage = "Creating generic Results Table in Geodatabase...";

            try
            {
                var settings = BuildCurrentSettings();
                var sourceLayer = SelectedSourceLayerItem?.Layer;
                var targetLayer = SelectedTargetLayerItem?.Layer;

                var tableRes = await _resultsTableService.CreateAndPopulateResultsTableAsync(MatchResults, sourceLayer, targetLayer, settings);
                if (tableRes.Success)
                {
                    string msg = $"Results Table created successfully!\n\nTable Name: {tableRes.TableName}\nRecords: {tableRes.RowCount}\nLocation: {tableRes.TablePath}\n\nAdded to the Active Map under Standalone Tables.\nYou can join or relate it to feature layers using Source_OID, Target_OID, or attributes.";
                    MessageHelper.ShowInfo(msg, "Results Table Created");
                    StatusMessage = $"Results Table '{tableRes.TableName}' created successfully ({tableRes.RowCount} records).";
                }
                else
                {
                    MessageHelper.ShowWarning(tableRes.Message);
                    StatusMessage = tableRes.Message;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Create Results Table error", ex);
                MessageHelper.ShowError($"Failed to create Results Table: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task CreateResultsFeatureClassAsync()
        {
            if (IsBusy) return;
            if (MatchResults.Count == 0)
            {
                MessageHelper.ShowWarning("No matching results are available. Run Preview first.", "Create Results Feature Class");
                return;
            }

            var sourceLayer = SelectedSourceLayerItem?.Layer;
            var targetLayer = SelectedTargetLayerItem?.Layer;
            if (sourceLayer == null)
            {
                MessageHelper.ShowWarning("Please select a valid Source / Drawing Layer first.", "Create Results Feature Class");
                return;
            }

            IsBusy = true;
            StatusMessage = "Creating polygon Results Feature Class in Geodatabase...";

            try
            {
                var settings = BuildCurrentSettings();
                var fcRes = await _resultsTableService.CreateAndPopulateResultsFeatureClassAsync(MatchResults, sourceLayer, targetLayer, settings);
                if (fcRes.Success)
                {
                    string msg = $"Results Feature Class created successfully!\n\n" +
                                 $"Feature Class Name: {fcRes.DatasetName}\n" +
                                 $"Features: {fcRes.FeatureCount}\n" +
                                 $"Location: {fcRes.DatasetPath}\n\n" +
                                 $"Added to the Active Map as a Feature Layer.\nYou can symbolize and inspect the transferred polygon geometries.";
                    MessageHelper.ShowInfo(msg, "Results Feature Class Created");
                    StatusMessage = $"Results Feature Class '{fcRes.DatasetName}' created successfully ({fcRes.FeatureCount} features).";
                }
                else
                {
                    MessageHelper.ShowWarning(fcRes.ErrorMessage ?? "Failed to create Results Feature Class.", "Results Feature Class");
                    StatusMessage = fcRes.ErrorMessage ?? "Creation failed.";
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Create Results Feature Class error", ex);
                MessageHelper.ShowError($"Failed to create Results Feature Class: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<ValidationResult> ValidateInputsAsync(bool requireTargetEditable = false)
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
                // 1. Validate Selections and Layer Compatibility
                var selAndLayerValidation = await QueuedTask.Run(() =>
                {
                    int srcCount = srcLayer.SelectionCount;
                    int tgtCount = tgtLayer.SelectionCount;

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (SelectedSourceLayerItem != null) SelectedSourceLayerItem.SelectionCount = srcCount;
                        if (SelectedTargetLayerItem != null) SelectedTargetLayerItem.SelectionCount = tgtCount;
                    });

                    var selValidation = SelectionValidationService.ValidateSelections(srcLayer, tgtLayer, srcCount, tgtCount);
                    if (!selValidation.IsValid)
                    {
                        return selValidation;
                    }

                    var layerValidation = LayerValidationService.ValidateLayers(srcLayer, tgtLayer, requireTargetEditable);
                    if (!layerValidation.IsValid)
                    {
                        return layerValidation;
                    }

                    return ValidationResult.Success();
                });

                if (!selAndLayerValidation.IsValid)
                {
                    return selAndLayerValidation;
                }

                // 2. HTTP Web Service Transfer Safeguard (From & To)
                // Preview is allowed even if HTTP service and checkbox is off; transfer is strictly blocked
                if (requireTargetEditable)
                {
                    var httpValidation = await LayerValidationService.ValidateTransferWebServiceAsync(srcLayer, tgtLayer, AllowWebServiceTransfer);
                    if (!httpValidation.IsValid)
                    {
                        return httpValidation;
                    }
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                Logger.Error("Validation check error", ex);
                return ValidationResult.Fail($"Validation failed: {ex.Message}");
            }
        }

        private async Task UpdateWebServicesStatusAsync()
        {
            try
            {
                var src = SelectedSourceLayerItem?.Layer;
                var tgt = SelectedTargetLayerItem?.Layer;

                IsSourceWebService = await LayerValidationService.IsHttpServiceLayerAsync(src);
                IsTargetWebService = await LayerValidationService.IsHttpServiceLayerAsync(tgt);
                IsWebServiceDetected = IsSourceWebService || IsTargetWebService;

                NotifyPropertyChanged(nameof(CanConfirmTransfer));
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to update web services status: {ex.Message}");
            }
        }

        private async Task LoadFieldsForLayerAsync(FeatureLayer? layer, bool isSource)
        {
            if (layer == null)
            {
                if (isSource)
                {
                    AvailableSourceFields.Clear();
                    DynamicSourceFields.Clear();
                }
                else
                {
                    AvailableTargetFields.Clear();
                }
                return;
            }

            try
            {
                var (fieldNames, dynamicItems) = await QueuedTask.Run(() =>
                {
                    var list = new List<string>();
                    var dynList = new List<DynamicFieldItem>();
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

                            if (isSource)
                            {
                                if (fld.FieldType != FieldType.OID &&
                                    !fld.Name.Equals("Shape_Length", StringComparison.OrdinalIgnoreCase) &&
                                    !fld.Name.Equals("Shape_Area", StringComparison.OrdinalIgnoreCase))
                                {
                                    dynList.Add(new DynamicFieldItem
                                    {
                                        FieldName = fld.Name,
                                        FieldType = fld.FieldType.ToString(),
                                        Alias = fld.AliasName,
                                        IsSelected = false
                                    });
                                }
                            }
                            else
                            {
                                // For target, exclude non-editable / system fields (§15)
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
                    return (list, dynList);
                });

                if (isSource)
                {
                    AvailableSourceFields = new ObservableCollection<string>(fieldNames);
                    DynamicSourceFields = new ObservableCollection<DynamicFieldItem>(dynamicItems);
                }
                else
                {
                    AvailableTargetFields = new ObservableCollection<string>(fieldNames);
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
            NotifyPropertyChanged(nameof(CanCreateResultsTable));
            StatusMessage = "Ready. Configure options and click Preview Matches.";
        }

        private TransferSettings BuildCurrentSettings()
        {
            string locType = SelectedOutputLocationOption switch
            {
                "Target Layer Workspace" => "TargetWorkspace",
                "Custom Geodatabase" => "CustomGdb",
                _ => "ProjectDefaultGdb"
            };

            var selectedSnapshotFields = DynamicSourceFields?
                .Where(f => f.IsSelected)
                .Select(f => f.FieldName)
                .ToList() ?? new List<string>();

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
                CreateResultsTable = CreateResultsTable,
                CreateResultsFeatureClass = CreateResultsFeatureClass,
                AllowWebServiceTransfer = AllowWebServiceTransfer,
                AllowWebServiceSourceTransfer = AllowWebServiceTransfer,
                OutputLocationType = locType,
                CustomGdbPath = CustomGdbPath,
                IncludeAttributeSnapshot = IncludeAttributeSnapshot,
                SelectedSnapshotFields = selectedSnapshotFields,
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
            _createResultsTable = s.CreateResultsTable;
            _createResultsFeatureClass = s.CreateResultsFeatureClass;
            // Safety safeguard: intentionally defaults to false (§3, §27)
            _allowWebServiceTransfer = false;
            _selectedOutputLocationOption = s.OutputLocationType switch
            {
                "TargetWorkspace" => "Target Layer Workspace",
                "CustomGdb" => "Custom Geodatabase",
                _ => "Project Default Geodatabase"
            };
            _customGdbPath = s.CustomGdbPath ?? string.Empty;
            _includeAttributeSnapshot = s.IncludeAttributeSnapshot;
            _skipPreview = s.SkipPreview;
            if (s.AttributeMappings != null)
            {
                _attributeMappings = new ObservableCollection<AttributeMappingItem>(s.AttributeMappings);
            }
        }

        #endregion
    }
}
