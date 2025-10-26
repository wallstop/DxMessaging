namespace DxMessaging.Tests.Runtime.Core
{
    using System;
    using NUnit.Framework;

    public sealed class IntegrationShimSmokeTests
    {
        [Test]
        public void ZenjectShimTypeAvailableWhenDefined()
        {
#if ZENJECT_PRESENT
            AssertIntegrationType(
                "DxMessaging.Unity.Integrations.Zenject.DxMessagingRegistrationInstaller"
            );
#else
            Assert.Ignore("ZENJECT_PRESENT not defined; skipping Zenject shim smoke test.");
#endif
        }

        [Test]
        public void VContainerShimTypeAvailableWhenDefined()
        {
#if VCONTAINER_PRESENT
            AssertIntegrationType(
                "DxMessaging.Unity.Integrations.VContainer.VContainerRegistrationExtensions"
            );
#else
            Assert.Ignore("VCONTAINER_PRESENT not defined; skipping VContainer shim smoke test.");
#endif
        }

        [Test]
        public void ReflexShimTypeAvailableWhenDefined()
        {
#if REFLEX_PRESENT
            AssertIntegrationType(
                "DxMessaging.Unity.Integrations.Reflex.DxMessagingRegistrationInstaller"
            );
#else
            Assert.Ignore("REFLEX_PRESENT not defined; skipping Reflex shim smoke test.");
#endif
        }

        private static void AssertIntegrationType(string typeName)
        {
            string qualifiedName = $"{typeName}, WallstopStudios.DxMessaging";
            Type type = Type.GetType(qualifiedName, throwOnError: false);
            Assert.IsNotNull(
                type,
                $"Expected type '{qualifiedName}' to be available when the corresponding scripting define is set."
            );
        }
    }
}
