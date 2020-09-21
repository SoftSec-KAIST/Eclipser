module Eclipser.GreyConcolic

open Utils
open Options

let printFoundSeed verbosity seed newEdgeN =
  let edgeStr = if newEdgeN > 0 then sprintf "(%d new edges) " newEdgeN else ""
  if verbosity >= 1 then
    log "[*] Found by grey-box concolic %s: %s" edgeStr (Seed.toString seed)
  elif verbosity >= 0 then
    log "[*] Found by grey-box concolic %s" edgeStr

let evalSeedsAux opt accSeeds seed =
  let newEdgeN, pathHash, edgeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.save opt seed newEdgeN pathHash edgeHash exitSig false
  if newEdgeN > 0 then printFoundSeed opt.Verbosity seed newEdgeN
  if isNewPath && not (Signal.isTimeout exitSig) && not (Signal.isCrash exitSig)
  then let priority = if newEdgeN > 0 then Favored else Normal
       (priority, seed) :: accSeeds
  else accSeeds

let evalSeeds opt items =
  List.fold (evalSeedsAux opt) [] items |> List.rev // To preserve order

let checkByProducts opt spawnedSeeds =
  evalSeeds opt spawnedSeeds

let run seed opt =
  let curByteVal = Seed.getCurByteVal seed
  let minByte, maxByte = ByteVal.getMinMax curByteVal seed.Source
  if minByte = maxByte then
    let seedStr = Seed.toString seed
    failwithf "Cursor pointing to Fixed ByteVal %s" seedStr
  let minVal, maxVal = bigint (int minByte), bigint (int maxByte)
  let branchTraces, spawnSeeds = BranchTrace.collect seed opt minVal maxVal
  let byteDir = Seed.getByteCursorDir seed
  let bytes = Seed.queryNeighborBytes seed byteDir
  let ctx = { Bytes = bytes; ByteDir = byteDir }
  let branchTree = BranchTree.make opt ctx branchTraces
  let branchTree = BranchTree.selectAndRepair opt branchTree
  GreySolver.clearSolutionCache ()
  let solutions = GreySolver.solve seed opt byteDir branchTree
  evalSeeds opt solutions @ checkByProducts opt spawnSeeds
