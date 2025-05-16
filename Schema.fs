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

/// Extracts the schema from a PostgreSQL database connection string.
let extractSchema (connectionString: string) =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    // Example: Query information_schema.tables and information_schema.columns
    use cmd = new NpgsqlCommand("""
        SELECT table_schema, table_name, column_name, data_type, is_nullable
        FROM information_schema.columns
        WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
        ORDER BY table_schema, table_name, ordinal_position
    """, conn)
    use reader = cmd.ExecuteReader()
    let schema =
        [ while reader.Read() do
            yield {
                Schema = reader.GetString(0)
                Table = reader.GetString(1)
                Column = reader.GetString(2)
                DataType = reader.GetString(3)
                IsNullable = reader.GetString(4)
            }
        ]
    schema

/// Generates a schema definition (as F# code or other format) from the extracted schema.
let generateSchema (schema: ColumnInfo list) =
    schema
    |> List.groupBy (fun c -> c.Schema)
    |> List.map (fun (schemaName, columns) ->
        let tables =
            columns
            |> List.groupBy (fun c -> c.Table)
            |> List.map (fun (tableName, tableColumns) ->
                let colLines =
                    tableColumns
                    |> List.map (fun c ->
                        sprintf "col %s %s [%s]" c.Column c.DataType (if c.IsNullable = "YES" then "Nullable" else "")
                    )
                    |> String.concat "\n        "
                sprintf "table %s [] [\n        %s\n    ]" tableName colLines
            )
            |> String.concat "\n    "
        sprintf "schema %s [] [\n    %s\n]" schemaName tables
    )
    |> String.concat "\n\n"
