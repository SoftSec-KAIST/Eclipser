module Eclipser.Sync

open System.IO
open System.Collections.Generic
open Utils
open Options

(*** Map of the maximum ID of the already imported test cases. ***)
let private maxImports = new Dictionary<string,int> ()

let private tryParseTCNum (tcPath: string) =
  let tcName = Path.GetFileName(tcPath)
  if not(tcName.StartsWith("id:")) then None
  else try Some (int (tcName.[3..8])) with _ -> None

let private importSeed opt tcPath seedQueue =
  let tcBytes = File.ReadAllBytes(tcPath)
  let seed = Seed.makeWith opt.FuzzSource tcBytes
  let covGain = Executor.getCoverage opt seed |> snd
  match Priority.ofCoverageGain covGain with
  | None -> seedQueue
  | Some priority -> SeedQueue.enqueue seedQueue (priority, seed)

let private syncTestCase opt maxImport (accSeedQueue, accMaxImport) tcPath =
  match tryParseTCNum tcPath with
  | None -> (accSeedQueue, accMaxImport)
  | Some num when num <= maxImport -> (accSeedQueue, accMaxImport)
  | Some num -> // Unhandled test case ID.
    log "Synchronizing seed queue with %s" tcPath
    let accMaxImport = if num > accMaxImport then num else accMaxImport
    let accSeedQueue = importSeed opt tcPath accSeedQueue
    (accSeedQueue, accMaxImport)

let private syncFromDir opt seedQueue dir =
  let maxImport = if maxImports.ContainsKey(dir) then maxImports.[dir] else 0
  let tcDir = Path.Combine(dir, "queue")
  let tcList = Directory.EnumerateFiles(tcDir) |> List.ofSeq
  let folder = syncTestCase opt maxImport
  let seedQueue, newMaxImport = List.fold folder (seedQueue, maxImport) tcList
  if newMaxImport > maxImport then maxImports.[dir] <- newMaxImport
  seedQueue

let run opt seedQueue =
  let outDir = Path.GetFullPath(opt.OutDir)
  let syncDir = Path.GetFullPath(opt.SyncDir)
  let subDirs = Directory.EnumerateDirectories(syncDir) |> List.ofSeq
                |> List.map (fun dirName -> Path.Combine(syncDir, dirName))
                |> List.filter (fun d -> d <> outDir) // Exclude our own output.
  Executor.disableRoundStatistics()
  TestCase.disableRoundStatistics()
  let newSeedQueue = List.fold (syncFromDir opt) seedQueue subDirs
  Executor.enableRoundStatistics()
  TestCase.enableRoundStatistics()
  newSeedQueue
