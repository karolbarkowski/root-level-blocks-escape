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
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Board : MonoBehaviour
    {
        public const int MaxTiles = 20; // block-puzzle/v1 board limit

        [SerializeField, Range(1, MaxTiles)] int xTiles = 8;
        [SerializeField, Range(1, MaxTiles)] int yTiles = 8;
        [SerializeField] Transform surface;

        [Header("Level")]
        [SerializeField] TextAsset level;
        [SerializeField] GameObject wallPrefab;
        [SerializeField] GateCell gatePrefab;
        [SerializeField] Block[] blockPrefabs = Array.Empty<Block>(); // one per shape; rotations are matched, mirror images are not
        [SerializeField, HideInInspector] Transform content; // everything spawned by Initialize

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

        public GameObject WallPrefab => wallPrefab;
        public GateCell GatePrefab => gatePrefab;
        public IReadOnlyList<Block> BlockPrefabs => blockPrefabs;
        public Transform Surface => surface;
        public Transform Content => content;
        public Vector2 Size => new Vector2(xTiles, yTiles) * GridUnits.CellSize;

        // Raised after a level has been spawned.
        public event Action Initialized;

        // Center of a level cell on the board surface, in the board's local space.
        public Vector3 CellToLocal(int x, int y) =>
            new Vector3((x + 0.5f) * GridUnits.CellSize, 0f, (yTiles - y - 0.5f) * GridUnits.CellSize);

        public void Initialize() => Initialize(LevelData.Parse(level != null ? level.text : throw new InvalidOperationException("No level assigned.")));

        // Clears previously spawned content, resizes the board to the level and spawns its walls, gates and blocks.
        public void Initialize(LevelData data)
        {
            if (data?.board == null || data.board.width < 1 || data.board.height < 1 ||
                data.board.width > MaxTiles || data.board.height > MaxTiles)
                throw new ArgumentException("Level has no board or an unsupported board size.");

            Clear();
            XTiles = data.board.width;
            YTiles = data.board.height;

            content = NewChild("Level", transform);
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
                    var gateRoot = NewChild($"Gate {gate.id} ({gate.color}, {gate.side})", gates);
                    var rotation = Quaternion.Euler(0f, OutwardYaw(gate.side), 0f);
                    var color = LevelPalette.Get(gate.color);
                    foreach (var cell in GateCells(gate, data))
                    {
                        var gateCell = Spawn(gatePrefab.gameObject, gateRoot).GetComponent<GateCell>();
                        gateCell.name = $"Gate Cell {cell.x},{cell.y}";
                        gateCell.transform.localPosition = CellToLocal(cell.x, cell.y);
                        gateCell.transform.localRotation = rotation;
                        gateCell.Setup(gate.id, gate.color, color);
                        RecordPrefabInstanceChanges(gateCell.transform);
                        RecordPrefabInstanceChanges(gateCell);
                    }
                }
            }

            var blocks = NewChild("Blocks", content);
            foreach (var blockData in data.blocks ?? Array.Empty<BlockData>())
            {
                if (!TryFindBlockPrefab(blockData, out var prefab, out int quarterTurns, out var pivotCell))
                {
                    Debug.LogWarning($"No block prefab matches the shape of block {blockData.id}; it was skipped.", this);
                    continue;
                }

                var block = Spawn(prefab.gameObject, blocks).GetComponent<Block>();
                block.name = $"Block {blockData.id} ({prefab.name})";
                var cell = new Vector2Int(blockData.position.x, blockData.position.y) + pivotCell;
                block.transform.localPosition = CellToLocal(cell.x, cell.y) + Vector3.up * block.PivotHeight;
                block.transform.localRotation = Quaternion.Euler(0f, quarterTurns * 90f, 0f);
                block.Setup(blockData);
                RecordPrefabInstanceChanges(block.transform);
                RecordPrefabInstanceChanges(block);
            }

            Initialized?.Invoke();
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

        // Cells a gate occupies. Enclosed gates sit on their drawn line; legacy rectangular gates
        // belong to the board edge and are placed one cell outside it.
        public static IEnumerable<Vector2Int> GateCells(GateData gate, LevelData data)
        {
            bool enclosed = data.IsEnclosed;
            for (int n = gate.start; n < gate.start + gate.length; n++)
            {
                switch (gate.side)
                {
                    case "top": yield return new Vector2Int(n, enclosed ? gate.line : -1); break;
                    case "bottom": yield return new Vector2Int(n, enclosed ? gate.line : data.board.height); break;
                    case "left": yield return new Vector2Int(enclosed ? gate.line : -1, n); break;
                    default: yield return new Vector2Int(enclosed ? gate.line : data.board.width, n); break;
                }
            }
        }

        // Yaw that turns a gate cell's local +Z to the gate's outward side. Level "top" is the board's +Z edge.
        static float OutwardYaw(string side) => side switch
        {
            "top" => 0f,
            "right" => 90f,
            "bottom" => 180f,
            _ => 270f,
        };

        public void Clear()
        {
            if (content == null) return;
            if (Application.isPlaying) Destroy(content.gameObject);
            else DestroyImmediate(content.gameObject);
            content = null;
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
