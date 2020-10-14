module Eclipser.Config

/// Size of bitmap to measure edge coverage. Should be updated along with the
/// macros at Instrumentor/patches-*/eclipser.c
let BITMAP_SIZE = 0x10000L

/// Synchronize the seed queue with AFL every SYNC_N iteration of fuzzing loop.
let SYNC_N = 10

/// We will consider every ROUND_SIZE executions as a single round. A 'round' is
/// the unit of time for resource scheduling (cf. Scheduler.fs)
let ROUND_SIZE = 10000
let SLEEP_FACTOR_MIN = 0.0
let SLEEP_FACTOR_MAX = 4.0

/// Default execution timeout of target program.
let DEF_EXEC_TO = 500UL

/// Maximum length of chunk to try in grey-box concolic testing.
let MAX_CHUNK_LEN = 10

/// The length of each input during the initialization of a seed. If the user
/// explicitly provided initial seed inputs, this parameter will not be used.
let INIT_INPUT_LEN = 16

let MAX_INPUT_LEN = 1048576

let BRANCH_COMB_WINDOW = 6
