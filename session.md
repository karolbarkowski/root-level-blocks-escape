# Next session: show layered (peelable) blocks

## Goal
A block with more than one type (`BlockData.types`, outside to inside, e.g. `["horse", "sheep"]`) leaves one layer per
matching gate. With abstract boxes that was easy to show; with animals it isn't. Make layers readable without
cluttering the board.

## Proposal: a rider on the back (recommended)
- A small head of the next inner animal rides on the outer animal's back, like a chick sitting on a horse.
- Each further layer adds another rider, stacked, so the count of layers is visible at a glance.
- When a gate peels the outer layer, the rider hops down and the block turns into that animal (the board already
  respawns peeled blocks with the new outer type's prefab: `Board.Drop` -> `SpawnBlock`).
- No new art per animal pair: reuse the 1x1 animal block models scaled down (the scenery already does this for the
  decor chicken and sheep: unpack the prefab, remove `Block` and the outline material).

### Where it would plug in
- `Block.ShowStatus` (Prefabs/Block/Block.cs) already draws the sleep bubble and axis arrow from `Board.RefreshStatus`;
  riders would be a third marker there. `Block.Types` holds the remaining types, outermost first.
- Rider placement: on the back, i.e. the tail cell (`Block.TailCell`), so it doesn't cover the face or the sleep
  bubble; the axis arrow currently also uses the tail cell, so one of them needs to move (e.g. arrow beside the rider).
- Rider models: a table type -> 1x1 prefab, from `BlockTypeModels` (`models.BlocksOf(type)` gives the block prefabs).
- Peel animation: in `Board.Drop`, on `DropResult.Peeled`, animate the rider jumping off before or while the new
  block spawns.

### Check
- Levels with layers: every level in Assets/Levels has exactly one two-layer block (e.g. 0249-simple-003: b5
  horse -> sheep). Render at phone size, then replay the stored solution in Play mode through the peel.

## Alternatives considered
- **Costume:** outer animal drawn as a costume with a seam and the inner animal's ears poking through. Charming,
  but needs art per animal pair and ears alone read less clearly.
- **Collar tag:** a small round tag in the inner animal's colour with its gate footprint print. Cheap and tidy, but
  abstract; players would have to learn it.

## Context from this session
- Frozen blocks sleep (eyes painted shut via `<Animal>_Sleep_Albedo.png`) with a wake counter bubble; axis-locked
  blocks carry a double arrow. Sizes live in `Prefabs/Block/BlockStatusArt.asset`.
- Art pipeline: `Game/BlocksEscape/Blender/animals.py` and `scenery.py`, then the Unity menus
  "Blocks Escape/Build Models From Blender" and "Build Scenery From Blender".
- Destroy is deferred in Play mode, and objects destroyed from the Unity MCP command runner during Play mode may not
  get removed at all: test board changes in Edit mode or detach-and-hide before destroying.
