module Eclipser.System

open System.Collections.Generic
open System.Runtime.InteropServices
open Config
open Utils
open Options

[<DllImport("libexec.dll")>] extern void set_env (string env_variable, string env_value)
[<DllImport("libexec.dll")>] extern void unset_env (string env_variable)

// Memorizing system information
let sysInfo = new Dictionary<string, string>()

let initialize opt =
  let outputDir =
    if opt.OutDir = ""
    then createDirWithPrefix "output"
    else (ignore (System.IO.Directory.CreateDirectory opt.OutDir); opt.OutDir)
  sysInfo.Add ("outputDir",  outputDir)
  System.IO.Directory.CreateDirectory (outputDir + "/testcase") |> ignore
  System.IO.Directory.CreateDirectory (outputDir + "/crash") |> ignore
  System.IO.Directory.CreateDirectory (outputDir + "/debug") |> ignore
  System.IO.Directory.CreateDirectory (outputDir + "/.internal") |> ignore
  sysInfo.Add ("hashLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("branchLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("coverageLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("nodeLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("syscallLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("dbgLog", System.IO.Path.GetTempFileName())
  (* Node log file should be shared between all the executions of QEMU tracer.
   * Therefore Eclipser code manages the file's creation and cleanup.
   *)
  set_env("CK_FORK_SERVER", "0") // In default, fork server is disabled
  set_env("CK_CTX_SENSITIVITY", string CtxSensitivity)
  set_env("CK_HASH_LOG", sysInfo.["hashLog"])
  set_env("CK_FEED_LOG", sysInfo.["branchLog"])
  set_env("CK_COVERAGE_LOG", sysInfo.["coverageLog"])
  set_env("CK_NODE_LOG", sysInfo.["nodeLog"])
  set_env("CK_SYSCALL_LOG", sysInfo.["syscallLog"])
  if opt.Verbosity >= 2 then
    set_env("CK_DBG_LOG", sysInfo.["dbgLog"])

let initializeForFiltering nodeCovPath pathCovPath =
  sysInfo.Add ("coverageLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("nodeLog", nodeCovPath) // Initialize with provided file
  sysInfo.Add ("edgeLog", System.IO.Path.GetTempFileName())
  sysInfo.Add ("pathLog", pathCovPath) // Initialize with provided file
  set_env("CK_COVERAGE_LOG", sysInfo.["coverageLog"])
  set_env("CK_NODE_LOG", sysInfo.["nodeLog"])
  set_env("CK_EDGE_LOG", sysInfo.["edgeLog"])
  set_env("CK_PATH_LOG", sysInfo.["pathLog"])

let cleanup () =
  System.IO.File.Delete(sysInfo.["hashLog"])
  System.IO.File.Delete(sysInfo.["branchLog"])
  System.IO.File.Delete(sysInfo.["coverageLog"])
  System.IO.File.Delete(sysInfo.["nodeLog"])
  System.IO.File.Delete(sysInfo.["syscallLog"])
  try System.IO.File.Delete(sysInfo.["dbgLog"]) with _ -> ()
