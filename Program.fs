module PgGen.Main

let owner = "read_write"

let db =  {
    DName = "proteins"
    Owner = "read_write"
    Schemas = [
        {   SName = "enzyme"
            Tables = [
                {
                        TName = "uniprot_entry"
                        Cols = [{   CName = "id" ; CType = Id ; Nullable = false ; Array = false}
                                {   CName = "name" ; CType = String ; Nullable = false ; Array = false}
                                {   CName = "common_name" ; CType = String ; Nullable = true ; Array = false}
                                {   CName = "accno" ; CType = String ; Nullable = false ; Array = false}
                                {   CName = "secondary" ; CType = String ; Nullable = false ; Array = true}
                            ]
                }
                {
                        TName = "organism"
                        Cols = [
                            { CName = "id" ; CType = Id ; Nullable = false ; Array = false}
                            { CName = "name" ; CType = String ; Nullable = false ; Array = false}
                            { CName = "id_taxon" ; CType = Int32 ; Nullable = true ; Array = false}
                            { CName = "common_name" ; CType = String ; Nullable = true ; Array = false}
                            { CName = "taxonomy" ; CType = String ; Nullable = true ; Array = true}
                        ]
                }
                {
                        TName = "tombstone"
                        Cols = [
                            { CName = "id" ; CType = Id ; Nullable = false ; Array = false}
                            { CName = "name" ; CType = String ; Nullable = false ; Array = false}
                            { CName = "processed" ; CType = Timestamp ; Nullable = false ; Array = false}
                        ]
                }
            ]
        }
    ]
}

let output = Generate.emitDatabase db

printfn $"{output}"
