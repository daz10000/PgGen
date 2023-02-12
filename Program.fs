module PgGen.Main

open Build
let owner = "read_write"

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
                table "tombstone" [] [
                            col "id" Id []
                            col "name"  String []
                            col "processed"  Timestamp []
                        ]
                // table "evidence" [] [
                //     col "id" Id []
                //     col "evidence_code" String []
                //     col "source" String []
                //     col "unicode_id" Int32 []
                // ]

                table "uniprot_data" [ Comment "largely json structured data"] [
                    col "id" Id []
                    col "keywords" Jsonb [] // id, category, name triples
                    col "genes" Jsonb []
                    col "comments" Jsonb []
                    col "features" Jsonb []
                    frefId "uniprot_entry" // associated uniprot_entry
                ]

                table "uniprot_rxn" [] [
                    col "id" Id []
                    col "name" String []
                    frefId "uniprot_entry"
                    col "refs" String [ Array ] // "RHEA:39799", "CHEBI:15378"
                ]

            ] ]

let output = Generate.emitDatabase db

printfn $"{output}"
