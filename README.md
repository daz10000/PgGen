# PgGen

Describe a Postgres database once, in a small F# DSL, and generate:

- **SQL DDL**: schemas, tables, sequences, enums, foreign keys, unique constraints, comments,
  ownership, and optional row level security (RLS) tenant policies.
- **An F# backend**: typed domain records, a storage layer whose SQL is checked against the live
  database at compile time, a service layer, Giraffe web api routes, and a typed .NET client.
- **A DSL spec from an existing database** (reverse engineering), so you can adopt PgGen on a
  database you already have.

The style is opinionated (heavily influenced by [pgModeler](https://pgmodeler.io/)): integer `id`
primary keys backed by sequences, `id_<table>` foreign key columns, lower case snake case names,
and everything owned by a single application role.

## Contents

- [Installation](#installation)
- [Quick start](#quick-start)
- [The DSL](#the-dsl)
- [Generated SQL](#generated-sql)
- [Generated F# code](#generated-f-code)
- [Row level security (multi-tenant tables)](#row-level-security-multi-tenant-tables)
- [Reverse engineering an existing database](#reverse-engineering-an-existing-database)
- [Limitations and roadmap](#limitations-and-roadmap)
- [Contributing](#contributing)

## Installation

PgGen is a library. You normally drive it from an F# script (`.fsx`) that holds your schema.

From NuGet (the package id is `FSharp.PgGen`; the namespace is `PgGen`):

```fsharp
#r "nuget: FSharp.PgGen"
```

or, in a project:

```bash
dotnet add package FSharp.PgGen
```

From a local checkout (handy while changing PgGen itself):

```bash
git clone https://github.com/daz10000/PgGen && cd PgGen && dotnet build
```

```fsharp
#r "<path to PgGen>/bin/Debug/net10.0/PgGen.dll"
```

Requirements: .NET 10 SDK. Postgres is needed to run the SQL, to build the generated backend
(see [compile-time database](#the-compile-time-database)) and for reverse engineering.

## Quick start

Create `schema.fsx`:

```fsharp
#r "nuget: FSharp.PgGen"

open PgGen
open PgGen.Build

let lab =
    db "lab_db" [ Owner "read_write" ] [
        schema "lab" [ SComment "Samples and the organisms they came from" ] [
            enumDef "status" [ Member "new"; Member "in_progress"; Member "done" ]

            table "organism" [] [
                col "id" Id []
                col "name" String []
                col "id_taxon" Int32 [ Nullable ]
            ]

            table "sample" [ Comment "A physical sample" ] [
                col "id" Id []
                col "label" String [ CComment "Human readable label" ]
                col "notes" String [ Nullable ]
                col "created" Timestamp []
                frefId "organism" []      // adds id_organism int NOT NULL + FK to lab.organism(id)
                enum "status" []          // adds a status column of type lab.status
                unique [ "label" ]
            ]
        ]
    ]

// 1. SQL
System.IO.File.WriteAllText("lab.sql", Generate.emitDatabase lab)

// 2. F# backend + shared api/client projects
CodeGen.generate "acme" "generated" lab   // project name "acme"
```

Run it, create the database and load the SQL, then build the generated code:

```bash
dotnet fsi schema.fsx

# as a superuser (the generated SQL starts with these commands as comments)
psql -U postgres -c "CREATE DATABASE lab_db WITH OWNER read_write"

# as the owner
psql -U read_write -d lab_db -h 127.0.0.1 -f lab.sql

# edit generated/compile_time_db.txt if your credentials differ, then
dotnet build generated/Acme.Backend.fsproj
```

## The DSL

A spec is a tree: `db` → `schema` → `table` → columns, references and constraints. Each builder
takes a name, a list of attributes, and a list of children.

### Database and schema

```fsharp
db "name" [ Owner "role"; DComment "..." ] [ schema ... ]   // Owner defaults to "read_write"
schema "name" [ SComment "..." ] [ enumDef ...; table ... ]
```

### Tables

```fsharp
table "name" [ Comment "..."; rlsTenant "tenant_id"; rlsOrganization "org_id" ] [
    col ...; frefId ...; enum ...; unique ...; pKey ...
]
```

### Columns: `col name type modifiers`

| DSL type    | Postgres                    | F# type    |
|-------------|-----------------------------|------------|
| `Id`        | `int` primary key + sequence| `int`      |
| `Id64`      | `bigint`                    | `int64`    |
| `Int32`     | `int`                       | `int`      |
| `String`    | `text`                      | `string`   |
| `Bool`      | `boolean`                   | `bool`     |
| `Float`     | `float`                     | `float`    |
| `Decimal`   | `decimal`                   | `decimal`  |
| `Guid`      | `uuid`                      | `Guid`     |
| `Timestamp` | `timestamptz default now()` | `DateTime` |
| `Jsonb`     | `jsonb`                     | `string`   |
| `Blob`      | `bytea`                     | `byte[]`   |

Modifiers:

| Modifier        | Effect                                                     |
|-----------------|------------------------------------------------------------|
| `Nullable`      | column is nullable; F# field becomes an `option`           |
| `Array`         | Postgres array (`text []`); F# array                       |
| `CComment "x"`  | `COMMENT ON COLUMN` and an XML doc comment on the F# field |

```fsharp
col "taxonomy" String [ Nullable; Array ]
```

### Foreign keys: `frefId "table" attrs`

Adds an `int` column called `id_<table>` and a foreign key to `<table>(id)`.

```fsharp
frefId "organism" []                                // id_organism -> organism(id), same schema
frefId "core.person" [ FName "id_owner" ]           // id_owner -> core.person(id)
frefId "organism" [ FNullable ]                     // nullable reference
```

### Enums

Define the type once per schema with `enumDef`, then add enum columns to tables with `enum`:

```fsharp
enumDef "status" [ Member "new"; Member "done"; EComment "Workflow state" ]

table "task" [] [
    col "id" Id []
    enum "status" []                                // column "status" of type <schema>.status
    enum "status" [ EName "review_status"; ENullable ]
    enum "core.priority" []                         // enum defined in another schema
]
```

In F# each enum becomes a union type (`Status.New | Status.Done`), and the storage layer converts to
and from the database type.

### Constraints

```fsharp
unique [ "id_organism"; "label" ]                   // CONSTRAINT uq_<table>_<cols> UNIQUE(...)
pKey [ "id_sample"; "tag" ]                         // composite key for tables without an Id column
```

Names are checked against Postgres and F# reserved words. `Generate.emitDatabase` throws if a
column name is a Postgres keyword.

## Generated SQL

For the `sample` table above, `Generate.emitDatabase` produces (abridged):

```sql
CREATE SEQUENCE lab.sample_id_seq;
ALTER SEQUENCE lab.sample_id_seq OWNER TO read_write;

CREATE TABLE lab.sample (
    id                   int PRIMARY KEY DEFAULT
                             nextval('lab.sample_id_seq'::regclass) NOT NULL,
    label                text NOT NULL,
    notes                text,
    created              timestamptz default now() NOT NULL,
    id_organism          int NOT NULL,
    status               lab.status NOT NULL,
    CONSTRAINT uq_sample_label UNIQUE(label),
    CONSTRAINT id_organism_refers_to_organism FOREIGN KEY (id_organism) REFERENCES lab.organism(id)
);

COMMENT on TABLE lab.sample is 'A physical sample';
COMMENT on COLUMN lab.sample.label is 'Human readable label';
ALTER TABLE lab.sample OWNER TO read_write;
```

## Generated F# code

`CodeGen.generate "<project>" "<folder>" spec` writes two projects:

```
generated/
├── Directory.Packages.props         NuGet central package versions (see below)
├── compile_time_db.txt              connection string used while compiling
├── Acme.Backend.fsproj              net10.0: database, services, web server
├── Common/
│   ├── Common.fs                    shared types (BatchOffset)            [shared]
│   └── Db.fs                        connection, transactions, RLS helpers [backend]
├── Acme.Domain.Lab/                 one folder per schema
│   ├── LabDomain.fs                 records: Sample, CreateSample, UpdateSample, enums  [shared]
│   ├── LabStorage.fs                create/read/update/list, SQL checked at compile time
│   └── LabService.fs                business logic goes here (thin wrappers to start)
└── Api/
    ├── AcmeApi.fsproj               netstandard2.0 shared project: domain + contract + client
    ├── LabApi.fs                    Api contract record + typed Client          [shared]
    ├── LabApiServer.fs              HTTP routes for the Api                     [backend]
    ├── LabApiWireup.fs              Api record implemented by the service layer [backend]
    ├── AcmeServerApi.fs             all schemas' apis in one record             [backend]
    └── AcmeClientApi.fs             root client with one member per schema      [shared]
```

**Shared project** (`Api/<Project>Api.fsproj`). Holds the domain records, the api contract and
the client. It targets `netstandard2.0` and depends only on the Plough contract packages, with no
Npgsql, Giraffe or type providers, so front ends and other services can reference it.
The backend references it, so both sides use the same types.

**Backend project** (`<Project>.Backend.fsproj`). Holds storage, services and server routes.
Reference it from your server application.

Each table gets these routes under `/<schema>`. The generated client expects them to be mounted
under `/api`, so a full path looks like `/api/lab/sample/read/42`.

| Route                       | Method | Notes                                     |
|-----------------------------|--------|-------------------------------------------|
| `/<table>/create`           | POST   | body: `Create<Table>`; returns the new id |
| `/<table>/read/{id}`        | GET    | POST `/<table>/read` for composite keys   |
| `/<table>/update`           | POST   | body: `Update<Table>`                     |
| `/<table>/delete`           | POST   | not implemented yet                       |
| `/<table>/list?offset=&batchSize=` | GET | paging is optional                     |

Using it at runtime:

```fsharp
// backend startup
Acme.Db.Db.connectionString <- Some "Host=...;Database=lab_db;Username=...;Password=..."
let labRoutes = Acme.Api.Lab.Server.build Acme.Api.Server.api.Lab   // mount under /api in your Giraffe app

// .NET client (create the ApiClient with Plough.WebApi.Client.Dotnet)
let client = Acme.Api.Acme(apiClient)
let sample = client.Lab.ReadSample 42      // TaskEither<Sample option>
```

Pick a project name that differs from your schema names. The root client type is named after the
project, and each schema gets a namespace with its own name.

### The compile-time database

The storage layer uses [FSharp.Data.Npgsql](https://github.com/demetrixbio/FSharp.Data.Npgsql),
a type provider that checks every SQL statement against a real database during compilation. To
build the backend, the database in `compile_time_db.txt` must exist and contain the current
schema. That means you load the generated SQL before building. The shared project does not need a
database.

### Package management

Generated projects use [NuGet Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management):
the `.fsproj` files list `<PackageReference Include="..." />` without versions, and the versions
live in `Directory.Packages.props`.

- If no `Directory.Packages.props` exists in the output folder or any parent folder, PgGen writes
  one. It is marked as generated and is overwritten on every run.
- If your repository already has one, PgGen leaves it alone and prints the `<PackageVersion>`
  lines it needs, so you can merge them.
- Older PgGen versions generated paket files. PgGen warns if it finds them; they can be deleted.

## Row level security (multi-tenant tables)

A table is tenant scoped if it has a `tenant_id` column or an explicit `rlsTenant` attribute. An
organization column adds a second level of isolation. It is detected from an `organization_id`
column or set with `rlsOrganization`.

```fsharp
table "invoice" [ rlsTenant "tenant_id"; rlsOrganization "organization_id" ] [
    col "id" Id []
    col "tenant_id" Guid []
    col "organization_id" Guid []
    col "amount" Decimal []
]
```

The generated SQL enables and forces RLS and adds a policy driven by session settings:

```sql
ALTER TABLE lab.invoice ENABLE ROW LEVEL SECURITY;
ALTER TABLE lab.invoice FORCE ROW LEVEL SECURITY;
CREATE POLICY invoice_tenant_isolation ON lab.invoice
    USING (tenant_id::text = current_setting('app.tenant_id', true)
    AND
    (current_setting('app.organization_id', true) = '' OR organization_id::text = current_setting('app.organization_id', true)))
    WITH CHECK (...same predicate...);
```

The generated `Db.fs` has a `TenantRls` module that holds the current tenant in an `AsyncLocal`.
Every generated storage function applies it to its connection:

```fsharp
open Lab.Db
TenantRls.setAmbientFromValues tenantId organizationId   // e.g. in request middleware
// ... storage / service calls now only see that tenant's rows ...
TenantRls.clearAmbient ()
```

## Reverse engineering an existing database

`schema2fs.fsx` reads an existing database and writes a PgGen spec. It is experimental.

```bash
dotnet fsi schema2fs.fsx --name proteins --output proteins.fsx \
    --connectionstring "Host=localhost;Database=proteins;Username=postgres;Password=postgres"
```

| Option                     | Meaning                                                    |
|----------------------------|------------------------------------------------------------|
| `--name`                   | project name (required)                                    |
| `--output`                 | spec file to write (required)                              |
| `--connectionstring`       | otherwise read from `connection_string.txt`                |
| `--schemaexclude a,b`      | schemas to skip                                            |
| `--codegen <folder>`       | append a `CodeGen.generate` call to the spec               |
| `--verbose true`           | print the extracted columns and enums                      |

The same functionality is available from code as `PgGen.ExtractSchema.extractSchemaAndEnums` and
`ExtractSchema.generateSchema`.

## Limitations and roadmap

- Delete operations are generated as stubs, and there is no soft delete yet.
- Nullable `Bool`, `Guid`, `Blob` and `Id64` columns, and nullable arrays, are not yet supported by
  the storage layer generator. The SQL is fine.
- Foreign keys are always integer references to `id`.
- `FComment`, `ERComment` and `CUpdate` are accepted but not emitted yet.
- There is no index support, and no migrations: PgGen generates `CREATE` scripts.
- The generated web layer uses the Plough libraries, which are no longer maintained. Replacing
  them is being investigated; see [docs/plough-removal.md](docs/plough-removal.md).

## Contributing

Start with [PLATFORM.md](PLATFORM.md), the architecture and maintenance guide.

## License

Apache 2.0
