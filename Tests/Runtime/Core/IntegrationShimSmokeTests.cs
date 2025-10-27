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
                "DxMessaging.Unity.Integrations.Zenject.DxMessagingRegistrationInstaller",
                "WallstopStudios.DxMessaging.Zenject"
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
                "DxMessaging.Unity.Integrations.VContainer.VContainerRegistrationExtensions",
                "WallstopStudios.DxMessaging.VContainer"
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
                "DxMessaging.Unity.Integrations.Reflex.DxMessagingRegistrationInstaller",
                "WallstopStudios.DxMessaging.Reflex"
            );
#else
            Assert.Ignore("REFLEX_PRESENT not defined; skipping Reflex shim smoke test.");
#endif
        }

        private static void AssertIntegrationType(string typeName, string assemblyName)
        {
            string qualifiedName = $"{typeName}, {assemblyName}";
            Type type = Type.GetType(qualifiedName, throwOnError: false);
            Assert.IsNotNull(
                type,
                $"Expected type '{qualifiedName}' to be available when the corresponding scripting define is set."
            );
        }
    }
}
