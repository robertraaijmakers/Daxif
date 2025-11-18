namespace DG.Daxif

open DG.Daxif.Common.Utility
open DG.Daxif.Common.InternalUtility
open DG.Daxif.Modules.Plugin
open Microsoft.PowerPlatform.Dataverse.Client

type Plugin private () =
  /// <summary>Updates plugin registrations in CRM based on the plugins found in your local assembly.</summary>
  /// <param name="serviceClient">ServiceClient for connection to Dataverse.</param>
  /// <param name="assemblyPath">Path to the plugin assembly dll to be synced (usually under the project bin folder).</param>
  /// <param name="solutionName">The name of the solution to which to sync plugins</param>
  /// <param name="dryRun">Flag whether or not to simulate/test syncing plugins (running a 'dry run'). - defaults to: false</param>
  /// <param name="isolationMode">Assembly Isolation Mode ('Sandbox' or 'None'). All Online environments must use 'Sandbox' - defaults to: 'Sandbox'</param>
  /// <param name="logLevel">Log Level - Error, Warning, Info, Verbose or Debug - defaults to: 'Verbose'</param>
  static member Sync(serviceClient: ServiceClient, assemblyPath: string, solutionName: string, ?dryRun: bool, ?isolationMode: AssemblyIsolationMode, ?logLevel: LogLevel) =
    let proxyGen() = serviceClient :> Microsoft.Xrm.Sdk.IOrganizationService
    log.setLevelOption logLevel

    let dryRun = dryRun ?| false
    let isolationMode = isolationMode ?| AssemblyIsolationMode.Sandbox
    
    Main.syncSolution proxyGen assemblyPath solutionName isolationMode dryRun |> ignore

  /// <summary>Activates or deactivates all plugin steps of a solution</summary>
  /// <param name="serviceClient">ServiceClient for connection to Dataverse.</param>
  /// <param name="solutionName">The name of the solution in which to enable or disable all plugins</param>
  /// <param name="enable">Flag whether to enable or disable all solution plugins. - defaults to: true</param>
  /// <param name="logLevel">Log Level - Error, Warning, Info, Verbose or Debug - defaults to: 'Verbose'</param>
  static member EnableSolutionPluginSteps(serviceClient: ServiceClient, solutionName, ?enable, ?logLevel) =
    let proxyGen() = serviceClient :> Microsoft.Xrm.Sdk.IOrganizationService
    log.setLevelOption logLevel
    DG.Daxif.Modules.Solution.Main.enablePluginSteps proxyGen solutionName enable logLevel