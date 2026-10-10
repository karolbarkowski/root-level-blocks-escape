using UnityEditor;
using UnityEngine;
using static BlocksEscape.EditorTools.AssetGeneration;

namespace BlocksEscape.EditorTools
{
    // Generates the wall cell assets: a rounded body with a lighter inset cap (one mesh, two submeshes),
    // its two URP Lit materials and Wall.prefab. Runs once automatically when the prefab is missing;
    // "Regenerate" rewrites the mesh and materials in place, so references to them stay valid.
    [InitializeOnLoad]
    static class WallAssetGenerator
    {
        const string ArtFolder = "Assets/Art/Walls";
        const string MeshPath = ArtFolder + "/WallMesh.asset";
        const string BodyMaterialPath = ArtFolder + "/WallBody.mat";
        const string CapMaterialPath = ArtFolder + "/WallCap.mat";
        const string PrefabPath = "Assets/Prefabs/Wall.prefab";

        // Shape, in cells. The body leaves a small gap to its neighbours so adjacent walls read as tiles.
        public const float BodyWidth = 0.96f;
        public const float BodyHeight = 0.62f;
        const float BodyBevel = 0.09f;
        const float CapWidth = 0.76f;
        const float CapHeight = 0.14f;
        const float CapBevel = 0.05f;
        const float CapSink = 0.04f; // cap overlaps the body slightly so no seam shows
        public const float TotalHeight = BodyHeight + CapHeight - CapSink;

        static readonly Color BodyColor = new Color(0.29f, 0.33f, 0.44f);
        static readonly Color CapColor = new Color(0.50f, 0.55f, 0.67f);

        static WallAssetGenerator()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                    Generate();
            };
        }

        [MenuItem("Tools/Blocks Escape/Regenerate Wall Assets")]
        public static void Generate()
        {
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");

            var mesh = LoadOrCreate(MeshPath, () => new Mesh());
            BuildMesh(mesh);
            EditorUtility.SetDirty(mesh);

            var body = LoadOrCreateLitMaterial(BodyMaterialPath, BodyColor, 0.25f);
            var cap = LoadOrCreateLitMaterial(CapMaterialPath, CapColor, 0.45f);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var go = new GameObject("Wall");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { body, cap };
                var collider = go.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, TotalHeight * 0.5f, 0f) * GridUnits.CellSize;
                collider.size = new Vector3(1f, TotalHeight, 1f) * GridUnits.CellSize;
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Object.DestroyImmediate(go);
            }

            AssignToBoardPrefab("wallPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            AssetDatabase.SaveAssets();
            Debug.Log($"Wall assets generated: {PrefabPath}");
        }

        static void BuildMesh(Mesh mesh)
        {
            var data = new MeshData();
            float cell = GridUnits.CellSize;

            // Pivot at the bottom center of the cell.
            AddRoundedBox(data, 0,
                new Vector3(0f, BodyHeight * 0.5f, 0f) * cell,
                new Vector3(BodyWidth, BodyHeight, BodyWidth) * cell, BodyBevel * cell, Quaternion.identity);
            AddRoundedBox(data, 1,
                new Vector3(0f, BodyHeight - CapSink + CapHeight * 0.5f, 0f) * cell,
                new Vector3(CapWidth, CapHeight, CapWidth) * cell, CapBevel * cell, Quaternion.identity);
            data.WriteTo(mesh, "WallMesh");
        }
    }
}
