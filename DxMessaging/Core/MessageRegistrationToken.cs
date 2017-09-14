using System;
using System.Collections.Generic;

namespace DxMessaging.Core
{
    /// <summary>
    /// Maintains all of the [de]registration logic for MessagingComponents. Wraps registrations up for lazy registration, which are executed on Enable() call.
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
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards it. 
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="targetedHandler">Actual handler functionality</param>
        public void RegisterTargeted<T>(Action<T> targetedHandler) where T : TargetedMessage
        {
            InternalRegister(targetedHandler, () => _messageHandler.RegisterTargetedMessageHandler(targetedHandler));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept UntargetedMessages of the given type.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="untargetedHandler">Actual handler functionality</param>
        public void RegisterUntargeted<T>(Action<T> untargetedHandler) where T : UntargetedMessage
        {
            InternalRegister(untargetedHandler, () => _messageHandler.RegisterUntargetedMessageHandler(untargetedHandler));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept TargetedMessages of the given type targeted towards anything (including itself).
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="messageHandler">Actual handler functionality</param>
        public void RegisterTargetedWithoutTargeting<T>(Action<T> messageHandler) where T : TargetedMessage
        {
            InternalRegister(messageHandler, () => _messageHandler.RegisterTargetedWithoutTargeting(messageHandler));
        }

        /// <summary>
        /// Stages a registration of the provided MessageHandler to accept every message that is broadcast.
        /// </summary>
        /// <note>
        /// DOES NOT ACTUALLY REGISTER THE HANDLER IF NOT ENABLED. To register, a call to Enable() is needed.
        /// </note>
        /// <typeparam name="T">Type of message that the handler accepts.</typeparam>
        /// <param name="globalAcceptAll">Actual handler functionality</param>
        public void RegisterGlobalAcceptAll(Action<AbstractMessage> globalAcceptAll)
        {
            InternalRegister(globalAcceptAll, () => _messageHandler.RegisterGlobalAcceptAll(globalAcceptAll));
        }

        /// <summary>
        /// Handles the actual [de]registration wrapping and (potential) lazy execution.
        /// </summary>
        /// <typeparam name="T">Type of message being registered.</typeparam>
        /// <param name="handler">Handler being registered.</param>
        /// <param name="registerAndGetDeregistration">Proxied registration function that returns a deregistration function.</param>
        private void InternalRegister<T>(Action<T> handler, Func<Action> registerAndGetDeregistration)
            where T : AbstractMessage
        {
            if (ReferenceEquals(handler, null))
            {
                throw new ArgumentNullException("handler");
            }
            bool newHandler = _sourceHandlersToWrappers.Add(handler);
            if (!newHandler)
            {
                // Nothing to do
                MessagingDebug.Log("Double registration of MessageHandler for {0} for {1} using {2}",
                    typeof (T), _messageHandler.Owner, registerAndGetDeregistration);
                return;
            }
            // We don't want to actually register at this time (might not be awake/enabled) - so we wrap that shit up, to lazy register when we're enabled.
            Action registration = () =>
            {
                Action actualDeregistration = registerAndGetDeregistration();
                _deregistrations.Add(actualDeregistration);
            };
            _registrations.Add(registration);

            // Generally, registrations should take place before all calls to enable. Just in case, though...
            if (_enabled)
            {
                registration();
            }
        }

        /// <summary>
        /// Enables the token if not already enabled. Executes all staged registrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
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
        /// Disables the token if not already disabled. Executes all staged deregistrations.
        /// </summary>
        /// <note>
        /// Idempotent.
        /// </note>
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
        /// Creates a MessagingRegistrationToken that operates on the given handler.
        /// </summary>
        /// <param name="messageHandler">Message handler to register handlers to.</param>
        /// <returns>MessagingRegistrationToken bound to the MessageHandler.</returns>
        public static MessageRegistrationToken Create(MessageHandler messageHandler)
        {
            if (ReferenceEquals(messageHandler, null))
            {
                throw new ArgumentNullException(string.Format("Cannot create a {0} with a null {1}",
                    typeof (MessageRegistrationToken), typeof (MessageHandler)));
            }
            return new MessageRegistrationToken(messageHandler);
        }
    }
}
