


module PgGen.CodeGen

open System.IO
open PgGen.StringBuffer
open Common
open System
open PgGen.CodeStatic


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

/// generates a file SchemaDomain.fs that contains type definitions for each table in the
/// for each of the CRUD operations
let emitDomain (proj:string) (s:Schema) =
    let domain = titleCase s.SName
    let projCap = proj |> titleCase


    let domainCode =
        stringBuffer {
            yield $"namespace {projCap}.Domain.{domain}\n"

            let needsSystem =
                [for t in s.Tables do
                    for c in t.Cols do
                        if c.CType = Timestamp || c.CType = ColumnType.Guid then // DateTime / Guid live in System
                            yield true] |> Seq.isEmpty |> not

            if needsSystem then
                yield $"open System\n"

            // Any enums in the schema that need F# types
            for e in s.Enums do
                yield $"\n"
                yield $"type {e.EName |> toFSharp} =\n"
                for v in e.EValues do
                    yield $"    | {v |> toFSharp}\n"
            yield $"\n"

            for t in s.Tables do
                let fsharpName = t.FSharpName()
                let colsExceptKey = t.Cols |> List.filter (fun c -> c.CType <> Id) // FIXFIX - generalize to non int keys

                let availCols = [   for c in t.Cols do
                                        yield {| Name = c.CName;Type = c.CType.FSharpType(); IsArray = c.Array |}
                                    for fr in t.FRefs do
                                        if fr.Generate then
                                            let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}"
                                            yield {| Name = colName;Type = "int" ; IsArray = false |}
                                        else
                                            failwithf "not implemented - not integer foreign key references"
                                    ]

                yield $" // -------------------------------------------\n"
                yield $" // {t.TName}\n"
                yield $" // -------------------------------------------\n"
                // CREATE type
                yield $"type Create{fsharpName} = {{\n"
                for col in colsExceptKey do
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
                    match col.Comment with
                    | None -> ()
                    | Some c ->
                        yield $"\n    /// {c}\n"
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}{arrayModifier}{optionModifier}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        let optionModifier =
                            if fr.IsNullable then
                                " option"
                            else
                                ""
                        yield $"    {colName} : int{optionModifier}\n"
                    else
                        failwithf "Not implemented - not integer foreign key references"

                // columns with enum type
                for e in t.ERefs do
                    if e.Generate then
                        let colName = e.Name |> Option.defaultValue e.EName |> toFSharp
                        let optionModifier =
                            if e.IsNullable then
                                " option"
                            else
                                ""
                        yield $"    {colName} : {e.EName |> toFSharp}{optionModifier}\n"
                    else
                        failwithf "Not implemented - nongenerated enum references"
                yield $"}}"
                yield "\n"

                // UPDATE type
                yield $"type Update{fsharpName} = {{\n"
                for col in t.Cols do // include key since we need to know what to update
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
                    match col.Comment with
                    | None -> ()
                    | Some c ->
                        yield $"\n    /// {c}\n"
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}{arrayModifier}{optionModifier}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        let optionModifier =
                            if fr.IsNullable then
                                " option"
                            else
                                ""
                        yield $"    {colName} : int{optionModifier}\n"
                for e in t.ERefs do
                    if e.Generate then
                        let colName = e.Name |> Option.defaultValue e.EName |> toFSharp
                        yield $"    {colName} : {e.EName |> toFSharp}\n"
                    else
                        failwithf "Not implemented - nongenerated enum references"
                yield $"}}"
                yield "\n"

                // Basic full field type
                yield $"type {fsharpName} = {{\n"
                for col in t.Cols do
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
                    match col.Comment with
                    | None -> ()
                    | Some c ->
                        yield $"\n    /// {c}\n"
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}{arrayModifier}{optionModifier}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        let optionModifier =
                            if fr.IsNullable then
                                " option"
                            else
                                ""
                        yield $"    {colName} : int{optionModifier}\n"
                for e in t.ERefs do
                    if e.Generate then
                        let colName = e.Name |> Option.defaultValue e.EName |> toFSharp
                        yield $"    {colName} : {e.EName |> toFSharp}\n"
                    else
                        failwithf "Not implemented - nongenerated enum references"
                yield $"}}"

                // Tables that aren't using a simple integer key need a custom primary key type
                match t.PKey with
                | Some pk ->
                    yield $"\n"
                    yield $"type {t.PKeyTypeName()}= {{\n"
                    for colName in pk.Cols do
                        let c =
                            match availCols|> List.tryFind (fun c -> c.Name = colName) with
                            | Some x -> x
                            | None ->
                                failwithf $"Could not find column primary key referenced '{colName}' in table '{t.TName}' cols {availCols}"

                        let optionalArray = if c.IsArray then " []" else ""
                        yield $"    {c.Name |> toFSharp} : {c.Type}{optionalArray}\n"
                    yield $"}}"
                | None -> ()
                yield "\n"

        }
    let storageCode =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Storage\n"
            yield $"\n"
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
                let placeHolderDefs =
                    [for c in colsExceptKey  ->
                                            if c.Nullable then
                                                let defaultVPostgres =
                                                    match c.CType with
                                                    | Int32 -> "-1"
                                                    | String -> "''"
                                                    | Timestamp -> "'0001-01-01'::timestamptz"
                                                    | Jsonb -> "'{}'::jsonb"
                                                    | Float -> "0.0"
                                                    | Enum x -> $"NULL::{x}"
                                                    | Decimal -> "0.0"
                                                    | Id64 -> "-1::bigint" // a plain -1 makes postgres infer an int parameter
                                                    | _ -> failwithf $"Not implemented - default value for {c.CType}"
                                                {| CName = c.CName ; CValue = $"(CASE WHEN @{c.CName} = {defaultVPostgres} THEN NULL ELSE @{c.CName} END)" |}
                                            else
                                                {| CName = c.CName ; CValue = $"@{c.CName}" |}
                                    ]
                let insertPlaceHolders = String.Join(",",[for c in placeHolderDefs  -> c.CValue])
                let updatePlaceHolders = String.Join(",",[for c in placeHolderDefs  -> $"{c.CName}={c.CValue}"])
                let fullPostgresCols =
                    String.Join(",",[for c in t.FullCols()  -> postgrestify c.CName])
                let nonPKParametersForCall =
                    String.Join(",",[for c in colsExceptKey  ->
                                        let optionalNullHack =
                                            if c.Nullable then
                                                if c.Array then
                                                    failwith $"Not implemented - nullable array columns"
                                                let defaultV =
                                                    match c.CType with
                                                    | Int32 -> "-1"
                                                    | String -> "\"\""
                                                    | Timestamp -> "\"0001-01-01\""
                                                    | Jsonb -> "\"{}\""
                                                    | Float -> "0.0"
                                                    | Enum x -> "NULL"
                                                    | Decimal -> "0.0M"
                                                    | Id64 -> "-1L"
                                                    | _ -> failwithf $"Not implemented - default value for {c.CType}"
                                                $"(request.{c.FSharpName()} |> Option.defaultValue {defaultV})"
                                            else
                                                $"request.{c.FSharpName()}"
                                        let optionalEnumConversion =
                                            match c.CType with
                                            | Enum e ->
                                                let fSharpVar = e |> toFSharpLower
                                                $"({optionalNullHack}|> {fSharpVar}ToEnum)"
                                            | _ -> $"{optionalNullHack}"
                                        $"{postgrestify c.CName}={optionalEnumConversion}"])

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
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
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
                yield $"        return result |> Option.map(fun r->\n"
                yield $"                                    {{\n"
                for col in t.FullCols() do
                    let optionalEnumMap =
                        match col.CType with
                        | Enum e -> $" |> {e |> toFSharpLower}FromEnum"
                        | _ -> ""
                    yield $"                                        {col.FSharpName()} = r.{col.CName}{optionalEnumMap}\n"
                // annotate: the PKey record can have exactly the same fields
                yield $"                                    }}:{fsharpName})\n"
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
                                yield $"id=request.Id"
                            | Some cols ->
                                // already covered I think by the nonPKParametersForCall
                                //for col in cols.Cols do
                                //    yield $"{col}=request.{col}"
                                ()
                        ])

                yield $"                {updatePlaceHolders}\n"
                yield $"          WHERE {readWhereClause}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let! _ = cmd.TaskAsyncExecute({updateParameters})\n"
                yield $"        return ()\n"
                yield $"    }}\n"
                yield $"\n"
                // ====================================================
                // Delete code
                // ====================================================
                yield $"let delete{fsharpName} (id:{t.PKeyTypeName()}) : Task<unit> =\n"
                yield $"    task {{\n"
                yield $"        return raise (System.NotImplementedException \"delete{fsharpName} is not implemented yet\")\n"
                yield $"    }}"
                yield $"\n\n"

                // ====================================================
                // List code
                // ====================================================
                let emitRunPart() =
                    stringBuffer {
                        yield $"            return result |> Seq.map(fun r->\n"
                        yield $"                                    {{\n"
                        for col in t.FullCols() do
                            let optionalEnumMap =
                                match col.CType with
                                | Enum e -> $" |> {e |> toFSharpLower}FromEnum"
                                | _ -> ""
                            yield $"                                        {col.FSharpName()} = r.{col.CName}{optionalEnumMap}\n"
                        yield $"                                    }}:{fsharpName}) |> Array.ofSeq \n"
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
            yield $"let api = {{\n"
            for t in s.Tables do
                let fsharpName = t.FSharpName()
                yield $"    // -------------------------------------------\n"
                yield $"    Create{fsharpName} = Service.create{fsharpName}\n"
                yield $"    Read{fsharpName} = Service.read{fsharpName}\n"
                yield $"    Update{fsharpName} = Service.update{fsharpName}\n"
                yield $"    Delete{fsharpName} = Service.delete{fsharpName}\n"
                yield $"    List{fsharpName} = Service.list{fsharpName}\n"
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
            for t in s.Tables do
                let tableCap = t.FSharpName()
                let createReturnType =
                    match t.PKey with
                    | None -> t.PKeyTypeName()
                    | Some _ -> "unit"
                let wrap (signature:string) (call:string) =
                    stringBuffer {
                        yield $"let {signature} =\n"
                        yield $"    task {{\n"
                        yield $"        let! result = {call}\n"
                        yield $"        return Ok result\n"
                        yield $"    }}\n"
                        yield $"\n"
                    }
                yield $"// -------------------------------------------\n"
                yield wrap $"create{tableCap} (request:Create{tableCap}) : ApiResult<{createReturnType}>" $"Storage.create{tableCap} request"
                yield wrap $"read{tableCap} (request:{t.PKeyTypeName()}) : ApiResult<{tableCap} option>" $"Storage.read{tableCap} request"
                yield wrap $"update{tableCap} (request:Update{tableCap}) : ApiResult<unit>" $"Storage.update{tableCap} request"
                yield wrap $"delete{tableCap} (request:{t.PKeyTypeName()}) : ApiResult<unit>" $"Storage.delete{tableCap} request"
                yield wrap $"list{tableCap} (batchOffset:BatchOffset) : ApiResult<{tableCap} []>" $"Storage.list{tableCap} batchOffset.Offset batchOffset.BatchSize"
        }
    // Api contract + client.  Compiled into the shared project, so it must not depend on server or
    // database libraries.  A namespace (rather than a module) lets the server routing live in a
    // separate backend-only file while still being addressable as {domain}.Server
    let apiDef =
        stringBuffer {
            yield $"namespace {projCap}.Api.{domain}\n"
            yield $"\n"
            yield $"open System.Net.Http\n"
            yield $"open {projCap}.Common\n"
            yield $"open {projCap}.Domain.{domain}\n"

            yield $"\n"
            yield $"type Api ={{\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                match t.PKey with
                | None ->
                    yield $"    Create{tableCap} : Create{tableCap} -> ApiResult<{t.PKeyTypeName()}>\n"
                | Some _ ->
                    yield $"    Create{tableCap} : Create{tableCap} -> ApiResult<unit>\n"
                yield $"    Read{tableCap}   : {t.PKeyTypeName()} -> ApiResult<{tableCap} option>\n"
                yield $"    Update{tableCap} : Update{tableCap} -> ApiResult<unit>\n"
                yield $"    Delete{tableCap} : {t.PKeyTypeName()} -> ApiResult<unit>\n"
                yield $"    List{tableCap} : BatchOffset -> ApiResult<{tableCap} []>\n"
            yield $"}}\n"

            // -------------------------------------------
            // Client
            // -------------------------------------------
            yield $"\n"
            yield $"/// Typed client for the {s.SName} api.  basePath is relative to HttpClient.BaseAddress\n"
            yield $"type Client(http : HttpClient, basePath : string) =\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                let createReturnType =
                    match t.PKey with
                    | None -> t.PKeyTypeName()
                    | Some _ -> "unit"
                let path = $"{{basePath}}/{t.TName}"
                yield $"    member x.Create{tableCap}(doc:Create{tableCap}) : ApiResult<{createReturnType}> =\n"
                yield $"        ApiClient.post http $\"{path}/create\" doc\n"
                yield $"    member x.Read{tableCap}(docId:{t.PKeyTypeName()}) : ApiResult<{tableCap} option> =\n"
                match t.PKey with
                | Some _ -> yield $"        ApiClient.post http $\"{path}/read\" docId\n"
                | None -> yield $"        ApiClient.get http $\"{path}/read/{{docId}}\"\n"
                yield $"    member x.Update{tableCap}(doc:Update{tableCap}) : ApiResult<unit> =\n"
                yield $"        ApiClient.post http $\"{path}/update\" doc\n"
                yield $"    member x.Delete{tableCap}(docId:{t.PKeyTypeName()}) : ApiResult<unit> =\n"
                yield $"        ApiClient.post http $\"{path}/delete\" docId\n"
                // query parameter names must match what Handlers.withBatchOffset reads
                yield $"    member x.List{tableCap}(?offset:int, ?batchSize:int) : ApiResult<{tableCap} []> =\n"
                yield $"        ApiClient.get http (ApiClient.withQuery $\"{path}/list\" [ \"offset\", offset; \"batchSize\", batchSize ])\n"
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
            for t in s.Tables do
                let tableCap = t.FSharpName()
                if t.PKey.IsNone then
                    yield $"            routef \"/{t.TName}/read/%%i\" (H.withArg api.Read{tableCap})\n"
                yield $"            route \"/{t.TName}/list\" >=> H.withBatchOffset api.List{tableCap}\n"
            yield $"        ]\n"
            yield $"        POST >=> choose [\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                if t.PKey.IsSome then
                    yield $"            route \"/{t.TName}/read\" >=> H.withBody api.Read{tableCap}\n"
                yield $"            route \"/{t.TName}/create\" >=> H.withBody api.Create{tableCap}\n"
                yield $"            route \"/{t.TName}/update\" >=> H.withBody api.Update{tableCap}\n"
                yield $"            route \"/{t.TName}/delete\" >=> H.withBody api.Delete{tableCap}\n"
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
        yield $"/// Every schema's routes under /api - add this to your Giraffe app (app.UseGiraffe handler)\n"
        yield $"let handler (api : {projCap}Api) : Giraffe.Core.HttpHandler =\n"
        yield $"    Giraffe.Routing.subRoute \"/api\" (Giraffe.Core.choose [\n"
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
