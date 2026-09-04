# Geometry Transfer Tool – Smart Polygon Matching (v2)

An **ArcGIS Pro Add-in** for intelligent, conflict-free polygon geometry transfer between editing/source layers and target/master layers.

---

## 🌟 Key Features

1. **Intelligent Polygon Overlap Matching**:
   - Computes exact geometric intersection area: `Source Overlap % = (Area(Source ∩ Target) / Area(Source)) * 100`.
   - Fast bounding box (envelope) pre-filtering to handle selections efficiently.
   - Automatic reprojection to common projected coordinate systems.

2. **Global Conflict Resolution**:
   - Evaluates all candidate pairs across the selection globally.
   - Sorts candidates descending by overlap percentage and greedily assigns 1-to-1 unique matches.
   - Prevents duplicate writes or double-overwrites on the same target feature.

3. **Ambiguity Detection**:
   - Detects if top candidates for a source polygon differ by less than the **Ambiguity Tolerance** (default 2%).
   - Flags ambiguous pairs and excludes them from automated transfer for safe manual review.

4. **Two-Phase Preview & Safe Transfer**:
   - **Phase 1 (Preview)**: Pure, read-only calculation in memory. Populates the detailed results table and summary KPIs.
   - **Phase 2 (Transfer)**: Executes transfers inside a single undoable `EditOperation` named `"Transfer Polygon Geometry"`. The entire batch can be undone with a single `Ctrl+Z`.
   - Safe **Geometry-Only** mode by default (preserves all existing target attributes, OIDs, GlobalIDs, and editor tracking metadata).

5. **Optional Attribute Mapping**:
   - Map explicit source fields to target fields if desired.
   - Automatically excludes system/read-only fields (`OID`, `GlobalID`, `Shape`, `Shape_Area`, etc.) from target selection.

6. **Selection Enforcement**:
   - Enforces 1–60 features per layer cap with descriptive dialogs before execution.

8. **Generic, Schema-Agnostic Results Table (§19, §25, §26)**:
   - Creates a dedicated `GeometryTransfer_Results` standalone table in the selected Geodatabase (Target Workspace, Project Default GDB, or Custom GDB).
   - Core fields: `Match_ID`, `Run_ID`, `Source_OID`, `Target_OID`, `Match_Status`, `Transfer_Status`, `Match_Method`, `Overlap_Pct`, `Threshold_Pct`, `Candidate_Count`, `Second_Best_Pct`, `Details`, `Run_Date` (as Date).
   - Zero hard-coded business fields—works dynamically with any schema.
   - Automatically registers and integrates the table into the Active Map under **Standalone Tables** for immediate joins, relates, and inspection.
   - Optional `Transfer_Result_Attributes` table to snapshot dynamic user-selected fields.
   - Dedicated **"📝 Create Results Table"** button available after Preview or Transfer.

9. **Strict Source Layer Safety Guarantee (§3)**:
   - The Source / Drawing layer is strictly read-only.
   - Zero write operations, geometry modifications, or attribute edits are ever performed against the Source Layer.

---

## 📂 Project Structure

```
Geometry Transfer/
├── Addin_Package/                             # Pre-compiled ArcGIS Pro Add-in package
│   └── GeometryTransferTool.esriAddInX        # Direct installable Add-in
│
├── GeometryTransferTool/                      # Main C# Project (.NET 8.0 / ArcGIS Pro SDK)
│   ├── Config.daml                            # ArcGIS Pro DAML definition (Ribbon Tab, Group, Button, DockPane)
│   ├── Module1.cs                             # Add-in Module singleton
│   ├── GeometryTransferButton.cs              # Ribbon button handler
│   ├── GeometryTransferDockPane.cs            # DockPane ViewModel (MVVM)
│   ├── GeometryTransferView.xaml              # WPF UI
│   ├── GeometryTransferView.xaml.cs           # UI code-behind
│   ├── Services/                              # Business logic, conflict resolution, matching algorithms
│   │   ├── SelectionValidationService.cs      # Selection enforcement (1-60 features)
│   │   ├── LayerValidationService.cs          # Polygon geometry & editability checks
│   │   ├── GeometryMatchingService.cs         # Overlap evaluation & spatial projection
│   │   ├── ConflictResolutionService.cs       # 1-to-1 greedy resolution & ambiguity detection
│   │   ├── GeometryTransferService.cs         # Target-only atomic EditOperation updates
│   │   ├── TransferResultsTableService.cs     # Schema-agnostic results table & TOC integration
│   │   └── SettingsService.cs                 # Settings persistence
│   ├── Models/                                # Data models, settings, summary KPIs
│   ├── Helpers/                               # Spatial utilities, reprojection, logging
│   └── Images/                                # Add-in and ribbon icons (16x16, 32x32)
│
├── scripts/
│   └── build_addin.ps1                        # Build and package script
├── GeometryTransferTool.sln                   # Visual Studio Solution file
├── build.bat                                  # 1-click build batch file
├── .gitignore                                 # Git ignore configuration
└── README.md                                  # Documentation
```

---

## 🚀 Installation

### 1-Click Direct Installation
Double-click `GeometryTransferTool.esriAddInX` inside the `Addin_Package/` folder. The **ESRI ArcGIS Pro Add-In Utility** will install it automatically.

---

## 🧭 Ribbon Location

Once installed, open ArcGIS Pro:
- Look at the top ribbon menu for the **"Geometry Tools"** tab.
- Click the **"Transfer Geometry"** button inside the **Transfer** group to open the DockPane.

---

## 🛠️ Building from Source

1. Open `GeometryTransferTool.sln` in **Visual Studio 2022**.
2. Select **Release** configuration.
3. Build the solution (**Ctrl + Shift + B**), or run `build.bat`.

---

## 📖 User Workflow

1. **Select Features**:
   - Select source polygon features from your editing layer (1–60 features).
   - Select candidate target polygon features from your target layer (1–60 features).
2. **Open Tool**:
   - Go to the **Geometry Tools** tab on the ribbon and click **Transfer Geometry**.
3. **Configure Parameters**:
   - Select **Source Layer** and **Target Layer**.
   - Set **Minimum Overlap Threshold** (e.g. `80%`).
   - Set **Ambiguity Tolerance** (e.g. `2.0%`).
   - Configure Results Table output location (Project Default GDB, Target Workspace, or Custom GDB).
4. **Preview Matches**:
   - Click **1. Preview Matches** to calculate overlaps and review results in the DataGrid without modifying any layer.
5. **Confirm & Transfer**:
   - Click **2. Confirm & Transfer** to execute geometry updates on Target features safely with full undo (`Ctrl+Z`) support.
   - Source Layer remains 100% untouched.
6. **Results Table**:
   - Automatically generated on transfer or exported on-demand via **"📝 Create Results Table"**.
   - Added directly to the Map under **Standalone Tables** for easy joining back to feature layers.
