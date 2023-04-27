


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
                yield $"type Create{fsharpName} = {{\n"
                for col in colsExceptKey do
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
                    yield $"    {col.FSharpName()} : {col.CType.FSharpType()}{arrayModifier}{optionModifier}\n"
                for fr in t.FRefs do
                    if fr.Generate then
                        let colName = fr.Name |> Option.defaultValue $"id_{fr.ToTable}" |> toFSharp
                        yield $"    {colName} : int\n"
                    else
                        failwithf "Not implemented - not integer foreign key references"
                yield $"}}"
                yield "\n"

                yield $"type Update{fsharpName} = {{\n"
                for col in colsExceptKey do
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
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
                yield $"}}"
                yield "\n"
                yield $"type {fsharpName} = {{\n"
                for col in t.Cols do
                    let optionModifier =
                        if col.Nullable then
                            " option"
                        else
                            ""
                    let arrayModifier = if col.Array then " []" else ""
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
                yield $"}}"

                // Tables that aren't using a simple integer key need a custom primary key type
                match t.PKey with
                | Some pk ->
                    yield $"\n"
                    yield $"type {t.PKeyName()}= {{\n"
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

            for t in s.Tables do
                let colsExceptKey = t.FullCols() |> List.filter (fun c -> c.CType <> Id) // FIXFIX - generalize to non int keys
                let colNamesExceptKey = String.Join(",",[for c in colsExceptKey  -> postgrestify c.CName])
                let placeHolders =
                    String.Join(",",[for c in colsExceptKey  ->
                                            if c.Nullable then
                                                let defaultVPostgres =
                                                    match c.CType with
                                                    | Int32 -> "-1"
                                                    | String -> "''"
                                                    | Timestamp -> "'0001-01-01'"
                                                    | Jsonb -> "'{}'::jsonb"
                                                    | _ -> failwithf $"Not implemented - default value for {c.CType}"
                                                $"(CASE WHEN @{c.CName} = {defaultVPostgres} THEN NULL ELSE @{c.CName} END)"
                                            else
                                                $"@{c.CName}"
                                    ]
                    )
                let fullPostgresCols =
                    String.Join(",",[for c in t.FullCols()  -> postgrestify c.CName])
                let assignedValues =
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
                                                    | Jsonb -> "\"'{}'\""
                                                    | _ -> failwithf $"Not implemented - default value for {c.CType}"
                                                $"(request.{c.FSharpName()} |> Option.defaultValue {defaultV})"
                                            else
                                                $"request.{c.FSharpName()}"
                                        $"{postgrestify c.CName}={optionalNullHack}"])

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
                yield $"let create{fsharpName} (request:Create{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\"INSERT INTO {s.SName}.{t.TName} (\n"
                yield $"            {colNamesExceptKey}) VALUES({placeHolders}) {returning}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let newId = cmd.Execute({assignedValues})\n"
                match t.PKey with
                | None ->
                    yield $"        return newId.Value // returns option but should be reliable\n"
                | Some _ ->
                    yield $"        return () // complex primary key, no return value\n"
                yield $"    }}\n"
                yield $"\n"
                yield $"let read{fsharpName} (request:{t.PKeyName()}) : Task<{fsharpName} option>=\n"
                yield $"    task {{\n"
                yield $"        use! conn = Db.openConnectionAsync()\n"
                yield $"        use cmd = Db.CreateCommand<\"\"\""
                yield $"        SELECT {fullPostgresCols} FROM {s.SName}.{t.TName} WHERE {readWhereClause}\n"
                yield $"                                   \"\"\",SingleRow=true>(conn)\n"
                yield $"        let result = cmd.Execute({parameters})\n"
                yield $"        return result |> Option.map(fun r->\n"
                yield $"                                    {{\n"
                for col in t.FullCols() do
                    yield $"                                        {col.FSharpName()} = r.{col.CName}\n"
                yield $"                                    }})\n"
                yield $"    }}\n"
                yield $"\n"
                yield $"let update{fsharpName} (x:Update{fsharpName}) =\n"
                yield $"    task {{\n"
                yield $"        failwithf \"Not implemented\"\n"
                yield $"    }}"
                yield $"\n"
                yield $"let delete{fsharpName} (id:{t.PKeyName()}) =\n"
                yield $"    task {{\n"
                yield $"        failwithf \"Not implemented\"\n"
                yield $"        return \"unimplemented\"\n"
                yield $"    }}"
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
            yield "}\n"

        }

    let serviceLayer =
        stringBuffer {
            yield $"module {projCap}.Domain.{domain}.Service\n"
            yield $"\n"
            yield $"open {projCap}.Domain.{domain}\n"
            // yield $"open Plough.Common\n"
            yield $"open Plough.ControlFlow\n"
            yield $"\n"
            yield $"// PgGen: note - these are simple wrappers for now\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                yield $"// -------------------------------------------\n"
                yield $"let create{tableCap} (request:Create{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.create{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let read{tableCap} (request:{t.PKeyName()}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.read{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let update{tableCap} (request:Update{tableCap}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.update{tableCap} request\n"
                yield $"    }}\n"
                yield $"\n"

                yield $"let delete{tableCap} (request:{t.PKeyName()}) =\n"
                yield $"    taskEither {{\n"
                yield $"        return! Storage.delete{tableCap} request\n"
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
            yield $"open {projCap}.Domain.{domain}\n"

            yield $"\n"
            yield $"type Api ={{\n"
            for t in s.Tables do
                let tableCap = t.FSharpName()
                match t.PKey with
                | None ->
                    yield $"    Create{tableCap} : Create{tableCap} -> TaskEither<{t.PKeyName()}>\n"
                | Some _ ->
                    yield $"    Create{tableCap} : Create{tableCap} -> TaskEither<unit>\n"
                yield $"    Read{tableCap}   : {t.PKeyName()} -> TaskEither<{tableCap} option>\n"
                yield $"    Update{tableCap} : Update{tableCap} -> TaskEither<unit>\n"
                yield $"    Delete{tableCap} : {t.PKeyName()} -> TaskEither<string>\n"
            yield $"}}"
        }

    {| Storage = storageCode
       ApiWireup = apiWireup
       ApiDef = apiDef
       Domain = domainCode
       ServiceLayer = serviceLayer|}


let commonFileSource proj =
    stringBuffer {
        yield $"module {proj |> titleCase}.Common\n"
        yield $"\n"
        yield $"open Plough.ControlFlow\n"
        yield $"open System\n"
        yield $"\n"
        yield $"// Shared data structures like User definitions go here\n"
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
