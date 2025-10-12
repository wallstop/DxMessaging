namespace DxMessaging.Core.Attributes
{
    using System;

    /// <summary>
    /// Marks a field as optional when used with <see cref="DxAutoConstructorAttribute"/>.
    /// </summary>
    /// <remarks>
    /// The source generator will emit a constructor parameter with a default value for fields annotated
    /// with this attribute. This is helpful for messages with sensible defaults.
    /// </remarks>
    /// <example>
    /// <code>
    /// [DxMessaging.Core.Attributes.DxAutoConstructor]
    /// public readonly struct Example
    /// {
    ///     public readonly int required;
    ///     [DxMessaging.Core.Attributes.DxOptionalParameter] public readonly int optional; // defaults to 0
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class DxOptionalParameterAttribute : Attribute { }
}
