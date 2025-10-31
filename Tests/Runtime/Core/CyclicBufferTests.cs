#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System.Collections;
    using DxMessaging.Core.DataStructure;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class CyclicBufferTests
    {
        [UnityTest]
        public IEnumerator AddAndOverwritePreservesChronology()
        {
            CyclicBuffer<int> buf = new(3);
            buf.Add(0);
            buf.Add(1);
            buf.Add(2);

            Assert.AreEqual(
                3,
                buf.Count,
                "Count should reflect number of elements added up to capacity."
            );
            Assert.AreEqual(0, buf[0], "Oldest element should be at index 0 before wrap.");
            Assert.AreEqual(1, buf[1], "Next element should be index 1.");
            Assert.AreEqual(2, buf[2], "Newest element should be index 2 before wrap.");

            // Overwrite oldest
            buf.Add(3);
            Assert.AreEqual(3, buf.Count, "Count should not grow beyond capacity.");
            Assert.AreEqual(1, buf[0], "After overwrite, oldest is dropped.");
            Assert.AreEqual(2, buf[1], "Element order should advance by one.");
            Assert.AreEqual(3, buf[2], "Newest written value should be last.");

            // Remove middle element
            bool removed = buf.Remove(2);
            Assert.IsTrue(removed, "Remove should return true when element existed.");
            Assert.AreEqual(2, buf.Count, "Count should decrease after remove.");
            Assert.AreEqual(1, buf[0], "Remaining first element should be unchanged.");
            Assert.AreEqual(3, buf[1], "Remaining second element should be next in order.");

            yield break;
        }

        [UnityTest]
        public IEnumerator ResizeTruncatesOrExtends()
        {
            CyclicBuffer<int> buf = new(5);
            for (int i = 0; i < 5; ++i)
            {
                buf.Add(i);
            }
            Assert.AreEqual(5, buf.Count, "Filled buffer should have full count.");

            // Shrink: oldest entries should be truncated
            buf.Resize(3);
            Assert.AreEqual(3, buf.Count, "Count should reflect new capacity after shrink.");
            Assert.AreEqual(2, buf[0], "Shrink should retain most recent entries and drop oldest.");
            Assert.AreEqual(3, buf[1], "Remaining order should be preserved (middle).");
            Assert.AreEqual(4, buf[2], "Remaining order should be preserved (newest).");

            // Grow: capacity increases, order stays
            buf.Resize(6);
            Assert.AreEqual(3, buf.Count, "Growing capacity should not change current count.");
            Assert.AreEqual(2, buf[0], "Growing capacity should not alter order (first).");
            Assert.AreEqual(3, buf[1], "Growing capacity should not alter order (second).");
            Assert.AreEqual(4, buf[2], "Growing capacity should not alter order (third).");
            yield break;
        }
    }
}

#endif
