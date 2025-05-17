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

/// Extracts the schema and enum definitions from a PostgreSQL database connection string.
let extractSchemaAndEnums (connectionString: string) =
    // Extract columns and enum type names
    let columnsWithUdt, enumTypes =
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
    columnsWithUdt, enumMap

/// Generates a schema definition (as F# code or other format) from the extracted schema and enums.
let generateSchema (schemaWithUdt: (ColumnInfo * string option) list, enumMap: Map<string, string list>) =
    let quote s = sprintf "\"%s\"" s
    let enumDefs =
        enumMap
        |> Map.toList
        |> List.map (fun (enumType, values) ->
            let members = values |> List.map (fun v -> sprintf "Member %s" (quote v)) |> String.concat "; "
            sprintf "enumDef %s [ %s ]" (quote enumType) members
        )
        |> String.concat "\n    "
    schemaWithUdt
    |> List.groupBy (fun (c, _) -> c.Schema)
    |> List.map (fun (schemaName, columns) ->
        let tables =
            columns
            |> List.groupBy (fun (c, _) -> c.Table)
            |> List.map (fun (tableName, tableColumns) ->
                let colLines =
                    tableColumns
                    |> List.map (fun (c, udtNameOpt) ->
                        let colType =
                            if c.DataType = "USER-DEFINED" then
                                match udtNameOpt with
                                | Some udtName when udtName = c.Column -> "enum []"
                                | Some udtName -> sprintf "enum [EName %s]" (quote udtName)
                                | None -> "enum []"
                            elif c.Column = "id" then
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
