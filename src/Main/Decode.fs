module Eclipser.Decode

open System
open Argu
open Utils
open BytesUtils

type Parser = ArgumentParser

type DecoderCLI =
  | [<AltCommandLine("-i")>] [<Unique>] InputDir of path: string
  | [<AltCommandLine("-o")>] [<Unique>] OutputDir of path: string
with
  interface IArgParserTemplate with
    member s.Usage =
      match s with
      | InputDir _ -> "Directory of testcases to decode"
      | OutputDir _ -> "Directory to store decoded outputs"

type DecodeOption = {
  TestcaseDir : string
  OutputDir   : string
}

let parseDecodeOption args =
  let cmdPrefix = "dotnet Eclipser.dll decode"
  let parser = ArgumentParser.Create<DecoderCLI>(programName = cmdPrefix)
  let r = try parser.Parse(args) with
          :? Argu.ArguParseException -> printLine (parser.PrintUsage()); exit 1
  { TestcaseDir = r.GetResult (<@ InputDir @>)
    OutputDir = r.GetResult (<@ OutputDir @>) }

/// Decode JSON-formatted/base64-encoded test case files and store decoded
/// payloads at output directory.
let run args =
  let decodeOpt = parseDecodeOption args
  let testcaseDir = decodeOpt.TestcaseDir
  let outDir = decodeOpt.OutputDir
  printLine ("Decoding test cases in : " + testcaseDir)
  let decodedArgDir = outDir + "/decoded_args"
  let decodedStdinDir = outDir + "/decoded_stdins"
  let decodedFileContentDir = outDir + "/decoded_files"
  let decodedFilePathDir = outDir + "/decoded_paths"
  System.IO.Directory.CreateDirectory outDir |> ignore
  System.IO.Directory.CreateDirectory decodedArgDir |> ignore
  System.IO.Directory.CreateDirectory decodedStdinDir |> ignore
  System.IO.Directory.CreateDirectory decodedFileContentDir |> ignore
  System.IO.Directory.CreateDirectory decodedFilePathDir |> ignore
  for tcFile in System.IO.Directory.EnumerateFiles(testcaseDir) do
    let tokens = tcFile.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
    let tcName = tokens.[Array.length tokens - 1]
    let tc = System.IO.File.ReadAllText tcFile |> TestCase.fromJSON
    let argInput =
      List.ofArray tc.Args
      |> List.map (fun arg -> "\"" + escapeString arg + "\"")
      |> String.concat " "
      |> strToBytes
    let stdInput = tc.StdIn
    let fileInput = tc.FileContent
    let filePath = strToBytes tc.FilePath
    Executor.writeFile (sprintf "%s/%s" decodedArgDir tcName) argInput
    Executor.writeFile (sprintf "%s/%s" decodedStdinDir tcName) stdInput
    Executor.writeFile (sprintf "%s/%s" decodedFileContentDir tcName) fileInput
    Executor.writeFile (sprintf "%s/%s" decodedFilePathDir tcName) filePath

  printLine "Decoding finished"
