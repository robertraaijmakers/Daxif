namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinMultiOptionSetDeserializerContent
{
    public static string Build(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import tools.jackson.core.JsonParser
import tools.jackson.core.JsonTokenId
import tools.jackson.databind.DeserializationContext
import tools.jackson.databind.deser.std.StdDeserializer

class D365MultiOptionSetDeserializer : StdDeserializer<List<Int>>(List::class.java) {{
    override fun deserialize(p: JsonParser, ctxt: DeserializationContext): List<Int>? {{
        return when (p.currentTokenId()) {{
            JsonTokenId.ID_STRING -> {{
                val raw = p.getString().trim()
                if (raw.isEmpty()) {{
                    emptyList()
                }} else {{
                    raw.split("","").map {{ it.trim().toInt() }}
                }}
            }}

            JsonTokenId.ID_START_ARRAY -> {{
                val result = mutableListOf<Int>()
                while (p.nextToken().id() != JsonTokenId.ID_END_ARRAY) result.add(p.intValue)
                result
            }}

            JsonTokenId.ID_NULL -> {{
                null
            }}

            else -> {{
                null
            }}
        }}
    }}
}}
";
    }
}
