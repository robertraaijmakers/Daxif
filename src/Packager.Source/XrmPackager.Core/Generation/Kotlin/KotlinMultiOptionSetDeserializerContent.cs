namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinMultiOptionSetDeserializerContent
{
    public static string Build(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import tools.jackson.core.JsonParser
import tools.jackson.core.JsonToken
import tools.jackson.databind.DeserializationContext
import tools.jackson.databind.deser.std.StdDeserializer

class D365MultiOptionSetDeserializer : StdDeserializer<List<Int>>(List::class.java) {{
    override fun deserialize(p: JsonParser, ctxt: DeserializationContext): List<Int>? {{
        return when (p.currentToken()) {{
            JsonToken.VALUE_STRING -> {{
                val raw = p.text.trim()
                if (raw.isEmpty()) emptyList()
                else raw.split("","").map {{ it.trim().toInt() }}
            }}
            JsonToken.START_ARRAY -> {{
                val result = mutableListOf<Int>()
                while (p.nextToken() != JsonToken.END_ARRAY) result.add(p.intValue)
                result
            }}
            JsonToken.VALUE_NULL -> null
            else -> null
        }}
    }}
}}
";
    }
}
