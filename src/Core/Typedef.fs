namespace Eclipser

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

/// Specifies which input source to fuzz.
type InputSource =
  /// Fuzz standard input source.
  | StdInput
  /// Fuzz file input source.
  | FileInput of filepath: string

/// Direction that the cursor of a 'Seed' should move toward.
type Direction = Stay | Left | Right

/// Priority of found seed. A seed that increased edge coverage is assigned
/// 'Favored' priority, while a seed that increased path coverage is assigned
/// 'Normal' priority.
type Priority = Favored | Normal