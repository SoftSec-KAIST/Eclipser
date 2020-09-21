module Eclipser.Config

/// Default execution timeout of target program.
let DefaultExecTO = 500UL

/// Maximum length of chunk to try in grey-box concolic testing.
let MaxChunkLen = 10

/// Call context sensitivity when measuring node coverage.
let CtxSensitivity = 0

/// The length of each input during the initialization of a seed. If the user
/// explicitly provided initial seed inputs, this parameter will not be used.
let INIT_INPUT_LEN = 16

let MAX_INPUT_LEN = 1048576

let DurableQueueMaxSize = 1000

let BranchCombinationWindow = 6
