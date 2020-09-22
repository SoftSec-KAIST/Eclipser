module Eclipser.Executor

open System
open System.IO
open System.Runtime.InteropServices
open Config
open Utils
open Options

let private WHITES = [| ' '; '\t'; '\n' |]

/// Kinds of QEMU instrumentor. Each instrumentor serves different purposes.
type Tracer = Coverage | Branch | BBCount

/// Specifies the method to handle execution timeout. It is necessary to use GDB
/// when replaying test cases against a gcov-compiled binary, to log coverage
/// correctly.
type TimeoutHandling =
  /// Send SIGTERM to the process
  | SendSigterm = 0
  /// Attach to the process with GDB and quit()
  | GDBQuit = 1

[<DllImport("libexec.dll")>] extern void set_env (string env_variable, string env_value)
[<DllImport("libexec.dll")>] extern void initialize_exec (TimeoutHandling is_replay)
[<DllImport("libexec.dll")>] extern int init_forkserver_coverage (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern int init_forkserver_branch (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern void kill_forkserver ()
[<DllImport("libexec.dll")>] extern Signal exec (int argc, string[] argv, int stdin_size, byte[] stdin_data, uint64 timeout, bool use_pty)
[<DllImport("libexec.dll")>] extern Signal exec_fork_coverage (uint64 timeout, int stdin_size, byte[] stdin_data)
[<DllImport("libexec.dll")>] extern Signal exec_fork_branch (uint64 timeout, int stdin_size, byte[] stdin_data)
[<DllImport("libexec.dll")>] extern int prepare_shared_mem ()
[<DllImport("libexec.dll")>] extern int release_shared_mem ()

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

let mutable pathHashLog = ""
let mutable branchTraceLog = ""
let mutable coverageLog = ""
let mutable dbgLog = ""
let mutable forkServerEnabled = false

let initialize opt =
  let outDir = opt.OutDir
  let verbosity = opt.Verbosity
  // Set environment variables for the instrumentor.
  pathHashLog <- System.IO.Path.Combine(outDir, ".path_hash")
  branchTraceLog <- System.IO.Path.Combine(outDir, ".branch_trace")
  coverageLog <- System.IO.Path.Combine(outDir, ".coverage")
  dbgLog <- System.IO.Path.Combine(outDir, ".debug_msg")
  set_env("CK_HASH_LOG", System.IO.Path.GetFullPath(pathHashLog))
  set_env("CK_FEED_LOG", System.IO.Path.GetFullPath(branchTraceLog))
  set_env("CK_COVERAGE_LOG", System.IO.Path.GetFullPath(coverageLog))
  if verbosity >= 2 then
    set_env("CK_DBG_LOG", System.IO.Path.GetFullPath(dbgLog))
  // Set other environment variables in advance, to avoid affecting path hash.
  set_env("CK_FEED_ADDR", "0")
  set_env("CK_FEED_IDX", "0")
  set_env("CK_CTX_SENSITIVITY", string CtxSensitivity)
  initialize_exec TimeoutHandling.SendSigterm
  // Initialize shared memory.
  let shmID = prepare_shared_mem()
  if shmID < 0 then failwith "Failed to allcate shared memory"
  set_env("CK_SHM_ID", string shmID)
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
  if release_shared_mem() < 0 then log "Failed to release shared memory!"
  removeFile pathHashLog
  removeFile branchTraceLog
  removeFile coverageLog
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

let private clearFile seed =
  match seed.Source with
  | StdInput -> ()
  | FileInput filePath -> removeFile filePath

let private prepareStdIn seed =
  match seed.Source with
  | StdInput -> Seed.concretize seed
  | FileInput filePath -> [| |]

(*** Tracer result parsing functions ***)

let private parseCoverage filename =
  let content = readAllLines filename
  match content with
  | [newEdgeCnt; pathHash; edgeHash] ->
    int newEdgeCnt, uint64 pathHash, uint64 edgeHash
  | _ -> log "[Warning] Coverage logging failed : %A" content; (0, 0UL, 0UL)

let private parseExecHash filename =
  let content = readAllLines filename
  match content with
  | [hash] -> uint64 hash
  | _ -> log "[Warning] Coverage logging failed : %A" content; 0UL

let private is64Bit = function
  | X86 -> false
  | X64 -> true

let private readEdgeSet opt filename =
  try
    let f = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
    use r = new BinaryReader(f)
    let arch = opt.Architecture
    let mutable valid = true
    let edgeList =
      [ while valid do
          let addr = try if is64Bit arch
                         then r.ReadUInt64()
                         else uint64 (r.ReadUInt32())
                     with :? EndOfStreamException -> 0UL
          if addr = 0UL then valid <- false else yield addr ]
    Set.ofList edgeList
  with | :? FileNotFoundException -> Set.empty

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

let private runCoverageTracerForked opt (stdin: byte array) =
  let timeout = opt.ExecTimeout
  let signal = exec_fork_coverage(timeout, stdin.Length, stdin)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

let private runBranchTracerForked opt (stdin: byte array) =
  let timeout = opt.ExecTimeout
  let signal = exec_fork_branch(timeout, stdin.Length, stdin)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

(*** Top-level tracer executor functions ***)

let getCoverage opt seed =
  setupFile seed
  let stdin = prepareStdIn seed
  let exitSig = if forkServerEnabled
                then runCoverageTracerForked opt stdin
                else runTracer Coverage opt stdin
  let newEdgeCnt, pathHash, edgeHash = parseCoverage coverageLog
  clearFile seed
  (newEdgeCnt, pathHash, edgeHash, exitSig)

let getBranchTrace opt seed tryVal =
  set_env("CK_FEED_ADDR", "0")
  set_env("CK_FEED_IDX", "0")
  setupFile seed
  let stdin = prepareStdIn seed
  if forkServerEnabled then runBranchTracerForked opt stdin
  else runTracer Branch opt stdin
  |> ignore
  let pathHash = parseExecHash pathHashLog
  let branchTrace = readBranchTrace opt branchTraceLog tryVal
  clearFile seed
  removeFile pathHashLog
  removeFile branchTraceLog
  pathHash, branchTrace

let getBranchInfoAt opt seed tryVal targPoint =
  set_env("CK_FEED_ADDR", sprintf "%016x" targPoint.Addr)
  set_env("CK_FEED_IDX", sprintf "%016x" targPoint.Idx)
  setupFile seed
  let stdin = prepareStdIn seed
  if forkServerEnabled then runBranchTracerForked opt stdin
  else runTracer Branch opt stdin
  |> ignore
  let pathHash = parseExecHash pathHashLog
  let branchInfoOpt =
    match readBranchTrace opt branchTraceLog tryVal with
    | [] -> None
    | [ branchInfo ] -> Some branchInfo
    | branchInfos -> None
  clearFile seed
  removeFile pathHashLog
  removeFile branchTraceLog
  (pathHash, branchInfoOpt)

let nativeExecute opt seed =
  let targetProg = opt.TargetProg
  setupFile seed
  let stdin = prepareStdIn seed
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let cmdLine = opt.Arg.Split(WHITES, StringSplitOptions.RemoveEmptyEntries)
  let args = Array.append [| targetProg |] cmdLine
  let argc = args.Length
  let signal = exec(argc, args, stdin.Length, stdin, timeout, usePty)
  clearFile seed
  signal
