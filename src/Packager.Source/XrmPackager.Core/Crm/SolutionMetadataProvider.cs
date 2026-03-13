namespace XrmPackager.Core.Crm;

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

internal static class SolutionMetadataProvider
{
    internal static (Guid SolutionId, string PublisherPrefix) GetSolutionInfo(
        ServiceClient client,
        string solutionName
    )
    {
        var query = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "publisherid"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, solutionName);

        var solution = client.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (solution == null)
        {
            throw new InvalidArgumentException($"Solution not found: {solutionName}");
        }

        var publisherRef =
            solution.GetAttributeValue<EntityReference>("publisherid")
            ?? throw new InvalidOperationException(
                $"Solution '{solutionName}' is missing publisher reference."
            );

        var publisher = client.Retrieve(
            "publisher",
            publisherRef.Id,
            new ColumnSet("customizationprefix")
        );

        return (
            solution.Id,
            publisher.GetAttributeValue<string>("customizationprefix") ?? string.Empty
        );
    }
}
