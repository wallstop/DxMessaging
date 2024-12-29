namespace DxMessaging.Core
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A light abstraction layer over Unity's InstanceId. Meant to uniquely identify a game object.
    /// </summary>
    [Serializable]
    [DataContract]
    public readonly struct InstanceId : IComparable, IComparable<InstanceId>, IEquatable<InstanceId>
    {
        public static readonly InstanceId EmptyId = new(0);

        [JsonInclude]
        [JsonPropertyName("id")]
        [DataMember(Name = "id")]
        public int Id => _id;

        [JsonIgnore]
        private readonly int _id;

#if UNITY_2017_1_OR_NEWER
        public UnityEngine.Object Object { get; }
#endif

        [JsonConstructor]
        public InstanceId(int id)
        {
            _id = id;
#if UNITY_2017_1_OR_NEWER
            Object = null;
#endif
        }

#if UNITY_2017_1_OR_NEWER
        private InstanceId(UnityEngine.Object @object)
        {
            _id = @object.GetInstanceID();
            Object = @object;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator InstanceId(UnityEngine.GameObject gameObject)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            return new InstanceId(gameObject);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator InstanceId(UnityEngine.Component component)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            return new InstanceId(component);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(InstanceId other)
        {
            return _id == other._id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object other)
        {
            return other is InstanceId id && Equals(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _id;
        }

        public override string ToString()
        {
#if UNITY_2017_1_OR_NEWER
            UnityEngine.Object instance = Object;
            string objectName = instance == null ? string.Empty : instance.name;
            return $"{{\"Id\": {_id}, \"Name\": \"{objectName}\"}}";
#else
            return $"{{\"Id\": {_id}}}";
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstanceId lhs, InstanceId rhs)
        {
            return lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstanceId lhs, InstanceId rhs)
        {
            return !lhs.Equals(rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(InstanceId lhs, InstanceId rhs)
        {
            return lhs.CompareTo(rhs) < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(InstanceId lhs, InstanceId rhs)
        {
            return lhs.CompareTo(rhs) <= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(InstanceId lhs, InstanceId rhs)
        {
            return lhs.CompareTo(rhs) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(InstanceId lhs, InstanceId rhs)
        {
            return lhs.CompareTo(rhs) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(InstanceId other)
        {
            return _id.CompareTo(other._id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(object rhs)
        {
            if (rhs is InstanceId other)
            {
                return CompareTo(other);
            }
            return -1;
        }
    }
}
