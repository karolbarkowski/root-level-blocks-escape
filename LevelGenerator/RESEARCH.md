# Generating crowded color-gate block puzzles

Research and implementation decision, 9 September 2026.

## Recommendation

Use **solvable reverse construction plus simulated annealing over initial positions, with a forward rules engine, opening-depth certification, and a small diversity archive**. Introduce special properties by modifying a known solution and replaying it. Export the board together with that solution as a certificate.

This is the chosen practical method for the supplied rules and the preference for difficult, space-constrained openings. It is not a claim that annealing universally produces the best puzzles. The accompanying comparison tests random reverse generation against the implemented hybrid; genetic algorithms, MCTS, and full MAP-Elites have not been implemented or benchmarked here.

## What the literature supports

**Guided construction is a credible starting point.** Kartal, Sohre and Guy generate Sokoban puzzles with Monte Carlo tree search and simulated gameplay, preserving solvability. They also use player studies to fit and validate difficulty features. The important transferable result is the combination of constructive solvability and an evaluated quality function; their human-difficulty correlations cannot simply be transferred to this game. [Data Driven Sokoban Puzzle Generation with Monte Carlo Tree Search, AIIDE 2016](https://ojs.aaai.org/index.php/AIIDE/article/view/12859)

**MCTS is not automatically the strongest generator.** Bhaumik, Khalifa, Green and Togelius compare tree search and optimization across Binary, Zelda, and Sokoban map generation. Optimization generally performs better in their experiments, with representation-dependent exceptions. This motivates testing a relatively inexpensive optimization baseline before investing in a more complicated construction tree. [Tree Search vs Optimization Approaches for Map Generation, AIIDE 2020](https://arxiv.org/abs/1903.11678)

**Variety deserves an explicit mechanism.** Mouret and Clune's MAP-Elites retains high-performing solutions across chosen feature dimensions. That is useful when the deliverable is a collection of puzzles rather than one highest-scoring board. The current implementation borrows the archive idea, using occupancy and mobility categories; it does not implement the complete MAP-Elites parent-selection/update workflow. [Illuminating search spaces by mapping elites, 2015](https://arxiv.org/abs/1504.04909)

**Solver difficulty and player difficulty are different targets.** Bento, Pereira and Lelis propose hardness measures and novelty-based exploration for generating Sokoban initial states, and report puzzles that are difficult for a specialized solver. This is evidence for search-guided generation, not proof of human enjoyment or useful misdirection. The present project therefore records solver effort but does not make it the principal quality objective. [Procedural Generation of Initial States of Sokoban, IJCAI 2019](https://www.ijcai.org/proceedings/2019/646)

These are related planning/puzzle domains, not empirical studies of the exact mechanics in this project. The recommendations below are engineering inferences tested only to the extent described in the local results.

## Alternatives considered

| Method | Strength | Main concern here | Decision |
|---|---|---|---|
| Random placement plus solver | Simple, explores arbitrary geometry | Many overlapping, deadlocked, or easy boards; solver cost spent on failures | Not the default |
| Random reverse construction | Cheap solvability witness | Long construction traces may simplify to one drag | Implemented baseline |
| Reverse construction plus annealing | Small legal mutations preserve a witness; accepts temporary regressions | Needs informative scoring and fresh starts | Implemented default |
| Genetic algorithm | Can search many structural alternatives | Crossover between packed boards needs a repair/encoding strategy | Defer until representation and quality targets are better established |
| MCTS | Can guide insertion and reverse-play decisions | Large branching factor; performance depends on representation and evaluation | A reasonable future comparison, not assumed superior |
| SAT/SMT/constraint programming | Express exact constraints and bounded solution requirements | Long turning drags and event-dependent properties complicate the encoding | Useful future option for small exact design requests |
| Full MAP-Elites | Explicit diversity across types of levels | Requires useful descriptors and a reliable candidate generator | Natural next extension of the existing archive |
| Learned generative model | Could learn style from a curated corpus | No training corpus; still needs a solver and validation | No current advantage justifying the complexity |

## Rules that change the algorithm

The state contains each piece's anchor and peeled-color count. A removed piece has no position. The sum of peeled colors is the global event counter; therefore separate freeze countdowns need not be stored in the search key. Shapes, axes, initial color stacks, walls, gates, and freeze thresholds are fixed within one level.

A legal move is a **complete drag and drop**, not one cell of translation. For each movable block, flood-fill all reachable anchors with other pieces treated as obstacles. Every reachable destination is a candidate drop. This collapses many possible mouse trajectories into the same successor and correctly treats turning around corners as one action. A Manhattan-distance heuristic would misrepresent this move cost.

At the destination, check the active color, gate span, allowed outward direction, and the swept silhouette. Clear at most one layer and update the event count. Merely passing a matching gate during flood fill does not trigger it.

Removal is irreversible in forward play, so an inverse construction step must restore a piece at a legal exit pose **and move it backward into the board as part of the same inverse action**. Treating insertion and that backward drag as independent normal moves creates incorrect intermediate assumptions about automatic removal. Every inverse event is checked against the forward transition.

## Implemented pipeline

1. Sample walls and boundary gates, or load a fixed layout.
2. Choose connected piece shapes, orientations, and gate-compatible colors.
3. Begin with a terminal configuration. Restore pieces through their gates, interleaving reverse shifts of existing pieces. Record the corresponding forward drops.
4. Reject incomplete constructions. Reverse the recorded action list and replay it to prove solvability.
5. Mutate initial positions by reversible non-clearing shifts. Prepend the inverse shifts to the witness. Annealing uses a gradually decreasing temperature; fresh constructions periodically replace the current search to explore new geometries.
6. Score opening difficulty, direct removability, occupancy, and mobility. Keep qualified candidates and a small archive of different descriptor bins.
7. With specials enabled, turn the last-removable block into gray; attempt an extra-layer detour; freeze late-used pieces; try axis restrictions. Replay every accepted modification. Recompute opening difficulty because adding a color can accidentally create an earlier exit.
8. Attempt A* to improve the solution and prove optimality. If its budget expires, retain the valid witness and mark it non-optimal.
9. Measure the opening more deeply, measure the first two physical removals, replay again, and export JSON/SVG/HTML.

The generation search currently uses a deliberately small ordinary-shape palette, including rotations of lines, squares, L, S, and T shapes plus single cells. The engine accepts other connected shapes too. Special-property attachment is conservative and does not promise every mechanic in every board.

## Quality objective and guarantees

The strongest current acceptance condition is a **proven lower bound on the number of drops before any removal event**. The default requires at least four. Breadth-first search proves this by exhausting all smaller-depth frontiers, accounting for all reachable drop positions. The program does not infer difficulty from the supplied witness.

A secondary penalty counts every initially directly removable piece and its exit poses. Without this term, all one-move openings receive the same depth score, leaving local search with little guidance toward eliminating the last easy exits. Occupancy rewards compact starts, and mobility favors a small number of initially movable blocks rather than arbitrary empty-cell percentage alone.

Layer peels and physical removals are tracked separately. Two peels may thaw pieces without freeing any cells, so the first-two-full-removals measurement better describes opening space. The distinct-piece count is currently a measurement on the returned solution, not a lower bound across every solution.

The search does **not** yet certify a difficult second half, the necessity of every special, unique meaningful solutions, or human surprise. Alternative solutions and commuting move orders are not automatically bad. A player study or a small curated preference set is the next useful way to tune these measures.

## Verification and practical scale

The rule tests cover turning drags, blocked paths, whole-width exits, concave-piece exit obstruction, both axis directions, freeze thresholds, layered pieces retaining their shape and position, passing a gate without dropping, gray blockers surviving victory, combined properties, and budget/unsolvability distinctions. Generated examples are checked again by the CLI verifier and their browser replays are inspected.

The 20×20 representation is supported, but exact search can grow exponentially with coupled pieces and mechanics. A 20×20 board is not inherently better or harder. The initial experiments use 6×6 boards with ten pieces, where the opening can be meaningfully analyzed and many complete solutions can be proven shortest within the available budgets.

The final browser viewer is a read-only solution inspector. It is not a replacement for the eventual game's input handling or a human playtest.

## Reference imagery

The official listings were inspected for visual context: [Block Slider: Color Jam](https://play.google.com/store/apps/details?id=and.brainworks.blockjam&hl=en_GB) and [Color Block Jam](https://play.google.com/store/apps/details?id=com.GybeGames.ColorBlockJam). They show irregular floor outlines, differently shaped blocks, and boundary gates. The project's rules follow the user's clarified specification. Recessed boundary gates seen in some reference images are supported through the optional `enclosed` boundary mode (see FORMAT.md).

See [RESULTS.md](../LevelGenerator.Tests/RESULTS.md) for the actual local measurements and benchmark commands.
