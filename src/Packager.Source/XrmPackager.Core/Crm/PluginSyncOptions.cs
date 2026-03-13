namespace XrmPackager.Core.Crm;

public sealed class PluginSyncOptions
{
    public required string AssemblyPath { get; init; }
    public required string SolutionName { get; init; }
    public AssemblyIsolationMode IsolationMode { get; init; } = AssemblyIsolationMode.Sandbox;
    public bool DryRun { get; init; }
}
