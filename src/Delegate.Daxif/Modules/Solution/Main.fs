module DG.Daxif.Modules.Solution.Main

open System
open Microsoft.Xrm.Sdk
open DG.Daxif
open DG.Daxif.Common
open DG.Daxif.Common.Utility

// Simplified version without Environment dependency for cross-platform compatibility
// This provides essential solution functions needed by Plugin and WebResource modules

let enablePluginSteps proxyGen solutionName (enable: bool option) (logLevel: LogLevel option) =
  let logLevel = logLevel ?| LogLevel.Verbose
  let enable = enable ?| true
  let log = ConsoleLogger logLevel
  
  log.Info @"PluginSteps solution: %s" solutionName
  log.Verbose @"Solution: %s" solutionName
  log.Verbose @"Enable: %b" enable
  
  // Plugin: stateCode = 1 and statusCode = 2 (inactive), 
  //         stateCode = 0 and statusCode = 1 (active) 
  // Remark: statusCode = -1, will default the statuscode for the given statecode
  let state, status = 
    if enable then 0, -1 else 1, -1
  
  let service = proxyGen()
  log.WriteLine(LogLevel.Verbose, @"Service instantiated")

  let solutionId = CrmDataInternal.Entities.retrieveSolutionId service solutionName
  CrmDataInternal.Entities.retrieveAllPluginProcessingSteps service solutionId
  |> Seq.toArray
  |> Array.Parallel.iter 
        (fun e -> 
        let en' = e.LogicalName
        let ei' = e.Id.ToString()
        try 
          CrmDataInternal.Entities.updateState service en' e.Id state status
          log.WriteLine(LogLevel.Verbose, sprintf "%s:%s state was updated" en' ei')
        with ex -> 
          log.WriteLine(LogLevel.Warning, sprintf "%s:%s %s" en' ei' ex.Message))
  
  let msg' = if enable then "enabled" else "disabled"
  log.Info @"The solution plugins were successfully %s" msg'
