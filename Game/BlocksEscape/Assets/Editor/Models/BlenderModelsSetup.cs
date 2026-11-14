using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Turns the models exported by Game/BlocksEscape/Blender/animals.py into prefabs and registers them in BlockTypeModels:
    // Prefabs/Block/<Animal>/<Animal>_<Shape>.fbx + <Animal>_Albedo.png,
    // Prefabs/Gate/<Animal>/<Animal>Gate_Tile.fbx + <Animal>Gate_Albedo.png, and the wall fence
    // Prefabs/Wall/Fence/Fence_Post.fbx + Fence_Rail.fbx + Fence_RailDiag.fbx + Fence_Albedo.png into Prefabs/Wall/Wall.prefab.
    static class BlenderModelsSetup
    {
        const string BlockRoot = "Assets/Prefabs/Block";
        const string GateRoot = "Assets/Prefabs/Gate";
        const string ModelsPath = "Assets/Prefabs/BlockTypeModels.asset";
        const string FenceDir = "Assets/Prefabs/Wall/Fence";
        const string WallPath = "Assets/Prefabs/Wall/Wall.prefab";
        // Blocks: sun, shadows and ambient only, with the sleeping eyes out of the same atlas (Shaders/Animal.shader).
        const string AnimalShader = "BlocksEscape/Animal";
        // Everything else: URP's cheap lit shader, without specular highlights.
        public const string PropShader = "Universal Render Pipeline/Simple Lit";

        // Face centre per shape, in footprint coordinates (level x right, y down); mirrors SHAPES in animals.py.
        static readonly Dictionary<string, Vector2> HeadCells = new()
        {
            ["1x1"] = new Vector2(0, 0), ["2x1"] = new Vector2(0, -1), ["2x2"] = new Vector2(0.5f, -1),
            ["3x1"] = new Vector2(0, -2), ["4x1"] = new Vector2(0, -3), ["Corner"] = new Vector2(0, -1),
            ["CornerMirror"] = new Vector2(0, -1), ["L"] = new Vector2(0, -2), ["LMirror"] = new Vector2(0, -2),
            ["S"] = new Vector2(1, -0.5f), ["SMirror"] = new Vector2(-1, -0.5f), ["T"] = new Vector2(0, -1),
        };

        // Model folder -> block type id it is used for; null is the neutral (typeless) blocker.
        static readonly (string animal, string type)[] Animals =
        {
            ("Cow", "cow"), ("Pig", "pig"), ("Chicken", "chicken"), ("Sheep", "sheep"),
            ("Horse", "horse"), ("Cat", "cat"), ("Dog", "dog"), ("HayBale", null),
        };

        [MenuItem("Blocks Escape/Build Models From Blender")]
        static void BuildAll()
        {
            BuildFence();
            var models = AssetDatabase.LoadAssetAtPath<BlockTypeModels>(ModelsPath);
            var so = new SerializedObject(models);
            var entries = so.FindProperty("entries");
            entries.arraySize = 0;
            foreach (var (animal, type) in Animals)
            {
                var blocks = BuildBlocks(animal);
                if (type == null)
                {
                    SetArray(so.FindProperty("neutralBlocks"), blocks);
                    continue;
                }
                entries.arraySize++;
                var entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                entry.FindPropertyRelative("type").stringValue = type;
                SetArray(entry.FindPropertyRelative("blocks"), blocks);
                entry.FindPropertyRelative("gate").objectReferenceValue = BuildGate(animal);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log($"Built block and gate prefabs for {Animals.Length} block types.", models);
        }

        // The materials alone (shaders, merged atlases, texture sizes), leaving the prefabs as they are.
        [MenuItem("Blocks Escape/Build Model Materials")]
        public static void BuildMaterials()
        {
            MakeMaterial($"{FenceDir}/Fence.mat", $"{FenceDir}/Fence_Albedo.png", PropShader);
            foreach (var (animal, type) in Animals)
            {
                BlockMaterials(animal);
                if (type != null) GateMaterials(animal);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Built the materials of {Animals.Length} block types.");
        }

        static void SetArray(SerializedProperty array, IReadOnlyList<Object> values)
        {
            array.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static List<Object> BuildBlocks(string animal)
        {
            var (material, sleep, outline) = BlockMaterials(animal);
            var blocks = new List<Object>();
            foreach (var path in BlockModels(animal))
            {
                var instance = Instantiate(path, material, outline);
                var block = instance.AddComponent<Block>();
                block.DetectFootprint();
                var shape = Path.GetFileNameWithoutExtension(path).Substring(animal.Length + 1);
                var blockSo = new SerializedObject(block);
                blockSo.FindProperty("headCell").vector2Value = HeadCells.TryGetValue(shape, out var head) ? head : Vector2.zero;
                blockSo.FindProperty("awakeMaterial").objectReferenceValue = material;
                blockSo.FindProperty("sleepMaterial").objectReferenceValue = sleep;
                blockSo.ApplyModifiedPropertiesWithoutUndo();
                if (block.Footprint.Count == 0) Debug.LogError($"{path}: the mesh covers no cell.");
                blocks.Add(Save(instance, Path.ChangeExtension(path, ".prefab")).GetComponent<Block>());
            }
            return blocks;
        }

        static List<string> BlockModels(string animal) =>
            Directory.GetFiles($"{BlockRoot}/{animal}", $"{animal}_*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList();

        // The animal's block materials: awake, asleep (null for a model without a face) and outline. animals.py bakes
        // the atlas twice, awake and with the eyes shut; AnimalAtlas merges the two, and the sleep material, shown on
        // frozen blocks, reads the shut eyes out of the merged atlas.
        static (Material awake, Material sleep, Material outline) BlockMaterials(string animal)
        {
            string dir = $"{BlockRoot}/{animal}";
            string texture = $"{dir}/{animal}_Albedo.png", sleepTexture = $"{dir}/{animal}_Sleep_Albedo.png";
            string atlas = $"{dir}/{animal}_Atlas.png", remap = $"{dir}/{animal}_SleepRemap.png";
            bool hasSleep = File.Exists(sleepTexture);
            bool merged = hasSleep && AnimalAtlas.Build(texture, sleepTexture, BlockModels(animal), atlas, remap);
            if (merged) texture = sleepTexture = atlas;

            var material = MakeMaterial($"{dir}/{animal}.mat", texture, AnimalShader);
            Material sleep = null;
            if (hasSleep)
            {
                sleep = MakeMaterial($"{dir}/{animal}_Sleep.mat", sleepTexture, AnimalShader);
                sleep.SetTexture("_SleepMap", merged ? AssetDatabase.LoadAssetAtPath<Texture2D>(remap) : null);
                sleep.SetFloat("_Sleep", merged ? 1f : 0f);
                if (merged) sleep.EnableKeyword("_SLEEP");
                else sleep.DisableKeyword("_SLEEP");
                EditorUtility.SetDirty(sleep);
            }
            return (material, sleep, MakeOutlineMaterial($"{dir}/{animal}Outline.mat", texture));
        }

        static (Material material, Material outline) GateMaterials(string animal)
        {
            string dir = $"{GateRoot}/{animal}";
            // A gate tile shows at about 100 px; its 800 px face is plenty at half size.
            return (MakeMaterial($"{dir}/{animal}Gate.mat", $"{dir}/{animal}Gate_Albedo.png", PropShader, 512),
                    MakeOutlineMaterial($"{dir}/{animal}GateOutline.mat", $"{dir}/{animal}Gate_Albedo.png"));
        }

        static GateCell BuildGate(string animal)
        {
            string dir = $"{GateRoot}/{animal}";
            var (material, outline) = GateMaterials(animal);
            var instance = Instantiate($"{dir}/{animal}Gate_Tile.fbx", material, outline);
            instance.name = $"{animal}Gate";
            instance.AddComponent<GateCell>();
            return Save(instance, $"{dir}/{animal}Gate.prefab").GetComponent<GateCell>();
        }

        // Wall prefab: a post plus eight rail arms (clockwise from +Z), enabled per cell by WallCell.Connect.
        static void BuildFence()
        {
            var material = MakeMaterial($"{FenceDir}/Fence.mat", $"{FenceDir}/Fence_Albedo.png", PropShader);
            var root = new GameObject("Wall");
            Instantiate($"{FenceDir}/Fence_Post.fbx", material).transform.SetParent(root.transform, false);
            var arms = new List<Object>();
            for (int i = 0; i < 8; i++)
            {
                var arm = Instantiate($"{FenceDir}/{(i % 2 == 0 ? "Fence_Rail" : "Fence_RailDiag")}.fbx", material);
                arm.name = $"Arm {i * 45}";
                arm.transform.SetParent(root.transform, false);
                arm.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                arms.Add(arm);
            }
            var so = new SerializedObject(root.AddComponent<WallCell>());
            SetArray(so.FindProperty("arms"), arms);
            so.ApplyModifiedPropertiesWithoutUndo();
            Save(root, WallPath);
        }

        static Material MakeMaterial(string path, string texturePath, string shaderName, int maxSize = 1024)
        {
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter textures)
            {
                // Flat cartoon colours survive ASTC 8x8 (2 bpp) fine; it halves the atlases' memory on phones.
                var android = textures.GetPlatformTextureSettings("Android");
                bool androidSet = android.overridden && android.maxTextureSize == maxSize && android.format == TextureImporterFormat.ASTC_8x8;
                if (textures.maxTextureSize != maxSize || !androidSet)
                {
                    // 1024 (a cell of a block is then 80 px): a quarter of the memory of 2048, a little softer on
                    // large phones. Raise it here if the animals look too soft.
                    textures.maxTextureSize = maxSize;
                    android.overridden = true;
                    android.maxTextureSize = maxSize;
                    android.format = TextureImporterFormat.ASTC_8x8;
                    textures.SetPlatformTextureSettings(android);
                    textures.SaveAndReimport();
                }
            }
            var shader = Shader.Find(shaderName);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader) material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            MatteSimpleLit(material);
            DropStaleTextures(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // A material keeps the textures saved for properties of shaders it used before (the old shader's _MainTex);
        // those would hold textures the game no longer uses, so they go.
        static void DropStaleTextures(Material material)
        {
            var so = new SerializedObject(material);
            var saved = so.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = saved.arraySize - 1; i >= 0; i--)
                if (!material.HasProperty(saved.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue))
                    saved.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Simple Lit without its specular highlights: plain diffuse light, the cheapest lit look.
        public static void MatteSimpleLit(Material material)
        {
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            material.DisableKeyword("_SPECULAR_COLOR");
            material.DisableKeyword("_SPECGLOSSMAP");
        }

        // Second material for the inverted-hull outline (Shaders/Outline.shader), tinted from the same albedo.
        // Width and tint are kept when the material already exists, so tuning in the inspector survives a rebuild.
        static Material MakeOutlineMaterial(string path, string texturePath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("BlocksEscape/Outline"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.renderQueue = -1; // the shader's queue: between the ground and the pieces
            EditorUtility.SetDirty(material);
            return material;
        }

        // Instance of an imported model (with its material, plus an optional outline) to build a prefab variant from.
        static GameObject Instantiate(string modelPath, Material material, Material outline = null)
        {
            if (AssetImporter.GetAtPath(modelPath) is ModelImporter importer &&
                importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            // An extra material on a single-submesh renderer draws that mesh again, which is all the hull needs.
            instance.GetComponent<MeshRenderer>().sharedMaterials = outline != null ? new[] { material, outline } : new[] { material };
            return instance;
        }

        static GameObject Save(GameObject instance, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }
    }
}
