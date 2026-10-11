using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BlocksEscape.EditorTools
{
    // Switches the Game view to a portrait phone resolution, adding it to the size list if needed.
    // Unity has no public API for Game view sizes, so this goes through the internal GameViewSizes types.
    static class GameViewPhoneSize
    {
        const int Width = 1080, Height = 2340; // a typical 19.5:9 phone
        const string Label = "Phone Portrait";

        [MenuItem("Tools/Blocks Escape/Game View: Phone Portrait")]
        static void Apply()
        {
            var editor = typeof(Editor).Assembly;
            var sizesType = editor.GetType("UnityEditor.GameViewSizes");
            var instance = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var groupType = sizesType.GetProperty("currentGroupType").GetValue(instance);
            var group = sizesType.GetMethod("GetGroup").Invoke(instance, new[] { groupType });
            var groupClass = group.GetType();

            int index = FindSize(group, groupClass);
            if (index < 0)
            {
                var sizeType = editor.GetType("UnityEditor.GameViewSize");
                var fixedResolution = Enum.Parse(editor.GetType("UnityEditor.GameViewSizeType"), "FixedResolution");
                var size = Activator.CreateInstance(sizeType, fixedResolution, Width, Height, Label);
                groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                sizesType.GetMethod("SaveToHDD")?.Invoke(instance, null);
                index = FindSize(group, groupClass);
            }

            var gameViewType = editor.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            var select = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (select != null) select.Invoke(gameView, new object[] { index, null });
            else gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(gameView, index);
            gameView.Repaint();
        }

        static int FindSize(object group, Type groupClass)
        {
            int total = (int)groupClass.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupClass.GetMethod("GetGameViewSize");
            for (int i = 0; i < total; i++)
            {
                var size = getSize.Invoke(group, new object[] { i });
                var type = size.GetType();
                if ((int)type.GetProperty("width").GetValue(size) == Width && (int)type.GetProperty("height").GetValue(size) == Height)
                    return i;
            }
            return -1;
        }
    }
}
