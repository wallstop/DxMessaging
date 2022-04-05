namespace DxMessaging.Core
{
    using System;
    using System.Runtime.Serialization;
    using global::Core.Extension;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A light abstraction layer over Unity's InstanceId. Meant to uniquely identify a game object.
    /// </summary>
    [Serializable]
    [DataContract]
    public readonly struct InstanceId : IComparable, IComparable<InstanceId>, IEquatable<InstanceId>
    {
        public static readonly InstanceId EmptyId = new(0);

        [DataMember(Name = "id")]
        private readonly long _id;

        public Object Object { get; }

        private InstanceId(long id) : this()
        {
            _id = id;
            Object = null;
        }

        private InstanceId(Object @object) : this()
        {
            _id = @object.GetInstanceID();
            Object = @object;
        }

        public static implicit operator InstanceId(GameObject gameObject)
        {
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            return new InstanceId(gameObject);
        }

        public static implicit operator InstanceId(Component component)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            return new InstanceId(component);
        }

        public int CompareTo(object rhs)
        {
            if (rhs is InstanceId other)
            {
                return CompareTo(other);
            }
            return -1;
        }

        public bool Equals(InstanceId other)
        {
            return _id == other._id;
        }

        public override bool Equals(object other)
        {
            return other is InstanceId id && Equals(id);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public override string ToString()
        {
            Object instance = Object;
            string objectName = instance == null ? string.Empty : instance.name;
            return new
            {
                Id = _id,
                Name = objectName,
            }.ToJson();
        }

        public static bool operator ==(InstanceId lhs, InstanceId rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(InstanceId lhs, InstanceId rhs)
        {
            return !(lhs == rhs);
        }

        public int CompareTo(InstanceId other)
        {
            return _id.CompareTo(other._id);
        }
    }
}
