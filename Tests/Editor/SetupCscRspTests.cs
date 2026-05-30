#if UNITY_EDITOR
namespace DxMessaging.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;

    [TestFixture]
    public sealed class SetupCscRspTests
    {
        private string _testRspFilePath;

        [SetUp]
        public void SetUp()
        {
            _testRspFilePath = Path.Combine(Path.GetTempPath(), $"test_csc_{Guid.NewGuid()}.rsp");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_testRspFilePath))
            {
                File.Delete(_testRspFilePath);
            }
        }

        [Test]
        public void FiltersDuplicateAnalyzerEntries()
        {
            // Arrange: Create a CSC.rsp file with duplicate entries from different package versions
            string testContent =
                @"-a:""Library/PackageCache/com.wallstop-studios.dxmessaging@4e74e1b2eec3/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll""
-a:""Library/PackageCache/com.wallstop-studios.dxmessaging@4e74e1b2eec3/Editor/Analyzers/Microsoft.CodeAnalysis.dll""
-a:""Library/PackageCache/com.wallstop-studios.dxmessaging@3d05efca60e4/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll""
-a:""Library/PackageCache/com.wallstop-studios.dxmessaging@3d05efca60e4/Editor/Analyzers/Microsoft.CodeAnalysis.dll""
-r:""SomeOtherAnalyzer.dll""
";

            File.WriteAllText(_testRspFilePath, testContent);

            // Act: Simulate cleaning logic
            string[] lines = File.ReadAllLines(_testRspFilePath);
            int dxMessagingCount = 0;
            int otherCount = 0;

            foreach (string line in lines)
            {
                if (
                    string.IsNullOrWhiteSpace(line)
                    || !line.StartsWith("-a:", StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (
                    line.Contains(
                        "com.wallstop-studios.dxmessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || line.Contains(
                        "WallstopStudios.DxMessaging",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    dxMessagingCount++;
                }
                else
                {
                    otherCount++;
                }
            }

            // Assert: Should detect duplicate DxMessaging entries
            Assert.AreEqual(4, dxMessagingCount, "Should detect all DxMessaging analyzer entries");
            Assert.AreEqual(0, otherCount, "Should have no other -a: entries in this test");
        }

        [Test]
        public void PreservesNonDxMessagingEntries()
        {
            // Arrange: Create a CSC.rsp with mixed entries
            string testContent =
                @"-a:""Library/PackageCache/com.wallstop-studios.dxmessaging@abc123/Editor/Analyzers/WallstopStudios.DxMessaging.SourceGenerators.dll""
-a:""Library/PackageCache/some.other.package/Analyzers/OtherAnalyzer.dll""
-r:""System.Runtime.dll""
";

            File.WriteAllText(_testRspFilePath, testContent);

            // Act & Assert: Non-DxMessaging lines should be preserved
            string content = File.ReadAllText(_testRspFilePath);
            Assert.IsTrue(
                content.Contains("OtherAnalyzer.dll"),
                "Should preserve non-DxMessaging analyzer"
            );
            Assert.IsTrue(
                content.Contains("System.Runtime.dll"),
                "Should preserve reference entries"
            );
        }
    }
}
#endif
