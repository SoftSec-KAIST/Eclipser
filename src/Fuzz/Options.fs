module Eclipser.Options

open Argu
open Utils
open Config

type FuzzerCLI =
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<AltCommandLine("-v")>] [<Unique>] Verbose of int
  | [<AltCommandLine("-t")>] [<Mandatory>] [<Unique>] Timelimit of sec: int
  | [<AltCommandLine("-o")>] [<Mandatory>] [<Unique>] OutputDir of path: string
  // Options related to seed initialization
  | [<AltCommandLine("-i")>] [<Unique>] InputDir of path: string
  | [<Unique>] Arg of string
  | [<AltCommandLine("-f")>] [<Unique>] Filepath of string
  // Options related to execution of program
  | [<Unique>] ExecTimeout of millisec:uint64
  | [<Unique>] UsePty
  | [<Unique>] Architecture of string
  // Options related to fuzzing process
  | [<Unique>] NSolve of int
  | [<Unique>] NSpawn of int
  | [<Unique>] GreyConcolicOnly
  | [<Unique>] RandFuzzOnly
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Program _ -> "Target program for test case generation with fuzzing."
      | Verbose _ -> "Verbosity level to control debug messages (default:0)."
      | Timelimit _ -> "Timeout for fuzz testing (in seconds)."
      | OutputDir _ -> "Directory to store testcase outputs."
      // Options related to seed initialization
      | InputDir _ -> "Directory containing initial seeds."
      | Arg _ -> "Command-line argument of program under test."
      | Filepath _ -> "File input's (fixed) path"
      // Options related to execution of program
      | ExecTimeout _ -> "Execution timeout (ms) for a fuzz run (default:500)"
      | UsePty _ -> "Use pseudo tty for standard input"
      | Architecture _ -> "Target program architecture (X86|X64) (default:X64)"
      // Options related to test case generation
      | NSolve _ -> "Number of branches to flip in grey-box concolic testing " +
                    "when an execution path is given. 'N_solve' parameter in " +
                    "the paper."
      | NSpawn _ -> "Number of byte values to initially spawn in grey-box " +
                    "concolic testing. 'N_spawn' parameter in the paper."
      | GreyConcolicOnly -> "Perform grey-box concolic testing only."
      | RandFuzzOnly -> "Perform random fuzzing only."

type FuzzOption = {
  Verbosity         : int
  OutDir            : string
  Timelimit         : int
  // Options related to program execution
  TargetProg        : string
  ExecTimeout       : uint64
  UsePty            : bool
  Architecture      : Arch
  // Options related to seed
  FuzzSource        : InputSource
  InputDir          : string
  Arg               : string
  // Options related to test case generation
  NSolve            : int
  NSpawn            : int
  GreyConcolicOnly  : bool
  RandFuzzOnly      : bool
}

let parseFuzzOption (args: string array) =
  let cmdPrefix = "dotnet Eclipser.dll fuzz"
  let parser = ArgumentParser.Create<FuzzerCLI> (programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  { Verbosity = r.GetResult (<@ Verbose @>, defaultValue = 0)
    OutDir = r.GetResult (<@ OutputDir @>)
    Timelimit = r.GetResult (<@ Timelimit @>)
    // Options related to program execution
    TargetProg = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = DefaultExecTO)
    UsePty = r.Contains (<@ UsePty @>)
    Architecture = r.GetResult(<@ Architecture @>, defaultValue = "X64")
                   |> Arch.ofString
    // Options related to seed
    FuzzSource = if not (r.Contains(<@ Filepath @>)) then StdInput
                 else FileInput (r.GetResult (<@ Filepath @>))
    InputDir = r.GetResult(<@ InputDir @>, defaultValue = "")
    Arg = r.GetResult (<@ Arg @>, defaultValue = "")
    // Options related to test case generation
    NSolve = r.GetResult(<@ NSolve @>, defaultValue = 600)
    NSpawn = r.GetResult(<@ NSpawn @>, defaultValue = 10)
    GreyConcolicOnly = r.Contains(<@ GreyConcolicOnly @>)
    RandFuzzOnly = r.Contains(<@ RandFuzzOnly @>) }

let validateFuzzOption opt =
  if opt.GreyConcolicOnly && opt.RandFuzzOnly then
    printLine "Cannot specify '--greyconcoliconly' and 'randfuzzonly' together."
    exit 1
  if opt.NSpawn < 3 then
    failwith "Should provide N_spawn greater than or equal to 3"
