module Eclipser.Fuzz

open System
open Config
open Utils
open Options

let findInputSrc opt seed =
  if opt.FuzzMode = AutoFuzz
  then Executor.getSyscallTrace opt seed
  else Set.empty

let rec moveCursorsAux opt isFromConcolic accConcolic accRandom items =
  match items with
  | [] -> (accConcolic, accRandom)
  | (priority, seed) :: tailItems ->
    let inputSrcs = findInputSrc opt seed
    let concSeeds = Seed.moveCursors seed isFromConcolic inputSrcs
    let concItems = List.map (fun s -> (priority, s)) concSeeds
    let randSeeds = seed :: Seed.moveSourceCursor seed inputSrcs
    let randItems = List.map (fun s -> (priority, s)) randSeeds
    let accConcolic = concItems @ accConcolic
    let accRandom = randItems @ accRandom
    moveCursorsAux opt isFromConcolic accConcolic accRandom tailItems

let moveCursors opt isFromConcolic seeds =
  let concItems, randItems = moveCursorsAux opt isFromConcolic [] [] seeds
  (List.rev concItems, List.rev randItems)

/// Allocate testing resource for each strategy (grey-box concolic testing and
/// random fuzz testing). Resource is managed through 'the number of allowed
/// program execution'. If the number of instrumented program execution exceeds
/// the specified number, the strategy will be switched.
let allocResource opt =
  if opt.GreyConcolicOnly then (ExecBudgetPerRound, 0)
  elif opt.RandFuzzOnly then (0, ExecBudgetPerRound)
  else
    let concolicEff = GreyConcolic.evaluateEfficiency ()
    let randFuzzEff = RandomFuzz.evaluateEfficiency ()
    let concolicRatio = concolicEff / (concolicEff + randFuzzEff)
    // Bound alloc ratio with 'MinResourceAlloc', to avoid extreme biasing
    let concolicRatio = max MinResrcRatio (min MaxResrcRatio concolicRatio)
    let randFuzzRatio = 1.0 - concolicRatio
    let totalBudget = ExecBudgetPerRound
    let greyConcBudget = int (float totalBudget * concolicRatio)
    let randFuzzBudget = int (float totalBudget * randFuzzRatio)
    (greyConcBudget, randFuzzBudget)

let rec greyConcolicLoop opt concQ randQ =
  if Executor.isResourceExhausted () || ConcolicQueue.isEmpty concQ
  then (concQ, randQ)
  else
    let pr, seed, concQ = ConcolicQueue.dequeue concQ
    if opt.Verbosity >= 1 then
      log "Grey-box concolic on %A seed : %s" pr (Seed.toString seed)
    let newSeeds = GreyConcolic.run seed opt
    // Move cursors of newly generated seeds.
    let newItemsForConc, newItemsForRand = moveCursors opt true newSeeds
    // Also generate seeds by just stepping the cursor of original seed.
    let steppedItems = List.map (fun s -> (pr, s)) (Seed.proceedCursors seed)
    let concQ = List.fold ConcolicQueue.enqueue concQ newItemsForConc
    let concQ = List.fold ConcolicQueue.enqueue concQ steppedItems
    // Note that 'Stepped' seeds are not enqueued for random fuzzing.
    let randQ = List.fold RandFuzzQueue.enqueue randQ newItemsForRand
    greyConcolicLoop opt concQ randQ

let repeatGreyConcolic opt concQ randQ concolicBudget =
  if opt.Verbosity >= 1 then log "Grey-box concoclic testing phase starts"
  Executor.allocateResource concolicBudget
  Executor.resetPhaseExecutions ()
  let pathNumBefore = Manager.getPathCount ()
  let concQ, randQ = greyConcolicLoop opt concQ randQ
  let pathNumAfter = Manager.getPathCount ()
  let concolicExecNum = Executor.getPhaseExecutions ()
  let concolicNewPathNum = pathNumAfter - pathNumBefore
  GreyConcolic.updateStatus opt concolicExecNum concolicNewPathNum
  (concQ, randQ)

let rec randFuzzLoop opt concQ randQ =
  // Random fuzzing seeds are involatile, so don't have to check emptiness.
  if Executor.isResourceExhausted ()
  then (concQ, randQ)
  else
    let pr, seed, randQ = RandFuzzQueue.dequeue randQ
    if opt.Verbosity >= 1 then
      log "Random fuzzing on %A seed : %s" pr (Seed.toString seed)
    let newSeeds = RandomFuzz.run seed opt
    // Move cursors of newly generated seeds.
    let newItemsForConc, newItemsForRand = moveCursors opt false newSeeds
    let concQ = List.fold ConcolicQueue.enqueue concQ newItemsForConc
    let randQ = List.fold RandFuzzQueue.enqueue randQ newItemsForRand
    randFuzzLoop opt concQ randQ

let repeatRandFuzz opt concQ randQ randFuzzBudget =
  if opt.Verbosity >= 1 then log "Random fuzzing phase starts"
  Executor.allocateResource randFuzzBudget
  Executor.resetPhaseExecutions ()
  let pathNumBefore = Manager.getPathCount ()
  let concQ, randQ = randFuzzLoop opt concQ randQ
  let pathNumAfter = Manager.getPathCount ()
  let randExecNum = Executor.getPhaseExecutions ()
  let randNewPathNum = pathNumAfter - pathNumBefore
  RandomFuzz.updateStatus opt randExecNum randNewPathNum
  (concQ, randQ)

let rec fuzzLoop opt concQ randQ =
  // Note that random fuzzing queue is always non-empty, since it's involatile.
  if not (opt.GreyConcolicOnly && ConcolicQueue.isEmpty concQ) then
    let concolicBudget, randFuzzBudget = allocResource opt
    // Perform grey-box concolic testing
    let concQ, randQ = repeatGreyConcolic opt concQ randQ concolicBudget
    // Perform random fuzzing
    let concQ, randQ = repeatRandFuzz opt concQ randQ randFuzzBudget
    // Minimize random-fuzzing queue if # of seeds increased considerably
    let randQ = if RandFuzzQueue.timeToMinimize randQ
                then RandFuzzQueue.minimize randQ opt
                else randQ
    fuzzLoop opt concQ randQ

let private terminator timelimitSec queueDir = async {
  let timespan = System.TimeSpan(0, 0, 0, timelimitSec)
  System.Threading.Thread.Sleep(timespan)
  log "===== Statistics ====="
  Manager.printStatistics ()
  log "Done, clean up and exit..."
  Executor.cleanUpForkServer ()
  Executor.cleanUpSharedMem ()
  Executor.cleanUpFiles ()
  removeDir queueDir
  exit (0)
}

let private setTimer opt queueDir =
  if opt.Timelimit > 0 then
    log "[*] Time limit : %d sec" opt.Timelimit
    Async.Start (terminator opt.Timelimit queueDir)
  else
    log "[*] No time limit given, run infinitely"

let run args =
  let opt = parseFuzzOption args
  validateFuzzOption opt
  assertFileExists opt.TargetProg
  log "[*] Fuzz target : %s" opt.TargetProg
  log "[*] Time limit : %d sec" opt.Timelimit
  createDirectoryIfNotExists opt.OutDir
  Manager.initialize opt.OutDir
  Executor.initialize opt.OutDir opt.Verbosity
  Executor.initialize_exec Executor.TimeoutHandling.SendSigterm
  Executor.prepareSharedMem ()
  if opt.FuzzMode = StdinFuzz || opt.FuzzMode = FileFuzz then
    Executor.initForkServer opt
  let queueDir = sprintf "%s/.internal" opt.OutDir
  let greyConcQueue, randFuzzQueue = Initialize.initQueue opt queueDir
  log "[*] Fuzzing starts"
  setTimer opt queueDir
  fuzzLoop opt greyConcQueue randFuzzQueue
