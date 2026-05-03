namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using DxMessaging.Unity;

    /// <summary>
    /// Test fixture component exposing a way to invoke the otherwise-protected
    /// <see cref="MessageAwareComponent.OnApplicationQuit"/> hook so tests can
    /// drive the quit lifecycle without spinning up a full Unity quit sequence.
    /// </summary>
    public sealed class QuitOnDemandMessageAwareComponent : MessageAwareComponent
    {
        /// <summary>
        /// Forwards to the protected <see cref="OnApplicationQuit"/> override
        /// so the test fixture can drive the lifecycle hook without
        /// terminating the Unity Editor.
        /// </summary>
        public void RaiseOnApplicationQuit()
        {
            OnApplicationQuit();
        }
    }
}
