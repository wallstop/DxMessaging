namespace Tests.Runtime.Scripts.Components
{
    using System;
    using DxMessaging.Core;
    using DxMessaging.Unity;
    using Messages;

    public sealed class SimpleMessageAwareComponent : MessageAwareComponent
    {
        public bool SlowComplexTargetingEnabled
        {
            get => _slowComplexTargetingEnabled;
            set
            {
                _slowComplexTargetingEnabled = value;
                ToggleTargetedRegistration();
            }
        }
        public bool FastComplexTargetingEnabled
        {
            get => _fastComplexTargetingEnabled;
            set
            {
                _fastComplexTargetingEnabled = value;
                ToggleTargetedRegistration();
            }
        }

        public Action untargetedHandler;
        public Action targetedHandler;
        public Action slowTargetedHandler;
        public Action targetedWithoutTargetingHandler;
        public Action slowComplexTargetedHandler;
        public Action complexTargetedHandler;
        public Action broadcastHandler;
        public Action broadcastWithoutSourceHandler;
        public Action componentTargetedHandler;
        public Action componentBroadcastHandler;

        private bool _slowComplexTargetingEnabled = true;
        private bool _fastComplexTargetingEnabled = true;

        private MessageRegistrationHandle? _slowComplexTargetingHandle;
        private MessageRegistrationHandle? _fastComplexTargetingHandle;

        protected override void RegisterMessageHandlers()
        {
            _ = _messageRegistrationToken.RegisterUntargeted<SimpleUntargetedMessage>(HandleSimpleUntargetedMessage);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(gameObject, HandleSimpleTargetedMessage);
            _ = _messageRegistrationToken.RegisterGameObjectTargeted<SimpleTargetedMessage>(gameObject, HandleSlowSimpleTargetedMessage);
            _ = _messageRegistrationToken.RegisterTargetedWithoutTargeting<SimpleTargetedMessage>(HandleSimpleTargetedWithoutTargetingMessage);
            _fastComplexTargetingHandle = _messageRegistrationToken.RegisterGameObjectTargeted<ComplexTargetedMessage>(gameObject, HandleComplexTargetedMessage);
            _slowComplexTargetingHandle = _messageRegistrationToken.RegisterGameObjectTargeted<ComplexTargetedMessage>(gameObject, HandleSlowComplexTargetedMessage);
            _ = _messageRegistrationToken.RegisterGameObjectBroadcast<SimpleBroadcastMessage>(gameObject, HandleSimpleBroadcastMessage);
            _ = _messageRegistrationToken.RegisterBroadcastWithoutSource<SimpleBroadcastMessage>(HandleSimpleBroadcastWithoutSourceMessage);
            _ = _messageRegistrationToken.RegisterComponentTargeted<SimpleTargetedMessage>(this, HandleSimpleComponentTargetedMessage);
            _ = _messageRegistrationToken.RegisterComponentBroadcast<SimpleBroadcastMessage>(this, HandleSimpleComponentBroadcastMessage);
        }

        private void ToggleTargetedRegistration()
        {
            if (SlowComplexTargetingEnabled)
            {
                _slowComplexTargetingHandle ??= _messageRegistrationToken.RegisterGameObjectTargeted<ComplexTargetedMessage>(gameObject, HandleSlowComplexTargetedMessage);
            }
            else if (_slowComplexTargetingHandle != null)
            {
                _messageRegistrationToken.RemoveRegistration(_slowComplexTargetingHandle.Value);
                _slowComplexTargetingHandle = null;
            }

            if (FastComplexTargetingEnabled)
            {
                _fastComplexTargetingHandle ??= _messageRegistrationToken.RegisterGameObjectTargeted<ComplexTargetedMessage>(gameObject, HandleComplexTargetedMessage);
            }
            else if (_fastComplexTargetingHandle != null)
            {
                _messageRegistrationToken.RemoveRegistration(_fastComplexTargetingHandle.Value);
                _fastComplexTargetingHandle = null;
            }
        }

        public void HandleSlowComplexTargetedMessage(ComplexTargetedMessage message)
        {
            slowComplexTargetedHandler?.Invoke();
        }

        public void HandleComplexTargetedMessage(ref ComplexTargetedMessage message)
        {
            complexTargetedHandler?.Invoke();
        }

        public void HandleSlowSimpleTargetedMessage(SimpleTargetedMessage message)
        {
            slowTargetedHandler?.Invoke();
        }

        public void HandleSimpleUntargetedMessage(ref SimpleUntargetedMessage message)
        {
            untargetedHandler?.Invoke();
        }

        public void HandleSimpleTargetedMessage(ref SimpleTargetedMessage message)
        {
            targetedHandler?.Invoke();
        }

        public void HandleSimpleTargetedWithoutTargetingMessage(ref InstanceId target, ref SimpleTargetedMessage message)
        {
            targetedWithoutTargetingHandler?.Invoke();
        }

        public void HandleSimpleBroadcastMessage(ref SimpleBroadcastMessage message)
        {
            broadcastHandler?.Invoke();
        }

        public void HandleSimpleBroadcastWithoutSourceMessage(ref InstanceId source, ref SimpleBroadcastMessage message)
        {
            broadcastWithoutSourceHandler?.Invoke();
        }

        public void HandleSimpleComponentTargetedMessage(ref SimpleTargetedMessage message)
        {
            componentTargetedHandler?.Invoke();
        }

        public void HandleSimpleComponentBroadcastMessage(ref SimpleBroadcastMessage message)
        {
            componentBroadcastHandler?.Invoke();
        }
    }
}
