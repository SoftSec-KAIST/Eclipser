module Eclipser.Fuzz

open System
open Config
open Utils
open Options

let mutable rounds = 0
let mutable greyConcolicDuration = 0.0
let mutable randFuzzDuration = 0.0

let createSeeds opt =
  let maxArgLens = List.filter (fun len -> len > 0) opt.MaxArgLen
  let inputSrc = InputKind.ofFuzzMode opt.FuzzMode
  let initSeedWithNArgs n =
    let (maxArgLens', _) = splitList n maxArgLens
    Seed.make inputSrc maxArgLens' opt.MaxFileLen opt.MaxStdInLen
  List.ofSeq { 1 .. List.length maxArgLens }
  |> List.map initSeedWithNArgs
  |> List.rev // Prioritize seed with more # of args, for better exploration.

let importSeeds opt =
  let inputSrc = InputKind.ofFuzzMode opt.FuzzMode
  let maxLen =
    match inputSrc with
    | Args when List.length opt.MaxArgLen = 1 -> List.head opt.MaxArgLen
    | Args -> failwith "Invalid max length option on argument input"
    | File -> opt.MaxFileLen
    | StdIn -> opt.MaxStdInLen
  log "Maximum length for imported seeds = %d" maxLen
  System.IO.Directory.EnumerateFiles opt.InitSeedsDir // Obtain file list
  |> List.ofSeq // Convert list to array
  |> List.map System.IO.File.ReadAllBytes // Read in file contents
  |> List.map (Seed.makeWith inputSrc maxLen) // Create seed with content

let initializeSeeds opt =
  let initArg = opt.InitArg
  let fixPath = opt.FixFilepath
  let seedDir = opt.InitSeedsDir
  // Initialize seeds, and set the initial argument/file path of each seed
  if seedDir = "" then createSeeds opt else importSeeds opt
  |> List.map (fun s -> if initArg <> "" then Seed.setArgs s initArg else s)
  |> List.map (fun s -> if fixPath <> "" then Seed.setFilepath s fixPath else s)

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

let printGeneratedItems concolicItems randItems =
  let concolicItems = List.filter (fun item -> fst item = Favored) concolicItems
  let randItems = List.filter (fun item -> fst item = Favored) randItems
  let concolicSeeds = snd (List.unzip concolicItems)
  let randSeeds = snd (List.unzip randItems)
  let itemToStr (pr, seed) = Seed.toString seed
  let concolicStr = String.concat "; " (List.map Seed.toString concolicSeeds)
  let randStr = String.concat "; " (List.map Seed.toString randSeeds)
  log "Generated seeds for grey-box concolic : [ %s ]" concolicStr
  log "Generated seeds for random fuzzing : [ %s ]" randStr

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
    if opt.Verbosity >= 1 then
      log "Resource allocation ratio for next round:"
      log "  Grey-box concolic = %.2f" concolicRatio
      log "  Random fuzzing = %.2f"  randFuzzRatio
    let totalBudget = ExecBudgetPerRound
    let greyConcBudget = int (float totalBudget * concolicRatio)
    let randFuzzBudget = int (float totalBudget * randFuzzRatio)
    (greyConcBudget, randFuzzBudget)

let rec greyConcolicLoop finishT opt concQ randQ =
  if Executor.isResourceExhausted () ||
     DateTime.Now > finishT ||
     ConcolicQueue.isEmpty concQ
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
    if opt.Verbosity >= 3 then
      printGeneratedItems (newItemsForConc @ steppedItems) newItemsForRand
    greyConcolicLoop finishT opt concQ randQ

let repeatGreyConcolic finishT opt concQ randQ concolicBudget =
  if opt.Verbosity >= 1 then log "Grey-box concoclic testing phase starts"
  Executor.allocateResource concolicBudget
  Executor.resetPhaseExecutions ()
  let pathNumBefore = Manager.getPathCount ()
  let startTime = DateTime.Now
  let concQ, randQ = greyConcolicLoop finishT opt concQ randQ
  let elapsedTime = DateTime.Now - startTime
  let pathNumAfter = Manager.getPathCount ()
  if opt.Verbosity >= 1 then log "elapsed time : %s" (elapsedTime.ToString ())
  greyConcolicDuration <- greyConcolicDuration + elapsedTime.TotalSeconds
  let concolicExecNum = Executor.getPhaseExecutions ()
  let concolicNewPathNum = pathNumAfter - pathNumBefore
  GreyConcolic.updateStatus opt concolicExecNum concolicNewPathNum
  (concQ, randQ)

let rec randFuzzLoop finishT opt concQ randQ =
  // Random fuzzing seeds are involatile, so don't have to check emptiness.
  if Executor.isResourceExhausted () || DateTime.Now > finishT
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
    if opt.Verbosity >= 3 then
      printGeneratedItems newItemsForConc newItemsForRand
    randFuzzLoop finishT opt concQ randQ

let repeatRandFuzz finishT opt concQ randQ randFuzzBudget =
  if opt.Verbosity >= 1 then log "Random fuzzing phase starts"
  Executor.allocateResource randFuzzBudget
  Executor.resetPhaseExecutions ()
  let pathNumBefore = Manager.getPathCount ()
  let startTime = DateTime.Now
  let concQ, randQ = randFuzzLoop finishT opt concQ randQ
  let elapsedTime = DateTime.Now - startTime
  let pathNumAfter = Manager.getPathCount ()
  if opt.Verbosity >= 1 then log "elapsed time : %s" (elapsedTime.ToString ())
  randFuzzDuration <- randFuzzDuration + elapsedTime.TotalSeconds
  let randExecNum = Executor.getPhaseExecutions ()
  let randNewPathNum = pathNumAfter - pathNumBefore
  RandomFuzz.updateStatus opt randExecNum randNewPathNum
  (concQ, randQ)

let checkTerminateCondition finishT opt concQ randQ =
  DateTime.Now > finishT ||
  (opt.GreyConcolicOnly && ConcolicQueue.isEmpty concQ)
  // Note that random fuzzing queue is always non-empty, since it's involatile.

let printQueueStatus (concQ: ConcolicQueue) (randQ: RandFuzzQueue) =
  let concFavor = Queue.getSize concQ.FavoredQueue
  let concNormal = FileQueue.getSize concQ.NormalQueue
  let randFavor = DurableQueue.getSize randQ.FavoredQueue
  let randNormal = FileQueue.getSize randQ.NormalQueue
  log "Queue : %d + %d & %d + %d" concFavor concNormal randFavor randNormal

let rec fuzzLoop finishT opt concQ randQ =
  if not (checkTerminateCondition finishT opt concQ randQ) then
    rounds <- rounds + 1
    let concolicBudget, randFuzzBudget = allocResource opt
    // Perform grey-box concolic testing
    let concQ, randQ = repeatGreyConcolic finishT opt concQ randQ concolicBudget
    // Perform random fuzzing
    let concQ, randQ = repeatRandFuzz finishT opt concQ randQ randFuzzBudget
    // Minimize random-fuzzing queue if # of seeds increased considerably
    let randQ = if RandFuzzQueue.timeToMinimize randQ
                then RandFuzzQueue.minimize randQ opt
                else randQ
    if opt.Verbosity >= 1 then printQueueStatus concQ randQ
    fuzzLoop finishT opt concQ randQ

let printStatistics () =
  log "===== Statistics ====="
  log "Total round of fuzzing: %d" rounds
  log "Executions : %d" (Executor.getTotalExecutions())
  log "Duration of grey-box concolic testing phase : %.1f" greyConcolicDuration
  log "Duration of random fuzzing phase phase : %.1f" randFuzzDuration
  log "Average input length : %d" (Executor.getAverageInputLen ())
  Manager.printStatistics ()
  GreyConcolic.printStatistics ()
  RandomFuzz.printStatistics ()

let run args =
  let opt = parseFuzzOption args
  validateFuzzOption opt
  checkFileExists opt.TargetProg
  printfn "Fuzz target: %s" opt.TargetProg
  printfn "Time: %.0f sec" opt.Timelimit
  Eclipser.System.initialize opt
  Executor.initialize_exec Executor.ExecMode.NonReplay
  if opt.FuzzMode = StdinFuzz || opt.FuzzMode = FileFuzz then
    Executor.initForkServer opt
  log "Fuzzing starts"
  let finishT = DateTime.Now.AddSeconds opt.Timelimit
  let initialSeeds = initializeSeeds opt
  let initItems = List.map (fun s -> (Favored, s)) initialSeeds
  let queueDir = sprintf "%s/.internal" sysInfo.["outputDir"]
  let greyConcQueue = ConcolicQueue.initialize queueDir
  let greyConcQueue = List.fold ConcolicQueue.enqueue greyConcQueue initItems
  let randFuzzQueue = RandFuzzQueue.initialize queueDir
  let randFuzzQueue = List.fold RandFuzzQueue.enqueue randFuzzQueue initItems
  fuzzLoop finishT opt greyConcQueue randFuzzQueue
  printStatistics()
  Executor.terminateForkServer ()
  Eclipser.System.cleanup ()
  printLine "Fuzzing finished"
