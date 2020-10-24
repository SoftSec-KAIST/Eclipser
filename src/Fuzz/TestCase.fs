module Eclipser.TestCase

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

let mutable private totalSegfaults = 0
let mutable private totalIllegals = 0
let mutable private totalFPEs = 0
let mutable private totalAborts = 0
let mutable private totalCrashes = 0
let mutable private totalTestCases = 0
let mutable private roundStatisticsOn = false
let mutable private roundTestCases = 0

let printStatistics () =
  log "Testcases : %d" totalTestCases
  log "Crashes : %d" totalCrashes
  log "  Segfault : %d" totalSegfaults
  log "  Illegal instruction : %d" totalIllegals
  log "  Floating point error : %d" totalFPEs
  log "  Program abortion : %d" totalAborts

let private incrCrashCount exitSig =
  match exitSig with
  | Signal.SIGSEGV -> totalSegfaults <- totalSegfaults + 1
  | Signal.SIGILL -> totalIllegals <- totalIllegals + 1
  | Signal.SIGFPE -> totalFPEs <- totalFPEs + 1
  | Signal.SIGABRT -> totalAborts <- totalAborts + 1
  | _ -> failwith "updateCrashCount() called with a non-crashing exit signal"
  totalCrashes <- totalCrashes + 1

let enableRoundStatistics () = roundStatisticsOn <- true

let disableRoundStatistics () = roundStatisticsOn <- false

let getRoundTestCaseCount () = roundTestCases

let incrTestCaseCount () =
  totalTestCases <- totalTestCases + 1
  if roundStatisticsOn then roundTestCases <- roundTestCases + 1

let resetRoundTestCaseCount () = roundTestCases <- 0

(*** Test case storing functions ***)

let private dumpCrash opt seed exitSig =
  if opt.Verbosity >= 1 then log "[*] Save crash seed : %s" (Seed.toString seed)
  let crashName = sprintf "id:%06d" totalCrashes
  let crashPath = System.IO.Path.Combine(crashDir, crashName)
  System.IO.File.WriteAllBytes(crashPath, Seed.concretize seed)
  incrCrashCount exitSig

let private dumpTestCase seed =
  let tcName = sprintf "id:%06d" totalTestCases
  let tcPath = System.IO.Path.Combine(testcaseDir, tcName)
  System.IO.File.WriteAllBytes(tcPath, Seed.concretize seed)
  incrTestCaseCount ()

let private checkCrash opt seed exitSig covGain =
  if Signal.isCrash exitSig && covGain = NewEdge then (true, exitSig)
  elif Signal.isTimeout exitSig then // Check again with native execution
    let exitSig' = Executor.nativeExecute opt seed
    if Signal.isCrash exitSig' && covGain = NewEdge then (true, exitSig')
    else (false, exitSig')
  else (false, exitSig)

let save opt seed exitSig covGain =
  let isNewCrash, exitSig' = checkCrash opt seed exitSig covGain
  if isNewCrash then dumpCrash opt seed exitSig'
  if covGain = NewEdge then dumpTestCase seed
