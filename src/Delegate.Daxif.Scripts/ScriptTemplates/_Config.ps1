# Configuration for Daxif .NET 8.0 PowerShell Scripts
# This configuration has been updated for cross-platform compatibility using ServiceClient

$solutionRoot = "$PSScriptRoot"
$webResourceProject = "$solutionRoot\WebResources"
$sharedPluginProject = "$solutionRoot\Plugins.Shared" 
$sharedNamespace = "Bol.Dynamics365.Plugins.Shared"

return @{

    # Environment Configuration
    # NOTE: URLs should be base URLs only (no /XRMServices/... path)
    Environments = @{
        Dev = @{
            name = "Development"
            # Base URL only - no /XRMServices path!
            url = "https://your-dev-crm-environment.crm4.dynamics.com"
            
            # Authentication Method: "OAuth" or "ClientSecret" or "Certificate"
            authType = "OAuth"
            
            # For OAuth (username/password or interactive)
            username = $env:DATAVERSE_USERNAME  # Or hardcode: "user@domain.com"
            password = $env:DATAVERSE_PASSWORD  # Or hardcode: "yourpassword"
            
            # OAuth App Registration (Microsoft's default for development)
            appId = "51f81489-12ee-4a9e-aaae-a2591f45987d"
            redirectUri = "app://58145B91-0C36-4500-8554-080854F2AC97"
            
            # For Client Secret authentication (uncomment if using)
            # clientId = "your-app-id-guid"
            # clientSecret = "your-client-secret"
            
            # Token cache location (for OAuth)
            tokenCacheStorePath = "$PSScriptRoot\Daxif\TokenCache.dat"
        }
        
        Test = @{
            name = "Test"
            url = "https://your-test-crm-environment.crm4.dynamics.com"
            authType = "OAuth"
            username = $env:DATAVERSE_USERNAME
            password = $env:DATAVERSE_PASSWORD
            appId = "51f81489-12ee-4a9e-aaae-a2591f45987d"
            redirectUri = "app://58145B91-0C36-4500-8554-080854F2AC97"
            tokenCacheStorePath = "$PSScriptRoot\Daxif\TokenCache.dat"
        }
        
        Prod = @{
            name = "Production"
            url = "https://your-prod-crm-environment.crm4.dynamics.com"
            # Production should use Client Secret or Certificate auth
            authType = "ClientSecret"
            clientId = "your-prod-app-id"
            clientSecret = $env:DATAVERSE_CLIENT_SECRET  # Store in environment variable!
        }
    }

    ##
    # CRM Solution Setup
    ##
    SolutionInfo = @{
        name = "YourSolutionName"
        displayName = "Your Solution Display Name"
    }

    ##
    # Plugin Configuration
    ##
    Plugins = @{
        sharedNamespace = $sharedNamespace
        entityNamespace = "$sharedNamespace.Models"
        # List all plugin projects to sync
        projects = @(
            "YourCompany.Dynamics365.Plugins"
        )
        entities = @(
            "account",
            "contact",
            "team",
            "systemuser",
            "opportunity",
            "businessunit",
            "invoicedetail",
            "product",
            "salesorder",
            "uom",
            "role"
        )
    };

    WebResources = @{
        entities = @(
            "account",
            "contact",
            "team",
            "systemuser",
            "opportunity",
            "businessunit",
            "invoicedetail",
            "product",
            "salesorder",
            "uom",
            "role"
        );
    }

    ##
    # Path and project setup
    ##
    Path = @{
        daxifRoot = "$PSScriptRoot\Daxif"
        solutionRoot = $solutionRoot
        toolsFolder = "$PSScriptRoot\Daxif"

        webResourceProject = $webResourceProject
        webResourceJSFolder = "$webResourceProject\js"
        webResourceResxFolder = "$webResourceProject\resx"

        sharedPluginProject = $sharedPluginProject
        entityFolder = "$sharedPluginProject\Models"
    }
}