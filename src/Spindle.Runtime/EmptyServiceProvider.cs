namespace Spindle;

internal sealed class EmptyServiceProvider : IServiceProvider
{
    public static EmptyServiceProvider Instance { get; } = new();

    private EmptyServiceProvider()
    {
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }
}
