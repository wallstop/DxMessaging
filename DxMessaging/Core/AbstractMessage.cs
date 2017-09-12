using System;

namespace DxMessaging.Core
{
    /// <summary>
    /// Common base class for all Messaging needs. A common base lets us share some implementation details with type safety.
    /// </summary>
    [Serializable]
    public abstract class AbstractMessage
    {
        [NonSerialized] private string _simpleTypeName;

        /// <summary>
        /// Lazy-loaded typename, cached for performance.
        /// </summary>
        public string TypeName
        {
            get
            {
                if (ReferenceEquals(_simpleTypeName, null))
                {
                    _simpleTypeName = GetType().Name;
                }
                return _simpleTypeName;
            }
        }

        /// <summary>
        /// Shhh, no need to make this public.
        /// </summary>
        protected AbstractMessage() { }
    }
}
