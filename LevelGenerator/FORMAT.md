# Level format: block-puzzle/v2

UTF-8 JSON. All coordinates and counts are integers. Property names and enum values use the exact camelCase spellings shown. The engine rejects unknown properties and versions. The JSON schema covers structure; the engine additionally checks overlaps, connected shapes, gate positions, and block types.

| Field | Meaning |
|---|---|
| `format` | `block-puzzle/v2` (v1 used color names; convert with `LevelGenerator.Tools migrate`) |
| `rules` | `grid-drag-drop/v1` |
| `seed` | Generator seed, provenance rather than identity |
| `board.width`, `board.height` | Dimensions, each 2–20 |
| `board.boundaryMode` | Optional: `rectangle` (legacy default) or `enclosed` |
| `board.walls` | Fixed wall cells; in enclosed mode these draw the level boundary |
| `board.gates` | Typed exit segments; enclosed gates occupy cells in the drawn boundary |
| `blocks` | Initial block definitions |
| `solution` | Replayable pickup/drop destinations, optional for a user-authored unsolved input |
| `analysis` | Optional generator/solver measurements, including `difficulty` (1–1000) and `difficultyFeatures`; gameplay must not depend on these |

### Block types

Gates and blocks refer to **block types** by id. The types are defined once in `Shared/BlockTypes/block-types.json` and shared by the generator, the layout editor and the game:

```json
{ "format": "block-types/v1", "types": [ { "id": "cow", "name": "Cow", "color": "#3373E6" } ] }
```

`id` is what level files store; `name` and the `#RRGGBB` `color` are for display. Readers ignore unknown fields, so the game can add visual data later. A level that uses an id missing from this file is rejected. To rename a type, change its id here, rebuild, and run `LevelGenerator.Tools rename-type <old> <new> <folders...>` on existing levels and layouts.

### Gates

A gate has a unique `id`, `side` (`top`, `right`, `bottom`, `left`), `start`, `length`, and `type`. For top/bottom, `start` is an X coordinate and length spans columns. For left/right, it is a Y coordinate and length spans rows. A gate accepts blocks whose active type equals its `type`. Gates cannot overlap on one side or face blocked cells.

### Drawn enclosures

Set `board.boundaryMode` to `enclosed`. Each gate must additionally supply an integer `line`: its column for left/right exits, or its row for top/bottom exits. `side` is the **outward direction**. For example, `{"id":"cow-left","side":"left","line":2,"start":6,"length":3,"type":"cow"}` occupies cells (2,6), (2,7), and (2,8), and removes matching blocks dropped immediately to its right. Gates replace wall cells; they cannot overlap walls or other gates.

The canvas has no implicit enclosing wall in this mode. Flood fill from its edges using four-way neighbors, treating walls and gate cells as barriers. Reachable empty cells are exterior; all remaining empty cells are playable floor. At least one floor cell must exist. Every gate cell must have floor immediately inward and exterior immediately outward (outside the canvas also counts as exterior). A leak opens the region to the exterior and makes it unplayable. Separate enclosed rooms are permitted, but generation still needs usable exits.

Blocks may never occupy or drag through exterior, walls, or gate cells. Dropping adjacent to a matching gate checks the full transverse span and outward silhouette. Exit motion crosses that gate; type peeling, freeze events and axis restrictions are unchanged. Gates stay in place after removal. Generation occupancy uses only playable floor.

See `../LayoutEditor/examples/enclosed-layout.json` for the recessed shape shown in the editor. Existing files that omit `boundaryMode` keep the rectangular rules and must omit gate `line`. Older generator binaries reject these optional geometry fields, so rebuild before using enclosed exports.

### Blocks

A block has a unique `id`, `shape`, `position`, `types`, `axis`, and `frozenFor`. Each shape cell is relative to `position`; absolute cells are their componentwise sum. Shape coordinates have minimum X and Y zero. The origin of an S shape can be empty. Shapes do not rotate during play. `axis` is `free`, `horizontal`, or `vertical` (default free); `frozenFor` defaults to zero. `types` is ordered outside to inside, and every listed type needs a gate; an empty array means a neutral (gray) blocker that never leaves.

Example of a layered, frozen, horizontal-only block (a fragment, not a complete solvable level):

```json
{
  "id": "b7",
  "shape": [{"x":0,"y":0},{"x":1,"y":0}],
  "position": {"x":2,"y":3},
  "types": ["cow","pig"],
  "axis": "horizontal",
  "frozenFor": 2
}
```

The state initially has zero removal events and type index zero for every typed piece. After two removal events anywhere on the board, b7 can move in either horizontal direction. It first needs a cow exit and then a pig exit. Each successful drop peels one layer and contributes one event. Final removal frees its occupied cells.

A solution action is `{"block":"b7","to":{"x":0,"y":3}}`. This is one complete drag and drop. No elapsed time, path length, or gate ID is needed. Reconstruct a legal path by flood-filling the block's translations; apply the matching gate at the destination. Gate choice has no differing side effect.

Opening measurements have `exact` (nullable), `lowerBound`, `status`, and `visited`. Status is `exact`, `depthLimit`, `stateBudget`, `timeBudget`, or `unreachable`. For `unreachable`, there is no finite path to the target; the lower bound is not a substitute for that status. A budget stop never means unsolvable.
