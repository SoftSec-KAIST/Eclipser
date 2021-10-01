module Eclipser.Options

open Argu
open Utils
open Config

type FuzzerCLI =
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<AltCommandLine("-v")>] [<Unique>] Verbose of int
  | [<AltCommandLine("-t")>] [<Mandatory>] [<Unique>] Timelimit of sec: int
  | [<AltCommandLine("-o")>] [<Mandatory>] [<Unique>] OutputDir of path: string
  | [<Mandatory>] [<Unique>] Src of string
  // Options related to seed initialization
  | [<AltCommandLine("-i")>] [<Unique>] InitSeedsDir of path: string
  | [<Unique>] InitSeedSrc of string
  | [<Unique>] MaxArgLen of int list
  | [<Unique>] MaxFileLen of int
  | [<Unique>] MaxStdInLen of int
  | [<Unique>] InitArg of string
  | [<AltCommandLine("-f")>] [<AltCommandLine("--fixfilepath")>] [<Unique>] Filepath of string
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
      | Src _ -> "Input source to fuzz (<arg|file|stdin|auto>).\n" +
                 "In case of 'auto', fuzzer will start from fuzzing command " +
                 "line arguments and then automatically identify and fuzz " +
                 "standard input and file inputs."
      // Options related to seed initialization
      | InitSeedsDir _ -> "Directory containing initial seeds."
      | InitSeedSrc _ -> "Input source to use the initial seeds for " +
                         "(<arg|file|stdin>)."
      | MaxArgLen _ -> "Maximum len of cmdline argument (default: [8byte])"
      | MaxFileLen _ -> "Maximum len of file input (default: 1MB)"
      | MaxStdInLen _ -> "Maximum len of standard input (default: 1MB)"
      | InitArg _ -> "Initial command-line argument of program under test."
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
  FuzzMode          : FuzzMode
  // Options related to program execution
  TargetProg        : string
  ExecTimeout       : uint64
  UsePty            : bool
  Architecture      : Arch
  // Options related to seed
  InitSeedsDir      : string
  InitSeedSrc       : InputKind
  MaxArgLen         : int list
  MaxFileLen        : int
  MaxStdInLen       : int
  InitArg           : string
  Filepath          : string
  // Options related to test case generation
  NSolve            : int
  NSpawn            : int
  GreyConcolicOnly  : bool
  RandFuzzOnly      : bool
}

let private printInitSeedSrcUsage() =
  printLine "It seems '--src=auto' and '--initseeddir' (-i) are given."
  printLine "In such case, '--initseedsrc' option is required, too."

let parseFuzzOption (args: string array) =
  let cmdPrefix = "dotnet Eclipser.dll fuzz"
  let parser = ArgumentParser.Create<FuzzerCLI> (programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  let fuzzMode = FuzzMode.ofString (r.GetResult(<@ Src @>))
  let seedSrcStr = r.GetResult(<@ InitSeedSrc @>, defaultValue = "")
  let seedSrc = if seedSrcStr <> "" then InputKind.ofString seedSrcStr
                elif fuzzMode <> AutoFuzz then InputKind.decideInitSrc fuzzMode
                else (printInitSeedSrcUsage(); exit 1)
  { Verbosity = r.GetResult (<@ Verbose @>, defaultValue = 0)
    OutDir = r.GetResult (<@ OutputDir @>)
    Timelimit = r.GetResult (<@ Timelimit @>)
    FuzzMode = fuzzMode
    // Options related to program execution
    TargetProg = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = DefaultExecTO)
    UsePty = r.Contains (<@ UsePty @>)
    Architecture = r.GetResult(<@ Architecture @>, defaultValue = "X64")
                   |> Arch.ofString
    // Options related to seed
    InitSeedsDir = r.GetResult(<@ InitSeedsDir @>, defaultValue = "")
    InitSeedSrc = seedSrc
    MaxArgLen = r.GetResult (<@ MaxArgLen @>, defaultValue = [8])
    MaxFileLen = r.GetResult (<@ MaxFileLen @>, defaultValue = 1048576)
    MaxStdInLen = r.GetResult (<@ MaxStdInLen @>, defaultValue = 1048576)
    InitArg = r.GetResult (<@ InitArg @>, defaultValue = "")
    Filepath = r.GetResult (<@ Filepath @>, defaultValue = "")
    // Options related to test case generation
    NSolve = r.GetResult(<@ NSolve @>, defaultValue = 600)
    NSpawn = r.GetResult(<@ NSpawn @>, defaultValue = 10)
    GreyConcolicOnly = r.Contains(<@ GreyConcolicOnly @>)
    RandFuzzOnly = r.Contains(<@ RandFuzzOnly @>) }

let validateFuzzOption opt =
  if opt.FuzzMode = FileFuzz && opt.Filepath = "" then
    printLine "Should specify the file input path in file fuzzing mode."
    exit 1
  if opt.GreyConcolicOnly && opt.RandFuzzOnly then
    printLine "Cannot specify --greyconcoliconly and --randfuzzonly together."
    exit 1
  if opt.NSpawn < 3 then
    failwith "Should provide N_spawn greater than or equal to 3"
  if opt.InitSeedSrc = Args && List.length opt.MaxArgLen <> 1 then
    failwith "When --initseedsrc is 'arg', --maxarglen must be singleton."
