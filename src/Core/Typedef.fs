namespace Eclipser

type Hash = uint64

type Signedness = Signed | Unsigned

type Sign = Positive  | Negative | Zero

/// Specifies which input source to fuzz.
type InputSource =
  /// Fuzz standard input source.
  | StdInput
  /// Fuzz file input source.
  | FileInput of filepath: string

/// Describes how to measure coverage in QEMU tracer for branch feedback.
type CoverageMeasure =
  | Ignore // Just collect branch information and ignore coverage.
  | NonCumulative // Measure coverage as well, but without any side effect.
  | Cumulative // Measure coverage as well, in cumulative manner.

module CoverageMeasure =
  let toEnum = function
    | Ignore -> 1
    | NonCumulative -> 2
    | Cumulative -> 3

/// Describes the gain of coverage.
type CoverageGain =
  | NoGain
  | NewPath
  | NewEdge

/// Priority of found seed. A seed that increased edge coverage is assigned
/// 'Favored' priority, while a seed that increased path coverage is assigned
/// 'Normal' priority.
type Priority = Favored | Normal

module Priority =

  let ofCoverageGain = function
  | NoGain -> None
  | NewPath -> Some Normal
  | NewEdge -> Some Favored


/// Architecture of target program to fuzz.
type Arch = X86 | X64

module Arch =
  exception UnsupportedArchException

  /// Convert a string into an Arch.
  let ofString (str: string) =
    match str.ToLower() with
    | "x86" -> X86
    | "x64" -> X64
    | _ -> raise UnsupportedArchException

/// Signal returned by program execution.
type Signal =
  | ERROR = -1
  | NORMAL = 0
  | SIGILL = 4
  | SIGABRT = 6
  | SIGFPE = 8
  | SIGSEGV = 11
  | SIGALRM = 14

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
