namespace DxMessaging.Core
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;

    /// <summary>
    /// Lightweight wrapper around Unity instance IDs to uniquely identify objects.
    /// </summary>
    /// <remarks>
    /// In Unity builds, this struct also carries a reference to the underlying <see cref="UnityEngine.Object"/>.
    /// It supports implicit conversions from <see cref="UnityEngine.GameObject"/> and <see cref="UnityEngine.Component"/>,
    /// and provides value semantics for hashing, equality, and ordering. Outside Unity, it stores only the integer ID.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Unity: send a targeted message to a GameObject
    /// var msg = new DxMessaging.Core.Messages.StringMessage("Hello");
    /// msg.EmitGameObjectTargeted(gameObject); // implicit conversion from GameObject handled by extension overload
    /// </code>
    /// </example>
    [Serializable]
    [DataContract]
    public readonly struct InstanceId : IComparable, IComparable<InstanceId>, IEquatable<InstanceId>
    {
        public static readonly InstanceId EmptyId = new(0);

        [DataMember(Name = "id")]
        public int Id => _id;

        private readonly int _id;

#if UNITY_2021_3_OR_NEWER
        // ReSharper disable once InconsistentNaming
        public readonly UnityEngine.Object Object;
#endif

        public InstanceId(int id)
        {
            _id = id;
#if UNITY_2021_3_OR_NEWER
            Object = null;
#endif
        }

#if UNITY_2021_3_OR_NEWER
        private InstanceId(UnityEngine.Object unityObject)
        {
            _id = unityObject.GetInstanceID();
            Object = unityObject;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator InstanceId(UnityEngine.GameObject gameObject)
        {
            return new InstanceId(gameObject);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator InstanceId(UnityEngine.Component component)
        {
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
#if UNITY_2021_3_OR_NEWER
            UnityEngine.Object instance = Object;
            string objectName = instance == null ? string.Empty : instance.name;
            return new { Id = _id, Name = objectName }.ToString();
#else
            return new { Id = _id }.ToString();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id == rhs._id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id != rhs._id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id.CompareTo(rhs._id) < 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id.CompareTo(rhs._id) <= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id.CompareTo(rhs._id) > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(InstanceId lhs, InstanceId rhs)
        {
            return lhs._id.CompareTo(rhs._id) >= 0;
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
