module Eclipser.TestCase

open System
open FSharp.Data
open FSharp.Data.JsonExtensions
open Utils

/// Represents a concrete test case that can be executed by the target program.
/// Note that 'TestCase' does not have additional information contained in
/// 'Seed' type (e.g. cursors, approximate path condition, etc).
type TestCase = {
    Args        : string array;
    StdIn       : byte array;
    FilePath    : string
    FileContent : byte array;
}

/// Concretize a Seed into a TestCase.
let fromSeed (seed: Seed) =
  { TestCase.Args = InputSeq.concretizeArg seed.Args
    TestCase.StdIn = Array.concat (InputSeq.concretize seed.StdIn)
    TestCase.FilePath = Seed.concretizeFilepath seed
    TestCase.FileContent = Array.concat (InputSeq.concretize seed.File.Content) }

/// Convert a TestCase into a JSON string.
let toJSON (tc: TestCase) =
  let args = Array.map escapeString tc.Args
             |> Array.map (fun arg -> "\"" + arg + "\"")
             |> String.concat ","
  let stdin = Convert.ToBase64String tc.StdIn
  let fileCont = Convert.ToBase64String tc.FileContent
  let filepath = escapeString tc.FilePath
  sprintf """{"args":[%s], "filepath":"%s","filecontent":"%s","stdin":"%s"}"""
    args filepath fileCont stdin

/// Convert a JSON string into a TestCase.
let fromJSON (str: string) =
  let info = JsonValue.Parse(str)
  { TestCase.Args = [| for v in info?args -> v.AsString() |]
    StdIn = info?stdin.AsString() |> Convert.FromBase64String
    FilePath = info?filepath.AsString()
    FileContent = info?filecontent.AsString() |> Convert.FromBase64String }
