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

/// Minimum and maximum value for the execution timeout of target program. Note
/// that AFL uses 1000UL for EXEC_TIMEOUT_MAX, but we use a higher value since
/// Eclipser is a binary-based fuzzer. Note that this range is ignored when an
/// explicit execution timeout is given with '-e' option.
let EXEC_TIMEOUT_MIN = 400UL
let EXEC_TIMEOUT_MAX = 4000UL

/// Maximum chunk length to try for solving linear branch conditions.
let MAX_LIN_SOLVE_LEN = 8
/// Maximum chunk length to try for solving monotonic branch conditions.
let MAX_MONO_SOLVE_LEN = 12
/// Maximum chunk length to try for solving non-monotonic string compare
/// branch conditions.
let MAX_STR_SOLVE_LEN = 48

/// The length of each input during the initialization of a seed. If the user
/// explicitly provided initial seed inputs, this parameter will not be used.
let INIT_INPUT_LEN = 16

let MAX_INPUT_LEN = 1048576

let BRANCH_COMB_WINDOW = 6
