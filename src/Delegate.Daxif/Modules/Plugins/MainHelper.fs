module internal DG.Daxif.Modules.Plugin.MainHelper

open System
open Microsoft.Xrm.Sdk
open Microsoft.Xrm.Sdk.Messages

open DG.Daxif
open DG.Daxif.Common
open DG.Daxif.Common.Utility

open Domain
open CrmUtility
open InternalUtility
open CrmDataHelper
open Retrieval

/// Transforms plugins from source to maps with names as keys
let localToMaps (plugins: Plugin seq) (customAPIs: CustomAPI seq) =
  let pluginTypeMap, stepMap, imageMap = 
    plugins
    |> Seq.fold (fun (typeMap, stepMap, imageMap) p ->
      let newTypeMap = if Map.containsKey p.TypeKey typeMap then typeMap else Map.add p.TypeKey p typeMap
      let newStepMap = Map.add p.StepKey p.step stepMap
      let newImageMap = p.ImagesWithKeys |> Seq.fold (fun acc (k,v) -> Map.add k v acc) imageMap

      newTypeMap, newStepMap, newImageMap
    ) (Map.empty, Map.empty, Map.empty)

  let customApiTypeMap, customApiMap, reqParamMap, respPropMap = 
    customAPIs
    |> Seq.fold (fun (typeMap, customApiMap, reqParamMap, respPropMap) c ->
      let newTypeMap = if Map.containsKey c.TypeKey typeMap then typeMap else Map.add c.TypeKey c typeMap
      let newcustomApiMap = Map.add c.Key c.message customApiMap
      let newReqParamMap = c.RequestParametersWithKeys |> Seq.fold (fun acc x -> Map.add x.name x acc) reqParamMap
      let newRespPropMap = c.ResponsePropertiesWithKeys |> Seq.fold (fun acc x -> Map.add x.name x acc) respPropMap

      newTypeMap, newcustomApiMap, newReqParamMap, newRespPropMap
    ) (Map.empty, Map.empty, Map.empty, Map.empty)
  
  // Convert CustomApi to Plugin to merge maps
  let apiTypeMapPlugins = 
    customApiTypeMap
    |> Map.map (fun (name) (api) -> 
    {
    step = {
        pluginTypeName = name
        executionStage = 10
        eventOperation = ""
        logicalName = ""
        deployment = 1
        executionMode = 1
        name = name
        executionOrder = 1
        filteredAttributes = ""
        userContext = Guid.Empty
        }
    images = Seq.empty 
    })
  
  // Merge pluginTypeMap and customApiTypeMap 
  let mergedTypeMap = Map.fold (fun acc key value -> Map.add key value acc) pluginTypeMap apiTypeMapPlugins

  mergedTypeMap, stepMap, imageMap, customApiTypeMap, customApiMap, reqParamMap, respPropMap

/// Check if major or minor version changed (requires delete-recreate in Power Platform)
let hasMajorMinorVersionChange (localVersion: Version) (registeredVersion: Version) =
  let (localMajor, localMinor, _, _) = localVersion
  let (regMajor, regMinor, _, _) = registeredVersion
  localMajor <> regMajor || localMinor <> regMinor

/// Determine which operation we want to perform on the assembly
let determineOperation (asmReg: AssemblyRegistration option) (asmLocal) : AssemblyOperation * Guid =
  match asmReg with
  | Some asm when Compare.registeredIsSameAsLocal asmLocal (Some asm) -> Unchanged, asm.id
  | Some asm when hasMajorMinorVersionChange asmLocal.version asm.version ->
      log.Warn "Assembly major/minor version changed from %s to %s - will delete and recreate assembly with all dependencies"
        (asm.version |> versionToString) (asmLocal.version |> versionToString)
      UpdateWithRecreate, asm.id
  | Some asm -> Update, asm.id
  | None     -> Create, Guid.Empty

/// Delete assembly and all its dependencies (types, steps, images, custom APIs, etc.)
let deleteAssemblyWithDependencies proxy assemblyId =
  log.Info "Deleting assembly and all dependencies (this may take a moment)..."
  
  // Retrieve all plugin types for this assembly
  let types = Query.pluginTypesByAssembly assemblyId |> CrmDataHelper.retrieveMultiple proxy |> Seq.toArray
  let typeIds = types |> Array.map (fun t -> t.Id) |> Set.ofArray
  
  // Retrieve all plugin steps for these types
  let steps = 
    types
    |> Array.map (fun t -> Query.pluginStepsByType t.Id)
    |> Array.collect (CrmDataHelper.retrieveMultiple proxy >> Seq.toArray)
  
  let stepIds = steps |> Array.map (fun s -> s.Id) |> Set.ofArray
  
  // Retrieve all images for these steps
  let images = 
    steps
    |> Array.map (fun s -> Query.pluginStepImagesByStep s.Id)
    |> Array.collect (CrmDataHelper.retrieveMultiple proxy >> Seq.toArray)
  
  // Retrieve all custom APIs for these types
  let customApis =
    types
    |> Array.map (fun t -> Query.CustomAPIsByType t.Id)
    |> Array.collect (CrmDataHelper.retrieveMultiple proxy >> Seq.toArray)
  
  let apiIds = customApis |> Array.map (fun a -> a.Id) |> Set.ofArray
  
  // Retrieve all request parameters and response properties
  let reqParams =
    customApis
    |> Array.map (fun a -> Query.customAPIReqParamsByCustomApiId a.Id)
    |> Array.collect (CrmDataHelper.retrieveMultiple proxy >> Seq.toArray)
  
  let respProps =
    customApis
    |> Array.map (fun a -> Query.customAPIRespPropsByCustomApiId a.Id)
    |> Array.collect (CrmDataHelper.retrieveMultiple proxy >> Seq.toArray)
  
  // Helper function to delete items in batches
  let deleteInBatches itemName (items: Entity[]) =
    if items.Length > 0 then
      log.Info "Deleting %d %s" items.Length itemName
      items
      |> Array.chunkBySize 200
      |> Array.iteri (fun idx batch ->
        if idx > 0 then
          System.Threading.Thread.Sleep(1000) // 1 second delay between batches
        if items.Length > 200 then
          log.Verbose "Deleting %s batch %d/%d (%d items)" itemName (idx + 1) ((items.Length + 199) / 200) batch.Length
        batch 
        |> Array.map (makeDeleteReq >> toOrgReq) 
        |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault ignore 
        |> ignore
      )
  
  // Delete in correct order: request params, response props, custom APIs, images, steps, types, assembly
  deleteInBatches "request parameters" reqParams
  deleteInBatches "response properties" respProps
  deleteInBatches "custom APIs" customApis
  deleteInBatches "images" images
  deleteInBatches "steps" steps
  deleteInBatches "types" types
  
  log.Info "Deleting assembly"
  CrmDataHelper.getResponse<DeleteResponse> proxy (DeleteRequest(Target = EntityReference("pluginassembly", assemblyId))) |> ignore
  
  log.Info "Assembly and all dependencies deleted successfully"

/// Update or create assembly
let ensureAssembly proxy solutionName asmLocal maybeAsm =
  match determineOperation maybeAsm asmLocal with
  | Unchanged, id ->
      log.Info "No changes to assembly %s detected" asmLocal.dllName
      id, false
  | Update, id ->
      let asmEntity = EntitySetup.createAssembly asmLocal.dllName asmLocal.dllPath asmLocal.assembly asmLocal.hash asmLocal.isolationMode
      asmEntity.Id <- id
      CrmDataHelper.getResponse<UpdateResponse> proxy (makeUpdateReq asmEntity) |> ignore
      log.Info "Updating %s: %s" asmEntity.LogicalName asmLocal.dllName
      id, false
  | UpdateWithRecreate, id ->
      // Delete existing assembly and all dependencies
      deleteAssemblyWithDependencies proxy id
      // Create new assembly
      let asmEntity = EntitySetup.createAssembly asmLocal.dllName asmLocal.dllPath asmLocal.assembly asmLocal.hash asmLocal.isolationMode
      log.Info "Creating %s: %s (after delete due to version change)" asmEntity.LogicalName asmLocal.dllName
      let newId = 
        CrmDataHelper.getResponseWithParams<CreateResponse> proxy (makeCreateReq asmEntity) [ "SolutionUniqueName", solutionName ]
        |> fun r -> r.id
      newId, true  // Return true to indicate assembly was recreated
  | Create, _ ->
      let asmEntity = EntitySetup.createAssembly asmLocal.dllName asmLocal.dllPath asmLocal.assembly asmLocal.hash asmLocal.isolationMode
      log.Info "Creating %s: %s" asmEntity.LogicalName asmLocal.dllName
      let newId = 
        CrmDataHelper.getResponseWithParams<CreateResponse> proxy (makeCreateReq asmEntity) [ "SolutionUniqueName", solutionName ]
        |> fun r -> r.id
      newId, false

// Deletes records in given map with batch logging for large sets
let performMapDelete proxy map =
  let items = Map.toArray map
  if items.Length > 0 then
    items |> Array.iter (fun (k, (v: Entity)) -> log.Info "Deleting %s: %s" v.LogicalName k)
    
    if items.Length > 200 then
      log.Verbose "Deleting %d items in batches of 200" items.Length
    
    items
    |> Array.map (snd >> makeDeleteReq >> toOrgReq)
    |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault ignore
    |> ignore

/// Deletes obsolete records in current plugin configuration
let performDelete proxy imgDiff stepDiff typeDiff apiDiff apiReqDiff apiRespDiff (sourceAPITypeMaps:Map<string,CustomAPI>) =
  // TODO Do this different correlates with note in CreateHelper.fs
  // Remove typeDiff.deletes which are to be used by Custom API
  let newTypeDeletes = 
    typeDiff.deletes
    |> Map.toArray
    |> Array.filter (fun (name, entity) -> not (sourceAPITypeMaps.ContainsKey name))
    |> Map
  
  performMapDelete proxy apiRespDiff.deletes
  performMapDelete proxy apiReqDiff.deletes
  performMapDelete proxy apiDiff.deletes
  performMapDelete proxy imgDiff.deletes 
  performMapDelete proxy stepDiff.deletes
  performMapDelete proxy newTypeDeletes


/// Updates with changes to the plugin configuration
let update proxy imgDiff stepDiff =
  let imgUpdates = 
    imgDiff.differences 
    |> Map.toArray
    |> Array.map (fun (name, (img, e: Entity)) -> name, EntitySetup.updateImage img e)

  let stepUpdates =
    stepDiff.differences
    |> Map.toArray
    |> Array.map (fun (name, (step, e: Entity)) -> name, EntitySetup.updateStep e.Id step)

  let updates = Array.concat [| imgUpdates; stepUpdates |]

  updates 
  |>> Array.iter (fun (name, record) -> log.Info "Updating %s: %s" record.LogicalName name)
  |> Array.map (snd >> makeUpdateReq >> toOrgReq)
  |> CrmDataHelper.performAsBulkResultHandling proxy raiseExceptionIfFault ignore
  |> ignore
  

/// Creates additions to the plugin configuration in the correct order and
/// passes guid maps to next step in process
let create proxy solutionName prefix sourceImgs imgDiff stepDiff apiDiff apiReqDiff apiRespDiff typeDiff asmId targetTypes targetSteps targetAPIs targetApiReqs targetApiResps =
  // Create plugin types
  let types = CreateHelper.createTypes proxy solutionName typeDiff asmId targetTypes
  
  // Create steps - this already handles batching internally via performAsBulk
  // which chunks into 200-item batches
  let allStepMap =
    types
    |> CreateHelper.createSteps proxy solutionName stepDiff targetSteps
  
  // Re-retrieve images from CRM to avoid duplicates
  // Query images for ALL steps that have images in source code to determine which already exist
  log.Verbose "Re-querying existing images to avoid creating duplicates"
  
  // Query images for all steps that should have images (from sourceImgs)
  // We need to check CRM to see which images already exist vs which need to be created
  let stepsToQueryForImages =
    sourceImgs
    |> Map.toSeq
    |> Seq.choose (fun (_, img) ->
      allStepMap 
      |> Map.tryFind img.stepName
      |> Option.map (fun (stepId, eventOp) -> img.stepName, stepId, eventOp)
    )
    |> Seq.distinctBy (fun (stepName, _, _) -> stepName)
    |> Array.ofSeq
  
  let allImagesFromCrm = 
    if stepsToQueryForImages.Length > 0 then
      stepsToQueryForImages
      |> Array.map (fun (_, stepId, _) -> Query.pluginStepImagesByStep stepId)
      |> CrmDataHelper.bulkRetrieveMultiple proxy
      |> Seq.choose (fun img -> 
        let stepIdRef = img.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid")
        if (stepIdRef = null) then None
        else
          let stepId = stepIdRef.Id
          let stepEntry = allStepMap |> Map.tryPick (fun k (id, _) -> if id = stepId then Some k else None)
          match stepEntry with
          | None -> None
          | Some stepName ->
            let imageName = getRecordName img
            (sprintf "%s, %s" stepName imageName, img) |> Some
      )
      |> Seq.toArray
    else
      Array.empty
  
  // Check for duplicate images and separate them: keep one, mark others for deletion
  let imageGroups = 
    allImagesFromCrm 
    |> Array.groupBy fst
    |> Array.map (fun (key, group) -> 
      let instances = group |> Array.map snd
      if instances.Length > 1 then
        log.Warn "Found %d copies of image '%s' in CRM (keeping 1, deleting %d)" instances.Length key (instances.Length - 1)
        // Keep the first one, mark the rest for deletion
        let toKeep = instances.[0]
        let toDelete = instances.[1..] |> Array.mapi (fun i img -> (sprintf "%s [duplicate %d]" key (i + 1), img))
        Some (key, toKeep, toDelete)
      else
        Some (key, instances.[0], Array.empty)
    )
    |> Array.choose id
  
  // Build the deduplicated map (one image per logical identity)
  let currentImagesInCrm = 
    imageGroups 
    |> Array.map (fun (key, img, _) -> key, img)
    |> Map.ofArray
  
  // Collect all duplicate images to delete
  let duplicatesToDelete = 
    imageGroups 
    |> Array.collect (fun (_, _, duplicates) -> duplicates)
    |> Map.ofArray
  
  if duplicatesToDelete.Count > 0 then
    log.Info "Deleting %d duplicate image(s)..." duplicatesToDelete.Count
    performMapDelete proxy duplicatesToDelete
  
  // Recalculate image diff using fresh data from CRM - compare ALL source images against what's in CRM
  let freshImgDiff = mapDiff sourceImgs currentImagesInCrm Compare.image
  
  log.Verbose "Image diff after re-query: %d to add, %d to update, %d to delete" 
    freshImgDiff.adds.Count freshImgDiff.differences.Count freshImgDiff.deletes.Count
  
  // Report if there are images to delete (more in CRM than in code)
  if freshImgDiff.deletes.Count > 0 then
    log.Warn "Found %d image(s) in CRM that don't exist in code and will be deleted:" freshImgDiff.deletes.Count
    freshImgDiff.deletes |> Map.iter (fun name _ -> 
      log.Warn "  - '%s'" name
    )
    // Delete the extra images
    performMapDelete proxy freshImgDiff.deletes
  
  // Create images - process in batches to handle large numbers
  // Group images by their parent step to ensure proper ordering
  let imagesByStep = 
    freshImgDiff.adds
    |> Map.toArray
    |> Array.filter (fun (imgName, img) -> 
      let stepExists = allStepMap.ContainsKey img.stepName
      if not stepExists then
        log.Warn "Skipping image '%s' because its step '%s' was not found in the step map" imgName img.stepName
      stepExists
    )
  
  if imagesByStep.Length > 0 then
    log.Verbose "Creating %d images in batches" imagesByStep.Length
    imagesByStep
    |> Array.chunkBySize 20
    |> Array.iteri (fun idx batch ->
      if idx > 0 then
        System.Threading.Thread.Sleep(1000) // 1 second delay between batches
      if imagesByStep.Length > 20 then
        log.Info "Creating image batch %d/%d (%d images)" (idx + 1) ((imagesByStep.Length + 19) / 20) batch.Length
      let batchImgDiff = { freshImgDiff with adds = Map.ofArray batch }
      CreateHelper.createImages proxy solutionName batchImgDiff allStepMap
    )
  else
    log.Verbose "No new images to create (all images already exist in CRM)"
  
  // Create custom APIs
  let apis = CreateHelper.createAPIs proxy solutionName prefix apiDiff targetAPIs asmId types
  
  // Create API request parameters and response properties
  apis
  |> CreateHelper.createAPIReqs proxy solutionName prefix apiReqDiff targetApiReqs

  apis 
  |> CreateHelper.createAPIResps proxy solutionName prefix apiRespDiff targetApiResps
  

/// Load a local assembly and validate its plugins
let loadAndValidateAssembly proxy dllPath isolationMode =
  log.Verbose "Loading local assembly and its plugins"
  let asmLocal = PluginDetection.getAssemblyContextFromDll dllPath isolationMode
  log.Verbose "Local assembly version %s loaded" (asmLocal.version |> versionToString)

  log.Verbose "Validating plugins to be registered"
  match Validation.validatePlugins proxy asmLocal.plugins with
  | Validation.Invalid err  -> failwith err
  | Validation.Valid _      -> ()
  log.Verbose "Validation completed"

  asmLocal


/// Analyzes local and remote registrations and returns the information about each of them
let analyze proxyGen dllPath solutionName isolationMode =
  let proxy = proxyGen()

  let asmLocal = loadAndValidateAssembly proxy dllPath isolationMode
  let solutionId = CrmDataInternal.Entities.retrieveSolutionId proxy solutionName
  let _id, prefix = CrmDataInternal.Entities.retrieveSolutionIdAndPrefix proxy solutionName
  let asmReg, pluginsReg = Retrieval.retrieveRegisteredByAssembly proxy solutionId asmLocal.dllName
  let pluginsLocal = localToMaps asmLocal.plugins asmLocal.customAPIs
    
  asmLocal, asmReg, pluginsLocal, pluginsReg, prefix


/// Performs a full synchronization of plugins
let performSync proxy solutionName prefix asmCtx asmReg (sourceTypes, sourceSteps, sourceImgs, sourceAPITypeMaps, sourceApis, sourceReqParams, sourceRespProps) (targetTypes, targetSteps, targetImgs, targetApis, targetReqParams, targetRespProps) =
  log.Info "Starting plugin synchronization"
 
  // Find differences
  let typeDiff = mapDiff sourceTypes targetTypes Compare.pluginType
  let stepDiff = mapDiff sourceSteps targetSteps Compare.step
  let imgDiff = mapDiff sourceImgs targetImgs Compare.image
  let apiDiff = mapDiff sourceApis targetApis Compare.api
  let apiReqDiff = mapDiff sourceReqParams targetReqParams Compare.apiReq
  let apiRespDiff = mapDiff sourceRespProps targetRespProps Compare.apiResp

  log.Info "Creating/updating assembly"
  let asmId, wasRecreated = ensureAssembly proxy solutionName asmCtx asmReg
  
  // If assembly was recreated (due to version change), all registrations were deleted
  // Reset target data to empty since everything is now gone from CRM
  let targetTypes, targetSteps, targetImgs, targetApis, targetReqParams, targetRespProps,
      typeDiff, stepDiff, imgDiff, apiDiff, apiReqDiff, apiRespDiff =
    if wasRecreated then
      log.Info "Assembly was recreated - resetting target data (all registrations were deleted)"
      let emptyTargets = Map.empty, Map.empty, Map.empty, Map.empty, Map.empty, Map.empty
      // Recalculate diffs with empty targets (everything becomes an 'add')
      let newTypeDiff = mapDiff sourceTypes Map.empty Compare.pluginType
      let newStepDiff = mapDiff sourceSteps Map.empty Compare.step
      let newImgDiff = mapDiff sourceImgs Map.empty Compare.image
      let newApiDiff = mapDiff sourceApis Map.empty Compare.api
      let newApiReqDiff = mapDiff sourceReqParams Map.empty Compare.apiReq
      let newApiRespDiff = mapDiff sourceRespProps Map.empty Compare.apiResp
      Map.empty, Map.empty, Map.empty, Map.empty, Map.empty, Map.empty,
      newTypeDiff, newStepDiff, newImgDiff, newApiDiff, newApiReqDiff, newApiRespDiff
    else
      targetTypes, targetSteps, targetImgs, targetApis, targetReqParams, targetRespProps,
      typeDiff, stepDiff, imgDiff, apiDiff, apiReqDiff, apiRespDiff

  // Perform sync operations (only if assembly wasn't recreated, since recreation deletes everything)
  if not wasRecreated then
    log.Info "Deleting removed registrations"
    performDelete proxy imgDiff stepDiff typeDiff apiDiff apiReqDiff apiRespDiff sourceAPITypeMaps
  
  log.Info "Updating existing registrations"
  update proxy imgDiff stepDiff

  log.Info "Creating new registrations"
  create proxy solutionName prefix sourceImgs imgDiff stepDiff apiDiff apiReqDiff apiRespDiff typeDiff asmId targetTypes targetSteps targetApis targetReqParams targetRespProps

  log.Info "Plugin synchronization was completed successfully"