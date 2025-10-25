namespace DxMessaging.Unity
{
    using Core;
    using Core.MessageBus;
    using Core.Messages;
    using UnityEngine;

    /// <summary>
    /// Base MonoBehaviour that wires up a <see cref="MessagingComponent"/> and a registration token.
    /// </summary>
    /// <remarks>
    /// Derive from this to quickly make components that participate in DxMessaging without boilerplate.
    /// Override <see cref="RegisterMessageHandlers"/> to stage your registrations. By default this class
    /// subscribes to <see cref="Core.Messages.StringMessage"/> in a few common forms to demonstrate usage.
    ///
    /// Lifecycle integration:
    /// - <see cref="Awake"/> creates the token and calls <see cref="RegisterMessageHandlers"/>.
    /// - <see cref="OnEnable"/>/<see cref="OnDisable"/> enable/disable the token when
    ///   <see cref="MessageRegistrationTiedToEnableStatus"/> is true.
    /// - <see cref="OnDestroy"/> disables the token and releases references.
    /// </remarks>
    /// <example>
    /// <code>
    /// public sealed class HealthComponent : DxMessaging.Unity.MessageAwareComponent
    /// {
    ///     protected override void RegisterMessageHandlers()
    ///     {
    ///         base.RegisterMessageHandlers();
    ///         // Listen for targeted damage commands to this component
    ///         _ = Token.RegisterComponentTargeted&lt;TookDamage&gt;(this, HandleDamage);
    ///         // Listen for global difficulty changes
    ///         _ = Token.RegisterUntargeted&lt;DifficultyChanged&gt;(HandleDifficulty);
    ///     }
    ///
    ///     private void HandleDamage(ref TookDamage msg)
    ///     {
    ///         // apply damage
    ///     }
    ///
    ///     private void HandleDifficulty(ref DifficultyChanged msg)
    ///     {
    ///         // adjust health scaling
    ///     }
    /// }
    /// </code>
    /// </example>
    [RequireComponent(typeof(MessagingComponent))]
    public abstract class MessageAwareComponent : MonoBehaviour
    {
        /// <summary>
        /// Accessor for the token that manages registrations for this component.
        /// </summary>
        public virtual MessageRegistrationToken Token => _messageRegistrationToken;

        protected MessageRegistrationToken _messageRegistrationToken;

        /// <summary>
        /// If true, will register/unregister handles when the component is enabled or disabled.
        /// </summary>
        protected virtual bool MessageRegistrationTiedToEnableStatus => true;

        /// <summary>
        /// If true, registers demo handlers for <see cref="Core.Messages.StringMessage"/> and
        /// <see cref="Core.Messages.GlobalStringMessage"/>. Override and return <c>false</c> to disable.
        /// </summary>
        protected virtual bool RegisterForStringMessages => true;

        protected MessagingComponent _messagingComponent;
        protected IMessageBus _configuredMessageBus;

        /// <summary>
        /// Creates the <see cref="MessagingComponent"/>, token, and calls <see cref="RegisterMessageHandlers"/>.
        /// </summary>
        protected virtual void Awake()
        {
            _messagingComponent = GetComponent<MessagingComponent>();
            if (_configuredMessageBus != null)
            {
                _messagingComponent.Configure(_configuredMessageBus);
            }
            _messageRegistrationToken = _messagingComponent.Create(this);
            RegisterMessageHandlers();
        }

        /// <summary>
        /// Stage message registrations for this component. Called from <see cref="Awake"/>.
        /// </summary>
        protected virtual void RegisterMessageHandlers()
        {
            if (RegisterForStringMessages)
            {
                _ = _messageRegistrationToken.RegisterGameObjectTargeted<StringMessage>(
                    gameObject,
                    HandleStringGameObjectMessage
                );
                _ = _messageRegistrationToken.RegisterComponentTargeted<StringMessage>(
                    this,
                    HandleStringComponentMessage
                );
                _ = _messageRegistrationToken.RegisterUntargeted<GlobalStringMessage>(
                    HandleGlobalStringMessage
                );
            }
        }

        /// <summary>
        /// Enables the token if <see cref="MessageRegistrationTiedToEnableStatus"/> is true.
        /// </summary>
        protected virtual void OnEnable()
        {
            if (MessageRegistrationTiedToEnableStatus)
            {
                _messageRegistrationToken?.Enable();
            }
        }

        /// <summary>
        /// Disables the token if <see cref="MessageRegistrationTiedToEnableStatus"/> is true.
        /// </summary>
        protected virtual void OnDisable()
        {
            if (MessageRegistrationTiedToEnableStatus)
            {
                _messageRegistrationToken?.Disable();
            }
        }

        /// <summary>
        /// Ensures deregistration and clears the token on destroy.
        /// </summary>
        protected virtual void OnDestroy()
        {
            if (_messagingComponent != null)
            {
                _messagingComponent.Release(this);
            }

            _messageRegistrationToken?.Disable();
            _messageRegistrationToken = null;
            _messagingComponent = null;
        }

        /// <summary>
        /// Receives the application quit message to avoid spurious Unity SendMessage warnings in tests.
        /// No default behavior.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            // Intentionally left blank
        }

        /// <summary>
        /// Supplies a custom <see cref="IMessageBus"/> for this component's underlying <see cref="MessageHandler"/>.
        /// </summary>
        /// <param name="messageBus">
        /// Container-managed bus to use. Pass <see langword="null"/> to revert to the global bus
        /// returned by <see cref="MessageHandler.MessageBus"/>.
        /// </param>
        /// <remarks>
        /// Call this during dependency injection (before <see cref="Awake"/>) to ensure the token is created against
        /// the provided bus, or invoke it later to retarget existing registrations.
        /// </remarks>
        public virtual void ConfigureMessageBus(IMessageBus messageBus)
        {
            _configuredMessageBus = messageBus;
            _messagingComponent?.Configure(_configuredMessageBus);
        }

        /// <summary>
        /// Demo handler: targeted string message to this GameObject.
        /// Override to implement behavior.
        /// </summary>
        protected virtual void HandleStringGameObjectMessage(ref StringMessage message)
        {
            // No-op by default
        }

        /// <summary>
        /// Demo handler: targeted string message to this Component.
        /// Override to implement behavior.
        /// </summary>
        protected virtual void HandleStringComponentMessage(ref StringMessage message)
        {
            // No-op by default
        }

        /// <summary>
        /// Demo handler: global string message.
        /// Override to implement behavior.
        /// </summary>
        protected virtual void HandleGlobalStringMessage(ref GlobalStringMessage message)
        {
            // No-op by default
        }
    }
}
