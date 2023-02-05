module PgGen.Generate

open System
open PgGen.StringBuffer

let emitTable owner schema (table:Table) =
    stringBuffer {
        let hasId = table.Cols |> List.exists (fun c -> c.CType = Id)
        let idSeqName = $"{schema}.{table.TName}_id_seq"
        if hasId then
            yield $"CREATE SEQUENCE {idSeqName};\n"
            yield $"ALTER SEQUENCE {idSeqName} OWNER TO {owner};\n\n"
        yield $"CREATE TABLE {schema}.{table.TName} (\n"
        let cols =
            [|  for column in table.Cols do
                    if column.CType = Id then
                        yield $"    %-20s{column.CName} {column.CType.Sql()} PRIMARY KEY DEFAULT nextval('{idSeqName}'::regclass) NOT NULL"
                    else
                        let array = if column.Array then " []" else ""
                        let nullable = if column.Nullable then "" else " NOT NULL"
                        yield $"    %-20s{column.CName} {column.CType.Sql()}{array}{nullable}"
            |]
        yield String.Join(",\n",cols)
        yield $"\n);\n"
        yield $"ALTER TABLE {schema}.{table.TName} OWNER TO {owner};\n"
        yield "\n"
    }

let emitSchema owner (schema:Schema) =
    stringBuffer {
        yield "\n"
        yield $"; -------------------------------------------------------------------------\n"
        yield $"; Schema: {schema.SName}\n"
        yield $"; -------------------------------------------------------------------------\n"
        yield $"CREATE SCHEMA {schema.SName};\n"
        yield $"ALTER SCHEMA {schema.SName} OWNER to {owner};\n\n"
        for table in schema.Tables do
            yield emitTable owner schema.SName table
        yield "\n"
    }

let emitDatabase (d:Db) =
    stringBuffer {
        yield "; run as superuser...\n"
        yield $"CREATE DATABASE {d.DName} WITH OWNER {d.Owner};\n"

        yield "; \n"
        yield $"; Now connect as {d.Owner} ...\n"
        yield $"; pgsql -U {d.Owner} -d {d.DName} -h 12.0.0.1\n"
        yield "; ==================================================\n"
        yield d.Schemas |> List.map (emitSchema d.Owner)


    }
