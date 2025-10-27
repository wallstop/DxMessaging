namespace DxMessaging.Core.Attributes
{
    using System;

    /// <summary>
    /// Instructs the source generator to synthesize a constructor matching the fields on the type.
    /// </summary>
    /// <remarks>
    /// Useful for keeping message types concise and immutable. Apply this to a <c>struct</c> or <c>class</c>
    /// and a constructor will be generated that assigns all fields in declaration order. Fields marked with
    /// <see cref="DxOptionalParameterAttribute"/> are emitted as optional parameters.
    /// </remarks>
    /// <example>
    /// <code>
    /// [DxMessaging.Core.Attributes.DxUntargetedMessage]
    /// [DxMessaging.Core.Attributes.DxAutoConstructor]
    /// public readonly partial struct VideoSettingsChanged
    /// {
    ///     public readonly int width;
    ///     public readonly int height;
    ///     [DxMessaging.Core.Attributes.DxOptionalParameter] public readonly bool fullscreen; // becomes optional
    /// }
    /// // Generated ctor: VideoSettingsChanged(int width, int height, bool fullscreen = default)
    /// </code>
    /// </example>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = false,
        AllowMultiple = false
    )]
    public sealed class DxAutoConstructorAttribute : Attribute { }
}
