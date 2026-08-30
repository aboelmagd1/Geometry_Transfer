"""
Geometry Transfer Tool - Test Data Generator Script
Generates a File Geodatabase (SampleGeometryTransferData.gdb) in the sample_data directory
containing Source_Drawing_Layer and Target_Master_Layer specifically crafted to validate all scenarios:
1. Normal High-Overlap Match (Transferred)
2. Below-Threshold Overlap (Below Threshold)
3. Global Greedy Conflict Resolution (§5a) - Two source polygons competing for one target
4. Ambiguity Tolerance (§8) - One source polygon overlapping two targets within tolerance
"""

import os
import sys

def create_sample_gdb():
    try:
        import arcpy
    except ImportError:
        print("[WARNING] ArcPy not found in current Python environment. Please run with ArcGIS Pro Python (arcgispro-py3).")
        return

    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(script_dir)
    data_dir = os.path.join(project_root, "sample_data")
    
    if not os.path.exists(data_dir):
        os.makedirs(data_dir)

    gdb_name = "SampleGeometryTransferData.gdb"
    gdb_path = os.path.join(data_dir, gdb_name)

    if arcpy.Exists(gdb_path):
        arcpy.management.Delete(gdb_path)

    print(f"Creating File Geodatabase at: {gdb_path}")
    arcpy.management.CreateFileGDB(data_dir, gdb_name)

    # Spatial Reference: WGS 1984 Web Mercator (Auxiliary Sphere) (WKID: 3857)
    sr = arcpy.SpatialReference(3857)

    # Create Source and Target Feature Classes
    source_fc = os.path.join(gdb_path, "Source_Drawing_Layer")
    target_fc = os.path.join(gdb_path, "Target_Master_Layer")

    arcpy.management.CreateFeatureclass(gdb_path, "Source_Drawing_Layer", "POLYGON", spatial_reference=sr)
    arcpy.management.CreateFeatureclass(gdb_path, "Target_Master_Layer", "POLYGON", spatial_reference=sr)

    # Add attribute fields to test optional attribute mapping (§15)
    arcpy.management.AddField(source_fc, "LP_ID", "TEXT", field_length=50)
    arcpy.management.AddField(source_fc, "Parcel_No", "TEXT", field_length=50)
    arcpy.management.AddField(source_fc, "Notes", "TEXT", field_length=100)

    arcpy.management.AddField(target_fc, "LP_ID", "TEXT", field_length=50)
    arcpy.management.AddField(target_fc, "Parcel_No", "TEXT", field_length=50)
    arcpy.management.AddField(target_fc, "Status", "TEXT", field_length=50)

    # Helper function to create polygon from coordinates
    def make_poly(coords):
        arr = arcpy.Array([arcpy.Point(x, y) for x, y in coords])
        return arcpy.Polygon(arr, sr)

    # --- Scenario 1: Clean High Overlap (94% Source Overlap) ---
    # Target 501: (0,0) to (100,100)
    # Source 101: (5,5) to (100,100)
    t501 = make_poly([(0, 0), (0, 100), (100, 100), (100, 0), (0, 0)])
    s101 = make_poly([(5, 5), (5, 100), (100, 100), (100, 5), (5, 5)])

    # --- Scenario 2: Clean High Overlap (88% Source Overlap) ---
    # Target 502: (200,0) to (300,100)
    # Source 102: (210,0) to (300,100)
    t502 = make_poly([(200, 0), (200, 100), (300, 100), (300, 0), (200, 0)])
    s102 = make_poly([(200, 0), (200, 100), (312, 100), (312, 0), (200, 0)])

    # --- Scenario 3: Below Threshold (65% Overlap, Threshold is 80%) ---
    # Target 503: (400,0) to (500,100)
    # Source 103: (435,0) to (535,100)
    t503 = make_poly([(400, 0), (400, 100), (500, 100), (500, 0), (400, 0)])
    s103 = make_poly([(435, 0), (435, 100), (535, 100), (535, 0), (435, 0)])

    # --- Scenario 4 & 5: Global Greedy Conflict (§5a) ---
    # Single Target 504: (600,0) to (700,100)
    # Source 104: 92% overlap with Target 504 (Winning match)
    # Source 105: 82% overlap with Target 504 (Losing tie-break -> Target Already Matched)
    t504 = make_poly([(600, 0), (600, 100), (700, 100), (700, 0), (600, 0)])
    s104 = make_poly([(608, 0), (608, 100), (700, 100), (700, 0), (608, 0)])
    s105 = make_poly([(618, 0), (618, 100), (700, 100), (700, 0), (618, 0)])

    # --- Scenario 6: Ambiguity Detection (§8) ---
    # Source 106: Overlaps Target 505 (83.0%) and Target 506 (82.1%) with < 2% difference
    # Target 505: (800,0) to (883,100)
    # Target 506: (817,0) to (900,100)
    # Source 106: (800,0) to (900,100)
    t505 = make_poly([(800, 0), (800, 100), (883, 100), (883, 0), (800, 0)])
    t506 = make_poly([(817, 0), (817, 100), (900, 100), (900, 0), (817, 0)])
    s106 = make_poly([(800, 0), (800, 100), (900, 100), (900, 0), (800, 0)])

    # Insert Source Features
    with arcpy.da.InsertCursor(source_fc, ["SHAPE@", "LP_ID", "Parcel_No", "Notes"]) as cur:
        cur.insertRow([s101, "LP-101", "P-101", "Clean Match 1 (Expected ~95% Overlap)"])
        cur.insertRow([s102, "LP-102", "P-102", "Clean Match 2 (Expected ~88% Overlap)"])
        cur.insertRow([s103, "LP-103", "P-103", "Below Threshold (Expected ~65% Overlap)"])
        cur.insertRow([s104, "LP-104", "P-104", "Conflict Winner (Expected ~92% Overlap on Target 504)"])
        cur.insertRow([s105, "LP-105", "P-105", "Conflict Loser (Expected ~82% Overlap on Target 504)"])
        cur.insertRow([s106, "LP-106", "P-106", "Ambiguous Match (Difference < 2% between Targets 505 & 506)"])

    # Insert Target Features
    with arcpy.da.InsertCursor(target_fc, ["SHAPE@", "LP_ID", "Parcel_No", "Status"]) as cur:
        cur.insertRow([t501, "ORIG-501", "OLD-501", "Official"])
        cur.insertRow([t502, "ORIG-502", "OLD-502", "Official"])
        cur.insertRow([t503, "ORIG-503", "OLD-503", "Official"])
        cur.insertRow([t504, "ORIG-504", "OLD-504", "Official"])
        cur.insertRow([t505, "ORIG-505", "OLD-505", "Official"])
        cur.insertRow([t506, "ORIG-506", "OLD-506", "Official"])

    print("Sample test feature classes generated successfully in:", gdb_path)
    print("Source features count: 6")
    print("Target features count: 6")

if __name__ == "__main__":
    create_sample_gdb()
