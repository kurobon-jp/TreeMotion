using NUnit.Framework;
using TreeMotion.Editor;
using UnityEditor;
using UnityEngine;

namespace TreeMotion.Tests
{
    public sealed class TreeMotionScrollViewMenuTests
    {
        private GameObject _canvas;
        private GameObject _eventSystem;
        private Object _previousSelection;

        [SetUp]
        public void SetUp()
        {
            _previousSelection = Selection.activeObject;
            _canvas = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _eventSystem = new GameObject("Test EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_canvas);
            Object.DestroyImmediate(_eventSystem);
            Selection.activeObject = _previousSelection;
        }

        [Test]
        public void Create_ConfiguresReferencesAndVerticalGeometry()
        {
            var root = TreeMotionScrollViewMenu.Create(_canvas);
            var scroll = root.GetComponent<UnityEngine.UI.ScrollRect>();
            var viewport = root.transform.Find("Viewport").GetComponent<RectTransform>();
            var content = viewport.Find("Content").GetComponent<RectTransform>();
            Assert.That(root.transform.parent, Is.EqualTo(_canvas.transform));
            Assert.That(scroll.viewport, Is.SameAs(viewport));
            Assert.That(scroll.content, Is.SameAs(content));
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.vertical, Is.True);
            Assert.That(viewport.GetComponent<UnityEngine.UI.Mask>(), Is.Not.Null);
            Assert.That(scroll.verticalScrollbar, Is.Not.Null);
            Assert.That(scroll.horizontalScrollbar, Is.Null);
            var scrollbarRect = (RectTransform)scroll.verticalScrollbar.transform;
            Assert.That(scrollbarRect.offsetMin.y, Is.Zero);
            Assert.That(scrollbarRect.offsetMax.y, Is.Zero);
            Assert.That(viewport.GetComponent<UnityEngine.UI.Image>().raycastTarget, Is.True);
            Assert.That(content.GetComponent<TreeMotionGroupView>().ChildrenFrame, Is.SameAs(content));
            Assert.That(content.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(content.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(content.pivot, Is.EqualTo(new Vector2(0.5f, 1f)));
            Assert.That(content.GetComponent<UnityEngine.UI.LayoutGroup>(), Is.Null);
            Assert.That(content.GetComponent<UnityEngine.UI.ContentSizeFitter>(), Is.Null);
            Assert.That(Selection.activeGameObject, Is.SameAs(root));
            // Bind validates all host references and the Content root Group.
            Assert.DoesNotThrow(() => root.GetComponent<TreeMotionScrollView>()
                .Bind(new TreeStore<int, string>(), new EmptyAdapter()));
        }

        [Test]
        public void Create_UsesSelectedCanvasChildAndSupportsUndoRedo()
        {
            var parent = new GameObject("Panel", typeof(RectTransform));
            parent.transform.SetParent(_canvas.transform, false);
            var root = TreeMotionScrollViewMenu.Create(parent);
            Assert.That(root.transform.parent, Is.SameAs(parent.transform));
            Undo.PerformUndo();
            Assert.That(root == null, Is.True);
            Assert.That(parent.transform.childCount, Is.Zero);
            Undo.PerformRedo();
            Assert.That(parent.transform.childCount, Is.EqualTo(1));
            var restored = parent.transform.GetChild(0).GetComponent<UnityEngine.UI.ScrollRect>();
            Assert.That(restored.viewport, Is.Not.Null);
            Assert.That(restored.content, Is.Not.Null);
            Assert.That(restored.content.GetComponent<TreeMotionGroupView>().ChildrenFrame,
                Is.SameAs(restored.content));
        }

        private sealed class EmptyAdapter : ITreeMotionAdapter<int, string>
        {
            public GameObject GetItemPrefab(int id, string item) => null;
            public float GetItemSize(int id, string item) => 32f;
            public void Bind(GameObject view, int id, string item, VisibleRow<int> row) { }
        }

        [Test]
        public void Create_UsesStandardCanvasForNonUISelection()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var context = new GameObject("Context");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(context, scene);
                var root = TreeMotionScrollViewMenu.Create(context);
                var canvas = root.GetComponentInParent<Canvas>();
                Assert.That(canvas, Is.Not.Null);
                Assert.That(root.transform.parent, Is.Not.SameAs(context.transform));
                Undo.PerformUndo();
                Assert.That(root == null, Is.True);
                Undo.PerformRedo();
                var scroll = canvas.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
                Assert.That(scroll.viewport, Is.Not.Null);
                Assert.That(scroll.content, Is.Not.Null);
                Object.DestroyImmediate(scroll.gameObject);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
