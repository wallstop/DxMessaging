using DxMessaging.Core.Attributes;

[DxUntargetedMessage]
[DxAutoConstructor]
public readonly partial struct ButtonClicked
{
    public readonly string id;
}
