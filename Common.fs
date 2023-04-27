module PgGen.Common

open System.Text.RegularExpressions

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then
        Some (List.tail [ for g in m.Groups -> g.Value ])
    else
        None

let toFSharp (s:string) =
    s.Split([|'_'|]) |> Array.map (fun (s:string) -> s.[0].ToString().ToUpper() + s.[1..])
    |> String.concat ""

let titleCase (s:string) =
    System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s)
