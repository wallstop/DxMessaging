namespace DxMessaging.Tests.Runtime.Scripts.Components
{
    using UnityEngine;

    public sealed class ReflexiveReceiverComponent : MonoBehaviour
    {
        public int InvocationCount { get; private set; }

        public void OnReflexive()
        {
            InvocationCount++;
        }
    }
}
