module Eclipser.Initialize

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

let initQueue opt queueDir =
  let initialSeeds = initializeSeeds opt
  let initItems = preprocess opt initialSeeds
  let greyConcQueue = ConcolicQueue.initialize queueDir
  let greyConcQueue = List.fold ConcolicQueue.enqueue greyConcQueue initItems
  let randFuzzQueue = RandFuzzQueue.initialize queueDir
  let randFuzzQueue = List.fold RandFuzzQueue.enqueue randFuzzQueue initItems
  (greyConcQueue, randFuzzQueue)
