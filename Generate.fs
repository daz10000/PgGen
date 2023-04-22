module PgGen.Generate

open System
open PgGen.StringBuffer

let cleanString (s:string) = s.Replace("'","''")
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
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}"
                        let nullable = if fr.IsNullable then "" else " NOT NULL"
                        yield $"    %-20s{colName} int{nullable}"
                // Emit references to columns defined as enum references
                for er in table.ERefs do
                    let colName = er.Name |> Option.defaultValue $"{er.EName}" // name of enum itself unless overridden
                    let schema = match er.ESchema with | Some s -> s | None -> schema
                    let typeName = $"{schema}.{er.EName}" // seems to need to be fully qualified
                    let nullable = if er.IsNullable then "" else " NOT NULL"
                    yield $"    %-20s{colName} {typeName}{nullable}"

                // Unique constraints
                for unique in table.Uniques do
                    let colsConcat = String.Join("_",unique.Cols)
                    let colsConcatComma = String.Join(",",unique.Cols)
                    let name = $"uq_{table.TName}_{colsConcat}"
                    yield $"    CONSTRAINT {name} UNIQUE({colsConcatComma})"

                // Foreign reference constraints
                for fr in table.FRefs do
                    let frCols = String.Join("_",fr.FromCols)
                    let toCols = String.Join("_",fr.ToCols)
                    let name = $"{frCols}_refers_to_{fr.ToTable}"
                    let toSchema = fr.ToSchema |> Option.defaultValue schema // default to current schema
                    let toTable = fr.ToTable
                    yield $"    CONSTRAINT {name} FOREIGN KEY ({frCols}) REFERENCES {toSchema}.{toTable}({toCols})"
            |]
        yield String.Join(",\n",cols)
        yield $"\n);\n\n"
        match table.Comment with
        | Some c ->
                yield $"COMMENT on TABLE {schema}.{table.TName} is '{cleanString c}';\n"
        | None -> ()

        for column in table.Cols do
            match column.Comment with
            | Some c ->
                yield $"COMMENT on COLUMN {schema}.{table.TName}.{column.CName} is '{cleanString c}';\n"
            | None -> ()
        yield $"ALTER TABLE {schema}.{table.TName} OWNER TO {owner};\n"
        yield "\n"
    }

let emitEnum (owner:string) (schema:string) (e:PEnum) =
    stringBuffer {
        yield $"CREATE TYPE {schema}.{e.EName} AS ENUM (\n"
        yield String.Join(",\n",e.EValues |> List.map (fun v -> $"    '{v}'"))
        yield $"\n);\n\n"
        match e.EComment with
        | Some c ->
            yield $"COMMENT on TYPE {schema}.{e.EName} is '{cleanString c}';\n"
        | None -> ()
        yield $"ALTER TYPE {schema}.{e.EName} OWNER TO {owner};\n"
        yield "\n"
    }
let emitSchema owner (schema:Schema) =
    stringBuffer {
        yield "\n"
        yield $"-- -------------------------------------------------------------------------\n"
        yield $"-- Schema: {schema.SName}\n"
        yield $"-- -------------------------------------------------------------------------\n\n"
        yield $"CREATE SCHEMA {schema.SName};\n"
        yield $"ALTER SCHEMA {schema.SName} OWNER to {owner};\n\n"
        match schema.Comment with
        | Some c ->
            yield $"COMMENT ON SCHEMA {schema.SName} is '{cleanString c}';\n\n"
        | None -> ()

        for e in schema.Enums do
            yield emitEnum owner schema.SName e
        for table in schema.Tables do
            yield emitTable owner schema.SName table
        yield "\n"
    }

let emitDatabase (d:Db) =
    let errs = [|
        for schema in d.Schemas do
            for t in schema.Tables do
                for c in t.Cols do
                    if Reserved.isReserved c.CName then
                        yield $"ERROR: {c.CName} is a reserved keyword in postgres"
                for f in t.FRefs do
                    match f.Name with
                    | None -> () // do nothing
                    | Some fn ->
                        if Reserved.isReserved fn then
                            yield $"ERROR: {fn} used for foreign table column name is a reserved keyword in postgres"
                for e in t.ERefs do
                    match e.Name with
                    | None -> () // do nothing
                    | Some en ->
                        if Reserved.isReserved en then
                            yield $"ERROR: {en} used for enum column reference name is a reserved keyword in postgres"
    |]
    if errs.Length > 0 then
        for e in errs do
            printfn $"{e}"
        exit 1

    stringBuffer {
        yield "-- ==================================================\n"
        yield "--  run as superuser...\n"
        yield "-- \n"
        yield $"--  CREATE DATABASE {d.DName} WITH OWNER {d.Owner};\n\n"
        match d.Comment with
        | Some c ->
            yield $"--  COMMENT ON DATABASE {d.DName} is '{cleanString c}';\n\n"
        | None -> ()

        yield "\n"
        yield $"-- Now connect as {d.Owner} ...\n"
        yield $"-- psql -U {d.Owner} -d {d.DName} -h 127.0.0.1\n"
        yield $"-- or just run sql.."
        yield $"-- psql -U {d.Owner} -d {d.DName} -h 127.0.0.1 -f output.sql\n"
        yield "-- ==================================================\n"
        yield d.Schemas |> List.map (emitSchema d.Owner)


    }
