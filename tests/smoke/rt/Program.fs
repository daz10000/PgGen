// Runs the generated server (Giraffe on Kestrel) and drives it with the generated client
// against the pggen_smoke database.  Exit code = number of failed checks.
open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Giraffe
open Smoke.Common
open Smoke.Domain.Lab

Smoke.Db.Db.connectionString <-
    Some (Environment.GetEnvironmentVariable "PGGEN_SMOKE_CONN"
          |> Option.ofObj
          |> Option.defaultValue "Host=127.0.0.1;Username=read_write;Password=readwrite;Database=pggen_smoke")

let builder = WebApplication.CreateBuilder()
builder.Services.AddGiraffe() |> ignore
builder.WebHost.UseUrls("http://127.0.0.1:5089") |> ignore
let app = builder.Build()
let tenantFromHeader : HttpHandler =
    fun next ctx ->
        match ctx.TryGetRequestHeader "X-Tenant" with
        | Some t -> Smoke.Db.TenantRls.setAmbientFromValues t ""
        | None -> Smoke.Db.TenantRls.clearAmbient ()
        next ctx
app.UseGiraffe(tenantFromHeader >=> Smoke.Api.Server.handler Smoke.Api.Server.api)
app.StartAsync().Wait()

let http = new HttpClient(BaseAddress = Uri "http://127.0.0.1:5089/")
let client = Smoke.Api.Smoke(http)
let mutable failures = 0
let check name cond detail =
    if cond then printfn "PASS %s" name else (failures <- failures + 1; printfn "FAIL %s: %A" name detail)

task {
    let! orgId = client.Lab.CreateOrganism { Name = "E. coli"; IdTaxon = Some 562; Taxonomy = [| "Bacteria"; "Proteobacteria" |]; GenomeSize = None; IsReference = None; ExternalId = None; Aliases = None; Photo = None }
    check "create organism" (match orgId with Ok i -> i > 0 | _ -> false) orgId
    let orgId = match orgId with Ok i -> i | _ -> failwith "stop"

    let! org = client.Lab.ReadOrganism orgId
    check "read organism round trips" (match org with Ok (Some o) -> o.Name = "E. coli" && o.IdTaxon = Some 562 && o.GenomeSize = None && o.Taxonomy.Length = 2 | _ -> false) org

    let! missing = client.Lab.ReadOrganism 999999
    check "read missing -> Ok None" (missing = Ok None) missing

    let extId = Guid.NewGuid()
    let! upd = client.Lab.UpdateOrganism { Id = orgId; Name = "Escherichia coli"; IdTaxon = None; Taxonomy = [||]; GenomeSize = Some 4_600_000_000L; IsReference = Some false; ExternalId = Some extId; Aliases = Some [| "K-12" |]; Photo = Some [| 1uy; 2uy; 255uy |] }
    check "update organism" (upd = Ok ()) upd
    let! org2 = client.Lab.ReadOrganism orgId
    check "update persisted (None -> NULL, big int64)" (match org2 with Ok (Some o) -> o.Name = "Escherichia coli" && o.IdTaxon = None && o.GenomeSize = Some 4_600_000_000L && o.IsReference = Some false && o.ExternalId = Some extId && o.Aliases = Some [| "K-12" |] && o.Photo = Some [| 1uy; 2uy; 255uy |] | _ -> false) org2

    let! sid = client.Lab.CreateSample { Label = "s1"; Notes = None; Weight = Some 1.25M; Data = """{"a":1}"""; Created = DateTime.UtcNow; IdOrganism = orgId; Status = Status.InProgress }
    check "create sample (enum, decimal, jsonb)" (match sid with Ok _ -> true | _ -> false) sid
    let sid = match sid with Ok i -> i | _ -> 0
    let! s = client.Lab.ReadSample sid
    check "read sample enum/decimal" (match s with Ok (Some x) -> x.Status = Status.InProgress && x.Weight = Some 1.25M | _ -> false) s

    let! sid0 = client.Lab.CreateSample { Label = "zero"; Notes = Some ""; Weight = Some 0.0M; Data = "{}"; Created = DateTime.UtcNow; IdOrganism = orgId; Status = Status.DoneX }
    let! s0 = match sid0 with Ok i -> client.Lab.ReadSample i | Error e -> Threading.Tasks.Task.FromResult(Error e)
    check "Some \"\" and Some 0.0M are not stored as NULL" (match s0 with Ok (Some x) -> x.Notes = Some "" && x.Weight = Some 0.0M | _ -> false) s0
    let! org3 = client.Lab.CreateOrganism { Name = "neg"; IdTaxon = Some -1; Taxonomy = [||]; GenomeSize = Some -1L; IsReference = None; ExternalId = None; Aliases = None; Photo = None }
    let! o3 = match org3 with Ok i -> client.Lab.ReadOrganism i | Error e -> Threading.Tasks.Task.FromResult(Error e)
    check "Some -1 is not stored as NULL" (match o3 with Ok (Some o) -> o.IdTaxon = Some -1 && o.GenomeSize = Some -1L | _ -> false) o3
    let! raw = http.GetStringAsync($"api/lab/sample/read/{sid}")
    printfn "  wire format: %s" raw
    check "enum on the wire is the postgres label" (raw.Contains "\"Status\":\"in_progress\"") raw

    let! badFk = client.Lab.CreateSample { Label = "s2"; Notes = None; Weight = None; Data = "{}"; Created = DateTime.UtcNow; IdOrganism = 424242; Status = Status.NewX }
    check "FK violation -> Conflict" (match badFk with Error (Conflict _) -> true | _ -> false) badFk

    let! t1 = client.Lab.CreateSampleTag { Tag = "red"; IdSample = sid; TagStatus = None }
    check "create composite key row" (t1 = Ok ()) t1
    let! t2 = client.Lab.CreateSampleTag { Tag = "red"; IdSample = sid; TagStatus = Some Status.DoneX }
    check "duplicate composite key -> Conflict" (match t2 with Error (Conflict _) -> true | _ -> false) t2
    let! tr = client.Lab.ReadSampleTag { IdSample = sid; Tag = "red" }
    check "composite key read (POST)" (match tr with Ok (Some x) -> x.Tag = "red" | _ -> false) tr

    let! page = client.Lab.ListOrganism(offset = 0, batchSize = 1)
    check "list with paging" (match page with Ok xs -> xs.Length = 1 | _ -> false) page
    let! all = client.Lab.ListSample()
    check "list without paging" (match all with Ok xs -> xs.Length >= 1 | _ -> false) all

    // ---- layer 1 queries: count, keyset paging, fk and unique lookups
    for i in 1 .. 5 do
        let! _ = client.Lab.CreateOrganism { Name = $"bulk{i}"; IdTaxon = None; Taxonomy = [||]; GenomeSize = None; IsReference = None; ExternalId = None; Aliases = None; Photo = None }
        ()
    let! total = client.Lab.CountOrganism()
    check "count" (match total with Ok n -> n >= 7L | _ -> false) total
    let total = match total with Ok n -> int n | _ -> 0
    let walk () =
        task {
            let mutable after = None
            let mutable ids = []
            let mutable pages = 0
            let mutable fin = false
            while not fin do
                match! client.Lab.PageOrganism { After = after; Limit = Some 2 } with
                | Ok p ->
                    pages <- pages + 1
                    ids <- ids @ (p.Items |> Array.toList |> List.map (fun o -> o.Id))
                    match p.Next with
                    | Some n -> after <- Some n
                    | None -> fin <- true
                | Error e -> failwithf "page failed %A" e
            return ids, pages
        }
    let! ids, pages = walk ()
    check "keyset paging visits every row once, in order" (ids.Length = total && ids = List.sort ids && List.distinct ids = ids && pages >= total / 2) (ids, pages, total)

    let! byOrg = client.Lab.ListSampleByIdOrganism { IdOrganism = orgId; After = None; Limit = None }
    check "list_by_id_organism" (match byOrg with Ok p -> p.Items.Length = 2 && p.Items |> Array.forall (fun x -> x.IdOrganism = orgId) && p.Next = None | _ -> false) byOrg
    let! byLabel = client.Lab.ReadSampleByLabel { Label = "s1" }
    check "read_by_label" (match byLabel with Ok (Some x) -> x.Id = sid | _ -> false) byLabel
    let! byLabelMissing = client.Lab.ReadSampleByLabel { Label = "nope" }
    check "read_by_label missing -> None" (byLabelMissing = Ok None) byLabelMissing
    let! dupLabel = client.Lab.CreateSample { Label = "s1"; Notes = None; Weight = None; Data = "{}"; Created = DateTime.UtcNow; IdOrganism = orgId; Status = Status.NewX }
    check "duplicate unique label -> Conflict" (match dupLabel with Error (Conflict _) -> true | _ -> false) dupLabel

    let! _ = client.Lab.CreateSampleTag { Tag = "blue"; IdSample = sid; TagStatus = None }
    let! _ = client.Lab.CreateSampleTag { Tag = "green"; IdSample = sid; TagStatus = Some Status.InProgress }
    let! tp1 = client.Lab.PageSampleTag { After = None; Limit = Some 2 }
    let next = match tp1 with Ok p -> p.Next | _ -> None
    let! tp2 = client.Lab.PageSampleTag { After = next; Limit = Some 2 }
    check "composite key keyset paging" (match tp1, tp2 with
                                          | Ok p1, Ok p2 -> (Array.append p1.Items p2.Items |> Array.map (fun x -> x.Tag)) = [| "blue"; "green"; "red" |]
                                          | _ -> false) (tp1, tp2)
    let! green = client.Lab.ReadSampleTag { IdSample = sid; Tag = "green" }
    check "nullable enum round trip" (match green with Ok (Some x) -> x.TagStatus = Some Status.InProgress | _ -> false) green
    let! _ = client.Lab.DeleteSampleTag { IdSample = sid; Tag = "blue" }
    let! _ = client.Lab.DeleteSampleTag { IdSample = sid; Tag = "green" }

    let! rawPage = http.PostAsync("api/lab/organism/page", new StringContent("{}", Text.Encoding.UTF8, "application/json"))
    let! rawPageBody = rawPage.Content.ReadAsStringAsync()
    check "non F# client: empty json body uses defaults" (int rawPage.StatusCode = 200 && rawPageBody.Contains "\"Items\"") rawPageBody

    let! spec = http.GetStringAsync("api/openapi.json")
    let specDoc = Text.Json.JsonDocument.Parse(spec)
    let paths = [ for p in specDoc.RootElement.GetProperty("paths").EnumerateObject() -> p.Name ]
    check "openapi.json served, 3.1, lists the routes" (specDoc.RootElement.GetProperty("openapi").GetString() = "3.1.0"
                                                     && [ "/lab/sample/page"; "/lab/sample/list_by_id_organism"; "/lab/sample/read_by_label"
                                                          "/lab/sample_tag/page"; "/lab/organism/read/{id}"; "/lab/organism/count" ]
                                                        |> List.forall (fun p -> paths |> List.contains p)) paths

    let! delParent = client.Lab.DeleteOrganism orgId
    check "delete row still referenced -> Conflict" (match delParent with Error (Conflict _) -> true | _ -> false) delParent
    let! delTag = client.Lab.DeleteSampleTag { IdSample = sid; Tag = "red" }
    check "delete composite key row" (delTag = Ok ()) delTag
    let! delTag2 = client.Lab.DeleteSampleTag { IdSample = sid; Tag = "red" }
    check "delete again -> NotFound" (match delTag2 with Error (NotFound _) -> true | _ -> false) delTag2
    let! delSample = client.Lab.DeleteSample sid
    let! goneSample = client.Lab.ReadSample sid
    check "delete sample, then read -> None" (delSample = Ok () && goneSample = Ok None) (delSample, goneSample)
    let! delMissing = client.Lab.DeleteOrganism 999999
    check "delete missing -> NotFound" (match delMissing with Error (NotFound _) -> true | _ -> false) delMissing
    let! updMissing = client.Lab.UpdateOrganism { Id = 999999; Name = "x"; IdTaxon = None; Taxonomy = [||]; GenomeSize = None; IsReference = None; ExternalId = None; Aliases = None; Photo = None }
    check "update missing -> NotFound" (match updMissing with Error (NotFound _) -> true | _ -> false) updMissing

    let! badQ = http.GetAsync("api/lab/organism/list?offset=abc")
    let! badQBody = badQ.Content.ReadAsStringAsync()
    check "bad query param -> 400 problem" (int badQ.StatusCode = 400 && badQBody.Contains "Validation") badQBody

    let! badBody = http.PostAsync("api/lab/organism/create", new StringContent("{not json", Text.Encoding.UTF8, "application/json"))
    check "malformed body -> 400" (int badBody.StatusCode = 400) badBody.StatusCode

    // RLS: no tenant set for the request -> policy rejects the insert
    let! inv = client.Lab.CreateInvoice { TenantId = Guid.NewGuid(); Amount = 10M }
    check "RLS blocks insert without tenant" (match inv with Error _ -> true | _ -> false) inv

    let tenantA, tenantB = Guid.NewGuid(), Guid.NewGuid()
    let clientFor (t:Guid) =
        let h = new HttpClient(BaseAddress = Uri "http://127.0.0.1:5089/")
        h.DefaultRequestHeaders.Add("X-Tenant", string t)
        Smoke.Api.Smoke(h)
    let! invA = (clientFor tenantA).Lab.CreateInvoice { TenantId = tenantA; Amount = 10M }
    check "RLS insert with matching tenant" (match invA with Ok _ -> true | _ -> false) invA
    let invId = match invA with Ok i -> i | _ -> 0
    let! seenA = (clientFor tenantA).Lab.ReadInvoice invId
    check "tenant A sees its invoice" (match seenA with Ok (Some _) -> true | _ -> false) seenA
    let! seenB = (clientFor tenantB).Lab.ReadInvoice invId
    check "tenant B cannot see it" (seenB = Ok None) seenB
    let! spoof = (clientFor tenantB).Lab.CreateInvoice { TenantId = tenantA; Amount = 1M }
    check "tenant B cannot insert as A" (match spoof with Error _ -> true | _ -> false) spoof
    let! delB = (clientFor tenantB).Lab.DeleteInvoice invId
    check "tenant B cannot delete A's row -> NotFound" (match delB with Error (NotFound _) -> true | _ -> false) delB
    let! delA = (clientFor tenantA).Lab.DeleteInvoice invId
    check "tenant A deletes its row" (delA = Ok ()) delA
}
|> fun t -> t.Wait()
app.StopAsync().Wait()
printfn "%d failures" failures
exit failures
