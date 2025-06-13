namespace DxMessaging.Core.Attributes
{
    using System;

    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class DxOptionalParameterAttribute : Attribute
    {
        public DxOptionalParameterAttribute() { }
    }
}
