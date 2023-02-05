
/// http://www.fssnip.net/7WR/title/Computation-expression-over-StringBuilder
module PgGen.StringBuffer
    open System.Text

    type StringBuffer = StringBuilder -> unit

    type StringBufferBuilder () =
        member inline __.Yield (txt: string) = fun (b: StringBuilder) -> Printf.bprintf b "%s" txt
        member inline __.Yield (c: char) = fun (b: StringBuilder) -> Printf.bprintf b "%c" c
        member inline __.Yield (strings: #seq<string>) =
            fun (b: StringBuilder) -> for s in strings do Printf.bprintf b "%s\n" s
        member inline __.YieldFrom (f: StringBuffer) = f
        member __.Combine (f, g) = fun (b: StringBuilder) -> f b; g b
        member __.Delay f = fun (b: StringBuilder) -> (f()) b
        member __.Zero () = ignore

        member __.For (xs: 'a seq, f: 'a -> StringBuffer) =
            fun (b: StringBuilder) ->
                use e = xs.GetEnumerator ()
                while e.MoveNext() do
                    (f e.Current) b

        member __.While (p: unit -> bool, f: StringBuffer) =
            fun (b: StringBuilder) -> while p () do f b

        member __.Run (f: StringBuffer) =
            let b = StringBuilder()
            do f b
            b.ToString()

    let stringBuffer = new StringBufferBuilder ()

    type StringBufferBuilder with
      member inline __.Yield (b: byte) = fun (sb: StringBuilder) -> Printf.bprintf sb "%02x " b
