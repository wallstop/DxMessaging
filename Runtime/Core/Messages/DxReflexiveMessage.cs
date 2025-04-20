namespace DxMessaging.Core.Messages
{
    using System;
    using Attributes;

    [Flags]
    public enum ReflexiveSendMode
    {
        [Obsolete("Please use a valid Send Mode")]
        None = 0,
        Flat = 1 << 0,
        Downwards = 1 << 1,
        Upwards = 1 << 2,
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
            foreach (Type type in _parameterTypes)
            {
                hashCode = hashCode * HashMultiplier + type.GetHashCode();
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

    [DxTargetedMessage]
    public readonly partial struct DxReflexiveMessage
    {
        public readonly string method;
        public readonly ReflexiveSendMode sendMode;
        public readonly object[] parameters;

        public readonly Type[] parameterTypes;

        public readonly MethodSignatureKey signatureKey;

        public DxReflexiveMessage(
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
    }
}
