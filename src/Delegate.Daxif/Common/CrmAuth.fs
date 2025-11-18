module internal DG.Daxif.Common.CrmAuth

open System.Net
open Microsoft.Xrm.Sdk
open DG.Daxif
open DG.Daxif.Common.InternalUtility
open System
open Microsoft.PowerPlatform.Dataverse.Client
open System.IO


// Legacy support - kept for backward compatibility
type AuthenticationProviderType =
  | ActiveDirectory = 0
  | OnlineFederation = 1
  | Federation = 2

let ensureClientIsReady (client: ServiceClient) =
  match client.IsReady with
  | false ->
    let s = sprintf "Client could not authenticate. If the application user was just created, it might take a while before it is available.\n%s" client.LastError 
    in failwith s
  | true -> client

// Get ServiceClient using username/password authentication
let internal getServiceClient userName password (orgUrl:Uri) mfaAppId mfaReturnUrl (timeOut: TimeSpan) (useUniqueInstance: bool) =
  let connectionString = 
    sprintf "AuthType=OAuth;Url=%s;Username=%s;Password=%s;AppId=%s;RedirectUri=%s;LoginPrompt=Auto;RequireNewInstance=%b"
      (orgUrl.ToString()) userName password mfaAppId mfaReturnUrl useUniqueInstance
  
  log.Verbose @"Connection timeout set to %i hour, %i minutes, %i seconds" timeOut.Hours timeOut.Minutes timeOut.Seconds
  ServiceClient.MaxConnectionTimeout <- timeOut
  new ServiceClient(connectionString)
  |> ensureClientIsReady

// Get ServiceClient using client credentials (app id + secret)
let internal getServiceClientClientSecret (org: Uri) appId clientSecret (timeOut: TimeSpan) (useUniqueInstance: bool) =
  let connectionString =
    sprintf "AuthType=ClientSecret;Url=%s;ClientId=%s;ClientSecret=%s;RequireNewInstance=%b"
      (org.ToString()) appId clientSecret useUniqueInstance
  log.Verbose @"Connection timeout set to %i hour, %i minutes, %i seconds" timeOut.Hours timeOut.Minutes timeOut.Seconds
  ServiceClient.MaxConnectionTimeout <- timeOut
  new ServiceClient(connectionString)
  |> ensureClientIsReady

// Get ServiceClient using connection string
let internal getServiceClientConnectionString (cs: string) (timeOut: TimeSpan) =
  log.Verbose @"Connection timeout set to %i hour, %i minutes, %i seconds" timeOut.Hours timeOut.Minutes timeOut.Seconds
  ServiceClient.MaxConnectionTimeout <- timeOut
  new ServiceClient(cs)
  |> ensureClientIsReady

// Legacy function names for backward compatibility
let getCrmServiceClient = getServiceClient
let getCrmServiceClientClientSecret = getServiceClientClientSecret  
let getCrmServiceClientConnectionString = getServiceClientConnectionString