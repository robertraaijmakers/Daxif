namespace XrmPackager.Core;

/// <summary>
/// Log level for console output.
/// </summary>
public enum LogLevel
{
    Error = 0,
    Warning = 1,
    Info = 2,
    Verbose = 3,
    Debug = 4,
}

/// <summary>
/// Assembly isolation mode for plugins.
/// </summary>
public enum AssemblyIsolationMode
{
    /// <summary>Sandbox isolation mode (online only)</summary>
    Sandbox = 2,

    /// <summary>No isolation mode (on-premises only)</summary>
    None = 1,
}

/// <summary>
/// Web resource types matching Dataverse option set values.
/// </summary>
public enum WebResourceType
{
    HTML = 1,
    CSS = 2,
    JavaScript = 3,
    XML = 4,
    XAML = 4,
    XSD = 4,
    PNG = 5,
    JPG = 6,
    JPEG = 6,
    GIF = 7,
    XAP = 8,
    XSL = 9,
    XSLT = 9,
    ICO = 10,
    SVG = 11,
    RESX = 12,
}

/// <summary>
/// CRM release versions.
/// </summary>
public enum CrmReleases
{
    CRM2011 = 2011,
    CRM2013 = 2013,
    CRM2015 = 2015,
    CRM2016 = 2016,
    D365 = 2017,
}

/// <summary>
/// State of an asynchronous job.
/// </summary>
public enum AsyncJobState
{
    WaitingForResources = 0,
    Waiting = 10,
    InProgress = 20,
    Pausing = 21,
    Canceling = 22,
    Succeeded = 30,
    Failed = 31,
    Canceled = 32,
}

/// <summary>
/// Assembly operation type.
/// </summary>
public enum AssemblyOperation
{
    Unchanged,
    Create,
    Update,
    UpdateWithRecreate,
}

/// <summary>
/// Web resource action type.
/// </summary>
public enum WebResourceAction
{
    Create,
    Update,
    UpdateAndAddToPatchSolution,
    Delete,
}
