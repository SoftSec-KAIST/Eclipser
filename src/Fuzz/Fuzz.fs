module Eclipser.Fuzz

open Config
open Utils
open Options

let private printFoundSeed opt seed =
  if opt.Verbosity >= 1 then log "[*] Found a new seed: %s" (Seed.toString seed)

let private initializeSeeds opt =
  if opt.InputDir = "" then [Seed.make opt.FuzzSource]
  else System.IO.Directory.EnumerateFiles opt.InputDir // Obtain file list
       |> List.ofSeq // Convert list to array
       |> List.map System.IO.File.ReadAllBytes // Read in file contents
       |> List.map (Seed.makeWith opt.FuzzSource) // Create seed with content

let private checkInitSeedCoverage opt seed =
  let exitSig, covGain = Executor.getCoverage opt seed
  TestCase.save opt seed exitSig covGain
  match Priority.ofCoverageGain covGain with
  | None -> None
  | Some priority -> Some (priority, seed)

let private initializeQueue opt seeds =
  // If the execution timeout is not given, use a large enough value for now.
  let opt = if opt.ExecTimeout <> 0UL then opt
            else { opt with ExecTimeout = EXEC_TIMEOUT_MAX }
  let initItems = List.choose (checkInitSeedCoverage opt) seeds
  List.fold SeedQueue.enqueue SeedQueue.empty initItems

// Measure the execution time of an initial seed. Choose the longer time between
// getCoverage() and getBranchTrace() call.
let private getInitSeedExecTime opt seed =
  let stopWatch = new System.Diagnostics.Stopwatch()
  stopWatch.Start()
  Executor.getCoverage opt seed |> ignore
  let time1 = stopWatch.Elapsed.TotalMilliseconds
  stopWatch.Reset()
  stopWatch.Start()
  Executor.getBranchTrace opt seed 0I |> ignore // Use a dummy 'tryVal' arg.
  let time2 = stopWatch.Elapsed.TotalMilliseconds
  max time1 time2

// Decide execution timeout based on the execution time of initial seeds. Adopt
// the basic idea from AFL, and adjust coefficients and range for Eclipser.
let private decideExecTimeout (execTimes: float list) =
  let avgExecTime = List.average execTimes
  let maxExecTime = List.max execTimes
  log "[*] Initial seed execution time: avg = %.1f (ms), max = %.1f (ms)"
    avgExecTime maxExecTime
  let execTimeout = uint64 (max (4.0 * avgExecTime) (1.2 * maxExecTime))
  let execTimeout = min EXEC_TIMEOUT_MAX (max EXEC_TIMEOUT_MIN execTimeout)
  log "[*] Set execution timeout to %d (ms)" execTimeout
  execTimeout

// If the execution timeout is not given, set it to a large enough value and
// find each seed's execution time. Then, decide a new timeout based on them.
let private updateExecTimeout opt seeds =
  if opt.ExecTimeout <> 0UL then opt
  else let opt = { opt with ExecTimeout = EXEC_TIMEOUT_MAX }
       let execTimes = List.map (getInitSeedExecTime opt) seeds
       { opt with ExecTimeout = decideExecTimeout execTimes }

let private evalSeed opt seed exitSig covGain =
  TestCase.save opt seed exitSig covGain
  if covGain = NewEdge then printFoundSeed opt seed
  let isAbnormal = Signal.isTimeout exitSig || Signal.isCrash exitSig
  if isAbnormal then None else Priority.ofCoverageGain covGain

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

// Decides how to share the resource with AFL instances.
let private scheduleWithAFL opt =
  if opt.SyncDir <> "" && opt.SingleCore then Scheduler.checkAndReserveTime opt

// Sychronize the seed queue with AFL instances.
let private syncWithAFL opt seedQueue n =
  if opt.SyncDir <> "" && n % SYNC_N = 0 then Sync.run opt seedQueue
  else seedQueue

let rec private fuzzLoop opt seedQueue n =
  scheduleWithAFL opt
  let seedQueue = syncWithAFL opt seedQueue n
  if SeedQueue.isEmpty seedQueue then
    if n % 10 = 0 && opt.Verbosity >= 2 then log "Seed queue empty, waiting..."
    System.Threading.Thread.Sleep(1000)
    fuzzLoop opt seedQueue (n + 1)
  else
    let priority, seed, seedQueue = SeedQueue.dequeue seedQueue
    if opt.Verbosity >= 2 then log "Fuzzing with: %s" (Seed.toString seed)
    let newItems = GreyConcolic.run seed opt
    // Relocate the cursors of newly generated seeds.
    let relocatedItems = makeRelocatedItems opt newItems
    // Also generate seeds by just stepping the cursor of the original seed.
    let steppedItems = makeSteppedItems priority seed
    // Add the new items to the seed queue.
    let seedQueue = List.fold SeedQueue.enqueue seedQueue relocatedItems
    let seedQueue = List.fold SeedQueue.enqueue seedQueue steppedItems
    fuzzLoop opt seedQueue (n + 1)

let private terminator timelimitSec = async {
  let timespan = System.TimeSpan(0, 0, 0, timelimitSec)
  System.Threading.Thread.Sleep(timespan)
  log "[*] Fuzzing timeout expired."
  log "===== Statistics ====="
  TestCase.printStatistics ()
  log "Done, clean up and exit..."
  Executor.cleanup ()
  exit (0)
}

let private setTimer opt =
  if opt.Timelimit > 0 then
    log "[*] Time limit : %d sec" opt.Timelimit
    Async.Start (terminator opt.Timelimit)
  else
    log "[*] No time limit given, run infinitely"

[<EntryPoint>]
let main args =
  let opt = parseFuzzOption args
  validateFuzzOption opt
  assertFileExists opt.TargetProg
  log "[*] Fuzz target : %s" opt.TargetProg
  createDirectoryIfNotExists opt.OutDir
  TestCase.initialize opt.OutDir
  Executor.initialize opt
  let initialSeeds = initializeSeeds opt
  log "[*] Total %d initial seeds" (List.length initialSeeds)
  let initQueue = initializeQueue opt initialSeeds
  let opt = updateExecTimeout opt initialSeeds
  setTimer opt
  Scheduler.initialize () // Should be called after preprocessing initial seeds.
  log "[*] Start fuzzing"
  fuzzLoop opt initQueue 1 // Start from 1, to slightly defer the first sync.
  0 // Unreachable
