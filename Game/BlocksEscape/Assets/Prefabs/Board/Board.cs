using System;
using System.Collections.Generic;
using BlocksEscape.Levels;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksEscape
{
    // Flat board surface spanning X Tiles by Y Tiles cells.
    // The pivot is the board's corner: tiles extend along +X and +Z from the GameObject's position,
    // so the tile edges line up with the editor grid. The surface is a child quad scaled to fit,
    // keeping the board root unscaled for anything parented to it later.
    //
    // Level cell (x, y) has y growing downwards, so level row 0 is the far (+Z) edge of the board.
    // Initialize also creates the level's BoardRules (the live game state); in Play mode the level is rebuilt on Start,
    // and Drop applies a move to both the rules and the spawned blocks.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Board : MonoBehaviour
    {
        public const int MaxTiles = 20; // block-puzzle board limit
        const string LevelRootName = "Level"; // root of everything Initialize spawns

        [SerializeField, Range(1, MaxTiles)] int xTiles = 8;
        [SerializeField, Range(1, MaxTiles)] int yTiles = 8;
        [SerializeField] Transform surface;

        [Header("Level")]
        [SerializeField] TextAsset level;
        [SerializeField] TextAsset blockTypes; // Packages/com.rootlevel.blocktypes/block-types.json
        [SerializeField] BlockTypeMaterials blockTypeMaterials;
        [SerializeField] GameObject wallPrefab;
        [SerializeField] GateCell gatePrefab;
        [SerializeField] Block[] blockPrefabs = Array.Empty<Block>(); // one per shape; rotations are matched, mirror images are not
        [SerializeField, HideInInspector] Transform content; // everything spawned by Initialize

        BoardRules _rules;
        Block[] _blocks = Array.Empty<Block>(); // spawned block per level block index; null when skipped or removed
        BlockTypeSet _types;

        public int XTiles
        {
            get => xTiles;
            set { xTiles = Mathf.Clamp(value, 1, MaxTiles); UpdateSurface(); }
        }

        public int YTiles
        {
            get => yTiles;
            set { yTiles = Mathf.Clamp(value, 1, MaxTiles); UpdateSurface(); }
        }

        public TextAsset Level
        {
            get => level;
            set => level = value;
        }

        public TextAsset BlockTypes
        {
            get => blockTypes;
            set => blockTypes = value;
        }

        public GameObject WallPrefab => wallPrefab;
        public GateCell GatePrefab => gatePrefab;
        public IReadOnlyList<Block> BlockPrefabs => blockPrefabs;
        public Transform Surface => surface;
        public Transform Content => content;
        public Vector2 Size => new Vector2(xTiles, yTiles) * GridUnits.CellSize;

        public BoardRules Rules => _rules;

        // Raised after a level has been spawned.
        public event Action Initialized;

        // Raised when the last typed block has left the board.
        public event Action Completed;

        public Block BlockAt(int index) => index >= 0 && index < _blocks.Length ? _blocks[index] : null;

        // Center of a level cell on the board surface, in the board's local space.
        public Vector3 CellToLocal(int x, int y) => LevelToLocal(new Vector2(x, y));

        // Level coordinates (x right, y down, cell centers on whole numbers) to the board's local space, on the surface.
        public Vector3 LevelToLocal(Vector2 p) =>
            new Vector3((p.x + 0.5f) * GridUnits.CellSize, 0f, (yTiles - p.y - 0.5f) * GridUnits.CellSize);

        public Vector2 LocalToLevel(Vector3 local) =>
            new Vector2(local.x / GridUnits.CellSize - 0.5f, yTiles - 0.5f - local.z / GridUnits.CellSize);

        // Local position of block i resting with its level position (anchor) at the given, possibly fractional, cell.
        public Vector3 BlockLocalPosition(int i, Vector2 anchor) =>
            LevelToLocal(anchor + _blocks[i].PivotCell) + Vector3.up * _blocks[i].PivotHeight;

        public void Initialize() => Initialize(LevelData.Parse(level != null ? level.text : throw new InvalidOperationException("No level assigned.")));

        // Clears previously spawned content, resizes the board to the level and spawns its walls, gates and blocks.
        public void Initialize(LevelData data)
        {
            if (data?.board == null || data.board.width < 1 || data.board.height < 1 ||
                data.board.width > MaxTiles || data.board.height > MaxTiles)
                throw new ArgumentException("Level has no board or an unsupported board size.");
            if (blockTypes == null) throw new InvalidOperationException("No block types assigned.");
            var types = BlockTypeSet.Parse(blockTypes.text);
            // Check every type before clearing, so a bad level leaves the current board untouched.
            foreach (var gate in data.board.gates ?? Array.Empty<GateData>()) types.Get(gate.type);
            foreach (var block in data.blocks ?? Array.Empty<BlockData>()) types.ColorOf(block.types);
            var rules = new BoardRules(data);

            Clear();
            _rules = rules;
            _types = types;
            XTiles = data.board.width;
            YTiles = data.board.height;

            content = NewChild(LevelRootName, transform);
            var walls = NewChild("Walls", content);
            if (wallPrefab != null)
            {
                foreach (var cell in data.board.walls ?? Array.Empty<CellData>())
                {
                    var wall = Spawn(wallPrefab, walls);
                    wall.name = $"Wall {cell.x},{cell.y}";
                    wall.transform.localPosition = CellToLocal(cell.x, cell.y);
                    RecordPrefabInstanceChanges(wall.transform);
                }
            }

            var gates = NewChild("Gates", content);
            if (gatePrefab != null)
            {
                foreach (var gate in data.board.gates ?? Array.Empty<GateData>())
                {
                    var type = types.Get(gate.type);
                    var gateRoot = NewChild($"Gate {gate.id} ({type.Name}, {gate.side})", gates);
                    var rotation = Quaternion.Euler(0f, OutwardYaw(gate.side), 0f);
                    foreach (var cell in data.GateCells(gate))
                    {
                        var gateCell = Spawn(gatePrefab.gameObject, gateRoot).GetComponent<GateCell>();
                        gateCell.name = $"Gate Cell {cell.x},{cell.y}";
                        gateCell.transform.localPosition = CellToLocal(cell.x, cell.y);
                        gateCell.transform.localRotation = rotation;
                        gateCell.Setup(gate.id, type);
                        RecordPrefabInstanceChanges(gateCell.transform);
                        RecordPrefabInstanceChanges(gateCell);
                    }
                }
            }

            var blocks = NewChild("Blocks", content);
            _blocks = new Block[rules.BlockCount];
            for (int i = 0; i < _blocks.Length; i++)
            {
                var blockData = rules.Data(i);
                if (!TryFindBlockPrefab(blockData, out var prefab, out int quarterTurns, out var pivotCell))
                {
                    Debug.LogWarning($"No block prefab matches the shape of block {blockData.id}; it was skipped.", this);
                    continue;
                }

                var block = Spawn(prefab.gameObject, blocks).GetComponent<Block>();
                block.name = $"Block {blockData.id} ({prefab.name})";
                block.Setup(blockData, pivotCell, types, blockTypeMaterials);
                _blocks[i] = block;
                block.transform.localPosition = BlockLocalPosition(i, rules.Anchor(i));
                block.transform.localRotation = Quaternion.Euler(0f, quarterTurns * 90f, 0f);
                RecordPrefabInstanceChanges(block.transform);
                RecordPrefabInstanceChanges(block);
            }

            Initialized?.Invoke();
        }

        // Drops block i at a whole-cell anchor: a matching gate peels its outer type or removes it from the board.
        public DropResult Drop(int i, Vector2Int anchor)
        {
            var result = _rules.Drop(i, anchor);
            var block = _blocks[i];
            if (block != null)
            {
                block.transform.localPosition = BlockLocalPosition(i, anchor);
                if (result == DropResult.Peeled) block.SetTypes(_rules.RemainingTypes(i), _types, blockTypeMaterials);
                if (result == DropResult.Removed)
                {
                    _blocks[i] = null;
                    if (Application.isPlaying) Destroy(block.gameObject);
                    else DestroyImmediate(block.gameObject);
                }
            }
            if (result != DropResult.Moved && _rules.Solved)
            {
                Debug.Log("Level complete.", this);
                Completed?.Invoke();
            }
            return result;
        }

        bool TryFindBlockPrefab(BlockData data, out Block prefab, out int quarterTurns, out Vector2Int pivotCell)
        {
            foreach (var candidate in blockPrefabs)
            {
                if (candidate != null && candidate.TryMatch(data.shape, out quarterTurns, out pivotCell))
                {
                    prefab = candidate;
                    return true;
                }
            }
            prefab = null;
            quarterTurns = 0;
            pivotCell = default;
            return false;
        }

        // Yaw that turns a gate cell's local +Z to the gate's outward side. Level "top" is the board's +Z edge.
        static float OutwardYaw(string side) => side switch
        {
            "top" => 0f,
            "right" => 90f,
            "bottom" => 180f,
            _ => 270f,
        };

        // Every spawned level root under the board. Normally just Content, but an undo can restore an old root
        // without its reference; those are found by name so they are never left behind as a duplicate level.
        public List<GameObject> LevelRoots()
        {
            var roots = new List<GameObject>();
            foreach (Transform child in transform)
                if (child == content || child.name == LevelRootName) roots.Add(child.gameObject);
            return roots;
        }

        public void Clear()
        {
            foreach (var root in LevelRoots())
            {
                if (Application.isPlaying) Destroy(root);
                else DestroyImmediate(root);
            }
            content = null;
            _rules = null;
            _blocks = Array.Empty<Block>();
        }

        static Transform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void RecordPrefabInstanceChanges(Object instance)
        {
#if UNITY_EDITOR
            // Values changed by script on a prefab instance are only saved once recorded as overrides.
            if (!Application.isPlaying) UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
#endif
        }

        static GameObject Spawn(GameObject prefab, Transform parent)
        {
#if UNITY_EDITOR
            // Keep the prefab link when building in the editor, so prefab edits propagate.
            if (!Application.isPlaying)
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
            return Object.Instantiate(prefab, parent, false);
        }

        void OnEnable() => UpdateSurface();

        // The spawned level in the scene has no live state after loading, so Play rebuilds it from the level file.
        void Start()
        {
            if (Application.isPlaying && level != null) Initialize();
        }

        void OnValidate() => UpdateSurface();

        void UpdateSurface()
        {
            if (surface == null) return;
            var size = Size;
            surface.localPosition = new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
            surface.localRotation = Quaternion.Euler(90f, 0f, 0f); // built-in quad faces -Z; this turns it face-up
            surface.localScale = new Vector3(size.x, size.y, 1f);
        }

        void OnDrawGizmos()
        {
            // Tile lines, so the cells are visible on the plain surface in the Scene view.
            var cell = GridUnits.CellSize;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
            const float lift = 0.001f;
            for (int x = 0; x <= xTiles; x++)
                Gizmos.DrawLine(new Vector3(x * cell, lift, 0f), new Vector3(x * cell, lift, yTiles * cell));
            for (int y = 0; y <= yTiles; y++)
                Gizmos.DrawLine(new Vector3(0f, lift, y * cell), new Vector3(xTiles * cell, lift, y * cell));
        }
    }
}
