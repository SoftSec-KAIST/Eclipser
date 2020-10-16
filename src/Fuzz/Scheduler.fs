module Eclipser.Scheduler

open Config
open Utils
open Options

let private timer = new System.Diagnostics.Stopwatch()

// Tentative efficiency of random fuzzing. TODO: Communicate with AFL for this.
let private RAND_FUZZ_EFFICIENCY = 0.0005

// Decides sleep factor 'f', which will be used to sleep for 'f * elapsed time'.
// This means we will utilize 1 / (2 * (f + 1)) of the system resource.
let private decideSleepFactor opt roundExecs roundTCs =
  let greyConcEfficiency = float (roundTCs) / float (roundExecs)
  if opt.Verbosity >= 1 then log "[*] Efficiency = %.4f" greyConcEfficiency
  // GREY_CONC_EFF : RAND_FUZZ_EFF = 1 : 2 * factor + 1
  let factor = if greyConcEfficiency = 0.0 then SLEEP_FACTOR_MAX
               else (RAND_FUZZ_EFFICIENCY / greyConcEfficiency - 1.0) / 2.0
  // Bound the factor between minimum and maximum value allowed.
  max SLEEP_FACTOR_MIN (min SLEEP_FACTOR_MAX factor)

let initialize () =
  Executor.enableRoundStatistics()
  TestCase.enableRoundStatistics()
  timer.Start()

// Check the efficiency of the system and sleep for a while to adjust the weight
// of resource use with AFL.
let checkAndReserveTime opt =
  let roundExecs = Executor.getRoundExecs ()
  if roundExecs > ROUND_SIZE then
    let roundTCs = TestCase.getRoundTestCaseCount ()
    Executor.resetRoundExecs()
    TestCase.resetRoundTestCaseCount()
    let sleepFactor = decideSleepFactor opt roundExecs roundTCs
    let roundElapsed = timer.ElapsedMilliseconds
    let sleepTime = int (float (roundElapsed) * sleepFactor)
    if opt.Verbosity >= 1 then
      log "[*] Elapsed round time: %d sec." (roundElapsed / 1000L)
      log "[*] Decided sleep time: %d sec."  (sleepTime / 1000)
    System.Threading.Thread.Sleep(sleepTime)
    timer.Reset()
    timer.Start()
