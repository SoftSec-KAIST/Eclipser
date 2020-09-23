module Eclipser.Executor

open System
open System.IO
open System.Runtime.InteropServices
open Utils
open Options

let private WHITES = [| ' '; '\t'; '\n' |]

/// Kinds of QEMU instrumentor. Each instrumentor serves different purposes.
type Tracer = Coverage | Branch | BBCount

[<DllImport("libexec.dll")>] extern void set_env (string env_variable, string env_value)
[<DllImport("libexec.dll")>] extern void initialize_exec ()
[<DllImport("libexec.dll")>] extern int init_forkserver_coverage (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern int init_forkserver_branch (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern void kill_forkserver ()
[<DllImport("libexec.dll")>] extern Signal exec (int argc, string[] argv, int stdin_size, byte[] stdin_data, uint64 timeout, bool use_pty)
[<DllImport("libexec.dll")>] extern Signal exec_fork_coverage (uint64 timeout, int stdin_size, byte[] stdin_data)
[<DllImport("libexec.dll")>] extern Signal exec_fork_branch (uint64 timeout, int stdin_size, byte[] stdin_data, uint64 targ_addr, uint32 targ_index, int measure_cov)

(*** Tracer and file paths ***)

let buildDir =
  let exePath = System.Reflection.Assembly.GetEntryAssembly().Location
  System.IO.Path.GetDirectoryName(exePath)
let coverageTracerX86 = sprintf "%s/qemu-trace-coverage-x86" buildDir
let coverageTracerX64 = sprintf "%s/qemu-trace-coverage-x64" buildDir
let branchTracerX86 = sprintf "%s/qemu-trace-branch-x86" buildDir
let branchTracerX64 = sprintf "%s/qemu-trace-branch-x64" buildDir
let bbCountTracerX86 = sprintf "%s/qemu-trace-bbcount-x86" buildDir
let bbCountTracerX64 = sprintf "%s/qemu-trace-bbcount-x64" buildDir

let selectTracer tracer arch =
  match tracer, arch with
  | Coverage, X86 -> coverageTracerX86
  | Coverage, X64 -> coverageTracerX64
  | Branch, X86 -> branchTracerX86
  | Branch, X64 -> branchTracerX64
  | BBCount, X86 -> bbCountTracerX86
  | BBCount, X64 -> bbCountTracerX64

let mutable branchLog = ""
let mutable coverageLog = ""
let mutable bitmapLog = ""
let mutable dbgLog = ""
let mutable forkServerEnabled = false

let initialize opt =
  let outDir = opt.OutDir
  let verbosity = opt.Verbosity
  // Set environment variables for the instrumentor.
  branchLog <- System.IO.Path.Combine(outDir, ".branch")
  coverageLog <- System.IO.Path.Combine(outDir, ".coverage")
  bitmapLog <- System.IO.Path.Combine(outDir, ".bitmap")
  dbgLog <- System.IO.Path.Combine(outDir, ".debug")
  set_env("CK_FEED_LOG", System.IO.Path.GetFullPath(branchLog))
  set_env("CK_COVERAGE_LOG", System.IO.Path.GetFullPath(coverageLog))
  use bitmapFile = File.Create(bitmapLog)
  bitmapFile.SetLength(0x10000L)
  set_env("CK_BITMAP_LOG", System.IO.Path.GetFullPath(bitmapLog))
  if verbosity >= 2 then
    set_env("CK_DBG_LOG", System.IO.Path.GetFullPath(dbgLog))
  set_env("CK_FEED_ADDR", "0")
  set_env("CK_FEED_IDX", "0")
  set_env("CK_MEASURE_COV", "0")
  initialize_exec ()
  // Initialize fork server.
  forkServerEnabled <- true
  set_env("CK_FORK_SERVER", "1")
  let cmdLine = opt.Arg.Split(WHITES, StringSplitOptions.RemoveEmptyEntries)
  let coverageTracer = selectTracer Coverage opt.Architecture
  let args = Array.append [|coverageTracer; opt.TargetProg|] cmdLine
  let pidCoverage = init_forkserver_coverage(args.Length, args, opt.ExecTimeout)
  if pidCoverage = -1 then
    failwith "Failed to initialize fork server for coverage tracer"
  let branchTracer = selectTracer Branch opt.Architecture
  let args = Array.append [|branchTracer; opt.TargetProg|] cmdLine
  let pidBranch = init_forkserver_branch (args.Length, args, opt.ExecTimeout)
  if pidBranch = -1 then
    failwith "Failed to initialize fork server for branch tracer"

let cleanup () =
  if forkServerEnabled then kill_forkserver ()
  removeFile branchLog
  removeFile coverageLog
  removeFile bitmapLog
  removeFile dbgLog

let abandonForkServer () =
  log "Abandon fork server"
  forkServerEnabled <- false
  set_env("CK_FORK_SERVER", "0")
  kill_forkserver ()

(*** File handling utilities ***)

let readAllLines filename =
  try List.ofSeq (System.IO.File.ReadLines filename) with
  | :? System.IO.FileNotFoundException -> []

let private setupFile seed =
  match seed.Source with
  | StdInput -> ()
  | FileInput filePath -> writeFile filePath (Seed.concretize seed)

let private prepareStdIn seed =
  match seed.Source with
  | StdInput -> Seed.concretize seed
  | FileInput filePath -> [| |]

(*** Tracer result parsing functions ***)

/// TODO. Currently we only support edge coverage gain. Will extend the system
/// to support path coverage gain if needed.
let private parseCoverage filename =
  match readAllLines filename with
  | [newEdgeFlag; _] -> if int newEdgeFlag = 1 then NewEdge else NoGain
  | x -> log "[Warning] Coverage logging failed : %A" x; NoGain

let private is64Bit = function
  | X86 -> false
  | X64 -> true

let private parseBranchTraceLog opt (r:BinaryReader) tryVal =
  let arch = opt.Architecture
  let addr =
    try if is64Bit arch then r.ReadUInt64() else uint64 (r.ReadUInt32 ()) with
    | :? EndOfStreamException -> 0UL
  if addr = 0UL then None else
    try
      let typeInfo = int(r.ReadByte())
      let opSize = typeInfo &&& 0x3f
      let brType =
        match typeInfo >>> 6 with
        | 0 -> Equality
        | 1 -> SignedSize
        | 2 -> UnsignedSize
        | _ -> log "[Warning] Unexpected branch type"; failwith "Unmatched"
      let oprnd1, oprnd2 =
        match opSize with
        | 1 -> uint64 (r.ReadByte()), uint64 (r.ReadByte())
        | 2 -> uint64 (r.ReadUInt16()), uint64 (r.ReadUInt16())
        | 4 -> uint64 (r.ReadUInt32()), uint64 (r.ReadUInt32())
        | 8 -> r.ReadUInt64(), r.ReadUInt64()
        | _ -> log "[Warning] Unexpected operand size"; failwith "Unmatched"
      let dist = (bigint oprnd1) - (bigint oprnd2)
      let branchInfo =
        { InstAddr = addr; BrType = brType; TryVal = tryVal; OpSize = opSize;
          Oprnd1 = oprnd1; Oprnd2 = oprnd2; Distance = dist }
      Some branchInfo
    with _ -> None

let private readBranchTrace opt filename tryVal =
  try
    let f = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
    use r = new BinaryReader(f)
    let mutable valid = true
    [while valid do
      match parseBranchTraceLog opt r tryVal with
      | None -> valid <- false
      | Some branchInfo -> yield branchInfo ]
  with | :? FileNotFoundException -> []

let private tryReadBranchInfo opt filename tryVal =
  match readBranchTrace opt filename tryVal with
  | [] -> None
  | [ branchInfo ] -> Some branchInfo
  | _ -> None

(*** Tracer execute functions ***)

let private runTracer tracerType opt (stdin: byte array) =
  let targetProg = opt.TargetProg
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let tracer = selectTracer tracerType opt.Architecture
  let cmdLine = opt.Arg.Split(WHITES, StringSplitOptions.RemoveEmptyEntries)
  let args = Array.append [|tracer; targetProg|] cmdLine
  let argc = args.Length
  exec(argc, args, stdin.Length, stdin, timeout, usePty)

let private runCoverageTracerForked opt stdin =
  let timeout = opt.ExecTimeout
  let stdLen = Array.length stdin
  let signal = exec_fork_coverage(timeout, stdLen, stdin)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

let private runBranchTracerForked opt stdin addr idx measureCov =
  let timeout = opt.ExecTimeout
  let stdLen = Array.length stdin
  let covFlag = if measureCov then 1 else 0
  let signal = exec_fork_branch(timeout, stdLen, stdin, addr, idx, covFlag)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

let private setEnvForBranch (addr: uint64) (idx: uint32) measureCov =
  set_env("CK_FEED_ADDR", sprintf "%016x" addr)
  set_env("CK_FEED_IDX", sprintf "%016x" idx)
  set_env("CK_MEASURE_COV", sprintf "%d" (if measureCov then 1 else 0))

(*** Top-level tracer executor functions ***)

let getCoverage opt seed =
  setupFile seed
  let stdin = prepareStdIn seed
  let exitSig = if forkServerEnabled then runCoverageTracerForked opt stdin
                else runTracer Coverage opt stdin
  let coverageGain = parseCoverage coverageLog
  (exitSig, coverageGain)

let getBranchTrace opt seed tryVal =
  setupFile seed
  let stdin = prepareStdIn seed
  let exitSig =
    if forkServerEnabled then runBranchTracerForked opt stdin 0UL 0ul true
    else setEnvForBranch 0UL 0ul true; runTracer Branch opt stdin
  let coverageGain = parseCoverage coverageLog
  let branchTrace = readBranchTrace opt branchLog tryVal
  removeFile coverageLog
  (exitSig, coverageGain, branchTrace)

let getBranchInfo opt seed tryVal targPoint =
  setupFile seed
  let stdin = prepareStdIn seed
  let addr, idx = targPoint.Addr, uint32 targPoint.Idx
  let exitSig =
    if forkServerEnabled then runBranchTracerForked opt stdin addr idx true
    else setEnvForBranch addr idx true; runTracer Branch opt stdin
  let coverageGain = parseCoverage coverageLog
  let branchInfoOpt = tryReadBranchInfo opt branchLog tryVal
  removeFile coverageLog
  (exitSig, coverageGain, branchInfoOpt)

let getBranchInfoOnly opt seed tryVal targPoint =
  setupFile seed
  let stdin = prepareStdIn seed
  let addr, idx = targPoint.Addr, uint32 targPoint.Idx
  if forkServerEnabled then runBranchTracerForked opt stdin addr idx false
  else setEnvForBranch addr idx false; runTracer Branch opt stdin
  |> ignore
  let brInfoOpt = tryReadBranchInfo opt branchLog tryVal
  brInfoOpt

let nativeExecute opt seed =
  let targetProg = opt.TargetProg
  setupFile seed
  let stdin = prepareStdIn seed
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let cmdLine = opt.Arg.Split(WHITES, StringSplitOptions.RemoveEmptyEntries)
  let args = Array.append [| targetProg |] cmdLine
  let argc = args.Length
  exec(argc, args, stdin.Length, stdin, timeout, usePty)

