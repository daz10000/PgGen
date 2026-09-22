#r "nuget: Argu, 6.2.0"
#r "nuget: Npgsql, 9.0.0"
#r "bin/Debug/net9.0/PgGen.dll"

open System
open System.IO
open Argu
open PgGen
open PgGen.ExtractSchema
open PgGen.CodeGen
open PgGen.Build

// Define CLI arguments
[<CliPrefix(CliPrefix.DoubleDash)>]
type CLIArgs =
    | Verbose of bool
    | Output of output:string
    | ConnectionString of conn:string
    | SchemaExclude of schema:string
    | [<Mandatory>] Name of name:string 
    | Codegen of folder:string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Verbose _ -> "Enable verbose output."
            | Name _ -> "Name of the project for code generation."
            | SchemaExclude _ -> "Exclude a specific schema from the output."
            | Output _ -> "Output file name for the generated schema (required)."
            | ConnectionString _ -> "Connection string to the database (optional)."
            | Codegen _ -> "Generate F# code from the schema. Format: --codegen <output_folder>"

let parser = ArgumentParser.Create<CLIArgs>(programName = "schema2fs.fsx")
let results = parser.ParseCommandLine(Environment.GetCommandLineArgs()[2..], raiseOnUsage = true)

let outputFile =
    match results.TryGetResult(<@ Output @>) with
    | Some o -> o
    | None ->
        eprintfn "Error: --output is required."
        exit 1

let connectionString =
    match results.TryGetResult(<@ ConnectionString @>) with
    | Some cs -> cs
    | None ->
        if File.Exists("connection_string.txt") then
            File.ReadAllText("connection_string.txt")
        else
            eprintfn "Error: --connectionstring not provided and connection_string.txt not found."
            exit 1

let schemaExcludes = 
    results.TryGetResult <@ SchemaExclude @> 
    |> Option.defaultValue "" 
    |> fun x -> x.Split([|','|]) 
    |> Array.choose (fun s -> match s.Trim() with | "" -> None | trimmed -> Some trimmed)
    |> Set.ofArray

if not <| schemaExcludes.IsEmpty then
    let s = String.Join(";",schemaExcludes)
    printfn $"Skipping schemas {s}"

let schemaInfo = ExtractSchema.extractSchemaAndEnums schemaExcludes connectionString

let verbose = results.Contains Verbose
// TODO: Handle schema exclusions, test exclude public

if verbose then
    printfn "Extracted schema columns:"
    for col, _ in schemaInfo.ColumnsWithUdt do
        let isFk = schemaInfo.ForeignKeys |> List.exists (fun fk -> fk.Schema = col.Schema && fk.Table = col.Table && fk.Column = col.Column)
        if isFk then
            let fk = schemaInfo.ForeignKeys |> List.find (fun fk -> fk.Schema = col.Schema && fk.Table = col.Table && fk.Column = col.Column)
            let refTable =
                match fk.RefSchema with
                | Some s when s <> col.Schema -> sprintf "%s.%s" s fk.RefTable
                | _ -> fk.RefTable
            printfn "  Schema: %s, Table: %s, Column: %s, DataType: %s, IsNullable: %s, FOREIGN KEY -> %s(%s)" col.Schema col.Table col.Column col.DataType col.IsNullable refTable fk.RefColumn
        else
            printfn "  Schema: %s, Table: %s, Column: %s, DataType: %s, IsNullable: %s" col.Schema col.Table col.Column col.DataType col.IsNullable
    printfn "\nExtracted enums:"
    for KeyValue(enumName, values) in schemaInfo.EnumMap do
        printfn "  Enum: %s -> [%s]" enumName (String.concat ", " [for v in values -> v.Enum])
    printfn "\n--- Re-emitted Schema ---\n"

// Check if codegen option was specified and generate schema accordingly
let codegenOpt = results.TryGetResult Codegen
let projectName = results.GetResult Name
let projectNameClean = Common.toFSharpLower projectName
let output = 
    match codegenOpt with
    | Some outputFolder -> 
        printfn "Including codegen call for project '%s' in folder '%s'" projectName outputFolder
        ExtractSchema.generateSchemaWithCodegen schemaInfo projectNameClean outputFolder
    | None -> 
        ExtractSchema.generateSchema schemaInfo projectNameClean

File.WriteAllText(outputFile, output)


// If we want the emitted code to make SQL output, it should include this instruction
// let output = Generate.emitDatabase proteins


match codegenOpt with
| Some outputFolder ->
    printfn "\nSchema generation complete with codegen call included."
    printfn "To generate F# code, execute the generated schema file: %s" outputFile
| None ->
    printfn "Schema generation complete. Use --codegen <project> <folder> to include code generation call."
