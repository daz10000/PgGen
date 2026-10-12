module PgGen.Common

open System.Text.RegularExpressions


/// Target framework (minus the "net" prefix) for generated backend projects
let dotnetVersion = "10.0"
/// Target framework for the generated shared (domain types + api contract + client) project.
/// Same as the backend: consumers are .NET (backends, Blazor/MAUI) or Fable (which compiles source).
let sharedTargetFramework = $"net{dotnetVersion}"

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then
        Some (List.tail [ for g in m.Groups -> g.Value ])
    else
        None

let deReservifyFSharp (s:string) =
    if Reserved.reservedFSharp.Contains (s.ToLower()) then
        $"{s}X" // append an X
    else s
let toFSharp (s:string) =
    s.Split([|'_'|]) 
    |> Array.map (fun (s:string) -> 
        if s.Length = 0 then "" 
        else
            s.[0].ToString().ToUpper() + 
                if s.Length > 1 then s.[1..] else ""
        )
    |> String.concat ""
    |> deReservifyFSharp
let toFSharpLower( s:string) =
    let t = toFSharp s
    $"{t.[0].ToString().ToLower()}{t.[1..]}"

let titleCase (s:string) =
    System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s)
