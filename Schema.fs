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

type ExtractedSchema = {
    ColumnsWithUdt: (ColumnInfo * string option) list
    EnumMap: Map<string, string list>
    PkSeqCols: (string * string * string) list
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
    { ColumnsWithUdt = columnsWithUdt; EnumMap = enumMap; PkSeqCols = pkSeqCols }

/// Generates a schema definition (as F# code or other format) from the extracted schema and enums.
let generateSchema (schema: ExtractedSchema) =
    let quote s = sprintf "\"%s\"" s
    let enumDefs =
        schema.EnumMap
        |> Map.toList
        |> List.map (fun (enumType, values) ->
            let members = values |> List.map (fun v -> sprintf "Member %s" (quote v)) |> String.concat "; "
            sprintf "enumDef %s [ %s ]" (quote enumType) members
        )
        |> String.concat "\n    "
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
                        let colType =
                            if c.DataType = "USER-DEFINED" then
                                match udtNameOpt with
                                | Some udtName when udtName = c.Column -> "enum []"
                                | Some udtName -> sprintf "enum [EName %s]" (quote udtName)
                                | None -> "enum []"
                            elif isPkSeq then
                                "Id []"
                            else
                                sprintf "%s [%s]" c.DataType (if c.IsNullable = "YES" then "Nullable" else "")
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
