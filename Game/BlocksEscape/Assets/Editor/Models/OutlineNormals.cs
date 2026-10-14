using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Bakes smoothed normals into UV channel 3 of block and gate models for Shaders/Outline.shader.
    // Vertices that share a position (split for hard edges or UV seams) get the average of their distinct normals,
    // so the outline hull pushes corners out diagonally instead of tearing open along hard edges.
    sealed class OutlineNormals : AssetPostprocessor
    {
        const int Channel = 3;

        public override uint GetVersion() => 1;

        void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith("Assets/Prefabs/Block/") && !assetPath.StartsWith("Assets/Prefabs/Gate/")) return;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null) Bake(filter.sharedMesh);
        }

        static void Bake(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            if (normals.Length != vertices.Length) return;

            var distinct = new Dictionary<Vector3Int, List<Vector3>>();
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                keys[i] = Vector3Int.RoundToInt(vertices[i] * 10000f);
                if (!distinct.TryGetValue(keys[i], out var list)) distinct[keys[i]] = list = new List<Vector3>();
                if (!list.Exists(n => Vector3.Dot(n, normals[i]) > 0.999f)) list.Add(normals[i]);
            }

            var smooth = new Dictionary<Vector3Int, Vector3>(distinct.Count);
            foreach (var (key, list) in distinct)
            {
                var sum = Vector3.zero;
                foreach (var n in list) sum += n;
                smooth[key] = sum.sqrMagnitude > 1e-8f ? sum.normalized : list[0];
            }

            var baked = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++) baked.Add(smooth[keys[i]]);
            mesh.SetUVs(Channel, baked);
        }
    }
}
