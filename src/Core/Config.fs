module Eclipser.Config

/// Default execution timeout of target program.
let DefaultExecTO = 500UL

/// Trigger seed queue culling when the queue size increased by this ratio.
let SeedCullingThreshold = 1.2

/// Number of program execution allowed per each round of grey-box concolic
/// testing and random fuzz testing.
let ExecBudgetPerRound = 5000

/// Probability to use the queue of 'favored' seeds when selecting the next
/// seed to use for test case generation.
let FavoredSeedProb = 0.8

let MutateRatio = 0.2
let RecentRoundN = 10
let MinResrcRatio = 0.1
let MaxResrcRatio = 1.0 - MinResrcRatio

/// Maximum length of chunk to try in grey-box concolic testing.
let MaxChunkLen = 10

/// In random fuzzing, try this number of random mutation for a chosen seed.
let RandSchTryCount = 100

/// Call context sensitivity when measuring node coverage.
let CtxSensitivity = 0

/// The length of each input during the initialization of a seed. If the user
/// explicitly provided initial seed inputs, this parameter will not be used.
let INIT_INPUT_LEN = 16

let MAX_INPUT_LEN = 1048576

let DurableQueueMaxSize = 1000

let FileQueueMaxSize = 10000

let FileQueueMaxBytes = 2048UL * 1024UL * 1024UL // 2048 megabytes.

let BranchCombinationWindow = 6

let SkipFixedProb = 50
