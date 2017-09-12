using System;
using System.Collections.Generic;

namespace DxMessaging.Core
{
    /// <summary>
    /// Maintains all of the [de]registration logic for MessagingComponents.
    /// </summary>
    /// <note>
    /// General usage should be to create one of these on awake or start (probably start), and bind all messaging functions there.
    /// Then, on OnEnable(), call .Enable(), OnDisable(), call .Disable()
    /// </note>
    [Serializable]
    public sealed class MessageRegistrationToken
    {
        private readonly MessageHandler _messageHandler;

        private readonly HashSet<Delegate> _sourceHandlersToWrappers;
        private readonly List<Action> _registrations;
        private readonly List<Action> _deregistrations;
        private bool _enabled;

        public bool Enabled
        {
            get { return _enabled; }
        }

        private MessageRegistrationToken(MessageHandler messageHandler)
        {
            _messageHandler = messageHandler;
            _sourceHandlersToWrappers = new HashSet<Delegate>();
            _registrations = new List<Action>();
            _deregistrations = new List<Action>();
            _enabled = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="messageHandler"></param>
        public void RegisterTargeted<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            InternalRegister(messageHandler, () => _messageHandler.RegisterTargetedMessageHandler(messageHandler));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="messageHandler"></param>
        public void RegisterUntargeted<T>(Action<T> messageHandler) where T : UntargetedMessage
        {
            InternalRegister(messageHandler, () => _messageHandler.RegisterUntargetedMessageHandler(messageHandler));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="messageHandler"></param>
        public void RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            InternalRegister(messageHandler, () => _messageHandler.RegisterTargetedWithoutTargeting(messageHandler));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="globalAcceptAll"></param>
        public void RegisterGlobalAcceptAll(Action<AbstractMessage> globalAcceptAll)
        {
            InternalRegister(globalAcceptAll, () => _messageHandler.RegisterGlobalAcceptAll(globalAcceptAll));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handler"></param>
        /// <param name="registerAndGetDeregistration"></param>
        private void InternalRegister<T>(Action<T> handler, Func<Action> registerAndGetDeregistration)
            where T : AbstractMessage
        {
            bool newHandler = _sourceHandlersToWrappers.Add(handler);
            if (!newHandler)
            {
                MessagingDebug.Log("Double registration of MessageHandler for {0} for {1} using {2}",
                    typeof (T), _messageHandler.Owner, registerAndGetDeregistration);
                return;
            }
            // We don't want to actually register at this time (might not be awake/enabled) - so we wrap that shit up, to lazy register when we're enabled
            Action registration = () =>
            {
                Action actualDeregistration = registerAndGetDeregistration();
                _deregistrations.Add(actualDeregistration);
            };
            _registrations.Add(registration);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Enable()
        {
            if (_enabled)
            {
                MessagingDebug.Log("Ignoring double enablement of Messaging for {0}", _messageHandler.Owner);
                return;
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < _registrations.Count; ++i)
            {
                _registrations[i]();
            }
            _enabled = true;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Disable()
        {
            if (!_enabled)
            {
                MessagingDebug.Log("Ignoring pointless disabling of Messaging for {0}", _messageHandler.Owner);
                return;
            }
            // ReSharper disable once ForCanBeConvertedToForeach
            for (int i = 0; i < _deregistrations.Count; ++i)
            {
                _deregistrations[i]();
            }
            _enabled = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="messageHandler"></param>
        /// <returns></returns>
        public static MessageRegistrationToken Create(MessageHandler messageHandler)
        {
            if (messageHandler == null)
            {
                throw new ArgumentNullException(string.Format("Cannot create a {0} with a null {1}",
                    typeof (MessageRegistrationToken), typeof (MessageHandler)));
            }
            return new MessageRegistrationToken(messageHandler);
        }
    }
}
