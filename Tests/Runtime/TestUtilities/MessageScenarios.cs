#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime
{
    using System.Collections.Generic;

    /// <summary>
    /// Property-based <see cref="IEnumerable{T}"/> sources for NUnit
    /// <c>[ValueSource]</c>. Each property exposes the cases for one scenario
    /// permutation set; properties are used so the source resolves by name and
    /// returns a fresh enumeration to the test runner on every access.
    /// </summary>
    /// <example>
    /// <code>
    /// [Test]
    /// public void DispatchAcrossKinds(
    ///     [ValueSource(typeof(MessageScenarios), nameof(MessageScenarios.AllKinds))]
    ///     MessageScenario scenario)
    /// {
    ///     // ...
    /// }
    /// </code>
    /// </example>
    public static class MessageScenarios
    {
        public static IEnumerable<MessageScenario> AllKinds
        {
            get
            {
                yield return MessageScenario.Untargeted();
                yield return MessageScenario.Targeted();
                yield return MessageScenario.Broadcast();
            }
        }

        public static IEnumerable<MessageScenario> KindsWithComponentTarget
        {
            get
            {
                yield return MessageScenario.Targeted();
                yield return MessageScenario.Broadcast();
            }
        }

        public static IEnumerable<MessageScenario> WithAndWithoutInterceptor
        {
            get
            {
                foreach (MessageScenario scenario in AllKinds)
                {
                    yield return scenario.WithInterceptor(false);
                    yield return scenario.WithInterceptor(true);
                }
            }
        }

        public static IEnumerable<MessageScenario> WithAndWithoutPostProcessor
        {
            get
            {
                foreach (MessageScenario scenario in AllKinds)
                {
                    yield return scenario.WithPostProcessor(false);
                    yield return scenario.WithPostProcessor(true);
                }
            }
        }

        public static IEnumerable<MessageScenario> WithDiagnosticsToggle
        {
            get
            {
                foreach (MessageScenario scenario in AllKinds)
                {
                    yield return scenario.WithDiagnostics(false);
                    yield return scenario.WithDiagnostics(true);
                }
            }
        }
    }
}
#endif
