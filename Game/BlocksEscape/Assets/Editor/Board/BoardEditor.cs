using System;
using System.Linq;
using BlocksEscape.Levels;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    [CustomEditor(typeof(Board))]
    sealed class BoardEditor : UnityEditor.Editor
    {
        const string LevelsFolder = "Assets/Levels";

        string[] _levelPaths = Array.Empty<string>();
        string[] _levelNames = Array.Empty<string>();
        string _error;

        void OnEnable() => RefreshLevels();

        void RefreshLevels()
        {
            _levelPaths = AssetDatabase.IsValidFolder(LevelsFolder)
                ? AssetDatabase.FindAssets("t:TextAsset", new[] { LevelsFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
            _levelNames = _levelPaths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var board = (Board)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Level Setup", EditorStyles.boldLabel);

            DrawLevelPopup();

            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(board);
            if (isAsset)
                EditorGUILayout.HelpBox("Open the prefab or place it in a scene to initialize a level.", MessageType.Info);
            if (board.WallPrefab == null)
                EditorGUILayout.HelpBox("No wall prefab assigned; walls will be skipped.", MessageType.Warning);
            if (board.Models == null)
                EditorGUILayout.HelpBox("No block type models assigned; blocks and gates will be skipped.", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(isAsset || targets.Length > 1))
            {
                using (new EditorGUI.DisabledScope(board.Level == null))
                    if (GUILayout.Button("Initialize")) InitializeWithUndo(board);
                using (new EditorGUI.DisabledScope(board.LevelRoots().Count == 0))
                    if (GUILayout.Button("Clear")) ClearWithUndo(board);
            }

            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);
        }

        void DrawLevelPopup()
        {
            serializedObject.Update();
            var levelProperty = serializedObject.FindProperty("level");
            var current = levelProperty.objectReferenceValue != null ? AssetDatabase.GetAssetPath(levelProperty.objectReferenceValue) : null;
            int index = Array.IndexOf(_levelPaths, current);

            using (new EditorGUILayout.HorizontalScope())
            {
                int picked = EditorGUILayout.Popup("Select Level", index, _levelNames);
                if (picked != index && picked >= 0)
                {
                    levelProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(_levelPaths[picked]);
                    serializedObject.ApplyModifiedProperties();
                }
                if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(28)))
                    RefreshLevels();
            }
        }

        void InitializeWithUndo(Board board)
        {
            _error = null;
            LevelData data;
            try
            {
                data = LevelData.Parse(board.Level.text);
            }
            catch (Exception e)
            {
                _error = $"Could not read {board.Level.name}: {e.Message}";
                return;
            }

            Undo.SetCurrentGroupName("Initialize Board");
            int group = Undo.GetCurrentGroup();
            // Record the board before destroying the old level, so undo restores the level and its reference together.
            Undo.RecordObject(board, "Initialize Board");
            foreach (var root in board.LevelRoots()) Undo.DestroyObjectImmediate(root);
            if (board.Surface != null) Undo.RecordObject(board.Surface, "Initialize Board");

            try
            {
                board.Initialize(data);
            }
            catch (Exception e)
            {
                _error = e.Message;
            }

            if (board.Content != null) Undo.RegisterCreatedObjectUndo(board.Content.gameObject, "Initialize Board");
            Undo.CollapseUndoOperations(group);
        }

        static void ClearWithUndo(Board board)
        {
            Undo.SetCurrentGroupName("Clear Board");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(board, "Clear Board");
            foreach (var root in board.LevelRoots()) Undo.DestroyObjectImmediate(root);
            board.Clear(); // the level is already gone; this just resets the references
            Undo.CollapseUndoOperations(group);
        }
    }
}
