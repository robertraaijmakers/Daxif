module internal DG.Daxif.Modules.Plugin.CreateHelper

open System
open Microsoft.Xrm.Sdk
open Microsoft.Xrm.Sdk.Messages
open System.Collections.Generic
open DG.Daxif.Common
open DG.Daxif.Common.InternalUtility
open DG.Daxif.Common.Utility

open Domain
open CrmUtility
open CrmDataHelper
open Retrieval


/// Creates types and returns guid map for them.
let createTypes proxy solutionName typeDiff asmId targetTypes =
  let newTypes =
    typeDiff.adds 
    |> Map.toArray
    |> Array.map (fun (name, _) -> name, EntitySetup.createType asmId name)

  let orgTypeMap = targetTypes |> Map.map (fun _ (e: Entity) -> e.Id)
  
  // Create new types and add them to the map of already registered types
  newTypes 
  |>> Array.iter (fun (name, record) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (snd >> makeCreateReq >> attachToSolution solutionName >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault
    (fun e -> 
      let name = fst newTypes.[e.RequestIndex]
      let guid = (e.Response :?> CreateResponse).id
      name, guid)
  |> Array.fold (fun map (k, v) -> Map.add k v map) orgTypeMap


/// Creates steps and binds to matching plugin types. Returns a guid map for steps.
let createSteps proxy solutionName stepDiff orgSteps typeMap =
  let stepsArray =
    stepDiff.adds
    |> Map.toArray

  // Get the necessary SdkMessage and SdkMessageFilter guids for the new steps
  let messageFilterMap = 
    stepsArray
    |> Array.map snd
    |> getRelevantMessagesAndFilters proxy 
  
  // Validate that all required message filters exist
  let missingFilters = 
    stepsArray
    |> Array.filter (fun (name, step) -> 
      not (messageFilterMap.ContainsKey (step.eventOperation, step.logicalName))
    )
  
  if missingFilters.Length > 0 then
    log.Error "The following plugin steps cannot be registered because their message/entity combinations are not supported in your CRM environment:"
    missingFilters |> Array.iter (fun (name, step) ->
      let entityName = if System.String.IsNullOrEmpty(step.logicalName) then "any entity" else step.logicalName
      log.Error "  - Step '%s': Operation '%s' on entity '%s'" name step.eventOperation entityName
    )
    log.Error "Please verify that these entities support these operations in your CRM version, or remove these steps from your plugin code."
    
    let firstMissing = fst missingFilters.[0]
    let firstStep = snd missingFilters.[0]
    let entityMsg = if System.String.IsNullOrEmpty(firstStep.logicalName) then "any entity" else firstStep.logicalName
    failwithf "Unable to register %d plugin step(s). First failure: '%s' - Operation '%s' is not supported on entity '%s'. See error log above for complete list." 
      missingFilters.Length firstMissing firstStep.eventOperation entityMsg

  let newSteps =
    stepsArray
    |> Array.map (fun (name, step) -> 
      let typeId = 
        try Map.find step.pluginTypeName typeMap
        with :? KeyNotFoundException ->
          failwith $"Plugin type '{step.pluginTypeName}' for step '{name}' not found. It might not be registered in the target environment."
      let messageId, filterId =
        try messageFilterMap.[step.eventOperation, step.logicalName]
        with :? KeyNotFoundException ->
          failwith $"Unable to find sdkmessagefilter for event '{step.eventOperation}' on entity '{step.logicalName}' for step '{name}'. This is a configuration issue."
      let messageRecord = EntitySetup.createStep typeId messageId filterId name step

      name, (messageRecord, step.eventOperation)
    )

  let orgStepMap = getStepMap proxy orgSteps

  // Create new steps and add them to the map of already registered steps
  newSteps
  |>> Array.iter (fun (name, (record, _)) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (snd >> fst >> makeCreateReq >> attachToSolution solutionName >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault
    (fun e -> 
      let stepName, (_, eventOp) = newSteps.[e.RequestIndex]
      let stepId = (e.Response :?> CreateResponse).id
      stepName, (stepId, eventOp)
    )
  |> Array.fold (fun map (k, v) -> Map.add k v map) orgStepMap


/// Creates images and binds to matching steps
let createImages proxy solutionName imgDiff stepMap =
  imgDiff.adds 
  |> Map.toArray
  |> Array.map (fun (name, img) -> 
    let stepId, eventOp =
      try Map.find img.stepName stepMap
      with :? KeyNotFoundException ->
        failwith $"Could not find step with name '{img.stepName}' for image '{name}'. This step might not be registered in the target environment."
    
    name, (img, EntitySetup.createImage stepId eventOp img)
  )
  |>> Array.iter (fun (name, (_, record)) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (fun (name, (img, entity)) -> name, img, makeCreateReq entity |> attachToSolution solutionName |> toOrgReq)
  |> fun arr -> 
      let requests = arr |> Array.map (fun (_, _, req) -> req)
      let results = 
        CrmDataHelper.performAsBulkResultHandling proxy 
          (fun fault ->
              try
                // Find which image caused the error using RequestIndex
                let idx = 
                  try
                    if not (isNull fault.ErrorDetails) && fault.ErrorDetails.Contains("RequestIndex") then
                      match fault.ErrorDetails.["RequestIndex"] with
                      | :? int as i -> i
                      | _ -> -1
                    else 
                      -1
                  with
                  | _ -> -1
                
                if idx >= 0 && idx < arr.Length then
                  let imgName, img, _ = arr.[idx]
                  log.Error "Failed to create image '%s' for step '%s'" imgName img.stepName
                  log.Error "  Entity alias: %s" img.entityAlias
                  log.Error "  Image type: %d" img.imageType
                  log.Error "  Attributes: %s" img.attributes
                  log.Error "CRM error: %s" fault.Message
                  log.Error "This usually means one or more attribute names in the image don't exist on the entity"
                else
                  log.Error "Failed to create image at index %d (batch size: %d)" idx arr.Length
                  log.Error "CRM error: %s" fault.Message
              with ex ->
                log.Error "Error in fault handler: %s" ex.Message
                log.Error "Original fault: %s" fault.Message
              
              raiseExceptionIfFault fault
          )
          ignore
          requests
      results
  |> ignore


let createAPIReqs proxy solutionName prefix apiReqDiff targetReqAPIs apiMap =
  apiReqDiff.adds 
  |> Map.toArray
  |> Array.map (fun (name, req: RequestParameter) -> 
    let apiId = 
      try Map.find req.customApiName apiMap
      with :? KeyNotFoundException ->
        failwith $"Could not find custom api with name '{req.customApiName}' for request parameter '{name}'. This custom api might not be registered in the target environment."
    name, EntitySetup.createCustomAPIReq req (EntityReference("customapi", id = apiId)) prefix
  )
  |>> Array.iter (fun (name, record) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (snd >> makeCreateReq >> attachToSolution solutionName >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault ignore
  |> ignore

let createAPIResps proxy solutionName prefix apiRespDiff targetApiResps apiMap =
  apiRespDiff.adds
  |> Map.toArray
  |> Array.map (fun (name, resp: ResponseProperty) -> 
    let apiId = 
      try Map.find resp.customApiName apiMap
      with :? KeyNotFoundException ->
        failwith $"Could not find custom api with name '{resp.customApiName}' for response property '{name}'. This custom api might not be registered in the target environment."
    name, EntitySetup.createCustomAPIResp resp (EntityReference("customapi", id = apiId)) prefix
  )
  |>> Array.iter (fun (name, record) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (snd >> makeCreateReq >> attachToSolution solutionName >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault ignore
  |> ignore

/// Creates custom apis and return guid map. 
let createAPIs proxy solutionName prefix apiDiff targetAPIs asmId (types: Map<string,Guid>) =
  let apiArray = 
    apiDiff.adds 
    |> Map.toArray

  let orgApisMap = targetAPIs |> Map.map (fun _ (e: Entity) -> e.Id)

  // Create custom api's and store guid maps

  let newApis = 
    apiArray
    |> Array.map (fun (_, api: Message) -> 
       match types.TryGetValue api.pluginTypeName with
       | true, value -> (api.name, EntitySetup.createCustomAPI (api) (EntityReference("plugintype", id = value)) (prefix))
       | _           -> failwith $"Could not find plugin type with name '{api.pluginTypeName}' for custom api '{api.name}'. This plugin type might not be registered in the target environment."
       )

  newApis
  |>> Array.iter (fun (name, record) -> log.Info "Creating %s: %s" record.LogicalName name)
  |> Array.map (snd >> makeCreateReq >> attachToSolution solutionName >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault 
    (fun e -> 
      let name = fst newApis.[e.RequestIndex]
      let guid = (e.Response :?> CreateResponse).id
      name, guid)
  |> Array.fold (fun map (k, v) -> Map.add k v map) orgApisMap



