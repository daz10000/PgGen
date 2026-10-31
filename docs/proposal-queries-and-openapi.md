# Proposal: richer queries, and OpenAPI

Status (Oct 2026): **OpenAPI, layer 1 and layer 2 are implemented.** Layers 3 and 4 and soft
delete are still proposals.

Decisions taken:
- keyset paging for new operations (offset `list` kept for compatibility);
- POST with a JSON body for lookups, pages and named queries;
- a separate `/count` route;
- `openapi.json` produced with the generated code (not hand-maintained), embedded in the backend
  at build time and served at `/api/openapi.json`.

Differences from the sketches below:
- FK lookups are `POST /<table>/list_by_<fk_column>` with a `<Table>By<Fk>Page` body.
- Unique lookups are `POST /<table>/read_by_<cols>`.
- Enum values on the wire are Postgres labels.
- Named queries need non-null, single-direction sort columns.

## Where we are

Each table gets create / read-by-key / update / delete / list. List is `SELECT *` with optional
`LIMIT/OFFSET`. Consumers cannot:

- filter (e.g. "samples for organism 7 with status Done created this week");
- sort;
- find a row by anything except its primary key, such as a unique column;
- get counts;
- page efficiently through large tables (`OFFSET` reads and discards every skipped row);
- ask for joins or aggregates.

### Constraint to design around

Storage SQL goes through FSharp.Data.Npgsql, which checks each statement against the database at
compile time. That safety is worth keeping, but it means SQL must be **static text**, not built at
runtime.

The previous maintenance pass proved the key enabler: `AllParametersOptional=true` gives the type
provider `option` parameters, with `None` sent as SQL `NULL`. So one static statement can carry
*optional* filters:

```sql
SELECT ... FROM lab.sample
WHERE (@id_organism IS NULL OR id_organism = @id_organism)
  AND (@status IS NULL OR status = ANY(@status))
  AND (@created_from IS NULL OR created >= @created_from)
ORDER BY created DESC, id DESC
LIMIT @limit
```

## Recommendation: four layers, cheapest first

### 1. Zero-config lookups (generated from what the schema already says)

| Schema fact | Generated operation | Route |
|---|---|---|
| `frefId "organism"` on `sample` | `listSampleByOrganism : int * Page -> ApiResult<Sample []>` | `GET /lab/sample/by_organism/{id}` |
| `unique ["label"]` | `readSampleByLabel : string -> ApiResult<Sample option>` | `GET /lab/sample/by_label?label=` |
| every table | `countSample : unit -> ApiResult<int64>` | `GET /lab/sample/count` |

Also in this layer:

- **Keyset paging** as an alternative to offset: `?after=<last id>&limit=50`. It stays fast at any
  depth. Keep `offset` for compatibility.
- **Indexes.** Add `index [cols]` to the DSL, and index FK columns automatically. Postgres does not
  index foreign keys by default, and the by-FK lookups above would otherwise scan the table.

This covers the large majority of real screens (parent → children, lookup by natural key) with no
new DSL beyond `index`.

### 2. Named queries in the DSL (declarative, still compile-time checked)

```fsharp
table "sample" [] [
    ...
    query "search" [
        Filter [ Eq "id_organism"; AnyOf "status"; Range "created"; Prefix "label" ]
        OrderBy [ Desc "created" ]
        Paging Keyset
    ]
]
```

The generator emits:

- a request record in the shared project, with every filter optional:
  ```fsharp
  type SampleSearch = { IdOrganism : int option; Status : Status [] option
                        CreatedFrom : DateTime option; CreatedTo : DateTime option
                        LabelPrefix : string option; After : int option; Limit : int option }
  ```
- one static SQL statement using the optional-predicate pattern above, type-checked at build time;
- a storage, service, api, route and client method:
  `POST /lab/sample/query/search` → `ApiResult<Sample []>`;
- OpenAPI for all of it (see below).

`Filter` vocabulary: `Eq`, `AnyOf` (`= ANY(@arr)`), `Range` (from/to), `Prefix` (`LIKE @p || '%'`),
`Contains` (ILIKE), `IsNull`, and `JsonPath` later. Every column name is checked against the table
at generation time, so typos fail when generating, not at runtime.

Performance note: the `(@p IS NULL OR col = @p)` pattern can stop Postgres choosing the best index
when a prepared statement switches to a generic plan. Mitigations:

- keep the number of optional predicates per query small;
- or have the generator emit a few variants keyed on which filters are present;
- or set `plan_cache_mode = force_custom_plan` for these commands.

Benchmark before choosing.

### 3. SQL file escape hatch (joins, aggregates, reports)

```fsharp
schema "lab" [] [
    sqlQuery "organism_stats" "queries/organism_stats.sql"
]
```

You write the SQL, with named `@parameters`. At **generation** time PgGen asks the database for
the parameter and result column types; it already connects to databases for reverse engineering.
It then generates:

- a shared request record and result record;
- a storage function that embeds the file through the type provider (still type-checked at build
  time);
- a route, a client method, and OpenAPI.

This is the "anything else" answer without giving up typing or safety.

### 4. Generic filtering endpoint (opt-in, only if a data-grid UI needs it)

`POST /lab/sample/filter` takes a small typed filter AST (field, operator, value, sort, page),
defined in the shared project. The server:

- turns it into parameterised SQL at runtime with plain Npgsql, bypassing the type provider;
- uses a column allow-list from the schema model;
- enforces a maximum limit;
- runs under RLS as usual.

This is flexible, but it is runtime SQL and blind to indexes. Enable it per table, not globally.

### Considered and not recommended

- **PostgREST / Hasura / GraphQL as the api.**
  - Pros: powerful querying for free.
  - Cons: they bypass the F# service layer, where business rules live, and its error model. The
    typed F# client goes away. Tenant handling moves into JWT claims.
  - They could sit *beside* the generated api for internal read-only analytics if that is ever
    wanted.
- **OData.** Too heavy, and its tooling for F# types is weak.

## OpenAPI

**Recommendation: generate `openapi.json` (OpenAPI 3.1) from the PgGen model at generation time**,
alongside the code. Serve it from the backend at `GET /api/openapi.json`, and optionally add a
Swagger UI / Scalar page.

Why generate it rather than build it at runtime (`Microsoft.AspNetCore.OpenApi`, Swashbuckle,
Giraffe.OpenApi):

- PgGen already knows every route, type, nullability, enum and key. The spec is exact, with no
  reflection guesswork.
- Runtime generators describe F# `option` and unions poorly and need custom schema transformers.
  Giraffe's `choose`/`route` handlers also carry no endpoint metadata for them to read.
- No runtime dependency, and the spec can be committed and diffed in code review. That makes API
  changes visible, which matters now that non-F# clients are expected.

Type mapping:

| F# / PgGen | OpenAPI 3.1 |
|---|---|
| record | `object`, with `required` = non-option fields |
| `'T option` | `type: [<t>, "null"]` |
| enum union | `type: string, enum: [...]` |
| `int` / `int64` | `integer` + `format: int32/int64` |
| `decimal` / `float` | `number` (+ `format: decimal/double`) |
| `DateTime` | `string, format: date-time` |
| `Guid` | `string, format: uuid` |
| `byte[]` | `string, contentEncoding: base64` |
| `Jsonb` | `string` (contains JSON); later perhaps a schema from the DSL |
| errors | shared `Problem` schema on 400 / 404 / 409 / 500 responses |

What it enables:

- TypeScript types and clients via `openapi-typescript`, `orval` or `openapi-generator`, for any
  front end;
- Swagger or Scalar docs;
- contract tests: the smoke test could validate live responses against the spec.

It is independent of the query work and small, roughly a few hundred lines in a new
`OpenApi.fs`. Each later query layer adds paths.

## Related: soft delete

Hard delete is now implemented. A soft-delete option fits the same model:

- `softDelete` table attribute → `deleted_at timestamptz` column;
- delete becomes `UPDATE ... SET deleted_at = now()`;
- reads, lists and queries add `deleted_at IS NULL`;
- an optional `restore` operation;
- unique constraints become partial (`WHERE deleted_at IS NULL`).

## Suggested order

1. **OpenAPI generation.** It describes what exists today and unblocks non-F# front ends.
2. **Layer 1** (FK / unique lookups, counts, keyset paging, indexes).
3. **Layer 2** (named queries), after agreeing the DSL vocabulary.
4. **Layer 3** (SQL files) when the first report-style need appears.
5. **Layer 4** only for a concrete data-grid requirement.
6. Soft delete whenever a table needs it.

## Decisions needed

- Paging: make keyset the default for new operations? Keep `offset` everywhere for compatibility?
- Named query transport: `POST` with a JSON body (proposed; handles arrays and ranges cleanly) or
  `GET` with a query string (cacheable and bookmarkable)?
- Counts: on demand via `/count` (proposed), or optionally returned with list pages (costs a
  second query)?
- OpenAPI: commit the generated `openapi.json`, or generate it at build time only?
