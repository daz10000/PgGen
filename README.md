# PgGen

## Intro
Postgres schema generation, following my own idiosyncratic style (heavily influenced by [PgModeler}(https://pgmodeler.io/)

## Features

Generates SQL for
    - database creation
    - schema creation
    - simple table creation
    - foreign key references, unique constraints
    - optional Row Level Security (RLS) policy scaffolding for tenant-isolated tables
    - CRUD operations
    - Asp.Net / Plough web api endpoints
    - ambient tenant RLS session helpers in generated Db module

## Tenant RLS support

PgGen now supports emitting tenant RLS policies and runtime helper hooks.

Mark a table as tenant-scoped with either:

- a conventional `tenant_id` column name (auto-detected), or
- an explicit table attribute via `rlsTenant "my_tenant_col"`

Optional organization scoping can be added with `rlsOrganization "organization_id"`.

Example:

```fsharp
table "invoice" [ rlsTenant "tenant_id"; rlsOrganization "organization_id" ] [
    col "id" Id []
    col "tenant_id" Guid []
    col "organization_id" Guid [Nullable]
    col "amount" Decimal []
]
```

Generated SQL includes:

- `ALTER TABLE ... ENABLE ROW LEVEL SECURITY`
- `ALTER TABLE ... FORCE ROW LEVEL SECURITY`
- tenant isolation policy using `current_setting('app.tenant_id', true)`
- optional organization isolation using `current_setting('app.organization_id', true)`

Generated Db code includes `TenantRls` helpers:

- `setAmbient` / `clearAmbient`
- `setAmbientFromValues`
- `applyAmbientToConnection`

Generated storage functions call `applyAmbientToConnection` automatically after opening a connection.

Reverse engineering of existing databases into Pggen speci
    - take 

```FSharp

let db =
    db "proteins" [ Owner "read_write" ] [
        schema "enzyme" [] [
                table "uniprot_entry" [] [
                                col "id" Id []
                                col "name" String []
                                col "common_name" String [Nullable]
                                col "accno" String []
                                col "secondary" String [Array]
                            ]
                table "organism" [] [
                            col "id"  Id []
                            col "name"  String []
                            col "id_taxon"  Int32 [Nullable]
                            col "common_name"  String [Nullable]
                            col "taxonomy"  String [Nullable ; Array]
                        ]

                table "uniprot_data" [ Comment "largely json structured data"] [
                    col "id" Id []
                    col "keywords" Jsonb [] 
                    col "genes" Jsonb []
                    col "comments" Jsonb []
                    col "features" Jsonb []
                    frefId "uniprot_entry" // add an id_uniprot_entry reference to table proteins.uniprot_entry.id
                ] ] ]

let output = Generate.emitDatabase db

printfn $"{output}"

```

```
+------------------+--------------------------+-------------------------------------------------------------------+
| Column           | Type                     | Modifiers                                                         |
|------------------+--------------------------+-------------------------------------------------------------------|
| id               | integer                  |  not null default nextval('enzyme.uniprot_data_id_seq'::regclass) |
| keywords         | jsonb[]                  |  not null                                                         |
| refs             | jsonb[]                  |  not null                                                         |
| comments         | jsonb[]                  |  not null                                                         |
| genes            | jsonb[]                  |  not null                                                         |
| features         | jsonb[]                  |  not null                                                         |
| created          | timestamp with time zone |  not null default now()                                           |
| updated          | timestamp with time zone |  not null default now()                                           |
| id_uniprot_entry | integer                  |  not null                                                         |
+------------------+--------------------------+-------------------------------------------------------------------+
```

## Reverse engineering an existing database

This is experimental, but if you have an existing database and want to generate a PgGen spec, the script `schema2fs.fsx` can make a spec.

### Usage:

```bash
dotnet fsi schema2fs.fsx [--connectionstring <connection_string>] --output <output_file.fsx>
```

If `connectionstring` is not provided, you must have a `connection_string.txt` file in the current directory with the connection string.

Example:
```bash

dotnet fsi schema2fs.fsx --connectionstring "Host=localhost;Port=5432;Database=proteins;Username=postgres;Password=postgres" --output schema.fsx
```

## Todo

- finish CRUD operations
    - update
    - delete
        - soft delete schemes?
    - list all?
    - no update fields (e.g. created)
    - ambient inputs
        - tenant id 
        - user id (not from update operations)
    - dapper / plough query api

- support for indices
- more field types
