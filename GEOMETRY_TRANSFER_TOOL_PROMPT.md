# Geometry Transfer Tool — Master Specification & Professional System Prompt
> **ArcGIS Pro Add-in (Pro 3.x / .NET Framework / C# / WPF / CIM Architecture)**
> **Version:** 2.0 (Polyline-to-Polygon Geoprocessing & Dynamic Audit Edition)

---

## 1. System Identity & Core Purpose

You are tasked with designing, maintaining, and extending **Geometry Transfer Tool**, an enterprise-grade **ArcGIS Pro Add-in** built using the **ArcGIS Pro SDK for .NET** (C#, WPF, MVVM, and CIM).

The primary objective of this add-in is to provide a high-precision, automated workflow for GIS editors to transfer polygon geometries from a temporary or local **Source / Drawing Layer** to a master **Target Layer**, while guaranteeing that:
1. **The Source Layer is strictly 100% read-only** (never modified, locked, or deleted under any circumstance).
2. **The Target Layer geometry is updated with mathematical precision** based on spatial overlap matching.
3. **Full auditability is maintained** through dynamic Result Tables and Result Feature Classes with complete spatial audit fields.

---

## 2. Technical Architecture & Technology Stack

- **Host Platform:** ArcGIS Pro 3.x (compatible with 3.0+).
- **Runtime / Framework:** .NET 6 / .NET 8 (or .NET Framework 4.8 for legacy Pro 2.x compatibility).
- **Threading Model:** Strict separation between UI Thread (WPF Dispatcher) and ArcGIS Pro Main CIM Thread (MCT) using `QueuedTask.Run(...)`.
- **UI Framework:** WPF with XAML, MVVM architecture, custom Dark Glassmorphic Theme, dynamic ComboBox binding with Dispatcher-marshaled selection counting.
- **Geoprocessing & Engine:** Native ArcGIS Pro SDK `GeometryEngine`, `PolygonBuilderEx`, combined with silent background Geoprocessing execution (`GPExecuteToolFlags.None`).

---

## 3. Supported Input Layer Types & Rules

### Source / Drawing Layer (Read-Only)
- **Supported Geometries:**
  - **Polygon**: Direct topological validation, simplification (`SimplifyAsFeature`), and metric projection.
  - **Polyline**: Up to **400 features**. Must be converted into closed polygons before matching.
- **Polyline to Polygon Conversion Strategy (Dual-Layer Architecture):**
  1. **Primary (Geoprocessing Tool)**:
     - Silently executes `arcpy.management.FeatureToPolygon` on the active selection into a scratch dataset with `GPExecuteToolFlags.None` (preventing any automatic addition to the Map or Contents pane).
     - Handles complex multi-line parcel boundaries, shared edges, and overshoots.
  2. **In-Memory Fallback (SDK Geometry Engine)**:
     - Single lines with $\ge 3$ vertices: Auto-closed into a polygon ring.
     - Multi-line segments: Chained into closed cycles using graph segment assembly (`GeometryHelper.AssemblePolylinesToPolygons`) with vertex snap tolerance.
- **Selection Limit:**
  - **Polygon Source:** Maximum **60 features**.
  - **Polyline Source:** Maximum **400 features**.
  - **Exact Error Message:** `"Selection limit exceeded. Please select a maximum of 60 features ploygon or 400 features line in the Source Layer."`

### Target / Master Layer (Editable)
- **Supported Geometries:** Strictly **Polygon only**.
- **Selection Limit:** Maximum **60 features**.
- **Editability:** Verified before transfer using `targetLayer.CanEditData()`.

---

## 4. Safety & Safeguard Mechanisms

1. **Strict Read-Only Source Enforcement:**
   - No Edit Operations or cursors can ever target the Source Layer table or shape.
2. **HTTP Web Service Safeguard (From & To):**
   - Automatically detects if either the Source Layer or Target Layer is an ArcGIS Server / Feature Service (URL containing `http://` or `https://` or `FeatureService` / `MapServer`).
   - If detected, transfer is **blocked by default** with a clear UI safety warning.
   - Requires the user to explicitly enable the safeguard checkbox:
     `"Allow Transfer to / from Web Service (HTTP Source or Target)"`.
3. **Atomic Undoable Transactions:**
   - All target modifications are wrapped inside a single named `ArcGIS.Desktop.Editing.EditOperation` (`editOp.Name = "Geometry Transfer"`).
   - Allows instant undo (`Ctrl+Z`) and redo (`Ctrl+Y`) within ArcGIS Pro.
4. **Exclusion of Failed / Invalid Features:**
   - Any feature with `MatchStatus.Failed`, `MatchStatus.InvalidGeometry`, or `ConversionStatus == "Failed"` is strictly filtered out from the Preview DataGrid, never transferred to the Target Layer, and never inserted into the Results Feature Class.

---

## 5. Spatial Matching Engine

- **Spatial Reference Projection:**
  - Automatically projects Source and Target geometries to a common projected metric spatial reference (`GeometryHelper.GetCommonProjectedSpatialReference`) for true metric area computation ($m^2$).
- **Overlap Formula:**
  $$\text{Overlap \%} = \frac{\text{Area}(\text{Source} \cap \text{Target})}{\text{Area}(\text{Source})} \times 100$$
- **Candidate Evaluation:**
  - Envelope-first pre-filtering (`Intersects(env1, env2)`) for high-speed batch evaluation.
  - Planar geometric intersection (`GeometryEngine.Instance.Intersection`).
- **Global Conflict Resolution (One-to-One Greedy Matching):**
  - Resolves many-to-one and one-to-many overlaps by descending overlap percentage.
  - Enforces strict one-to-one matching between Source and Target.
- **Ambiguity Detection:**
  - Flags sources where the top two target candidate overlaps are within the user's `AmbiguityTolerance` (e.g. $\le 2\%$).
- **Ignore Threshold Mode:**
  - Option to bypass the percentage threshold and accept any positive spatial overlap ($> 0\%$).

---

## 6. Results Generation & Dynamic Audit Outputs

The tool generates two independent audit outputs in the project Default Geodatabase or user-specified workspace:

### 1. Results Standalone Table (`GeometryTransfer_Results_<timestamp>`)
Contains an audit record for every valid candidate match:
- `Match_ID` (String): Unique transaction code (e.g. `GT-M000001`).
- `Run_ID` (String): Unique batch run execution code.
- `Source_OID` (Long): Source feature ObjectID.
- `Target_OID` (Long): Matched Target feature ObjectID.
- `Match_Status` (String): `Matched`, `BelowThreshold`, `Ambiguous`, `TargetAlreadyMatched`, `NoIntersection`.
- `Transfer_Status` (String): `NotAttempted`, `Success`, `Failed`, `Skipped`.
- `Match_Method` (String): Matching algorithm description.
- `Overlap_Pct` (Double): Percentage overlap.
- `Threshold_Pct` (Double): Threshold used.
- `Candidate_Count` (Long): Number of intersecting target polygons.
- `Second_Best_Pct` (Double): Overlap percentage of the second-highest target candidate.
- `Source_Geometry_Type` (String): `Polygon` or `Polyline`.
- `Conversion_Status` (String): `None` or `Converted`.
- `Details` (String): Human-readable audit narrative (e.g. contributing line OIDs).
- `Run_Date` (Date): Timestamp of execution.

### 2. Results Feature Class (`GeometryTransfer_Results_FC_<timestamp>`)
- Creates a physical Polygon Feature Class containing the transferred/converted polygon geometries.
- Reuses the in-memory cached `WorkingPolygon` / `ResultGeometry` directly from the matching phase.
- Excludes any `Failed` or `InvalidGeometry` records.
- Automatically registered and added to the active ArcGIS Pro Map with default styling.

---

## 7. UI & UX Architecture (WPF DockPane)

- **Layout Structure:**
  1. **Header**: Professional branding with status badge.
  2. **Section 1: Layer Selection**:
     - Source ComboBox with real-time selection counting: `LayerName (X selected)`.
     - Target ComboBox with real-time selection counting: `LayerName (Y selected)`.
     - Web Service Warning & Safeguard Checkbox.
     - Read-only drawing layer guarantee banner.
  3. **Section 2: Matching Criteria**:
     - Overlap Percentage Slider (0–100%) with manual text box input.
     - "Ignore threshold" toggle.
     - Ambiguity Tolerance slider.
  4. **Section 3: Attribute Transfer (Optional)**:
     - Toggle checkbox with field mapping DataGrid (Source Field $\to$ Target Field).
  5. **Section 4: Action Controls**:
     - **Preview Matches** (Executes matching without writing changes).
     - **Transfer Geometries** (Executes modification on Target Layer).
     - **Create Results Table** (Standalone geodatabase table).
     - **Create Results Feature Class** (Spatial polygon feature class).
  6. **Section 5: Interactive Results DataGrid**:
     - Columns: `Match ID`, `Source OID`, `Target OID`, `Overlap %`, `Status`, `Transfer Status`.
     - **Zoom & Flash**: Clicking any row zooms the active map view and flashes both source and target geometries with color-coded halos.

---

## 8. Development Guidelines & Best Practices

1. **Threading Rules**:
   - Never call `Geoprocessing.ExecuteToolAsync` inside `QueuedTask.Run`.
   - Never touch WPF UI properties directly from `QueuedTask.Run`; marshal back via `System.Windows.Application.Current.Dispatcher`.
2. **Layer Item Equality**:
   - `LayerItem` must always implement `IEquatable<LayerItem>`, `Equals`, and `GetHashCode` based on `FeatureLayer` instance reference to prevent WPF ComboBoxes from resetting selections during collection updates.
3. **Silent Geoprocessing**:
   - Always pass `GPExecuteToolFlags.None` when executing temporary background tools (`management.FeatureToPolygon`, `management.Delete`) to prevent spurious map layer events.
4. **Zero Code Smells**:
   - Release COM/MCT cursors using `using` blocks (`using var cursor = table.Search(...)`).
   - Guard all coordinate operations against null spatial references.

---

## 9. Ready-to-Use Developer Prompt (Copy & Paste)

```markdown
Act as a Senior ArcGIS Pro SDK (.NET / C#) Architect.
Develop or modify the "Geometry Transfer Tool" ArcGIS Pro Add-in according to the following specifications:
- Source Layer: Local Drawing Layer (Polygon or Polyline). Polyline inputs must be converted into polygons using FeatureToPolygon logic (up to 400 line features). Must remain 100% read-only.
- Target Layer: Polygon layer (up to 60 features).
- Matching: Metric polygon overlap percentage with one-to-one conflict resolution and ambiguity detection.
- Transfer: Geometry-only transfer by default into Target Layer using EditOperation; never modify Source Layer.
- Web Service Protection: Detect HTTP/HTTPS services and block transfer unless user checks the web service override.
- Output: Dynamic Results Table and Results Feature Class with full audit attributes. Exclude Failed and InvalidGeometry records from all outputs.
- UI: WPF DockPane with real-time selection counts, Dispatcher-marshaled updates, and Zoom/Flash feature interaction.
```
