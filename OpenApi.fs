/// OpenAPI 3.1 description of the generated http api, built from the same model (ApiModel) as the
/// generated code, so the two cannot drift apart.
module PgGen.OpenApi

open System
open System.Text.Json
open System.Text.Json.Nodes
open Common
open ApiModel

let private obj (props:(string * JsonNode) list) : JsonNode =
    let o = JsonObject()
    for k, v in props do o[k] <- v
    o :> JsonNode

let private str (s:string) : JsonNode = JsonValue.Create(s) :> JsonNode
let private boolean (b:bool) : JsonNode = JsonValue.Create(b) :> JsonNode
let private arr (items:JsonNode list) : JsonNode =
    let a = JsonArray()
    for i in items do a.Add(i)
    a :> JsonNode

/// component schema name, qualified by the postgres schema so tables with the same name in
/// different schemas don't collide
let componentName (schemaCap:string) (typeName:string) = $"{schemaCap}.{typeName}"

let private reference (schemaCap:string) (typeName:string) =
    obj [ "$ref", str $"#/components/schemas/{componentName schemaCap typeName}" ]

let rec typeSchema (schemaCap:string) (t:TypeRef) : JsonNode =
    let prim (typ:string) (format:string option) =
        obj [ yield "type", str typ
              match format with
              | Some f -> yield "format", str f
              | None -> () ]
    match t with
    | TInt -> prim "integer" (Some "int32")
    | TInt64 -> prim "integer" (Some "int64")
    | TBool -> prim "boolean" None
    | TString -> prim "string" None
    | TJson -> obj [ "type", str "string"; "contentMediaType", str "application/json"
                     "description", str "A JSON document, as a string" ]
    | TFloat -> prim "number" (Some "double")
    | TDecimal -> prim "number" (Some "decimal")
    | TDateTime -> prim "string" (Some "date-time")
    | TGuid -> prim "string" (Some "uuid")
    | TBytes -> obj [ "type", str "string"; "contentEncoding", str "base64" ]
    | TUnit -> obj [ "type", str "null" ]
    | TEnum n | TRecord n -> reference schemaCap n
    | TArray inner -> obj [ "type", str "array"; "items", typeSchema schemaCap inner ]
    | TOption inner ->
        let innerSchema = typeSchema schemaCap inner
        match innerSchema["type"] with
        | :? JsonValue as v ->
            // primitive or array: widen the type to allow null
            let o = innerSchema.AsObject()
            o["type"] <- arr [ str (v.GetValue<string>()); str "null" ]
            o :> JsonNode
        | _ -> obj [ "oneOf", arr [ innerSchema; obj [ "type", str "null" ] ] ]

let private withDescription (doc:string option) (schema:JsonNode) =
    match doc with
    | Some d -> schema.AsObject()["description"] <- str d
    | None -> ()
    schema

let recordSchema (schemaCap:string) (r:RecordDef) =
    let props = JsonObject()
    for f in r.Fields do
        props[f.Name] <- typeSchema schemaCap f.Type |> withDescription f.Doc
    let required =
        [ for f in r.Fields do
            match f.Type with
            | TOption _ -> ()
            | _ -> yield str f.Name ]
    obj [ yield "type", str "object"
          yield "properties", (props :> JsonNode)
          if not required.IsEmpty then yield "required", arr required ]
    |> withDescription r.Doc

let enumSchema (e:EnumDef) =
    obj [ "type", str "string"; "enum", arr [ for _, wire in e.Cases -> str wire ] ]
    |> withDescription e.Doc

let private problemSchema =
    obj [ "type", str "object"
          "description", str "Error response. Title is the error kind (NotFound, Validation, Conflict, Unexpected)"
          "properties", obj [ "Title", obj [ "type", str "string"; "enum", arr [ str "NotFound"; str "Validation"; str "Conflict"; str "Unexpected" ] ]
                              "Status", obj [ "type", str "integer"; "format", str "int32" ]
                              "Detail", obj [ "type", str "string" ] ]
          "required", arr [ str "Title"; str "Status"; str "Detail" ] ]

let private errorResponse (description:string) =
    obj [ "description", str description
          "content", obj [ "application/json", obj [ "schema", obj [ "$ref", str "#/components/schemas/Problem" ] ] ] ]

let operationSchema (s:Schema) (op:Operation) =
    let schemaCap = titleCase s.SName
    let errors =
        [ match op.Arg with
          | JsonBody | RouteId | OffsetQuery -> yield "400", "Invalid request, or a check / not-null constraint failed"
          | NoArg -> ()
          match op.Kind with
          | UpdateOp | DeleteOp -> yield "404", "No such row (or not visible to the current tenant)"
          | _ -> ()
          match op.Kind with
          | CreateOp | UpdateOp | DeleteOp -> yield "409", "Unique or foreign key constraint violation"
          | _ -> ()
          yield "500", "Unexpected error (details are only in the server log)" ]
    obj [ yield "operationId", str $"{schemaCap}{op.Name}"
          yield "summary", str op.Summary
          yield "tags", arr [ str $"{s.SName}.{op.Table.TName}" ]
          match op.Arg with
          | RouteId ->
              yield "parameters",
                    arr [ obj [ "name", str "id"; "in", str "path"; "required", boolean true
                                "schema", typeSchema schemaCap op.ArgType ] ]
          | OffsetQuery ->
              yield "parameters",
                    arr [ for name, doc in [ "offset", "Rows to skip"; "batchSize", "Maximum rows to return" ] ->
                            obj [ "name", str name; "in", str "query"; "required", boolean false
                                  "description", str doc; "schema", typeSchema schemaCap TInt ] ]
          | JsonBody ->
              yield "requestBody",
                    obj [ "required", boolean true
                          "content", obj [ "application/json", obj [ "schema", typeSchema schemaCap op.ArgType ] ] ]
          | NoArg -> ()
          yield "responses",
                obj [ yield "200", obj [ "description", str "OK"
                                         "content", obj [ "application/json", obj [ "schema", typeSchema schemaCap op.Result ] ] ]
                      for code, description in errors -> code, errorResponse description ] ]

/// The OpenAPI document for every schema's api, as indented json.  Paths are relative to the
/// server's /api mount point.
let document (proj:string) (generatorVersion:string) (d:Db) =
    let paths = JsonObject()
    let schemas = JsonObject()
    schemas["Problem"] <- problemSchema
    for s in d.Schemas do
        let schemaCap = titleCase s.SName
        for e in s.Enums do
            let def = enumDef e
            schemas[componentName schemaCap def.Name] <- enumSchema def
        for t in s.Tables do
            for r in tableRecords t do
                schemas[componentName schemaCap r.Name] <- recordSchema schemaCap r
        for op in schemaOperations s do
            let path =
                match op.Arg with
                | RouteId -> $"/{s.SName}{op.Path}/{{id}}"
                | _ -> $"/{s.SName}{op.Path}"
            let verb = match op.Verb with Get -> "get" | Post -> "post"
            let item =
                match paths[path] with
                | null -> let o = JsonObject() in paths[path] <- o; o
                | existing -> existing.AsObject()
            item[verb] <- operationSchema s op
    let doc =
        obj [ "openapi", str "3.1.0"
              "info", obj [ "title", str $"{titleCase proj} api"
                            "version", str "1.0.0"
                            "description", str $"Generated by FSharp.PgGen {generatorVersion} from the {d.DName} database schema" ]
              "servers", arr [ obj [ "url", str "/api" ] ]
              "paths", (paths :> JsonNode)
              "components", obj [ "schemas", (schemas :> JsonNode) ] ]
    doc.ToJsonString(JsonSerializerOptions(WriteIndented = true))
