namespace Eclipser

type Signal =
  | ERROR = -1
  | NORMAL = 0
  | SIGILL = 4
  | SIGABRT = 6
  | SIGFPE = 8
  | SIGSEGV = 11
  | SIGALRM = 14



[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Signal =
  let isCrash signal =
    match signal with
    | Signal.SIGSEGV | Signal.SIGILL | Signal.SIGABRT-> true
    | _ -> false

  let isSegfault signal =
    signal = Signal.SIGSEGV

  let isIllegal signal =
    signal = Signal.SIGILL

  let isFPE signal =
    signal = Signal.SIGFPE

  let isAbort signal =
    signal = Signal.SIGABRT

  let isTimeout signal =
    signal = Signal.SIGALRM
