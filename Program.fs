module PgGen.Main

open Build
let owner = "read_write"

let db =  {
    DName = "proteins"
    Owner = "read_write"
    Schemas = [
        {   SName = "enzyme"
            Tables = [
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
            ]
        }
    ]
}

let output = Generate.emitDatabase db

printfn $"{output}"
