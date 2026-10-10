using System;
using System.Collections.Generic;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BlocksEscape.EditorTools.LevelPreview
{
    // Builds a throwaway GameObject hierarchy of plain cubes for a level.
    // Grid cell (x, y) with y growing downwards maps to world XZ, centered on the origin, top row at +Z.
    public sealed class LevelPreviewBuilder : IDisposable
    {
        public struct Label
        {
            public Vector3 position;
            public string text;
        }

        public sealed class Result
        {
            public GameObject root;
            public Bounds bounds;
            public readonly List<Label> labels = new List<Label>();
        }

        const float FloorThickness = 0.08f;
        const float WallHeight = 0.8f;
        const float GateHeight = 0.8f;
        const float GateAlpha = 0.4f;
        const float BlockHeight = 0.5f;
        const float LayerHeight = 0.2f;
        const float BlockInset = 0.06f;
        const float LayerInset = 0.12f;

        static readonly Color WallColor = new Color(0.22f, 0.23f, 0.26f);
        static readonly Color FloorColorA = new Color(0.86f, 0.86f, 0.83f);
        static readonly Color FloorColorB = new Color(0.80f, 0.80f, 0.77f);
        static readonly Color GrayBlockColor = LevelPalette.Gray;
        static readonly Color AxisMarkColor = new Color(0.1f, 0.1f, 0.1f);


        readonly Dictionary<(Color, bool), Material> _materials = new Dictionary<(Color, bool), Material>();
        readonly Mesh _cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        readonly Shader _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        HideFlags _flags;
        float _offsetX, _offsetZ;

        public Result Build(LevelData level, string name, HideFlags flags)
        {
            _flags = flags;
            var board = level.board;
            _offsetX = -board.width * 0.5f;
            _offsetZ = board.height * 0.5f;

            var result = new Result { root = NewObject(name, null) };
            var floorRoot = NewObject("Floor", result.root.transform);
            var wallRoot = NewObject("Walls", result.root.transform);
            var gateRoot = NewObject("Gates", result.root.transform);
            var blockRoot = NewObject("Blocks", result.root.transform);

            var floor = ComputeFloor(level);
            for (int y = 0; y < board.height; y++)
            for (int x = 0; x < board.width; x++)
            {
                if (!floor[x, y]) continue;
                var color = (x + y) % 2 == 0 ? FloorColorA : FloorColorB;
                AddBox(floorRoot.transform, $"Floor {x},{y}", CellCenter(x, y, -FloorThickness * 0.5f),
                    new Vector3(1f, FloorThickness, 1f), color, false);
            }

            foreach (var w in board.walls ?? Array.Empty<CellData>())
                AddBox(wallRoot.transform, $"Wall {w.x},{w.y}", CellCenter(w.x, w.y, WallHeight * 0.5f),
                    new Vector3(1f, WallHeight, 1f), WallColor, false);

            foreach (var g in board.gates ?? Array.Empty<GateData>())
                BuildGate(gateRoot.transform, g, level, result);

            foreach (var b in level.blocks ?? Array.Empty<BlockData>())
                BuildBlock(blockRoot.transform, b, result);

            // Everything above is built in cell units; scale once to the game's cell size.
            result.root.transform.localScale = Vector3.one * GridUnits.CellSize;
            result.bounds = new Bounds(Vector3.zero, new Vector3(board.width + 2f, WallHeight, board.height + 2f) * GridUnits.CellSize);
            return result;
        }

        static bool[,] ComputeFloor(LevelData level)
        {
            var board = level.board;
            int w = board.width, h = board.height;
            var floor = new bool[w, h];
            var barrier = new bool[w, h];
            bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;

            foreach (var c in board.walls ?? Array.Empty<CellData>())
                if (Inside(c.x, c.y)) barrier[c.x, c.y] = true;

            if (!level.IsEnclosed)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    floor[x, y] = !barrier[x, y];
                return floor;
            }

            foreach (var g in board.gates ?? Array.Empty<GateData>())
            foreach (var c in Board.GateCells(g, level))
                if (Inside(c.x, c.y)) barrier[c.x, c.y] = true;

            // Flood fill exterior space from the canvas edges; whatever stays unreached is playable floor.
            var exterior = new bool[w, h];
            var queue = new Queue<Vector2Int>();
            void Visit(int x, int y)
            {
                if (!Inside(x, y) || barrier[x, y] || exterior[x, y]) return;
                exterior[x, y] = true;
                queue.Enqueue(new Vector2Int(x, y));
            }
            for (int x = 0; x < w; x++) { Visit(x, 0); Visit(x, h - 1); }
            for (int y = 0; y < h; y++) { Visit(0, y); Visit(w - 1, y); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                Visit(c.x + 1, c.y); Visit(c.x - 1, c.y); Visit(c.x, c.y + 1); Visit(c.x, c.y - 1);
            }

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                floor[x, y] = !barrier[x, y] && !exterior[x, y];
            return floor;
        }

        void BuildGate(Transform parent, GateData g, LevelData level, Result result)
        {
            var color = ParseColor(g.color);
            var gateObject = NewObject($"Gate {g.id} ({g.color}, {g.side})", parent);
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var c in Board.GateCells(g, level))
            {
                var center = CellCenter(c.x, c.y, GateHeight * 0.5f);
                AddBox(gateObject.transform, $"{c.x},{c.y}", center, new Vector3(0.98f, GateHeight, 0.98f), color, true);
                sum += center;
                count++;
            }

            if (count > 0)
            {
                var arrow = g.side == "top" ? "^" : g.side == "bottom" ? "v" : g.side == "left" ? "<" : ">";
                result.labels.Add(new Label { position = sum / count + Vector3.up * GateHeight * 0.5f, text = arrow });
            }
        }

        void BuildBlock(Transform parent, BlockData b, Result result)
        {
            var blockObject = NewObject($"Block {b.id}", parent);
            var cells = new HashSet<Vector2Int>();
            foreach (var s in b.shape ?? Array.Empty<CellData>())
                cells.Add(new Vector2Int(b.position.x + s.x, b.position.y + s.y));

            var colors = b.colors ?? Array.Empty<string>();
            int layers = Mathf.Max(1, colors.Length);
            float top = 0f;

            // Layer 0 (outermost color) is the full-size base; each inner layer is a thinner, inset slab on top.
            for (int layer = 0; layer < layers; layer++)
            {
                var color = colors.Length == 0 ? GrayBlockColor : ParseColor(colors[layer]);
                float height = layer == 0 ? BlockHeight : LayerHeight;
                float inset = BlockInset + layer * LayerInset;
                var layerObject = NewObject(colors.Length == 0 ? "gray" : $"Layer {layer} {colors[layer]}", blockObject.transform);
                foreach (var c in cells)
                    AddCellSlab(layerObject.transform, c, cells, top, height, inset, color);
                top += height;
            }

            if (b.axis == "horizontal" || b.axis == "vertical")
            {
                bool horizontal = b.axis == "horizontal";
                var markObject = NewObject($"Axis {b.axis}", blockObject.transform);
                foreach (var c in cells)
                {
                    var size = horizontal ? new Vector3(0.7f, 0.02f, 0.08f) : new Vector3(0.08f, 0.02f, 0.7f);
                    AddBox(markObject.transform, $"{c.x},{c.y}", CellCenter(c.x, c.y, top + 0.01f), size, AxisMarkColor, false);
                }
            }

            if (cells.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (var c in cells) sum += CellCenter(c.x, c.y, 0f);
                var text = b.id;
                if (b.frozenFor > 0) text += $"\nfrozen {b.frozenFor}";
                if (b.axis == "horizontal") text += "\n<->";
                else if (b.axis == "vertical") text += "\nup/down";
                result.labels.Add(new Label { position = sum / cells.Count + Vector3.up * top, text = text });
            }
        }

        // One cell of a block slab. Sides touching another cell of the same block are not inset,
        // so multi-cell shapes read as one connected piece.
        void AddCellSlab(Transform parent, Vector2Int c, HashSet<Vector2Int> cells, float bottom, float height, float inset, Color color)
        {
            float left = cells.Contains(new Vector2Int(c.x - 1, c.y)) ? 0f : inset;
            float right = cells.Contains(new Vector2Int(c.x + 1, c.y)) ? 0f : inset;
            float up = cells.Contains(new Vector2Int(c.x, c.y - 1)) ? 0f : inset;
            float down = cells.Contains(new Vector2Int(c.x, c.y + 1)) ? 0f : inset;

            var center = CellCenter(c.x, c.y, bottom + height * 0.5f);
            center.x += (left - right) * 0.5f;
            center.z += (down - up) * 0.5f;
            var size = new Vector3(1f - left - right, height, 1f - up - down);
            AddBox(parent, $"{c.x},{c.y}", center, size, color, false);
        }

        Vector3 CellCenter(int x, int y, float height) => new Vector3(x + 0.5f + _offsetX, height, _offsetZ - y - 0.5f);

        GameObject NewObject(string name, Transform parent)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, _flags);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        void AddBox(Transform parent, string name, Vector3 center, Vector3 size, Color color, bool transparent)
        {
            var go = NewObject(name, parent);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = _cube;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial(color, transparent);
            renderer.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }

        Material GetMaterial(Color color, bool transparent)
        {
            if (_materials.TryGetValue((color, transparent), out var material) && material != null)
                return material;

            material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave };
            var c = transparent ? new Color(color.r, color.g, color.b, GateAlpha) : color;
            material.SetColor("_BaseColor", c); // URP
            material.SetColor("_Color", c);     // Built-in fallback
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Glossiness", 0.2f);

            if (transparent)
            {
                material.SetFloat("_Surface", 1f); // URP: transparent
                material.SetFloat("_Blend", 0f);   // URP: alpha blend
                material.SetFloat("_Mode", 3f);    // Built-in: transparent
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            _materials[(color, transparent)] = material;
            return material;
        }

        public static Color ParseColor(string name) => string.IsNullOrEmpty(name) ? GrayBlockColor : LevelPalette.Get(name);

        public void Dispose()
        {
            foreach (var material in _materials.Values)
                if (material != null) Object.DestroyImmediate(material);
            _materials.Clear();
        }
    }
}
