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
    /// [DxAutoConstructor]
    /// public readonly struct Example
    /// {
    ///     public readonly int required;
    ///     [DxOptionalParameter] public readonly int optional; // defaults to 0
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class DxOptionalParameterAttribute : Attribute
    {
        /// <summary>
        /// Marks the field as optional with the type's default value.
        /// </summary>
        public DxOptionalParameterAttribute() { }

        /// <summary>
        /// Optional default value overloads. Values must be compile-time constants and
        /// will be validated by the source generator against the field type.
        /// </summary>
        public DxOptionalParameterAttribute(bool value) { }

        public DxOptionalParameterAttribute(char value) { }

        public DxOptionalParameterAttribute(string value) { }

        public DxOptionalParameterAttribute(byte value) { }

        public DxOptionalParameterAttribute(sbyte value) { }

        public DxOptionalParameterAttribute(short value) { }

        public DxOptionalParameterAttribute(ushort value) { }

        public DxOptionalParameterAttribute(int value) { }

        public DxOptionalParameterAttribute(uint value) { }

        public DxOptionalParameterAttribute(long value) { }

        public DxOptionalParameterAttribute(ulong value) { }

        public DxOptionalParameterAttribute(float value) { }

        public DxOptionalParameterAttribute(double value) { }

        /// <summary>
        /// Advanced: supply a C# expression to use as the default value.
        /// For example: <c>Expression = "null"</c>, <c>Expression = "nameof(SomeConst)"</c>, or <c>Expression = "MyEnum.Value"</c>.
        /// The generator inserts this expression verbatim and the C# compiler enforces type safety.
        /// </summary>
        public string Expression { get; set; }
    }
}
