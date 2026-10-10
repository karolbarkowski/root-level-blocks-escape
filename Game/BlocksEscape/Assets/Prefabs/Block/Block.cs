using System;
using System.Collections.Generic;
using BlocksEscape.Levels;
using UnityEngine;
using UnityEngine.Serialization;

namespace BlocksEscape
{
    // A block on the board. The prefab declares which cells its mesh covers (its footprint), so the board
    // can match a level shape to a prefab in any of four 90° rotations; mirror images are separate prefabs.
    // Footprint cells are in level coordinates (x right, y down) relative to the cell under the pivot,
    // and the pivot must sit on that cell's center.
    // The block is drawn with its outermost type's material when that type has one (see BlockTypeMaterials);
    // otherwise the prefab's shared material is tinted with the type's color through a property block.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Block : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] Vector2Int[] footprint = { Vector2Int.zero };
        [SerializeField] string blockId;
        [SerializeField, FormerlySerializedAs("colors")] string[] types = Array.Empty<string>(); // outside to inside; empty = neutral
        [SerializeField] Color color = BlockTypeSet.NeutralColor;
        [SerializeField] Material material; // type material; null uses the prefab material, tinted
        [SerializeField] Material prefabMaterial;
        [SerializeField] Vector2Int pivotCell; // shape cell under the pivot, relative to the level position
        [SerializeField] string axis = "free";
        [SerializeField] int frozenFor;

        MaterialPropertyBlock _block;

        public IReadOnlyList<Vector2Int> Footprint => footprint;
        public string BlockId => blockId;
        public Vector2Int PivotCell => pivotCell;
        public IReadOnlyList<string> Types => types;
        public string Axis => axis;
        public int FrozenFor => frozenFor;

        // Height from the pivot down to the bottom of the mesh, so the block can be lifted to rest on the board.
        public float PivotHeight
        {
            get
            {
                var meshFilter = GetComponentInChildren<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) return 0f;
                var bounds = meshFilter.sharedMesh.bounds;
                var bottom = meshFilter.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                return transform.position.y - bottom.y;
            }
        }

        // Finds the rotation that turns this footprint into a level shape (cells relative to the block position).
        // quarterTurns is clockwise seen from above (yaw of 90° per turn); pivotCell is the shape cell under the pivot.
        public bool TryMatch(IReadOnlyList<CellData> shape, out int quarterTurns, out Vector2Int pivotCell)
        {
            quarterTurns = 0;
            pivotCell = default;
            if (shape == null || shape.Count != footprint.Length) return false;

            var target = new HashSet<Vector2Int>();
            foreach (var cell in shape) target.Add(new Vector2Int(cell.x, cell.y));
            var targetMin = Min(target);

            for (int turns = 0; turns < 4; turns++)
            {
                var rotated = new Vector2Int[footprint.Length];
                for (int i = 0; i < footprint.Length; i++) rotated[i] = Rotate(footprint[i], turns);
                var offset = targetMin - Min(rotated); // moves the rotated footprint onto the shape
                bool matches = true;
                foreach (var cell in rotated)
                    if (!target.Contains(cell + offset)) { matches = false; break; }
                if (!matches) continue;

                quarterTurns = turns;
                pivotCell = offset; // the pivot is footprint cell (0,0), which rotation leaves in place
                return true;
            }
            return false;
        }

        // Quarter turns clockwise seen from above, in level coordinates (y down): right -> down -> left -> up.
        static Vector2Int Rotate(Vector2Int cell, int quarterTurns)
        {
            for (int i = 0; i < quarterTurns; i++) cell = new Vector2Int(-cell.y, cell.x);
            return cell;
        }

        static Vector2Int Min(IEnumerable<Vector2Int> cells)
        {
            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            foreach (var cell in cells) min = Vector2Int.Min(min, cell);
            return min;
        }

        public void Setup(BlockData data, Vector2Int pivot, BlockTypeSet blockTypes, BlockTypeMaterials typeMaterials)
        {
            blockId = data.id;
            pivotCell = pivot;
            axis = string.IsNullOrEmpty(data.axis) ? "free" : data.axis;
            frozenFor = data.frozenFor;
            SetTypes(data.types, blockTypes, typeMaterials);
        }

        // Changes the remaining types, e.g. after a gate peeled the outer one; the look follows the new outer type.
        public void SetTypes(string[] remaining, BlockTypeSet blockTypes, BlockTypeMaterials typeMaterials)
        {
            types = remaining ?? Array.Empty<string>();
            color = blockTypes.ColorOf(types);
            material = typeMaterials != null && types.Length > 0 ? typeMaterials.Get(types[0]) : null;
            ApplyColor();
        }

        void OnEnable() => ApplyColor();

        void OnValidate() => ApplyColor();

        void ApplyColor()
        {
            var meshRenderer = GetComponentInChildren<MeshRenderer>();
            if (meshRenderer == null) return;
            if (prefabMaterial == null && meshRenderer.sharedMaterial != material) prefabMaterial = meshRenderer.sharedMaterial;
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
                meshRenderer.SetPropertyBlock(null);
                return;
            }
            if (prefabMaterial != null) meshRenderer.sharedMaterial = prefabMaterial;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            meshRenderer.SetPropertyBlock(_block);
        }

#if UNITY_EDITOR
        // Reads the footprint from the mesh: a cell belongs to the block when the mesh covers its center
        // seen from above. Run it after changing a block's mesh.
        [ContextMenu("Detect Footprint From Mesh")]
        public void DetectFootprint()
        {
            var meshFilter = GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;
            var mesh = meshFilter.sharedMesh;
            var toBlock = transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
            var scale = transform.lossyScale; // footprint is measured in board units, not the block's local units
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = Vector3.Scale(toBlock.MultiplyPoint3x4(vertices[i]), scale);
            var triangles = mesh.triangles;

            var cells = new List<Vector2Int>();
            float cell = GridUnits.CellSize;
            int reach = Board.MaxTiles;
            for (int y = -reach; y <= reach; y++)
            for (int x = -reach; x <= reach; x++)
                if (CoversFromAbove(vertices, triangles, x * cell, -y * cell)) // level y grows towards -Z
                    cells.Add(new Vector2Int(x, y));

            UnityEditor.Undo.RecordObject(this, "Detect Footprint");
            footprint = cells.ToArray();
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        static bool CoversFromAbove(Vector3[] v, int[] t, float px, float pz)
        {
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-6f) continue;
                float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d;
                float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d;
                if (l1 >= 0f && l2 >= 0f && l1 + l2 <= 1f) return true;
            }
            return false;
        }
#endif
    }
}
