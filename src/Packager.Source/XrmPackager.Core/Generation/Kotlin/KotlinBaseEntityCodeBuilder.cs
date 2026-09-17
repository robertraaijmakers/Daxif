namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinBaseEntityCodeBuilder
{
    public static string BuildTrackableEntity(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import com.fasterxml.jackson.annotation.JsonFilter
import com.fasterxml.jackson.annotation.JsonIgnore
import com.fasterxml.jackson.annotation.JsonProperty
import kotlin.properties.ReadWriteProperty
import kotlin.reflect.KProperty

@JsonFilter(""d365DirtyFilter"")
open class D365TrackableEntity {{

    companion object

    @JsonIgnore
    private val _dirtyFields = mutableSetOf<String>()

    @get:JsonIgnore
    val dirtyFields: Set<String> get() = _dirtyFields

    fun clearDirtyFields() = _dirtyFields.clear()

    protected fun <T> trackable(initialValue: T, jsonName: String): ReadWriteProperty<Any?, T> =
        object : ReadWriteProperty<Any?, T> {{
            private var value = initialValue

            override fun getValue(thisRef: Any?, property: KProperty<*>): T = value

            override fun setValue(thisRef: Any?, property: KProperty<*>, value: T) {{
                _dirtyFields.add(jsonName)
                this.value = value
            }}
        }}

    @field:JsonProperty(value = ""@odata.etag"", access = JsonProperty.Access.READ_ONLY)
    @get:JsonProperty(value = ""@odata.etag"", access = JsonProperty.Access.READ_ONLY)
    var etag: String? = null
}}
";
    }

    public static string BuildBaseEntity(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import com.fasterxml.jackson.annotation.JsonProperty
import java.util.UUID

open class D365BaseEntity : D365TrackableEntity() {{

    @D365Field(logicalName = ""versionnumber"", kind = D365FieldKind.INTEGER)
    @get:JsonProperty(""versionnumber"")
    var versionnumber: Long? by trackable(null, ""versionnumber"")

    @D365Field(logicalName = ""createdon"", kind = D365FieldKind.DATETIME)
    @get:JsonProperty(""createdon"")
    var createdon: String? by trackable(null, ""createdon"")

    @D365Field(logicalName = ""modifiedon"", kind = D365FieldKind.DATETIME)
    @get:JsonProperty(""modifiedon"")
    var modifiedon: String? by trackable(null, ""modifiedon"")

    @D365Field(logicalName = ""overriddencreatedon"", kind = D365FieldKind.DATETIME)
    @get:JsonProperty(""overriddencreatedon"")
    var overriddencreatedon: String? by trackable(null, ""overriddencreatedon"")

    @D365Field(logicalName = ""createdby"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_createdby_value"", access = JsonProperty.Access.READ_ONLY)
    var createdbyValue: UUID? = null

    @D365Field(logicalName = ""modifiedby"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_modifiedby_value"", access = JsonProperty.Access.READ_ONLY)
    var modifiedbyValue: UUID? = null

    @D365Field(logicalName = ""createdonbehalfby"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_createdonbehalfby_value"", access = JsonProperty.Access.READ_ONLY)
    var createdonbehalfbyValue: UUID? = null

    @D365Field(logicalName = ""modifiedonbehalfby"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_modifiedonbehalfby_value"", access = JsonProperty.Access.READ_ONLY)
    var modifiedonbehalfbyValue: UUID? = null
}}
";
    }

    public static string BuildOwnableEntity(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import com.fasterxml.jackson.annotation.JsonProperty
import java.util.UUID

open class D365OwnableEntity : D365BaseEntity() {{

    @D365Field(logicalName = ""ownerid"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_ownerid_value"", access = JsonProperty.Access.READ_ONLY)
    var owneridValue: UUID? = null

    @field:JsonProperty(value = ""_ownerid_value@Microsoft.Dynamics.CRM.lookuplogicalname"", access = JsonProperty.Access.READ_ONLY)
    var owneridLogicalName: String? = null

    @get:JsonProperty(value = ""ownerid_systemuser@odata.bind"", access = JsonProperty.Access.WRITE_ONLY)
    var owneridSystemuserBind: String? by trackable(null, ""ownerid_systemuser@odata.bind"")

    @get:JsonProperty(value = ""ownerid_team@odata.bind"", access = JsonProperty.Access.WRITE_ONLY)
    var owneridTeamBind: String? by trackable(null, ""ownerid_team@odata.bind"")

    @D365Field(logicalName = ""owningbusinessunit"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owningbusinessunit_value"", access = JsonProperty.Access.READ_ONLY)
    var owningbusinessunitValue: UUID? = null

    @D365Field(logicalName = ""owningteam"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owningteam_value"", access = JsonProperty.Access.READ_ONLY)
    var owningteamValue: UUID? = null

    @D365Field(logicalName = ""owninguser"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owninguser_value"", access = JsonProperty.Access.READ_ONLY)
    var owninguserValue: UUID? = null
}}
";
    }
}
