namespace Spindle.Abstractions.Exceptions;

public abstract class SpindleException : Exception
{
    protected SpindleException(string message)
        : base(message)
    {
    }

    protected SpindleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}