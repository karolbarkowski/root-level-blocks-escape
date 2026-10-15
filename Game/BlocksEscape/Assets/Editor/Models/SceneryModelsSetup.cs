using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Turns the models exported by Game/BlocksEscape/Blender/scenery.py (Prefabs/Scenery/*.fbx + Scenery_Palette.png)
    // into prefabs that share one URP Lit material. The palette texture is a grid of flat colour swatches the meshes are
    // UV-mapped into, so it is imported without filtering or mipmaps to keep the swatches from bleeding.
    // The back button and the menu plank get a collider and a WorldButton; the sign gets a LevelSign, and the plank and
    // the title sign a WorldLabel, all wired to the game font (Fonts/Fredoka SDF, "Blocks Escape/Build Game Font").
    // A model named <Prop>_<Part>.fbx is a moving part of <Prop> (the barn's door leaves): it is hung on a hinge inside
    // that prop's prefab instead of becoming a prefab. Props that answer taps get a TapReaction (see Reaction).
    static class SceneryModelsSetup
    {
        const string Dir = "Assets/Prefabs/Scenery";
        const string PalettePath = Dir + "/Scenery_Palette.png";
        const string MaterialPath = Dir + "/Scenery.mat";
        const string LeafMaterialPath = "Assets/Materials/Leaf.mat";
        const string RippleMaterialPath = "Assets/Materials/Ripple.mat";
        const string FontPath = "Assets/Fonts/Fredoka SDF.asset";

        static readonly Color Leaf = new(0.36f, 0.62f, 0.25f), LeafLight = new(0.55f, 0.75f, 0.3f);

        // Prop -> how it answers a tap, its particle material and colours, and how many particles a tap sends out.
        static (TapReaction.Kind kind, string material, Color a, Color b, int count)? Reaction(string name)
        {
            if (name.StartsWith("Tree")) return (TapReaction.Kind.Shake, LeafMaterialPath, Leaf, LeafLight, 7);
            if (name == "FlowerBushPink") return (TapReaction.Kind.Shrug, LeafMaterialPath, Leaf, new Color(0.96f, 0.6f, 0.75f), 7);
            if (name == "FlowerBushWhite") return (TapReaction.Kind.Shrug, LeafMaterialPath, Leaf, new Color(0.97f, 0.96f, 0.92f), 7);
            if (name.StartsWith("Bush")) return (TapReaction.Kind.Shrug, LeafMaterialPath, Leaf, LeafLight, 7);
            if (name.StartsWith("Flowers") || name.StartsWith("Grass")) return (TapReaction.Kind.Wobble, null, default, default, 0);
            if (name == "Pond") return (TapReaction.Kind.Ripple, RippleMaterialPath, new Color(1f, 1f, 1f, 0.75f), default, 3);
            if (name == "Barn") return (TapReaction.Kind.Doors, null, default, default, 0);
            if (name == "Butterfly") return (TapReaction.Kind.TakeOff, null, default, default, 0);
            return null;
        }

        [MenuItem("Blocks Escape/Build Scenery From Blender")]
        static void Build()
        {
            if (AssetImporter.GetAtPath(PalettePath) is TextureImporter palette)
            {
                palette.filterMode = FilterMode.Point;
                palette.mipmapEnabled = false;
                palette.textureCompression = TextureImporterCompression.Uncompressed;
                palette.SaveAndReimport();
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath));
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);

            var models = Directory.GetFiles(Dir, "*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToList();
            bool IsPart(string modelName)
            {
                int cut = modelName.IndexOf('_');
                return cut > 0 && models.Contains($"{Dir}/{modelName.Substring(0, cut)}.fbx");
            }
            foreach (var path in models)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter importer &&
                    importer.materialImportMode != ModelImporterMaterialImportMode.None)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.animationType = ModelImporterAnimationType.None;
                    importer.SaveAndReimport();
                }
                string name = Path.GetFileNameWithoutExtension(path);
                if (IsPart(name)) continue;
                string prefabPath = $"{Dir}/{name}.prefab";
                // Each prefab is a root with the model as its child, so scripts can animate the model (a button's squash)
                // while the root keeps the position and scale ScreenAnchor gives it. An existing prefab is updated in
                // place, only its model child replaced, so its root components keep the overrides scene instances have,
                // and children added by hand (the title sign's peeking animals) stay. The model stays the first child.
                bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                var root = exists ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(name);
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    if (root.transform.GetChild(i).name == name) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                model.transform.SetParent(root.transform, false);
                model.transform.SetSiblingIndex(0);
                foreach (var part in models.Where(p => Path.GetFileNameWithoutExtension(p).StartsWith(name + "_")))
                    if (IsPart(Path.GetFileNameWithoutExtension(part))) AddHingedPart(model.transform, part);
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = material;
                // Shadows of ankle-high props and floating badges are a few pixels, not worth a second draw in the shadow pass.
                // Turned off on the whole prop, so children added by hand (the title sign's peeking animals) go along.
                bool casts = !SkipsShadow(name);
                foreach (var r in (casts ? model : root).GetComponentsInChildren<MeshRenderer>(true))
                    r.shadowCastingMode = casts ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                Configure(root, name);

                if (name is "BackButton" or "MenuPlank")
                {
                    var collider = model.AddComponent<BoxCollider>();
                    collider.size += new Vector3(0.1f, 0.3f, 0.1f); // a little forgiving for fingers
                    if (root.GetComponent<WorldButton>() == null) root.AddComponent<WorldButton>();
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }

            WireFont<LevelSign>("Sign");
            WireFont<WorldLabel>("MenuPlank");
            WireFont<WorldLabel>("TitleSign");

            AssetDatabase.SaveAssets();
            Debug.Log("Built scenery prefabs.", material);
        }

        static bool SkipsShadow(string name) =>
            name.StartsWith("Grass") || name.StartsWith("Flowers") || name.StartsWith("Rock") ||
            name is "Butterfly" or "SleepBubble" or "AxisArrow" or
                "TitleSign"; // UI: its shadow would vanish with it when the menu pops out, a visible blink on the ground

        // Adds T to the prop's prefab if missing and sets its font to the game font.
        static void WireFont<T>(string prop) where T : Component
        {
            var path = $"{Dir}/{prop}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            var component = root.GetComponent<T>();
            if (component == null) component = root.AddComponent<T>();
            var so = new SerializedObject(component);
            so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(FontPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // A part modelled in its owner's coordinates, hung on a hinge at its outer vertical edge (the side away from the
        // owner's centre line), so turning the hinge about Y swings it like a door.
        static void AddHingedPart(Transform model, string path)
        {
            var part = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var bounds = part.GetComponent<MeshFilter>().sharedMesh.bounds;
            var hingeAt = new Vector3(bounds.center.x < 0f ? bounds.min.x : bounds.max.x, bounds.min.y, bounds.center.z);
            var hinge = new GameObject($"Hinge {part.name}").transform;
            hinge.SetParent(model, false);
            hinge.localPosition = hingeAt;
            part.transform.SetParent(hinge, false);
            part.transform.localPosition = -hingeAt;
        }

        static void Configure(GameObject root, string name)
        {
            var reaction = Reaction(name);
            var existing = root.GetComponent<TapReaction>();
            if (reaction == null)
            {
                if (existing != null) Object.DestroyImmediate(existing, true);
                return;
            }
            var (kind, materialPath, a, b, count) = reaction.Value;
            var so = new SerializedObject(existing != null ? existing : root.AddComponent<TapReaction>());
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("particles").objectReferenceValue = materialPath != null ? AssetDatabase.LoadAssetAtPath<Material>(materialPath) : null;
            so.FindProperty("particleColor").colorValue = a;
            so.FindProperty("particleColor2").colorValue = b;
            so.FindProperty("particleCount").intValue = count;
            so.ApplyModifiedPropertiesWithoutUndo();
            so.FindProperty("hitScale").floatValue = kind == TapReaction.Kind.TakeOff ? 2.2f : 1f; // the butterfly is tiny
            so.ApplyModifiedPropertiesWithoutUndo();
            if (kind == TapReaction.Kind.TakeOff && root.GetComponent<ButterflyFlight>() == null) root.AddComponent<ButterflyFlight>();
        }
    }
}
