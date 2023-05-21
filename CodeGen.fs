


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
    s.Replace(" ","_").Replace("-","_").ToLowerInvariant()

let emitDomain (proj:string) (s:Schema) =
    let domain = titleCase s.SName
    let projCap = proj |> titleCase


    let domainCode =
        stringBuffer {
            yield $"namespace {projCap}.Domain.{domain}\n"

            let needsSystem =
                [for t in s.Tables do
                    for c in t.Cols do
                        if c.CType = Timestamp then
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
            yield $"open Plough.ControlFlow\n"

            // Any enums in the schema that need converter functions to/from F# types
            for e in s.Enums do

                let fSharpType = e.EName |> toFSharp
                let fSharpVar = e.EName |> toFSharpLower
                yield $"let {fSharpVar}ToEnum(v:{fSharpType}) =\n"
                yield $"    match v with\n"
                for v in e.EValues do
                    yield $"    | {fSharpType}.{v |> toFSharp} -> Db.{s.SName}.Types.{e.EName}.{v}\n"
                yield $"\n"
                yield $"let {fSharpVar}FromEnum(e:Db.{s.SName}.Types.{e.EName}) =\n"
                yield $"    match e with\n"
                for v in e.EValues do
                    yield $"    | Db.{s.SName}.Types.{e.EName}.{v} -> {fSharpType}.{v |> toFSharp}\n"
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
                                                    | Timestamp -> "'0001-01-01'"
                                                    | Jsonb -> "'{}'::jsonb"
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
                yield $"        use cmd = Db.CreateCommand<\"\"\"INSERT INTO {s.SName}.{t.TName} (\n"
                yield $"            {colNamesExceptKey}) \n                    VALUES({insertPlaceHolders}) {returning}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let newId = cmd.Execute({nonPKParametersForCall})\n"
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
                yield $"        use cmd = Db.CreateCommand<\"\"\""
                yield $"        SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} WHERE {readWhereClause}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let result = cmd.Execute({parameters})\n"
                yield $"        return result |> Option.map(fun r->\n"
                yield $"                                    {{\n"
                for col in t.FullCols() do
                    let optionalEnumMap =
                        match col.CType with
                        | Enum e -> $" |> {e |> toFSharpLower}FromEnum"
                        | _ -> ""
                    yield $"                                        {col.FSharpName()} = r.{col.CName}{optionalEnumMap}\n"
                yield $"                                    }})\n"
                yield $"    }}\n"
                yield $"\n"


                // ====================================================
                // Update code
                // ====================================================
                yield $"let update{fsharpName} (request:Update{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
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
                yield $"        let result = cmd.Execute({updateParameters})\n"
                yield $"        return ()\n"
                yield $"    }}\n"
                yield $"\n"
                // ====================================================
                // Delete code
                // ====================================================
                yield $"let delete{fsharpName} (id:{t.PKeyTypeName()}) =\n"
                yield $"    task {{\n"
                yield $"        failwithf \"Not implemented\"\n"
                yield $"        return \"unimplemented\"\n"
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
            yield $"open Plough.ControlFlow\n"
            yield $"\n"
            yield $"// PgGen: note - these are simple wrappers for now but business logic / transactions / multiple IO calls can be handled here\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                yield $"// -------------------------------------------\n"
                yield $"let create{tableCap} (request:Create{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.create{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let read{tableCap} (request:{t.PKeyTypeName()}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.read{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let update{tableCap} (request:Update{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.update{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let delete{tableCap} (request:{t.PKeyTypeName()}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.delete{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let list{tableCap} (batchOffset:BatchOffset) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.list{tableCap} batchOffset.Offset batchOffset.BatchSize\n"
                yield $"    }}\n"
                yield $"\n"
        }
    let apiDef =
        stringBuffer {
            yield $"module {projCap}.Api.{domain}\n"
            yield $"\n"
            yield $"open Plough.ControlFlow\n"
            yield $"open Plough.WebApi.Client\n"
            yield $"open Plough.WebApi.Server\n"
            yield $"open Plough.WebApi.Server.Plain\n"
            yield $"open {projCap}.Common\n"
            yield $"open {projCap}.Domain.{domain}\n"

            yield $"\n"
            yield $"type Api ={{\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                match t.PKey with
                | None ->
                    yield $"    Create{tableCap} : Create{tableCap} -> TaskEither<{t.PKeyTypeName()}>\n"
                | Some _ ->
                    yield $"    Create{tableCap} : Create{tableCap} -> TaskEither<unit>\n"
                yield $"    Read{tableCap}   : {t.PKeyTypeName()} -> TaskEither<{tableCap} option>\n"
                yield $"    Update{tableCap} : Update{tableCap} -> TaskEither<unit>\n"
                yield $"    Delete{tableCap} : {t.PKeyTypeName()} -> TaskEither<string>\n"
                yield $"    List{tableCap} : BatchOffset -> TaskEither<{tableCap} []>\n"
            yield $"}}\n"

            // -------------------------------------------
            // Server api
            // -------------------------------------------
            yield $"module Server =\n"


            yield $"    let build<'ctx> (api : Api) : ApiServer<'ctx> = subRoute \"/{s.SName}\" <| choose [\n"
            yield $"        GET >=> choose [\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                let canUseGet =
                    match t.PKey with
                    | None -> true
                    | Some _ -> false
                if canUseGet then
                    yield $"            routef \"/{t.TName}/read/%%i\" (makeJSONHandlerWithArgAsync api.Read{tableCap})\n"
                yield $"            route \"/{t.TName}/list\" >=> makeJSONHandlerWithQueryParamAsync api.List{tableCap} \n"
            yield $"        ]\n"
            yield $"        POST >=> choose [\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                let needsPostForGet =
                    match t.PKey with
                    | None -> false
                    | Some _ -> true
                if needsPostForGet then
                    yield $"            route \"/{t.TName}/read\" >=> makeJSONHandlerWithObjAsync api.Read{tableCap}\n"
                yield $"            route \"/{t.TName}/create\" >=> makeJSONHandlerWithObjAsync api.Create{tableCap}\n"
                yield $"            route \"/{t.TName}/update\" >=> makeJSONHandlerWithObjAsync api.Update{tableCap}\n"
                yield $"            route \"/{t.TName}/delete\" >=> makeJSONHandlerWithObjAsync api.Delete{tableCap}\n"
            yield $"        ]\n"
            yield $"    ]\n"

            // -------------------------------------------
            // Client
            // -------------------------------------------
            yield $"type Client(client : ClientBuilder) =\n"
            yield $"    inherit ClientBuilder(Nested(\"{s.SName}\", client))\n"
            yield $"\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                let createReturnType =
                    match t.PKey with
                    | None -> t.PKeyTypeName()
                    | Some _ -> "unit"
                yield $"    member x.Create{tableCap}(doc:Create{tableCap}) : TaskEither<{createReturnType}> =\n"
                yield $"        x.Post(\"{t.TName}/create\",doc)\n"
                yield $"    member x.Read{tableCap}(docId:int) : TaskEither<{tableCap} option> =\n"
                let needsPostForGet =
                    match t.PKey with
                    | None -> false
                    | Some _ -> true
                if needsPostForGet then
                    yield $"        x.Post(\"{t.TName}/read\",docId)\n"
                else
                    yield $"        x.Get <| $\"{t.TName}/read/{{docId}}\"\n"
                yield $"    member x.Update{tableCap}(doc:Update{tableCap}) : TaskEither<unit> =\n"
                yield $"        x.Post(\"{t.TName}/update\",doc)\n"

                yield $"    member x.Delete{tableCap}(docId:int) : TaskEither<string> =\n"
                yield $"        x.Post(\"{t.TName}/delete\",docId)\n"
                yield $"    member x.List{tableCap}(offset:int option,batchSize:int option) : TaskEither<{tableCap} []> =\n"
                yield $"        match offset,batchSize with\n"
                yield $"                 | Some o,Some b -> $\"{t.TName}/list?offset={{offset}}&batch={{batchSize}}\"\n"
                yield $"                 | None ,Some b -> $\"{t.TName}/list?batch={{batchSize}}\"\n"
                yield $"                 | Some o,None -> $\"{t.TName}/list?offset={{offset}}\"\n"
                yield $"                 | None ,None -> $\"{t.TName}/list\"\n"
                yield $"        |> x.Get\n"
            // yield $"        }}\n"
        }
    {| Storage = storageCode
       ApiWireup = apiWireup
       ApiDef = apiDef
       Domain = domainCode
       ServiceLayer = serviceLayer|}

let generateMasterServerApiFile (proj:string) (d:Db) =
    stringBuffer {
        yield $"module {proj |> titleCase}.Api.Server\n"
        let dbCap = d.DName |> titleCase
        yield $"open   {dbCap}.Api\n"
        yield $"\n"
        // Definition of the master Api type with functions
        yield $"type {dbCap}Api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} : {schemaCap}.Api\n"
        yield $"}}\n"


        // Now wire it up with references to the subapis
        yield $"let api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} = {dbCap}.Domain.{schemaCap}.Api.api\n"
        yield $"}}"

        yield $"\n"
    }
let generateMasterClientApiFile (proj:string) (d:Db) =
    let projCap = proj |> titleCase
    stringBuffer {
        yield $"namespace {projCap}.Api\n"
        yield $"open Plough.WebApi.Client\n"
        let dbCap = d.DName |> titleCase
        //yield $"open   {dbCap}.Api\n"
        //yield $"\n"
        // Definition of the master Api type with functions
        yield "/// Top level api type definition\n"
        yield $"type Api = {{\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    {schemaCap} : {schemaCap}.Api\n"
        yield $"}}\n"
        yield $"type {projCap} (client : ApiClient) =\n"
        yield $"    inherit ClientBuilder(Root(\"/api\", client))\n"
        for s in d.Schemas do
            let schemaCap = titleCase s.SName
            yield $"    member x.{schemaCap} = {schemaCap}.Client(x)\n"

        yield $"\n"
    }


let cleanSlash (path:string) =
    path.Replace("\\","/")
let generate (proj:string) (folder:string) (d:Db) =
    let projCap = titleCase proj
    ensureFolder folder
    let apiFolder = Path.Combine(folder, $"Api")
    ensureFolder apiFolder

    let generatedFileNames = [
        for schema in d.Schemas do
            let schemaCap = titleCase schema.SName
            printfn $"Generating schema {schemaCap}"
            let schemaSubFolder = $"{projCap}.Domain.{schemaCap}"
            let schemaFolder = Path.Combine(folder, $"{projCap}.Domain.{schemaCap}")
            ensureFolder schemaFolder

            let code = emitDomain proj schema


            let storageFile = Path.Combine(schemaSubFolder,$"{schemaCap}Storage.fs")
            let serviceFile = Path.Combine(schemaSubFolder,$"{schemaCap}Service.fs")
            let domainFile = Path.Combine(schemaSubFolder,$"{schemaCap}Domain.fs")
            let apiDefFile = Path.Combine("Api",$"{schemaCap}Api.fs")
            let apiWireupFile = Path.Combine("Api",$"{schemaCap}ApiWireup.fs")

            yield domainFile
            yield apiDefFile // api uses data structures from domainFile
            yield storageFile
            yield serviceFile
            yield apiWireupFile

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


    let apiFiles,nonApiFiles = generatedFileNames |> List.partition (fun x -> x.StartsWith("Api/") || x.StartsWith("Api\\"))

    let fileNames = auxFileNames @ nonApiFiles @ apiFiles
    // might be a good idea long term to strongly type these different file types since we need different subsets
    let domainFiles = nonApiFiles |> List.filter (fun x -> x.EndsWith("Domain.fs"))

    // write out an fsproj file with all the individual files in it
    let fsProjContent =
        stringBuffer {
            yield $"<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            yield $"  <PropertyGroup>\n"
            yield $"    <TargetFramework>net7.0</TargetFramework>\n"
            yield $"  </PropertyGroup>\n"
            yield $"  <ItemGroup>\n"
            for file in fileNames do
                yield $"    <Compile Include=\"{file|> cleanSlash}\" />\n"
            yield $"  </ItemGroup>\n"
            yield $"  <Import Project=\".paket\\Paket.Restore.targets\" />"
            yield $"</Project>\n"
        }
    let fsProjPath = Path.Combine(folder, $"{projCap}.Backend.fsproj")
    printfn $"Generating {fsProjPath|> cleanSlash}"
    File.WriteAllText(fsProjPath, fsProjContent)

    // write out a client api fsproj file with all the individual files in it
    let apiProjContent =
        stringBuffer {
            yield $"<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            yield $"  <PropertyGroup>\n"
            yield $"    <TargetFramework>net7.0</TargetFramework>\n"
            yield $"  </PropertyGroup>\n"
            yield $"  <ItemGroup>\n"
            yield $"    <Compile Include=\"../Common/Common.fs\" />\n"
            for file in domainFiles do
                yield $"    <Compile Include=\"../{file|> cleanSlash}\" />\n"
            for file in apiFiles do
                if file.EndsWith("Wireup.fs") ||
                    file.EndsWith($"{projCap}ServerApi.fs") then
                    () // skip these files
                else
                    yield $"    <Compile Include=\"{file[4..]|> cleanSlash}\" />\n" // remove Api/ prefix
            yield $"    <Compile Include=\"{projCap}ClientApi.fs\" />\n"
            yield $"  </ItemGroup>\n"
            yield $"  <Import Project=\"..\\.paket\\Paket.Restore.targets\" />"
            yield $"</Project>\n"
        }
    let apiProjPath = Path.Combine(folder,"Api",$"{projCap}Api.fsproj")
    printfn $"Generating {apiProjPath|> cleanSlash}"
    File.WriteAllText(apiProjPath, apiProjContent)


    let compileTimeDbFile = Path.Combine(folder,compileTimeDbFile)
    File.WriteAllText(compileTimeDbFile,$"Host=127.0.0.1;Username=read_write;Password=readwrite;Database={proj};Pooling=true")

    let paketDepsFile = Path.Combine(folder, "paket.dependencies")
    let paketRefsFile = Path.Combine(folder, "paket.references")
    let configFolder = Path.Combine(folder, ".config")
    ensureFolder configFolder
    File.WriteAllText(Path.Combine(configFolder, "dotnet-tools.json"), dotnetToolsJson)

    File.WriteAllText(paketDepsFile, paketFile)
    File.WriteAllText(paketRefsFile, paketReferencesFile)

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
