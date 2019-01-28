module Eclipser.Manager

open System.Collections.Generic
open Utils
open Options
open Eclipser.System

let mutable private segfaults = 0
let mutable private illegalInstrs = 0
let mutable private fpErrors = 0
let mutable private aborts = 0
let mutable private argCrashes = 0
let mutable private stdinCrashes = 0
let mutable private fileCrashes = 0
let mutable private crashes = 0
let mutable private argTestcases = 0
let mutable private stdinTestcases = 0
let mutable private fileTestcases = 0
let mutable private testcases = 0
let private pathHashes = new HashSet<Hash>()
let private crashNodeHashes = new HashSet<Hash>()

let isNewPath pathHash =
  // The case of pathHash = 0UL means abortion due to threshold or other errors.
  pathHash <> 0UL && not (pathHashes.Contains pathHash)

let private addPathHash pathHash =
  // The case of pathHash = 0UL means abortion due to threshold or other errors.
  let isNewPath = pathHash <> 0UL && pathHashes.Add pathHash
  isNewPath

let private addCrashHash crashNodeHash =
  crashNodeHash <> 0UL && crashNodeHashes.Add crashNodeHash

let getPathCount () = pathHashes.Count

let printStatistics () =
  log "Paths : %d" pathHashes.Count
  log "Testcases : %d" testcases
  log "Input vector of test cases"
  log "  Argument : %d" argTestcases
  log "  Stdin : %d" stdinTestcases
  log "  File : %d" fileTestcases
  log "Crashes : %d" crashes
  log "  Segfault : %d" segfaults
  log "  Illegal instruction : %d" illegalInstrs
  log "  Floating point error : %d" fpErrors
  log "  Program abortion : %d" aborts
  log "Input vector of crashes"
  log "  Argument : %d" argCrashes
  log "  Stdin : %d" stdinCrashes
  log "  File : %d" fileCrashes

let updateCrashCount seed exitSig =
  match exitSig with
  | Signal.SIGSEGV -> segfaults <- segfaults + 1
  | Signal.SIGILL -> illegalInstrs <- illegalInstrs + 1
  | Signal.SIGFPE -> fpErrors <- fpErrors + 1
  | Signal.SIGABRT -> aborts <- aborts + 1
  | _ -> failwith "updateCrashCount() called with a non-crashing exit signal"
  match seed.SourceCursor with
  | Args -> argCrashes <- argCrashes + 1
  | StdIn -> stdinCrashes <- stdinCrashes + 1
  | File -> fileCrashes <- fileCrashes + 1
  crashes <- crashes + 1

let saveCrash opt seed exitSig =
  if opt.Verbosity >= 0 then log "Found crash seed : %s" (Seed.toString seed)
  let crashTC = TestCase.fromSeed seed
  let path = sprintf "%s/crash/crash-%d" sysInfo.["outputDir"] crashes
  System.IO.File.WriteAllText(path, (TestCase.toJSON crashTC))
  updateCrashCount seed exitSig

let updateTestcaseCount seed =
  match seed.SourceCursor with
  | Args -> argTestcases <- argTestcases + 1
  | StdIn -> stdinTestcases <- stdinTestcases + 1
  | File -> fileTestcases <- fileTestcases + 1
  testcases <- testcases + 1

let saveTestCase seed =
  let tc = TestCase.fromSeed seed
  let filepath = sprintf "%s/testcase/tc-%d" sysInfo.["outputDir"] testcases
  System.IO.File.WriteAllText(filepath, (TestCase.toJSON tc))
  updateTestcaseCount seed

let checkCrash opt exitSig tc nodeHash =
  if Signal.isCrash exitSig && addCrashHash nodeHash
  then (true, exitSig)
  elif Signal.isTimeout exitSig then // Check again with native execution
    let exitSig' = Executor.nativeExecute opt tc
    if Signal.isCrash exitSig' && addCrashHash nodeHash
    then (true, exitSig')
    else (false, exitSig')
  else (false, exitSig)

let addSeed opt seed newN pathHash nodeHash exitSig =
  let tc = TestCase.fromSeed seed
  let isNewPath = addPathHash pathHash
  let isCrash, exitSig' = checkCrash opt exitSig tc nodeHash
  if newN > 0 then saveTestCase seed
  if isCrash then saveCrash opt seed exitSig'
  isNewPath
