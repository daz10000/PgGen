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
    let! orgId = client.Lab.CreateOrganism { Name = "E. coli"; IdTaxon = Some 562; Taxonomy = [| "Bacteria"; "Proteobacteria" |]; GenomeSize = None }
    check "create organism" (match orgId with Ok i -> i > 0 | _ -> false) orgId
    let orgId = match orgId with Ok i -> i | _ -> failwith "stop"

    let! org = client.Lab.ReadOrganism orgId
    check "read organism round trips" (match org with Ok (Some o) -> o.Name = "E. coli" && o.IdTaxon = Some 562 && o.GenomeSize = None && o.Taxonomy.Length = 2 | _ -> false) org

    let! missing = client.Lab.ReadOrganism 999999
    check "read missing -> Ok None" (missing = Ok None) missing

    let! upd = client.Lab.UpdateOrganism { Id = orgId; Name = "Escherichia coli"; IdTaxon = None; Taxonomy = [||]; GenomeSize = Some 4_600_000_000L }
    check "update organism" (upd = Ok ()) upd
    let! org2 = client.Lab.ReadOrganism orgId
    check "update persisted (None -> NULL, big int64)" (match org2 with Ok (Some o) -> o.Name = "Escherichia coli" && o.IdTaxon = None && o.GenomeSize = Some 4_600_000_000L | _ -> false) org2

    let! sid = client.Lab.CreateSample { Label = "s1"; Notes = None; Weight = Some 1.25M; Data = """{"a":1}"""; Created = DateTime.UtcNow; IdOrganism = orgId; Status = Status.InProgress }
    check "create sample (enum, decimal, jsonb)" (match sid with Ok _ -> true | _ -> false) sid
    let sid = match sid with Ok i -> i | _ -> 0
    let! s = client.Lab.ReadSample sid
    check "read sample enum/decimal" (match s with Ok (Some x) -> x.Status = Status.InProgress && x.Weight = Some 1.25M | _ -> false) s

    let! raw = http.GetStringAsync($"api/lab/sample/read/{sid}")
    printfn "  wire format: %s" raw
    check "enum on the wire is a plain string" (raw.Contains "\"Status\":\"InProgress\"") raw

    let! badFk = client.Lab.CreateSample { Label = "s2"; Notes = None; Weight = None; Data = "{}"; Created = DateTime.UtcNow; IdOrganism = 424242; Status = Status.NewX }
    check "FK violation -> Conflict" (match badFk with Error (Conflict _) -> true | _ -> false) badFk

    let! t1 = client.Lab.CreateSampleTag { Tag = "red"; IdSample = sid }
    check "create composite key row" (t1 = Ok ()) t1
    let! t2 = client.Lab.CreateSampleTag { Tag = "red"; IdSample = sid }
    check "duplicate composite key -> Conflict" (match t2 with Error (Conflict _) -> true | _ -> false) t2
    let! tr = client.Lab.ReadSampleTag { IdSample = sid; Tag = "red" }
    check "composite key read (POST)" (match tr with Ok (Some x) -> x.Tag = "red" | _ -> false) tr

    let! page = client.Lab.ListOrganism(offset = 0, batchSize = 1)
    check "list with paging" (match page with Ok xs -> xs.Length = 1 | _ -> false) page
    let! all = client.Lab.ListSample()
    check "list without paging" (match all with Ok xs -> xs.Length >= 1 | _ -> false) all

    let! del = client.Lab.DeleteOrganism orgId
    check "delete stub -> Unexpected" (match del with Error (Unexpected _) -> true | _ -> false) del

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
}
|> fun t -> t.Wait()
app.StopAsync().Wait()
printfn "%d failures" failures
exit failures
