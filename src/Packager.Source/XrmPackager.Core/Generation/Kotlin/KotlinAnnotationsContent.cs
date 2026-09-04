namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinAnnotationsContent
{
    public static string Build(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

@Target(AnnotationTarget.CLASS)
@Retention(AnnotationRetention.RUNTIME)
annotation class D365Entity(
    val logicalName: String,
    val entitySetName: String,
    val primaryKey: String,
    val primaryName: String = """"
)

@Target(AnnotationTarget.FIELD, AnnotationTarget.PROPERTY)
@Retention(AnnotationRetention.RUNTIME)
annotation class D365Field(
    val logicalName: String,
    val kind: D365FieldKind = D365FieldKind.STRING,
    val readOnly: Boolean = false
)

enum class D365FieldKind {{
    STRING, MEMO, INTEGER, DECIMAL, MONEY, BOOLEAN, DATE, DATETIME,
    OPTION_SET, MULTI_OPTION_SET, LOOKUP, GUID
}}
";
    }
}
