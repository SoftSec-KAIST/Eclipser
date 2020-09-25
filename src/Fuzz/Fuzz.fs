module Eclipser.Fuzz

open System.Threading
open Utils
open Options

let private printFoundSeed verbosity seed =
  if verbosity >= 1 then
    log "[*] Found by grey-box concolic: %s" (Seed.toString seed)
  elif verbosity >= 0 then
    log "[*] Found by grey-box concolic %s" (Seed.toString seed)

let private evalSeed opt seed exitSig covGain =
  TestCase.save opt seed exitSig covGain
  if covGain = NewEdge then printFoundSeed opt.Verbosity seed
  let isAbnormal = Signal.isTimeout exitSig || Signal.isCrash exitSig
  if isAbnormal then None else Priority.ofCoverageGain covGain

let private initializeSeeds opt =
  if opt.InputDir = "" then [Seed.make opt.FuzzSource]
  else System.IO.Directory.EnumerateFiles opt.InputDir // Obtain file list
       |> List.ofSeq // Convert list to array
       |> List.map System.IO.File.ReadAllBytes // Read in file contents
       |> List.map (Seed.makeWith opt.FuzzSource) // Create seed with content

let private makeInitialItems opt seed =
  let exitSig, covGain = Executor.getCoverage opt seed
  TestCase.save opt seed exitSig covGain
  Option.map (fun pr -> (pr, seed)) (Priority.ofCoverageGain covGain)

let private makeRelocatedItems opt seeds =
  let collector (seed, exitSig, covGain) =
    match evalSeed opt seed exitSig covGain with
    | None -> []
    | Some pr -> List.map (fun s -> (pr, s)) (Seed.relocateCursor seed)
  List.collect collector seeds

let private makeSteppedItems pr seed =
  match Seed.proceedCursor seed with
  | None -> []
  | Some s -> [(pr, s)]

let syncWithAFL opt seedQueue n =
  // Sychronize the seed queue with AFL instances every ten iterations.
  if n % 10 = 0 && opt.SyncDir <> "" then Sync.run opt seedQueue
  else seedQueue

let rec private fuzzLoop opt seedQueue n =
  let verbosity = opt.Verbosity
  let seedQueue = syncWithAFL opt seedQueue n
  if SeedQueue.isEmpty seedQueue then
    if n % 10 = 0 && verbosity >= 1 then log "Seed queue empty, waiting..."
    Thread.Sleep(1000)
    fuzzLoop opt seedQueue (n + 1)
  else
    let priority, seed, seedQueue = SeedQueue.dequeue seedQueue
    if verbosity >= 1 then log "Fuzzing with: %s" (Seed.toString seed)
    let newItems = GreyConcolic.run seed opt
    // Relocate the cursors of newly generated seeds.
    let relocatedItems = makeRelocatedItems opt newItems
    // Also generate seeds by just stepping the cursor of the original seed.
    let steppedItems = makeSteppedItems priority seed
    // Add the new items to the seed queue.
    let seedQueue = List.fold SeedQueue.enqueue seedQueue relocatedItems
    let seedQueue = List.fold SeedQueue.enqueue seedQueue steppedItems
    fuzzLoop opt seedQueue (n + 1)

let private fuzzingTimer timeoutSec = async {
  let timespan = System.TimeSpan(0, 0, 0, timeoutSec)
  System.Threading.Thread.Sleep(timespan )
  printLine "Fuzzing timeout expired."
  log "===== Statistics ====="
  TestCase.printStatistics ()
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
  TestCase.initialize opt.OutDir
  Executor.initialize opt
  let emptyQueue = SeedQueue.initialize ()
  let initialSeeds = initializeSeeds opt
  log "[*] Total %d initial seeds" (List.length initialSeeds)
  let initItems = List.choose (makeInitialItems opt) initialSeeds
  let initQueue = List.fold SeedQueue.enqueue emptyQueue initItems
  log "[*] Start fuzzing"
  Async.Start (fuzzingTimer opt.Timelimit)
  fuzzLoop opt initQueue 0
  0 // Unreachable
