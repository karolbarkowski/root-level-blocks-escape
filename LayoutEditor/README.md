# Block Studio · Layout editor

A separate, local web app for creating the predefined layouts accepted by `LevelGenerator`. No package installation, build step, external CDN, or backend service is needed. It uses native JavaScript modules and browser file APIs. The included Node server only serves files; imports, edits, validation, and exports run in the browser.

## Run

Requires Node.js 20 or newer:

```powershell
cd LayoutEditor
node server.mjs
```

Open **http://127.0.0.1:8770**. Alternatively, serve the `LayoutEditor` directory with any static HTTP server (for example `python -m http.server 8770 --bind 127.0.0.1`). Use HTTP rather than opening `index.html` as a file because the app uses ES modules.

Change the default port with the process environment variable `PORT`. `start.ps1` runs the same server from any working directory. An `npm start` alias is also provided, but npm is not required. Use the direct Node command if your global npm launcher is unavailable.

## Workflow

1. Create a **New layout**, or use **Open JSON** / file drag-and-drop. Layouts have widths and heights from 2 to 20.
2. **Draw walls** by clicking or dragging on the empty canvas. Close a boundary with walls and gates: enclosed floor turns white, while outside space stays pale and cannot hold blocks. Hold Shift while starting a stroke to erase, or use the Erase tool. Right-drag also erases.
3. Choose a **gate color** and **exit direction**, then drag a straight span across cells. Gates replace walls and can sit anywhere along the drawn boundary. The arrow points outward. Click a gate to edit its ID, color, direction, Line, Start and Length. For left/right gates, Line is the column and Start is the first row; for top/bottom gates, Line is the row and Start is the first column. Erase or Delete removes a whole gate.
4. Check the validation panel. Gate overlaps, duplicated IDs, out-of-bounds spans, an open boundary, and incorrectly oriented gates block export. No gates and disconnected floor areas are advisory warnings, not claims about solvability.
5. **Export JSON** downloads a layout to your browser's download location. Pass that file to the generator:

```powershell
# From the LevelGenerator folder; use the actual path where the browser saved the file.
dotnet bin/Release/net10.0/LevelGenerator.dll generate --layout "C:\path\to\my-layout.json" --coverage 80 --single-cell-ratio 0.4 --maxCandidates 1 --out output/custom
```

Build the generator with `LevelGenerator/build.ps1` before using enclosed layouts. Saving exports into `LevelGenerator/layouts/` keeps them next to the other fixed layouts. The editor does not execute the generator or save files directly into its folders. **Load example layout** reproduces the recessed shape with two inset gates.

## Import and export

Legacy rectangular files retain their existing geometry and use this form:

```json
{
  "width": 6,
  "height": 6,
  "walls": [{ "x": 2, "y": 2 }],
  "gates": [{ "id": "gate-1", "side": "top", "start": 0, "length": 3, "color": "red" }]
}
```

Opening a complete generated level also works: the editor extracts `board` and ignores blocks, solutions, and analysis. It does not overwrite the original level file. **Inspect JSON** lets you inspect, copy, or paste/import a document.

Malformed structures and unsupported layout fields are rejected. Repairable conflicts such as overlapping gates are retained and shown for correction; nothing is silently discarded. Custom color strings are preserved exactly and displayed with a stable fallback swatch. The filename and editor UI state are never included in generator JSON.

New layouts use `"boundaryMode": "enclosed"` and add `"line"` to each gate. See `examples/enclosed-layout.json` and `../LevelGenerator/FORMAT.md` for a complete example and geometry rules. Imported files without a boundary mode keep their legacy rectangle. The Boundary mode selector can convert a draft; review the gate positions and enclosure after conversion. Undo restores the prior mode. There is no block editor in this project.

## Editing conveniences

* Undo/redo retains up to 100 edits, with one drag stroke counted as one edit. Import, new layout, and resize are undoable too.
* Resizing preserves cell coordinates and gate start positions. Inset gates keep their Line coordinates; only legacy bottom/right gates follow the new canvas boundary. If shrinking removes out-of-bounds walls or gates, a confirmation shows the counts. Gates are removed whole rather than silently shortened.
* The last draft is saved in this browser's local storage and restored on reload. Storage is specific to the browser and origin; use Export for a durable, portable file. Undo history itself is not persisted.
* Board zoom, coordinate labels, named gate lists, and a generator-compatible example layout are included.
* Keyboard: **1** wall, **2** eraser, **3** gate, **4** select; **Ctrl/Cmd+Z** undo, **Ctrl/Cmd+Shift+Z** or **Ctrl+Y** redo, **Ctrl/Cmd+O** open, **Ctrl/Cmd+S** export, **Delete** selected gate. Native text-field undo is preserved.
* Keyboard canvas: focus a cell, use arrow keys to move, then Enter/Space to paint or erase. In gate mode, Enter places a one-cell gate at the focused cell; adjust its length in the inspector. Legacy layouts retain edge controls.

## Verification

```powershell
cd LayoutEditor
node --test tests/*.test.mjs
```

Tests cover real generator JSON round-trips, extracting board data from a generated level, schema failures, gate collisions and all side coordinates, painting constraints, resize behavior, undo/redo, disconnected-floor warnings, and custom colors. Integration fixtures are the files in `verification/`: a browser-exported enclosed layout, a legacy rectangular export and a generated legacy level.

`model.js` contains pure layout operations and validation. `app.js` contains the UI and SVG interaction. `styles.css` contains the responsive workspace styling. `server.mjs` is a small localhost-only static server.


