module Eclipser.Fuzz

open System.Threading
open Utils
open Options

let private initializeSeeds opt =
  if opt.InputDir = "" then [Seed.make opt.FuzzSource]
  else System.IO.Directory.EnumerateFiles opt.InputDir // Obtain file list
       |> List.ofSeq // Convert list to array
       |> List.map System.IO.File.ReadAllBytes // Read in file contents
       |> List.map (Seed.makeWith opt.FuzzSource) // Create seed with content

let private preprocessAux opt seed =
  let newEdgeN, pathHash, edgeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.save opt seed newEdgeN pathHash edgeHash exitSig true
  if newEdgeN > 0 then Some (Favored, seed)
  elif isNewPath then Some (Normal, seed)
  else None

let private preprocess opt seeds =
  log "[*] Total %d initial seeds" (List.length seeds)
  let items = List.choose (preprocessAux opt) seeds
  let favoredCount = List.filter (fst >> (=) Favored) items |> List.length
  let normalCount = List.filter (fst >> (=) Normal) items |> List.length
  log "[*] %d initial items with high priority" favoredCount
  log "[*] %d initial items with low priority" normalCount
  items

let private makeNewItems seeds =
  let expand (pr, seed) = List.map (fun s -> (pr, s)) (Seed.relocateCursor seed)
  List.collect expand seeds

let private makeSteppedItems pr seed =
  match Seed.proceedCursor seed with
  | None -> []
  | Some s -> [(pr, s)]

let rec private fuzzLoop opt seedQueue =
  if SeedQueue.isEmpty seedQueue then
    Thread.Sleep(5000)
    fuzzLoop opt seedQueue
  else
    let pr, seed, seedQueue = SeedQueue.dequeue seedQueue
    if opt.Verbosity >= 1 then
      log "Grey-box concolic on %A seed : %s" pr (Seed.toString seed)
    let newSeeds = GreyConcolic.run seed opt
    // Move cursors of newly generated seeds.
    let newItems = makeNewItems newSeeds
    // Also generate seeds by just stepping the cursor of original seed.
    let steppedItems = makeSteppedItems pr seed
    let seedQueue = List.fold SeedQueue.enqueue seedQueue newItems
    let seedQueue = List.fold SeedQueue.enqueue seedQueue steppedItems
    // Perform random fuzzing
    fuzzLoop opt seedQueue

let private fuzzingTimer timeoutSec = async {
  let timespan = System.TimeSpan(0, 0, 0, timeoutSec)
  System.Threading.Thread.Sleep(timespan )
  printLine "Fuzzing timeout expired."
  log "===== Statistics ====="
  Manager.printStatistics ()
  log "Done, clean up and exit..."
  Executor.cleanup ()
  exit (0)
}

[<EntryPoint>]
let main args =
  let opt = parseFuzzOption args
  validateFuzzOption opt
  assertFileExists opt.TargetProg
  log "[*] Fuzz target : %s" opt.TargetProg
  log "[*] Time limit : %d sec" opt.Timelimit
  createDirectoryIfNotExists opt.OutDir
  Manager.initialize opt.OutDir
  Executor.initialize opt
  let emptyQueue = SeedQueue.initialize ()
  let initialSeeds = initializeSeeds opt
  let initItems = preprocess opt initialSeeds
  let initQueue = List.fold SeedQueue.enqueue emptyQueue initItems
  log "[*] Fuzzing starts"
  Async.Start (fuzzingTimer opt.Timelimit)
  fuzzLoop opt initQueue
  0 // Unreachable
