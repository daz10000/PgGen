module PgGen.Build

let col (name:string) (cType:ColumnType) (modList:ColumnMatters list) =

    let hasMod (x:ColumnMatters) = modList |> List.contains x

    ColumnItem
        {   CName = name
            CType = cType
            Nullable = hasMod Nullable
            Array = hasMod Array
        }


let table (name:string) (tableAttr:TableAttr list) (body:TableBodyItem list) =
    {   TName = name
        Cols = [ for b in body do
                    match b with
                    | ColumnItem c ->
                        c
                    | _ -> ()
        ]

    }

// Doesn't really work smoothly
type Build() =
    static member col2(name:string,cType:ColumnType,?modList:ColumnMatters list) =
        let hasMod (x:ColumnMatters) =
            match modList with
            | None -> false
            | Some y -> y |> List.contains x

        {   CName = name
            CType = cType
            Nullable = hasMod Nullable
            Array = hasMod Array
        }
