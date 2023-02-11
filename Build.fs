module PgGen.Build

let col (name:string) (cType:ColumnType) (modList:Modifier list) =

    let hasMod (x:Modifier) = modList |> List.contains x

    {   CName = name
        CType = cType
        Nullable = hasMod Nullable
        Array = hasMod Array
    }
