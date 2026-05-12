namespace XrmPackager.Core.Crm;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

public sealed class MasterDataOperations
{
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    public MasterDataOperations(ILogger logger)
    {
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Export
    // -------------------------------------------------------------------------

    public void Export(ServiceClient client, MasterDataExportOptions options)
    {
        var schemas = MasterDataSchemaParser.Parse(options.SchemaPath);

        foreach (var entitySchema in schemas)
        {
            _logger.Info($"Exporting entity: {entitySchema.Name}");
            var outputFolder = Path.Combine(options.DataFolder, entitySchema.Name);
            if (Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, recursive: true);
            Directory.CreateDirectory(outputFolder);

            var fieldNames = entitySchema.Fields.Select(f => f.Name).ToArray();
            int recordCount;

            if (entitySchema.FetchXmlFilter is not null)
            {
                recordCount = ExportWithFetchXml(client, entitySchema, outputFolder, fieldNames);
            }
            else
            {
                recordCount = ExportWithQueryExpression(client, entitySchema, outputFolder, fieldNames);
            }

            _logger.Info($"  Exported {recordCount} record(s) for {entitySchema.Name}");
        }
    }

    private int ExportWithQueryExpression(
        ServiceClient client,
        MasterDataEntitySchema entitySchema,
        string outputFolder,
        string[] fieldNames
    )
    {
        var query = new QueryExpression(entitySchema.Name)
        {
            ColumnSet = new ColumnSet(fieldNames),
            PageInfo = new PagingInfo
            {
                Count = 5000,
                PageNumber = 1,
                ReturnTotalRecordCount = false,
            },
        };

        var recordCount = 0;
        EntityCollection results;
        do
        {
            results = client.RetrieveMultiple(query);
            WriteRecords(results, entitySchema, outputFolder, ref recordCount);
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = results.PagingCookie;
        } while (results.MoreRecords);

        return recordCount;
    }

    private int ExportWithFetchXml(
        ServiceClient client,
        MasterDataEntitySchema entitySchema,
        string outputFolder,
        string[] fieldNames
    )
    {
        // Parse the stored FetchXML and extract the <filter> element(s) from it
        var filterDoc = XDocument.Parse(entitySchema.FetchXmlFilter!);
        var filterElements = filterDoc.Root
            ?.Element("entity")
            ?.Elements("filter")
            .Select(f => new XElement(f)) // deep-copy
            .ToList() ?? new List<XElement>();

        var recordCount = 0;
        var page = 1;
        string? pagingCookie = null;

        while (true)
        {
            var fetchXml = BuildFetchXml(
                entitySchema.Name,
                fieldNames,
                filterElements,
                page,
                pagingCookie
            );
            var results = client.RetrieveMultiple(new FetchExpression(fetchXml));
            WriteRecords(results, entitySchema, outputFolder, ref recordCount);

            if (!results.MoreRecords)
                break;

            pagingCookie = results.PagingCookie;
            page++;
        }

        return recordCount;
    }

    private static string BuildFetchXml(
        string entityName,
        string[] fieldNames,
        List<XElement> filterElements,
        int page,
        string? pagingCookie
    )
    {
        var fetchEl = new XElement(
            "fetch",
            new XAttribute("version", "1.0"),
            new XAttribute("mapping", "logical"),
            new XAttribute("distinct", "true"),
            new XAttribute("page", page),
            new XAttribute("count", "5000")
        );

        if (pagingCookie is not null)
            fetchEl.Add(new XAttribute("paging-cookie", pagingCookie));

        var entityEl = new XElement("entity", new XAttribute("name", entityName));

        foreach (var field in fieldNames)
            entityEl.Add(new XElement("attribute", new XAttribute("name", field)));

        foreach (var filter in filterElements)
            entityEl.Add(new XElement(filter));

        fetchEl.Add(entityEl);
        return fetchEl.ToString();
    }

    private void WriteRecords(
        EntityCollection results,
        MasterDataEntitySchema entitySchema,
        string outputFolder,
        ref int recordCount
    )
    {
        foreach (var record in results.Entities)
        {
            var json = SerializeRecord(record, entitySchema);
            var filePath = Path.Combine(outputFolder, $"{record.Id}.json");
            File.WriteAllText(filePath, json);
            recordCount++;
        }
    }

    private static string SerializeRecord(Entity record, MasterDataEntitySchema schema)
    {
        var obj = new JsonObject();

        foreach (var field in schema.Fields)
        {
            if (!record.Attributes.TryGetValue(field.Name, out var rawValue) || rawValue is null)
            {
                obj[field.Name] = null;
                continue;
            }

            obj[field.Name] = SerializeValue(rawValue, field.Type);
        }

        return JsonSerializer.Serialize(obj, JsonWriteOptions);
    }

    private static JsonNode? SerializeValue(object value, string fieldType)
    {
        // Dispatch on runtime type first — avoids schema mismatches (e.g. statecode/statuscode)
        return value switch
        {
            OptionSetValue osv => JsonValue.Create(osv.Value),
            OptionSetValueCollection osvc => new JsonArray(
                osvc.Select(v => (JsonNode?)JsonValue.Create(v.Value)).ToArray()
            ),
            EntityReference er => SerializeEntityReference(er),
            AliasedValue av => av.Value is null ? null : SerializeValue(av.Value, ""),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            long l => JsonValue.Create(l),
            double d => JsonValue.Create(d),
            decimal dec => JsonValue.Create(dec),
            Money m => JsonValue.Create(m.Value),
            DateTime dt => JsonValue.Create(dt.ToString("O")),
            Guid g => JsonValue.Create(g.ToString()),
            string s => JsonValue.Create(s),
            _ => JsonValue.Create(value.ToString()),
        };
    }

    private static JsonNode? SerializeEntityReference(EntityReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        var node = new JsonObject
        {
            ["id"] = JsonValue.Create(reference.Id.ToString()),
            ["entityname"] = JsonValue.Create(reference.LogicalName),
        };

        if (!string.IsNullOrEmpty(reference.Name))
        {
            node["name"] = JsonValue.Create(reference.Name);
        }

        return node;
    }

    // -------------------------------------------------------------------------
    // Import
    // -------------------------------------------------------------------------

    public void Import(ServiceClient client, MasterDataImportOptions options)
    {
        var schemas = MasterDataSchemaParser.Parse(options.SchemaPath);

        foreach (var entitySchema in schemas)
        {
            var entityFolder = Path.Combine(options.DataFolder, entitySchema.Name);
            if (!Directory.Exists(entityFolder))
            {
                _logger.Info(
                    $"No data folder found for entity '{entitySchema.Name}', skipping."
                );
                continue;
            }

            var files = Directory.GetFiles(entityFolder, "*.json");
            _logger.Info($"Importing {files.Length} record(s) for {entitySchema.Name}");

            var created = 0;
            var updated = 0;

            foreach (var file in files)
            {
                var json = File.ReadAllText(file);
                var entity = DeserializeRecord(json, entitySchema);

                // Try alternate-key match first (non-primary fields with updateCompare=true)
                var altKeyField = entitySchema.Fields.FirstOrDefault(f =>
                    f.UpdateCompare && !f.IsPrimaryKey
                );

                if (
                    altKeyField is not null
                    && entity.Attributes.TryGetValue(altKeyField.Name, out var altKeyValue)
                    && altKeyValue is not null
                )
                {
                    var existing = FindByAlternateKey(
                        client,
                        entitySchema.Name,
                        altKeyField.Name,
                        altKeyValue
                    );
                    if (existing is not null)
                    {
                        // Update the existing record (preserve its environment GUID)
                        entity.Id = existing.Id;
                        client.Update(entity);
                        updated++;
                        continue;
                    }
                }

                // Upsert by GUID: creates with the stored GUID if absent, updates if present
                var upsertRequest = new UpsertRequest { Target = entity };
                var upsertResponse = (UpsertResponse)client.Execute(upsertRequest);

                if (upsertResponse.RecordCreated)
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }

            _logger.Info(
                $"  {entitySchema.Name}: {created} created, {updated} updated"
            );
        }
    }

    private static Entity DeserializeRecord(string json, MasterDataEntitySchema schema)
    {
        var obj =
            JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON record.");

        var entity = new Entity(schema.Name);

        foreach (var field in schema.Fields)
        {
            if (!obj.TryGetPropertyValue(field.Name, out var node) || node is null)
            {
                continue;
            }

            var value = DeserializeValue(node, field, schema);
            if (value is null)
            {
                continue;
            }

            if (field.IsPrimaryKey && value is Guid primaryGuid)
            {
                entity.Id = primaryGuid;
            }

            entity[field.Name] = value;
        }

        return entity;
    }

    private static object? DeserializeValue(
        JsonNode node,
        MasterDataFieldSchema field,
        MasterDataEntitySchema schema
    )
    {
        if (node is JsonValue jsonValue)
        {
            return field.Type switch
            {
                "guid" => jsonValue.TryGetValue<string>(out var gs) && Guid.TryParse(gs, out var g)
                    ? g
                    : null,
                "string" => jsonValue.TryGetValue<string>(out var s) ? s : null,
                "bool" => jsonValue.TryGetValue<bool>(out var b) ? b : null,
                "number" => jsonValue.TryGetValue<int>(out var i) ? i : null,
                "float" => jsonValue.TryGetValue<double>(out var d) ? (object)d : null,
                "optionsetvalue" => jsonValue.TryGetValue<int>(out var ov)
                    ? new OptionSetValue(ov)
                    : null,
                "datetime" => jsonValue.TryGetValue<string>(out var ds)
                    && DateTime.TryParse(ds, out var dt)
                    ? dt.ToUniversalTime()
                    : null,
                _ => jsonValue.TryGetValue<string>(out var fallback) ? fallback : null,
            };
        }

        if (node is JsonObject refObj && field.Type == "entityreference")
        {
            return DeserializeEntityReference(refObj, field);
        }

        return null;
    }

    private static EntityReference? DeserializeEntityReference(
        JsonObject obj,
        MasterDataFieldSchema field
    )
    {
        var idStr = obj["id"]?.GetValue<string>();
        if (idStr is null || !Guid.TryParse(idStr, out var id))
        {
            return null;
        }

        var entityName =
            obj["entityname"]?.GetValue<string>()
            ?? field.LookupType
            ?? string.Empty;

        return new EntityReference(entityName, id);
    }

    private static Entity? FindByAlternateKey(
        ServiceClient client,
        string entityName,
        string fieldName,
        object fieldValue
    )
    {
        var query = new QueryExpression(entityName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        query.Criteria.AddCondition(fieldName, ConditionOperator.Equal, fieldValue);

        var results = client.RetrieveMultiple(query);
        return results.Entities.Count > 0 ? results.Entities[0] : null;
    }

    // =========================================================================
    // JSON-schema Export
    // =========================================================================

    public void ExportJson(ServiceClient client, MasterDataExportOptions options)
    {
        var config = MasterDataJsonConfig.Parse(options.SchemaPath);

        foreach (var entityConfig in config.Entities)
        {
            _logger.Info($"Exporting entity: {entityConfig.EntityName}");
            var outputFolder = Path.Combine(options.DataFolder, entityConfig.FolderName);
            if (Directory.Exists(outputFolder))
                Directory.Delete(outputFolder, recursive: true);
            Directory.CreateDirectory(outputFolder);

            var (explicitIncludes, effectiveExcludes) = entityConfig.ResolveFields(
                config.DefaultExcludedFields
            );

            int recordCount;

            if (entityConfig.IsFetchXmlFilter)
            {
                var filterDoc = XDocument.Parse(entityConfig.Filter!);
                var filterElements = filterDoc
                    .Root?.Element("entity")
                    ?.Elements("filter")
                    .Select(f => new XElement(f))
                    .ToList() ?? new List<XElement>();
                recordCount = ExportJsonWithFetchXmlFilters(
                    client,
                    entityConfig.EntityName,
                    outputFolder,
                    explicitIncludes,
                    effectiveExcludes,
                    filterElements
                );
            }
            else if (entityConfig.HasFilter)
            {
                // Convert OData filter string to FetchXML
                var filterEl = ConvertODataToFetchXmlFilter(entityConfig.Filter!);
                recordCount = ExportJsonWithFetchXmlFilters(
                    client,
                    entityConfig.EntityName,
                    outputFolder,
                    explicitIncludes,
                    effectiveExcludes,
                    new List<XElement> { filterEl }
                );
            }
            else
            {
                // No filter — QueryExpression
                var query = new QueryExpression(entityConfig.EntityName)
                {
                    ColumnSet = explicitIncludes != null
                        ? new ColumnSet(explicitIncludes.ToArray())
                        : new ColumnSet(true),
                    PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
                };

                recordCount = 0;
                EntityCollection qResults;
                do
                {
                    qResults = client.RetrieveMultiple(query);
                    WriteJsonRecords(
                        qResults,
                        outputFolder,
                        explicitIncludes,
                        effectiveExcludes,
                        ref recordCount
                    );
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = qResults.PagingCookie;
                } while (qResults.MoreRecords);
            }

            _logger.Info($"  Exported {recordCount} record(s) for {entityConfig.EntityName}");
        }
    }

    private int ExportJsonWithFetchXmlFilters(
        ServiceClient client,
        string entityName,
        string outputFolder,
        HashSet<string>? explicitIncludes,
        HashSet<string> effectiveExcludes,
        List<XElement> filterElements
    )
    {
        var recordCount = 0;
        var page = 1;
        string? pagingCookie = null;

        while (true)
        {
            var fetchEl = new XElement(
                "fetch",
                new XAttribute("version", "1.0"),
                new XAttribute("mapping", "logical"),
                new XAttribute("distinct", "true"),
                new XAttribute("page", page),
                new XAttribute("count", "5000")
            );
            if (pagingCookie != null)
                fetchEl.Add(new XAttribute("paging-cookie", pagingCookie));

            var entityEl = new XElement("entity", new XAttribute("name", entityName));

            if (explicitIncludes != null)
                foreach (var f in explicitIncludes)
                    entityEl.Add(new XElement("attribute", new XAttribute("name", f)));
            else
                entityEl.Add(new XElement("all-attributes"));

            foreach (var filter in filterElements)
                entityEl.Add(new XElement(filter));

            fetchEl.Add(entityEl);

            var results = client.RetrieveMultiple(new FetchExpression(fetchEl.ToString()));
            WriteJsonRecords(results, outputFolder, explicitIncludes, effectiveExcludes, ref recordCount);

            if (!results.MoreRecords)
                break;
            pagingCookie = results.PagingCookie;
            page++;
        }

        return recordCount;
    }

    private void WriteJsonRecords(
        EntityCollection results,
        string outputFolder,
        HashSet<string>? includeFields,
        HashSet<string> excludeFields,
        ref int recordCount
    )
    {
        foreach (var record in results.Entities)
        {
            var obj = new JsonObject();
            foreach (var attr in record.Attributes)
            {
                var key = attr.Key;
                if (includeFields != null)
                {
                    if (!includeFields.Contains(key))
                        continue;
                }
                else
                {
                    if (excludeFields.Contains(key))
                        continue;
                }
                obj[key] = attr.Value is null ? null : SerializeValue(attr.Value, "");
            }

            var filePath = Path.Combine(outputFolder, $"{record.Id}.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(obj, JsonWriteOptions));
            recordCount++;
        }
    }

    // =========================================================================
    // JSON-schema Import
    // =========================================================================

    public void ImportJson(ServiceClient client, MasterDataImportOptions options)
    {
        var config = MasterDataJsonConfig.Parse(options.SchemaPath);

        // ── Phase 1: load entity metadata for every entity in the schema ──────
        _logger.Info("Loading entity metadata...");
        var metaCache = new Dictionary<string, (EntityMetadata Meta, Dictionary<string, AttributeMetadata> Attrs)>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var entityConfig in config.Entities)
        {
            if (entityConfig.IsNNRelationship)
                continue; // N:N intersect entities need no attribute metadata
            if (metaCache.ContainsKey(entityConfig.EntityName))
                continue;
            var metaReq = new RetrieveEntityRequest
            {
                LogicalName = entityConfig.EntityName,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = false,
            };
            var metaResp = (RetrieveEntityResponse)client.Execute(metaReq);
            var meta = metaResp.EntityMetadata;
            metaCache[entityConfig.EntityName] = (
                meta,
                meta.Attributes.ToDictionary(a => a.LogicalName, StringComparer.OrdinalIgnoreCase)
            );
        }

        // ── Phase 2: build source→target GUID map ─────────────────────────────
        // For GUID-keyed entities: identity mapping (source GUID == target GUID).
        // For alternate-keyed entities: batch-query target by key value, map
        // sourceGuid → targetGuid so references are rewritten during import.
        _logger.Info("Building GUID resolution map...");
        var guidMap = new Dictionary<string, Dictionary<Guid, Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityConfig in config.Entities)
        {
            if (entityConfig.IsNNRelationship)
                continue; // N:N intersect entities are not referenced by other entities
            if (!metaCache.TryGetValue(entityConfig.EntityName, out var metaEntry))
                continue;

            var guidField = metaEntry.Meta.PrimaryIdAttribute;
            var matchField = entityConfig.PrimaryKey ?? guidField;
            var isGuidKey = matchField.Equals(guidField, StringComparison.OrdinalIgnoreCase);

            var entityFolder = Path.Combine(options.DataFolder, entityConfig.FolderName);
            if (!Directory.Exists(entityFolder))
                continue;

            var files = Directory.GetFiles(entityFolder, "*.json");
            if (files.Length == 0)
                continue;

            var entityGuidMap = new Dictionary<Guid, Guid>();
            guidMap[entityConfig.EntityName] = entityGuidMap;

            if (isGuidKey)
            {
                // Identity mapping: source GUID is used as-is in the target environment.
                foreach (var file in files)
                {
                    var obj = ParseJsonFile(file);
                    if (obj is null)
                        continue;
                    var sourceGuid = GetGuidFromJson(obj, guidField);
                    if (sourceGuid != Guid.Empty)
                        entityGuidMap[sourceGuid] = sourceGuid;
                }
            }
            else
            {
                // Alternate-key matching: collect key values from source files, then
                // batch-query the target environment to resolve source→target GUIDs.
                var sourcePairs = new List<(Guid sourceGuid, string keyValue)>();
                foreach (var file in files)
                {
                    var obj = ParseJsonFile(file);
                    if (obj is null)
                        continue;
                    var sourceGuid = GetGuidFromJson(obj, guidField);
                    if (sourceGuid == Guid.Empty)
                        continue;
                    var keyValue = obj[matchField]?.ToString();
                    if (!string.IsNullOrEmpty(keyValue))
                        sourcePairs.Add((sourceGuid, keyValue));
                }

                // Batch-query target in groups of 500
                var allKeyValues = sourcePairs.Select(p => (object)p.keyValue).ToArray();
                var keyToTargetGuid = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < allKeyValues.Length; i += 500)
                {
                    var batch = allKeyValues.Skip(i).Take(500).ToArray();
                    var q = new QueryExpression(entityConfig.EntityName)
                    {
                        ColumnSet = new ColumnSet(guidField, matchField),
                    };
                    var inCondition = new ConditionExpression(matchField, ConditionOperator.In);
                    inCondition.Values.AddRange(batch);
                    q.Criteria.Conditions.Add(inCondition);

                    var qResult = client.RetrieveMultiple(q);
                    foreach (var record in qResult.Entities)
                    {
                        if (record.Attributes.TryGetValue(matchField, out var mv) && mv is not null)
                            keyToTargetGuid[mv.ToString()!] = record.Id;
                    }
                }

                foreach (var (sourceGuid, keyValue) in sourcePairs)
                {
                    if (keyToTargetGuid.TryGetValue(keyValue, out var targetGuid))
                        entityGuidMap[sourceGuid] = targetGuid;
                    // Not found → record will be created; no mapping entry needed.
                }
            }
        }

        // ── Phase 3: import each entity ───────────────────────────────────────
        if (options.DryRun)
            _logger.Info("[DRY RUN] No records will be written to Dataverse.");

        foreach (var entityConfig in config.Entities)
        {
            if (entityConfig.ReferenceOnly)
            {
                _logger.Info($"Skipping '{entityConfig.EntityName}' (referenceOnly).");
                continue;
            }

            if (entityConfig.IsNNRelationship)
            {
                ImportNNRelationship(client, entityConfig, guidMap, options);
                continue;
            }

            var entityFolder = Path.Combine(options.DataFolder, entityConfig.FolderName);
            if (!Directory.Exists(entityFolder))
            {
                _logger.Info($"No data folder found for '{entityConfig.FolderName}', skipping.");
                continue;
            }

            var files = Directory.GetFiles(entityFolder, "*.json");
            _logger.Info($"Importing {files.Length} record(s) for {entityConfig.EntityName}");

            if (!metaCache.TryGetValue(entityConfig.EntityName, out var metaEntry))
                continue;

            var (entityMeta, attrMeta) = metaEntry;
            var guidField = entityMeta.PrimaryIdAttribute;
            var matchField = entityConfig.PrimaryKey ?? guidField;
            var isGuidKey = matchField.Equals(guidField, StringComparison.OrdinalIgnoreCase);

            guidMap.TryGetValue(entityConfig.EntityName, out var entityGuidMap);

            // Pre-fetch existing records so we can detect changes without extra round-trips.
            var targetGuids = entityGuidMap?.Values.Distinct().ToList() ?? new List<Guid>();
            var (explicitIncludes, _) = entityConfig.ResolveFields(config.DefaultExcludedFields);
            var existingRecords = FetchExistingByGuids(
                client, entityConfig.EntityName, guidField, targetGuids, explicitIncludes
            );

            var created = 0;
            var updated = 0;
            var skipped = 0;

            // Parse all files upfront so we can sort them before importing.
            var records = files
                .Select(f => (File: f, Obj: ParseJsonFile(f)))
                .Where(r => r.Obj is not null)
                .Select(r => (r.File, Obj: r.Obj!))
                .ToList();

            // If the entity references itself (e.g. parent/child hierarchy),
            // sort records topologically so parents are imported before children.
            var selfRefField = FindSelfReferentialField(records, entityConfig.EntityName);
            if (selfRefField is not null)
            {
                _logger.Info($"  Detected self-referential field '{selfRefField}', sorting topologically.");
                records = SortTopologically(records, guidField, selfRefField);
            }

            foreach (var (_, obj) in records)
            {

                var sourceGuid = GetGuidFromJson(obj, guidField);

                // Resolve source GUID to its counterpart in the target environment.
                Guid targetGuid;
                if (entityGuidMap is not null && entityGuidMap.TryGetValue(sourceGuid, out var mappedGuid))
                    targetGuid = mappedGuid;
                else if (isGuidKey)
                    targetGuid = sourceGuid;
                else
                    targetGuid = Guid.Empty; // record not found → will be created

                // Deserialize, resolving all entity references through the GUID map.
                var entity = DeserializeJsonRecord(obj, entityConfig.EntityName, guidField, attrMeta, guidMap);

                if (targetGuid != Guid.Empty)
                {
                    entity.Id = targetGuid;
                    // Keep the attribute bag in sync — Dataverse throws if entity.Id
                    // and entity[primaryIdAttr] disagree.
                    entity[guidField] = targetGuid;
                }

                // Change detection: skip the upsert when nothing actually changed.
                if (targetGuid != Guid.Empty && existingRecords.TryGetValue(targetGuid, out var existing))
                {
                    var changedFields = CollectChanges(entity, existing);
                    if (changedFields.Count == 0)
                    {
                        skipped++;
                        continue;
                    }
                    if (options.LogChanges)
                    {
                        _logger.Info($"    [CHANGES] {entityConfig.EntityName} ({targetGuid}):");
                        foreach (var f in changedFields)
                            _logger.Info($"      {f}");
                    }
                    updated++;
                }
                else
                {
                    created++;
                }

                if (!options.DryRun)
                    client.Execute(new UpsertRequest { Target = entity });
            }

            _logger.Info(
                $"  {entityConfig.EntityName}: {created} created, {updated} updated, {skipped} skipped (no changes)"
            );
        }
    }

    private static Entity DeserializeJsonRecord(
        JsonObject obj,
        string entityName,
        string primaryIdAttr,
        Dictionary<string, AttributeMetadata> attrMeta,
        Dictionary<string, Dictionary<Guid, Guid>> guidMap
    )
    {
        var entity = new Entity(entityName);

        foreach (var (key, node) in obj)
        {
            if (node is null)
                continue;
            if (!attrMeta.TryGetValue(key, out var meta))
                continue;
            if (meta.IsValidForCreate == false && meta.IsValidForUpdate == false)
                continue;

            var value = MapJsonValueToSdkType(node, meta);
            if (value is null)
                continue;

            // Rewrite EntityReference GUIDs to their target-environment counterparts.
            if (
                value is EntityReference er
                && er.Id != Guid.Empty
                && guidMap.TryGetValue(er.LogicalName, out var refGuidMap)
                && refGuidMap.TryGetValue(er.Id, out var targetRefGuid)
            )
            {
                value = new EntityReference(er.LogicalName, targetRefGuid) { Name = er.Name };
            }

            if (
                key.Equals(primaryIdAttr, StringComparison.OrdinalIgnoreCase)
                && value is Guid primaryGuid
            )
                entity.Id = primaryGuid;

            entity[key] = value;
        }

        return entity;
    }

    private static object? MapJsonValueToSdkType(JsonNode node, AttributeMetadata meta)
    {
        // EntityReference (lookup / owner / customer)
        if (node is JsonObject refObj)
        {
            var idStr = refObj["id"]?.GetValue<string>();
            if (idStr is null || !Guid.TryParse(idStr, out var refId))
                return null;
            var refEntityName =
                refObj["entityname"]?.GetValue<string>()
                ?? (meta is LookupAttributeMetadata lam ? lam.Targets?.FirstOrDefault() : null)
                ?? string.Empty;
            return new EntityReference(refEntityName, refId);
        }

        // Multi-select option set
        if (node is JsonArray arr)
        {
            var values = arr
                .Select(n => n is JsonValue v && v.TryGetValue<int>(out var i)
                    ? new OptionSetValue(i)
                    : null)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList();
            return new OptionSetValueCollection(values);
        }

        if (node is not JsonValue jsonVal)
            return null;

        return meta.AttributeType switch
        {
            AttributeTypeCode.Boolean => jsonVal.TryGetValue<bool>(out var b) ? b : null,
            AttributeTypeCode.Integer => jsonVal.TryGetValue<int>(out var i) ? (object?)i : null,
            AttributeTypeCode.BigInt => jsonVal.TryGetValue<long>(out var l) ? l : null,
            AttributeTypeCode.Double => jsonVal.TryGetValue<double>(out var d) ? d : null,
            AttributeTypeCode.Decimal => jsonVal.TryGetValue<decimal>(out var dec) ? dec : null,
            AttributeTypeCode.Money => jsonVal.TryGetValue<decimal>(out var mv)
                ? new Money(mv)
                : null,
            AttributeTypeCode.Picklist
            or AttributeTypeCode.State
            or AttributeTypeCode.Status => jsonVal.TryGetValue<int>(out var ov)
                ? new OptionSetValue(ov)
                : null,
            AttributeTypeCode.Uniqueidentifier => jsonVal.TryGetValue<string>(out var gs)
                && Guid.TryParse(gs, out var g)
                ? g
                : null,
            AttributeTypeCode.DateTime => jsonVal.TryGetValue<string>(out var ds)
                && DateTime.TryParse(ds, out var dt)
                ? dt.ToUniversalTime()
                : null,
            _ => jsonVal.TryGetValue<string>(out var s) ? s : null,
        };
    }

    // =========================================================================
    // Import helpers
    // =========================================================================

    /// <summary>
    /// Imports N:N relationship records using <c>AssociateRequest</c>.
    /// Checks whether the relationship already exists before associating.
    /// </summary>
    private void ImportNNRelationship(
        ServiceClient client,
        MasterDataJsonEntityConfig entityConfig,
        Dictionary<string, Dictionary<Guid, Guid>> guidMap,
        MasterDataImportOptions options
    )
    {
        if (entityConfig.Entity1 is null || entityConfig.Entity2 is null)
        {
            _logger.Info(
                $"  Skipping N:N '{entityConfig.EntityName}': entity1 and entity2 must be configured in the schema."
            );
            return;
        }

        var entityFolder = Path.Combine(options.DataFolder, entityConfig.FolderName);
        if (!Directory.Exists(entityFolder))
        {
            _logger.Info($"No data folder found for '{entityConfig.FolderName}', skipping.");
            return;
        }

        var files = Directory.GetFiles(entityFolder, "*.json");
        _logger.Info($"Importing {files.Length} N:N relationship(s) for {entityConfig.EntityName}");

        var relationshipSchemaName = ResolveNNRelationshipSchemaName(client, entityConfig);

        // ── Pre-fetch all existing intersect rows in one paged query ──────────
        // Build a HashSet<(Guid, Guid)> so existence checks are O(1) in-memory.
        var field1 = entityConfig.Entity1.FieldName;
        var field2 = entityConfig.Entity2.FieldName;

        var existingPairs = new HashSet<(Guid, Guid)>();
        var existingQuery = new QueryExpression(entityConfig.EntityName)
        {
            ColumnSet = new ColumnSet(field1, field2),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
        };
        EntityCollection page;
        do
        {
            page = client.RetrieveMultiple(existingQuery);
            foreach (var record in page.Entities)
            {
                var g1 = ReadGuidValue(record, field1);
                var g2 = ReadGuidValue(record, field2);
                if (g1 != Guid.Empty && g2 != Guid.Empty)
                    existingPairs.Add((g1, g2));
            }
            existingQuery.PageInfo.PageNumber++;
            existingQuery.PageInfo.PagingCookie = page.PagingCookie;
        }
        while (page.MoreRecords);

        // ── Process source files ──────────────────────────────────────────────
        var associated = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            var obj = ParseJsonFile(file);
            if (obj is null)
                continue;

            var sourceGuid1 = GetGuidFromJson(obj, field1);
            var sourceGuid2 = GetGuidFromJson(obj, field2);

            if (sourceGuid1 == Guid.Empty || sourceGuid2 == Guid.Empty)
            {
                _logger.Info($"  Skipping record in '{entityConfig.FolderName}': could not read FK GUIDs.");
                skipped++;
                continue;
            }

            // Resolve GUIDs to their target-environment counterparts via the global GUID map.
            // Falls back to the source GUID when the parent entity is not in the map
            // (e.g. it uses the same GUIDs across environments).
            var targetGuid1 = guidMap.TryGetValue(entityConfig.Entity1.EntityName, out var map1) && map1.TryGetValue(sourceGuid1, out var resolved1)
                ? resolved1
                : sourceGuid1;
            var targetGuid2 = guidMap.TryGetValue(entityConfig.Entity2.EntityName, out var map2) && map2.TryGetValue(sourceGuid2, out var resolved2)
                ? resolved2
                : sourceGuid2;

            if (existingPairs.Contains((targetGuid1, targetGuid2)))
            {
                skipped++;
                continue;
            }

            if (!options.DryRun)
            {
                client.Execute(new AssociateRequest
                {
                    Target = new EntityReference(entityConfig.Entity1.EntityName, targetGuid1),
                    RelatedEntities = new EntityReferenceCollection
                    {
                        new EntityReference(entityConfig.Entity2.EntityName, targetGuid2),
                    },
                    Relationship = new Relationship(relationshipSchemaName),
                });

                // Keep the cache up to date so duplicates within the same run are caught.
                existingPairs.Add((targetGuid1, targetGuid2));
            }

            associated++;
        }

        _logger.Info(
            $"  {entityConfig.EntityName}: {associated} associated, {skipped} skipped (already exist)"
        );
    }

    private string ResolveNNRelationshipSchemaName(
        ServiceClient client,
        MasterDataJsonEntityConfig entityConfig
    )
    {
        if (entityConfig.Entity1 is null || entityConfig.Entity2 is null)
            return entityConfig.EntityName;

        var endpoint1 = entityConfig.Entity1.EntityName;
        var endpoint2 = entityConfig.Entity2.EntityName;

        foreach (var logicalName in new[] { endpoint1, endpoint2 }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var req = new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Relationships,
                RetrieveAsIfPublished = false,
            };

            var resp = (RetrieveEntityResponse)client.Execute(req);
            var rel = resp
                .EntityMetadata
                .ManyToManyRelationships?.FirstOrDefault(r =>
                    string.Equals(r.IntersectEntityName, entityConfig.EntityName, StringComparison.OrdinalIgnoreCase)
                    && (
                        (
                            string.Equals(r.Entity1LogicalName, endpoint1, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.Entity2LogicalName, endpoint2, StringComparison.OrdinalIgnoreCase)
                        )
                        || (
                            string.Equals(r.Entity1LogicalName, endpoint2, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(r.Entity2LogicalName, endpoint1, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                );

            if (!string.IsNullOrWhiteSpace(rel?.SchemaName))
                return rel.SchemaName;
        }

        _logger.Info(
            $"  Could not resolve relationship schema for intersect '{entityConfig.EntityName}', using intersect name as fallback."
        );
        return entityConfig.EntityName;
    }

    private static Guid ReadGuidValue(Entity record, string fieldName)
    {
        if (!record.Attributes.TryGetValue(fieldName, out var value) || value is null)
            return Guid.Empty;

        return value switch
        {
            Guid guid => guid,
            EntityReference er => er.Id,
            _ => Guid.Empty,
        };
    }

    /// <summary>
    /// Returns the first field name in any record that is a lookup back to the same entity,
    /// indicating a self-referential hierarchy. Returns <c>null</c> when none found.
    /// </summary>
    private static string? FindSelfReferentialField(
        List<(string File, JsonObject Obj)> records,
        string entityName
    )
    {
        foreach (var (_, obj) in records)
        {
            foreach (var (key, node) in obj)
            {
                if (
                    node is JsonObject refObj
                    && refObj["entityname"]?.GetValue<string>() is string en
                    && en.Equals(entityName, StringComparison.OrdinalIgnoreCase)
                )
                    return key;
            }
        }
        return null;
    }

    /// <summary>
    /// Topologically sorts records so that parents always precede their children.
    /// Uses Kahn's algorithm. Any cycles or orphan references are appended at the end.
    /// </summary>
    private static List<(string File, JsonObject Obj)> SortTopologically(
        List<(string File, JsonObject Obj)> records,
        string guidField,
        string selfRefField
    )
    {
        var count = records.Count;

        // Map source GUID → index in records list.
        var guidToIndex = new Dictionary<Guid, int>();
        for (var i = 0; i < count; i++)
        {
            var g = GetGuidFromJson(records[i].Obj, guidField);
            if (g != Guid.Empty)
                guidToIndex[g] = i;
        }

        var inDegree = new int[count];
        var children = Enumerable.Range(0, count).Select(_ => new List<int>()).ToArray();

        for (var i = 0; i < count; i++)
        {
            var refNode = records[i].Obj[selfRefField];
            if (
                refNode is JsonObject refObj
                && refObj["id"]?.GetValue<string>() is string parentIdStr
                && Guid.TryParse(parentIdStr, out var parentGuid)
                && guidToIndex.TryGetValue(parentGuid, out var parentIndex)
                && parentIndex != i // guard against pointing to itself
            )
            {
                children[parentIndex].Add(i);
                inDegree[i]++;
            }
        }

        // Kahn's BFS topological sort.
        var queue = new Queue<int>();
        for (var i = 0; i < count; i++)
            if (inDegree[i] == 0)
                queue.Enqueue(i);

        var sorted = new List<(string File, JsonObject Obj)>(count);
        while (queue.Count > 0)
        {
            var idx = queue.Dequeue();
            sorted.Add(records[idx]);
            foreach (var child in children[idx])
                if (--inDegree[child] == 0)
                    queue.Enqueue(child);
        }

        // Append any remaining records (cycle members) so they are not lost.
        for (var i = 0; i < count; i++)
            if (inDegree[i] > 0)
                sorted.Add(records[i]);

        return sorted;
    }

    private static JsonObject? ParseJsonFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        return JsonNode.Parse(text)?.AsObject();
    }

    private static Guid GetGuidFromJson(JsonObject obj, string fieldName)
    {
        var node = obj[fieldName];
        if (node is JsonValue jv && jv.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g))
            return g;
        return Guid.Empty;
    }

    /// <summary>
    /// Batch-fetches existing records by a list of target GUIDs.
    /// Queries are sent in groups of 500 to stay within Dataverse IN-clause limits.
    /// </summary>
    private static Dictionary<Guid, Entity> FetchExistingByGuids(
        ServiceClient client,
        string entityName,
        string guidField,
        List<Guid> targetGuids,
        HashSet<string>? explicitFields
    )
    {
        var result = new Dictionary<Guid, Entity>();
        if (targetGuids.Count == 0)
            return result;

        const int batchSize = 500;
        for (var i = 0; i < targetGuids.Count; i += batchSize)
        {
            var batch = targetGuids.Skip(i).Take(batchSize).Cast<object>().ToArray();

            var query = new QueryExpression(entityName)
            {
                ColumnSet = explicitFields is not null
                    ? new ColumnSet(explicitFields.ToArray())
                    : new ColumnSet(true),
            };

            var inCondition = new ConditionExpression(guidField, ConditionOperator.In);
            inCondition.Values.AddRange(batch);
            query.Criteria.Conditions.Add(inCondition);

            var qResult = client.RetrieveMultiple(query);
            foreach (var record in qResult.Entities)
                result[record.Id] = record;
        }

        return result;
    }

    /// <summary>
    /// Returns the names of fields in <paramref name="incoming"/> that differ from
    /// <paramref name="existing"/>. An empty list means no changes.
    /// </summary>
    private static List<string> CollectChanges(Entity incoming, Entity existing)
    {
        var changed = new List<string>();
        foreach (var (key, incomingValue) in incoming.Attributes)
        {
            var existingValue = existing.Contains(key) ? existing[key] : null;
            if (!SdkValuesEqual(incomingValue, existingValue))
                changed.Add(key);
        }
        return changed;
    }

    private static bool SdkValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        return (a, b) switch
        {
            (OptionSetValue oa, OptionSetValue ob) =>
                oa.Value == ob.Value,
            (EntityReference ea, EntityReference eb) =>
                ea.Id == eb.Id &&
                string.Equals(ea.LogicalName, eb.LogicalName, StringComparison.OrdinalIgnoreCase),
            (Money ma, Money mb) =>
                ma.Value == mb.Value,
            (OptionSetValueCollection ca, OptionSetValueCollection cb) =>
                ca.Count == cb.Count &&
                !ca.Select(v => v.Value).Except(cb.Select(v => v.Value)).Any(),
            _ => a.Equals(b),
        };
    }

    // =========================================================================
    // OData filter → FetchXML conversion
    // =========================================================================

    private static XElement ConvertODataToFetchXmlFilter(string odataFilter)
    {
        var filter = odataFilter.Trim();

        // Top-level "and" split (evaluated before "or" — no parentheses nesting supported)
        var andParts = SplitODataLogical(filter, "and");
        if (andParts.Count > 1)
        {
            var el = new XElement("filter", new XAttribute("type", "and"));
            foreach (var p in andParts)
                el.Add(ParseSingleODataCondition(p.Trim()));
            return el;
        }

        var orParts = SplitODataLogical(filter, "or");
        if (orParts.Count > 1)
        {
            var el = new XElement("filter", new XAttribute("type", "or"));
            foreach (var p in orParts)
                el.Add(ParseSingleODataCondition(p.Trim()));
            return el;
        }

        var single = new XElement("filter", new XAttribute("type", "and"));
        single.Add(ParseSingleODataCondition(filter));
        return single;
    }

    private static List<string> SplitODataLogical(string filter, string op)
    {
        // Simple word-boundary split — does not handle parentheses or string literals
        // containing the logical operator word.
        var parts = Regex.Split(filter, $@"\s+{Regex.Escape(op)}\s+", RegexOptions.IgnoreCase);
        return parts.ToList();
    }

    private static XElement ParseSingleODataCondition(string condition)
    {
        // contains(field, 'value')
        var m = Regex.Match(
            condition,
            @"^contains\s*\(\s*(\w+)\s*,\s*'([^']*)'\s*\)$",
            RegexOptions.IgnoreCase
        );
        if (m.Success)
            return new XElement(
                "condition",
                new XAttribute("attribute", m.Groups[1].Value),
                new XAttribute("operator", "like"),
                new XAttribute("value", $"%{m.Groups[2].Value}%")
            );

        // startswith(field, 'value')
        m = Regex.Match(
            condition,
            @"^startswith\s*\(\s*(\w+)\s*,\s*'([^']*)'\s*\)$",
            RegexOptions.IgnoreCase
        );
        if (m.Success)
            return new XElement(
                "condition",
                new XAttribute("attribute", m.Groups[1].Value),
                new XAttribute("operator", "begins-with"),
                new XAttribute("value", m.Groups[2].Value)
            );

        // endswith(field, 'value')
        m = Regex.Match(
            condition,
            @"^endswith\s*\(\s*(\w+)\s*,\s*'([^']*)'\s*\)$",
            RegexOptions.IgnoreCase
        );
        if (m.Success)
            return new XElement(
                "condition",
                new XAttribute("attribute", m.Groups[1].Value),
                new XAttribute("operator", "ends-with"),
                new XAttribute("value", m.Groups[2].Value)
            );

        // field op value   (eq/ne/gt/ge/lt/le)
        m = Regex.Match(
            condition,
            @"^(\w+)\s+(eq|ne|gt|ge|lt|le)\s+(.+)$",
            RegexOptions.IgnoreCase
        );
        if (!m.Success)
            throw new InvalidArgumentException(
                $"Unrecognized OData filter condition: '{condition}'. "
                    + "Use FetchXML format for complex filters (start with '<fetch ...')."
            );

        var field = m.Groups[1].Value;
        var op = m.Groups[2].Value.ToLowerInvariant();
        var raw = m.Groups[3].Value.Trim();

        // null checks
        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase))
            return new XElement(
                "condition",
                new XAttribute("attribute", field),
                new XAttribute("operator", op == "eq" ? "null" : "not-null")
            );

        // Strip single quotes from string literals
        var value = raw.StartsWith('\'') && raw.EndsWith('\'') ? raw[1..^1] : raw;

        return new XElement(
            "condition",
            new XAttribute("attribute", field),
            new XAttribute("operator", op),
            new XAttribute("value", value)
        );
    }
}
