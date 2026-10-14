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
    // and the pivot must sit on that cell's center. Each prefab is a model made for one block type (see BlockTypeModels).
    [DisallowMultipleComponent]
    public sealed class Block : MonoBehaviour
    {
        [SerializeField] Vector2Int[] footprint = { Vector2Int.zero };
        [SerializeField] string blockId;
        [SerializeField, FormerlySerializedAs("colors")] string[] types = Array.Empty<string>(); // outside to inside; empty = neutral
        [SerializeField] Vector2Int pivotCell; // shape cell under the pivot, relative to the level position
        [SerializeField] string axis = "free";
        [SerializeField] int frozenFor;
        [SerializeField] int quarterTurns; // rotation on the board, see TryMatch

        [Header("Status")]
        [Tooltip("Centre of the face on top, in footprint coordinates (level x right, y down).")]
        [SerializeField] Vector2 headCell;
        [SerializeField] Material awakeMaterial;
        [Tooltip("Same model with the eyes painted shut, shown while the block is frozen.")]
        [SerializeField] Material sleepMaterial;

        const string BubbleName = "Sleep Bubble", ArrowName = "Axis Arrow", TurnsName = "Turns", LayersName = "Layers";
        Transform _bubble;
        Vector3 _bubbleAnchor; // above the face, in block space; the bubble is offset from it on screen
        Vector2 _bubbleOffset;
        int _shownTurns = -1;

        float _pivotHeight = float.NaN; // cached in Play mode, where the mesh no longer changes

        public IReadOnlyList<Vector2Int> Footprint => footprint;
        public string BlockId => blockId;
        public Vector2Int PivotCell => pivotCell;
        public IReadOnlyList<string> Types => types;
        public string Axis => axis;
        public int FrozenFor => frozenFor;

        // Height from the pivot down to the bottom of the mesh, so the block can be lifted to rest on the board.
        // Read every frame while dragging, so in Play mode it is measured once (the lookup allocates).
        public float PivotHeight
        {
            get
            {
                if (!Application.isPlaying) return MeasurePivotHeight();
                if (float.IsNaN(_pivotHeight)) _pivotHeight = MeasurePivotHeight();
                return _pivotHeight;
            }
        }

        float MeasurePivotHeight()
        {
            var meshFilter = GetComponentInChildren<MeshFilter>(true);
            if (meshFilter == null || meshFilter.sharedMesh == null) return 0f;
            var bounds = meshFilter.sharedMesh.bounds;
            var bottom = meshFilter.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            return transform.position.y - bottom.y;
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

        // Shows the block's state: a frozen block sleeps (eyes shut) with a bubble over its head counting the layers that
        // still have to leave the board before it wakes; an axis-locked block carries an arrow along its axis.
        public void ShowStatus(int turnsToWake, string moveAxis, BlockStatusArt art)
        {
            var body = Body();
            if (body == null) return;
            if (awakeMaterial != null)
            {
                var materials = body.sharedMaterials;
                materials[0] = turnsToWake > 0 && sleepMaterial != null ? sleepMaterial : awakeMaterial;
                body.sharedMaterials = materials;
            }
            float top = body.bounds.max.y;

            _bubble = transform.Find(BubbleName);
            if (turnsToWake > 0 && art != null && art.SleepBubble != null)
            {
                if (_bubble == null)
                {
                    _bubble = Instantiate(art.SleepBubble, transform, false).transform;
                    _bubble.name = BubbleName;
                    _shownTurns = -1;
                }
                _bubble.localScale = Vector3.one * art.BubbleSize;
                var head = transform.TransformPoint(FootprintToLocal(headCell));
                _bubbleAnchor = transform.InverseTransformPoint(new Vector3(head.x, top + art.BubbleHeight, head.z));
                _bubbleOffset = art.BubbleScreenOffset;
                FaceCamera();
                if (_shownTurns != turnsToWake)
                {
                    var number = DigitText.Build(_bubble, TurnsName, turnsToWake, art.Digits, art.DigitHeight, 0.06f);
                    if (number != null) number.localPosition = art.DigitOffset;
                    _shownTurns = turnsToWake;
                }
            }
            else if (_bubble != null)
            {
                DestroyChild(_bubble);
                _bubble = null;
            }

            var arrow = transform.Find(ArrowName);
            bool locked = moveAxis == "horizontal" || moveAxis == "vertical";
            if (locked && art != null && art.AxisArrow != null)
            {
                if (arrow == null)
                {
                    arrow = Instantiate(art.AxisArrow, transform, false).transform;
                    arrow.name = ArrowName;
                }
                // On the cell farthest from the face; a one-cell block has only its face, so the arrow goes under the chin.
                var cell = TailCell();
                var local = FootprintToLocal(cell);
                float scale = 1f;
                if (footprint.Length == 1) { local += new Vector3(0f, 0f, -0.3f); scale = 0.6f; }
                var p = transform.TransformPoint(local);
                arrow.position = new Vector3(p.x, top - 0.02f, p.z);
                // Level x is world x, level y runs along world z; the model points along x.
                var board = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
                arrow.rotation = board * Quaternion.Euler(0f, moveAxis == "vertical" ? 90f : 0f, 0f);
                arrow.localScale = Vector3.one * scale;
            }
            else if (arrow != null) DestroyChild(arrow);
        }

        // Shows a block with several types as a stack of literal layers, outermost on top: one model per type, each
        // squashed to 1/n of the height so the stack is as tall as a single block. Like a stepped cake, each layer is
        // inset by rim (in cells, per side) from the one below, so every layer shows a ledge from above; the bottom one
        // keeps the full footprint. layers are full-size models of this block's shape (top first) already posed on the
        // board; they become children and lose their Block component.
        public void StackLayers(IReadOnlyList<GameObject> layers, float rim)
        {
            var own = GetComponent<MeshRenderer>();
            if (own == null || layers == null || layers.Count < 2) return;
            float bottom = own.bounds.min.y, height = own.bounds.size.y;
            int n = layers.Count;

            var root = new GameObject(LayersName).transform;
            root.SetParent(transform, false);
            for (int k = 0; k < n; k++)
            {
                var layer = layers[k].transform;
                layer.name = $"Layer {k} ({types[k]})";
                var cells = Vector2.one; // the layer's footprint extent along its own x and z, in cells
                if (layer.TryGetComponent<Block>(out var block))
                {
                    cells = block.Extent();
                    DestroyImmediate(block); // a layer is only a model
                }
                layer.SetParent(root, true);
                if (!layer.TryGetComponent<MeshRenderer>(out var mesh)) continue;
                // Animals differ in height, so each is squashed to exactly its share, resting on the layer below;
                // layer 0 ends up on top.
                var scale = layer.localScale;
                scale.y *= height / n / Mathf.Max(mesh.bounds.size.y, 1e-4f);
                // The inset shrinks the layer about its centre seen from above, the same distance on every side. It is
                // measured against the footprint, not the mesh, as ears and snouts make animals' bounds differ.
                var center = mesh.bounds.center;
                float inset = 2f * rim * (n - 1 - k);
                scale.x *= Mathf.Max(0.1f, 1f - inset / cells.x);
                scale.z *= Mathf.Max(0.1f, 1f - inset / cells.y);
                layer.localScale = scale;
                var shift = center - mesh.bounds.center;
                layer.position += new Vector3(shift.x, bottom + (n - 1 - k) * height / n - mesh.bounds.min.y, shift.z);
            }
            own.enabled = false; // the stack stands in for the block's own model
        }

        // Footprint size in cells along the block's own x and y (level y runs along -Z).
        Vector2 Extent()
        {
            var min = Min(footprint);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in footprint) max = Vector2Int.Max(max, cell);
            return max - min + Vector2Int.one;
        }

        // The model whose face is on top: the top layer of a stack, otherwise the block's own mesh.
        MeshRenderer Body()
        {
            var layers = transform.Find(LayersName);
            if (layers != null && layers.childCount > 0 && layers.GetChild(0).TryGetComponent<MeshRenderer>(out var top)) return top;
            return GetComponentInChildren<MeshRenderer>();
        }

        void LateUpdate()
        {
            if (_bubble != null) FaceCamera();
        }

        // The bubble is a flat badge; turning it like the camera keeps it square on screen. Straight above the head it
        // would cover the face from this steep camera, so it sits off to the side in screen terms (camera right and up).
        void FaceCamera()
        {
            var cam = Camera.main;
            if (cam == null || _bubble == null) return;
            var view = cam.transform;
            _bubble.rotation = view.rotation;
            _bubble.position = transform.TransformPoint(_bubbleAnchor) + view.right * _bubbleOffset.x + view.up * _bubbleOffset.y;
        }

        Vector2 TailCell()
        {
            var tail = (Vector2)footprint[0];
            float best = -1f;
            foreach (var c in footprint)
            {
                float d = ((Vector2)c - headCell).sqrMagnitude;
                if (d > best) { best = d; tail = c; }
            }
            return tail;
        }

        // Footprint coordinates (level x right, y down) to the block's local space, where level y grows towards -Z.
        static Vector3 FootprintToLocal(Vector2 cell) => new Vector3(cell.x, 0f, -cell.y) * GridUnits.CellSize;

        static void DestroyChild(Transform child)
        {
            // Destroy is deferred in Play mode: hide and detach it now, so it neither shows nor turns up in Find.
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        // remaining are the block's types still on it, outermost first; the prefab was chosen for the outermost one.
        public void Setup(BlockData data, string[] remaining, Vector2Int pivot, int turns)
        {
            blockId = data.id;
            pivotCell = pivot;
            quarterTurns = turns;
            axis = string.IsNullOrEmpty(data.axis) ? "free" : data.axis;
            frozenFor = data.frozenFor;
            types = remaining ?? Array.Empty<string>();
        }

#if UNITY_EDITOR
        // Reads the footprint from the mesh: a cell belongs to the block when the mesh covers its center
        // seen from above. Run it after changing a block's mesh.
        [ContextMenu("Detect Footprint From Mesh")]
        public void DetectFootprint()
        {
            var meshFilter = GetComponentInChildren<MeshFilter>(true);
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
