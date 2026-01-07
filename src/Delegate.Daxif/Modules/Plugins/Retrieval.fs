module DG.Daxif.Modules.Plugin.Retrieval

open System
open Microsoft.Xrm.Sdk
open Microsoft.Xrm.Sdk.Messages

open DG.Daxif
open DG.Daxif.Common
open DG.Daxif.Common.Utility

open Domain
open CrmUtility
open CrmDataHelper


/// Retrieve registered plugins from CRM related to a certain assembly and solution
/// Note: Retrieves steps/APIs from solution scope but filters to only those belonging to this assembly's types
let retrieveRegistered proxy solutionId assemblyId =
  // Get all plugin types for THIS specific assembly only
  let typeMap =
    Query.pluginTypesByAssembly assemblyId 
    |> CrmDataHelper.retrieveAndMakeMap proxy getRecordName

  let validTypeGuids = typeMap |> Seq.map (fun kv -> kv.Value.Id) |> Set.ofSeq

  // Get all steps from solution, but filter to only those belonging to types in THIS assembly
  // This ensures we don't accidentally delete steps from other assemblies in the same solution
  let steps =
    Query.pluginStepsBySolution solutionId 
    |> CrmDataHelper.retrieveMultiple proxy
    |> Seq.cache
    |> Seq.filter (fun e -> 
        let plugintypeid = e.GetAttributeValue<EntityReference>("plugintypeid")
        if plugintypeid = null then
            let name = if e.Contains("name") then e.GetAttributeValue<string>("name") else e.Id.ToString()
            ConsoleLogger.Global.Warn($"Plugin Step '{name}' (Id: {e.Id}) is missing the plugintypeid attribute and will be skipped.")
            false
        else
            // Only include steps that belong to types from THIS assembly
            validTypeGuids.Contains plugintypeid.Id
    )

  let stepMap =
    steps |> makeMap getRecordName
    
  let stepGuidMap =
    steps |> makeMap (fun step -> step.Id)

  // Images do not have a unique name. It will be combined with the name of the parent step.
  let imageMap = 
    steps
    |> Seq.map (fun s -> Query.pluginStepImagesByStep s.Id)
    |> CrmDataHelper.bulkRetrieveMultiple proxy
    |> Seq.choose (fun img -> 
      let stepIdRef = img.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid")
      if (stepIdRef = null) then
        let name = if img.Contains("name") then img.GetAttributeValue<string>("name") else img.Id.ToString()
        ConsoleLogger.Global.Warn($"Plugin Image '{name}' (Id: {img.Id}) is missing the sdkmessageprocessingstepid attribute and will be skipped.")
        None
      else
        let stepId = stepIdRef.Id
        match stepGuidMap.TryFind stepId with
        | None      -> None
        | Some step ->
          let stepName =  getRecordName step
          let imageName = getRecordName img
          (sprintf "%s, %s" stepName imageName, img) |> Some
    )
    |> Map.ofSeq

  // Get all custom APIs from solution, but filter to only those belonging to types in THIS assembly
  // This ensures we don't accidentally delete custom APIs from other assemblies in the same solution
  let customApis = 
    Query.customAPIsBySolution solutionId 
    |> CrmDataHelper.retrieveMultiple proxy
    |> Seq.cache
    |> Seq.filter (fun e -> 
        let plugintypeid = e.GetAttributeValue<EntityReference>("plugintypeid")
        if plugintypeid = null then
          let name = if e.Contains("name") then e.GetAttributeValue<string>("name") else e.Id.ToString()
          ConsoleLogger.Global.Warn($"Custom API '{name}' (Id: {e.Id}) is missing the plugintypeid attribute and will be skipped.")
          false
        else
          // Only include custom APIs that belong to types from THIS assembly
          validTypeGuids.Contains plugintypeid.Id
        )

  let customApiMap =
    customApis |> makeMap (fun (x:Entity) -> x.GetAttributeValue<string>("name"))
    
  let customApiGuidMap =
    customApis |> makeMap (fun step -> step.Id)
    
  // Add Request Parameters based on custom id
  // Make map where:
  // Iterate through all customapis
  // Request related request parameters 
  // Gather al in mapp where: 
  // Key = "ReqParam.Name"
  // Value = ReqParam

  let reqMap =
    customApis   
    |> Seq.toArray
    |> Array.map(fun (x) -> Query.customAPIReqParamsByCustomApiId x.Id)
    |> Array.map(fun (x) -> (
       CrmDataHelper.retrieveMultiple proxy x))
    |> Seq.fold Seq.append Seq.empty
    |> makeMap (fun req -> req.GetAttributeValue<string>("name"))


  let respMap =
      customApis   
      |> Seq.toArray
      |> Array.map(fun (x) -> Query.customAPIRespParamsByCustomApiId x.Id)
      |> Array.map(fun (x) -> (
         CrmDataHelper.retrieveMultiple proxy x))
      |> Seq.fold Seq.append Seq.empty
      |> makeMap (fun resp -> resp.GetAttributeValue<string>("name"))

  typeMap, stepMap, imageMap, customApiMap, reqMap, respMap


/// Retrieve registered plugins from CRM under a given assembly
let retrieveRegisteredByAssembly proxy solutionId assemblyName =
  let targetAssembly = 
    Query.pluginAssembliesBySolution solutionId
    |> CrmDataHelper.retrieveMultiple proxy
    |> Seq.tryFind (fun a -> getRecordName a = assemblyName)
    ?|> AssemblyRegistration.fromEntity

  match targetAssembly with
  | Some asm -> ConsoleLogger.Global.Verbose "Registered assembly version %s found for %s" (asm.version |> versionToString) assemblyName
  | None -> ConsoleLogger.Global.Verbose "No registered assembly found matching %s" assemblyName

  let maps = 
    match targetAssembly with
    | None         -> Map.empty, Map.empty, Map.empty, Map.empty, Map.empty, Map.empty
    | Some asmInfo -> retrieveRegistered proxy solutionId asmInfo.id
  
  targetAssembly, maps

/// Retrieves the necessary SdkMessage and SdkMessageFilter GUIDs for a collection of Steps
let getRelevantMessagesAndFilters proxy (steps: Step seq) : Map<(EventOperation * LogicalName), (Guid * Guid)> =
  // Messages
  let messageRequests = 
    steps
    |> Seq.distinctBy (fun step -> step.eventOperation)
    |> Seq.map (fun step -> 
      step.eventOperation, 
      Query.sdkMessage step.eventOperation |> makeRetrieveMultiple
    ) 
    |> Array.ofSeq

  let messageMap = 
    messageRequests
    |> Array.map (snd >> toOrgReq)
    |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault
      (fun resp -> 
        let result = (resp.Response :?> RetrieveMultipleResponse)
        let message = 
            match result.EntityCollection.Entities |> Seq.tryHead with
            | Some m -> m
            | None ->
                let messageName = fst messageRequests.[resp.RequestIndex]
                failwithf "SdkMessage with name '%s' not found." messageName
        let messageName = fst messageRequests.[resp.RequestIndex]
        messageName, message.Id
      )
    |> Map.ofArray

  // Build a lookup of what filters we need
  let neededFilters = 
    steps
    |> Seq.distinctBy (fun step -> step.eventOperation, step.logicalName)
    |> Seq.map (fun step -> 
      let messageId = messageMap.[step.eventOperation]
      (step.eventOperation, step.logicalName, messageId)
    )
    |> Array.ofSeq

  ConsoleLogger.Global.Verbose "Retrieving SDK message filters for %d unique message/entity combinations" neededFilters.Length

  // Retrieve ALL filters for these messages in one efficient query
  let messageIds = neededFilters |> Array.map (fun (_, _, msgId) -> msgId) |> Array.distinct
  let allFilters = 
    if messageIds.Length = 0 then
      Array.empty
    else
      Query.sdkMessageFiltersByMessageIds messageIds
      |> CrmDataHelper.retrieveMultiple proxy
      |> Seq.toArray

  ConsoleLogger.Global.Verbose "Retrieved %d SDK message filters from CRM" allFilters.Length

  // Build the map by matching filters to our needed combinations
  let finalMap =
    neededFilters
    |> Array.choose (fun (op, logicalName, messageId) ->
        // Find matching filter
        let matchingFilter = 
          allFilters 
          |> Array.tryFind (fun f ->
              let filterMessageId = f.GetAttributeValue<EntityReference>("sdkmessageid").Id
              let filterEntityCode = 
                if f.Contains("primaryobjecttypecode") 
                then f.GetAttributeValue<string>("primaryobjecttypecode")
                else ""
              
              // Match message ID first
              if filterMessageId <> messageId then false
              else
                // For entity-agnostic operations (Associate, Disassociate, etc.)
                // Match when both are empty OR when filter is "none" and we're looking for empty
                if String.IsNullOrEmpty(logicalName) then
                  String.IsNullOrEmpty(filterEntityCode) || 
                  String.Equals(filterEntityCode, "none", StringComparison.OrdinalIgnoreCase)
                // For entity-specific operations, match the entity code
                else
                  String.Equals(logicalName, filterEntityCode, StringComparison.OrdinalIgnoreCase)
          )
        
        match matchingFilter with
        | Some f -> 
            Some ((op, logicalName), (messageId, f.Id))
        | None ->
            // Log but don't fail - let the validation in createSteps handle it
            let entityMsg = if String.IsNullOrEmpty(logicalName) then "any entity" else sprintf "entity '%s'" logicalName
            ConsoleLogger.Global.Warn "SdkMessageFilter not found for operation '%s' on %s - this step cannot be registered" op entityMsg
            None
    )
    |> Map.ofArray
    
  finalMap
   

/// Retrieves the associated event operation for a given collection of step entities
/// and creates a map of it. This map is used for the images associated with the steps
let getStepMap proxy (steps: Map<string, Entity>) =
  let messageGuids =
    steps
    |> Map.toSeq
    |> Seq.choose (fun (_, s) -> 
      s.GetAttributeValue<EntityReference>("sdkmessageid") 
      |> objToMaybe
      ?|> fun e -> e.Id)
    |> Set.ofSeq
    |> Array.ofSeq

  let eventOpMap =
    messageGuids
    |> Array.map (fun guid -> makeRetrieve "sdkmessage" guid (RetrieveSelect.Fields ["name"]) |> toOrgReq)
    |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault
      (fun resp -> 
        let guid = messageGuids.[resp.RequestIndex]
        let entity = (resp.Response :?> RetrieveResponse).Entity
        let name = entity.GetAttributeValue<string>("name")
        guid, name
      )
    |> Map.ofArray

  steps
  |> Map.filter (fun name step -> 
      let sdkMessageId = step.GetAttributeValue<EntityReference>("sdkmessageid")
      if sdkMessageId <> null then true
      else 
        ConsoleLogger.Global.Warn($"Plugin Step '{name}' (Id: {step.Id}) is missing the sdkmessageid attribute and will be skipped when building step map.")
        false
      )
  |> Map.map (fun _ step -> 
    step.Id, eventOpMap.[step.GetAttributeValue<EntityReference>("sdkmessageid").Id])