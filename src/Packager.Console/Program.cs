using XrmPackager.Commands;
using XrmPackager.Core;
using XrmPackager.Core.Authentication;

namespace XrmPackager.Console;

class Program
{
    static int Main(string[] args)
    {
        var logger = new ConsoleLogger(LogLevel.Info);

        try
        {
            PrintBanner();

            if (args.Length == 0)
            {
                PrintUsage();
                return 0;
            }

            var command = args[0].ToLower();

            switch (command)
            {
                case "help":
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;

                case "test-connection":
                    return HandleTestConnection(args, logger);

                case "plugin":
                    return HandlePluginCommand(args, logger);

                case "webresource":
                    return HandleWebResourceCommand(args, logger);

                case "solution":
                    return HandleSolutionCommand(args, logger);

                case "context":
                    return HandleContextCommand(args, logger);

                case "xrmdt":
                    return HandleXrmDtCommand(args, logger);

                default:
                    logger.Error($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Fatal error: {ex.Message}");
            if (System.Environment.GetEnvironmentVariable("DEBUG") == "1")
            {
                logger.Debug(ex.StackTrace ?? "No stack trace available");
            }
            return 1;
        }
    }

    static void PrintBanner()
    {
        System.Console.WriteLine(
            @"
╔═══════════════════════════════════════════════════════════════════╗
║            XRM PACKAGER - Dynamics 365 Automation Tool            ║
║                     .NET 10.0 - Cross-Platform                    ║
╚═══════════════════════════════════════════════════════════════════╝"
        );
    }

    static void PrintUsage()
    {
        System.Console.WriteLine(
            @"
USAGE:
    xrmpackager <command> [options]

COMMANDS:
    test-connection       Test connection to Dataverse
    plugin sync          Sync plugin assembly to Dataverse
    webresource sync     Sync web resources to Dataverse
    solution import      Import solution to Dataverse
    solution publish     Publish all customizations
    context generate     Generate C# early-bound context
    xrmdt generate       Generate TypeScript definitions
    help                 Show this help message

EXAMPLES:
    xrmpackager test-connection
    xrmpackager plugin sync --assembly ./bin/Debug/MyPlugins.dll --solution MySolution
    xrmpackager plugin sync --assembly ./bin/Debug/MyPlugins.dll --solution MySolution --dry-run
    xrmpackager webresource sync --folder ./WebResources --solution MySolution
    xrmpackager webresource sync --folder ./WebResources --solution MySolution --dry-run
    xrmpackager solution import --zip ./MySolution.zip --publish
    xrmpackager context generate --out ./Generated/XrmContext.cs --namespace MyCompany.Crm
    xrmpackager xrmdt generate --out ./Generated/Xrm.d.ts --namespace Xrm

AUTHENTICATION:
    Authentication is configured via environment variables:

    DATAVERSE_URL              - Organization URL (required)
    DATAVERSE_AUTH_TYPE        - OAuth, ClientSecret, or Certificate (required)
    DATAVERSE_APP_ID           - Application ID (required for OAuth/ClientSecret/Cert)
    DATAVERSE_CLIENT_SECRET    - Client Secret (for ClientSecret auth)
    DATAVERSE_THUMBPRINT       - Certificate Thumbprint (for Certificate auth)
    DATAVERSE_USERNAME         - Username (optional for OAuth)
    DATAVERSE_PASSWORD         - Password (optional for OAuth)
    DATAVERSE_TOKEN_CACHE      - Token cache path (optional)
    DATAVERSE_REDIRECT_URI     - OAuth redirect URI (default: http://localhost)
    DATAVERSE_LOGIN_PROMPT     - OAuth prompt mode: Auto, Never, Always (optional)
    DATAVERSE_LOGIN_HINT       - OAuth preferred account/email hint (optional)

For more information, visit: https://github.com/XrmPackager/XrmPackager
"
        );
    }

    static int HandleTestConnection(string[] args, ILogger logger)
    {
        var command = new TestConnectionCommand(logger);
        return command.Execute();
    }

    static int HandlePluginCommand(string[] args, ILogger logger)
    {
        var command = new PluginSyncCommand(logger);
        return command.Execute(args);
    }

    static int HandleWebResourceCommand(string[] args, ILogger logger)
    {
        var command = new WebResourceSyncCommand(logger);
        return command.Execute(args);
    }

    static int HandleSolutionCommand(string[] args, ILogger logger)
    {
        var command = new SolutionCommand(logger);
        return command.Execute(args);
    }

    static int HandleContextCommand(string[] args, ILogger logger)
    {
        var command = new ContextGenerateCommand(logger);
        return command.Execute(args);
    }

    static int HandleXrmDtCommand(string[] args, ILogger logger)
    {
        var command = new XrmDefinitelyTypedGenerateCommand(logger);
        return command.Execute(args);
    }
}
