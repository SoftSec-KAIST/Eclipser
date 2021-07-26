module Eclipser.GreyConcolic

// Evaluate seeds that gained coverage while collecting branch traces. If the
// newly covered code is also reached with grey-box concolic testing solutions,
// the coverage gain will disappear in this reinvestigation.
let private reconsiderCandidates opt seeds =
  let sigs, covGains = List.map (Executor.getCoverage opt) seeds |> List.unzip
  List.zip3 seeds sigs covGains

let run seed opt =
  let curByteVal = Seed.getCurByteVal seed
  let minByte, maxByte = ByteVal.getMinMax curByteVal
  if minByte = maxByte then
    let seedStr = Seed.toString seed
    failwithf "Cursor pointing to Fixed ByteVal %s" seedStr
  let minVal, maxVal = bigint (int minByte), bigint (int maxByte)
  let branchTraces, candidates = BranchTrace.collect seed opt minVal maxVal
  let byteDir = Seed.getByteCursorDir seed
  let bytes = Seed.queryNeighborBytes seed byteDir
  let ctx = { Bytes = bytes; ByteDir = byteDir }
  let branchTree = BranchTree.make opt ctx branchTraces
  let branchTree = BranchTree.selectAndRepair opt branchTree
  GreySolver.clearSolutionCache ()
  let solutions = GreySolver.solve seed opt byteDir branchTree
  let byProducts = reconsiderCandidates opt candidates
  solutions @ byProducts
