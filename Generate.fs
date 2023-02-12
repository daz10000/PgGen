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
                        yield $"    %-20s{column.CName} {column.CType.Sql()} PRIMARY KEY DEFAULT \n                             nextval('{idSeqName}'::regclass) NOT NULL"
                    else
                        let array = if column.Array then " []" else ""
                        let nullable = if column.Nullable then "" else " NOT NULL"
                        yield $"    %-20s{column.CName} {column.CType.Sql()}{array}{nullable}"

                // Emit implicitly generated foreign reference columns
                for fr in table.FRefs do
                    if fr.Generate then
                        let colName = $"id_{fr.ToTable}"
                        yield $"    %-20s{colName} int NOT NULL"

                // Unique constraints
                for unique in table.Uniques do
                    let colsConcat = String.Join("_",unique.Cols)
                    let colsConcatComma = String.Join(",",unique.Cols)
                    let name = $"uq_{table.TName}_{colsConcat}"
                    yield $"                        CONSTRAINT {name} UNIQUE({colsConcatComma}"

                // Foreign reference constraints
                for fr in table.FRefs do
                    let frCols = String.Join("_",fr.FromCols)
                    let toCols = String.Join("_",fr.ToCols)
                    let name = $"fk_{table.TName}_{frCols}_refs_{fr.ToTable}_{toCols}"
                    let toSchema = fr.ToSchema |> Option.defaultValue schema // default to current schema
                    let toTable = fr.ToTable
                    let frSchema = schema
                    let frTable = table.TName
                    yield $"    CONSTRAINT {name} FOREIGN KEY {frSchema}.{frTable}({frCols}) REFERENCES {toSchema}.{toTable}({toCols})"
            |]
        yield String.Join(",\n",cols)
        yield $"\n);\n\n"
        for attr in table.Attributes do
            match attr with
            | Comment c->
                yield $"COMMENT on {schema}.{table.TName} is '{c}';\n"
        yield $"ALTER TABLE {schema}.{table.TName} OWNER TO {owner};\n"
        yield "\n"
    }

let emitSchema owner (schema:Schema) =
    stringBuffer {
        yield "\n"
        yield $"; -------------------------------------------------------------------------\n"
        yield $"; Schema: {schema.SName}\n"
        yield $"; -------------------------------------------------------------------------\n\n"
        yield $"CREATE SCHEMA {schema.SName};\n"
        yield $"ALTER SCHEMA {schema.SName} OWNER to {owner};\n\n"
        for table in schema.Tables do
            yield emitTable owner schema.SName table
        yield "\n"
    }

let emitDatabase (d:Db) =
    stringBuffer {
        yield "; run as superuser...\n"
        yield $"CREATE DATABASE {d.DName} WITH OWNER {d.Owner};\n\n"

        yield "; \n"
        yield $"; Now connect as {d.Owner} ...\n"
        yield $"; pgsql -U {d.Owner} -d {d.DName} -h 12.0.0.1\n"
        yield "; ==================================================\n"
        yield d.Schemas |> List.map (emitSchema d.Owner)


    }
