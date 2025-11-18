open System
open Microsoft.PowerPlatform.Dataverse.Client
open DG.Daxif

let printUsage () =
    printfn """
╔═══════════════════════════════════════════════════════════════════╗
║                       DAXIF CLI for .NET 8.0                      ║
║                  Cross-Platform Dataverse Automation              ║
╚═══════════════════════════════════════════════════════════════════╝

USAGE:
    daxif <command> [options]

COMMANDS:
    plugin sync      Sync plugins to Dataverse
    webresource sync Sync web resources to Dataverse
    test-connection  Test connection to Dataverse
    help             Show this help message

AUTHENTICATION:
    Set these environment variables for authentication:

    DATAVERSE_URL              - Dataverse URL (e.g., https://org.crm.dynamics.com)
    DATAVERSE_AUTH_TYPE        - Auth type: OAuth, ClientSecret, or Certificate
    
    For OAuth:
      DATAVERSE_APP_ID         - Application (Client) ID
      DATAVERSE_REDIRECT_URI   - Redirect URI (default: http://localhost)
      DATAVERSE_USERNAME       - (Optional) Username for username/password flow
      DATAVERSE_PASSWORD       - (Optional) Password for username/password flow
      DATAVERSE_TOKEN_CACHE    - (Optional) Token cache path
    
    For ClientSecret:
      DATAVERSE_CLIENT_ID      - Application (Client) ID
      DATAVERSE_CLIENT_SECRET  - Client Secret
    
    For Certificate:
      DATAVERSE_CLIENT_ID      - Application (Client) ID
      DATAVERSE_THUMBPRINT     - Certificate Thumbprint

EXAMPLES:
    # Test connection
    daxif test-connection

    # Sync plugins
    daxif plugin sync --assembly ./bin/Debug/MyPlugins.dll --solution MySolution

    # Sync web resources
    daxif webresource sync --folder ./WebResources --solution MySolution

For more information, visit: https://github.com/delegateas/Daxif
"""

let getEnvVar name defaultValue =
    match Environment.GetEnvironmentVariable(name) with
    | null | "" -> defaultValue
    | value -> value

let getRequiredEnvVar name =
    match Environment.GetEnvironmentVariable(name) with
    | null | "" -> 
        printfn "ERROR: Required environment variable '%s' is not set." name
        printfn "Run 'daxif help' for usage information."
        exit 1
    | value -> value

let createConnectionString () =
    let url = getRequiredEnvVar "DATAVERSE_URL"
    let authType = getRequiredEnvVar "DATAVERSE_AUTH_TYPE"
    
    match authType.ToLower() with
    | "oauth" ->
        let appId = getRequiredEnvVar "DATAVERSE_APP_ID"
        let redirectUri = getEnvVar "DATAVERSE_REDIRECT_URI" "http://localhost"
        let username = getEnvVar "DATAVERSE_USERNAME" ""
        let password = getEnvVar "DATAVERSE_PASSWORD" ""
        let tokenCache = getEnvVar "DATAVERSE_TOKEN_CACHE" "./token.cache"
        
        if String.IsNullOrEmpty(username) then
            // Interactive OAuth
            sprintf "AuthType=OAuth;Url=%s;AppId=%s;RedirectUri=%s;TokenCacheStorePath=%s;LoginPrompt=Auto" 
                url appId redirectUri tokenCache
        else
            // Username/Password OAuth
            sprintf "AuthType=OAuth;Url=%s;Username=%s;Password=%s;AppId=%s;RedirectUri=%s;TokenCacheStorePath=%s;LoginPrompt=Auto" 
                url username password appId redirectUri tokenCache
    
    | "clientsecret" ->
        let clientId = getRequiredEnvVar "DATAVERSE_CLIENT_ID"
        let clientSecret = getRequiredEnvVar "DATAVERSE_CLIENT_SECRET"
        sprintf "AuthType=ClientSecret;Url=%s;ClientId=%s;ClientSecret=%s" url clientId clientSecret
    
    | "certificate" ->
        let clientId = getRequiredEnvVar "DATAVERSE_CLIENT_ID"
        let thumbprint = getRequiredEnvVar "DATAVERSE_THUMBPRINT"
        sprintf "AuthType=Certificate;Url=%s;ClientId=%s;Thumbprint=%s" url clientId thumbprint
    
    | _ ->
        printfn "ERROR: Invalid DATAVERSE_AUTH_TYPE '%s'. Must be OAuth, ClientSecret, or Certificate." authType
        exit 1

let createServiceClient () =
    let connectionString = createConnectionString ()
    printfn "Connecting to Dataverse..."
    
    let client = new ServiceClient(connectionString)
    
    if client.IsReady then
        printfn "✓ Connected successfully!"
        printfn "  Organization: %s" client.ConnectedOrgFriendlyName
        printfn "  User: %s" (if String.IsNullOrEmpty(client.OAuthUserId) then "Service Principal" else client.OAuthUserId)
        client
    else
        printfn "✗ Connection failed!"
        printfn "  Error: %s" client.LastError
        exit 1

let testConnection () =
    printfn "\n=========================================="
    printfn "Testing Dataverse Connection"
    printfn "=========================================="
    
    use client = createServiceClient()
    
    printfn "\n✓ Connection test successful!"
    printfn "\nConnection Details:"
    printfn "  Organization ID: %s" (client.ConnectedOrgId.ToString())
    printfn "  Organization: %s" client.ConnectedOrgFriendlyName
    printfn "  Version: %s" (client.ConnectedOrgVersion.ToString())
    printfn "=========================================="
    0

let syncPlugins args =
    let mutable assemblyPath = ""
    let mutable solutionName = ""
    let mutable i = 0
    
    while i < Array.length args do
        match args.[i] with
        | "--assembly" | "-a" ->
            if i + 1 < Array.length args then
                assemblyPath <- args.[i + 1]
                i <- i + 2
            else
                printfn "ERROR: --assembly requires a value"
                exit 1
        | "--solution" | "-s" ->
            if i + 1 < Array.length args then
                solutionName <- args.[i + 1]
                i <- i + 2
            else
                printfn "ERROR: --solution requires a value"
                exit 1
        | _ ->
            printfn "ERROR: Unknown option '%s'" args.[i]
            exit 1
    
    if String.IsNullOrEmpty(assemblyPath) then
        printfn "ERROR: --assembly is required"
        printfn "Usage: daxif plugin sync --assembly <path> --solution <name>"
        exit 1
    
    if String.IsNullOrEmpty(solutionName) then
        printfn "ERROR: --solution is required"
        printfn "Usage: daxif plugin sync --assembly <path> --solution <name>"
        exit 1
    
    if not (IO.File.Exists(assemblyPath)) then
        printfn "ERROR: Assembly file not found: %s" assemblyPath
        exit 1
    
    printfn "\n=========================================="
    printfn "Syncing Plugins"
    printfn "=========================================="
    printfn "Assembly: %s" assemblyPath
    printfn "Solution: %s" solutionName
    
    use client = createServiceClient()
    
    printfn "\nAnalyzing assembly..."
    Plugin.Sync(client, assemblyPath, solutionName)
    
    printfn "\n✓ Plugin sync completed!"
    printfn "=========================================="
    0

let syncWebResources args =
    let mutable folderPath = ""
    let mutable solutionName = ""
    let mutable i = 0
    
    while i < Array.length args do
        match args.[i] with
        | "--folder" | "-f" ->
            if i + 1 < Array.length args then
                folderPath <- args.[i + 1]
                i <- i + 2
            else
                printfn "ERROR: --folder requires a value"
                exit 1
        | "--solution" | "-s" ->
            if i + 1 < Array.length args then
                solutionName <- args.[i + 1]
                i <- i + 2
            else
                printfn "ERROR: --solution requires a value"
                exit 1
        | _ ->
            printfn "ERROR: Unknown option '%s'" args.[i]
            exit 1
    
    if String.IsNullOrEmpty(folderPath) then
        printfn "ERROR: --folder is required"
        printfn "Usage: daxif webresource sync --folder <path> --solution <name>"
        exit 1
    
    if String.IsNullOrEmpty(solutionName) then
        printfn "ERROR: --solution is required"
        printfn "Usage: daxif webresource sync --folder <path> --solution <name>"
        exit 1
    
    if not (IO.Directory.Exists(folderPath)) then
        printfn "ERROR: Folder not found: %s" folderPath
        exit 1
    
    printfn "\n=========================================="
    printfn "Syncing Web Resources"
    printfn "=========================================="
    printfn "Folder: %s" folderPath
    printfn "Solution: %s" solutionName
    
    use client = createServiceClient()
    
    printfn "\nAnalyzing web resources..."
    WebResource.Sync(client, folderPath, solutionName)
    
    printfn "\n✓ Web resource sync completed!"
    printfn "=========================================="
    0

[<EntryPoint>]
let main args =
    try
        match args with
        | [||] | [| "help" |] | [| "--help" |] | [| "-h" |] ->
            printUsage ()
            0
        
        | [| "test-connection" |] ->
            testConnection ()
        
        | [| "plugin"; "sync" |] ->
            printfn "ERROR: Missing required arguments"
            printfn "Usage: daxif plugin sync --assembly <path> --solution <name>"
            1
        
        | _ when args.Length >= 2 && args.[0] = "plugin" && args.[1] = "sync" ->
            syncPlugins args.[2..]
        
        | [| "webresource"; "sync" |] ->
            printfn "ERROR: Missing required arguments"
            printfn "Usage: daxif webresource sync --folder <path> --solution <name>"
            1
        
        | _ when args.Length >= 2 && args.[0] = "webresource" && args.[1] = "sync" ->
            syncWebResources args.[2..]
        
        | _ ->
            printfn "ERROR: Unknown command '%s'" (String.Join(" ", args))
            printfn "Run 'daxif help' for usage information."
            1
    
    with
    | ex ->
        printfn "\n✗ ERROR: %s" ex.Message
        if not (String.IsNullOrEmpty(ex.StackTrace)) then
            printfn "\nStack Trace:"
            printfn "%s" ex.StackTrace
        1
