module Eclipser.Replay

open Argu
open Config
open Utils

type ReplayerCLI =
  | [<AltCommandLine("-p")>] [<Mandatory>] [<Unique>] Program of path: string
  | [<AltCommandLine("-i")>] [<Mandatory>] [<Unique>] InputDir of path: string
  // Options related to execution of program
  | [<Unique>] ExecTimeout of millisec: uint64
  | [<Unique>] UsePty
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | Program _ -> "Target program for test case replaying"
      | InputDir _ -> "Directory of testcases to replay"
      // Options related to execution of program
      | ExecTimeout _ -> "Timeout for each program execution (default:500)"
      | UsePty _ -> "Use pseudo terminal for STDIN during the execution"

type ReplayOption = {
  Program     : string
  ExecTimeout : uint64
  UsePty      : bool
  TestcaseDir : string
}

let parseReplayOption args =
  let cmdPrefix = "dotnet Eclipser.dll replay"
  let parser = ArgumentParser.Create<ReplayerCLI> (programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  { Program = System.IO.Path.GetFullPath(r.GetResult (<@ Program @>))
    ExecTimeout = r.GetResult (<@ ExecTimeout @>, defaultValue = DefaultExecTO)
    UsePty = r.Contains (<@ UsePty @>)
    TestcaseDir = r.GetResult (<@ InputDir @>) }

/// Replay test cases in the given directory on target program. This utility was
/// devised to estimate the code coverage of generated test cases, by replaying
/// test cases against a program compiled with 'gcov' options.
let run args =
  let opt = parseReplayOption args
  let program = opt.Program
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let testcaseDir = opt.TestcaseDir
  assertFileExists program
  Executor.initialize_exec Executor.TimeoutHandling.GDBQuit
  printLine ("Start replaying test cases in : " + testcaseDir)
  for file in System.IO.Directory.EnumerateFiles(testcaseDir) do
    let tc = System.IO.File.ReadAllText file |> TestCase.fromJSON
    printLine ("Replaying test case : " + file)
    let args = Array.append [| program |] tc.Args
    let argc = args.Length
    let stdin = tc.StdIn
    let stdinLen = stdin.Length
    let files = Executor.setupFiles program tc true
    Executor.exec(argc, args, stdinLen, stdin, timeout, usePty) |> ignore
    removeFiles files
  printLine "Replay finished"
