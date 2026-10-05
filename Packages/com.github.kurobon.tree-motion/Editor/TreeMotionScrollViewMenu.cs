using System;
using UnityEditor;
using UnityEngine;

namespace TreeMotion.Editor
{
    public static class TreeMotionScrollViewMenu
    {
        [MenuItem("GameObject/UI (Canvas)/Tree Motion/Scroll View", false)]
        private static void CreateFromMenu(MenuCommand command)
        {
            Create(command.context as GameObject ?? Selection.activeGameObject);
        }

        public static GameObject Create(GameObject context)
        {
            const string undoName = "Create Tree Motion Scroll View";
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Selection.activeGameObject = context;
            try
            {
                if (!EditorApplication.ExecuteMenuItem("GameObject/UI (Canvas)/Scroll View"))
                    throw new InvalidOperationException("Unity's Scroll View creation menu is unavailable.");
                var root = Selection.activeGameObject;
                if (root == null || root == context ||
                    !root.TryGetComponent<UnityEngine.UI.ScrollRect>(out var scrollRect) ||
                    scrollRect.viewport == null || scrollRect.content == null)
                    throw new InvalidOperationException("Unity's Scroll View creation menu did not create a configured Scroll View.");

                Undo.RecordObject(root, undoName);
                if (context != null && context.GetComponentInParent<Canvas>() != null &&
                    root.transform.parent != context.transform)
                {
                    Undo.SetTransformParent(root.transform, context.transform, undoName);
                    Undo.RecordObject(root.transform, undoName);
                    ((RectTransform)root.transform).anchoredPosition = Vector2.zero;
                }
                root.name = "TreeMotion Scroll View";
                GameObjectUtility.EnsureUniqueNameForSibling(root);
                Undo.RecordObject(scrollRect, undoName);
                scrollRect.horizontal = false;
                var horizontalScrollbar = scrollRect.horizontalScrollbar;
                scrollRect.horizontalScrollbar = null;
                if (horizontalScrollbar != null)
                    Undo.DestroyObjectImmediate(horizontalScrollbar.gameObject);

                // Unity initially reserves space for the horizontal scrollbar.
                if (scrollRect.verticalScrollbar != null)
                {
                    var scrollbarRect = (RectTransform)scrollRect.verticalScrollbar.transform;
                    Undo.RecordObject(scrollbarRect, undoName);
                    var bottom = scrollbarRect.offsetMin;
                    var top = scrollbarRect.offsetMax;
                    bottom.y = top.y = 0f;
                    scrollbarRect.offsetMin = bottom;
                    scrollbarRect.offsetMax = top;
                }

                var content = scrollRect.content;
                Undo.RecordObject(content, undoName);
                content.anchorMin = new Vector2(0f, 1f);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 1f);
                content.sizeDelta = Vector2.zero;
                var group = Undo.AddComponent<TreeMotionGroupView>(content.gameObject);
                Undo.RecordObject(group, undoName);
                group.Configure(content, 0f, new TreeMotionPadding());
                var treeView = Undo.AddComponent<TreeMotionScrollView>(root);
                Undo.RecordObject(treeView, undoName);
                treeView.Configure(scrollRect, scrollRect.viewport, content);
                Undo.FlushUndoRecordObjects();
                return root;
            }
            finally
            {
                Undo.SetCurrentGroupName(undoName);
                Undo.CollapseUndoOperations(undoGroup);
            }
        }
    }
}
