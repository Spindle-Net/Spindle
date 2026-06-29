namespace Spindle.Abstractions.Policies;

public enum RetryBackoffKind
{
    /// <summary>
    /// Retry after a fixed delay
    /// </summary>
    Fixed,

    /// <summary>
    /// Retry after a linearly increasing delay
    /// </summary>
    Linear,

    /// <summary>
    /// Exponential backoff, retry after an exponentially increasing delay
    /// </summary>
    Exponential
}