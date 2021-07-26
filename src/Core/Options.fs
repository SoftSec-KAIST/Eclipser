module Eclipser.Options

open Argu
open Utils

type FuzzerCLI =
  // General options.
  | [<AltCommandLine("-v")>] [<Unique>] Verbose of int
  | [<AltCommandLine("-t")>] [<Unique>] Timelimit of sec: int
  | [<AltCommandLine("-i")>] [<Unique>] InputDir of path: string
  | [<AltCommandLine("-o")>] [<Mandatory>] [<Unique>] OutputDir of path: string
  | [<AltCommandLine("-s")>] [<Unique>] SyncDir of path: string
  // Options related to the target program execution.
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<AltCommandLine("-e")>] [<Unique>] ExecTimeout of millisec:uint64
  | [<Unique>] Architecture of string
  | [<Unique>] NoForkServer
  | [<Unique>] Arg of string
  | [<AltCommandLine("-f")>] [<Unique>] Filepath of string
  // Options related to grey-box concolic testing technique.
  | [<Unique>] NSpawn of int
  | [<Unique>] NSolve of int
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Verbose _ -> "Verbosity level to control debug messages (default: 1)"
      | Timelimit _ -> "Timeout for fuzz testing (in sec, default: INFINITE)"
      | InputDir _ -> "Directory containing initial seeds"
      | OutputDir _ -> "Directory to store testcase outputs"
      | SyncDir _ -> "Directory shared with AFL instances"
      | Program _ -> "Target program for test case generation with fuzzing"
      | ExecTimeout _ -> "Execution timeout (ms) for a fuzz run (default: 500)"
      | Architecture _ -> "Target program architecture (x86|x64) (default: x64)"
      | NoForkServer -> "Do not use fork server for target program execution"
      | Arg _ -> "Command-line argument of the target program to fuzz"
      | Filepath _ -> "Path of input file used by the target program"
      | NSpawn _ -> "Number of seeds to spawn in the first step of grey-box " +
                    "concolic testing (default: 10)"
      | NSolve _ -> "Number of branches to flip in grey-box concolic testing " +
                    "when an execution path is given (default: 600)"

type FuzzOption = {
  // General options.
  Verbosity         : int
  Timelimit         : int
  InputDir          : string
  OutDir            : string
  SyncDir           : string
  // Options related to the target program execution.
  TargetProg        : string
  ExecTimeout       : uint64
  Architecture      : Arch
  ForkServer        : bool
  Arg               : string
  FuzzSource        : InputSource
  // Options related to grey-box concolic testing technique.
  NSpawn            : int
  NSolve            : int
}

let parseFuzzOption (args: string array) =
  let cmdPrefix = "dotnet Eclipser.dll"
  let parser = ArgumentParser.Create<FuzzerCLI> (programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  { Verbosity = r.GetResult (<@ Verbose @>, defaultValue = 1)
    Timelimit = r.GetResult (<@ Timelimit @>, defaultValue = -1)
    InputDir = r.GetResult(<@ InputDir @>, defaultValue = "")
    OutDir = r.GetResult (<@ OutputDir @>)
    SyncDir = r.GetResult (<@ SyncDir @>, defaultValue = "")
    TargetProg = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = 0UL)
    Architecture = r.GetResult(<@ Architecture @>, defaultValue = "X64")
                   |> Arch.ofString
    ForkServer = not (r.Contains(<@ NoForkServer @>)) // Enable by default.
    Arg = r.GetResult (<@ Arg @>, defaultValue = "")
    FuzzSource = if not (r.Contains(<@ Filepath @>)) then StdInput
                 else FileInput (r.GetResult (<@ Filepath @>))
    NSpawn = r.GetResult(<@ NSpawn @>, defaultValue = 10)
    NSolve = r.GetResult(<@ NSolve @>, defaultValue = 600) }

let validateFuzzOption opt =
  if opt.NSpawn < 3 then
    failwith "Should provide N_spawn greater than or equal to 3"
