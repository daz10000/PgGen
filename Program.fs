module PgGen.Main

open Build

let db =
    db "proteins" [ Owner "read_write" ] [
        schema "enzyme" [] [
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

                table "uniprot_entry" [] [
                                col "id" Id []
                                col "uniprot_kb_id" String [Nullable]
                                col "common_name" String [Nullable]
                                col "accno" String []
                                col "secondary" String [Array]
                                col "entry_version" Int32 []
                                col "seq_version" Int32 []
                                col "last_annotation_update" Timestamp []
                                col "last_seq_update" Timestamp []
                                frefId "ec_group"
                                frefId "protein_sequence"
                                frefId "organism"
                                unique ["id_ec_group";"accno"]
                            ]

                table "uniprot_data" [ Comment "largely json structured data"] [
                    col "id" Id []
                    col "keywords" Jsonb [Array] // id, category, name triples
                    col "refs" Jsonb [Array]
                    col "comments" Jsonb [Array]
                    col "genes" Jsonb [Array]
                    col "features" Jsonb [Array]

                    col "created" Timestamp []
                    col "updated" Timestamp []
                    frefId "uniprot_entry" // associated uniprot_entry
                ]

                table "uniprot_rxn" [Comment "text description of reactions and references to other rxn dbs"] [
                    col "id" Id []
                    col "name" String []
                    frefId "uniprot_entry"
                    col "refs" String [ Array ] // "RHEA:39799", "CHEBI:15378"
                ]

            ] ]

let output = Generate.emitDatabase db

printfn $"{output}"
