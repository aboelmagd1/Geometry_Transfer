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

7. **Extensible Architecture**:
   - Built on clean MVVM and service architecture with extensible matching strategies.

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
│   ├── Models/                                # Data models, settings, summary KPIs
│   ├── Helpers/                               # Spatial utilities, reprojection, logging
│   └── Images/                                # Add-in and ribbon icons (16x16, 32x32)
│
├── GeometryTransferTool.sln                   # Visual Studio Solution file
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
3. Build the solution (**Ctrl + Shift + B**).

---

## 📖 User Workflow

1. **Select Features**:
   - Select source polygon features from your editing layer.
   - Select candidate target polygon features from your target layer.
2. **Open Tool**:
   - Go to the **Geometry Tools** tab on the ribbon and click **Transfer Geometry**.
3. **Configure Parameters**:
   - Select **Source Layer** and **Target Layer**.
   - Set **Minimum Overlap Threshold** (e.g. `80%`).
   - Set **Ambiguity Tolerance** (e.g. `2.0%`).
4. **Preview Matches**:
   - Click **1. Preview Matches** to review match candidates and statistics in the grid.
5. **Confirm & Transfer**:
   - Click **2. Confirm & Transfer** to execute geometry updates safely with full undo (`Ctrl+Z`) support.
