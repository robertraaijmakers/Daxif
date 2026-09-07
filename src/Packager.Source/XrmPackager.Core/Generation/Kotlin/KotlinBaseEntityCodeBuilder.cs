namespace XrmPackager.Core.Generation.Kotlin;

public static class KotlinBaseEntityCodeBuilder
{
    public static string BuildBaseEntity(string basePackage)
    {
        var corePackage = $"{basePackage}.core";
        return $@"package {corePackage}

import com.fasterxml.jackson.annotation.JsonInclude
import com.fasterxml.jackson.annotation.JsonProperty
import java.util.UUID

@JsonInclude(JsonInclude.Include.NON_NULL)
open class D365BaseEntity {{

    @field:JsonProperty(value = ""@odata.etag"", access = JsonProperty.Access.READ_ONLY)
    var etag: String? = null

    @D365Field(logicalName = ""versionnumber"", kind = D365FieldKind.INTEGER)
    @field:JsonProperty(""versionnumber"")
    var versionnumber: Long? = null

    @D365Field(logicalName = ""createdon"", kind = D365FieldKind.DATETIME)
    @field:JsonProperty(""createdon"")
    var createdon: String? = null

    @D365Field(logicalName = ""modifiedon"", kind = D365FieldKind.DATETIME)
    @field:JsonProperty(""modifiedon"")
    var modifiedon: String? = null

    @D365Field(logicalName = ""overriddencreatedon"", kind = D365FieldKind.DATETIME)
    @field:JsonProperty(""overriddencreatedon"")
    var overriddencreatedon: String? = null

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

    @D365Field(logicalName = ""owningbusinessunit"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owningbusinessunit_value"", access = JsonProperty.Access.READ_ONLY)
    var owningbusinessunitValue: UUID? = null

    @D365Field(logicalName = ""owningteam"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owningteam_value"", access = JsonProperty.Access.READ_ONLY)
    var owningteamValue: UUID? = null

    @D365Field(logicalName = ""owninguser"", kind = D365FieldKind.LOOKUP, readOnly = true)
    @field:JsonProperty(value = ""_owninguser_value"", access = JsonProperty.Access.READ_ONLY)
    var owninguserValue: UUID? = null

    @field:JsonProperty(value = ""ownerid_systemuser@odata.bind"", access = JsonProperty.Access.WRITE_ONLY)
    var owneridSystemuserBind: String? = null

    @field:JsonProperty(value = ""ownerid_team@odata.bind"", access = JsonProperty.Access.WRITE_ONLY)
    var owneridTeamBind: String? = null
}}
";
    }
}
