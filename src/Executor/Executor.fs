module Eclipser.Executor

open System
open System.IO
open System.Runtime.InteropServices
open Config
open Utils
open Options
open Syscall
open TestCase

/// Kinds of QEMU instrumentor. Each instrumentor serves different purposes.
type Tracer = Coverage | Branch | Syscall | BBCount

/// Specifies the method to handle execution timeout. It is necessary to use GDB
/// when replaying test cases against a gcov-compiled binary, to log coverage
/// correctly.
type TimeoutHandling =
  /// Send SIGTERM to the process
  | SendSigterm = 0
  /// Attach to the process with GDB and quit()
  | GDBQuit = 1

/// Mode of coverage tracer execution.
type CoverageTracerMode =
  /// Count the number of new edges (also obtains a path hash as a by-product)
  | CountNewEdge = 0
  /// Calculate the hash of edge set visitied in this execution
  | EdgeHash = 1
  /// Find the edge set visited in this execution
  | EdgeSet = 2

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
let coverageTracerX86 = sprintf "%s/qemu-trace-pathcov-x86" buildDir
let coverageTracerX64 = sprintf "%s/qemu-trace-pathcov-x64" buildDir
let branchTracerX86 = sprintf "%s/qemu-trace-feedback-x86" buildDir
let branchTracerX64 = sprintf "%s/qemu-trace-feedback-x64" buildDir
let syscallTracerX86 = sprintf "%s/qemu-trace-syscall-x86" buildDir
let syscallTracerX64 = sprintf "%s/qemu-trace-syscall-x64" buildDir
let bbCountTracerX86 = sprintf "%s/qemu-trace-bbcount-x86" buildDir
let bbCountTracerX64 = sprintf "%s/qemu-trace-bbcount-x64" buildDir

let selectTracer tracer arch =
  match tracer, arch with
  | Coverage, X86 -> coverageTracerX86
  | Coverage, X64 -> coverageTracerX64
  | Branch, X86 -> branchTracerX86
  | Branch, X64 -> branchTracerX64
  | Syscall, X86 -> syscallTracerX86
  | Syscall, X64 -> syscallTracerX64
  | BBCount, X86 -> bbCountTracerX86
  | BBCount, X64 -> bbCountTracerX64

let mutable pathHashLog = ""
let mutable branchTraceLog = ""
let mutable coverageLog = ""
let mutable syscallTraceLog = ""
let mutable dbgLog = ""

// TODO : Reorder functions and create a top-level function
let cleanUpSharedMem () =
  if release_shared_mem() < 0 then log "Failed to release shared memory!"

let prepareSharedMem () =
  let shmID = prepare_shared_mem()
  if shmID < 0 then failwith "Failed to allcate shared memory"
  set_env("CK_SHM_ID", string shmID)

let initialize outdir verbosity =
  pathHashLog <- System.IO.Path.Combine(outdir, ".path_hash")
  branchTraceLog <- System.IO.Path.Combine(outdir, ".branch_trace")
  coverageLog <- System.IO.Path.Combine(outdir, ".coverage")
  syscallTraceLog <- System.IO.Path.Combine(outdir, ".syscall_trace")
  dbgLog <- System.IO.Path.Combine(outdir, ".debug_msg")
  set_env("CK_HASH_LOG", System.IO.Path.GetFullPath(pathHashLog))
  set_env("CK_FEED_LOG", System.IO.Path.GetFullPath(branchTraceLog))
  set_env("CK_COVERAGE_LOG", System.IO.Path.GetFullPath(coverageLog))
  set_env("CK_SYSCALL_LOG", System.IO.Path.GetFullPath(syscallTraceLog))
  if verbosity >= 2 then
    set_env("CK_DBG_LOG", System.IO.Path.GetFullPath(dbgLog))
  // Set all the other env. variables in advance, to avoid affecting path hash.
  set_env("CK_MODE", "0")
  set_env("CK_FEED_ADDR", "0")
  set_env("CK_FEED_IDX", "0")
  set_env("CK_FORK_SERVER", "0") // In default, fork server is not enabled.
  set_env("CK_CTX_SENSITIVITY", string CtxSensitivity)

let cleanUpFiles () =
  removeFiles [pathHashLog; branchTraceLog; coverageLog]
  removeFiles [syscallTraceLog; dbgLog]

(*** Statistics ***)

let mutable totalExecutions = 0
let mutable phaseExecutions = 0

let getTotalExecutions () = totalExecutions
let getPhaseExecutions () = phaseExecutions
let resetPhaseExecutions () = phaseExecutions <- 0

(*** Resource scheduling ***)

let mutable allowedExecutions = 0
let allocateResource n = allowedExecutions <- n
let isResourceExhausted () = allowedExecutions <= 0
let incrExecutionCount () =
  allowedExecutions <- allowedExecutions - 1
  totalExecutions <- totalExecutions + 1
  phaseExecutions <- phaseExecutions + 1

(*** Fork server ***)

let mutable forkServerEnabled = false

let initForkServer opt =
  forkServerEnabled <- true
  set_env("CK_FORK_SERVER", "1")
  let white = [| ' '; '\t'; '\n' |]
  let initArgStr = opt.InitArg
  let initArgs = initArgStr.Split(white, StringSplitOptions.RemoveEmptyEntries)
  let coverageTracer = selectTracer Coverage opt.Architecture
  let args = Array.append [|coverageTracer; opt.TargetProg|] initArgs
  let pidCoverage = init_forkserver_coverage(args.Length, args, opt.ExecTimeout)
  if pidCoverage = -1 then
    failwith "Failed to initialize fork server for coverage tracer"
  let branchTracer = selectTracer Branch opt.Architecture
  let args = Array.append [|branchTracer; opt.TargetProg|] initArgs
  let pidBranch = init_forkserver_branch (args.Length, args, opt.ExecTimeout)
  if pidBranch = -1 then
    failwith "Failed to initialize fork server for branch tracer"

let abandonForkServer () =
  log "Abandon fork server"
  forkServerEnabled <- false
  set_env("CK_FORK_SERVER", "0")
  kill_forkserver ()

let cleanUpForkServer () =
  if forkServerEnabled then kill_forkserver ()

(*** File handling utilities ***)

let touchFile filename =
  // Explicitly delete file before creating one, to avoid hanging when the
  // specified file already exists as a FIFO.
  try System.IO.File.Delete(filename)
      System.IO.File.WriteAllBytes(filename, [|0uy|])
  with _ -> ()

let writeFile filename content =
  try System.IO.File.WriteAllBytes(filename, content) with
  | _ -> log "[Warning] Failed to write file '%s'" filename

let readAllLines filename =
  try List.ofSeq (System.IO.File.ReadLines filename) with
  | :? System.IO.FileNotFoundException -> []

let isProgPath targetProg argStr =
  try Path.GetFullPath argStr = targetProg with
  | _ -> false

let setupFiles targetProg (tc: TestCase) autoFuzzMode =
  let argFiles =
    if autoFuzzMode then
      // Create files with argument strings as name, to identify input sources.
      List.ofArray tc.Args
      |> List.filter (fun arg -> not (isProgPath targetProg arg))
      |> (fun args -> List.iter touchFile args; args)
    else []
  let inputFile = tc.FilePath
  if inputFile <> "" then
    writeFile inputFile tc.FileContent
    inputFile :: argFiles
  else argFiles

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

let private runTracer (tc: TestCase) tracerType opt =
  incrExecutionCount ()
  let targetProg = opt.TargetProg
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let tracer = selectTracer tracerType opt.Architecture
  let args = Array.append [|tracer; targetProg|] tc.Args
  let argc = args.Length
  exec(argc, args, tc.StdIn.Length, tc.StdIn, timeout, usePty)

let private runCoverageTracerForked (tc: TestCase) opt =
  incrExecutionCount ()
  let timeout = opt.ExecTimeout
  let signal = exec_fork_coverage(timeout, tc.StdIn.Length, tc.StdIn)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

let private runBranchTracerForked (tc: TestCase) opt =
  incrExecutionCount ()
  let timeout = opt.ExecTimeout
  let signal = exec_fork_branch(timeout, tc.StdIn.Length, tc.StdIn)
  if signal = Signal.ERROR then abandonForkServer ()
  signal

(*** Top-level tracer executor functions ***)

let getEdgeHash opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", string (int CoverageTracerMode.EdgeHash))
  let _ = if forkServerEnabled
          then runCoverageTracerForked tc opt
          else runTracer tc Coverage opt
  let edgeHash = parseExecHash coverageLog
  removeFiles files
  edgeHash

let getCoverage opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", string (int CoverageTracerMode.CountNewEdge))
  let exitSig = if forkServerEnabled
                then runCoverageTracerForked tc opt
                else runTracer tc Coverage opt
  let newEdgeCnt, pathHash, edgeHash = parseCoverage coverageLog
  removeFiles files
  (newEdgeCnt, pathHash, edgeHash, exitSig)

let getEdgeSet opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", string (int CoverageTracerMode.EdgeSet))
  let _ = if forkServerEnabled
          then runCoverageTracerForked tc opt
          else runTracer tc Coverage opt
  let edgeSet = readEdgeSet opt coverageLog
  removeFiles files
  edgeSet

let getBranchTrace opt seed tryVal =
  let tc = TestCase.fromSeed seed
  set_env("CK_FEED_ADDR", "0")
  set_env("CK_FEED_IDX", "0")
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = if forkServerEnabled
          then runBranchTracerForked tc opt
          else runTracer tc Branch opt
  let pathHash = parseExecHash pathHashLog
  let branchTrace = readBranchTrace opt branchTraceLog tryVal
  removeFiles (pathHashLog :: branchTraceLog :: files)
  pathHash, branchTrace

let getBranchInfoAt opt seed tryVal targPoint =
  let tc = TestCase.fromSeed seed
  set_env("CK_FEED_ADDR", sprintf "%016x" targPoint.Addr)
  set_env("CK_FEED_IDX", sprintf "%016x" targPoint.Idx)
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = if forkServerEnabled
          then runBranchTracerForked tc opt
          else runTracer tc Branch opt
  let pathHash = parseExecHash pathHashLog
  let branchInfoOpt =
    match readBranchTrace opt branchTraceLog tryVal with
    | [] -> None
    | [ branchInfo ] -> Some branchInfo
    | branchInfos -> None
  removeFiles (pathHashLog :: branchTraceLog :: files)
  (pathHash, branchInfoOpt)

let getSyscallTrace opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = runTracer tc Syscall opt
  let syscallTrace = readAllLines syscallTraceLog
  let inputSrcs = checkInputSource opt.TargetProg tc.Args syscallTrace
  removeFiles (syscallTraceLog :: files)
  inputSrcs

let nativeExecute opt tc =
  let targetProg = opt.TargetProg
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let args = Array.append [| targetProg |] tc.Args
  let argc = args.Length
  let signal = exec(argc, args, tc.StdIn.Length, tc.StdIn, timeout, usePty)
  removeFiles files
  signal
