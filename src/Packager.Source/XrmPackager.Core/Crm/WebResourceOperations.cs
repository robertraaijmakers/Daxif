namespace XrmPackager.Core.Crm;

using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

public sealed class WebResourceOperations
{
    private readonly ILogger _logger;

    public WebResourceOperations(ILogger logger)
    {
        _logger = logger;
    }

    public void Sync(ServiceClient client, WebResourceSyncOptions options)
    {
        if (!Directory.Exists(options.FolderPath))
        {
            throw new InvalidArgumentException(
                $"Webresource folder not found: {options.FolderPath}"
            );
        }

        var rootFolder = ResolveRootFolder(options.FolderPath);
        var (solutionId, solutionPublisherPrefix) = SolutionMetadataProvider.GetSolutionInfo(
            client,
            options.SolutionName
        );
        var prefix = ResolveWebResourcePrefix(
            options.SolutionName,
            rootFolder,
            solutionPublisherPrefix
        );
        _logger.Info($"Resolved webresource prefix from solution publisher: {prefix}");

        var localResources = LoadLocalResources(rootFolder, prefix);
        var existingResources = RetrieveExistingInSolution(client, solutionId);

        var localByName = localResources.ToDictionary(
            x => x.Name,
            StringComparer.OrdinalIgnoreCase
        );
        var crmByName = existingResources.ToDictionary(
            x => x.GetAttributeValue<string>("name"),
            StringComparer.OrdinalIgnoreCase
        );

        var toCreate = localByName
            .Keys.Except(crmByName.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var updateCandidates = localByName
            .Keys.Intersect(crmByName.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var toUpdate = new List<string>();
        foreach (var name in updateCandidates)
        {
            var local = localByName[name];
            var current = crmByName[name];
            var currentContent = current.GetAttributeValue<string>("content") ?? string.Empty;
            var currentDisplay = current.GetAttributeValue<string>("displayname") ?? string.Empty;

            if (
                !string.Equals(currentContent, local.ContentBase64, StringComparison.Ordinal)
                || !string.Equals(currentDisplay, local.DisplayName, StringComparison.Ordinal)
            )
            {
                toUpdate.Add(name);
            }
        }

        var toDelete = options.DeleteMissing
            ? crmByName.Keys.Except(localByName.Keys, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        _logger.Info(
            $"Webresources - create: {toCreate.Count}, update: {toUpdate.Count}, delete: {toDelete.Count}"
        );

        if (options.DryRun)
        {
            _logger.Info("Dry-run enabled, skipping Dataverse mutations.");
            LogPlannedChanges("Would add", toCreate);
            LogPlannedChanges("Would update", toUpdate);
            LogPlannedChanges("Would delete", toDelete);
            return;
        }

        foreach (var name in toCreate)
        {
            var local = localByName[name];
            var entity = new Entity("webresource")
            {
                ["name"] = local.Name,
                ["displayname"] = local.DisplayName,
                ["content"] = local.ContentBase64,
                ["webresourcetype"] = new OptionSetValue((int)local.Type),
            };
            var createdId = client.Create(entity);
            TryAddComponentToSolution(client, createdId, options.SolutionName, 61);
            // Track newly created entity so RESX dependency sync can find it.
            var createdEntity = new Entity("webresource", createdId) { ["dependencyxml"] = string.Empty };
            crmByName[name] = createdEntity;
            _logger.Info($"Created webresource: {name}");
        }

        foreach (var name in toUpdate)
        {
            var local = localByName[name];
            var current = crmByName[name];

            var update = new Entity("webresource", current.Id)
            {
                ["displayname"] = local.DisplayName,
                ["content"] = local.ContentBase64,
            };
            client.Update(update);
            TryAddComponentToSolution(client, current.Id, options.SolutionName, 61);
            _logger.Info($"Updated webresource: {name}");
        }

        foreach (var name in toDelete)
        {
            var current = crmByName[name];
            client.Delete("webresource", current.Id);
            _logger.Info($"Deleted webresource: {name}");
        }

        _logger.Info("Webresource sync completed.");

        SyncResxDependencies(client, localResources, crmByName, options.DryRun);

        if (options.PublishAfterSync)
        {
            _logger.Info("Publishing all customizations...");
            client.Execute(new PublishAllXmlRequest());
            _logger.Info("Publish completed.");
        }
    }

    private void LogPlannedChanges(string actionLabel, List<string> names)
    {
        if (names.Count == 0)
        {
            _logger.Info($"{actionLabel}: none");
            return;
        }

        _logger.Info($"{actionLabel}: {names.Count}");
        foreach (var name in names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"  - {name}");
        }
    }

    private static List<Entity> RetrieveExistingInSolution(
        IOrganizationService service,
        Guid solutionId
    )
    {
        var linkToSolutionComponent = new LinkEntity
        {
            JoinOperator = JoinOperator.Inner,
            LinkFromEntityName = "webresource",
            LinkFromAttributeName = "webresourceid",
            LinkToEntityName = "solutioncomponent",
            LinkToAttributeName = "objectid",
            LinkCriteria = new FilterExpression(LogicalOperator.And),
        };
        linkToSolutionComponent.LinkCriteria.AddCondition(
            "solutionid",
            ConditionOperator.Equal,
            solutionId
        );

        var query = new QueryExpression("webresource")
        {
            ColumnSet = new ColumnSet("webresourceid", "name", "displayname", "content", "dependencyxml"),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("ismanaged", ConditionOperator.Equal, false);
        query.LinkEntities.Add(linkToSolutionComponent);

        var result = service.RetrieveMultiple(query);
        return result.Entities.ToList();
    }

    private string ResolveWebResourcePrefix(
        string solutionName,
        string rootFolder,
        string solutionPublisherPrefix
    )
    {
        if (string.IsNullOrWhiteSpace(solutionPublisherPrefix))
        {
            throw new InvalidArgumentException(
                $"Solution '{solutionName}' does not have a publisher customization prefix."
            );
        }

        var normalizedPrefix = solutionPublisherPrefix.Trim();
        if (normalizedPrefix.EndsWith("_", StringComparison.Ordinal))
        {
            normalizedPrefix = normalizedPrefix.TrimEnd('_');
        }

        var folderPrefixHint = TryGetFolderPrefixHint(rootFolder);
        if (
            !string.IsNullOrWhiteSpace(folderPrefixHint)
            && !string.Equals(
                folderPrefixHint,
                normalizedPrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            _logger.Warning(
                $"Webresource folder suggests prefix '{folderPrefixHint}', but solution '{solutionName}' uses publisher prefix '{normalizedPrefix}'. Using the solution publisher prefix."
            );
        }

        return normalizedPrefix + "_";
    }

    private static string ResolveRootFolder(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);

        if (!Directory.Exists(full))
        {
            throw new InvalidArgumentException($"Webresource folder not found: {folderPath}");
        }

        return full;
    }

    private static string? TryGetFolderPrefixHint(string folderPath)
    {
        var leaf = Path.GetFileName(
            folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        );

        if (string.IsNullOrWhiteSpace(leaf))
        {
            return null;
        }

        if (leaf.EndsWith("_", StringComparison.Ordinal))
        {
            return leaf.TrimEnd('_');
        }

        var split = leaf.Split('_', 2, StringSplitOptions.RemoveEmptyEntries);
        if (split.Length >= 2)
        {
            return split[0];
        }

        return null;
    }

    private static List<LocalWebResource> LoadLocalResources(string rootFolder, string prefix)
    {
        var files = Directory
            .EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories)
            .Where(x => !x.EndsWith("_nosync", StringComparison.OrdinalIgnoreCase))
            .Where(x => TryGetWebResourceType(x, out _))
            .ToList();

        var result = new List<LocalWebResource>(files.Count);
        foreach (var file in files)
        {
            if (!TryGetWebResourceType(file, out var type))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(rootFolder, file).Replace('\\', '/');
            var name = $"{prefix}/{relativePath}";
            var contentBase64 = Convert.ToBase64String(File.ReadAllBytes(file));

            result.Add(
                new LocalWebResource
                {
                    Name = name,
                    DisplayName = Path.GetFileName(file),
                    Type = type,
                    ContentBase64 = contentBase64,
                }
            );
        }

        return result;
    }

    private static bool TryGetWebResourceType(string filePath, out WebResourceType type)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        switch (ext)
        {
            case "HTML":
            case "HTM":
                type = WebResourceType.HTML;
                return true;
            case "CSS":
                type = WebResourceType.CSS;
                return true;
            case "JS":
                type = WebResourceType.JavaScript;
                return true;
            case "XML":
            case "XAML":
            case "XSD":
                type = WebResourceType.XML;
                return true;
            case "PNG":
                type = WebResourceType.PNG;
                return true;
            case "JPG":
            case "JPEG":
                type = WebResourceType.JPG;
                return true;
            case "GIF":
                type = WebResourceType.GIF;
                return true;
            case "XAP":
                type = WebResourceType.XAP;
                return true;
            case "XSL":
            case "XSLT":
                type = WebResourceType.XSL;
                return true;
            case "ICO":
                type = WebResourceType.ICO;
                return true;
            case "SVG":
                type = WebResourceType.SVG;
                return true;
            case "RESX":
                type = WebResourceType.RESX;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private void TryAddComponentToSolution(
        ServiceClient client,
        Guid objectId,
        string solutionName,
        int componentType
    )
    {
        try
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentType = componentType,
                ComponentId = objectId,
                SolutionUniqueName = solutionName,
            };

            client.Execute(addRequest);
        }
        catch
        {
            _logger.Verbose($"Component {objectId} may already exist in solution {solutionName}.");
        }
    }

    private void SyncResxDependencies(
        IOrganizationService client,
        List<LocalWebResource> localResources,
        Dictionary<string, Entity> crmByName,
        bool dryRun
    )
    {
        // Find all ScriptTranslations.<lcid>.resx resources (located in the /resx/ folder).
        var scriptTranslations = localResources
            .Where(x =>
                x.Type == WebResourceType.RESX
                && IsScriptTranslationsResx(x.Name)
            )
            .ToList();

        if (scriptTranslations.Count == 0)
        {
            return;
        }

        // Every JS webresource in Dataverse gets a dependency on all ScriptTranslations RESX files.
        var jsEntities = crmByName
            .Values
            .Where(e => e.GetAttributeValue<string>("name")?.EndsWith(".js", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        foreach (var jsEntity in jsEntities)
        {
            var jsName = jsEntity.GetAttributeValue<string>("name") ?? string.Empty;
            var currentXml = jsEntity.GetAttributeValue<string>("dependencyxml") ?? string.Empty;
            var updatedXml = MergeResxDependencies(currentXml, scriptTranslations);

            if (string.Equals(currentXml, updatedXml, StringComparison.Ordinal))
            {
                _logger.Verbose($"RESX dependencies for '{jsName}' are already up to date.");
                continue;
            }

            if (dryRun)
            {
                _logger.Info($"Would register RESX dependencies for '{jsName}'.");
                continue;
            }

            var update = new Entity("webresource", jsEntity.Id)
            {
                ["dependencyxml"] = updatedXml,
            };
            client.Update(update);
            _logger.Info($"Registered RESX dependencies for '{jsName}'.");
        }
    }

    /// <summary>
    /// Returns true when the webresource name matches the pattern
    /// &lt;prefix&gt;/resx/ScriptTranslations.&lt;lcid&gt;.resx
    /// </summary>
    private static bool IsScriptTranslationsResx(string name)
    {
        // e.g. bol_/resx/ScriptTranslations.1033.resx
        var fileName = name.Split('/').Last();
        if (!fileName.StartsWith("ScriptTranslations.", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Middle part must be a numeric LCID: ScriptTranslations.<lcid>.resx
        var middle = fileName["ScriptTranslations.".Length..^".resx".Length];
        return int.TryParse(middle, out _);
    }

    private static string MergeResxDependencies(
        string currentXml,
        List<LocalWebResource> resxResources
    )
    {
        XElement root;
        XElement dependencyElement;

        if (!string.IsNullOrWhiteSpace(currentXml))
        {
            root = XElement.Parse(currentXml);
            dependencyElement =
                root.Elements("Dependency")
                    .FirstOrDefault(
                        e => (string?)e.Attribute("componentType") == "WebResource"
                    )
                ?? new XElement(
                    "Dependency",
                    new XAttribute("componentType", "WebResource")
                );

            if (dependencyElement.Parent == null)
            {
                root.Add(dependencyElement);
            }
        }
        else
        {
            root = new XElement("Dependencies");
            dependencyElement = new XElement(
                "Dependency",
                new XAttribute("componentType", "WebResource")
            );
            root.Add(dependencyElement);
        }

        var existingNames = dependencyElement
            .Elements("Library")
            .Select(e => (string?)e.Attribute("name"))
            .Where(n => n != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = false;
        foreach (var resx in resxResources)
        {
            if (existingNames.Contains(resx.Name))
            {
                continue;
            }

            var nameWithoutResxExt = resx.Name[..^5];
            var lastDot = nameWithoutResxExt.LastIndexOf('.');
            var languageCode = nameWithoutResxExt[(lastDot + 1)..];

            dependencyElement.Add(
                new XElement(
                    "Library",
                    new XAttribute("name", resx.Name),
                    new XAttribute("displayName", resx.DisplayName),
                    new XAttribute("languagecode", languageCode),
                    new XAttribute("description", ""),
                    new XAttribute("libraryUniqueId", $"{{{Guid.NewGuid()}}}")
                )
            );
            added = true;
        }

        return added || string.IsNullOrWhiteSpace(currentXml)
            ? root.ToString(SaveOptions.DisableFormatting)
            : currentXml;
    }

    private sealed class LocalWebResource
    {
        public required string Name { get; init; }
        public required string DisplayName { get; init; }
        public required WebResourceType Type { get; init; }
        public required string ContentBase64 { get; init; }
    }
}
