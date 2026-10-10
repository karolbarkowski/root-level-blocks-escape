using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlocksEscape.EditorTools.LevelPreview
{
    // Lists level JSON files from a project folder and renders the selected one as plain cubes,
    // either inside this window or as a temporary (unsaved) object in the open scene.
    public sealed class LevelPreviewWindow : EditorWindow
    {
        const string FolderPrefKey = "BlocksEscape.LevelPreview.Folder";
        const string DefaultFolder = "Assets/Levels";
        const float ListWidth = 230f;
        const float DefaultPitch = 60f;

        string _folder;
        string[] _levelPaths = Array.Empty<string>();
        int _selected = -1;
        Vector2 _listScroll;

        LevelData _level;
        string _error;
        LevelPreviewBuilder _builder;
        LevelPreviewBuilder.Result _preview;
        LevelPreviewBuilder.Result _sceneInstance;
        PreviewRenderUtility _renderer;
        GameObject _previewContainer; // lives in the preview scene; Cleanup() destroys it

        float _yaw;
        float _pitch = DefaultPitch;
        float _zoom = 1f;
        bool _showLabels = true;

        GUIStyle _labelStyle;

        [MenuItem("Tools/Blocks Escape/Level Preview")]
        static void Open() => GetWindow<LevelPreviewWindow>("Level Preview");

        void OnEnable()
        {
            _folder = EditorPrefs.GetString(FolderPrefKey, DefaultFolder);
            _builder = new LevelPreviewBuilder();
            _renderer = new PreviewRenderUtility();
            _renderer.camera.fieldOfView = 30f;
            _renderer.camera.nearClipPlane = 0.1f;
            _renderer.camera.farClipPlane = 200f;
            _renderer.camera.clearFlags = CameraClearFlags.SolidColor;
            _renderer.camera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            _renderer.lights[0].intensity = 1.1f;
            _renderer.lights[0].transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            _renderer.lights[1].intensity = 0.5f;
            _renderer.lights[1].transform.rotation = Quaternion.Euler(-30f, 150f, 0f);
            _renderer.ambientColor = new Color(0.35f, 0.35f, 0.38f);
            _previewContainer = EditorUtility.CreateGameObjectWithHideFlags("Level Preview", HideFlags.HideAndDontSave);
            _renderer.AddSingleGO(_previewContainer);

            SceneView.duringSceneGui += OnSceneGUI;
            RefreshList();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            ClearSceneInstance();
            DestroyPreview();
            _renderer?.Cleanup();
            _renderer = null;
            _builder?.Dispose();
            _builder = null;
        }

        void OnProjectChange()
        {
            // Rebuild so edits to the selected JSON (or its removal) show up immediately.
            RefreshList();
            Repaint();
        }

        void RefreshList()
        {
            var current = _selected >= 0 && _selected < _levelPaths.Length ? _levelPaths[_selected] : null;
            _levelPaths = AssetDatabase.IsValidFolder(_folder)
                ? AssetDatabase.FindAssets("t:TextAsset", new[] { _folder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();

            int index = Array.IndexOf(_levelPaths, current);
            Select(index >= 0 ? index : _levelPaths.Length > 0 ? 0 : -1);
        }

        void Select(int index)
        {
            _selected = index;
            _level = null;
            _error = null;
            DestroyPreview();
            if (index < 0 || index >= _levelPaths.Length) return;

            var path = _levelPaths[index];
            try
            {
                var level = LevelData.Parse(File.ReadAllText(path));
                if (level?.board == null || level.board.width <= 0 || level.board.height <= 0)
                    throw new InvalidDataException("Missing board or board size.");
                _level = level;
                _preview = _builder.Build(level, Path.GetFileNameWithoutExtension(path), HideFlags.HideAndDontSave);
                _preview.root.transform.SetParent(_previewContainer.transform, false);
                if (_sceneInstance != null) SpawnInScene();
            }
            catch (Exception e)
            {
                _error = $"Could not load {Path.GetFileName(path)}: {e.Message}";
                DestroyPreview();
            }
        }

        void DestroyPreview()
        {
            if (_preview?.root != null) DestroyImmediate(_preview.root);
            _preview = null;
        }

        void SpawnInScene()
        {
            ClearSceneInstance();
            if (_level == null) return;
            var name = $"Level Preview - {Path.GetFileNameWithoutExtension(_levelPaths[_selected])}";
            _sceneInstance = _builder.Build(_level, name, HideFlags.DontSave);
            Selection.activeGameObject = _sceneInstance.root;
            SceneView.lastActiveSceneView?.Frame(_sceneInstance.bounds, false);
            SceneView.RepaintAll();
        }

        void ClearSceneInstance()
        {
            if (_sceneInstance?.root != null) DestroyImmediate(_sceneInstance.root);
            _sceneInstance = null;
            SceneView.RepaintAll();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLevelList();
                DrawPreviewArea();
            }
        }

        void DrawLevelList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _folder = EditorGUILayout.DelayedTextField(_folder);
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetString(FolderPrefKey, _folder);
                        _selected = -1;
                        DestroyPreview();
                        _level = null;
                        RefreshList();
                    }
                    if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(28)))
                        RefreshList();
                }

                if (_levelPaths.Length == 0)
                    EditorGUILayout.HelpBox($"No .json levels in {_folder}.", MessageType.Info);

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                for (int i = 0; i < _levelPaths.Length; i++)
                {
                    var label = Path.GetFileNameWithoutExtension(_levelPaths[i]);
                    bool selected = i == _selected;
                    if (GUILayout.Toggle(selected, label, EditorStyles.toolbarButton) && !selected)
                        Select(i);
                }
                EditorGUILayout.EndScrollView();

                if (_level != null) DrawLevelInfo();
            }
        }

        void DrawLevelInfo()
        {
            var board = _level.board;
            int walls = board.walls?.Length ?? 0;
            int gates = board.gates?.Length ?? 0;
            int blocks = _level.blocks?.Length ?? 0;
            var info = $"Board {board.width}x{board.height} ({(_level.IsEnclosed ? "enclosed" : "rectangle")})\n" +
                       $"Walls {walls}, gates {gates}, blocks {blocks}\n" +
                       $"Solution moves {_level.solution?.Length ?? 0}";
            if (_level.analysis != null && _level.analysis.difficulty > 0)
                info += $"\nDifficulty {_level.analysis.difficulty}";
            EditorGUILayout.HelpBox(info, MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show in Scene")) SpawnInScene();
                using (new EditorGUI.DisabledScope(_sceneInstance == null))
                    if (GUILayout.Button("Clear Scene")) ClearSceneInstance();
            }
            if (GUILayout.Button("Ping JSON"))
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(_levelPaths[_selected]));
        }

        void DrawPreviewArea()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    _showLabels = GUILayout.Toggle(_showLabels, "Labels", EditorStyles.toolbarButton, GUILayout.Width(55));
                    if (GUILayout.Button("Top", EditorStyles.toolbarButton, GUILayout.Width(40))) { _yaw = 0f; _pitch = 90f; }
                    if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(75))) { _yaw = 0f; _pitch = DefaultPitch; _zoom = 1f; }
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Drag: orbit   Scroll: zoom", EditorStyles.miniLabel);
                }

                var rect = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (_error != null)
                {
                    EditorGUI.HelpBox(new Rect(rect.x + 8, rect.y + 8, rect.width - 16, 40), _error, MessageType.Error);
                    return;
                }
                if (_preview == null || rect.width < 2f || rect.height < 2f) return;

                HandleInput(rect);
                if (Event.current.type == EventType.Repaint) RenderPreview(rect);
            }
        }

        void HandleInput(Rect rect)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!rect.Contains(e.mousePosition)) break;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    _yaw += e.delta.x * 0.4f;
                    _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.4f, 10f, 90f);
                    e.Use();
                    Repaint();
                    break;
                case EventType.ScrollWheel:
                    if (!rect.Contains(e.mousePosition)) break;
                    _zoom = Mathf.Clamp(_zoom * (1f + e.delta.y * 0.05f), 0.3f, 3f);
                    e.Use();
                    Repaint();
                    break;
            }
        }

        void RenderPreview(Rect rect)
        {
            var board = _level.board;
            float size = (Mathf.Max(board.width, board.height) + 2f) * GridUnits.CellSize;
            float distance = size * 0.5f / Mathf.Tan(_renderer.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f * _zoom;
            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            var camera = _renderer.camera;
            camera.transform.rotation = rotation;
            camera.transform.position = rotation * new Vector3(0f, 0f, -distance);
            camera.farClipPlane = distance + size * 2f;
            camera.aspect = rect.width / rect.height;

            _renderer.BeginPreview(rect, GUIStyle.none);
            _renderer.Render(true);
            GUI.DrawTexture(rect, _renderer.EndPreview(), ScaleMode.StretchToFill, false);

            if (_showLabels) DrawLabels(rect, camera, _preview);
        }

        void DrawLabels(Rect rect, Camera camera, LevelPreviewBuilder.Result preview)
        {
            _labelStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };

            foreach (var label in preview.labels)
            {
                var vp = camera.WorldToViewportPoint(preview.root.transform.TransformPoint(label.position));
                if (vp.z <= 0f) continue;
                var p = new Vector2(rect.x + vp.x * rect.width, rect.y + (1f - vp.y) * rect.height);
                var content = new GUIContent(label.text);
                var labelSize = _labelStyle.CalcSize(content);
                var r = new Rect(p.x - labelSize.x * 0.5f, p.y - labelSize.y * 0.5f, labelSize.x, labelSize.y);
                EditorGUI.DrawRect(new Rect(r.x - 2, r.y - 1, r.width + 4, r.height + 2), new Color(0f, 0f, 0f, 0.45f));
                GUI.Label(r, content, _labelStyle);
            }
        }

        void OnSceneGUI(SceneView view)
        {
            if (!_showLabels || _sceneInstance?.root == null) return;
            var origin = _sceneInstance.root.transform;
            foreach (var label in _sceneInstance.labels)
                Handles.Label(origin.TransformPoint(label.position), label.text, EditorStyles.whiteMiniLabel);
        }
    }
}
