# 🛠️ MACGREGOR FITTING TOOLS - USER GUIDE

Welcome to the **Fitting Tools** documentation. This comprehensive toolkit is designed to automate Inventor drawing extraction, centrally manage fitting libraries, export Bill of Materials (BOM), and place smart balloons in AutoCAD.

> **⚠️ The "Single Source of Truth" Rule:** > For geometry and quantities, **the CAD Drawing is the ultimate truth**. Do not manually edit quantities or Position Numbers in Excel. If changes are needed, update the CAD drawing (Re-scan, Sync Pos) and re-export the BOM.

The toolkit interface is divided into 5 main sections corresponding to the standard workflow:

---

## 1. FITTING EXTRACTION (STEP 1)
The first step to migrate geometric data from 3D models (Inventor) into the 2D CAD system.
* **[Import .idw files] Button:** Allows batch selection of Inventor drawings (`.idw` or `.dwg`). The tool automatically extracts geometries and metadata into intermediary JSON and DWG files.

## 2. FITTING FACTORY (STEP 2)
Processes the extracted raw data and transforms it into intelligent AutoCAD Blocks.
* **Set Target BOM Type:** Define the target BOM category before importing: **Panel (Structure)** or **Detail (Hull Matrix)**.
* **[Import .json files] Button:** Reads JSON files, automatically maps layers (Visible, Hidden, Center), aligns insertion points, and injects invisible Attributes (like `BOM_TYPE`, `PART_NUMBER`, `MASS`...) directly into the blocks.

## 3. FITTING LIBRARY (STEP 3)
The hub for searching, managing the central library, and defining new items.
* **Master Library & Project Library:** Distinct separation between company-wide standard items and project-specific items.
* **Add Virtual Items & Accessories:** Allows defining non-geometric items (Cables/Wires measured in meters) or virtual Accessories linked to Main Fittings. Supports multi-selection of blocks on CAD (e.g., combining -tv, -fv, -sv views) into a single Part ID.
* **[Insert to CAD] Button:** Inserts fittings from the library into the active drawing, automatically injecting the `POS_NUM` attribute.
* **[Push Update] Button:** Edit a fitting's geometry in the current drawing and push the updates back to overwrite the library file.

## 4. BOM EXPORT & BALLOONING (STEP 4)
The area for quantity takeoff and automated annotations. Open the **BOM EXPORT** window to operate:
* **Scan & Count from CAD:** The X-Ray algorithm recursively scans the drawing to count fittings. **The system automatically recognizes Parent-Child accessory links, calculates total lengths for virtual items, and smartly ignores auxiliary views (`-fv`, `-sv`, `-iso`) to prevent overcounting.**
* **[Auto-Assign Positions] Button:** Automatically generates sequential Position Numbers (e.g., 001, 002...) and groups accessories under their Parent Fitting.
* **[Sync Pos to CAD] Button:** Injects the Position Numbers from the BOM matrix back into the hidden `POS_NUM` attributes in CAD. Grouped items are merged (e.g., `001,002,003`).
* **[Place Smart Balloon] Button:** A 1-click annotation tool. Automatically generates **Stacked Balloons** for fittings with accessories and utilizes standard `_TagCircle` blocks.
* **[Mass Auto-Balloon] Button:** The ultimate batch-ballooning tool. Simply select a boundary, and the system filters fittings, removes duplicates, calculates magnetic spacing, and perfectly aligns stacked balloons along the margins without overlapping.
* **Excel Export:** Generates a clean BOM table with strict `00x` formatting and visual hierarchy (Accessories are indented directly beneath their Main Fitting).

## 5. BLOCK UTILITIES
Auxiliary tools to rapidly modify blocks directly in the drawing without Exploding them, preserving Attribute integrity:
* **Rename Block:** A native Command-Line utility to rename definitions or clone blocks. Automatically updates internal `MText` and supports multi-view grouping.
* **Redefine Blocks:** Synchronizes block definitions from the Library.
* **Replace Block:** Replaces existing blocks with new ones at their exact current coordinates.
* **Change Insertion Point:** Redefines the base point of a block.
* **Add / Extract Objects from Block:** Seamlessly move entities into or out of an existing block definition.