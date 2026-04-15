namespace XrmPackager.Core.Crm;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

public sealed class PluginOperations
{
    private readonly ILogger logger;

    public PluginOperations(ILogger logger)
    {
        this.logger = logger;
    }

    public void SyncPluginAssembly(ServiceClient client, PluginSyncOptions options)
    {
        if (!File.Exists(options.AssemblyPath))
        {
            throw new InvalidArgumentException(
                $"Plugin assembly not found: {options.AssemblyPath}"
            );
        }

        var local = LoadLocalAssembly(client, options.AssemblyPath, options.IsolationMode);
        var (solutionId, publisherPrefix) = SolutionMetadataProvider.GetSolutionInfo(
            client,
            options.SolutionName
        );
        var (registeredAssembly, targetMaps) = RetrieveRegistered(
            client,
            solutionId,
            local.DllName
        );
        var sourceMaps = BuildSourceMaps(local.Plugins, local.CustomApis);

        var typeDiff = Diff(sourceMaps.Types, targetMaps.Types, ComparePluginType);
        var stepDiff = Diff(sourceMaps.Steps, targetMaps.Steps, CompareStep);
        var imageDiff = Diff(sourceMaps.Images, targetMaps.Images, CompareImage);
        var apiDiff = Diff(sourceMaps.CustomApis, targetMaps.CustomApis, CompareApi);
        var reqDiff = Diff(sourceMaps.RequestParams, targetMaps.RequestParams, CompareReqParam);
        var respDiff = Diff(sourceMaps.ResponseProps, targetMaps.ResponseProps, CompareRespProp);

        if (options.DryRun)
        {
            var op = DetermineAssemblyOperation(registeredAssembly, local);
            logger.Info("***** Dry run *****");
            logger.Info(
                op switch
                {
                    AssemblyOperation.Unchanged => "No changes detected to assembly",
                    AssemblyOperation.Create => "Would create new assembly",
                    AssemblyOperation.Update => "Would update assembly",
                    AssemblyOperation.UpdateWithRecreate =>
                        "Would delete and recreate assembly due to major/minor version change",
                    _ => "Unknown operation",
                }
            );

            LogDryRunInventory(sourceMaps, targetMaps);
            LogDiff("Types", typeDiff);
            LogDiff("Steps", stepDiff);
            LogDiff("Images", imageDiff);
            LogDiff("Custom APIs", apiDiff);
            LogDiff("Custom API request parameters", reqDiff);
            LogDiff("Custom API response properties", respDiff);

            LogDryRunDetails("Types", typeDiff, DescribeTypeDifference);
            LogDryRunDetails("Steps", stepDiff, DescribeStepDifference);
            LogDryRunDetails("Images", imageDiff, DescribeImageDifference);
            LogDryRunDetails("Custom APIs", apiDiff, DescribeApiDifference);
            LogDryRunDetails(
                "Custom API request parameters",
                reqDiff,
                DescribeRequestParameterDifference
            );
            LogDryRunDetails(
                "Custom API response properties",
                respDiff,
                DescribeResponsePropertyDifference
            );
            return;
        }

        logger.Info("Creating/updating assembly");
        var (assemblyId, recreated) = EnsureAssembly(
            client,
            options.SolutionName,
            local,
            registeredAssembly
        );

        if (recreated)
        {
            targetMaps = RegisteredMaps.Empty;
            typeDiff = Diff(sourceMaps.Types, targetMaps.Types, ComparePluginType);
            stepDiff = Diff(sourceMaps.Steps, targetMaps.Steps, CompareStep);
            imageDiff = Diff(sourceMaps.Images, targetMaps.Images, CompareImage);
            apiDiff = Diff(sourceMaps.CustomApis, targetMaps.CustomApis, CompareApi);
            reqDiff = Diff(sourceMaps.RequestParams, targetMaps.RequestParams, CompareReqParam);
            respDiff = Diff(sourceMaps.ResponseProps, targetMaps.ResponseProps, CompareRespProp);
        }
        else
        {
            logger.Info("Deleting removed registrations");
            DeleteRemoved(
                client,
                sourceMaps.CustomApiTypeMap,
                imageDiff,
                stepDiff,
                typeDiff,
                apiDiff,
                reqDiff,
                respDiff
            );
        }

        logger.Info("Updating existing registrations");
        UpdateChanged(client, imageDiff, stepDiff);

        logger.Info("Creating new registrations");
        CreateAdded(
            client,
            options.SolutionName,
            publisherPrefix,
            assemblyId,
            sourceMaps,
            targetMaps,
            typeDiff,
            stepDiff,
            imageDiff,
            apiDiff,
            reqDiff,
            respDiff
        );

        logger.Info("Plugin synchronization was completed successfully");
    }

    private void CreateAdded(
        ServiceClient client,
        string solutionName,
        string publisherPrefix,
        Guid assemblyId,
        SourceMaps sourceMaps,
        RegisteredMaps targetMaps,
        DiffResult<PluginDefinition> typeDiff,
        DiffResult<StepDefinition> stepDiff,
        DiffResult<ImageDefinition> imageDiff,
        DiffResult<CustomApiDefinition> apiDiff,
        DiffResult<RequestParamDefinition> reqDiff,
        DiffResult<ResponsePropDefinition> respDiff
    )
    {
        var typeMap = CreateTypes(
            client,
            solutionName,
            assemblyId,
            typeDiff.Adds,
            targetMaps.Types
        );
        var stepMap = CreateSteps(client, solutionName, stepDiff.Adds, targetMaps.Steps, typeMap);

        var currentImages = RefreshAndDeduplicateImages(client, sourceMaps.Images, stepMap);
        var freshImageDiff = Diff(sourceMaps.Images, currentImages, CompareImage);

        if (freshImageDiff.Deletes.Count > 0)
        {
            logger.Warning(
                $"Found {freshImageDiff.Deletes.Count} image(s) in CRM that don't exist in code and will be deleted."
            );
            DeleteMap(client, "images", freshImageDiff.Deletes, 500, TimeSpan.FromSeconds(1));
        }

        var imagesToCreate = freshImageDiff.Adds.ToArray();
        var imageBatches = Chunk(imagesToCreate, 100).ToList();
        for (var batchIndex = 0; batchIndex < imageBatches.Count; batchIndex++)
        {
            if (batchIndex > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            if (imagesToCreate.Length > 100)
            {
                logger.Info(
                    $"Creating image batch {batchIndex + 1}/{imageBatches.Count} ({imageBatches[batchIndex].Count} images)"
                );
            }

            CreateImages(client, solutionName, imageBatches[batchIndex], stepMap);
        }

        var apiMap = CreateApis(
            client,
            solutionName,
            publisherPrefix,
            apiDiff.Adds,
            targetMaps.CustomApis,
            typeMap
        );
        CreateRequestParams(client, solutionName, reqDiff.Adds, apiMap);
        CreateResponseProps(client, solutionName, respDiff.Adds, apiMap);
    }

    private Dictionary<string, Guid> CreateTypes(
        ServiceClient client,
        string solutionName,
        Guid assemblyId,
        Dictionary<string, PluginDefinition> adds,
        Dictionary<string, Entity> existing
    )
    {
        var typeMap = existing.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal);
        if (adds.Count == 0)
        {
            return typeMap;
        }

        var payload = adds.Select(x =>
            {
                var entity = new Entity("plugintype")
                {
                    ["name"] = x.Key,
                    ["typename"] = x.Key,
                    ["friendlyname"] = Guid.NewGuid().ToString(),
                    ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
                    ["description"] = SyncDescription(),
                };
                return (Name: x.Key, Entity: entity);
            })
            .ToList();

        foreach (var item in payload)
        {
            logger.Info($"Creating {item.Entity.LogicalName}: {item.Name}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        var responses = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
        for (var i = 0; i < responses.Count; i++)
        {
            var id = ((CreateResponse)responses[i].Response).id;
            typeMap[payload[i].Name] = id;
        }

        return typeMap;
    }

    private Dictionary<string, (Guid StepId, string EventOp)> CreateSteps(
        ServiceClient client,
        string solutionName,
        Dictionary<string, StepDefinition> adds,
        Dictionary<string, Entity> existing,
        Dictionary<string, Guid> typeMap
    )
    {
        var result = BuildStepMap(client, existing);
        if (adds.Count == 0)
        {
            return result;
        }

        var requiredFilters = ResolveMessageFilters(client, adds.Values.ToList());
        var missing = adds.Where(x =>
                !requiredFilters.ContainsKey((x.Value.EventOperation, x.Value.LogicalName))
            )
            .ToList();

        if (missing.Count > 0)
        {
            foreach (var item in missing)
            {
                var entityName = string.IsNullOrEmpty(item.Value.LogicalName)
                    ? "any entity"
                    : item.Value.LogicalName;
                logger.Error(
                    $"Step '{item.Key}' cannot be registered: operation '{item.Value.EventOperation}' on entity '{entityName}' is unsupported."
                );
            }
            throw new InvalidOperationException(
                "Unable to register one or more plugin steps. See errors above."
            );
        }

        var payload = adds.Select(x =>
            {
                if (!typeMap.TryGetValue(x.Value.PluginTypeName, out var pluginTypeId))
                {
                    throw new InvalidOperationException(
                        $"Plugin type '{x.Value.PluginTypeName}' for step '{x.Key}' not found."
                    );
                }

                var pair = requiredFilters[(x.Value.EventOperation, x.Value.LogicalName)];
                var entity = CreateStepEntity(
                    pluginTypeId,
                    pair.MessageId,
                    pair.FilterId,
                    x.Key,
                    x.Value
                );
                return (Name: x.Key, EventOperation: x.Value.EventOperation, Entity: entity);
            })
            .ToList();

        foreach (var item in payload)
        {
            logger.Info($"Creating {item.Entity.LogicalName}: {item.Name}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        var responses = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
        for (var i = 0; i < responses.Count; i++)
        {
            var id = ((CreateResponse)responses[i].Response).id;
            result[payload[i].Name] = (id, payload[i].EventOperation);
        }

        // Re-query all steps for all current types (mirrors legacy behavior and avoids request-index assumptions)
        var reloaded = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var typeId in typeMap.Values.Distinct())
        {
            var query = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(true),
            };
            query.Criteria.AddCondition("plugintypeid", ConditionOperator.Equal, typeId);
            foreach (var step in client.RetrieveMultiple(query).Entities)
            {
                var stepName = step.GetAttributeValue<string>("name");
                if (!string.IsNullOrWhiteSpace(stepName))
                {
                    reloaded[stepName] = step;
                }
            }
        }

        result = BuildStepMap(client, reloaded);
        logger.Verbose($"Complete step map has {result.Count} steps");
        return result;
    }

    private void CreateImages(
        ServiceClient client,
        string solutionName,
        IReadOnlyList<KeyValuePair<string, ImageDefinition>> adds,
        Dictionary<string, (Guid StepId, string EventOp)> stepMap
    )
    {
        var payload = new List<(string Name, ImageDefinition Source, Entity Entity)>();
        foreach (var add in adds)
        {
            if (!stepMap.TryGetValue(add.Value.StepName, out var step))
            {
                logger.Warning(
                    $"Skipping image '{add.Key}' because step '{add.Value.StepName}' was not found."
                );
                continue;
            }

            var entity = CreateImageEntity(step.StepId, step.EventOp, add.Value);
            payload.Add((add.Key, add.Value, entity));
            logger.Info($"Creating {entity.LogicalName}: {add.Key}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        _ = ExecuteBulk(
            client,
            requests,
            20,
            TimeSpan.Zero,
            true,
            (fault, index) =>
            {
                if (index < 0 || index >= payload.Count)
                {
                    return;
                }

                var failed = payload[index];
                logger.Error(
                    $"Failed to create image '{failed.Name}' for step '{failed.Source.StepName}'. CRM error: {fault.Message}"
                );
            }
        );
    }

    private Dictionary<string, Guid> CreateApis(
        ServiceClient client,
        string solutionName,
        string publisherPrefix,
        Dictionary<string, CustomApiDefinition> adds,
        Dictionary<string, Entity> existing,
        Dictionary<string, Guid> typeMap
    )
    {
        var apiMap = existing.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal);
        if (adds.Count == 0)
        {
            return apiMap;
        }

        var payload = adds.Select(x =>
            {
                var message = x.Value.Message;
                if (!typeMap.TryGetValue(message.PluginTypeName, out var pluginTypeId))
                {
                    throw new InvalidOperationException(
                        $"Could not find plugin type '{message.PluginTypeName}' for custom API '{message.Name}'."
                    );
                }

                var entity = CreateApiEntity(message, publisherPrefix, pluginTypeId);
                return (Name: message.Name, Entity: entity);
            })
            .ToList();

        foreach (var item in payload)
        {
            logger.Info($"Creating {item.Entity.LogicalName}: {item.Name}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        var responses = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
        for (var i = 0; i < responses.Count; i++)
        {
            var id = ((CreateResponse)responses[i].Response).id;
            apiMap[payload[i].Name] = id;
        }

        return apiMap;
    }

    private void CreateRequestParams(
        ServiceClient client,
        string solutionName,
        Dictionary<string, RequestParamDefinition> adds,
        Dictionary<string, Guid> apiMap
    )
    {
        if (adds.Count == 0)
        {
            return;
        }

        var payload = adds.Select(x =>
            {
                var source = x.Value;
                if (!apiMap.TryGetValue(source.CustomApiName, out var apiId))
                {
                    throw new InvalidOperationException(
                        $"Could not find custom API '{source.CustomApiName}' for request parameter '{source.Name}'."
                    );
                }

                var entity = new Entity("customapirequestparameter")
                {
                    ["description"] = SyncDescription(),
                    ["displayname"] = source.DisplayName,
                    ["logicalentityname"] = source.LogicalEntityName,
                    ["isoptional"] = source.IsOptional,
                    ["name"] = source.Name,
                    ["type"] = new OptionSetValue(source.Type),
                    ["uniquename"] = source.UniqueName,
                    ["iscustomizable"] = new BooleanManagedProperty(source.IsCustomizable),
                    ["customapiid"] = new EntityReference("customapi", apiId),
                };

                return (Name: x.Key, Entity: entity);
            })
            .ToList();

        foreach (var item in payload)
        {
            logger.Info($"Creating {item.Entity.LogicalName}: {item.Name}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        _ = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
    }

    private void CreateResponseProps(
        ServiceClient client,
        string solutionName,
        Dictionary<string, ResponsePropDefinition> adds,
        Dictionary<string, Guid> apiMap
    )
    {
        if (adds.Count == 0)
        {
            return;
        }

        var payload = adds.Select(x =>
            {
                var source = x.Value;
                if (!apiMap.TryGetValue(source.CustomApiName, out var apiId))
                {
                    throw new InvalidOperationException(
                        $"Could not find custom API '{source.CustomApiName}' for response property '{source.Name}'."
                    );
                }

                var entity = new Entity("customapiresponseproperty")
                {
                    ["description"] = SyncDescription(),
                    ["displayname"] = source.DisplayName,
                    ["logicalentityname"] = source.LogicalEntityName,
                    ["name"] = source.Name,
                    ["type"] = new OptionSetValue(source.Type),
                    ["uniquename"] = source.UniqueName,
                    ["iscustomizable"] = new BooleanManagedProperty(source.IsCustomizable),
                    ["customapiid"] = new EntityReference("customapi", apiId),
                };

                return (Name: x.Key, Entity: entity);
            })
            .ToList();

        foreach (var item in payload)
        {
            logger.Info($"Creating {item.Entity.LogicalName}: {item.Name}");
        }

        var requests = payload
            .Select(x => (OrganizationRequest)CreateRequestWithSolution(x.Entity, solutionName))
            .ToList();

        _ = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
    }

    private Dictionary<string, Entity> RefreshAndDeduplicateImages(
        ServiceClient client,
        Dictionary<string, ImageDefinition> sourceImages,
        Dictionary<string, (Guid StepId, string EventOp)> stepMap
    )
    {
        var stepEntries = sourceImages
            .Values.Select(x => x.StepName)
            .Distinct(StringComparer.Ordinal)
            .Select(stepName =>
            {
                if (!stepMap.TryGetValue(stepName, out var step))
                {
                    return (StepName: stepName, HasValue: false, StepId: Guid.Empty);
                }
                return (StepName: stepName, HasValue: true, StepId: step.StepId);
            })
            .Where(x => x.HasValue)
            .ToList();

        var allImages = new List<(string Key, Entity Entity)>();
        foreach (var step in stepEntries)
        {
            var query = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet(true),
            };
            query.Criteria.AddCondition(
                "sdkmessageprocessingstepid",
                ConditionOperator.Equal,
                step.StepId
            );

            foreach (var image in client.RetrieveMultiple(query).Entities)
            {
                var imageName = image.GetAttributeValue<string>("name");
                if (string.IsNullOrWhiteSpace(imageName))
                {
                    continue;
                }

                allImages.Add(($"{step.StepName}, {imageName}", image));
            }
        }

        var deduped = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var group in allImages.GroupBy(x => x.Key, StringComparer.Ordinal))
        {
            var groupList = group.Select(x => x.Entity).ToList();
            deduped[group.Key] = groupList[0];

            if (groupList.Count > 1)
            {
                logger.Warning(
                    $"Found {groupList.Count} copies of image '{group.Key}' in CRM (keeping 1, deleting {groupList.Count - 1})"
                );
                for (var i = 1; i < groupList.Count; i++)
                {
                    duplicates[$"{group.Key} [duplicate {i}]"] = groupList[i];
                }
            }
        }

        if (duplicates.Count > 0)
        {
            logger.Info($"Deleting {duplicates.Count} duplicate image(s)...");
            DeleteMap(client, "duplicate images", duplicates, 500, TimeSpan.FromSeconds(1));
        }

        return deduped;
    }

    private void UpdateChanged(
        ServiceClient client,
        DiffResult<ImageDefinition> imageDiff,
        DiffResult<StepDefinition> stepDiff
    )
    {
        var updates = new List<(string Name, Entity Entity)>();

        updates.AddRange(
            imageDiff.Differences.Select(pair =>
            {
                var source = pair.Value.Source;
                var target = pair.Value.Target;
                var entity = new Entity("sdkmessageprocessingstepimage", target.Id)
                {
                    ["name"] = source.Name,
                    ["entityalias"] = source.EntityAlias,
                    ["imagetype"] = new OptionSetValue(source.ImageType),
                    ["attributes"] = source.Attributes,
                    ["sdkmessageprocessingstepid"] = target.GetAttributeValue<EntityReference>(
                        "sdkmessageprocessingstepid"
                    ),
                };
                return (pair.Key, entity);
            })
        );

        updates.AddRange(
            stepDiff.Differences.Select(pair =>
            {
                var source = pair.Value.Source;
                var target = pair.Value.Target;
                var entity = new Entity("sdkmessageprocessingstep", target.Id)
                {
                    ["stage"] = new OptionSetValue(source.ExecutionStage),
                    ["filteringattributes"] = source.FilteredAttributes,
                    ["supporteddeployment"] = new OptionSetValue(source.Deployment),
                    ["mode"] = new OptionSetValue(source.ExecutionMode),
                    ["rank"] = source.ExecutionOrder,
                    ["description"] = StepDescription(source),
                };
                entity["impersonatinguserid"] =
                    source.UserContext == Guid.Empty
                        ? null
                        : new EntityReference("systemuser", source.UserContext);
                return (pair.Key, entity);
            })
        );

        foreach (var update in updates)
        {
            logger.Info($"Updating {update.Entity.LogicalName}: {update.Name}");
        }

        var requests = updates
            .Select(x => (OrganizationRequest)new UpdateRequest { Target = x.Entity })
            .ToList();

        _ = ExecuteBulk(client, requests, 20, TimeSpan.Zero, true);
    }

    private void DeleteRemoved(
        ServiceClient client,
        Dictionary<string, CustomApiDefinition> sourceApiTypeMap,
        DiffResult<ImageDefinition> imageDiff,
        DiffResult<StepDefinition> stepDiff,
        DiffResult<PluginDefinition> typeDiff,
        DiffResult<CustomApiDefinition> apiDiff,
        DiffResult<RequestParamDefinition> reqDiff,
        DiffResult<ResponsePropDefinition> respDiff
    )
    {
        var filteredTypeDeletes = new Dictionary<string, Entity>(
            typeDiff.Deletes,
            StringComparer.Ordinal
        );
        foreach (var key in sourceApiTypeMap.Keys)
        {
            filteredTypeDeletes.Remove(key);
        }

        DeleteMap(
            client,
            "custom API response properties",
            respDiff.Deletes,
            500,
            TimeSpan.FromSeconds(1)
        );
        DeleteMap(
            client,
            "custom API request parameters",
            reqDiff.Deletes,
            500,
            TimeSpan.FromSeconds(1)
        );
        DeleteMap(client, "custom APIs", apiDiff.Deletes, 500, TimeSpan.FromSeconds(1));
        DeleteMap(client, "images", imageDiff.Deletes, 500, TimeSpan.FromSeconds(1));
        DeleteMap(client, "steps", stepDiff.Deletes, 500, TimeSpan.FromSeconds(1));
        DeleteMap(client, "types", filteredTypeDeletes, 500, TimeSpan.FromSeconds(1));
    }

    private void DeleteMap(
        ServiceClient client,
        string label,
        IReadOnlyDictionary<string, Entity> map,
        int batchSize,
        TimeSpan delayBetweenBatches
    )
    {
        if (map.Count == 0)
        {
            return;
        }

        foreach (var item in map)
        {
            logger.Info($"Deleting {item.Value.LogicalName}: {item.Key}");
        }

        var entities = map.Values.ToArray();
        var batches = Chunk(entities, batchSize).ToList();

        logger.Info($"Deleting {entities.Length} {label}");
        for (var i = 0; i < batches.Count; i++)
        {
            if (i > 0)
            {
                Thread.Sleep(delayBetweenBatches);
            }

            if (entities.Length > batchSize)
            {
                logger.Verbose(
                    $"Deleting {label} batch {i + 1}/{batches.Count} ({batches[i].Count} items)"
                );
            }

            var requests = batches[i]
                .Select(x =>
                    (OrganizationRequest)new DeleteRequest { Target = x.ToEntityReference() }
                )
                .ToList();

            _ = ExecuteBulk(
                client,
                requests,
                batchSize,
                TimeSpan.Zero,
                false,
                (fault, index) =>
                {
                    if (IsMissingDeleteFault(fault))
                    {
                        if (index >= 0 && index < batches[i].Count)
                        {
                            var missing = batches[i][index];
                            logger.Verbose(
                                $"Skipping delete for {missing.LogicalName} '{missing.Id}' because it no longer exists."
                            );
                        }

                        return;
                    }

                    throw new InvalidOperationException(fault.Message);
                }
            );
        }
    }

    private (Guid AssemblyId, bool Recreated) EnsureAssembly(
        ServiceClient client,
        string solutionName,
        LocalAssembly local,
        AssemblyRegistration? registered
    )
    {
        var operation = DetermineAssemblyOperation(registered, local);
        switch (operation)
        {
            case AssemblyOperation.Unchanged:
                logger.Info($"No changes to assembly {local.DllName} detected");
                return (registered!.Id, false);

            case AssemblyOperation.Create:
            {
                var id = CreateWithSolution(client, CreateAssemblyEntity(local), solutionName);
                logger.Info($"Creating plugin assembly: {local.DllName}");
                return (id, false);
            }

            case AssemblyOperation.Update:
            {
                var update = CreateAssemblyEntity(local);
                update.Id = registered!.Id;
                client.Update(update);
                logger.Info($"Updating plugin assembly: {local.DllName}");
                return (registered.Id, false);
            }

            case AssemblyOperation.UpdateWithRecreate:
            {
                DeleteAssemblyWithDependencies(client, registered!.Id);
                var id = CreateWithSolution(client, CreateAssemblyEntity(local), solutionName);
                logger.Info($"Creating plugin assembly after delete/recreate: {local.DllName}");
                return (id, true);
            }

            default:
                throw new InvalidOperationException("Unknown assembly operation");
        }
    }

    private void DeleteAssemblyWithDependencies(ServiceClient client, Guid assemblyId)
    {
        logger.Info("Deleting assembly and all dependencies...");

        var typeQuery = new QueryExpression("plugintype") { ColumnSet = new ColumnSet(true) };
        typeQuery.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
        var types = client.RetrieveMultiple(typeQuery).Entities.ToArray();

        var steps = new List<Entity>();
        foreach (var type in types)
        {
            var stepQuery = new QueryExpression("sdkmessageprocessingstep")
            {
                ColumnSet = new ColumnSet(true),
            };
            stepQuery.Criteria.AddCondition("plugintypeid", ConditionOperator.Equal, type.Id);
            steps.AddRange(client.RetrieveMultiple(stepQuery).Entities);
        }

        var images = new List<Entity>();
        foreach (var step in steps)
        {
            var imageQuery = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet(true),
            };
            imageQuery.Criteria.AddCondition(
                "sdkmessageprocessingstepid",
                ConditionOperator.Equal,
                step.Id
            );
            images.AddRange(client.RetrieveMultiple(imageQuery).Entities);
        }

        var apis = new List<Entity>();
        foreach (var type in types)
        {
            var apiQuery = new QueryExpression("customapi") { ColumnSet = new ColumnSet(true) };
            apiQuery.Criteria.AddCondition("plugintypeid", ConditionOperator.Equal, type.Id);
            apis.AddRange(client.RetrieveMultiple(apiQuery).Entities);
        }

        var reqs = new List<Entity>();
        var resps = new List<Entity>();
        foreach (var api in apis)
        {
            var reqQuery = new QueryExpression("customapirequestparameter")
            {
                ColumnSet = new ColumnSet(true),
            };
            reqQuery.Criteria.AddCondition("customapiid", ConditionOperator.Equal, api.Id);
            reqs.AddRange(client.RetrieveMultiple(reqQuery).Entities);

            var respQuery = new QueryExpression("customapiresponseproperty")
            {
                ColumnSet = new ColumnSet(true),
            };
            respQuery.Criteria.AddCondition("customapiid", ConditionOperator.Equal, api.Id);
            resps.AddRange(client.RetrieveMultiple(respQuery).Entities);
        }

        DeleteEntities(client, "request parameters", reqs, 500);
        DeleteEntities(client, "response properties", resps, 500);
        DeleteEntities(client, "custom APIs", apis, 500);
        DeleteEntities(client, "images", images, 500);
        DeleteEntities(client, "steps", steps, 500);
        DeleteEntities(client, "types", types.ToList(), 500);

        client.Delete("pluginassembly", assemblyId);
        logger.Info("Assembly and all dependencies deleted successfully");
    }

    private void DeleteEntities(
        ServiceClient client,
        string label,
        IReadOnlyList<Entity> entities,
        int batchSize
    )
    {
        if (entities.Count == 0)
        {
            return;
        }

        logger.Info($"Deleting {entities.Count} {label}");
        var batches = Chunk(entities, batchSize).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            if (i > 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            var requests = batches[i]
                .Select(x =>
                    (OrganizationRequest)new DeleteRequest { Target = x.ToEntityReference() }
                )
                .ToList();

            _ = ExecuteBulk(
                client,
                requests,
                batchSize,
                TimeSpan.Zero,
                false,
                (fault, index) =>
                {
                    if (IsMissingDeleteFault(fault))
                    {
                        if (index >= 0 && index < batches[i].Count)
                        {
                            var missing = batches[i][index];
                            logger.Verbose(
                                $"Skipping delete for {missing.LogicalName} '{missing.Id}' because it no longer exists."
                            );
                        }

                        return;
                    }

                    throw new InvalidOperationException(fault.Message);
                }
            );
        }
    }

    private AssemblyOperation DetermineAssemblyOperation(
        AssemblyRegistration? registered,
        LocalAssembly local
    )
    {
        if (registered == null)
        {
            return AssemblyOperation.Create;
        }

        var unchanged =
            registered.Version >= local.Version
            && string.Equals(
                registered.SourceHash,
                local.SourceHash,
                StringComparison.OrdinalIgnoreCase
            );
        if (unchanged)
        {
            return AssemblyOperation.Unchanged;
        }

        var majorMinorChanged =
            registered.Version.Major != local.Version.Major
            || registered.Version.Minor != local.Version.Minor;

        if (majorMinorChanged)
        {
            logger.Warning(
                $"Assembly major/minor version changed from {registered.Version} to {local.Version} - will delete and recreate assembly with dependencies"
            );
            return AssemblyOperation.UpdateWithRecreate;
        }

        return AssemblyOperation.Update;
    }

    private LocalAssembly LoadLocalAssembly(
        ServiceClient client,
        string assemblyPath,
        AssemblyIsolationMode isolationMode
    )
    {
        var local = GetAssemblyContextFromDll(assemblyPath, isolationMode);
        ValidatePlugins(local.Plugins, client);
        return local;
    }

    private LocalAssembly GetAssemblyContextFromDll(
        string assemblyPath,
        AssemblyIsolationMode isolationMode
    )
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");

        File.Copy(fullPath, tempPath, true);
        var bytes = File.ReadAllBytes(fullPath);
        var asm = Assembly.LoadFile(tempPath);

        return new LocalAssembly(
            DllName: Path.GetFileNameWithoutExtension(fullPath),
            DllPath: fullPath,
            SourceHash: ComputeSha1(bytes),
            Version: asm.GetName().Version ?? new Version(1, 0, 0, 0),
            IsolationMode: isolationMode,
            Plugins: ExtractPlugins(asm),
            CustomApis: ExtractCustomApis(asm)
        );
    }

    private List<PluginDefinition> ExtractPlugins(Assembly assembly)
    {
        try
        {
            var types = GetLoadableTypes(assembly);
            var pluginBaseType = types.FirstOrDefault(t => t.Name == "Plugin");
            if (pluginBaseType == null)
            {
                return [];
            }

            var validTypes = types
                .Where(t => t.IsSubclassOf(pluginBaseType))
                .Where(t =>
                {
                    var valid = !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null;
                    if (!valid)
                    {
                        if (t.IsAbstract)
                        {
                            logger.Warning(
                                $"The plugin '{t.Name}' is abstract and will not be synchronized"
                            );
                        }

                        if (t.GetConstructor(Type.EmptyTypes) == null)
                        {
                            logger.Warning(
                                $"The plugin '{t.Name}' has no empty constructor and will not be synchronized"
                            );
                        }
                    }
                    return valid;
                })
                .ToArray();

            var plugins = new List<PluginDefinition>();
            foreach (var type in validTypes)
            {
                var instance =
                    Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException(
                        $"Could not instantiate plugin type '{type.FullName}'"
                    );

                var method =
                    type.GetMethod("PluginProcessingStepConfigs")
                    ?? throw new InvalidOperationException(
                        $"Plugin type '{type.FullName}' is missing PluginProcessingStepConfigs"
                    );

                var values = method.Invoke(instance, []) as IEnumerable;
                if (values == null)
                {
                    continue;
                }

                foreach (var value in values)
                {
                    plugins.Add(ToPluginDefinition(value!));
                }
            }

            return plugins;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to fetch plugin configuration from plugin assembly. This can happen if an old Plugin.cs base class is used.",
                ex
            );
        }
    }

    private List<CustomApiDefinition> ExtractCustomApis(Assembly assembly)
    {
        try
        {
            var types = GetLoadableTypes(assembly);
            var customApiBaseType = types.FirstOrDefault(t => t.Name == "CustomAPI");
            if (customApiBaseType == null)
            {
                return [];
            }

            var validTypes = types
                .Where(t => t.IsSubclassOf(customApiBaseType))
                .Where(t =>
                {
                    var valid = !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null;
                    if (!valid)
                    {
                        if (t.IsAbstract)
                        {
                            logger.Warning(
                                $"The custom api '{t.Name}' is abstract and will not be synchronized"
                            );
                        }

                        if (t.GetConstructor(Type.EmptyTypes) == null)
                        {
                            logger.Warning(
                                $"The custom api '{t.Name}' has no empty constructor and will not be synchronized"
                            );
                        }
                    }
                    return valid;
                })
                .ToArray();

            var apis = new List<CustomApiDefinition>();
            foreach (var type in validTypes)
            {
                var instance =
                    Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException(
                        $"Could not instantiate custom api type '{type.FullName}'"
                    );

                var method =
                    type.GetMethod("GetCustomAPIConfig")
                    ?? throw new InvalidOperationException(
                        $"Custom api type '{type.FullName}' is missing GetCustomAPIConfig"
                    );

                var value = method.Invoke(instance, []);
                if (value != null)
                {
                    apis.Add(ToCustomApiDefinition(value));
                }
            }

            return apis;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to fetch custom API configuration from assembly. This can happen if an old CustomAPI.cs base class is used.",
                ex
            );
        }
    }

    private void ValidatePlugins(IReadOnlyList<PluginDefinition> plugins, ServiceClient client)
    {
        var invalid = plugins.FirstOrDefault(p =>
            p.Step.ExecutionMode == 1 && p.Step.ExecutionStage != 40
        );
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Post execution stage is required for asynchronous execution mode"
            );
        }

        invalid = plugins.FirstOrDefault(p =>
            (p.Step.ExecutionStage == 10 || p.Step.ExecutionStage == 20)
            && p.Images.Any(i => i.ImageType == 1)
        );
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Pre execution stages do not support post-images"
            );
        }

        var assoc = plugins.Where(p =>
            string.Equals(p.Step.EventOperation, "Associate", StringComparison.Ordinal)
            || string.Equals(p.Step.EventOperation, "Disassociate", StringComparison.Ordinal)
        );

        invalid = assoc.FirstOrDefault(p => p.Step.FilteredAttributes != null);
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Associate/Disassociate cannot have filtered attributes"
            );
        }

        invalid = assoc.FirstOrDefault(p => p.Images.Count > 0);
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Associate/Disassociate cannot have images"
            );
        }

        invalid = assoc.FirstOrDefault(p => !string.IsNullOrEmpty(p.Step.LogicalName));
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Associate/Disassociate must target all entities"
            );
        }

        invalid = plugins.FirstOrDefault(p =>
            string.Equals(p.Step.EventOperation, "Create", StringComparison.Ordinal)
            && p.Images.Any(i => i.ImageType == 0)
        );
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Create does not support pre-images"
            );
        }

        invalid = plugins.FirstOrDefault(p =>
            string.Equals(p.Step.EventOperation, "Delete", StringComparison.Ordinal)
            && p.Images.Any(i => i.ImageType == 1)
        );
        if (invalid != null)
        {
            throw new InvalidOperationException(
                $"Plugin {invalid.Step.Name}: Delete does not support post-images"
            );
        }

        foreach (var plugin in plugins.Where(p => p.Step.UserContext != Guid.Empty))
        {
            if (!Exists(client, "systemuser", plugin.Step.UserContext))
            {
                throw new InvalidOperationException(
                    $"Plugin {plugin.Step.Name}: defined user context is not in the system"
                );
            }
        }
    }

    private static bool Exists(IOrganizationService service, string entityName, Guid id)
    {
        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        query.Criteria.AddCondition(entityName + "id", ConditionOperator.Equal, id);
        return service.RetrieveMultiple(query).Entities.Count > 0;
    }

    private (AssemblyRegistration? Assembly, RegisteredMaps Maps) RetrieveRegistered(
        ServiceClient client,
        Guid solutionId,
        string assemblyName
    )
    {
        var assemblyQuery = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("pluginassemblyid", "name", "sourcehash", "version"),
        };
        var assemblyLink = assemblyQuery.AddLink(
            "solutioncomponent",
            "pluginassemblyid",
            "objectid",
            JoinOperator.Inner
        );
        assemblyLink.LinkCriteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);

        var assemblyEntity = client
            .RetrieveMultiple(assemblyQuery)
            .Entities.FirstOrDefault(x =>
                string.Equals(
                    x.GetAttributeValue<string>("name"),
                    assemblyName,
                    StringComparison.Ordinal
                )
            );

        if (assemblyEntity == null)
        {
            logger.Verbose($"No registered assembly found matching {assemblyName}");
            return (null, RegisteredMaps.Empty);
        }

        var registration = new AssemblyRegistration(
            assemblyEntity.Id,
            assemblyEntity.GetAttributeValue<string>("sourcehash") ?? string.Empty,
            ParseVersion(assemblyEntity.GetAttributeValue<string>("version"))
        );

        logger.Verbose(
            $"Registered assembly version {registration.Version} found for {assemblyName}"
        );

        var typeQuery = new QueryExpression("plugintype") { ColumnSet = new ColumnSet(true) };
        typeQuery.Criteria.AddCondition(
            "pluginassemblyid",
            ConditionOperator.Equal,
            registration.Id
        );
        var typeEntities = client.RetrieveMultiple(typeQuery).Entities;
        var typeMap = ToEntityMap(typeEntities, x => x.GetAttributeValue<string>("name"));
        var validTypeIds = typeEntities.Select(x => x.Id).ToHashSet();

        var stepQuery = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet(true),
        };
        var stepLink = stepQuery.AddLink(
            "solutioncomponent",
            "sdkmessageprocessingstepid",
            "objectid",
            JoinOperator.Inner
        );
        stepLink.LinkCriteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);

        var stepEntities = client
            .RetrieveMultiple(stepQuery)
            .Entities.Where(step =>
            {
                var typeRef = step.GetAttributeValue<EntityReference>("plugintypeid");
                if (typeRef == null)
                {
                    var name = step.GetAttributeValue<string>("name") ?? step.Id.ToString();
                    logger.Warning(
                        $"Plugin Step '{name}' ({step.Id}) is missing plugintypeid and will be skipped."
                    );
                    return false;
                }

                return validTypeIds.Contains(typeRef.Id);
            })
            .ToList();

        var stepMap = ToEntityMap(stepEntities, x => x.GetAttributeValue<string>("name"));
        var stepById = stepEntities.ToDictionary(x => x.Id, x => x);

        var imageMap = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var step in stepEntities)
        {
            var imageQuery = new QueryExpression("sdkmessageprocessingstepimage")
            {
                ColumnSet = new ColumnSet(true),
            };
            imageQuery.Criteria.AddCondition(
                "sdkmessageprocessingstepid",
                ConditionOperator.Equal,
                step.Id
            );

            foreach (var image in client.RetrieveMultiple(imageQuery).Entities)
            {
                var stepRef = image.GetAttributeValue<EntityReference>(
                    "sdkmessageprocessingstepid"
                );
                if (stepRef == null)
                {
                    var imageName = image.GetAttributeValue<string>("name") ?? image.Id.ToString();
                    logger.Warning(
                        $"Plugin Image '{imageName}' ({image.Id}) is missing sdkmessageprocessingstepid and will be skipped."
                    );
                    continue;
                }

                if (!stepById.TryGetValue(stepRef.Id, out var parentStep))
                {
                    continue;
                }

                var stepName = parentStep.GetAttributeValue<string>("name") ?? string.Empty;
                var imageName2 = image.GetAttributeValue<string>("name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(stepName) || string.IsNullOrWhiteSpace(imageName2))
                {
                    continue;
                }

                imageMap[$"{stepName}, {imageName2}"] = image;
            }
        }

        var apiQuery = new QueryExpression("customapi") { ColumnSet = new ColumnSet(true) };
        var apiLink = apiQuery.AddLink(
            "solutioncomponent",
            "customapiid",
            "objectid",
            JoinOperator.Inner
        );
        apiLink.LinkCriteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);

        var apiEntities = client
            .RetrieveMultiple(apiQuery)
            .Entities.Where(api =>
            {
                var typeRef = api.GetAttributeValue<EntityReference>("plugintypeid");
                if (typeRef == null)
                {
                    var apiName = api.GetAttributeValue<string>("name") ?? api.Id.ToString();
                    logger.Warning(
                        $"Custom API '{apiName}' ({api.Id}) is missing plugintypeid and will be skipped."
                    );
                    return false;
                }

                return validTypeIds.Contains(typeRef.Id);
            })
            .ToList();

        var apiMap = ToEntityMap(apiEntities, x => x.GetAttributeValue<string>("name"));

        var reqMap = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var respMap = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var api in apiEntities)
        {
            var reqQuery = new QueryExpression("customapirequestparameter")
            {
                ColumnSet = new ColumnSet(true),
            };
            reqQuery.Criteria.AddCondition("customapiid", ConditionOperator.Equal, api.Id);
            foreach (var req in client.RetrieveMultiple(reqQuery).Entities)
            {
                var reqName = req.GetAttributeValue<string>("name");
                if (!string.IsNullOrWhiteSpace(reqName))
                {
                    reqMap[reqName] = req;
                }
            }

            var respQuery = new QueryExpression("customapiresponseproperty")
            {
                ColumnSet = new ColumnSet(true),
            };
            respQuery.Criteria.AddCondition("customapiid", ConditionOperator.Equal, api.Id);
            foreach (var resp in client.RetrieveMultiple(respQuery).Entities)
            {
                var respName = resp.GetAttributeValue<string>("name");
                if (!string.IsNullOrWhiteSpace(respName))
                {
                    respMap[respName] = resp;
                }
            }
        }

        return (
            registration,
            new RegisteredMaps(typeMap, stepMap, imageMap, apiMap, reqMap, respMap)
        );
    }

    private Dictionary<string, (Guid StepId, string EventOp)> BuildStepMap(
        ServiceClient client,
        IReadOnlyDictionary<string, Entity> steps
    )
    {
        var messageIds = steps
            .Values.Select(x => x.GetAttributeValue<EntityReference>("sdkmessageid")?.Id)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        var operationByMessageId = new Dictionary<Guid, string>();
        foreach (var messageId in messageIds)
        {
            var message = client.Retrieve("sdkmessage", messageId, new ColumnSet("name"));
            operationByMessageId[messageId] =
                message.GetAttributeValue<string>("name") ?? string.Empty;
        }

        var map = new Dictionary<string, (Guid StepId, string EventOp)>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            var sdkMessageRef = step.Value.GetAttributeValue<EntityReference>("sdkmessageid");
            if (sdkMessageRef == null)
            {
                logger.Warning(
                    $"Plugin Step '{step.Key}' ({step.Value.Id}) is missing sdkmessageid and will be skipped in step map."
                );
                continue;
            }

            map[step.Key] = (step.Value.Id, operationByMessageId[sdkMessageRef.Id]);
        }

        return map;
    }

    private Dictionary<
        (string EventOperation, string LogicalName),
        (Guid MessageId, Guid FilterId)
    > ResolveMessageFilters(ServiceClient client, IReadOnlyList<StepDefinition> steps)
    {
        var operations = steps
            .Select(x => x.EventOperation)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var messageIdByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            var query = new QueryExpression("sdkmessage")
            {
                ColumnSet = new ColumnSet("sdkmessageid", "name"),
                TopCount = 1,
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, operation);

            var message = client.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (message == null)
            {
                throw new InvalidOperationException(
                    $"SdkMessage with name '{operation}' not found."
                );
            }

            messageIdByName[operation] = message.Id;
        }

        var required = steps
            .Select(x =>
                (x.EventOperation, x.LogicalName, MessageId: messageIdByName[x.EventOperation])
            )
            .Distinct()
            .ToList();

        var filterQuery = new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet(
                "sdkmessagefilterid",
                "sdkmessageid",
                "primaryobjecttypecode"
            ),
        };

        var messageIds = required.Select(x => x.MessageId).Distinct().Cast<object>().ToArray();
        if (messageIds.Length > 0)
        {
            filterQuery.Criteria.AddCondition("sdkmessageid", ConditionOperator.In, messageIds);
        }

        var allFilters = RetrieveAllEntities(client, filterQuery);

        var map =
            new Dictionary<
                (string EventOperation, string LogicalName),
                (Guid MessageId, Guid FilterId)
            >();
        foreach (var item in required)
        {
            if (string.IsNullOrEmpty(item.LogicalName))
            {
                map[(item.EventOperation, item.LogicalName)] = (item.MessageId, Guid.Empty);
                continue;
            }

            var match = allFilters.FirstOrDefault(filter =>
            {
                var messageRef = filter.GetAttributeValue<EntityReference>("sdkmessageid");
                if (messageRef == null || messageRef.Id != item.MessageId)
                {
                    return false;
                }

                var filterEntity =
                    filter.GetAttributeValue<string>("primaryobjecttypecode") ?? string.Empty;

                return string.Equals(
                    item.LogicalName,
                    filterEntity,
                    StringComparison.OrdinalIgnoreCase
                );
            });

            if (match != null)
            {
                map[(item.EventOperation, item.LogicalName)] = (item.MessageId, match.Id);
            }
            else
            {
                var entityMsg = string.IsNullOrEmpty(item.LogicalName)
                    ? "any entity"
                    : $"entity '{item.LogicalName}'";
                logger.Warning(
                    $"SdkMessageFilter not found for operation '{item.EventOperation}' on {entityMsg}."
                );
            }
        }

        return map;
    }

    private static Entity CreateStepEntity(
        Guid typeId,
        Guid messageId,
        Guid filterId,
        string stepName,
        StepDefinition step
    )
    {
        var entity = new Entity("sdkmessageprocessingstep")
        {
            ["name"] = stepName,
            ["asyncautodelete"] = false,
            ["rank"] = step.ExecutionOrder,
            ["mode"] = new OptionSetValue(step.ExecutionMode),
            ["plugintypeid"] = new EntityReference("plugintype", typeId),
            ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
            ["stage"] = new OptionSetValue(step.ExecutionStage),
            ["filteringattributes"] = step.FilteredAttributes,
            ["supporteddeployment"] = new OptionSetValue(step.Deployment),
            ["description"] = StepDescription(step),
        };

        if (step.UserContext != Guid.Empty)
        {
            entity["impersonatinguserid"] = new EntityReference("systemuser", step.UserContext);
        }

        if (!string.IsNullOrEmpty(step.LogicalName))
        {
            entity["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId);
        }

        return entity;
    }

    private static readonly Dictionary<string, string> ImageMessagePropertyNameByOperation = new(
        StringComparer.Ordinal
    )
    {
        ["Assign"] = "Target",
        ["Create"] = "id",
        ["Delete"] = "Target",
        ["DeliverIncoming"] = "emailid",
        ["DeliverPromote"] = "emailid",
        ["Merge"] = "Target",
        ["Route"] = "Target",
        ["Send"] = "emailid",
        ["SetState"] = "entityMoniker",
        ["SetStateDynamicEntity"] = "entityMoniker",
        ["Update"] = "Target",
    };

    private static Entity CreateImageEntity(Guid stepId, string eventOp, ImageDefinition image)
    {
        if (!ImageMessagePropertyNameByOperation.TryGetValue(eventOp, out var messagePropertyName))
        {
            throw new InvalidOperationException(
                $"Event operation '{eventOp}' is not recognized for step image creation."
            );
        }

        return new Entity("sdkmessageprocessingstepimage")
        {
            ["name"] = image.Name,
            ["entityalias"] = image.EntityAlias,
            ["imagetype"] = new OptionSetValue(image.ImageType),
            ["attributes"] = image.Attributes,
            ["messagepropertyname"] = messagePropertyName,
            ["sdkmessageprocessingstepid"] = new EntityReference(
                "sdkmessageprocessingstep",
                stepId
            ),
        };
    }

    private static Entity CreateApiEntity(
        MessageDefinition message,
        string publisherPrefix,
        Guid pluginTypeId
    )
    {
        return new Entity("customapi")
        {
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(
                message.AllowedCustomProcessingStepType
            ),
            ["bindingtype"] = new OptionSetValue(message.BindingType),
            ["boundentitylogicalname"] = message.BoundEntityLogicalName,
            ["description"] = SyncDescription(),
            ["displayname"] = message.DisplayName,
            ["executeprivilegename"] = message.ExecutePrivilegeName,
            ["isfunction"] = message.IsFunction,
            ["isprivate"] = message.IsPrivate,
            ["name"] = message.Name,
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
            ["uniquename"] = string.IsNullOrWhiteSpace(publisherPrefix)
                ? message.UniqueName
                : $"{publisherPrefix}_{message.UniqueName}",
            ["iscustomizable"] = new BooleanManagedProperty(message.IsCustomizable),
        };
    }

    private static Entity CreateAssemblyEntity(LocalAssembly local)
    {
        var assemblyName = AssemblyName.GetAssemblyName(local.DllPath);
        var culture = string.IsNullOrWhiteSpace(assemblyName.CultureName)
            ? "neutral"
            : assemblyName.CultureName;
        var publicKeyToken = ConvertPublicKeyToken(assemblyName.GetPublicKeyToken());

        return new Entity("pluginassembly")
        {
            ["name"] = local.DllName,
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(local.DllPath)),
            ["sourcehash"] = local.SourceHash,
            ["isolationmode"] = new OptionSetValue((int)local.IsolationMode),
            ["version"] = local.Version.ToString(),
            ["description"] = SyncDescription(),
            ["culture"] = culture,
            ["publickeytoken"] = publicKeyToken,
            ["sourcetype"] = new OptionSetValue(0),
        };
    }

    private static OrganizationRequest CreateRequestWithSolution(Entity entity, string solutionName)
    {
        var request = new CreateRequest { Target = entity };
        request["SolutionUniqueName"] = solutionName;
        return request;
    }

    private static Guid CreateWithSolution(ServiceClient client, Entity entity, string solutionName)
    {
        var request = new CreateRequest { Target = entity };
        request["SolutionUniqueName"] = solutionName;
        var response = (CreateResponse)client.Execute(request);
        return response.id;
    }

    private static SourceMaps BuildSourceMaps(
        IReadOnlyList<PluginDefinition> plugins,
        IReadOnlyList<CustomApiDefinition> apis
    )
    {
        var typeMap = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal);
        var stepMap = new Dictionary<string, StepDefinition>(StringComparer.Ordinal);
        var imageMap = new Dictionary<string, ImageDefinition>(StringComparer.Ordinal);

        foreach (var plugin in plugins)
        {
            if (!typeMap.ContainsKey(plugin.TypeKey))
            {
                typeMap[plugin.TypeKey] = plugin;
            }

            stepMap[plugin.StepKey] = plugin.Step;
            foreach (var image in plugin.Images)
            {
                imageMap[$"{plugin.StepKey}, {image.Name}"] = image;
            }
        }

        var customApiTypeMap = new Dictionary<string, CustomApiDefinition>(StringComparer.Ordinal);
        var customApiMap = new Dictionary<string, CustomApiDefinition>(StringComparer.Ordinal);
        var reqMap = new Dictionary<string, RequestParamDefinition>(StringComparer.Ordinal);
        var respMap = new Dictionary<string, ResponsePropDefinition>(StringComparer.Ordinal);

        foreach (var api in apis)
        {
            if (!customApiTypeMap.ContainsKey(api.TypeKey))
            {
                customApiTypeMap[api.TypeKey] = api;
            }

            customApiMap[api.Key] = api;
            foreach (var req in api.RequestParams)
            {
                reqMap[req.Name] = req;
            }

            foreach (var resp in api.ResponseProps)
            {
                respMap[resp.Name] = resp;
            }
        }

        // Legacy parity: merge custom API plugin types into the plugin type map.
        // This prevents custom API handler types from being treated as obsolete plugin types.
        foreach (var apiType in customApiTypeMap)
        {
            if (!typeMap.ContainsKey(apiType.Key))
            {
                var syntheticStep = new StepDefinition(
                    PluginTypeName: apiType.Key,
                    ExecutionStage: 10,
                    EventOperation: string.Empty,
                    LogicalName: string.Empty,
                    Deployment: 1,
                    ExecutionMode: 1,
                    Name: apiType.Key,
                    Description: apiType.Key,
                    ExecutionOrder: 1,
                    FilteredAttributes: string.Empty,
                    UserContext: Guid.Empty
                );

                typeMap[apiType.Key] = new PluginDefinition(syntheticStep, []);
            }
        }

        return new SourceMaps(
            typeMap,
            stepMap,
            imageMap,
            customApiTypeMap,
            customApiMap,
            reqMap,
            respMap
        );
    }

    private static DiffResult<T> Diff<T>(
        IReadOnlyDictionary<string, T> source,
        IReadOnlyDictionary<string, Entity> target,
        Func<T, Entity, bool> comparer
    )
    {
        var adds = new Dictionary<string, T>(StringComparer.Ordinal);
        var updates = new Dictionary<string, (T Source, Entity Target)>(StringComparer.Ordinal);
        var deletes = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var src in source)
        {
            if (!target.TryGetValue(src.Key, out var targetEntity))
            {
                adds[src.Key] = src.Value;
                continue;
            }

            if (!comparer(src.Value, targetEntity))
            {
                updates[src.Key] = (src.Value, targetEntity);
            }
        }

        foreach (var trg in target)
        {
            if (!source.ContainsKey(trg.Key))
            {
                deletes[trg.Key] = trg.Value;
            }
        }

        return new DiffResult<T>(adds, updates, deletes);
    }

    private void LogDiff<T>(string title, DiffResult<T> diff)
    {
        logger.Info(
            $"{title}: create={diff.Adds.Count}, update={diff.Differences.Count}, delete={diff.Deletes.Count}"
        );
    }

    private void LogDryRunInventory(SourceMaps sourceMaps, RegisteredMaps targetMaps)
    {
        logger.Info("Found in source assembly:");
        logger.Info(
            $"  Types={sourceMaps.Types.Count}, Steps={sourceMaps.Steps.Count}, Images={sourceMaps.Images.Count}, Custom APIs={sourceMaps.CustomApis.Count}, Custom API request parameters={sourceMaps.RequestParams.Count}, Custom API response properties={sourceMaps.ResponseProps.Count}"
        );

        logger.Info("Found in Dataverse (scoped to current assembly + solution):");
        logger.Info(
            $"  Types={targetMaps.Types.Count}, Steps={targetMaps.Steps.Count}, Images={targetMaps.Images.Count}, Custom APIs={targetMaps.CustomApis.Count}, Custom API request parameters={targetMaps.RequestParams.Count}, Custom API response properties={targetMaps.ResponseProps.Count}"
        );
    }

    private void LogDryRunDetails<T>(
        string title,
        DiffResult<T> diff,
        Func<T, Entity, IEnumerable<string>> differenceDescriber
    )
    {
        logger.Info($"{title} details:");

        if (diff.Adds.Count > 0)
        {
            logger.Info($"  CREATE ({diff.Adds.Count})");
            foreach (var key in diff.Adds.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                logger.Info($"    + {key}");
            }
        }
        else
        {
            logger.Info("  CREATE (0)");
        }

        if (diff.Differences.Count > 0)
        {
            logger.Info($"  UPDATE ({diff.Differences.Count})");
            foreach (var pair in diff.Differences.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var reasons = differenceDescriber(pair.Value.Source, pair.Value.Target)
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .ToArray();

                var reasonText = reasons.Length == 0 ? "changed" : string.Join("; ", reasons);

                logger.Info($"    ~ {pair.Key}: {reasonText}");
            }
        }
        else
        {
            logger.Info("  UPDATE (0)");
        }

        if (diff.Deletes.Count > 0)
        {
            logger.Info($"  DELETE ({diff.Deletes.Count})");
            foreach (var key in diff.Deletes.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                logger.Info($"    - {key}");
            }
        }
        else
        {
            logger.Info("  DELETE (0)");
        }
    }

    private static IEnumerable<string> DescribeTypeDifference(
        PluginDefinition source,
        Entity target
    )
    {
        var targetName = target.GetAttributeValue<string>("name") ?? string.Empty;
        if (!string.Equals(targetName, source.TypeKey, StringComparison.Ordinal))
        {
            yield return $"name: CRM='{targetName}' source='{source.TypeKey}'";
        }
    }

    private static IEnumerable<string> DescribeStepDifference(StepDefinition source, Entity target)
    {
        var stage = target.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 20;
        if (stage != source.ExecutionStage)
        {
            yield return $"stage: CRM={stage} source={source.ExecutionStage}";
        }

        var deployment =
            target.GetAttributeValue<OptionSetValue>("supporteddeployment")?.Value ?? 0;
        if (deployment != source.Deployment)
        {
            yield return $"deployment: CRM={deployment} source={source.Deployment}";
        }

        var mode = target.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0;
        if (mode != source.ExecutionMode)
        {
            yield return $"mode: CRM={mode} source={source.ExecutionMode}";
        }

        var rank = target.GetAttributeValue<int?>("rank") ?? 0;
        if (rank != source.ExecutionOrder)
        {
            yield return $"execution order: CRM={rank} source={source.ExecutionOrder}";
        }

        var filtered = target.GetAttributeValue<string>("filteringattributes");
        if (!string.Equals(filtered, source.FilteredAttributes, StringComparison.Ordinal))
        {
            yield return $"filtered attributes: CRM='{filtered}' source='{source.FilteredAttributes}'";
        }

        var userContext =
            target.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id ?? Guid.Empty;
        if (userContext != source.UserContext)
        {
            yield return $"user context: CRM={userContext} source={source.UserContext}";
        }
    }

    private static IEnumerable<string> DescribeImageDifference(
        ImageDefinition source,
        Entity target
    )
    {
        var alias = target.GetAttributeValue<string>("entityalias");
        if (!string.Equals(alias, source.EntityAlias, StringComparison.Ordinal))
        {
            yield return $"entity alias: CRM='{alias}' source='{source.EntityAlias}'";
        }

        var imageType = target.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0;
        if (imageType != source.ImageType)
        {
            yield return $"image type: CRM={imageType} source={source.ImageType}";
        }

        var attrs = target.GetAttributeValue<string>("attributes");
        if (!string.Equals(attrs, source.Attributes, StringComparison.Ordinal))
        {
            yield return $"attributes: CRM='{attrs}' source='{source.Attributes}'";
        }
    }

    private static IEnumerable<string> DescribeApiDifference(
        CustomApiDefinition source,
        Entity target
    )
    {
        var targetName = target.GetAttributeValue<string>("name") ?? string.Empty;
        if (!string.Equals(targetName, source.Message.Name, StringComparison.Ordinal))
        {
            yield return $"name: CRM='{targetName}' source='{source.Message.Name}'";
        }

        var targetDisplayName = target.GetAttributeValue<string>("displayname") ?? string.Empty;
        if (!string.Equals(targetDisplayName, source.Message.DisplayName, StringComparison.Ordinal))
        {
            yield return $"display name: CRM='{targetDisplayName}' source='{source.Message.DisplayName}'";
        }

        var targetDescription = target.GetAttributeValue<string>("description");
        if (!string.Equals(targetDescription, source.Message.Description, StringComparison.Ordinal))
        {
            yield return $"description: CRM='{targetDescription}' source='{source.Message.Description}'";
        }

        var pluginTypeName = target.GetAttributeValue<EntityReference>("plugintypeid")?.Name;
        if (!string.Equals(pluginTypeName, source.Message.PluginTypeName, StringComparison.Ordinal))
        {
            yield return $"plugin type: CRM='{pluginTypeName}' source='{source.Message.PluginTypeName}'";
        }

        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;
        if (isCustomizable != source.Message.IsCustomizable)
        {
            yield return $"is customizable: CRM={isCustomizable} source={source.Message.IsCustomizable}";
        }

        var isPrivate = target.GetAttributeValue<bool>("isprivate");
        if (isPrivate != source.Message.IsPrivate)
        {
            yield return $"is private: CRM={isPrivate} source={source.Message.IsPrivate}";
        }

        var executePrivilege = target.GetAttributeValue<string>("executeprivilegename");
        if (
            !string.Equals(
                executePrivilege,
                source.Message.ExecutePrivilegeName,
                StringComparison.Ordinal
            )
        )
        {
            yield return $"execute privilege: CRM='{executePrivilege}' source='{source.Message.ExecutePrivilegeName}'";
        }
    }

    private static IEnumerable<string> DescribeRequestParameterDifference(
        RequestParamDefinition source,
        Entity target
    )
    {
        var name = target.GetAttributeValue<string>("name") ?? string.Empty;
        if (!string.Equals(name, source.Name, StringComparison.Ordinal))
        {
            yield return $"name: CRM='{name}' source='{source.Name}'";
        }

        var displayName = target.GetAttributeValue<string>("displayname");
        if (!string.Equals(displayName, source.DisplayName, StringComparison.Ordinal))
        {
            yield return $"display name: CRM='{displayName}' source='{source.DisplayName}'";
        }

        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;
        if (isCustomizable != source.IsCustomizable)
        {
            yield return $"is customizable: CRM={isCustomizable} source={source.IsCustomizable}";
        }

        var isOptional = target.GetAttributeValue<bool>("isoptional");
        if (isOptional != source.IsOptional)
        {
            yield return $"is optional: CRM={isOptional} source={source.IsOptional}";
        }

        var logicalEntityName = target.GetAttributeValue<string>("logicalentityname");
        if (!string.Equals(logicalEntityName, source.LogicalEntityName, StringComparison.Ordinal))
        {
            yield return $"logical entity: CRM='{logicalEntityName}' source='{source.LogicalEntityName}'";
        }

        var type = target.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0;
        if (type != source.Type)
        {
            yield return $"type: CRM={type} source={source.Type}";
        }
    }

    private static IEnumerable<string> DescribeResponsePropertyDifference(
        ResponsePropDefinition source,
        Entity target
    )
    {
        var name = target.GetAttributeValue<string>("name") ?? string.Empty;
        if (!string.Equals(name, source.Name, StringComparison.Ordinal))
        {
            yield return $"name: CRM='{name}' source='{source.Name}'";
        }

        var displayName = target.GetAttributeValue<string>("displayname");
        if (!string.Equals(displayName, source.DisplayName, StringComparison.Ordinal))
        {
            yield return $"display name: CRM='{displayName}' source='{source.DisplayName}'";
        }

        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;
        if (isCustomizable != source.IsCustomizable)
        {
            yield return $"is customizable: CRM={isCustomizable} source={source.IsCustomizable}";
        }

        var logicalEntityName = target.GetAttributeValue<string>("logicalentityname");
        if (!string.Equals(logicalEntityName, source.LogicalEntityName, StringComparison.Ordinal))
        {
            yield return $"logical entity: CRM='{logicalEntityName}' source='{source.LogicalEntityName}'";
        }

        var type = target.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0;
        if (type != source.Type)
        {
            yield return $"type: CRM={type} source={source.Type}";
        }
    }

    private static bool ComparePluginType(PluginDefinition source, Entity target) =>
        string.Equals(
            target.GetAttributeValue<string>("name"),
            source.TypeKey,
            StringComparison.Ordinal
        );

    private static bool CompareStep(StepDefinition source, Entity target)
    {
        var stage = target.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 20;
        var deployment =
            target.GetAttributeValue<OptionSetValue>("supporteddeployment")?.Value ?? 0;
        var mode = target.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0;
        var rank = target.GetAttributeValue<int?>("rank") ?? 0;
        var filtered = target.GetAttributeValue<string>("filteringattributes");
        var userContext =
            target.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id ?? Guid.Empty;

        return stage == source.ExecutionStage
            && deployment == source.Deployment
            && mode == source.ExecutionMode
            && rank == source.ExecutionOrder
            && string.Equals(filtered, source.FilteredAttributes, StringComparison.Ordinal)
            && userContext == source.UserContext;
    }

    private static bool CompareImage(ImageDefinition source, Entity target)
    {
        var alias = target.GetAttributeValue<string>("entityalias");
        var imageType = target.GetAttributeValue<OptionSetValue>("imagetype")?.Value ?? 0;
        var attrs = target.GetAttributeValue<string>("attributes");

        return string.Equals(alias, source.EntityAlias, StringComparison.Ordinal)
            && imageType == source.ImageType
            && string.Equals(attrs, source.Attributes, StringComparison.Ordinal);
    }

    private static bool CompareApi(CustomApiDefinition source, Entity target)
    {
        var pluginType = target.GetAttributeValue<EntityReference>("plugintypeid");
        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;

        return string.Equals(
                target.GetAttributeValue<string>("name"),
                source.Message.Name,
                StringComparison.Ordinal
            )
            && string.Equals(
                target.GetAttributeValue<string>("displayname"),
                source.Message.DisplayName,
                StringComparison.Ordinal
            )
            && string.Equals(
                target.GetAttributeValue<string>("description"),
                source.Message.Description,
                StringComparison.Ordinal
            )
            && string.Equals(
                pluginType?.Name,
                source.Message.PluginTypeName,
                StringComparison.Ordinal
            )
            && isCustomizable == source.Message.IsCustomizable
            && target.GetAttributeValue<bool>("isprivate") == source.Message.IsPrivate
            && string.Equals(
                target.GetAttributeValue<string>("executeprivilegename"),
                source.Message.ExecutePrivilegeName,
                StringComparison.Ordinal
            );
    }

    private static bool CompareReqParam(RequestParamDefinition source, Entity target)
    {
        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;
        var type = target.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0;

        return string.Equals(
                target.GetAttributeValue<string>("name"),
                source.Name,
                StringComparison.Ordinal
            )
            && string.Equals(
                target.GetAttributeValue<string>("displayname"),
                source.DisplayName,
                StringComparison.Ordinal
            )
            && isCustomizable == source.IsCustomizable
            && target.GetAttributeValue<bool>("isoptional") == source.IsOptional
            && string.Equals(
                target.GetAttributeValue<string>("logicalentityname"),
                source.LogicalEntityName,
                StringComparison.Ordinal
            )
            && type == source.Type;
    }

    private static bool CompareRespProp(ResponsePropDefinition source, Entity target)
    {
        var isCustomizable =
            target.GetAttributeValue<BooleanManagedProperty>("iscustomizable")?.Value ?? false;
        var type = target.GetAttributeValue<OptionSetValue>("type")?.Value ?? 0;

        return string.Equals(
                target.GetAttributeValue<string>("name"),
                source.Name,
                StringComparison.Ordinal
            )
            && string.Equals(
                target.GetAttributeValue<string>("displayname"),
                source.DisplayName,
                StringComparison.Ordinal
            )
            && isCustomizable == source.IsCustomizable
            && string.Equals(
                target.GetAttributeValue<string>("logicalentityname"),
                source.LogicalEntityName,
                StringComparison.Ordinal
            )
            && type == source.Type;
    }

    private static Dictionary<string, Entity> ToEntityMap(
        IEnumerable<Entity> entities,
        Func<Entity, string?> keySelector
    )
    {
        var map = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var key = keySelector(entity);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = entity;
            }
        }

        return map;
    }

    private static List<ExecuteMultipleResponseItem> ExecuteBulk(
        ServiceClient client,
        IReadOnlyList<OrganizationRequest> requests,
        int chunkSize,
        TimeSpan delayBetweenBatches,
        bool failOnFault,
        Action<OrganizationServiceFault, int>? faultHandler = null
    )
    {
        var result = new List<ExecuteMultipleResponseItem>();
        if (requests.Count == 0)
        {
            return result;
        }

        var batches = Chunk(requests, chunkSize).ToList();
        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            if (batchIndex > 0 && delayBetweenBatches > TimeSpan.Zero)
            {
                Thread.Sleep(delayBetweenBatches);
            }

            var exec = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings
                {
                    ContinueOnError = true,
                    ReturnResponses = true,
                },
                Requests = new OrganizationRequestCollection(),
            };

            foreach (var req in batches[batchIndex])
            {
                exec.Requests.Add(req);
            }

            var response = (ExecuteMultipleResponse)client.Execute(exec);
            foreach (var item in response.Responses)
            {
                var globalIndex = (batchIndex * chunkSize) + item.RequestIndex;
                if (item.Fault != null)
                {
                    faultHandler?.Invoke(item.Fault, globalIndex);
                    if (failOnFault)
                    {
                        throw new InvalidOperationException(item.Fault.Message);
                    }
                }

                result.Add(item);
            }
        }

        return result;
    }

    private static List<Entity> RetrieveAllEntities(ServiceClient client, QueryExpression query)
    {
        var entities = new List<Entity>();
        query.PageInfo = new PagingInfo
        {
            Count = 5000,
            PageNumber = 1,
        };

        while (true)
        {
            var response = client.RetrieveMultiple(query);
            entities.AddRange(response.Entities);

            if (!response.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = response.PagingCookie;
        }

        return entities;
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int chunkSize)
    {
        for (var i = 0; i < source.Count; i += chunkSize)
        {
            var size = Math.Min(chunkSize, source.Count - i);
            var list = new List<T>(size);
            for (var j = 0; j < size; j++)
            {
                list.Add(source[i + j]);
            }
            yield return list;
        }
    }

    private static List<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().ToList();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>().ToList();
        }
    }

    private static string ComputeSha1(byte[] bytes)
    {
        var hash = SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Version ParseVersion(string? input) =>
        Version.TryParse(input, out var version) ? version : new Version(0, 0, 0, 0);

    private static string SyncDescription() => $"Synchronized by XrmPackager ({DateTime.UtcNow:O})";

    private static string StepDescription(StepDefinition step) =>
        string.IsNullOrWhiteSpace(step.Description) ? SyncDescription() : step.Description;

    private static string ConvertPublicKeyToken(byte[]? tokenBytes)
    {
        if (tokenBytes is null || tokenBytes.Length == 0)
        {
            return "null";
        }

        return BitConverter.ToString(tokenBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static bool IsMissingDeleteFault(OrganizationServiceFault fault)
    {
        var message = fault.Message ?? string.Empty;
        return message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || message.Contains("cannot find", StringComparison.OrdinalIgnoreCase);
    }

    private static int TupleInt(ITuple tuple, int index) => Convert.ToInt32(tuple[index]);

    private static bool TupleBool(ITuple tuple, int index) => Convert.ToBoolean(tuple[index]);

    private static string TupleString(ITuple tuple, int index) =>
        tuple[index]?.ToString() ?? string.Empty;

    private static string? TupleNullableString(ITuple tuple, int index) => tuple[index]?.ToString();

    private PluginDefinition ToPluginDefinition(object tupleObj)
    {
        var root = (ITuple)tupleObj;
        var stepTuple = (ITuple)root[0]!;
        var extTuple = (ITuple)root[1]!;
        var imageTuples = (IEnumerable)root[2]!;

        var className = TupleString(stepTuple, 0);
        var stage = TupleInt(stepTuple, 1);
        var eventOperation = TupleString(stepTuple, 2);
        var logicalName = TupleString(stepTuple, 3);

        var deployment = TupleInt(extTuple, 0);
        var executionMode = TupleInt(extTuple, 1);
        var configuredStepName = TupleNullableString(extTuple, 2);
        var executionOrder = TupleInt(extTuple, 3);
        var filteredAttributes = TupleNullableString(extTuple, 4);
        var userContextRaw = TupleNullableString(extTuple, 5);

        var entityDisplay = string.IsNullOrEmpty(logicalName) ? "any Entity" : logicalName;
        var modeDisplay = executionMode switch
        {
            0 => "Synchronous",
            1 => "Asynchronous",
            _ => "Unknown",
        };
        var stageDisplay = stage switch
        {
            10 => "PreValidation",
            20 => "Pre",
            40 => "Post",
            _ => "Unknown",
        };

        var stepName =
            $"{className}: {modeDisplay} {stageDisplay} {eventOperation} of {entityDisplay}";
        var stepDescription = string.IsNullOrWhiteSpace(configuredStepName)
            ? stepName
            : configuredStepName;

        var userContext = Guid.TryParse(userContextRaw, out var user) ? user : Guid.Empty;
        var step = new StepDefinition(
            PluginTypeName: className,
            ExecutionStage: stage,
            EventOperation: eventOperation,
            LogicalName: logicalName,
            Deployment: deployment,
            ExecutionMode: executionMode,
            Name: stepName,
            Description: stepDescription,
            ExecutionOrder: executionOrder,
            FilteredAttributes: filteredAttributes,
            UserContext: userContext
        );

        var images = new List<ImageDefinition>();
        foreach (var imgObj in imageTuples)
        {
            var img = (ITuple)imgObj!;
            images.Add(
                new ImageDefinition(
                    StepName: stepName,
                    Name: TupleString(img, 0),
                    EntityAlias: TupleString(img, 1),
                    ImageType: TupleInt(img, 2),
                    Attributes: TupleNullableString(img, 3)
                )
            );
        }

        return new PluginDefinition(step, images);
    }

    private CustomApiDefinition ToCustomApiDefinition(object tupleObj)
    {
        var root = (ITuple)tupleObj;
        var mainTuple = (ITuple)root[0]!;
        var extTuple = (ITuple)root[1]!;
        var reqTuples = (IEnumerable)root[2]!;
        var respTuples = (IEnumerable)root[3]!;

        var name = TupleString(mainTuple, 0);
        var ownerRaw = TupleNullableString(extTuple, 1);
        var ownerId = Guid.TryParse(ownerRaw, out var parsedOwner) ? parsedOwner : Guid.Empty;

        var message = new MessageDefinition(
            UniqueName: name,
            Name: name,
            DisplayName: name,
            Description: TupleNullableString(extTuple, 6),
            IsFunction: TupleBool(mainTuple, 1),
            EnabledForWorkflow: TupleInt(mainTuple, 2),
            BindingType: TupleInt(mainTuple, 4),
            BoundEntityLogicalName: TupleNullableString(mainTuple, 5),
            AllowedCustomProcessingStepType: TupleInt(mainTuple, 3),
            PluginTypeName: TupleString(extTuple, 0),
            OwnerId: ownerId,
            OwnerType: TupleNullableString(extTuple, 2),
            IsCustomizable: TupleBool(extTuple, 3),
            IsPrivate: TupleBool(extTuple, 4),
            ExecutePrivilegeName: TupleNullableString(extTuple, 5)
        );

        var req = new List<RequestParamDefinition>();
        foreach (var reqObj in reqTuples)
        {
            var t = (ITuple)reqObj!;
            req.Add(
                new RequestParamDefinition(
                    Name: TupleString(t, 0),
                    UniqueName: TupleString(t, 1),
                    CustomApiName: message.Name,
                    DisplayName: TupleNullableString(t, 2),
                    IsCustomizable: TupleBool(t, 3),
                    IsOptional: TupleBool(t, 4),
                    LogicalEntityName: TupleNullableString(t, 5),
                    Type: TupleInt(t, 6)
                )
            );
        }

        var resp = new List<ResponsePropDefinition>();
        foreach (var respObj in respTuples)
        {
            var t = (ITuple)respObj!;
            resp.Add(
                new ResponsePropDefinition(
                    Name: TupleString(t, 0),
                    UniqueName: TupleString(t, 1),
                    CustomApiName: message.Name,
                    DisplayName: TupleNullableString(t, 2),
                    IsCustomizable: TupleBool(t, 3),
                    LogicalEntityName: TupleNullableString(t, 4),
                    Type: TupleInt(t, 5)
                )
            );
        }

        return new CustomApiDefinition(message, req, resp);
    }

    private enum AssemblyOperation
    {
        Unchanged,
        Update,
        UpdateWithRecreate,
        Create,
    }

    private sealed record LocalAssembly(
        string DllName,
        string DllPath,
        string SourceHash,
        Version Version,
        AssemblyIsolationMode IsolationMode,
        IReadOnlyList<PluginDefinition> Plugins,
        IReadOnlyList<CustomApiDefinition> CustomApis
    );

    private sealed record AssemblyRegistration(Guid Id, string SourceHash, Version Version);

    private sealed record PluginDefinition(
        StepDefinition Step,
        IReadOnlyList<ImageDefinition> Images
    )
    {
        public string TypeKey => Step.PluginTypeName;

        public string StepKey => Step.Name;
    }

    private sealed record StepDefinition(
        string PluginTypeName,
        int ExecutionStage,
        string EventOperation,
        string LogicalName,
        int Deployment,
        int ExecutionMode,
        string Name,
        string Description,
        int ExecutionOrder,
        string? FilteredAttributes,
        Guid UserContext
    );

    private sealed record ImageDefinition(
        string StepName,
        string Name,
        string EntityAlias,
        int ImageType,
        string? Attributes
    );

    private sealed record MessageDefinition(
        string UniqueName,
        string Name,
        string DisplayName,
        string? Description,
        bool IsFunction,
        int EnabledForWorkflow,
        int BindingType,
        string? BoundEntityLogicalName,
        int AllowedCustomProcessingStepType,
        string PluginTypeName,
        Guid OwnerId,
        string? OwnerType,
        bool IsCustomizable,
        bool IsPrivate,
        string? ExecutePrivilegeName
    );

    private sealed record RequestParamDefinition(
        string Name,
        string UniqueName,
        string CustomApiName,
        string? DisplayName,
        bool IsCustomizable,
        bool IsOptional,
        string? LogicalEntityName,
        int Type
    );

    private sealed record ResponsePropDefinition(
        string Name,
        string UniqueName,
        string CustomApiName,
        string? DisplayName,
        bool IsCustomizable,
        string? LogicalEntityName,
        int Type
    );

    private sealed record CustomApiDefinition(
        MessageDefinition Message,
        IReadOnlyList<RequestParamDefinition> RequestParams,
        IReadOnlyList<ResponsePropDefinition> ResponseProps
    )
    {
        public string TypeKey => Message.PluginTypeName;

        public string Key => Message.UniqueName;
    }

    private sealed record SourceMaps(
        Dictionary<string, PluginDefinition> Types,
        Dictionary<string, StepDefinition> Steps,
        Dictionary<string, ImageDefinition> Images,
        Dictionary<string, CustomApiDefinition> CustomApiTypeMap,
        Dictionary<string, CustomApiDefinition> CustomApis,
        Dictionary<string, RequestParamDefinition> RequestParams,
        Dictionary<string, ResponsePropDefinition> ResponseProps
    );

    private sealed record RegisteredMaps(
        Dictionary<string, Entity> Types,
        Dictionary<string, Entity> Steps,
        Dictionary<string, Entity> Images,
        Dictionary<string, Entity> CustomApis,
        Dictionary<string, Entity> RequestParams,
        Dictionary<string, Entity> ResponseProps
    )
    {
        public static readonly RegisteredMaps Empty = new(
            new Dictionary<string, Entity>(StringComparer.Ordinal),
            new Dictionary<string, Entity>(StringComparer.Ordinal),
            new Dictionary<string, Entity>(StringComparer.Ordinal),
            new Dictionary<string, Entity>(StringComparer.Ordinal),
            new Dictionary<string, Entity>(StringComparer.Ordinal),
            new Dictionary<string, Entity>(StringComparer.Ordinal)
        );
    }

    private sealed record DiffResult<T>(
        Dictionary<string, T> Adds,
        Dictionary<string, (T Source, Entity Target)> Differences,
        Dictionary<string, Entity> Deletes
    );
}
