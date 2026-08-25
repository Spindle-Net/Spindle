namespace Spindle.Persistence.EFCore;

internal static class SpindleDesignTimeSchema
{
    private const string MigrationScriptArgument = "--spindle-ef-script";

    public static string? Get(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!args.Any(argument => string.Equals(
                argument,
                MigrationScriptArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var schema = Environment.GetEnvironmentVariable("SPINDLE_EF_SCHEMA");
        return string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
    }
}
