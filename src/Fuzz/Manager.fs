module Eclipser.Manager

open System.Collections.Generic
open Utils
open Options

(*** Directory paths ***)

let mutable testcaseDir = ""
let mutable crashDir = ""

let initialize outDir =
  testcaseDir <- System.IO.Path.Combine(outDir, "queue")
  System.IO.Directory.CreateDirectory(testcaseDir) |> ignore
  crashDir <- System.IO.Path.Combine(outDir, "crashes")
  System.IO.Directory.CreateDirectory(crashDir) |> ignore

(*** Statistics ***)

let mutable private segfaultCount = 0
let mutable private illegalInstrCount = 0
let mutable private fpErrorCount = 0
let mutable private abortCount = 0
let mutable private crashCount = 0
let mutable private argTestCaseCount = 0
let mutable private stdinTestCaseCount = 0
let mutable private fileTestCaseCount = 0
let mutable private testCaseCount = 0
let private pathHashes = new HashSet<Hash>()
let private crashEdgeHashes = new HashSet<Hash>()

let isNewPath pathHash =
  // The case of pathHash = 0UL means abortion due to threshold or other errors.
  pathHash <> 0UL && not (pathHashes.Contains pathHash)

let private addPathHash pathHash =
  // The case of pathHash = 0UL means abortion due to threshold or other errors.
  pathHash <> 0UL && pathHashes.Add pathHash

let private addCrashHash crashEdgeHash =
  crashEdgeHash <> 0UL && crashEdgeHashes.Add crashEdgeHash

let getPathCount () =
  pathHashes.Count

let printStatistics () =
  log "Paths : %d" pathHashes.Count
  log "Testcases : %d" testCaseCount
  log "Input vector of test cases"
  log "  Argument : %d" argTestCaseCount
  log "  Stdin : %d" stdinTestCaseCount
  log "  File : %d" fileTestCaseCount
  log "Crashes : %d" crashCount
  log "  Segfault : %d" segfaultCount
  log "  Illegal instruction : %d" illegalInstrCount
  log "  Floating point error : %d" fpErrorCount
  log "  Program abortion : %d" abortCount

let private updateCrashCount exitSig =
  match exitSig with
  | Signal.SIGSEGV -> segfaultCount <- segfaultCount + 1
  | Signal.SIGILL -> illegalInstrCount <- illegalInstrCount + 1
  | Signal.SIGFPE -> fpErrorCount <- fpErrorCount + 1
  | Signal.SIGABRT -> abortCount <- abortCount + 1
  | _ -> failwith "updateCrashCount() called with a non-crashing exit signal"
  crashCount <- crashCount + 1

let private updateTestcaseCount () =
  testCaseCount <- testCaseCount + 1

(*** Test case storing functions ***)

let private dumpCrash opt seed exitSig =
  if opt.Verbosity >= 0 then log "Save crash seed : %s" (Seed.toString seed)
  let crashName = sprintf "crash-%05d" crashCount
  let crashPath = System.IO.Path.Combine(crashDir, crashName)
  System.IO.File.WriteAllBytes(crashPath, Seed.concretize seed)
  updateCrashCount exitSig

let private dumpTestCase seed =
  let tcName = sprintf "tc-%05d" testCaseCount
  let tcPath = System.IO.Path.Combine(testcaseDir, tcName)
  System.IO.File.WriteAllBytes(tcPath, Seed.concretize seed)
  updateTestcaseCount ()

let private checkCrash opt exitSig seed edgeHash =
  if Signal.isCrash exitSig && addCrashHash edgeHash
  then (true, exitSig)
  elif Signal.isTimeout exitSig then // Check again with native execution
    let exitSig' = Executor.nativeExecute opt seed
    if Signal.isCrash exitSig' && addCrashHash edgeHash
    then (true, exitSig')
    else (false, exitSig')
  else (false, exitSig)

let save opt seed newN pathHash edgeHash exitSig isInitSeed =
  let isNewPath = addPathHash pathHash
  let isNewCrash, exitSig' = checkCrash opt exitSig seed edgeHash
  if newN > 0 || isInitSeed then dumpTestCase seed
  if isNewCrash then dumpCrash opt seed exitSig'
  isNewPath
