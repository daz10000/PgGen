#r "nuget: Argu, 6.2.0"
#r "bin/Debug/net9.0/PgGen.dll"

open System
open System.IO
open Argu
open PgGen
open PgGen.ExtractSchema

// Define CLI arguments
[<CliPrefix(CliPrefix.DoubleDash)>]
type CLIArgs =
    | Output of output:string
    | ConnectionString of conn:string
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Output _ -> "Output file name for the generated schema (required)."
            | ConnectionString _ -> "Connection string to the database (optional)."

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

let schemaInfo = ExtractSchema.extractSchemaAndEnums connectionString

let verbose = true
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
        printfn "  Enum: %s -> [%s]" enumName (String.concat ", " values)
    printfn "\n--- Re-emitted Schema ---\n"

let output = ExtractSchema.generateSchema schemaInfo
File.WriteAllText(outputFile, output)
