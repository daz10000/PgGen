


module PgGen.CodeGen

open System.IO
open PgGen.StringBuffer
open Common
open System
open PgGen.CodeStatic
open PgGen.ApiModel


let createModuleName suffix (project:string) (domain:string) =
    $"{project}.Domain.{domain}.{suffix}"

let ensureFolder(folder:string) =
    if not (Directory.Exists folder) then
        printfn $"Creating folder {folder}"
        Directory.CreateDirectory folder |> ignore

// postgres prefers lowercase, so CatDog -> cat_dog
// todo: not sure we are doing this on sql generation side to be consistent
let postgrestify (s:string) =
    let quote =  s.ToLowerInvariant() <> s
    let pre = s.Replace(" ","_").Replace("-","_")
    if quote then
        $"\"{pre}\""
    else
        pre.ToLowerInvariant()

let private docComment (indent:string) (doc:string option) =
    match doc with
    | Some d -> $"{indent}/// {d.Replace('\r', ' ').Replace('\n', ' ')}\n"
    | None -> ""

let emitRecord (r:RecordDef) =
    stringBuffer {
        yield docComment "" r.Doc
        yield $"type {r.Name} = {{\n"
        for f in r.Fields do
            yield docComment "    " f.Doc
            yield $"    {f.Name} : {f.Type.FSharp()}\n"
        yield $"}}\n"
    }

let emitEnum (e:EnumDef) =
    stringBuffer {
        yield docComment "" e.Doc
        yield $"type {e.Name} =\n"
        for case, wire in e.Cases do
            // on the wire the value is the postgres enum label, not the F# case name
            yield $"    | [<JsonName \"{wire}\">] {case}\n"
    }

/// postgres type to cast a parameter to when its type can't be inferred (e.g. "@p IS NULL")
let private sqlParamType (schema:string) (c:Column) =
    if c.Array then failwithf $"Array column {c.CName} can't be used as a key"
    match c.CType with
    | Id | Int32 -> "int"
    | Id64 -> "bigint"
    | String -> "text"
    | Jsonb -> "jsonb"
    | Bool -> "boolean"
    | Float -> "float8"
    | Decimal -> "numeric"
    | Timestamp -> "timestamptz"
    | ColumnType.Guid -> "uuid"
    | Blob -> "bytea"
    | Enum e -> $"{schema}.{e}"

/// F# lambda turning a type provider row into the table's record
let private rowToRecord (t:Table) =
    let fields =
        [ for col in t.FullCols() ->
            let value =
                match col.CType, col.Nullable with
                | Enum e, false -> $"r.{col.CName} |> {e |> toFSharpLower}FromEnum"
                | Enum e, true -> $"r.{col.CName} |> Option.map {e |> toFSharpLower}FromEnum"
                | _ -> $"r.{col.CName}"
            $"{col.FSharpName()} = {value}" ]
    let fieldsText = String.Join("; ", fields)
    $"(fun r -> ({{ {fieldsText} }} : {t.FSharpName()}))"

/// generates, for one schema: the domain types (shared), storage, service, api contract + client
/// (shared), server routes and the wiring of the api to the service layer
let emitDomain (proj:string) (s:Schema) =
    let domain = titleCase s.SName
    let projCap = proj |> titleCase
    let operations = schemaOperations s

    let domainCode =
        stringBuffer {
            yield $"namespace {projCap}.Domain.{domain}\n"
            yield $"\n"
            yield $"open System\n"
            yield $"open System.Text.Json.Serialization\n"

            // Any enums in the schema that need F# types
            for e in s.Enums do
                yield $"\n"
                yield emitEnum (enumDef e)
            yield $"\n"

            for t in s.Tables do
                yield $"// -------------------------------------------\n"
                yield $"// {t.TName}\n"
                yield $"// -------------------------------------------\n"
                for r in tableRecords t do
                    yield emitRecord r
                    yield "\n"
        }

    let storageCode =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Storage\n"
            yield $"\n"
            yield $"open {projCap}.Common\n"
            yield $"open {projCap}.Db\n"
            yield $"open {projCap}.Domain.{domain}\n"
            yield $"open System.Threading.Tasks\n"

            // Any enums in the schema that need converter functions to/from F# types
            for e in s.Enums do

                let fSharpType = e.EName |> toFSharp
                let fSharpVar = e.EName |> toFSharpLower
                yield $"let {fSharpVar}ToEnum(v:{fSharpType}) =\n"
                yield $"    match v with\n"
                for v in e.EValues do
                    yield $"    | {fSharpType}.{v |> toFSharp} -> Db.{Reserved.quoteFSharpReserved s.SName}.Types.{e.EName}.{Reserved.quoteFSharpReserved v}\n"
                yield $"\n"
                yield $"let {fSharpVar}FromEnum(e:Db.{Reserved.quoteFSharpReserved s.SName}.Types.{e.EName}) =\n"
                yield $"    match e with\n"
                for v in e.EValues do
                    yield $"    | Db.{Reserved.quoteFSharpReserved s.SName}.Types.{e.EName}.{Reserved.quoteFSharpReserved v} -> {fSharpType}.{v |> toFSharp}\n"
                yield $"    | x -> failwithf $\"Impossible {e.EName} enum value {{x}}\"\n"
                yield $"\n"

            for t in s.Tables do
                let colsExceptKey = t.FullCols() |> List.filter (fun c -> c.CType <> Id) // FIXFIX - generalize to non int keys
                let colNamesExceptKey = String.Join(",",[for c in colsExceptKey  -> postgrestify c.CName])
                // Create / update commands use AllParametersOptional=true: every parameter is an option
                // and None is sent as SQL NULL, so nullable columns need no special handling.
                let insertPlaceHolders = String.Join(",",[for c in colsExceptKey -> $"@{c.CName}"])
                let updatePlaceHolders = String.Join(",",[for c in colsExceptKey -> $"{postgrestify c.CName}=@{c.CName}"])
                let fullPostgresCols =
                    String.Join(",",[for c in t.FullCols()  -> postgrestify c.CName])
                let nonPKParametersForCall =
                    String.Join(",",[for c in colsExceptKey  ->
                                        let field = $"request.{c.FSharpName()}"
                                        let value =
                                            match c.CType, c.Nullable with
                                            | Enum e, true -> $"({field} |> Option.map {e |> toFSharpLower}ToEnum)"
                                            | Enum e, false -> $"Some ({field} |> {e |> toFSharpLower}ToEnum)"
                                            | _, true -> field
                                            | _, false -> $"Some {field}"
                                        $"{postgrestify c.CName}={value}"])

                let fsharpName = t.FSharpName()
                let returning,readWhereClause,parameters =
                    match t.PKey with
                    | None -> "RETURNING id","id= @id","id=request"
                    | Some x ->
                        "", // return nothing where there's a custom primary key
                        String.Join(" AND ",[for c in x.Cols -> $"{postgrestify c}=@{c}"]),
                        String.Join(",",[for c in x.Cols -> $"{c}=request.{c |> toFSharp}"])
                yield $"// -------------------------------------------\n"
                yield $"// {fsharpName} CRUD operations\n"
                yield $"// -------------------------------------------\n"
                yield $"\n"
                // ====================================================
                // Create code
                // ====================================================
                yield $"let create{fsharpName} (request:Create{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"INSERT INTO {s.SName}.{t.TName} (\n"
                yield $"            {colNamesExceptKey}) \n                    VALUES({insertPlaceHolders}) {returning}\n"
                yield $"                                   \"\"\",SingleRow=true,AllParametersOptional=true>(conn)\n"
                yield $"        let! newId = cmd.TaskAsyncExecute({nonPKParametersForCall})\n"
                match t.PKey with
                | None ->
                    yield $"        return newId.Value // returns option but should be reliable\n"
                | Some _ ->
                    yield $"        return () // complex primary key, no return value\n"
                yield $"    }}\n"
                yield $"\n"
                yield $"let read{fsharpName} (request:{t.PKeyTypeName()}) : Task<{fsharpName} option>=\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\""
                yield $"        SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} WHERE {readWhereClause}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let! result = cmd.TaskAsyncExecute({parameters})\n"
                yield $"        return result |> Option.map {rowToRecord t}\n"
                yield $"    }}\n"
                yield $"\n"


                // ====================================================
                // Update code
                // ====================================================
                yield $"let update{fsharpName} (request:Update{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"\n"
                yield $"          UPDATE {s.SName}.{t.TName} \n"
                yield $"              SET\n"

                let updateParameters =
                    String.Join(",",
                        [   yield nonPKParametersForCall // already have these from insert
                            match t.PKey with // updates also need the primary key columns
                            | None ->
                                yield $"id=Some request.Id"
                            | Some _ ->
                                () // key columns are already in nonPKParametersForCall
                        ])

                yield $"                {updatePlaceHolders}\n"
                yield $"          WHERE {readWhereClause}\n"
                yield $"                                   \"\"\",AllParametersOptional=true>(conn)\n"
                yield $"        // rows affected: 0 when the row doesn't exist (or RLS hides it)\n"
                yield $"        return! cmd.TaskAsyncExecute({updateParameters})\n"
                yield $"    }}\n"
                yield $"\n"
                // ====================================================
                // Delete code
                // ====================================================
                yield $"let delete{fsharpName} (request:{t.PKeyTypeName()}) : Task<int> =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"DELETE FROM {s.SName}.{t.TName} WHERE {readWhereClause}\"\"\">(conn)\n"
                yield $"        // rows affected: 0 when the row doesn't exist (or RLS hides it)\n"
                yield $"        return! cmd.TaskAsyncExecute({parameters})\n"
                yield $"    }}"
                yield $"\n\n"

                // ====================================================
                // List code (offset paging)
                // ====================================================
                let emitRunPart() =
                    stringBuffer {
                        yield $"            return result |> Seq.map {rowToRecord t} |> Array.ofSeq\n"
                    }
                yield $"let list{fsharpName} (row:int option) (batchSize:int option) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        match row,batchSize with\n"
                yield $"        | Some row, Some batchSize ->\n"
                yield $"            use cmd = Db.CreateCommand<\"\"\""
                yield $"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} LIMIT @batch_size OFFSET @row\n"
                yield $"                                   \"\"\">(conn)\n"
                yield $"            let! result = cmd.TaskAsyncExecute(row=row,batch_size=batchSize)\n"
                yield emitRunPart()
                yield $"        | None , Some batchSize ->\n"
                yield $"            use cmd = Db.CreateCommand<\"\"\""
                yield $"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} LIMIT @batch_size\n"
                yield $"                                   \"\"\">(conn)\n"
                yield $"            let! result = cmd.TaskAsyncExecute(batch_size=batchSize)\n"
                yield emitRunPart()
                yield $"        | Some row , None ->\n"
                yield $"            use cmd = Db.CreateCommand<\"\"\""
                yield $"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} LIMIT @batch_size OFFSET @row\n"
                yield $"                                   \"\"\">(conn)\n"
                yield $"            let! result = cmd.TaskAsyncExecute(row=row,batch_size=50)\n"
                yield emitRunPart()
                yield $"        | None , None ->\n"
                yield $"            use cmd = Db.CreateCommand<\"\"\""
                yield $"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} "
                yield $"                                   \"\"\">(conn)\n"
                yield $"            let! result = cmd.TaskAsyncExecute()\n"
                yield emitRunPart()
                yield $"    }}\n"
                yield $"\n"

                // ====================================================
                // Count
                // ====================================================
                yield $"let count{fsharpName} () : Task<int64> =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"SELECT count(*) AS n FROM {s.SName}.{t.TName}\"\"\",SingleRow=true>(conn)\n"
                yield $"        let! n = cmd.TaskAsyncExecute()\n"
                yield $"        return n |> Option.flatten |> Option.defaultValue 0L\n"
                yield $"    }}\n"
                yield $"\n"

                // ====================================================
                // Keyset paging: page, and one list_by_<fk> per foreign key
                // ====================================================
                let keyCols = keyColumns t
                let orderBy = String.Join(", ", keyCols |> List.map (fun k -> postgrestify k.CName))
                let keysetClause =
                    match t.PKey with
                    | None ->
                        "(@after_id::int IS NULL OR id > @after_id)"
                    | Some _ ->
                        let first = keyCols.Head
                        let lhs = String.Join(", ", keyCols |> List.map (fun k -> postgrestify k.CName))
                        let rhs = String.Join(", ", keyCols |> List.map (fun k -> $"@after_{k.CName}::{sqlParamType s.SName k}"))
                        $"(@after_{first.CName}::{sqlParamType s.SName first} IS NULL OR ({lhs}) > ({rhs}))"
                let keysetArgs =
                    match t.PKey with
                    | None -> [ "after_id=request.After" ]
                    | Some _ ->
                        [ for k in keyCols ->
                            let conv =
                                match k.CType with
                                | Enum e -> $" |> {e |> toFSharpLower}ToEnum"
                                | _ -> ""
                            $"after_{k.CName}=(request.After |> Option.map (fun key -> key.{k.FSharpName()}{conv}))" ]
                let nextKey =
                    match t.PKey with
                    | None -> "last.Id"
                    | Some _ ->
                        let fields = String.Join("; ", keyCols |> List.map (fun k -> $"{k.FSharpName()} = last.{k.FSharpName()}"))
                        $"({{ {fields} }} : {t.PKeyTypeName()})"
                let emitKeysetQuery (signature:string) (extraWhere:string) (extraArgs:string list) =
                    stringBuffer {
                        let args = String.Join(", ", extraArgs @ keysetArgs @ [ "limit=Some (int64 limit)" ])
                        yield $"let {signature} : Task<{pageResultName t}> =\n"
                        yield $"    task {{\n"
                        yield $"        let limit = Paging.limit request.Limit\n"
                        yield $"        use! conn = Db.openConnectionAsync()\n"
                        yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                        yield $"        use cmd = Db.CreateCommand<\"\"\"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName}\n"
                        yield $"            WHERE {extraWhere}{keysetClause}\n"
                        yield $"            ORDER BY {orderBy} LIMIT @limit\"\"\",AllParametersOptional=true>(conn)\n"
                        yield $"        let! rows = cmd.TaskAsyncExecute({args})\n"
                        yield $"        let items = rows |> Seq.map {rowToRecord t} |> Array.ofSeq\n"
                        yield $"        let next =\n"
                        yield $"            if items.Length = limit then\n"
                        yield $"                let last = items.[items.Length - 1]\n"
                        yield $"                Some {nextKey}\n"
                        yield $"            else None\n"
                        yield $"        return ({{ Items = items; Next = next }} : {pageResultName t})\n"
                        yield $"    }}\n"
                        yield $"\n"
                    }
                yield emitKeysetQuery $"page{fsharpName} (request:{pageRequestName t})" "" []
                for c in foreignKeyColumns t do
                    yield emitKeysetQuery
                            $"list{fsharpName}By{c.FSharpName()} (request:{fkRequestName t c})"
                            $"{postgrestify c.CName} = @{c.CName} AND "
                            [ $"{c.CName}=Some request.{c.FSharpName()}" ]

                // ====================================================
                // read_by_<unique cols>
                // ====================================================
                for cols in uniqueLookups t do
                    let suffix = cols |> List.map (fun c -> c.FSharpName()) |> String.concat ""
                    let whereClause = String.Join(" AND ", cols |> List.map (fun c -> $"{postgrestify c.CName} = @{c.CName}"))
                    let args =
                        String.Join(", ",
                            [ for c in cols ->
                                match c.CType with
                                | Enum e -> $"{c.CName}=(request.{c.FSharpName()} |> {e |> toFSharpLower}ToEnum)"
                                | _ -> $"{c.CName}=request.{c.FSharpName()}" ])
                    yield $"let read{fsharpName}By{suffix} (request:{uniqueRequestName t cols}) : Task<{fsharpName} option> =\n"
                    yield $"    task {{\n"
                    yield $"        use! conn = Db.openConnectionAsync()\n"
                    yield $"        do! TenantRls.applyAmbientToConnection conn\n"
                    yield $"        use cmd = Db.CreateCommand<\"\"\"SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} WHERE {whereClause}\"\"\",SingleRow=true>(conn)\n"
                    yield $"        let! result = cmd.TaskAsyncExecute({args})\n"
                    yield $"        return result |> Option.map {rowToRecord t}\n"
                    yield $"    }}\n"
                    yield $"\n"
                yield $"\n"
        }
    let apiWireup =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Api\n"

            yield $"\n"
            yield $"open {projCap}.Api.{domain}\n"
            yield $"open {projCap}.Domain.{domain}\n"
            yield $"\n"
            yield $"// Wire up the api definition to the service layer calls here\n"
            yield $"let api : Api = {{\n"
            for op in operations do
                yield $"    {op.Name} = Service.{op.FunctionName}\n"
            yield "}\n"

        }

    let serviceLayer =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Service\n"
            yield $"\n"
            yield $"open {projCap}.Common\n"
            yield $"open {projCap}.Domain.{domain}\n"
            yield $"\n"
            yield $"// PgGen: note - these are simple wrappers for now but business logic / validation / transactions /\n"
            yield $"// multiple IO calls can be handled here.  Return Error (NotFound / Validation / Conflict ..) for\n"
            yield $"// expected failures; exceptions are turned into api errors by the web layer.\n"
            let wrap (signature:string) (call:string) =
                stringBuffer {
                    yield $"let {signature} =\n"
                    yield $"    task {{\n"
                    yield $"        let! result = {call}\n"
                    yield $"        return Ok result\n"
                    yield $"    }}\n"
                    yield $"\n"
                }
            for t in s.Tables do
                // update / delete: zero rows affected means there was nothing (visible) to change
                let wrapRowCount (signature:string) (call:string) (keyExpr:string) =
                    stringBuffer {
                        yield $"let {signature} =\n"
                        yield $"    task {{\n"
                        yield $"        let! rows = {call}\n"
                        yield $"        if rows = 0 then\n"
                        yield $"            let key = {keyExpr}\n"
                        yield $"            return Error (NotFound $\"{t.TName} {{key}} not found\")\n"
                        yield $"        else\n"
                        yield $"            return Ok ()\n"
                        yield $"    }}\n"
                        yield $"\n"
                    }
                // key as text for the NotFound message, e.g. 42 or 7/red for composite keys
                let compositeKeyExpr (pk:PKey) =
                    String.Join(" + \"/\" + ", [ for c in pk.Cols -> $"string request.{c |> toFSharp}" ])
                yield $"// -------------------------------------------\n"
                for op in tableOperations t do
                    let signature = $"{op.FunctionName} (request:{op.ArgType.FSharp()}) : ApiResult<{op.Result.FSharp()}>"
                    match op.Kind with
                    | UpdateOp ->
                        let keyExpr =
                            match t.PKey with
                            | None -> "string request.Id"
                            | Some pk -> compositeKeyExpr pk
                        yield wrapRowCount signature $"Storage.{op.FunctionName} request" keyExpr
                    | DeleteOp ->
                        let keyExpr =
                            match t.PKey with
                            | None -> "string request"
                            | Some pk -> compositeKeyExpr pk
                        yield wrapRowCount signature $"Storage.{op.FunctionName} request" keyExpr
                    | ListOp ->
                        yield wrap signature $"Storage.{op.FunctionName} request.Offset request.BatchSize"
                    | CountOp ->
                        yield wrap $"{op.FunctionName} () : ApiResult<{op.Result.FSharp()}>" $"Storage.{op.FunctionName} ()"
                    | CreateOp | ReadOp | PageOp | ListByOp _ | ReadByOp _ ->
                        yield wrap signature $"Storage.{op.FunctionName} request"
        }
    // Api contract + client.  Compiled into the shared project, so it must not depend on server or
    // database libraries.  A namespace (rather than a module) lets the server routing live in a
    // separate backend-only file while still being addressable as {domain}.Server
    let apiDef =
        stringBuffer {
            yield $"namespace {projCap}.Api.{domain}\n"
            yield $"\n"
            yield $"open System\n"
            yield $"open System.Net.Http\n"
            yield $"open {projCap}.Common\n"
            yield $"open {projCap}.Domain.{domain}\n"

            yield $"\n"
            yield $"type Api = {{\n"
            for op in operations do
                yield $"    /// {op.Summary}\n"
                yield $"    {op.Name} : {op.ArgType.FSharp()} -> ApiResult<{op.Result.FSharp()}>\n"
            yield $"}}\n"

            // -------------------------------------------
            // Client
            // -------------------------------------------
            yield $"\n"
            yield $"/// Typed client for the {s.SName} api.  basePath is relative to HttpClient.BaseAddress\n"
            yield $"type Client(http : HttpClient, basePath : string) =\n"
            for op in operations do
                let path = $"{{basePath}}{op.Path}"
                let result = op.Result.FSharp()
                yield $"    /// {op.Summary}\n"
                match op.Arg with
                | RouteId ->
                    yield $"    member x.{op.Name}(id:{op.ArgType.FSharp()}) : ApiResult<{result}> =\n"
                    yield $"        ApiClient.get http $\"{path}/{{id}}\"\n"
                | OffsetQuery ->
                    // query parameter names must match what Handlers.withBatchOffset reads
                    yield $"    member x.{op.Name}(?offset:int, ?batchSize:int) : ApiResult<{result}> =\n"
                    yield $"        ApiClient.get http (ApiClient.withQuery $\"{path}\" [ \"offset\", offset; \"batchSize\", batchSize ])\n"
                | NoArg ->
                    yield $"    member x.{op.Name}() : ApiResult<{result}> =\n"
                    yield $"        ApiClient.get http $\"{path}\"\n"
                | JsonBody ->
                    yield $"    member x.{op.Name}(request:{op.ArgType.FSharp()}) : ApiResult<{result}> =\n"
                    yield $"        ApiClient.post http $\"{path}\" request\n"
        }

    // Server side routing for the api (plain Giraffe).  Backend only.
    let apiServer =
        stringBuffer {
            yield $"namespace {projCap}.Api.{domain}\n"
            yield $"\n"
            yield $"open Giraffe\n"
            yield $"\n"
            yield $"module Server =\n"
            yield $"    module H = {projCap}.Api.Handlers\n"
            yield $"\n"
            yield $"    /// Routes for the {s.SName} api, mounted under /{s.SName}\n"
            yield $"    let build (api : Api) : HttpHandler = subRoute \"/{s.SName}\" (choose [\n"
            yield $"        GET >=> choose [\n"
            for op in operations do
                match op.Arg with
                | RouteId -> yield $"            routef \"{op.Path}/%%i\" (H.withArg api.{op.Name})\n"
                | OffsetQuery -> yield $"            route \"{op.Path}\" >=> H.withBatchOffset api.{op.Name}\n"
                | NoArg -> yield $"            route \"{op.Path}\" >=> H.respond (fun _ -> api.{op.Name} ())\n"
                | JsonBody -> ()
            yield $"        ]\n"
            yield $"        POST >=> choose [\n"
            for op in operations do
                match op.Arg with
                | JsonBody -> yield $"            route \"{op.Path}\" >=> H.withBody api.{op.Name}\n"
                | RouteId | OffsetQuery | NoArg -> ()
            yield $"        ]\n"
            yield $"    ])\n"
        }
    {| Storage = storageCode
       ApiWireup = apiWireup
       ApiDef = apiDef
       ApiServer = apiServer
       Domain = domainCode
       ServiceLayer = serviceLayer|}

let generateMasterServerApiFile (proj:string) (d:Db) =
    stringBuffer {
        yield $"module {proj |> titleCase}.Api.Server\n"
        let dbCap = d.DName |> titleCase
        let projCap = proj |> titleCase
        yield $"open   {projCap}.Api\n"
        yield $"open   Giraffe\n"
        yield $"\n"
        // Definition of the master Api type with functions
        yield $"type {projCap}Api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} : {schemaCap}.Api\n"
        yield $"}}\n"


        // Now wire it up with references to the subapis
        yield $"let api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} = {projCap}.Domain.{schemaCap}.Api.api\n"
        yield $"}}\n"
        yield $"\n"
        yield $"/// The OpenAPI document, generated by PgGen with this code and embedded at build time\n"
        yield $"let openApiJson =\n"
        yield $"    lazy (\n"
        yield $"        use stream = typeof<{projCap}Api>.Assembly.GetManifestResourceStream \"openapi.json\"\n"
        yield $"        use reader = new System.IO.StreamReader(stream)\n"
        yield $"        reader.ReadToEnd())\n"
        yield $"\n"
        yield $"/// Every schema's routes under /api, plus GET /api/openapi.json - add this to your Giraffe app\n"
        yield $"/// (app.UseGiraffe handler)\n"
        yield $"let handler (api : {projCap}Api) : Giraffe.Core.HttpHandler =\n"
        yield $"    Giraffe.Routing.subRoute \"/api\" (Giraffe.Core.choose [\n"
        yield $"        Giraffe.Core.GET >=> Giraffe.Routing.route \"/openapi.json\"\n"
        yield $"            >=> Giraffe.Core.setHttpHeader \"Content-Type\" \"application/json\"\n"
        yield $"            >=> Giraffe.Core.warbler (fun _ -> Giraffe.Core.setBodyFromString openApiJson.Value)\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"        {schemaCap}.Server.build api.{schemaCap}\n"
        yield $"    ])\n"
    }
let generateMasterClientApiFile (proj:string) (d:Db) =
    let projCap = proj |> titleCase
    stringBuffer {
        yield $"namespace {projCap}.Api\n"
        yield $"open System.Net.Http\n"
        // Definition of the master Api type with functions
        yield "/// Top level api type definition\n"
        yield $"type Api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} : {schemaCap}.Api\n"
        yield $"}}\n"
        yield $"\n"
        yield $"/// Typed client for the whole api.  Set http.BaseAddress to the server root (ending in /);\n"
        yield $"/// basePath (default \"api\") is where the server mounted the api.\n"
        yield $"type {projCap} (http : HttpClient, basePath : string) =\n"
        yield $"    new (http : HttpClient) = {projCap}(http, \"api\")\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    member x.{schemaCap} = {schemaCap}.Client(http, basePath + \"/{s.SName}\")\n"

        yield $"\n"
    }


let cleanSlash (path:string) =
    path.Replace("\\","/")

/// Nearest file called name in folder or any of its parents (mirrors how MSBuild finds Directory.*.props)
let rec private findFileAbove (folder:string) (name:string) =
    let candidate = Path.Combine(folder, name)
    if File.Exists candidate then Some candidate
    else
        match Directory.GetParent folder with
        | null -> None
        | parent -> findFileAbove parent.FullName name

/// Generated projects use NuGet Central Package Management.  Write a Directory.Packages.props
/// next to them unless the surrounding repo already manages versions centrally, in which case we
/// leave that file alone and tell the user which versions it needs.
let private writePackageProps (folder:string) =
    let fullFolder = Path.GetFullPath folder
    let ownProps = Path.Combine(fullFolder, "Directory.Packages.props")
    let isGenerated (path:string) = File.ReadAllText(path).Contains(generatedPropsMarker)
    match findFileAbove fullFolder "Directory.Packages.props" with
    | Some existing when not (isGenerated existing) ->
        printfn $"Found {existing |> cleanSlash}, not generating Directory.Packages.props."
        printfn "Make sure it contains versions for these packages:"
        for line in packageVersionLines () do
            printfn $"{line}"
    | _ ->
        printfn $"Generating {ownProps |> cleanSlash}"
        File.WriteAllText(ownProps, directoryPackagesPropsSource ())

/// Older versions of PgGen generated paket files.  We don't delete user files, but say so.
let private warnAboutPaket (folder:string) =
    let leftovers =
        [ "paket.dependencies"; "paket.references"; "paket.lock"; ".paket" ]
        |> List.filter (fun f -> File.Exists(Path.Combine(folder, f)) || Directory.Exists(Path.Combine(folder, f)))
    if not leftovers.IsEmpty then
        let names = String.Join(", ", leftovers)
        printfn $"WARNING: {folder |> cleanSlash} contains paket files from an older PgGen ({names})."
        printfn "         Generated projects now use NuGet central package management; these can be deleted"
        printfn "         (along with the paket entry in .config/dotnet-tools.json)."

let generate (proj:string) (folder:string) (d:Db) =
    let projCap = titleCase proj
    ensureFolder folder
    let apiFolder = Path.Combine(folder, $"Api")
    ensureFolder apiFolder

    let generatedFileNames = [
        for schema in d.Schemas do
            let schemaCap = titleCase schema.SName |> deReservifyFSharp // avoid collision with keywords
            printfn $"Generating schema {schemaCap}"
            let schemaSubFolder = $"{projCap}.Domain.{schemaCap}"
            let schemaFolder = Path.Combine(folder, $"{projCap}.Domain.{schemaCap}")
            ensureFolder schemaFolder

            let code = emitDomain proj schema


            let storageFile = Path.Combine(schemaSubFolder,$"{schemaCap}Storage.fs")
            let serviceFile = Path.Combine(schemaSubFolder,$"{schemaCap}Service.fs")
            let domainFile = Path.Combine(schemaSubFolder,$"{schemaCap}Domain.fs")
            let apiDefFile = Path.Combine("Api",$"{schemaCap}Api.fs")
            let apiServerFile = Path.Combine("Api",$"{schemaCap}ApiServer.fs")
            let apiWireupFile = Path.Combine("Api",$"{schemaCap}ApiWireup.fs")

            yield domainFile
            yield apiDefFile // api uses data structures from domainFile
            yield storageFile
            yield serviceFile
            yield apiServerFile // server routing needs the Api type from apiDefFile
            yield apiWireupFile

            let fullApiServerFile = Path.Combine(folder, apiServerFile)
            printfn $"  generating {fullApiServerFile|> cleanSlash}"
            File.WriteAllText(fullApiServerFile, code.ApiServer)

            let fullStorageFile = Path.Combine(folder, storageFile)
            printfn $"  generating {fullStorageFile|> cleanSlash}"
            File.WriteAllText(fullStorageFile, code.Storage)

            let fullApiWireupFile = Path.Combine(folder, apiWireupFile)
            printfn $"  generating {fullApiWireupFile|> cleanSlash}"
            File.WriteAllText(fullApiWireupFile, code.ApiWireup)

            let fullServiceFile = Path.Combine(folder, serviceFile)
            printfn $"  generating {fullServiceFile|> cleanSlash}"
            File.WriteAllText(fullServiceFile, code.ServiceLayer)

            let fullDomainFile = Path.Combine(folder, domainFile)
            printfn $"  generating {fullDomainFile|> cleanSlash}"
            File.WriteAllText(fullDomainFile, code.Domain)

            let fullApiDef = Path.Combine(folder, apiDefFile)
            printfn $"  generating {fullApiDef|> cleanSlash}"
            File.WriteAllText(fullApiDef, code.ApiDef)
        // Generate master server API file to rule them all
        let apiFile = Path.Combine("Api",$"{projCap}ServerApi.fs")
        yield apiFile

        let fullApiFile = Path.Combine(folder, apiFile)
        printfn $"  generating {fullApiFile|> cleanSlash}"
        let apiFileContent = generateMasterServerApiFile proj d
        File.WriteAllText(fullApiFile,apiFileContent)

        let clientApiFile = Path.Combine(folder,"Api",$"{projCap}ClientApi.fs")
        let apiFileContent = generateMasterClientApiFile proj d
        File.WriteAllText(clientApiFile,apiFileContent)

    ]
    let auxFileNames = [
        let commonFolderName = "Common"
        let fullCommonFolder = Path.Join(folder,commonFolderName)
        if not <| Directory.Exists fullCommonFolder then
            printfn $"Creating folder {fullCommonFolder}"
            ensureFolder fullCommonFolder

        let commonFile = Path.Join(commonFolderName,"Common.fs")
        yield commonFile

        let fullCommonFile = Path.Combine(folder, commonFile)
        File.WriteAllText(fullCommonFile, commonFileSource proj)

        let dbFile = Path.Join(commonFolderName,"Db.fs")
        yield dbFile

        let fullDbFile = Path.Combine(folder, dbFile)
        File.WriteAllText(fullDbFile, dbFileSource proj)

    ]


    let commonFile, dbFile =
        match auxFileNames with
        | [ c; d ] -> c, d
        | x -> failwithf $"Internal error: unexpected aux files {x}"

    // might be a good idea long term to strongly type these different file types since we need different subsets
    let isSharedFile (file:string) =
        file.EndsWith("Domain.fs")
        || (file.EndsWith("Api.fs") && (file.StartsWith("Api/") || file.StartsWith("Api\\"))
            && not (file.EndsWith($"{projCap}ServerApi.fs")))
    let isApiFolderFile (file:string) = file.StartsWith("Api/") || file.StartsWith("Api\\")

    let sharedFiles = generatedFileNames |> List.filter isSharedFile
    let backendFiles = generatedFileNames |> List.filter (isSharedFile >> not)
    // the api folder files go last since they depend on the service layer
    let backendApiFiles, backendDomainFiles = backendFiles |> List.partition isApiFolderFile

    let packageRefs (packages:string list) =
        stringBuffer {
            yield $"  <ItemGroup>\n"
            for p in packages do
                yield $"    <PackageReference Include=\"{p}\" />\n" // versions live in Directory.Packages.props
            yield $"  </ItemGroup>\n"
        }

    // Shared project: domain types, api contract and client.  No server / database dependencies so
    // that front ends and other non backend consumers can reference it.
    let apiProjName = $"{projCap}Api.fsproj"
    let apiProjContent =
        stringBuffer {
            yield $"<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            yield $"  <PropertyGroup>\n"
            yield $"    <TargetFramework>{sharedTargetFramework}</TargetFramework>\n"
            yield $"    <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>\n"
            yield $"  </PropertyGroup>\n"
            yield $"  <ItemGroup>\n"
            yield $"    <Compile Include=\"../{commonFile |> cleanSlash}\" />\n"
            for file in sharedFiles do
                if isApiFolderFile file then
                    yield $"    <Compile Include=\"{file[4..]|> cleanSlash}\" />\n" // remove Api/ prefix
                else
                    yield $"    <Compile Include=\"../{file|> cleanSlash}\" />\n"
            yield $"    <Compile Include=\"{projCap}ClientApi.fs\" />\n"
            yield $"  </ItemGroup>\n"
            yield packageRefs sharedPackages
            yield $"</Project>\n"
        }
    let apiProjPath = Path.Combine(folder,"Api",apiProjName)
    printfn $"Generating {apiProjPath|> cleanSlash}"
    File.WriteAllText(apiProjPath, apiProjContent)

    // Giraffe handlers shared by all the schema server files (backend only)
    let handlersFile = Path.Combine("Api", "ApiHandlers.fs")
    // OpenAPI document: produced from the same model as the code, embedded in the backend and
    // served at /api/openapi.json
    let openApiFile = Path.Combine("Api", "openapi.json")
    printfn $"Generating {Path.Combine(folder, openApiFile) |> cleanSlash}"
    File.WriteAllText(Path.Combine(folder, openApiFile), OpenApi.document proj pgGenVersion d)
    File.WriteAllText(Path.Combine(folder, handlersFile), handlersFileSource proj)

    // Backend project: database access, service layer and server side api.  References the
    // shared project rather than recompiling its files so the types are identical on both sides.
    let fsProjContent =
        stringBuffer {
            yield $"<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            yield $"  <PropertyGroup>\n"
            yield $"    <TargetFramework>net{dotnetVersion}</TargetFramework>\n"
            yield $"    <DisableImplicitFSharpCoreReference>true</DisableImplicitFSharpCoreReference>\n"
            yield $"  </PropertyGroup>\n"
            yield $"  <ItemGroup>\n"
            yield $"    <Compile Include=\"{dbFile|> cleanSlash}\" />\n"
            yield $"    <Compile Include=\"{handlersFile |> cleanSlash}\" />\n"
            for file in backendDomainFiles @ backendApiFiles do
                yield $"    <Compile Include=\"{file|> cleanSlash}\" />\n"
            yield $"  </ItemGroup>\n"
            yield $"  <ItemGroup>\n"
            yield $"    <ProjectReference Include=\"Api/{apiProjName}\" />\n"
            yield $"  </ItemGroup>\n"
            yield $"  <ItemGroup>\n"
            yield $"    <EmbeddedResource Include=\"{openApiFile |> cleanSlash}\" LogicalName=\"openapi.json\" />\n"
            yield $"  </ItemGroup>\n"
            yield packageRefs backendPackages
            yield $"</Project>\n"
        }
    let fsProjPath = Path.Combine(folder, $"{projCap}.Backend.fsproj")
    printfn $"Generating {fsProjPath|> cleanSlash}"
    File.WriteAllText(fsProjPath, fsProjContent)


    let compileTimeDbFile = Path.Combine(folder,compileTimeDbFile)
    File.WriteAllText(compileTimeDbFile,$"Host=127.0.0.1;Username=read_write;Password=readwrite;Database={d.DName};Pooling=true")

    writePackageProps folder
    warnAboutPaket folder

(* future work

// Using table api for bulk inserts
 use table = new Db.enzyme.Tables.enzyme ()
        let mutable counter = 0

        for signature, accessions in enzymes do
            if not <| lookup.ContainsKey(signature) then
                let newRow =
                    table.NewRow(accessions = Some accessions, signature = signature, id_source = uniprotSource)

                table.Rows.Add(newRow)
                counter <- counter + 1
                if counter % 1000 = 0 then printf $"... {counter}"

        let updatedCount = table.Update(conn)
        printfn $"   added {updatedCount} records for enzymes in {elapsed ()} seconds"

        for row in table.Rows do
            lookup.Add(row.signature, row.id * 1<GlobalProteinId>)




// Example of bulk insert style, where we prefetch a range of ids, and write directly to the db (this is fastest / lowest level)

 let! ids =
            task {
                use getIds = Db.CreateCommand<"
                    SELECT nextval('enzyme.protein_id_seq') FROM generate_series(1, @n)">(conn)
                let! ids = getIds.TaskAsyncExecute(idsNeededCount)
                return ids |> Seq.map Option.get |> Array.ofSeq
            }
        log "loadprots" $"reserved {ids.Length} ids"

        use proteinWriter = conn.BeginBinaryImport(importProteinSeqCmd)

        for protein in proteins do
            if not <| cache.Protein.ContainsKey protein.Accession then
                let idSource =
                    match protein.Origin with
                    | SwissProt -> swissprotSource
                    | Trembl -> tremblSource
                    | Uniprot -> uniprotSource
                let md5 =
                    protein.Md5 |> Option.defaultValue (Common.md5String protein.Sequence)

                proteinWriter.StartRow()
                proteinWriter.Write(ids[added],NpgsqlTypes.NpgsqlDbType.Integer)
                proteinWriter.Write(protein.Accession,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(protein.Name,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(protein.IsFragment,NpgsqlTypes.NpgsqlDbType.Boolean)
                proteinWriter.Write(protein.IsPrecursor,NpgsqlTypes.NpgsqlDbType.Boolean)
                proteinWriter.Write(md5,NpgsqlTypes.NpgsqlDbType.Text)
                proteinWriter.Write(idSource,NpgsqlTypes.NpgsqlDbType.Integer)
                proteinWriter.Write(protein.Sequence,NpgsqlTypes.NpgsqlDbType.Text)

                cache.Protein.Add(protein.Accession, ProteinLookup(int(ids[added])* 1<GlobalProteinId>,md5))

*)
