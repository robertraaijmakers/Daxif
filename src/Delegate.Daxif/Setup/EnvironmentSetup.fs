namespace DG.Daxif

open System
open Microsoft.Xrm.Sdk
open Microsoft.PowerPlatform.Dataverse.Client

// ============================================================================
// DEPRECATED MODULE
// ============================================================================
// This module provided legacy credential management and authentication.
// It has been deprecated in favor of using ServiceClient directly.
//
// Migration guide:
// Instead of using Environment.create(), use ServiceClient directly:
//
//   // OAuth/Office365 authentication
//   let connectionString = "AuthType=OAuth;Url=https://org.crm.dynamics.com;Username=user@org.com;Password=***;AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Auto"
//   let client = new ServiceClient(connectionString)
//   let service = client :> IOrganizationService
//
//   // Client Secret authentication  
//   let client = new ServiceClient(new Uri("https://org.crm.dynamics.com"), "clientId", ServiceClient.MakeSecureString("clientSecret"), true)
//   let service = client :> IOrganizationService
//
// ============================================================================

// Minimal type definitions kept for backward compatibility
type CrmServiceClientOAuth = {
  orgUrl: Uri
  username: string
  password: string
  clientId: string
  returnUrl: string
}

type CrmServiceClientClientSecret = {
  orgUrl: Uri
  clientId: string
  clientSecret: string
}

type ConnectionMethod =
  | CrmServiceClientOAuth of CrmServiceClientOAuth
  | CrmServiceClientClientSecret of CrmServiceClientClientSecret
  | ConnectionString of string

type ConnectionType = 
  | OAuth
  | ClientSecret
  | ConnectionString
