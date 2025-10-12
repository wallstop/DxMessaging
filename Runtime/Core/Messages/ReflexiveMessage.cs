namespace DxMessaging.Core.Messages
{
    using System;

    /// <summary>
    /// Controls the traversal pattern for Unity-style reflexive sends.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="ReflexiveMessage"/> to determine which components receive the reflected call.
    /// - <c>Flat</c>: only the immediate <see cref="UnityEngine.GameObject"/>.
    /// - <c>Upwards</c>: up the transform parent chain.
    /// - <c>Downwards</c>: down into children.
    /// - <c>OnlyIncludeActive</c>: exclude disabled components when sending.
    /// Modes can be combined via flags. See examples in <see cref="ReflexiveMessage"/>.
    /// </remarks>
    [Flags]
    public enum ReflexiveSendMode
    {
        [Obsolete("Please use a valid Send Mode")]
        None = 0,
        Flat = 1 << 0,
        Downwards = 1 << 1,
        Upwards = 1 << 2,
        OnlyIncludeActive = 1 << 3,
    }

    public readonly struct MethodSignatureKey : IEquatable<MethodSignatureKey>
    {
        private const int HashBase = 5556137;
        private const int HashMultiplier = 95785853;

        private readonly string _methodName;
        private readonly Type[] _parameterTypes;

        private readonly int _hashCode;

        public MethodSignatureKey(string methodName, Type[] parameterTypes)
            : this()
        {
            _methodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
            _parameterTypes = parameterTypes;
            _hashCode = CalculateHashCode();
        }

        private int CalculateHashCode()
        {
            int hashCode = HashBase + _methodName.GetHashCode();
            ReadOnlySpan<Type> types = _parameterTypes.AsSpan();
            for (int i = 0; i < types.Length; ++i)
            {
                hashCode = hashCode * HashMultiplier + types[i].GetHashCode();
            }

            return hashCode;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            return obj is MethodSignatureKey other && Equals(other);
        }

        public bool Equals(MethodSignatureKey other)
        {
            if (
                _parameterTypes.Length != other._parameterTypes.Length
                || _methodName != other._methodName
            )
            {
                return false;
            }

            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int i = 0; i < _parameterTypes.Length; ++i)
            {
                if (_parameterTypes[i] != other._parameterTypes[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static bool operator ==(MethodSignatureKey left, MethodSignatureKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MethodSignatureKey left, MethodSignatureKey right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Unity helper message that reflects to component methods by name.
    /// </summary>
    /// <remarks>
    /// This message is handled specially by the bus to mimic Unity's <c>SendMessage*</c> patterns while still
    /// flowing through DxMessaging pipelines (interceptors, post-processors, diagnostics).
    /// It resolves methods on components on or around the targeted <see cref="UnityEngine.GameObject"/> based on
    /// <see cref="sendMode"/> and invokes them with provided <see cref="parameters"/>.
    /// Prefer type-safe message contracts in production; use this to integrate with legacy patterns.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Call "OnHit(int amount)" upwards in the hierarchy
    /// var msg = new DxMessaging.Core.Messages.ReflexiveMessage("OnHit", DxMessaging.Core.Messages.ReflexiveSendMode.Upwards, 10);
    /// msg.EmitGameObjectTargeted(gameObject);
    ///
    /// // Call "OnInteract()" on children only if active
    /// var msg2 = new DxMessaging.Core.Messages.ReflexiveMessage(
    ///     "OnInteract",
    ///     DxMessaging.Core.Messages.ReflexiveSendMode.Downwards | DxMessaging.Core.Messages.ReflexiveSendMode.OnlyIncludeActive
    /// );
    /// msg2.EmitGameObjectTargeted(gameObject);
    /// </code>
    /// </example>
    public readonly struct ReflexiveMessage : ITargetedMessage<ReflexiveMessage>
    {
        public Type MessageType => typeof(ReflexiveMessage);

        public readonly string method;
        public readonly ReflexiveSendMode sendMode;
        public readonly object[] parameters;
        public readonly Type[] parameterTypes;

        public readonly MethodSignatureKey signatureKey;

        /// <summary>
        /// Creates a reflexive message resolving parameter types automatically.
        /// </summary>
        /// <param name="method">Method name to invoke on recipients.</param>
        /// <param name="sendMode">Traversal mode for recipient discovery.</param>
        /// <param name="parameters">Arguments passed to the method.</param>
        public ReflexiveMessage(
            string method,
            ReflexiveSendMode sendMode,
            params object[] parameters
        )
        {
            this.method = method;
            this.sendMode = sendMode;
            this.parameters = parameters;

            if (0 < parameters.Length)
            {
                int parameterCount = parameters.Length;
                parameterTypes = new Type[parameterCount];
                for (int i = 0; i < parameterCount; i++)
                {
                    object parameter = parameters[i];
                    if (parameter == null)
                    {
                        throw new ArgumentNullException(
                            $"Parameter at index {i} is null, cannot resolve type!"
                        );
                    }
                    parameterTypes[i] = parameter.GetType();
                }
            }
            else
            {
                parameterTypes = Array.Empty<Type>();
            }
            signatureKey = new MethodSignatureKey(method, parameterTypes);
        }

        /// <summary>
        /// Creates a reflexive message with explicit parameter type metadata.
        /// </summary>
        /// <param name="method">Method name to invoke on recipients.</param>
        /// <param name="sendMode">Traversal mode for recipient discovery.</param>
        /// <param name="parameters">Arguments passed to the method.</param>
        /// <param name="parameterTypes">Explicit types for <paramref name="parameters"/> in matching order.</param>
        public ReflexiveMessage(
            string method,
            ReflexiveSendMode sendMode,
            object[] parameters,
            Type[] parameterTypes
        )
        {
            if (parameters.Length != parameterTypes.Length)
            {
                throw new ArgumentException(
                    $"Parameter length {parameters.Length} does not match parameter length {parameterTypes.Length}"
                );
            }
            this.method = method;
            this.sendMode = sendMode;
            this.parameters = parameters;
            this.parameterTypes = parameterTypes;
            signatureKey = new MethodSignatureKey(method, parameterTypes);
        }
    }
}
