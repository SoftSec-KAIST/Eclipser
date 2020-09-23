module Eclipser.Config

/// Default execution timeout of target program.
let DEF_EXEC_TO = 500UL

/// Maximum length of chunk to try in grey-box concolic testing.
let MAX_CHUNK_LEN = 10

/// The length of each input during the initialization of a seed. If the user
/// explicitly provided initial seed inputs, this parameter will not be used.
let INIT_INPUT_LEN = 16

let MAX_INPUT_LEN = 1048576

let BRANCH_COMB_WINDOW = 6
