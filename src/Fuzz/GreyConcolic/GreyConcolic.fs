module Eclipser.GreyConcolic

open Config
open Utils
open Options

// Mutable variables for statistics and state management.
let mutable private nodeCoverageIncrCount = 0
let mutable private eqSolveNodeCoverageInc = 0
let mutable private ineqSolveNodeCoverageInc = 0
let mutable private binSchNodeCoverageInc = 0
let mutable private spawnNodeCoverageInc = 0
let mutable private pathCoverageIncrCount = 0
let mutable private eqSolvePathCoverageInc = 0
let mutable private ineqSolvePathCoverageInc = 0
let mutable private binSchPathCoverageInc = 0
let mutable private spawnPathCoverageInc = 0
let mutable private executions = 0
let mutable private recentExecNums: Queue<int> = Queue.empty
let mutable private recentNewPathNums: Queue<int> = Queue.empty

let updateStatus opt execN newPathN =
  if opt.Verbosity >= 1 then log "Found %d paths (%d execs)" newPathN execN
  executions <- executions + execN
  let recentExecNums' = if Queue.getSize recentExecNums > RecentRoundN
                        then Queue.drop recentExecNums
                        else recentExecNums
  recentExecNums <- Queue.enqueue recentExecNums' execN
  let recentNewPathNums' = if Queue.getSize recentNewPathNums > RecentRoundN
                           then Queue.drop recentNewPathNums
                           else recentNewPathNums
  recentNewPathNums <- Queue.enqueue recentNewPathNums' newPathN

let evaluateEfficiency () =
  let execNum = List.sum (Queue.elements recentExecNums)
  let newPathNum = List.sum (Queue.elements recentNewPathNums)
  if execNum = 0 then 1.0 else float newPathNum / float execNum

let recordNodeCoverageIncrease origin =
  nodeCoverageIncrCount <- nodeCoverageIncrCount + 1
  match origin with
  | Equation -> eqSolveNodeCoverageInc <- eqSolveNodeCoverageInc + 1
  | Inequality -> ineqSolveNodeCoverageInc <- ineqSolveNodeCoverageInc + 1
  | Monoton -> binSchNodeCoverageInc <- binSchNodeCoverageInc + 1
  | Spawn -> spawnNodeCoverageInc <- spawnNodeCoverageInc + 1

let recordPathCoverageIncrease origin =
  pathCoverageIncrCount <- pathCoverageIncrCount + 1
  match origin with
  | Equation -> eqSolvePathCoverageInc <- eqSolvePathCoverageInc + 1
  | Inequality -> ineqSolvePathCoverageInc <- ineqSolvePathCoverageInc + 1
  | Monoton -> binSchPathCoverageInc <- binSchPathCoverageInc + 1
  | Spawn -> spawnPathCoverageInc <- spawnPathCoverageInc + 1

let printStatistics () =
  log "Executions of grey-box concolic phase : %d" executions
  log "Grey-box concolic : Node cov. increase : %d / Path cov. increase = %d"
    nodeCoverageIncrCount pathCoverageIncrCount
  log "  Found by solving equations : %d + %d"
    eqSolveNodeCoverageInc eqSolvePathCoverageInc
  log "  Found by solving inequalities : %d + %d"
    ineqSolveNodeCoverageInc ineqSolvePathCoverageInc
  log "  Found by binary search on monotonic functions : %d + %d"
    binSchNodeCoverageInc binSchPathCoverageInc
  log "  Found by initial seed spawning : %d + %d"
    spawnNodeCoverageInc spawnPathCoverageInc

let printFoundSeed seed newNodeN =
  let seedStr = Seed.toString seed
  let nodeStr = if newNodeN > 0 then sprintf "(%d new nodes) " newNodeN else ""
  log "[*] Found by grey-box concolic %s: %s" nodeStr seedStr

let evalSeedsAux opt accSeeds (seed, origin) =
  let newNodeN, pathHash, nodeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.addSeed opt seed newNodeN pathHash nodeHash exitSig
  if newNodeN > 0 then recordNodeCoverageIncrease origin
  if isNewPath then recordPathCoverageIncrease origin
  if newNodeN > 0 && opt.Verbosity >= 0 then printFoundSeed seed newNodeN
  if isNewPath && not (Signal.isTimeout exitSig) && not (Signal.isCrash exitSig)
  then let priority = if newNodeN > 0 then Favored else Normal
       (priority, seed) :: accSeeds
  else accSeeds

let evalSeeds opt items =
  // Duplicate seeds can be found during linear constraint solving
  List.distinctBy fst items
  |> List.fold (evalSeedsAux opt) []
  |> List.rev // To preserve original order

let checkByProducts opt spawnedSeeds =
  if opt.Verbosity >= 4 then log "Examining by products of sampling"
  let spawnedResults = List.map (fun s -> (s, Spawn)) spawnedSeeds
  evalSeeds opt spawnedResults

let run seed opt =
  let curByteVal = Seed.getCurByteVal seed
  let minByte, maxByte = ByteVal.getMinMax curByteVal seed.SourceCursor
  if minByte = maxByte then
    let seedStr = Seed.toString seed
    failwithf "Cursor pointing to Fixed ByteVal %s" seedStr
  let minVal, maxVal = bigint (int minByte), bigint (int maxByte)
  let sampleN = opt.NSpawn
  if sampleN < 3 then failwith "Invalid # of sample"
  let branchTraces, spawnedSeeds = BranchTrace.collect seed opt minVal maxVal sampleN
  let byteDir = Seed.getByteCursorDir seed
  let bytes = Seed.queryNeighborBytes seed byteDir
  let ctx = { Bytes = bytes; ByteDir = byteDir }
  let branchTrace = BranchTree.make opt ctx branchTraces
  let solutions = BranchCondition.solve seed opt byteDir branchTrace
  evalSeeds opt solutions @ checkByProducts opt spawnedSeeds
