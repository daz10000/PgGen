module PgGen.ExtractSchema

open Npgsql
open System
open System.Text.RegularExpressions

type ColumnInfo = {
    Schema: string
    Table: string
    Column: string
    DataType: string
    IsNullable: string
    UDTName: string
}

type ForeignKeyInfo = {
    Schema: string
    Table: string
    Column: string
    RefSchema: string option
    RefTable: string
    RefColumn: string
}

type EnumSchema = {
    Enum : string 
    Schema : string}

type ExtractedSchema = {
    ColumnsWithUdt: (ColumnInfo * string option) list
    EnumMap: Map<string, EnumSchema list>
    PkSeqCols: (string * string * string) list
    ForeignKeys: ForeignKeyInfo list
}

/// Extracts the schema, enum definitions, and primary key/sequence info from a PostgreSQL database connection string.
let extractSchemaAndEnums (schemaExclude: Set<string>) (connectionString: string) =
    let schemaSkips = 
        schemaExclude 
        |> Seq.map (fun s -> $",'{s}'") 
        |> String.concat ""

    // Extract columns and enum type names
    let columnsWithUdt, enumTypes, pkSeqCols =
        let columnsWithEnums, enumTypes, pkSeqCols =
            // Block for first connection
            let columnsWithEnums, enumTypes =
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()
                use cmd = new NpgsqlCommand($"""
                    SELECT table_schema, table_name, column_name, data_type, is_nullable, udt_name
                    FROM information_schema.columns
                    WHERE table_schema NOT IN ('pg_catalog', 'information_schema'{schemaSkips})
                    ORDER BY table_schema, table_name, ordinal_position """, conn)
                use reader = cmd.ExecuteReader()
                let columnsWithEnums =
                    [ while reader.Read() do
                        let colInfo = {
                            Schema = reader.GetString(0)
                            Table = reader.GetString(1)
                            Column = reader.GetString(2)
                            DataType = reader.GetString(3)
                            IsNullable = reader.GetString(4)
                            UDTName = reader.GetString(5)
                        }
                        let udtNameOpt = 
                            if reader.IsDBNull(3) then None else Some(reader.GetString(5)) 
                        yield (colInfo, udtNameOpt)
                    ]
                let enumTypes =
                    columnsWithEnums
                    |> List.choose (fun (colInfo, udtNameOpt) ->
                        match udtNameOpt with
                        | Some udt when colInfo.DataType = "USER-DEFINED" -> Some udt
                        | _ -> None
                    )
                    |> List.distinct
                columnsWithEnums, enumTypes
            // Block for sequence columns
            let pkSeqCols =
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()
                use seqCmd = new NpgsqlCommand("""
                    SELECT c.table_schema, c.table_name, c.column_name
                    FROM information_schema.columns c
                    JOIN information_schema.tables t ON c.table_schema = t.table_schema 
                    AND c.table_name = t.table_name
                    WHERE (
                        c.is_identity = 'YES' OR
                        c.column_default LIKE 'nextval(%'
                    )
                    AND t.table_type = 'BASE TABLE'
                    AND c.table_schema NOT IN ('pg_catalog', 'information_schema'"""+schemaSkips+")", conn)
                use seqReader = seqCmd.ExecuteReader()
                [ while seqReader.Read() do
                    let schema = seqReader.GetString(0)
                    let table = seqReader.GetString(1)
                    let col = seqReader.GetString(2)
                    yield (schema, table, col)
                ]
            columnsWithEnums, enumTypes, pkSeqCols
        columnsWithEnums, enumTypes, pkSeqCols
    // Now open a new connection for enum value extraction
    let enumMap =
        if List.isEmpty enumTypes then Map.empty
        else
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()
            enumTypes
            |> List.map (fun enumType ->
                // JOIN   pg_catalog.pg_namespace n
                // n.nspname as schema
                //  n.oid = t.typnamespace
                use enumCmd = new NpgsqlCommand($"""
                                    SELECT enumlabel,n.nspname as schema FROM pg_enum 
                                    JOIN pg_type ON pg_enum.enumtypid = pg_type.oid 
                                    JOIN pg_catalog.pg_namespace n on n.oid = pg_type.typnamespace
                                    WHERE pg_type.typname = '{enumType}' ORDER BY enumsortorder""", conn)
                use enumReader = enumCmd.ExecuteReader()
                let values = [ while enumReader.Read() do yield { Enum = enumReader.GetString(0); Schema = enumReader.GetString(1) }]
                enumType, values
            )
            |> Map.ofList
    // Foreign key extraction
    let foreignKeys =
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        use fkCmd = new NpgsqlCommand("""
            SELECT
                kcu.table_schema, kcu.table_name, kcu.column_name,
                ccu.table_schema AS foreign_table_schema,
                ccu.table_name AS foreign_table_name,
                ccu.column_name AS foreign_column_name
            FROM information_schema.key_column_usage AS kcu
            JOIN information_schema.referential_constraints AS rc
                ON kcu.constraint_catalog = rc.constraint_catalog
                AND kcu.constraint_schema = rc.constraint_schema
                AND kcu.constraint_name = rc.constraint_name
            JOIN information_schema.constraint_column_usage AS ccu
                ON rc.unique_constraint_catalog = ccu.constraint_catalog
                AND rc.unique_constraint_schema = ccu.constraint_schema
                AND rc.unique_constraint_name = ccu.constraint_name
            WHERE kcu.table_schema NOT IN ('pg_catalog', 'information_schema'"""+schemaSkips+""")
        """, conn)
        use fkReader = fkCmd.ExecuteReader()
        [ while fkReader.Read() do
            yield {
                Schema = fkReader.GetString(0)
                Table = fkReader.GetString(1)
                Column = fkReader.GetString(2)
                RefSchema =
                    let s = fkReader.GetString(3)
                    if s = "" then None else Some s
                RefTable = fkReader.GetString(4)
                RefColumn = fkReader.GetString(5)
            }
        ] 
    { ColumnsWithUdt = columnsWithUdt; EnumMap = enumMap; PkSeqCols = pkSeqCols; ForeignKeys = foreignKeys }

/// Generates a schema definition (as F# code or other format) from the extracted schema and enums.
let generateSchema (schema: ExtractedSchema) (projectName:string) =
    let header = """#r "bin/Debug/net9.0/PgGen.dll"
open PgGen
open PgGen.Build

"""
    let quote s = sprintf "\"%s\"" s
    let body =
        schema.ColumnsWithUdt
        |> List.groupBy (fun (c, _) -> c.Schema)
        |> List.map (fun (schemaName, columns) ->
            let enumDefs =
                // enumMap is a map of enum name -> list of values
                // values have associated schema, so we want to make sure we only define
                // enums whose values are in the current schema
                // user can refer to enums in non local schmemas in which case we need to
                // qualify the schema location where the enum is defined
                schema.EnumMap
                |> Map.filter (fun _k v -> v |> List.exists (fun v -> v.Schema = schemaName))
                |> Map.toList
                |> List.map (fun (enumType, values) ->
                    let members = values |> List.map (fun v -> sprintf "Member %s" (quote v.Enum)) |> String.concat "; "
                    sprintf "enumDef %s [ %s ]" (quote enumType) members
                )
                |> String.concat "\n    "
            let tables =
                columns
                |> List.groupBy (fun (c, _) -> c.Table)
                |> List.map (fun (tableName, tableColumns) ->
                    let colLines =
                        tableColumns
                        |> List.map (fun (c, udtNameOpt) ->
                            let isPkSeq = schema.PkSeqCols |> List.exists (fun (s, t, col) -> s = c.Schema && t = c.Table && col = c.Column)
                            let fkOpt = schema.ForeignKeys |> List.tryFind (fun fk ->
                                fk.Schema = c.Schema && fk.Table = c.Table && fk.Column = c.Column
                            )
                            let mapType2 (c: ColumnInfo) =
                                match c.UDTName with
                                | "timestamptz" -> "Timestamp"
                                | "timestamp with time zone" -> "Timestamp"
                                | "int8" -> "Int32"
                                | "float4" -> "Float"
                                | "timestamp" -> "Timestamp"
                                | "numeric" -> "Decimal"
                                | "decimal" -> "Decimal"
                                | "bool" -> "Bool"
                                | "int4" -> "Int32"
                                | "varchar" -> "String"
                                | "float8" -> "Float"
                                | "_float8" -> "Float"
                                | "json" -> "Jsonb"
                                | "text" -> "String"
                                | x when c.DataType = "USER-DEFINED" -> 
                                    // Enum type
                                    x
                                | _ -> 
                                    failwithf "Unknown type %s for column %s, table %s, schema %s udtOpt=%A" c.DataType c.Column c.Table c.Schema udtNameOpt

                            // TODO: replace function below with one above
                            // handle ARRAY column
                            // integratie array, enum and regular responses

                            let mapType (typ: string) =
                                match typ.Trim().ToLowerInvariant() with
                                | "character varying" | "varchar" | "text" | "char" -> "String"
                                | "int" | "integer" | "int4" -> "Int32"
                                | "bigint" | "int8" -> "Id64"
                                | "bool" | "boolean" -> "Bool"
                                | "timestamp" | "Timestamp" | "timestamptz" 
                                | "timestamp with time zone" 
                                | "timestamp without time zone" 
                                    -> "Timestamp"
                                | "jsonb" -> "Jsonb"
                                | "json" -> "Jsonb"
                                | "float" | "float4" | "float8" | "double precision" | "real" -> "Float"
                                | "uuid" -> "Guid"
                                | "bytea" -> "Blob"
                                | "decimal" | "numeric" -> "Decimal"
                                | _ as x -> 
                                    failwithf $"Unknown type {x} for column {c}, table {c.Table}, schema {c.Schema} udtOpt={udtNameOpt}"
                            let colLine =
                                match fkOpt with
                                | Some fk ->
                                    let refTable =
                                        match fk.RefSchema with
                                        | Some s when s <> c.Schema -> sprintf "%s.%s" s fk.RefTable
                                        | _ -> fk.RefTable
                                    let attrs =
                                        let baseAttrs =
                                            [if c.IsNullable.Trim().ToUpperInvariant() = "YES" then yield "FNullable"]
                                        let arrAttrs =
                                            if c.DataType.EndsWith("[]") then ["Array"] else []
                                        let allAttrs = baseAttrs @ arrAttrs
                                        if List.isEmpty allAttrs then "[]"
                                        else sprintf "[%s]" (String.concat "; " allAttrs)
                                    sprintf "frefId %s %s" (quote refTable) attrs
                                | None ->
                                    if c.DataType = "USER-DEFINED" then
                                        match udtNameOpt with
                                        | Some udtName ->
                                            let attrs =
                                                let baseAttrs =
                                                    [if c.IsNullable.Trim().ToUpperInvariant() = "YES" then yield "ENullable"]
                                                let arrAttrs =
                                                    if c.DataType.EndsWith("[]") then ["Array"] else []
                                                // do we need to explicitly name the column?
                                                // only if the chosen name isn't the enum type name itself
                                                let nameAttr =
                                                    if udtName = c.Column then []
                                                    else [$"EName {quote c.Column}"]
                                                let allAttrs = baseAttrs @ arrAttrs @ nameAttr
                                                if List.isEmpty allAttrs then "[]"
                                                else sprintf "[%s]" (String.concat "; " allAttrs)
                                            // the enum 1st argument is the name of the enum
                                            // itself.  Potentially qualified with schema
                                            // if the *column* name doesn't match the default (name of enum)
                                            // then we have to provide an additional EName attribute
                                            sprintf $"enum {quote c.UDTName} {attrs}"
                                        | None -> sprintf "enum %s [EName <unknown_enum>]" (quote c.Column)
                                    elif isPkSeq then
                                        sprintf "col %s Id []" (quote c.Column)
                                    else
                                        let mappedType = mapType2 c
                                        let attrs =
                                            let baseAttrs =
                                                [if c.IsNullable.Trim().ToUpperInvariant() = "YES" then yield "Nullable"]
                                            let arrAttrs = if c.DataType = "ARRAY" then ["Array"] else []
                                                //if c.DataType.EndsWith("[]") then ["Array"] else []
                                            let allAttrs = baseAttrs @ arrAttrs
                                            if List.isEmpty allAttrs then "[]"
                                            else sprintf "[%s]" (String.concat "; " allAttrs)
                                        sprintf "col %s %s%s" (quote c.Column) mappedType (if attrs = "[]" then " []" else " " + attrs)
                            colLine
                        )
                        |> String.concat "\n        "
                    sprintf "table %s [] [\n        %s\n    ]" (quote tableName) colLines
                )
                |> String.concat "\n    "
            let body =
                [enumDefs; tables]
                |> List.filter (fun s -> s <> "")
                |> String.concat "\n    "
            sprintf "schema %s [] [\n    %s\n]" (quote schemaName) body
        )
        |> String.concat "\n\n"

    // Need to indent the body by 8 spaces
    let bodyIndented = Regex.Replace(body, @"^", "        ", RegexOptions.Multiline)
    let projectNameClean = Common.toFSharpLower projectName
    $"""{header}
let {projectNameClean}Db = 
    db "{projectNameClean.ToLower()}" [ Owner "read_write" ] [
{bodyIndented}
    ]
"""

/// Generates a schema definition with optional codegen call
let generateSchemaWithCodegen (schema: ExtractedSchema) projectName outputFolder =
    let baseSchema = generateSchema schema projectName
    let projectNameClean = Common.toFSharpLower projectName
    let codeGenInstructions = sprintf "CodeGen.generate \"%s\" \"%s\" %sDb" projectNameClean outputFolder projectName
    $"{baseSchema}\n{codeGenInstructions}"
