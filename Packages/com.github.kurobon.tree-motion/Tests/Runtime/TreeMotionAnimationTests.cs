using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class TreeMotionAnimationTests
    {
        [Test]
        public void ConsecutiveRemoves_PreserveExistingExitProgress()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(new[]
            {
                new TreeMotionLayout<int>(1, 0f, 20f),
                new TreeMotionLayout<int>(2, 30f, 20f)
            });
            animation.Retarget(new[] { new TreeMotionLayout<int>(2, 0f, 20f) }, 1f);
            animation.Advance(0.4f);

            animation.Retarget(Array.Empty<TreeMotionLayout<int>>(), 1f);
            Assert.That(Value(animation, 1).Progress, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(Value(animation, 2).Progress, Is.Zero);
            animation.Advance(0.5f);
            Assert.That(Value(animation, 1).Progress, Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(Value(animation, 2).Progress, Is.EqualTo(0.5f).Within(0.0001f));
            animation.Advance(0.5f);
            Assert.That(animation.Count, Is.Zero);
        }

        [Test]
        public void PresentationProgress_IsLinearWhileGeometryUsesSmoothStep()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(new[] { new TreeMotionLayout<int>(1, 0f, 100f) });
            animation.Retarget(new[]
            {
                new TreeMotionLayout<int>(1, 100f, 200f),
                new TreeMotionLayout<int>(2, 300f, 100f)
            }, 1f);
            animation.Advance(0.25f);
            Assert.That(Value(animation, 1).Offset, Is.EqualTo(15.625f).Within(0.0001f));
            Assert.That(Value(animation, 1).Size, Is.EqualTo(115.625f).Within(0.0001f));
            Assert.That(Value(animation, 2).Progress, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(animation.GetValue(1).Progress, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void Retarget_UnchangedLayoutCompletesImmediately()
        {
            var animation = new TreeMotionAnimation<int>();
            var layout = new[] { new TreeMotionLayout<int>(1, 0f, 20f) };
            animation.Snap(layout);
            animation.Retarget(layout, 1f);
            Assert.That(animation.IsAnimating, Is.False);
            Assert.That(Value(animation, 1).Kind, Is.EqualTo(TreeMotionAnimationKind.Stable));
            animation.Retarget(Array.Empty<TreeMotionLayout<int>>(), 1f);
            Assert.That(animation.IsAnimating, Is.True);
            animation.Advance(1f);
            Assert.That(animation.IsAnimating, Is.False);
        }

        [Test]
        public void Retarget_AnimatesInsertMoveResizeAndRemoveByStableId()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(new[]
            {
                new TreeMotionLayout<int>(1, 0f, 20f),
                new TreeMotionLayout<int>(2, 30f, 20f),
                new TreeMotionLayout<int>(3, 60f, 20f)
            });

            animation.Retarget(new[]
            {
                new TreeMotionLayout<int>(2, 0f, 30f),
                new TreeMotionLayout<int>(4, 40f, 20f),
                new TreeMotionLayout<int>(3, 70f, 20f)
            }, 1f);

            Assert.That(Value(animation, 1).Kind, Is.EqualTo(TreeMotionAnimationKind.Remove));
            Assert.That(Value(animation, 2).Kind, Is.EqualTo(TreeMotionAnimationKind.Move));
            Assert.That(Value(animation, 4).Kind, Is.EqualTo(TreeMotionAnimationKind.Insert));

            animation.Advance(0.5f);
            Assert.That(Value(animation, 1).Progress, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Value(animation, 2).Offset, Is.EqualTo(15f).Within(0.0001f));
            Assert.That(Value(animation, 2).Size, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(Value(animation, 4).Progress, Is.EqualTo(0.5f).Within(0.0001f));

            animation.Advance(0.5f);
            Assert.That(animation.IsAnimating, Is.False);
            Assert.That(animation.Count, Is.EqualTo(3));
            Assert.Throws<KeyNotFoundException>(() => Value(animation, 1));
            Assert.That(Value(animation, 2).Kind, Is.EqualTo(TreeMotionAnimationKind.Stable));
        }

        [Test]
        public void RetargetDuringAnimation_StartsFromDisplayedValue()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(new[] { new TreeMotionLayout<int>(1, 0f, 20f) });
            animation.Retarget(new[] { new TreeMotionLayout<int>(1, 100f, 20f) }, 1f);
            animation.Advance(0.5f);
            var displayed = animation.GetValue(0).Offset;

            animation.Retarget(new[] { new TreeMotionLayout<int>(1, 200f, 20f) }, 1f);

            Assert.That(animation.GetValue(0).Offset, Is.EqualTo(displayed).Within(0.0001f));
        }

        [Test]
        public void RetargetDuringInsert_DoesNotResetPresentationProgress()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(Array.Empty<TreeMotionLayout<int>>());
            animation.Retarget(new[] { new TreeMotionLayout<int>(1, 0f, 20f) }, 1f);
            animation.Advance(0.5f);
            var displayed = animation.GetValue(0).Progress;

            animation.Retarget(new[] { new TreeMotionLayout<int>(1, 50f, 20f) }, 1f);

            Assert.That(animation.GetValue(0).Kind, Is.EqualTo(TreeMotionAnimationKind.Insert));
            Assert.That(animation.GetValue(0).Progress, Is.EqualTo(displayed).Within(0.0001f));
        }

        [Test]
        public void SwapChange_MovesNodesFromTheirOwnPreviousPositions()
        {
            var tree = new TreeStore<int, string>();
            tree.BeginUpdate().Insert(1, "first").Insert(2, "second").Commit();
            var animation = new TreeMotionAnimation<int>();
            var layout = new[]
            {
                new TreeMotionLayout<int>(1, 0f, 20f),
                new TreeMotionLayout<int>(2, 30f, 40f)
            };
            animation.Snap(layout);

            var changes = tree.BeginUpdate().Swap(1, 2).Commit();
            var target = new[]
            {
                new TreeMotionLayout<int>(2, 0f, 20f),
                new TreeMotionLayout<int>(1, 30f, 40f)
            };
            animation.Retarget(target, 1f, changes);

            Assert.That(Value(animation, 1).Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
            Assert.That(Value(animation, 1).Offset, Is.Zero);
            Assert.That(Value(animation, 1).Size, Is.EqualTo(20f));
            Assert.That(Value(animation, 2).Kind, Is.EqualTo(TreeMotionAnimationKind.Swap));
            Assert.That(Value(animation, 2).Offset, Is.EqualTo(30f));
            Assert.That(Value(animation, 2).Size, Is.EqualTo(40f));
            animation.Advance(1f);
            Assert.That(Value(animation, 1).Kind, Is.EqualTo(TreeMotionAnimationKind.Stable));
            Assert.That(Value(animation, 2).Kind, Is.EqualTo(TreeMotionAnimationKind.Stable));
        }

        [Test]
        public void GetValue_DoesNotAllocateAfterWarmup()
        {
            var animation = new TreeMotionAnimation<int>();
            animation.Snap(new[] { new TreeMotionLayout<int>(1, 0f, 20f) });
            _ = animation.GetValue(0);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var checksum = 0f;
            for (var i = 0; i < 10000; i++)
            {
                animation.TryGetValue(1, out var value);
                checksum += value.Offset;
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(checksum, Is.Zero);
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void InvalidLayoutsAndTimeAreRejected()
        {
            var animation = new TreeMotionAnimation<int>();
            Assert.Throws<ArgumentException>(() => animation.Snap(new[]
            {
                new TreeMotionLayout<int>(1, 0f, 1f),
                new TreeMotionLayout<int>(1, 2f, 1f)
            }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                animation.Retarget(Array.Empty<TreeMotionLayout<int>>(), 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => animation.Advance(-1f));
        }

        private static TreeMotionAnimationValue<int> Value(TreeMotionAnimation<int> animation, int id)
        {
            for (var i = 0; i < animation.Count; i++)
            {
                var value = animation.GetValue(i);
                if (value.Id == id)
                    return value;
            }
            throw new KeyNotFoundException();
        }
    }
}
