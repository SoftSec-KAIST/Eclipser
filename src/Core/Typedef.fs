namespace Eclipser

open Utils

type Hash = uint64

type Signedness = Signed | Unsigned

type Sign = Positive  | Negative | Zero

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

/// Fuzzing modes that specify input source to fuzz.
type FuzzMode =
  /// Fuzz command line argument input source.
  | ArgFuzz
  /// Fuzz file input source.
  | FileFuzz
  /// Fuzz standard input source.
  | StdinFuzz
  /// Start by fuzzing command line argument, and then automatically identify
  /// and fuzz standard/file input source.
  | AutoFuzz

module FuzzMode =

  /// Convert a string into FuzzMode.
  let ofString (str: string) =
    match str.ToLower() with
    | "arg" -> ArgFuzz
    | "stdin" -> StdinFuzz
    | "file" -> FileFuzz
    | "auto" -> AutoFuzz
    | _ -> printLine "Supported fuzz mode: 'arg', 'stdin', 'file', 'auto'."
           exit 1

/// Direction that the cursor of a 'Seed' should move toward.
type Direction = Stay | Left | Right

/// Type of input source. Currently we support three input sources (argument,
/// file, standard input).
type InputKind = Args | File | StdIn

module InputKind =

  let decideInitSrc = function
    | ArgFuzz -> Args
    | StdinFuzz -> StdIn
    | FileFuzz -> File
    | AutoFuzz -> Args

  let ofString (str: string) =
    match str.ToLower() with
    | "arg" -> Args
    | "stdin" -> StdIn
    | "file" -> File
    | _ -> printLine "Supported input kinds : 'arg', 'stdin', 'file'"
           exit 1

  /// Decides whether a sequence of multiple inputs is allowed.
  let isSingularInput = function
    | Args -> false
    | StdIn -> true // Currently, consider only one-time standard input.
    | File -> true // Currently, consider only one file input.

/// Priority of found seed. A seed that increased edge coverage is assigned
/// 'Favored' priority, while a seed that increased path coverage is assigned
/// 'Normal' priority.
type Priority = Favored | Normal