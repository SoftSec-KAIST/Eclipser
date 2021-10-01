module Eclipser.Initialize

open Utils
open Options

let createSeeds opt =
  let maxArgLens = List.filter (fun len -> len > 0) opt.MaxArgLen
  let initSeedWithNArgs n =
    let (maxArgLens', _) = splitList n maxArgLens
    Seed.make opt.FuzzMode maxArgLens' opt.MaxFileLen opt.MaxStdInLen
  List.ofSeq { 1 .. List.length maxArgLens }
  |> List.map initSeedWithNArgs
  |> List.rev // Prioritize seed with more # of args, for better exploration.

let importSeeds opt =
  let fuzzMode = opt.FuzzMode
  let seedSrc = opt.InitSeedSrc
  let maxArgLen = opt.MaxArgLen
  let maxFileLen = opt.MaxFileLen
  let maxStdinLen = opt.MaxStdInLen
  System.IO.Directory.EnumerateFiles opt.InitSeedsDir
  |> List.ofSeq
  |> List.map System.IO.File.ReadAllBytes
  |> List.map (Seed.makeWith fuzzMode seedSrc maxArgLen maxFileLen maxStdinLen)

let initializeSeeds opt =
  let maxArgLen = opt.MaxArgLen
  let cmd = opt.InitArg
  let fpath = opt.Filepath
  // Initialize seeds, and set the initial argument/file path of each seed
  if opt.InitSeedsDir = "" then createSeeds opt else importSeeds opt
  |> List.map (fun s -> if cmd <> "" then Seed.setArgs s maxArgLen cmd else s)
  |> List.map (fun s -> if fpath <> "" then Seed.setFilepath s fpath else s)

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

let preprocessAux opt seed =
  let newEdgeN, pathHash, edgeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.save opt seed newEdgeN pathHash edgeHash exitSig true
  let inputSrcs = findInputSrc opt seed
  let newSeeds = seed :: Seed.moveSourceCursor seed inputSrcs
  if newEdgeN > 0 then List.map (fun s -> (Favored, s)) newSeeds
  elif isNewPath then List.map (fun s -> (Normal, s)) newSeeds
  else []

let preprocess opt seeds =
  log "[*] Total %d initial seeds" (List.length seeds)
  let items = List.collect (preprocessAux opt) seeds
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
