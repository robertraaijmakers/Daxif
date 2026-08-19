namespace XrmPackager.Core.Crm;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
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
            WriteRecords(client, results, entitySchema, outputFolder, ref recordCount);
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
        var filterDoc = XDocument.Parse(entitySchema.FetchXmlFilter!);
        var filterElements = filterDoc.Root
            ?.Element("entity")
            ?.Elements("filter")
            .Select(f => new XElement(f))
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
            WriteRecords(client, results, entitySchema, outputFolder, ref recordCount);

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
        ServiceClient client,
        EntityCollection results,
        MasterDataEntitySchema entitySchema,
        string outputFolder,
        ref int recordCount
    )
    {
        var fileFields = entitySchema.Fields.Where(f => f.Type == "file").ToList();

        foreach (var record in results.Entities)
        {
            var obj = new JsonObject();

            foreach (var field in entitySchema.Fields)
            {
                record.Attributes.TryGetValue(field.Name, out var rawValue);

                if (field.Type == "file")
                {
                    obj[field.Name] = rawValue is not null
                        ? DownloadAndSaveFile(client, record, field.Name, outputFolder)
                        : null;
                    continue;
                }

                obj[field.Name] = rawValue is null ? null : SerializeValue(rawValue, field.Type);
            }

            var filePath = Path.Combine(outputFolder, $"{record.Id}.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(obj, JsonWriteOptions));
            recordCount++;
        }
    }

    private JsonNode? DownloadAndSaveFile(
        ServiceClient client,
        Entity record,
        string fieldName,
        string outputFolder
    )
    {
        try
        {
            var initResp = (InitializeFileBlocksDownloadResponse)client.Execute(
                new InitializeFileBlocksDownloadRequest
                {
                    Target = record.ToEntityReference(),
                    FileAttributeName = fieldName,
                }
            );

            var token = initResp.FileContinuationToken;
            var fileSize = initResp.FileSizeInBytes;
            var fileName = initResp.FileName;

            var allBytes = new List<byte>((int)Math.Min(fileSize, int.MaxValue));
            const long chunkSize = 4 * 1024 * 1024;
            long offset = 0;
            while (offset < fileSize)
            {
                var blockResp = (DownloadBlockResponse)client.Execute(
                    new DownloadBlockRequest
                    {
                        FileContinuationToken = token,
                        Offset = offset,
                        BlockLength = Math.Min(chunkSize, fileSize - offset),
                    }
                );
                allBytes.AddRange(blockResp.Data);
                offset += blockResp.Data.LongLength;
            }

            var relativeDir = Path.Combine("_files", record.Id.ToString(), fieldName);
            var absoluteDir = Path.Combine(outputFolder, relativeDir);
            Directory.CreateDirectory(absoluteDir);
            File.WriteAllBytes(Path.Combine(absoluteDir, fileName), allBytes.ToArray());

            var relativePath = (relativeDir + Path.DirectorySeparatorChar + fileName)
                .Replace(Path.DirectorySeparatorChar, '/');

            return new JsonObject
            {
                ["__type"] = "file",
                ["fileName"] = fileName,
                ["path"] = relativePath,
            };
        }
        catch (Exception ex)
        {
            _logger.Info(
                $"  Warning: Could not download file for field '{fieldName}' on record '{record.Id}': {ex.Message}"
            );
            return null;
        }
    }

    private static JsonNode? SerializeValue(object value, string fieldType)
    {
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
                var (entity, filesToUpload) = DeserializeRecord(json, entitySchema);

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
                        entity.Id = existing.Id;
                        client.Update(entity);
                        UploadFiles(client, entitySchema.Name, entity.Id, filesToUpload, entityFolder);
                        updated++;
                        continue;
                    }
                }

                var upsertRequest = new UpsertRequest { Target = entity };
                var upsertResponse = (UpsertResponse)client.Execute(upsertRequest);
                UploadFiles(client, entitySchema.Name, entity.Id, filesToUpload, entityFolder);

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

    private void UploadFiles(
        ServiceClient client,
        string entityName,
        Guid recordId,
        List<(string FieldName, string RelativePath, string FileName)> files,
        string baseFolder
    )
    {
        foreach (var (fieldName, relativePath, fileName) in files)
        {
            var absolutePath = Path.Combine(
                baseFolder,
                relativePath.Replace('/', Path.DirectorySeparatorChar)
            );
            if (!File.Exists(absolutePath))
            {
                _logger.Info($"  Warning: File not found for field '{fieldName}': {absolutePath}");
                continue;
            }

            try
            {
                UploadFileContent(client, entityName, recordId, fieldName, fileName, File.ReadAllBytes(absolutePath));
            }
            catch (Exception ex)
            {
                _logger.Info(
                    $"  Warning: Could not upload file for field '{fieldName}' on record '{recordId}': {ex.Message}"
                );
            }
        }
    }

    private void UploadFileContent(
        ServiceClient client,
        string entityName,
        Guid recordId,
        string fieldName,
        string fileName,
        byte[] fileContent
    )
    {
        if (fileContent.Length == 0)
        {
            _logger.Info($"  Warning: Skipping upload of empty file '{fileName}' for field '{fieldName}'.");
            return;
        }

        var initResp = (InitializeFileBlocksUploadResponse)client.Execute(
            new InitializeFileBlocksUploadRequest
            {
                Target = new EntityReference(entityName, recordId),
                FileAttributeName = fieldName,
                FileName = fileName,
            }
        );

        var token = initResp.FileContinuationToken;
        const int chunkSize = 4 * 1024 * 1024;
        var blockIds = new List<string>();

        for (int offset = 0, blockIndex = 0; offset < fileContent.Length; offset += chunkSize, blockIndex++)
        {
            var blockSize = Math.Min(chunkSize, fileContent.Length - offset);
            var block = new byte[blockSize];
            Array.Copy(fileContent, offset, block, 0, blockSize);

            // Block IDs must be unique, consistent-length base64 strings.
            var blockId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(blockIndex.ToString("D8")));
            blockIds.Add(blockId);

            client.Execute(new UploadBlockRequest
            {
                FileContinuationToken = token,
                BlockId = blockId,
                BlockData = block,
            });
        }

        client.Execute(new CommitFileBlocksUploadRequest
        {
            FileContinuationToken = token,
            FileName = fileName,
            MimeType = DetermineMimeType(fileName),
            BlockList = blockIds.ToArray(),
        });
    }

    private static string DetermineMimeType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };

    private static (Entity entity, List<(string FieldName, string RelativePath, string FileName)> FilesToUpload)
    DeserializeRecord(string json, MasterDataEntitySchema schema)
    {
        var obj =
            JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("Invalid JSON record.");

        var entity = new Entity(schema.Name);
        var filesToUpload = new List<(string, string, string)>();

        foreach (var field in schema.Fields)
        {
            if (!obj.TryGetPropertyValue(field.Name, out var node) || node is null)
            {
                continue;
            }

            if (field.Type == "file")
            {
                if (node is JsonObject fileObj)
                {
                    var path = fileObj["path"]?.GetValue<string>();
                    var fileName = fileObj["fileName"]?.GetValue<string>();
                    if (path is not null && fileName is not null)
                        filesToUpload.Add((field.Name, path, fileName));
                }
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

        return (entity, filesToUpload);
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

            // Detect file attributes via metadata so we can handle them specially.
            var fileFieldNames = LoadFileAttributeNames(client, entityConfig.EntityName);

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
                    filterElements,
                    fileFieldNames
                );
            }
            else if (entityConfig.HasFilter)
            {
                var filterEl = ConvertODataToFetchXmlFilter(entityConfig.Filter!);
                recordCount = ExportJsonWithFetchXmlFilters(
                    client,
                    entityConfig.EntityName,
                    outputFolder,
                    explicitIncludes,
                    effectiveExcludes,
                    new List<XElement> { filterEl },
                    fileFieldNames
                );
            }
            else
            {
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
                        client,
                        qResults,
                        outputFolder,
                        explicitIncludes,
                        effectiveExcludes,
                        fileFieldNames,
                        ref recordCount
                    );
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = qResults.PagingCookie;
                } while (qResults.MoreRecords);
            }

            _logger.Info($"  Exported {recordCount} record(s) for {entityConfig.EntityName}");
        }
    }

    private static HashSet<string> LoadFileAttributeNames(ServiceClient client, string entityName)
    {
        try
        {
            var metaReq = new RetrieveEntityRequest
            {
                LogicalName = entityName,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = false,
            };
            var metaResp = (RetrieveEntityResponse)client.Execute(metaReq);
            return metaResp.EntityMetadata.Attributes
                .Where(a => a is FileAttributeMetadata)
                .Select(a => a.LogicalName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private int ExportJsonWithFetchXmlFilters(
        ServiceClient client,
        string entityName,
        string outputFolder,
        HashSet<string>? explicitIncludes,
        HashSet<string> effectiveExcludes,
        List<XElement> filterElements,
        HashSet<string> fileFieldNames
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
            WriteJsonRecords(client, results, outputFolder, explicitIncludes, effectiveExcludes, fileFieldNames, ref recordCount);

            if (!results.MoreRecords)
                break;
            pagingCookie = results.PagingCookie;
            page++;
        }

        return recordCount;
    }

    private void WriteJsonRecords(
        ServiceClient client,
        EntityCollection results,
        string outputFolder,
        HashSet<string>? includeFields,
        HashSet<string> excludeFields,
        HashSet<string> fileFieldNames,
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

                if (fileFieldNames.Contains(key) && attr.Value is not null)
                {
                    obj[key] = DownloadAndSaveFile(client, record, key, outputFolder);
                }
                else
                {
                    obj[key] = attr.Value is null ? null : SerializeValue(attr.Value, "");
                }
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
                continue;
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
        _logger.Info("Building GUID resolution map...");
        var guidMap = new Dictionary<string, Dictionary<Guid, Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityConfig in config.Entities)
        {
            if (entityConfig.IsNNRelationship)
                continue;
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

            var targetGuids = entityGuidMap?.Values.Distinct().ToList() ?? new List<Guid>();
            var (explicitIncludes, _) = entityConfig.ResolveFields(config.DefaultExcludedFields);
            var existingRecords = FetchExistingByGuids(
                client, entityConfig.EntityName, guidField, targetGuids, explicitIncludes
            );

            var created = 0;
            var updated = 0;
            var skipped = 0;

            var records = files
                .Select(f => (File: f, Obj: ParseJsonFile(f)))
                .Where(r => r.Obj is not null)
                .Select(r => (r.File, Obj: r.Obj!))
                .ToList();

            var selfRefField = FindSelfReferentialField(records, entityConfig.EntityName);
            if (selfRefField is not null)
            {
                _logger.Info($"  Detected self-referential field '{selfRefField}', sorting topologically.");
                records = SortTopologically(records, guidField, selfRefField);
            }

            foreach (var (_, obj) in records)
            {
                var sourceGuid = GetGuidFromJson(obj, guidField);

                Guid targetGuid;
                if (entityGuidMap is not null && entityGuidMap.TryGetValue(sourceGuid, out var mappedGuid))
                    targetGuid = mappedGuid;
                else if (isGuidKey)
                    targetGuid = sourceGuid;
                else
                    targetGuid = Guid.Empty;

                var (entity, filesToUpload) = DeserializeJsonRecord(
                    obj, entityConfig.EntityName, guidField, attrMeta, guidMap
                );

                if (targetGuid != Guid.Empty)
                {
                    entity.Id = targetGuid;
                    entity[guidField] = targetGuid;
                }

                if (targetGuid != Guid.Empty && existingRecords.TryGetValue(targetGuid, out var existing))
                {
                    var changedFields = CollectChanges(entity, existing);
                    if (changedFields.Count == 0 && filesToUpload.Count == 0)
                    {
                        skipped++;
                        continue;
                    }
                    if (options.LogChanges)
                    {
                        _logger.Info($"    [CHANGES] {entityConfig.EntityName} ({targetGuid}):");
                        foreach (var f in changedFields)
                            _logger.Info($"      {f}");
                        if (filesToUpload.Count > 0)
                            _logger.Info($"      (+ {filesToUpload.Count} file field(s))");
                    }
                    updated++;
                }
                else
                {
                    created++;
                }

                if (!options.DryRun)
                {
                    client.Execute(new UpsertRequest { Target = entity });
                    UploadFiles(client, entityConfig.EntityName, entity.Id, filesToUpload, entityFolder);
                }
            }

            _logger.Info(
                $"  {entityConfig.EntityName}: {created} created, {updated} updated, {skipped} skipped (no changes)"
            );
        }
    }

    private static (Entity entity, List<(string FieldName, string RelativePath, string FileName)> FilesToUpload)
    DeserializeJsonRecord(
        JsonObject obj,
        string entityName,
        string primaryIdAttr,
        Dictionary<string, AttributeMetadata> attrMeta,
        Dictionary<string, Dictionary<Guid, Guid>> guidMap
    )
    {
        var entity = new Entity(entityName);
        var filesToUpload = new List<(string, string, string)>();

        foreach (var (key, node) in obj)
        {
            if (node is null)
                continue;
            if (!attrMeta.TryGetValue(key, out var meta))
                continue;
            // File attributes are uploaded separately after the record is created/updated.
            // Check this before the IsValidForCreate/Update guard — file attrs have both false.
            if (meta is FileAttributeMetadata)
            {
                if (node is JsonObject fileObj)
                {
                    var path = fileObj["path"]?.GetValue<string>();
                    var fileName = fileObj["fileName"]?.GetValue<string>();
                    if (path is not null && fileName is not null)
                        filesToUpload.Add((key, path, fileName));
                }
                continue;
            }

            if (meta.IsValidForCreate == false && meta.IsValidForUpdate == false)
                continue;

            var value = MapJsonValueToSdkType(node, meta);
            if (value is null)
                continue;

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

        return (entity, filesToUpload);
    }

    private static object? MapJsonValueToSdkType(JsonNode node, AttributeMetadata meta)
    {
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

    private static List<(string File, JsonObject Obj)> SortTopologically(
        List<(string File, JsonObject Obj)> records,
        string guidField,
        string selfRefField
    )
    {
        var count = records.Count;

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
                && parentIndex != i
            )
            {
                children[parentIndex].Add(i);
                inDegree[i]++;
            }
        }

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
        var parts = Regex.Split(filter, $@"\s+{Regex.Escape(op)}\s+", RegexOptions.IgnoreCase);
        return parts.ToList();
    }

    private static XElement ParseSingleODataCondition(string condition)
    {
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

        if (raw.Equals("null", StringComparison.OrdinalIgnoreCase))
            return new XElement(
                "condition",
                new XAttribute("attribute", field),
                new XAttribute("operator", op == "eq" ? "null" : "not-null")
            );

        var value = raw.StartsWith('\'') && raw.EndsWith('\'') ? raw[1..^1] : raw;

        return new XElement(
            "condition",
            new XAttribute("attribute", field),
            new XAttribute("operator", op),
            new XAttribute("value", value)
        );
    }
}
