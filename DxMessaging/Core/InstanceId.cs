using System;

namespace DxMessaging.Core
{
    /// <summary>
    /// A light abstraction layer over Unity's InstanceId. Meant to uniquely identify a game object.
    /// </summary>
    [Serializable]
    // ReSharper disable once InheritdocConsiderUsage
    public struct InstanceId : IComparable, IEquatable<InstanceId>
    {
        public static readonly InstanceId InvalidId = new InstanceId(long.MinValue);

        private readonly long _id;

        private InstanceId(long id) : this()
        {
            _id = id;
        }

        private InstanceId(int id) : this()
        {
            _id = id;
        }

        public int CompareTo(object rhs)
        {
            if (rhs is InstanceId)
            {
                InstanceId other = (InstanceId) rhs;
                return _id.CompareTo(other._id);
            }
            return -1;
        }

        public bool Equals(InstanceId other)
        {
            return _id == other._id;
        }

        public override bool Equals(object other)
        {
            return other is InstanceId && Equals((InstanceId) other);
        }

        public override int GetHashCode()
        {
            return _id.GetHashCode();
        }

        public override string ToString()
        {
            return "Id: " + _id;
        }

        public static implicit operator InstanceId(int id)
        {
            return new InstanceId(id);
        }

        public static implicit operator InstanceId(long id)
        {
            return new InstanceId(id);
        }

        public static bool operator ==(InstanceId lhs, InstanceId rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(InstanceId lhs, InstanceId rhs)
        {
            return !(lhs == rhs);
        }

        public static implicit operator bool(InstanceId id)
        {
            return id != InvalidId;
        }
    }
}
