// Smoke test spec: covers enums with F# keyword values, FKs, Guid without Timestamp, nullable
// Int32/Id64/Decimal/String/Bool/Guid/Blob/enum, arrays, jsonb, RLS, a composite key, unique and
// explicit indexes.  Run via run.sh.
// usage: dotnet fsi spec.fsx <output folder>
#r "../../bin/Debug/net10.0/PgGen.dll"
open PgGen
open PgGen.Build

let smoke =
    db "pggen_smoke" [ Owner "read_write" ] [
        schema "lab" [ SComment "smoke test schema" ] [
            enumDef "status" [ Member "new"; Member "in_progress"; Member "done"; EComment "work status" ]
            table "organism" [] [
                col "id" Id []
                col "name" String []
                col "id_taxon" Int32 [ Nullable ]
                col "taxonomy" String [ Array ]
                col "genome_size" Id64 [ Nullable ]
                col "is_reference" Bool [ Nullable ]       // nullable types that had no sentinel
                col "external_id" Guid [ Nullable ]
                col "aliases" String [ Nullable; Array ]
                col "photo" Blob [ Nullable ]
                query "by_name" [ Filter [ Prefix "name"; Eq "is_reference" ] ]       // unordered: key order
            ]
            table "sample" [ Comment "a physical sample" ] [
                col "id" Id []
                col "label" String [ CComment "human readable label" ]
                col "notes" String [ Nullable ]
                col "weight" Decimal [ Nullable ]
                col "data" Jsonb []
                col "created" Timestamp []
                frefId "organism" []
                enum "status" []
                unique [ "label" ]                            // -> read_by_label
                index [ "created" ]
                query "search" [
                    Filter [ Eq "id_organism"; AnyOf "status"; Range "created"; Prefix "label"; Contains "notes" ]
                    OrderBy [ Desc "created" ] ]
            ]
            table "invoice" [ rlsTenant "tenant_id" ] [   // Guid but no Timestamp -> needs open System
                col "id" Id []
                col "tenant_id" Guid []
                col "amount" Decimal []
            ]
            table "sample_tag" [] [
                col "tag" String []
                frefId "sample" []
                enum "status" [ EName "tag_status"; ENullable ]   // nullable enum
                pKey [ "id_sample"; "tag" ]
            ]
        ]
    ]

let outFolder = fsi.CommandLineArgs.[1]
System.IO.Directory.CreateDirectory outFolder |> ignore
System.IO.File.WriteAllText(System.IO.Path.Combine(outFolder, "smoke.sql"), Generate.emitDatabase smoke)
CodeGen.generate "smoke" (System.IO.Path.Combine(outFolder, "out")) smoke
