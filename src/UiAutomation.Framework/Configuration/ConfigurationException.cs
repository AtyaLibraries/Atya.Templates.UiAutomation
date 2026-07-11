namespace UiAutomation.Framework.Configuration;

/// <summary>
/// Raised when infrastructure configuration is invalid. Per Section 6.5, invalid
/// configuration must fail the run before any UI tests execute; this is an
/// infrastructure/framework-initialization failure, never a product-test failure.
/// </summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message)
        : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
