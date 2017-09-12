using System;

namespace DxMessaging.Core
{
    /// <inheritdoc cref="AbstractMessage" />
    /// <summary>
    /// Used to specify general-purposes messages that are meant to be sent to a specific entity.
    /// </summary>
    /// <note>
    /// TargetedMessages should be thought of as commands.
    /// Inheritance should be completely flat. Ie, UntargetedMessages should be the direct parent of every implementer.
    /// </note>
    [Serializable]
    public abstract class TargetedMessage : AbstractMessage, IEquatable<TargetedMessage>
    {
        /// <summary>
        /// The Id of the GameObject that this is intended for.
        /// </summary>
        /// <note>
        /// Should never be changed - public and non-readonly for serialization purposes.
        /// </note>
        public InstanceId Target;

        /// <inheritdoc />
        /// <summary>
        /// All inherited classes should be constructed with a valid target.
        /// </summary>
        /// <param name="target">The Id of the GameObject that this is intended for.</param>
        protected TargetedMessage(InstanceId target)
        {
            Target = target;
        }

        public override bool Equals(object obj)
        {
            TargetedMessage other = obj as TargetedMessage;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return Target.GetHashCode();
        }

        public bool Equals(TargetedMessage other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            return Target.Equals(other.Target);
        }
    }
}
