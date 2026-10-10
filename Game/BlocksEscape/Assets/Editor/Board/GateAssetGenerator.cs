using UnityEditor;
using UnityEngine;
using static BlocksEscape.EditorTools.AssetGeneration;

namespace BlocksEscape.EditorTools
{
    // Generates the gate cell assets: a low threshold, a translucent glass block and an arrow chevron
    // on top pointing along local +Z (one mesh, three submeshes), their URP Lit materials and Gate.prefab.
    // Threshold and glass materials are white; GateCell tints them per instance with the gate color.
    // Runs once automatically when the prefab is missing; "Regenerate" rewrites mesh and materials in place.
    [InitializeOnLoad]
    static class GateAssetGenerator
    {
        const string ArtFolder = "Assets/Art/Gates";
        const string MeshPath = ArtFolder + "/GateMesh.asset";
        const string ThresholdMaterialPath = ArtFolder + "/GateThreshold.mat";
        const string GlassMaterialPath = ArtFolder + "/GateGlass.mat";
        const string ArrowMaterialPath = ArtFolder + "/GateArrow.mat";
        const string PrefabPath = "Assets/Prefabs/Gate.prefab";

        // Shape, in cells.
        const float ThresholdWidth = WallAssetGenerator.BodyWidth;
        const float ThresholdHeight = 0.10f;
        const float ThresholdBevel = 0.04f;
        const float GlassWidth = 0.88f;
        const float GlassBottom = 0.08f;
        const float GlassTop = 0.68f;
        const float GlassBevel = 0.08f;
        const float ArrowLength = 0.36f; // per chevron arm
        const float ArrowWidth = 0.09f;
        const float ArrowThickness = 0.04f;
        const float ArrowBevel = 0.03f;
        const float ArrowTip = 0.14f;    // chevron tip, forward of the cell center

        static GateAssetGenerator()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                    Generate();
            };
        }

        [MenuItem("Tools/Blocks Escape/Regenerate Gate Assets")]
        public static void Generate()
        {
            EnsureFolder(ArtFolder);
            EnsureFolder("Assets/Prefabs");

            var mesh = LoadOrCreate(MeshPath, () => new Mesh());
            BuildMesh(mesh);
            EditorUtility.SetDirty(mesh);

            var threshold = LoadOrCreateLitMaterial(ThresholdMaterialPath, Color.white, 0.3f);
            var glass = LoadOrCreateLitMaterial(GlassMaterialPath, new Color(1f, 1f, 1f, 0.45f), 0.85f);
            MakeTransparent(glass);
            var arrow = LoadOrCreateLitMaterial(ArrowMaterialPath, new Color(0.97f, 0.97f, 0.95f), 0.5f);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var go = new GameObject("Gate");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { threshold, glass, arrow };
                go.AddComponent<GateCell>();
                PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
                Object.DestroyImmediate(go);
            }

            AssignToBoardPrefab("gatePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            AssetDatabase.SaveAssets();
            Debug.Log($"Gate assets generated: {PrefabPath}");
        }

        static void BuildMesh(Mesh mesh)
        {
            var data = new MeshData();
            float cell = GridUnits.CellSize;

            // Pivot at the bottom center of the cell.
            AddRoundedBox(data, 0,
                new Vector3(0f, ThresholdHeight * 0.5f, 0f) * cell,
                new Vector3(ThresholdWidth, ThresholdHeight, ThresholdWidth) * cell, ThresholdBevel * cell, Quaternion.identity);

            float glassHeight = GlassTop - GlassBottom;
            AddRoundedBox(data, 1,
                new Vector3(0f, GlassBottom + glassHeight * 0.5f, 0f) * cell,
                new Vector3(GlassWidth, glassHeight, GlassWidth) * cell, GlassBevel * cell, Quaternion.identity);

            // Chevron: two arms meeting at the tip, swept back at 45 degrees.
            var tip = new Vector3(0f, GlassTop + ArrowThickness * 0.5f - 0.01f, ArrowTip);
            foreach (var yaw in new[] { 45f, -45f })
            {
                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var center = tip - rotation * Vector3.forward * (ArrowLength - ArrowWidth) * 0.5f;
                AddRoundedBox(data, 2, center * cell,
                    new Vector3(ArrowWidth, ArrowThickness, ArrowLength) * cell, ArrowBevel * cell, rotation, 3);
            }

            data.WriteTo(mesh, "GateMesh");
        }
    }
}
