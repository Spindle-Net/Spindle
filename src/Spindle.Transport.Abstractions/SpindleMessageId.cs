namespace Spindle.Transport;

public readonly record struct SpindleMessageId(string Value)
{
    public override string ToString() => Value;
}
