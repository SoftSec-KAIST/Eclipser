module Eclipser.Options

open Argu
open Utils
open Config

type FuzzerCLI =
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<AltCommandLine("-v")>] Verbose of int
  | [<AltCommandLine("-t")>] Timelimit of sec: int
  | [<AltCommandLine("-o")>] OutputDir of path: string
  | [<Mandatory>] [<Unique>] Src of string
  // Options related to seed initialization
  | [<AltCommandLine("-i")>] InitSeedsDir of path: string
  | MaxArgLen of int list
  | MaxFileLen of int
  | MaxStdInLen of int
  | InitArg of string
  | FixFilepath of string
  // Options related to execution of program
  | ExecTimeout of millisec:uint64
  | UsePty
  | Architecture of string
  // Options related to fuzzing process
  | NSolve of int
  | NSpawn of int
  | GreyConcolicOnly
  | RandFuzzOnly
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Program _ -> "Target program for test case generation with fuzzing."
      | Verbose _ -> "Verbosity level to control debug messages (default:0)."
      | Timelimit _ -> "Timeout for fuzz testing (in seconds)."
      | OutputDir _ -> "Directory to store testcase outputs."
      | Src _ -> "Input source to fuzz (<arg|file|stdin|auto>).\n" +
                 "In case of 'auto', fuzzer will start from fuzzing command " +
                 "line arguments and then automatically identify and fuzz " +
                 "standard input and file inputs."
      // Options related to seed initialization
      | InitSeedsDir _ -> "Directory containing initial seeds."
      | MaxArgLen _ -> "Maximum len of cmdline argument (default:[8])"
      | MaxFileLen _ -> "Maximum len of file input (default:[8])"
      | MaxStdInLen _ -> "Maximum len of file input (default:[8])"
      | InitArg _ -> "Initial command-line argument of program under test."
      | FixFilepath _ -> "Fix the path of file input"
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
  Timelimit         : float
  FuzzMode          : FuzzMode
  // Options related to program execution
  TargetProg        : string
  ExecTimeout       : uint64
  UsePty            : bool
  Architecture      : Arch
  // Options related to seed
  InitSeedsDir      : string
  MaxArgLen         : int list
  MaxFileLen        : int
  MaxStdInLen       : int
  InitArg           : string
  FixFilepath       : string
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
    OutDir = r.GetResult (<@ OutputDir @>, defaultValue = "")
    Timelimit = float (r.GetResult (<@ Timelimit @>))
    FuzzMode = FuzzMode.ofString (r.GetResult(<@ Src @>))
    // Options related to program execution
    TargetProg = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = DefaultExecTO)
    UsePty = r.Contains (<@ UsePty @>)
    Architecture = r.GetResult(<@ Architecture @>, defaultValue = "X64")
                   |> Arch.ofString
    // Options related to seed
    InitSeedsDir = r.GetResult(<@ InitSeedsDir @>, defaultValue = "")
    MaxArgLen = r.GetResult (<@ MaxArgLen @>, defaultValue = [8])
    MaxFileLen = r.GetResult (<@ MaxFileLen @>, defaultValue = 8)
    MaxStdInLen = r.GetResult (<@ MaxStdInLen @>, defaultValue = 8)
    InitArg = r.GetResult (<@ InitArg @>, defaultValue = "")
    FixFilepath = r.GetResult (<@ FixFilepath @>, defaultValue = "")
    // Options related to test case generation
    NSolve = r.GetResult(<@ NSolve @>, defaultValue = 600)
    NSpawn = r.GetResult(<@ NSpawn @>, defaultValue = 10)
    GreyConcolicOnly = r.Contains(<@ GreyConcolicOnly @>)
    RandFuzzOnly = r.Contains(<@ RandFuzzOnly @>) }

let validateFuzzOption opt =
  if opt.FuzzMode = FileFuzz && opt.FixFilepath = "" then
    printLine "Should specify the input file path in file fuzzing mode"; exit 1
