module PgGen.ExtractSchema

open Npgsql
open System

type ColumnInfo = {
    Schema: string
    Table: string
    Column: string
    DataType: string
    IsNullable: string
}

type ForeignKeyInfo = {
    Schema: string
    Table: string
    Column: string
    RefSchema: string option
    RefTable: string
    RefColumn: string
}

type ExtractedSchema = {
    ColumnsWithUdt: (ColumnInfo * string option) list
    EnumMap: Map<string, string list>
    PkSeqCols: (string * string * string) list
    ForeignKeys: ForeignKeyInfo list
}

/// Extracts the schema, enum definitions, and primary key/sequence info from a PostgreSQL database connection string.
let extractSchemaAndEnums (connectionString: string) =
    // Extract columns and enum type names
    let columnsWithUdt, enumTypes, pkSeqCols =
        let columnsWithEnums, enumTypes, pkSeqCols =
            // Block for first connection
            let columnsWithEnums, enumTypes =
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()
                use cmd = new NpgsqlCommand("""
                    SELECT table_schema, table_name, column_name, data_type, is_nullable, udt_name
                    FROM information_schema.columns
                    WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                    ORDER BY table_schema, table_name, ordinal_position
                """, conn)
                use reader = cmd.ExecuteReader()
                let columnsWithEnums =
                    [ while reader.Read() do
                        let colInfo = {
                            Schema = reader.GetString(0)
                            Table = reader.GetString(1)
                            Column = reader.GetString(2)
                            DataType = reader.GetString(3)
                            IsNullable = reader.GetString(4)
                        }
                        let udtNameOpt = if reader.GetString(3) = "USER-DEFINED" then Some(reader.GetString(5)) else None
                        yield (colInfo, udtNameOpt)
                    ]
                let enumTypes =
                    columnsWithEnums
                    |> List.choose snd
                    |> List.distinct
                columnsWithEnums, enumTypes
            // Block for sequence columns
            let pkSeqCols =
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()
                use seqCmd = new NpgsqlCommand("""
                    SELECT c.table_schema, c.table_name, c.column_name
                    FROM information_schema.columns c
                    JOIN information_schema.tables t ON c.table_schema = t.table_schema AND c.table_name = t.table_name
                    WHERE (
                        c.is_identity = 'YES' OR
                        c.column_default LIKE 'nextval(%'
                    )
                    AND t.table_type = 'BASE TABLE'
                    AND c.table_schema NOT IN ('pg_catalog', 'information_schema')
                """, conn)
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
                use enumCmd = new NpgsqlCommand($"SELECT enumlabel FROM pg_enum JOIN pg_type ON pg_enum.enumtypid = pg_type.oid WHERE pg_type.typname = '{enumType}' ORDER BY enumsortorder", conn)
                use enumReader = enumCmd.ExecuteReader()
                let values = [ while enumReader.Read() do yield enumReader.GetString(0) ]
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
            WHERE kcu.table_schema NOT IN ('pg_catalog', 'information_schema')
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
let generateSchema (schema: ExtractedSchema) =
    let header = """#r "bin/Debug/net9.0/PgGen.dll"
open PgGen
open PgGen.Build
"""
    let quote s = sprintf "\"%s\"" s
    let enumDefs =
        schema.EnumMap
        |> Map.toList
        |> List.map (fun (enumType, values) ->
            let members = values |> List.map (fun v -> sprintf "Member %s" (quote v)) |> String.concat "; "
            sprintf "enumDef %s [ %s ]" (quote enumType) members
        )
        |> String.concat "\n    "
    let body =
        schema.ColumnsWithUdt
        |> List.groupBy (fun (c, _) -> c.Schema)
        |> List.map (fun (schemaName, columns) ->
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
                            let mapType (typ: string) =
                                match typ.Trim().ToLowerInvariant() with
                                | "character varying" | "varchar" | "text" | "char" -> "String"
                                | "int" | "integer" | "int4" -> "Int32"
                                | "bigint" | "int8" -> "Id64"
                                | "bool" | "boolean" -> "Bool"
                                | "timestamp" | "timestamp with time zone" | "timestamptz" -> "Timestamp"
                                | "jsonb" -> "Jsonb"
                                | "float" | "float4" | "float8" | "double precision" | "real" -> "Float"
                                | "uuid" -> "Guid"
                                | "bytea" -> "Blob"
                                | "decimal" | "numeric" -> "Decimal"
                                | other -> other
                            let colType =
                                if c.DataType = "USER-DEFINED" then
                                    match udtNameOpt with
                                    | Some udtName when udtName = c.Column -> "enum []"
                                    | Some udtName -> sprintf "enum [EName %s]" (quote udtName)
                                    | None -> "enum []"
                                elif Option.isSome fkOpt then
                                    let fk = fkOpt.Value
                                    let refTable =
                                        match fk.RefSchema with
                                        | Some s when s <> c.Schema -> sprintf "%s.%s" s fk.RefTable
                                        | _ -> fk.RefTable
                                    sprintf "frefId %s []" (quote refTable)
                                elif isPkSeq then
                                    "Id []"
                                else
                                    let mappedType = mapType c.DataType
                                    if c.IsNullable.Trim().ToUpperInvariant() = "YES" then
                                        sprintf "%s [Nullable]" mappedType
                                    else
                                        sprintf "%s []" mappedType
                            sprintf "col %s %s" (quote c.Column) colType
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
    header + body
