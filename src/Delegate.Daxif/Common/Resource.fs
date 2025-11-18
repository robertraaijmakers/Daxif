namespace DG.Daxif.Common

open System
open System.IO
open System.Reflection
open DG.Daxif
open DG.Daxif.Common

module internal Resource = 
  let private stringToWebResourceType (s: string) =
    match s.ToUpper() with
    | "HTML" -> Some WebResourceType.HTML
    | "HTM"  -> Some WebResourceType.HTM
    | "CSS"  -> Some WebResourceType.CSS
    | "JS"   -> Some WebResourceType.JS
    | "XML"  -> Some WebResourceType.XML
    | "XAML" -> Some WebResourceType.XAML
    | "XSD"  -> Some WebResourceType.XSD
    | "PNG"  -> Some WebResourceType.PNG
    | "JPG"  -> Some WebResourceType.JPG
    | "JPEG" -> Some WebResourceType.JPEG
    | "GIF"  -> Some WebResourceType.GIF
    | "XAP"  -> Some WebResourceType.XAP
    | "XSL"  -> Some WebResourceType.XSL
    | "XSLT" -> Some WebResourceType.XSLT
    | "ICO"  -> Some WebResourceType.ICO
    | "SVG"  -> Some WebResourceType.SVG
    | "RESX" -> Some WebResourceType.RESX
    | _ -> None

  let (|XML|Binary|Text|Unknown|) (s : string) = 
    let fileSplit = s.Split([| '.' |])
    let fileExtension = fileSplit.[fileSplit.Length - 1]
    match stringToWebResourceType fileExtension with
    | Some resourceType ->
        match int resourceType with
        | 1 | 2 | 3 | 9 -> Text
        | 5 | 6 | 7 | 8 | 10 -> Binary
        | 4 -> XML
        | _ -> Unknown
    | None -> Unknown
  
  let resources = 
    Assembly.GetExecutingAssembly().GetManifestResourceNames()
    |> Array.toList
    |> List.map (fun x -> 
         use stream = 
           Assembly.GetExecutingAssembly().GetManifestResourceStream(x)
         use sr = new StreamReader(stream)
         x, sr.ReadToEnd())
    |> Map.ofList
  
  let txtResources = 
    Assembly.GetExecutingAssembly().GetManifestResourceNames()
    |> Array.filter (function | Text -> true | _ -> false)
    |> Array.toList
    |> List.map (fun x -> 
         use stream = 
           Assembly.GetExecutingAssembly().GetManifestResourceStream(x)
         use sr = new StreamReader(stream)
         x, sr.ReadToEnd())
    |> Map.ofList
