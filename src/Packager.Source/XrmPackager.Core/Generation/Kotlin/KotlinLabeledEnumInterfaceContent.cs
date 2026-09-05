namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinLabeledEnumInterfaceContent
{
    public static string Build(string basePackage)
    {
        var enumsPackage = $"{basePackage}.entity.enums";
        return $@"package {enumsPackage}

interface LabeledEnum {{
    val internalLabel: String
    val externalLabel: String
}}

inline fun <reified T> fromInternalLabel(label: String?): T?
    where T : Enum<T>, T : LabeledEnum =
    label?.let {{ l -> enumValues<T>().firstOrNull {{ it.internalLabel.equals(l, ignoreCase = true) }} }}

inline fun <reified T> fromExternalLabel(label: String?): T?
    where T : Enum<T>, T : LabeledEnum =
    label?.let {{ l -> enumValues<T>().firstOrNull {{ it.externalLabel.equals(l, ignoreCase = true) }} }}
";
    }
}
