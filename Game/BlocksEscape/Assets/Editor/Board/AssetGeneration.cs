using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BlocksEscape.EditorTools
{
    // Shared pieces for the generated board assets (walls, gates): asset create-or-update,
    // URP materials and rounded-box mesh building.
    static class AssetGeneration
    {
        public const string BoardPrefabPath = "Assets/Prefabs/Board.prefab";

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        public static T LoadOrCreate<T>(string path, Func<T> create) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        public static Material LoadOrCreateLitMaterial(string path, Color color, float smoothness)
        {
            var material = LoadOrCreate(path, () =>
                new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")));
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        // Switches a URP Lit material to alpha-blended transparency.
        public static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
        }

        // Fills a Board.prefab object field (e.g. "wallPrefab") if it is still empty.
        public static void AssignToBoardPrefab(string field, Object value)
        {
            var board = AssetDatabase.LoadAssetAtPath<Board>(BoardPrefabPath);
            if (board == null || value == null) return;
            var so = new SerializedObject(board);
            var property = so.FindProperty(field);
            if (property == null || property.objectReferenceValue != null) return;
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(board);
        }

        public sealed class MeshData
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<List<int>> submeshes = new List<List<int>>();

            public List<int> Submesh(int index)
            {
                while (submeshes.Count <= index) submeshes.Add(new List<int>());
                return submeshes[index];
            }

            public void WriteTo(Mesh mesh, string name)
            {
                mesh.Clear();
                mesh.name = name;
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = submeshes.Count;
                for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
            }
        }

        // Box with rounded edges and corners: each face is a grid whose rows bunch up at the edges,
        // and every grid point is pushed out from an inner box by the bevel radius.
        public static void AddRoundedBox(MeshData mesh, int submesh, Vector3 center, Vector3 size, float radius,
            Quaternion rotation, int segments = 4)
        {
            var triangles = mesh.Submesh(submesh);
            var half = size * 0.5f;
            radius = Mathf.Min(radius, half.x, half.y, half.z);
            var inner = half - Vector3.one * radius;

            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int b = (axis + 1) % 3, c = (axis + 2) % 3;
                var normal = Vector3.zero;
                normal[axis] = sign;
                var bCoords = EdgeBunchedCoords(half[b], radius, segments);
                var cCoords = EdgeBunchedCoords(half[c], radius, segments);
                int start = mesh.vertices.Count;

                foreach (var cb in bCoords)
                foreach (var cc in cCoords)
                {
                    var p = Vector3.zero;
                    p[axis] = sign * half[axis];
                    p[b] = cb;
                    p[c] = cc;
                    var core = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    var n = (p - core).normalized;
                    mesh.vertices.Add(center + rotation * (core + n * radius));
                    mesh.normals.Add(rotation * n);
                    mesh.uvs.Add(new Vector2(cb + half[b], cc + half[c]));
                }

                // Pick the winding that faces outwards (Unity front faces are clockwise).
                var eb = Vector3.zero; eb[b] = 1f;
                var ec = Vector3.zero; ec[c] = 1f;
                bool flip = Vector3.Dot(Vector3.Cross(eb, ec), normal) < 0f;
                int columns = cCoords.Length;
                for (int i = 0; i < bCoords.Length - 1; i++)
                for (int j = 0; j < columns - 1; j++)
                {
                    int v00 = start + i * columns + j, v10 = v00 + columns, v01 = v00 + 1, v11 = v10 + 1;
                    if (flip) triangles.AddRange(new[] { v00, v01, v10, v10, v01, v11 });
                    else triangles.AddRange(new[] { v00, v10, v01, v10, v11, v01 });
                }
            }
        }

        static float[] EdgeBunchedCoords(float half, float radius, int segments)
        {
            var coords = new float[(segments + 1) * 2];
            for (int k = 0; k <= segments; k++)
            {
                float t = (float)k / segments;
                coords[k] = -half + radius * t;
                coords[segments + 1 + k] = half - radius + radius * t;
            }
            return coords;
        }
    }
}
