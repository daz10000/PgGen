module PgGen.Main

open Build
let owner = "read_write"

let db =  {
    DName = "proteins"
    Owner = "read_write"
    Schemas = [
        {   SName = "enzyme"
            Tables = [
                {
                        TName = "uniprot_entry"
                        Cols = [col "id" Id []
                                col "name" String []
                                col "common_name" String [Nullable]
                                col "accno" String []
                                col "secondary" String [Array]
                            ]
                }
                {
                        TName = "organism"
                        Cols = [
                            col "id"  Id []
                            col "name"  String []
                            col "id_taxon"  Int32 [Nullable]
                            col "common_name"  String [Nullable]
                            col "taxonomy"  String [Nullable ; Array]
                        ]
                }
                {
                        TName = "tombstone"
                        Cols = [
                            col "id"  Id []
                            col "name"  String []
                            col "processed"  Timestamp []
                        ]
                }
            ]
        }
    ]
}

let output = Generate.emitDatabase db

printfn $"{output}"
