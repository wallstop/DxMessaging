namespace DxMessaging.Core
{
    using System;

    public readonly struct MessageRegistrationHandle : IEquatable<MessageRegistrationHandle>, IComparable<MessageRegistrationHandle>
    {
        private readonly Guid _handle;
        private readonly int _hashCode;

        public static MessageRegistrationHandle CreateMessageRegistrationHandle()
        {
            return new MessageRegistrationHandle(Guid.NewGuid());
        }

        private MessageRegistrationHandle(Guid handle)
        {
            _handle = handle;
            _hashCode = _handle.GetHashCode();
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object other)
        {
            return other is MessageRegistrationHandle handle && Equals(handle);
        }

        public bool Equals(MessageRegistrationHandle other)
        {
            return _handle.Equals(other._handle);
        }

        public int CompareTo(MessageRegistrationHandle other)
        {
            return _handle.CompareTo(other._handle);
        }
    }
}
