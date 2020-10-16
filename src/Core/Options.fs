module Eclipser.Options

open Argu
open Utils
open Config

type FuzzerCLI =
  | [<AltCommandLine("-v")>] [<Unique>] Verbose of int
  | [<AltCommandLine("-t")>] [<Unique>] Timelimit of sec: int
  | [<AltCommandLine("-o")>] [<Mandatory>] [<Unique>] OutputDir of path: string
  | [<AltCommandLine("-s")>] [<Unique>] SyncDir of path: string
  // Options related to program execution.
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<Unique>] ExecTimeout of millisec:uint64
  | [<Unique>] Architecture of string
  | [<Unique>] NoForkServer
  // Options related to seed.
  | [<AltCommandLine("-i")>] [<Unique>] InputDir of path: string
  | [<Unique>] Arg of string
  | [<AltCommandLine("-f")>] [<Unique>] Filepath of string
  // Options related to grey-box concolic testing technique.
  | [<Unique>] NSolve of int
  | [<Unique>] NSpawn of int
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Verbose _ -> "Verbosity level to control debug messages (default:0)."
      | Timelimit _ -> "Timeout for fuzz testing (in seconds)."
      | OutputDir _ -> "Directory to store testcase outputs."
      | SyncDir _ -> "Directory shared with AFL instances"
      // Options related to program execution.
      | Program _ -> "Target program for test case generation with fuzzing."
      | ExecTimeout _ -> "Execution timeout (ms) for a fuzz run (default:500)"
      | Architecture _ -> "Target program architecture (x86|x64) (default:x64)"
      | NoForkServer -> "Do not use fork server for target program execution"
      // Options related to seed.
      | InputDir _ -> "Directory containing initial seeds."
      | Arg _ -> "Command-line argument of the target program to fuzz."
      | Filepath _ -> "File input's (fixed) path"
      // Options related to grey-box concolic testing technique.
      | NSolve _ -> "Number of branches to flip in grey-box concolic testing " +
                    "when an execution path is given. 'N_solve' parameter in " +
                    "the paper."
      | NSpawn _ -> "Number of byte values to initially spawn in grey-box " +
                    "concolic testing. 'N_spawn' parameter in the paper."

type FuzzOption = {
  Verbosity         : int
  Timelimit         : int
  OutDir            : string
  SyncDir           : string
  // Options related to program execution.
  TargetProg        : string
  ExecTimeout       : uint64
  Architecture      : Arch
  ForkServer        : bool
  // Options related to seed.
  InputDir          : string
  Arg               : string
  FuzzSource        : InputSource
  // Options related to grey-box concolic testing technique.
  NSolve            : int
  NSpawn            : int
}

let parseFuzzOption (args: string array) =
  let cmdPrefix = "dotnet Eclipser.dll fuzz"
  let parser = ArgumentParser.Create<FuzzerCLI> (programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  { Verbosity = r.GetResult (<@ Verbose @>, defaultValue = 0)
    Timelimit = r.GetResult (<@ Timelimit @>, defaultValue = -1)
    OutDir = r.GetResult (<@ OutputDir @>)
    SyncDir = r.GetResult (<@ SyncDir @>, defaultValue = "")
    // Options related to program execution.
    TargetProg = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = DEF_EXEC_TO)
    Architecture = r.GetResult(<@ Architecture @>, defaultValue = "X64")
                   |> Arch.ofString
    ForkServer = not (r.Contains(<@ NoForkServer @>)) // Enable by default.
    // Options related to seed.
    InputDir = r.GetResult(<@ InputDir @>, defaultValue = "")
    Arg = r.GetResult (<@ Arg @>, defaultValue = "")
    FuzzSource = if not (r.Contains(<@ Filepath @>)) then StdInput
                 else FileInput (r.GetResult (<@ Filepath @>))
    // Options related to grey-box concolic testing technique.
    NSolve = r.GetResult(<@ NSolve @>, defaultValue = 600)
    NSpawn = r.GetResult(<@ NSpawn @>, defaultValue = 10) }

let validateFuzzOption opt =
  if opt.NSpawn < 3 then
    failwith "Should provide N_spawn greater than or equal to 3"
