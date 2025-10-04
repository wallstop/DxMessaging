namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.RegularExpressions;
    using Core;
    using DxMessaging.Core;
    using DxMessaging.Core.Extensions;
    using DxMessaging.Core.Messages;
    using NUnit.Framework;
    using Scripts.Components;
    using Scripts.Messages;
    using UnityEngine;
    using UnityEngine.TestTools.Constraints;
    using Debug = UnityEngine.Debug;
    using Is = NUnit.Framework.Is;
    using Object = UnityEngine.Object;

    public sealed class PerformanceTests : MessagingTestBase
    {
        private const int NumInvocationsPerIteration = 1_000;

        private static readonly List<BenchmarkResult> BenchmarkResults = new();

        private readonly struct BenchmarkResult
        {
            internal BenchmarkResult(string messageTech, long operationsPerSecond, bool allocating)
            {
                MessageTech = messageTech;
                OperationsPerSecond = operationsPerSecond;
                Allocating = allocating;
            }

            internal string MessageTech { get; }

            internal long OperationsPerSecond { get; }

            internal bool Allocating { get; }
        }

        protected override bool MessagingDebugEnabled => false;

        [Test]
        public void Benchmark()
        {
            BenchmarkResults.Clear();
            try
            {
                TimeSpan timeout = TimeSpan.FromSeconds(5);

                Debug.Log("| Message Tech | Operations / Second | Allocations? |");
                Debug.Log("| ------------ | ------------------- | ------------ | ");

                ComplexTargetedMessage message = new(Guid.NewGuid());
                ReflexiveMessage reflexiveMessage = new(
                    nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                    ReflexiveSendMode.Flat,
                    message
                );

                Stopwatch timer = Stopwatch.StartNew();

                RunTest(component => Unity(timer, timeout, component.gameObject, message));

                RunTest(component => NormalGameObject(timer, timeout, component, message));
                RunTest(component => NormalComponent(timer, timeout, component, message));
                RunTest(component => NoCopyGameObject(timer, timeout, component, message));
                RunTest(component => NoCopyComponent(timer, timeout, component, message));

                SimpleUntargetedMessage untargetedMessage = new();
                RunTest(component =>
                    NoCopyUntargeted(timer, timeout, component, untargetedMessage)
                );
                RunTest(component =>
                    ReflexiveOneArgument(timer, timeout, component.gameObject, reflexiveMessage)
                );
                RunTest(component => ReflexiveTwoArguments(timer, timeout, component.gameObject));
                RunTest(component => ReflexiveThreeArguments(timer, timeout, component.gameObject));

                UpdateReadmeWithBenchmarks();
            }
            finally
            {
                BenchmarkResults.Clear();
            }
        }

        private GameObject CreateGameObject()
        {
            GameObject target = new(
                nameof(Benchmark),
                typeof(EmptyMessageAwareComponent),
                typeof(SpriteRenderer),
                typeof(Rigidbody2D),
                typeof(CircleCollider2D),
                typeof(LineRenderer)
            );
            _spawned.Add(target);

            return target;
        }

        private static void DisplayCount(
            string testName,
            int count,
            TimeSpan timeout,
            bool allocating
        )
        {
            long operationsPerSecond = (long)Math.Floor(count / timeout.TotalSeconds);
            BenchmarkResults.Add(new BenchmarkResult(testName, operationsPerSecond, allocating));
            string formattedOperations = operationsPerSecond.ToString(
                "N0",
                CultureInfo.InvariantCulture
            );
            Debug.Log($"| {testName} | {formattedOperations} | {(allocating ? "Yes" : "No")} |");
        }

        private void RunTest(Action<EmptyMessageAwareComponent> test)
        {
            GameObject go = CreateGameObject();
            try
            {
                test(go.GetComponent<EmptyMessageAwareComponent>());
            }
            finally
            {
                _spawned.Remove(go);
                Object.Destroy(go);
            }
        }

        private static void Unity(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject target,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            if (!target.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = target.AddComponent<SimpleMessageAwareComponent>();
            }
            component.slowComplexTargetedHandler = () => ++count;
            // Pre-warm
            target.SendMessage(
                nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                message
            );

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    target.SendMessage(
                        nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                        message
                    );
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(
                    () =>
                        target.SendMessage(
                            nameof(SimpleMessageAwareComponent.HandleSlowComplexTargetedMessage),
                            message
                        ),
                    Is.Not.AllocatingGCMemory()
                );
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("Unity", count, timeout, allocating);
        }

        private static void ReflexiveThreeArguments(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject go
        )
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.reflexiveThreeArgumentHandler = () => ++count;
            ReflexiveMessage message = new(
                nameof(SimpleMessageAwareComponent.HandleReflexiveMessageThreeArguments),
                ReflexiveSendMode.Flat,
                1,
                2,
                3
            );
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (Three Arguments)", count, timeout, allocating);
        }

        private static void ReflexiveTwoArguments(Stopwatch timer, TimeSpan timeout, GameObject go)
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.reflexiveTwoArgumentHandler = () => ++count;
            ReflexiveMessage message = new(
                nameof(SimpleMessageAwareComponent.HandleReflexiveMessageTwoArguments),
                ReflexiveSendMode.Flat,
                1,
                2
            );
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (Two Arguments)", count, timeout, allocating);
        }

        private static void ReflexiveOneArgument(
            Stopwatch timer,
            TimeSpan timeout,
            GameObject go,
            ReflexiveMessage message
        )
        {
            int count = 0;
            if (!go.TryGetComponent(out SimpleMessageAwareComponent component))
            {
                component = go.AddComponent<SimpleMessageAwareComponent>();
            }
            component.slowComplexTargetedHandler = () => ++count;
            InstanceId target = go;
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("Reflexive (One Argument)", count, timeout, allocating);
        }

        private static void NormalGameObject(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            MessageRegistrationToken token = GetToken(component);

            GameObject go = component.gameObject;
            InstanceId target = go;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }

            DisplayCount("DxMessaging (GameObject) - Normal", count, timeout, allocating);
            return;

            void Handle(ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private static void NormalComponent(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            MessageRegistrationToken token = GetToken(component);
            InstanceId target = component;

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (Component) - Normal", count, timeout, allocating);
            return;

            void Handle(ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private static void NoCopyGameObject(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            MessageRegistrationToken token = GetToken(component);

            GameObject go = component.gameObject;
            InstanceId target = go;
            token.RegisterGameObjectTargeted<ComplexTargetedMessage>(go, Handle);
            // Pre-warm
            message.EmitTargeted(target);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);
            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (GameObject) - No-Copy", count, timeout, allocating);
            return;

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private static void NoCopyComponent(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            ComplexTargetedMessage message
        )
        {
            int count = 0;
            MessageRegistrationToken token = GetToken(component);
            InstanceId target = component;

            token.RegisterComponentTargeted<ComplexTargetedMessage>(component, Handle);
            // Pre-warm
            message.EmitComponentTargeted(component);

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitTargeted(target);
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => message.EmitTargeted(target), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (Component) - No-Copy", count, timeout, allocating);
            return;

            void Handle(ref ComplexTargetedMessage _)
            {
                ++count;
            }
        }

        private static void NoCopyUntargeted(
            Stopwatch timer,
            TimeSpan timeout,
            EmptyMessageAwareComponent component,
            SimpleUntargetedMessage message
        )
        {
            int count = 0;
            MessageRegistrationToken token = GetToken(component);

            token.RegisterUntargeted<SimpleUntargetedMessage>(Handle);
            // Pre-warm
            message.EmitUntargeted();

            timer.Restart();
            do
            {
                for (int i = 0; i < NumInvocationsPerIteration; ++i)
                {
                    message.EmitUntargeted();
                }
            } while (timer.Elapsed < timeout);

            bool allocating;
            try
            {
                Assert.That(() => message.EmitUntargeted(), Is.Not.AllocatingGCMemory());
                allocating = false;
            }
            catch
            {
                allocating = true;
            }
            DisplayCount("DxMessaging (Untargeted) - No-Copy", count, timeout, allocating);
            return;

            void Handle(ref SimpleUntargetedMessage _)
            {
                ++count;
            }
        }

        private static void UpdateReadmeWithBenchmarks()
        {
            if (BenchmarkResults.Count == 0)
            {
                return;
            }

            if (IsRunningInContinuousIntegration())
            {
                Debug.Log("Skipping README update because the benchmarks are running in CI.");
                return;
            }

            string operatingSystemSection = GetOperatingSystemSection();
            if (string.IsNullOrEmpty(operatingSystemSection))
            {
                Debug.LogWarning(
                    "Skipping README update because the operating system could not be determined."
                );
                return;
            }

            string readmePath = FindReadmePath();
            if (string.IsNullOrEmpty(readmePath))
            {
                Debug.LogWarning("Skipping README update because README.md could not be located.");
                return;
            }

            try
            {
                string table = BuildBenchmarkTable();
                string originalContent = File.ReadAllText(readmePath);
                string updatedContent = ReplaceOperatingSystemSection(
                    originalContent,
                    operatingSystemSection,
                    table
                );

                if (string.Equals(originalContent, updatedContent, StringComparison.Ordinal))
                {
                    Debug.Log(
                        $"README benchmarks for {operatingSystemSection} are already up to date."
                    );
                    return;
                }

                File.WriteAllText(readmePath, updatedContent, new UTF8Encoding(false));
                Debug.Log($"Updated README benchmarks for {operatingSystemSection}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to update README benchmarks: {exception}");
            }
        }

        private static string BuildBenchmarkTable()
        {
            StringBuilder builder = new();
            builder.AppendLine("| Message Tech | Operations / Second | Allocations? |");
            builder.AppendLine("| ------------ | ------------------- | ------------ |");

            foreach (BenchmarkResult result in BenchmarkResults)
            {
                builder
                    .Append("| ")
                    .Append(result.MessageTech)
                    .Append(" | ")
                    .Append(result.OperationsPerSecond.ToString("N0", CultureInfo.InvariantCulture))
                    .Append(" | ")
                    .Append(result.Allocating ? "Yes" : "No")
                    .AppendLine(" |");
            }

            return builder.ToString().TrimEnd('\r', '\n');
        }

        private static string ReplaceOperatingSystemSection(
            string content,
            string sectionName,
            string tableContent
        )
        {
            string replacement = $"## {sectionName}\n\n{tableContent}\n";
            string pattern =
                $@"## {Regex.Escape(sectionName)}\r?\n(?:\r?\n)*[\s\S]*?(?=\r?\n## |\r?\n# |\Z)";
            Regex regex = new(pattern, RegexOptions.CultureInvariant);
            string updated = regex.Replace(content, replacement, 1);

            if (string.Equals(content, updated, StringComparison.Ordinal))
            {
                string prefix = content.EndsWith("\n", StringComparison.Ordinal)
                    ? string.Empty
                    : "\n";
                updated = $"{content}{prefix}\n{replacement}";
            }

            if (!updated.EndsWith("\n", StringComparison.Ordinal))
            {
                updated += "\n";
            }

            return updated;
        }

        private static string GetOperatingSystemSection()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }

            return null;
        }

        private static bool IsRunningInContinuousIntegration()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JENKINS_URL")))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITLAB_CI")))
            {
                return true;
            }

            return false;
        }

        private static string FindReadmePath()
        {
            string current = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(current))
            {
                string candidate = Path.Combine(current, "README.md");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                string packageCandidate = Path.Combine(
                    current,
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    "README.md"
                );
                if (File.Exists(packageCandidate))
                {
                    return packageCandidate;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            string assemblyLocation = typeof(PerformanceTests).Assembly.Location;
            if (string.IsNullOrEmpty(assemblyLocation))
            {
                return null;
            }

            string assemblyDirectoryPath = Path.GetDirectoryName(assemblyLocation);
            if (string.IsNullOrEmpty(assemblyDirectoryPath))
            {
                return null;
            }

            DirectoryInfo directory = new(assemblyDirectoryPath);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "README.md");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                string packageCandidate = Path.Combine(
                    directory.FullName,
                    "Packages",
                    "com.wallstop-studios.dxmessaging",
                    "README.md"
                );
                if (File.Exists(packageCandidate))
                {
                    return packageCandidate;
                }

                directory = directory.Parent;
            }

            return null;
        }
    }
}
