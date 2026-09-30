using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace TreeMotion.Tests
{
    public sealed class VariableHeightLayoutIndexTests
    {
        [Test]
        public void OffsetsAndContentSizeUseVariableHeightsSpacingAndPadding()
        {
            var index = new VariableHeightLayoutIndex<int>(30f, 5f, 7f, 9f);
            index.Reset(new[] { 10, 11, 12 }, new[] { 20f, 40f, 10f });

            Assert.That(index.GetOffset(0), Is.EqualTo(7f));
            Assert.That(index.GetOffset(1), Is.EqualTo(32f));
            Assert.That(index.GetOffset(2), Is.EqualTo(77f));
            Assert.That(index.GetOffset(3), Is.EqualTo(92f));
            Assert.That(index.GetRangeSize(0, 3), Is.EqualTo(80f));
            Assert.That(index.ContentSize, Is.EqualTo(96f));
        }

        [Test]
        public void MeasuredHeightUpdatesFollowingOffsetsByDeltaAndStableId()
        {
            var index = new VariableHeightLayoutIndex<string>(20f, 3f);
            index.Reset(new[] { "a", "b", "c" });

            Assert.That(index.SetHeight("b", 47f), Is.EqualTo(27f));

            Assert.That(index.GetHeight("b"), Is.EqualTo(47f));
            Assert.That(index.GetOffset(2), Is.EqualTo(73f));
            Assert.That(index.ContentSize, Is.EqualTo(93f));
        }

        [Test]
        public void VisibleRangeHonorsGapsAndPixelOverscan()
        {
            var index = new VariableHeightLayoutIndex<int>(10f, 5f);
            index.Reset(new[] { 0, 1, 2 });

            Assert.That(index.GetVisibleRange(10f, 5f).IsEmpty, Is.True);

            var visible = index.GetVisibleRange(15f, 10f);
            Assert.That(visible.Start, Is.EqualTo(1));
            Assert.That(visible.End, Is.EqualTo(2));

            var overscanned = index.GetVisibleRange(15f, 10f, 6f);
            Assert.That(overscanned.Start, Is.EqualTo(0));
            Assert.That(overscanned.End, Is.EqualTo(3));
        }

        [Test]
        public void InsertRemoveAndMovePreserveOrderAndHeightsAcrossChunks()
        {
            var ids = new List<int>();
            var heights = new List<float>();
            for (var i = 0; i < 50; i++)
            {
                ids.Add(i);
                heights.Add(i + 1f);
            }

            var index = new VariableHeightLayoutIndex<int>(10f, chunkCapacity: 16);
            index.Reset(ids, heights);
            index.InsertRange(17, new[] { 100, 101, 102 }, new[] { 7f, 8f, 9f });
            index.RemoveRange(8, 13);
            index.MoveRange(20, 9, 3);

            ids.InsertRange(17, new[] { 100, 101, 102 });
            heights.InsertRange(17, new[] { 7f, 8f, 9f });
            ids.RemoveRange(8, 13);
            heights.RemoveRange(8, 13);
            MoveRange(ids, 20, 9, 3);
            MoveRange(heights, 20, 9, 3);

            AssertMatches(index, ids, heights);
        }

        [Test]
        public void RandomEditsMatchSimpleListModel()
        {
            var random = new Random(314159);
            var ids = new List<int>();
            var heights = new List<float>();
            for (var i = 0; i < 100; i++)
            {
                ids.Add(i);
                heights.Add(random.Next(0, 81));
            }

            var index = new VariableHeightLayoutIndex<int>(24f, 2f, 3f, 4f, 16);
            index.Reset(ids, heights);
            var nextId = 100;

            for (var operation = 0; operation < 500; operation++)
            {
                switch (random.Next(4))
                {
                    case 0:
                    {
                        var at = random.Next(ids.Count + 1);
                        var id = nextId++;
                        var height = random.Next(0, 81);
                        index.Insert(at, id, height);
                        ids.Insert(at, id);
                        heights.Insert(at, height);
                        break;
                    }
                    case 1 when ids.Count > 0:
                    {
                        var at = random.Next(ids.Count);
                        var count = random.Next(1, Math.Min(8, ids.Count - at) + 1);
                        index.RemoveRange(at, count);
                        ids.RemoveRange(at, count);
                        heights.RemoveRange(at, count);
                        break;
                    }
                    case 2 when ids.Count > 1:
                    {
                        var from = random.Next(ids.Count);
                        var count = random.Next(1, Math.Min(8, ids.Count - from) + 1);
                        var destination = random.Next(ids.Count - count + 1);
                        index.MoveRange(from, count, destination);
                        MoveRange(ids, from, count, destination);
                        MoveRange(heights, from, count, destination);
                        break;
                    }
                    case 3 when ids.Count > 0:
                    {
                        var at = random.Next(ids.Count);
                        var height = random.Next(0, 81);
                        index.SetHeight(ids[at], height);
                        heights[at] = height;
                        break;
                    }
                    default:
                        break;
                }

                AssertMatches(index, ids, heights);
            }
        }

        [Test]
        public void DuplicateIdsAndInvalidHeightsAreRejected()
        {
            var index = new VariableHeightLayoutIndex<int>(10f);
            index.Reset(new[] { 1, 2 });

            Assert.Throws<ArgumentException>(() => index.Insert(1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => index.SetHeight(1, -1f));
            Assert.Throws<ArgumentException>(() => index.InsertRange(1, new[] { 3, 3 }));
        }

        private static void AssertMatches(VariableHeightLayoutIndex<int> index,
            IReadOnlyList<int> ids, IReadOnlyList<float> heights)
        {
            Assert.That(index.Count, Is.EqualTo(ids.Count));
            var expectedOffset = index.PaddingStart;
            for (var i = 0; i < ids.Count; i++)
            {
                Assert.That(index.GetId(i), Is.EqualTo(ids[i]), $"ID at {i}");
                Assert.That(index.IndexOf(ids[i]), Is.EqualTo(i), $"index of {ids[i]}");
                Assert.That(index.GetHeight(i), Is.EqualTo(heights[i]), $"height at {i}");
                Assert.That(index.GetOffset(i), Is.EqualTo(expectedOffset).Within(0.001f),
                    $"offset at {i}");
                expectedOffset += heights[i] + index.Spacing;
            }

            var expectedContent = index.PaddingStart + index.PaddingEnd;
            for (var i = 0; i < heights.Count; i++)
                expectedContent += heights[i];
            if (heights.Count > 1)
                expectedContent += (heights.Count - 1) * index.Spacing;
            Assert.That(index.ContentSize, Is.EqualTo(expectedContent).Within(0.01f));
        }

        private static void MoveRange<T>(List<T> list, int from, int count, int destination)
        {
            var moved = list.GetRange(from, count);
            list.RemoveRange(from, count);
            list.InsertRange(destination, moved);
        }
    }
}
