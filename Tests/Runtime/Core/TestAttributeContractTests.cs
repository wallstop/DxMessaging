#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public sealed class TestAttributeContractTests
    {
        [Test]
        public void UnityTestsDoNotUseTestCaseAttributes()
        {
            List<string> offenders = FindMethods(method =>
                    HasAttribute<UnityTestAttribute>(method)
                    && (
                        HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method)
                    )
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found [UnityTest] methods decorated with [TestCase] or [TestCaseSource]. Use [ValueSource] for parameterized coroutine tests.\n"
                    + string.Join("\n", offenders)
            );
        }

        [Test]
        public void NonUnityTestsDoNotReturnIEnumerator()
        {
            List<string> offenders = FindMethods(method =>
                    method.ReturnType == typeof(IEnumerator)
                    && !HasAttribute<UnityTestAttribute>(method)
                    && (
                        HasAttribute<TestAttribute>(method)
                        || HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method)
                    )
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found non-[UnityTest] methods returning IEnumerator. Use [UnityTest] for coroutine tests.\n"
                    + string.Join("\n", offenders)
            );
        }

        [Test]
        public void UnityTestsReturnIEnumerator()
        {
            List<string> offenders = FindMethods(method =>
                    HasAttribute<UnityTestAttribute>(method)
                    && method.ReturnType != typeof(IEnumerator)
                )
                .Select(FormatMethod)
                .ToList();

            Assert.That(
                offenders,
                Is.Empty,
                "Found [UnityTest] methods that do not return IEnumerator.\n"
                    + string.Join("\n", offenders)
            );
        }

        private static IEnumerable<MethodInfo> FindMethods(Func<MethodInfo, bool> predicate)
        {
            return GetRuntimeTestMethods().Where(predicate);
        }

        private static IEnumerable<MethodInfo> GetRuntimeTestMethods()
        {
            Assembly assembly = typeof(TestAttributeContractTests).Assembly;
            BindingFlags methodFlags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic;

            foreach (Type type in assembly.GetTypes())
            {
                if (
                    type.Namespace == null
                    || !type.Namespace.StartsWith(
                        "DxMessaging.Tests.Runtime",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(methodFlags))
                {
                    if (method.IsSpecialName)
                    {
                        continue;
                    }

                    bool isTestMethod =
                        HasAttribute<TestAttribute>(method)
                        || HasAttribute<UnityTestAttribute>(method)
                        || HasAttribute<TestCaseAttribute>(method)
                        || HasAttribute<TestCaseSourceAttribute>(method);
                    // ValueSource is parameter data only and is always paired with a test-defining attribute.

                    if (isTestMethod)
                    {
                        yield return method;
                    }
                }
            }
        }

        private static bool HasAttribute<TAttribute>(MemberInfo method)
            where TAttribute : Attribute
        {
            return method.GetCustomAttributes(typeof(TAttribute), inherit: false).Length > 0;
        }

        private static string FormatMethod(MethodInfo method)
        {
            Type declaringType = method.DeclaringType;
            string declaringTypeName = declaringType == null ? "<unknown>" : declaringType.FullName;
            return $"{declaringTypeName}.{method.Name} returns {method.ReturnType.FullName}";
        }
    }
}

#endif
