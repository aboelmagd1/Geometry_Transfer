# Geometry Transfer Tool – Smart Polygon Matching (v2 – Refined)

An **ArcGIS Pro Add-in** for intelligent, conflict-free polygon geometry transfer between editing/source layers and master/target layers.

---

## 🌟 Key Features

1. **Intelligent Polygon Overlap Matching**:
   - Computes exact geometric intersection area: `Source Overlap % = (Area(Source ∩ Target) / Area(Source)) * 100`.
   - Fast bounding box (envelope) pre-filtering to handle large selections efficiently.
   - Automatic reprojection to common projected/equal-area coordinate systems (no calculations in angular degrees).

2. **Global Conflict Resolution (§5a)**:
   - Evaluates all candidate pairs across the selection globally.
   - Sorts candidates descending by overlap percentage and greedily assigns 1-to-1 unique matches.
   - Prevents duplicate writes or double-overwrites on the same target feature.

3. **Ambiguity Detection (§8)**:
   - Detects if top candidates for a source polygon differ by less than the **Ambiguity Tolerance** (default 2%).
   - Flags ambiguous pairs and excludes them from automated transfer for safe manual review.

4. **Two-Phase Preview & Safe Transfer (§9a, §18)**:
   - **Phase 1 (Preview)**: Pure, read-only calculation in memory. Populates the detailed results table and summary KPIs.
   - **Phase 2 (Transfer)**: Executes transfers inside a single undoable `EditOperation` named `"Transfer Polygon Geometry"` (`QueuedTask.Run()`). The entire batch can be undone with a single `Ctrl+Z`.
   - Safe **Geometry-Only** mode by default (preserves all existing target attributes, OIDs, GlobalIDs, and editor tracking metadata).

5. **Optional Attribute Mapping (§15)**:
   - Map explicit source fields to target fields (e.g. `LP_ID -> LP_ID`, `Parcel_No -> Parcel_No`).
   - Automatically excludes system/read-only fields (`OID`, `GlobalID`, `Shape`, `Shape_Area`, etc.) from target selection.

6. **Selection Enforcement (§4)**:
   - Enforces 1–60 features per layer hard cap with descriptive blocking dialogs before execution.

7. **Extensible Matching Architecture (§16)**:
   - Implements `IMatchingStrategy` interface, allowing future matching algorithms (Centroid Distance, IoU/Symmetric Overlap, Attribute ID Matching).

8. **Diagnostic Logging (§21)**:
   - Rolling daily logs written to `%LOCALAPPDATA%\GeometryTransferTool\logs\`.

---

## 📂 Project Structure

```
Geometry Transfer & Smart Polygon Matching/
├── geometery tools/                           # Distribution folder containing the ArcGIS Pro Add-in package
│   └── GeometryTransferTool.esriAddInX        # The installable ArcGIS Pro Add-in
│
├── GeometryTransferTool/                      # Main Visual Studio C# Project (.NET 8.0 / ArcGIS Pro SDK)
│   ├── Config.daml                            # ArcGIS Pro DAML definition (Ribbon Tab: "Geometry Tools", Group, Button, DockPane)
│   ├── Module1.cs                             # Add-in Module singleton
│   ├── GeometryTransferButton.cs              # Ribbon button handler
│   ├── GeometryTransferDockPane.cs            # DockPane ViewModel (MVVM)
│   ├── GeometryTransferView.xaml              # Modern WPF UI
│   ├── GeometryTransferView.xaml.cs           # UI code-behind
│   ├── Services/                              # Business logic, conflict resolution, matching algorithms
│   ├── Models/                                # Data models, settings, summary KPIs
│   ├── Helpers/                               # Spatial utilities, reprojection, logging
│   └── Images/                                # Add-in and ribbon icons (16x16, 32x32)
│
├── scripts/                                   # Automation and maintenance scripts
│   ├── build_addin.ps1                        # Compiles solution & packages .esriAddInX to geometery tools/
│   ├── install_addin.ps1                      # Deploys .esriAddInX to ArcGIS Pro AddIns folders
│   ├── generate_icons.ps1                     # Recreates PNG icon assets
│   └── generate_test_data.py                  # ArcPy script to generate test scenarios in sample_data/
│
├── sample_data/                               # Geodatabase datasets for testing & validation
│   └── SampleGeometryTransferData.gdb/        # Test Geodatabase with pre-configured validation scenarios
│
├── tools/                                     # Developer utility projects
│   └── IconGen/                               # Standalone C# icon generation tool
│
├── build.bat                                  # 1-Click Windows shortcut to build & package the Add-in
├── install.bat                                # 1-Click Windows shortcut to install the Add-in
├── GeometryTransferTool.sln                   # Visual Studio 2022 Solution file
├── .gitignore                                 # Git ignore configuration for VS & ArcGIS Pro
└── README.md                                  # Comprehensive documentation
```

---

## 🚀 Installation

### Option 1: Direct Double-Click (Recommended)
Double-click [`GeometryTransferTool.esriAddInX`](file:///d:/Learning/Geometry%20Transfer%20&%20Smart%20Polygon%20Matching%20%28v2%20%E2%80%93%20Refined%29/geometery%20tools/GeometryTransferTool.esriAddInX) inside the `geometery tools/` folder. The **ESRI ArcGIS Pro Add-In Utility** will install it automatically.

### Option 2: 1-Click Batch Installer
Double-click `install.bat` in the root folder.

### Option 3: PowerShell Script
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install_addin.ps1
```

---

## 🧭 Ribbon Location

Once installed, open ArcGIS Pro:
- Look at the top ribbon menu for the dedicated **"Geometry Tools"** tab.
- Click the **"Transfer Geometry"** button inside the **Transfer** group to open the DockPane.
- *(Note: The tool is configured exclusively under the "Geometry Tools" tab and will not clutter the generic "Add-In" tab).*

---

## 🛠️ Building and Packaging

### 1-Click Build
Double-click `build.bat` in the root folder.

### Build via PowerShell
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_addin.ps1
```

### Build via Visual Studio
1. Open `GeometryTransferTool.sln` in Visual Studio 2022.
2. Select **Release** or **Debug** configuration.
3. Build the solution (**Ctrl + Shift + B**).

---

## 📖 Step-by-Step User Workflow

1. **Draw / Edit Features**: Edit or digitize new polygon shapes in your local source layer.
2. **Select Features**:
   - Select between 1 and 60 source polygon features.
   - Select between 1 and 60 candidate target polygon features.
3. **Open Tool**:
   - Go to the **Geometry Tools** tab on the ArcGIS Pro ribbon.
   - Click **Transfer Geometry** to open the dockpane.
4. **Configure Parameters**:
   - Confirm **Source Layer** and **Target Layer**.
   - Set **Minimum Overlap Threshold** (default: `80%`).
   - Set **Ambiguity Tolerance** (default: `2.0%`).
   - (Optional) Check **Enable Attribute Mapping** to map specific fields.
5. **Preview Matches**:
   - Click **1. Preview Matches**.
   - Inspect the KPI summary cards and the detailed results table.
6. **Confirm & Transfer**:
   - Click **2. Confirm & Transfer**.
   - The tool transfers geometries within a single undoable transaction.
   - The modified target features are highlighted and selected on the map.

---

## 🧪 Test Data & Scenarios

To regenerate the test geodatabase, run `scripts/generate_test_data.py` using ArcGIS Pro Python:
```powershell
& "C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe" .\scripts\generate_test_data.py
```

The sample dataset exercises all scenarios:
| Source OID | Target OID | Scenario | Expected Outcome |
|:---:|:---:|:---|:---|
| **101** | **501** | Clean high overlap (95%) | `Matched & Ready` -> `Transferred` |
| **102** | **502** | Clean high overlap (88%) | `Matched & Ready` -> `Transferred` |
| **103** | **503** | Low overlap (65% vs 80% threshold) | `Below Threshold` |
| **104** | **504** | Competing for Target 504 (92% overlap) | `Transferred` (Winner) |
| **105** | **504** | Competing for Target 504 (82% overlap) | `Target Already Matched` (Loser) |
| **106** | **505 / 506** | Overlaps 505 (83.0%) and 506 (82.1%) (diff 0.9% < 2%) | `Ambiguous` (Manual Review) |
