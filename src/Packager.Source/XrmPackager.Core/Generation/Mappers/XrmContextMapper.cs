using XrmPackager.Core.Domain;

namespace XrmPackager.Core.Generation.Mappers;

public static class XrmContextMapper
{
    public static object MapToTemplateModel(
        IEnumerable<TableModel> input,
        GenerationContext context
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        return new
        {
            tables = input,
            @namespace = context.Namespace,
            serviceContextName = context.ServiceContextName ?? "Xrm",
            version = context.Version,
        };
    }
}
