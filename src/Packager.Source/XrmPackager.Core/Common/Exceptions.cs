namespace XrmPackager.Core;

/// <summary>
/// Custom exceptions for XrmPackager operations.
/// </summary>
public class XrmPackagerException : Exception
{
    public XrmPackagerException(string message)
        : base(message) { }

    public XrmPackagerException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when authentication fails.
/// </summary>
public class AuthenticationException : XrmPackagerException
{
    public AuthenticationException(string message)
        : base(message) { }

    public AuthenticationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when a required configuration is missing.
/// </summary>
public class ConfigurationException : XrmPackagerException
{
    public ConfigurationException(string message)
        : base(message) { }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when a CRM operation fails.
/// </summary>
public class CrmOperationException : XrmPackagerException
{
    public CrmOperationException(string message)
        : base(message) { }

    public CrmOperationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when an invalid argument is provided.
/// </summary>
public class InvalidArgumentException : XrmPackagerException
{
    public InvalidArgumentException(string message)
        : base(message) { }

    public InvalidArgumentException(string message, Exception innerException)
        : base(message, innerException) { }
}
