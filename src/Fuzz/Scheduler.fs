module Eclipser.Scheduler

open Config
open Utils
open System.Threading

let private timer = new System.Diagnostics.Stopwatch()

// Tentative efficiency of random fuzzing. TODO: Communicate with AFL for this.
let private RAND_FUZZ_EFFICIENCY = 0.0005

// Decides sleep factor 'f', which will be used to sleep for 'f * elapsed time'.
// This means we will utilize 1 / (2 * (f + 1)) of the system resource.
let private decideSleepFactor roundExecs roundTCs =
  let greyConcEfficiency = float (roundTCs) / float (roundExecs)
  // GREY_CONC_EFF : RAND_FUZZ_EFF = 1 : 2 * factor + 1
  let factor = if greyConcEfficiency = 0.0 then SLEEP_FACTOR_MAX
               else (RAND_FUZZ_EFFICIENCY / greyConcEfficiency - 1.0) / 2.0
  log "[*] Grey-concolic eff. = %.3f, factor = %.3f" greyConcEfficiency factor
  // Bound the factor between minimum and maximum value allowed.
  max SLEEP_FACTOR_MIN (min SLEEP_FACTOR_MAX factor)

let initialize () =
  Executor.resetRoundExecutions()
  TestCase.resetRoundTestCaseCount()
  timer.Start()

// Check the efficiency of the system and sleep for a while to adjust the weight
// of resource use with AFL.
let checkAndReserveTime () =
  let roundExecs = Executor.getRoundExecutions ()
  if roundExecs > ROUND_SIZE then
    let roundTCs = TestCase.getRoundTestCaseCount ()
    Executor.resetRoundExecutions()
    TestCase.resetRoundTestCaseCount()
    let sleepFactor = decideSleepFactor roundExecs roundTCs
    let roundElapsed = timer.ElapsedMilliseconds
    log "[*] Elapsed round time: %d sec." (roundElapsed / 1000L)
    timer.Reset()
    let sleepTime = int (float (roundElapsed) * sleepFactor)
    log "[*] Decided sleep time: %d sec."  (sleepTime / 1000)
    Thread.Sleep(sleepTime)
    timer.Start()
