module Eclipser.Executor

open System
open System.IO
open System.Runtime.InteropServices
open Utils
open Options
open Syscall
open TestCase

/// Kinds of QEMU instrumentor. Each instrumentor serves different purposes.
type Tracer = Coverage | Branch | Syscall | BBCount

/// Execution mode that specifies whether we are executing program for test case
/// replaying. In replay mode, internal behavior of execution module changes
/// (see libexec.c file).
type ExecMode = Replay = 1 | NonReplay = 0

// Constants
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
let NormalMode = "0"    // Trace both node set & path hash
let HashOnlyMode = "1"  // Trace path hash only
let SetOnlyMode = "2"   // Trace node set only

// Mutable variables for statistics and state management

/// Number of total executions from the start of fuzzing.
let mutable totalExecutions = 0
/// Number of executions in current phase (i.e. repetition of grey-box concolic
/// testing or random fuzzing until the allocated resource is exhausted.)
let mutable phaseExecutions = 0
let mutable allowedExecutions = 0
let mutable accumFileLength = 0UL

let mutable forkServerEnabled = false
let mutable forkedPidCoverage = 0
let mutable forkedPidBranch = 0

let getTotalExecutions () = totalExecutions
let getPhaseExecutions () = phaseExecutions
let getAverageInputLen () = accumFileLength / uint64 totalExecutions
let allocateResource n = allowedExecutions <- n
let isResourceExhausted () = allowedExecutions <= 0
let resetPhaseExecutions () = phaseExecutions <- 0
let incrExecutionCount () =
  allowedExecutions <- allowedExecutions - 1
  totalExecutions <- totalExecutions + 1
  phaseExecutions <- phaseExecutions + 1

[<DllImport("libexec.dll")>] extern void initialize_exec (ExecMode is_replay)
[<DllImport("libexec.dll")>] extern int init_forkserver_coverage (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern int init_forkserver_branch (int argc, string[] argv, uint64 timeout)
[<DllImport("libexec.dll")>] extern void kill_forkserver ()
[<DllImport("libexec.dll")>] extern Signal exec (int argc, string[] argv, int stdin_size, byte[] stdin_data, uint64 timeout, bool use_pty)
[<DllImport("libexec.dll")>] extern Signal exec_fork_coverage (uint64 timeout, int stdin_size, byte[] stdin_data)
[<DllImport("libexec.dll")>] extern Signal exec_fork_branch (uint64 timeout, int stdin_size, byte[] stdin_data)
(* Caution : When you use path hash as fitness function of seed, you should set
  all the environment variables in advance (i.e. in System.initialize function),
  since number of environment variable affect the program execution path. *)

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
  else
    log "Forkserver for coverage tracer : %d" pidCoverage
    forkedPidCoverage <- pidCoverage
  let branchTracer = selectTracer Branch opt.Architecture
  let args = Array.append [|branchTracer; opt.TargetProg|] initArgs
  let pidBranch = init_forkserver_branch (args.Length, args, opt.ExecTimeout)
  if pidBranch = -1 then
    failwith "Failed to initialize fork server for branch tracer"
  else
    log "Forkserver for branch tracer : %d" pidBranch
    forkedPidBranch <- pidBranch

(* Just a wrapper for consistent naming convention *)
let terminateForkServer () =
  kill_forkserver()

let touchFile filename =
  // Explicitly delete file before creating one, to avoid hanging when the
  // specified file already exists as a FIFO.
  try System.IO.File.Delete(filename)
      System.IO.File.WriteAllBytes(filename, [|0uy|])
  with _ -> ()

let writeFile filename content =
  try System.IO.File.WriteAllBytes(filename, content) with
  | _ -> log "[Warning] Failed to write file '%s'" filename

let isProgPath targetProg argStr =
  try Path.GetFullPath argStr = targetProg with
  | _ -> false

let setupFiles targetProg (tc: TestCase) autoFuzzMode =
  let args = List.ofArray tc.Args
  let args' = List.filter (fun arg -> not (isProgPath targetProg arg)) args
  // Create files with argument strings as name, to identify file input sources.
  if autoFuzzMode then List.iter (fun arg -> touchFile arg) args
  let inputFile = tc.FilePath
  if inputFile <> "" then
    writeFile inputFile tc.FileContent
    accumFileLength <- accumFileLength + (uint64 tc.FileContent.Length)
    inputFile :: args'
  else args'

let clearFiles files =
  List.iter (fun file -> try System.IO.File.Delete(file) with err -> ()) files

let runTracer (tc:TestCase) tracerType opt =
  incrExecutionCount ()
  let targetProg = opt.TargetProg
  let timeout = opt.ExecTimeout
  let usePty = opt.UsePty
  let tracer = selectTracer tracerType opt.Architecture
  let args = Array.append [|tracer; targetProg|] tc.Args
  let argc = args.Length
  exec(argc, args, tc.StdIn.Length, tc.StdIn, timeout, usePty)

let abandonForkServer () =
  log "Abandon fork server"
  forkServerEnabled <- false
  set_env("CK_FORK_SERVER", "0")
  terminateForkServer ()

let dumpDebugTestcase tc =
  let filepath = sprintf "%s/debug/tc" sysInfo.["outputDir"]
  System.IO.File.WriteAllText(filepath, (TestCase.toJSON tc))

let runCoverageTracerForked (tc: TestCase) opt =
  incrExecutionCount ()
  let timeout = opt.ExecTimeout
  let signal = exec_fork_coverage(timeout, tc.StdIn.Length, tc.StdIn)
  if signal = Signal.ERROR then (abandonForkServer (); dumpDebugTestcase tc)
  signal

let runBranchTracerForked (tc: TestCase) opt =
  incrExecutionCount ()
  let timeout = opt.ExecTimeout
  let signal = exec_fork_branch(timeout, tc.StdIn.Length, tc.StdIn)
  if signal = Signal.ERROR then (abandonForkServer (); dumpDebugTestcase tc)
  signal

let readAllLines filename =
  try List.ofSeq (System.IO.File.ReadLines filename) with
  | :? System.IO.FileNotFoundException -> []

let parseCoverage filename =
  let content = readAllLines filename
  match content with
  | [newNodeCnt; pathHash; nodeHash] ->
    int newNodeCnt, uint64 pathHash, uint64 nodeHash
  | _ -> (0, 0UL, 0UL)

let parseBBCount filename =
  let content = readAllLines filename
  match List.rev content with
  | _ :: pathLine :: _ :: nodeLine :: _ ->
    let nodeTokens = nodeLine.Split('(', ')')
    let pathTokens = pathLine.Split('(', ')')
    int (nodeTokens.[1].[1 .. ]), int (pathTokens.[1].[1 .. ])
  | _ -> failwith "Invalid basic block count tracer result"

let parseExecHash filename =
  let content = readAllLines filename
  match content with
  | [hash] -> uint64 hash
  | _ -> 0UL

let is64Bit = function
  | X86 -> false
  | X64 -> true

let readNodeSet opt filename =
  try
    let f = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
    use r = new BinaryReader(f)
    let arch = opt.Architecture
    let mutable valid = true
    let nodeList =
      [ while valid do
          let addr = try if is64Bit arch
                         then r.ReadUInt64()
                         else uint64 (r.ReadUInt32())
                     with :? EndOfStreamException -> 0UL
          if addr = 0UL then valid <- false else yield addr ]
    Set.ofList nodeList
  with | :? FileNotFoundException -> Set.empty

let parseBranchTraceLog opt (r:BinaryReader) tryVal =
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

let readBranchTrace opt filename tryVal =
  try
    let f = File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
    use r = new BinaryReader(f)
    let mutable valid = true
    [while valid do
      match parseBranchTraceLog opt r tryVal with
      | None -> valid <- false
      | Some branchInfo -> yield branchInfo ]
  with | :? FileNotFoundException -> []

let printNewNodes () =
  let dbgMsg = readAllLines (sysInfo.["dbgLog"])
  if not (List.isEmpty dbgMsg) then
    log "New nodes : [%s]" (String.concat "; " dbgMsg)

let getExecHash opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", HashOnlyMode)
  let _ = if forkServerEnabled
          then runCoverageTracerForked tc opt
          else runTracer tc Coverage opt
  let nodeHash = parseExecHash sysInfo.["hashLog"]
  clearFiles (sysInfo.["hashLog"] :: files)
  nodeHash

let getCoverage opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", NormalMode)
  let exitSig = if forkServerEnabled
                then runCoverageTracerForked tc opt
                else runTracer tc Coverage opt
  let newNodeCnt, pathHash, nodeHash = parseCoverage sysInfo.["coverageLog"]
  if opt.Verbosity >= 2 then printNewNodes()
  clearFiles (sysInfo.["hashLog"] :: sysInfo.["coverageLog"] :: files)
  (newNodeCnt, pathHash, nodeHash, exitSig)

let getNodeSet opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  set_env("CK_MODE", SetOnlyMode)
  let _ = if forkServerEnabled
          then runCoverageTracerForked tc opt
          else runTracer tc Coverage opt
  let nodeSet = readNodeSet opt sysInfo.["coverageLog"]
  clearFiles (sysInfo.["coverageLog"] :: files)
  nodeSet

let getBranchTrace opt seed tryVal =
  let tc = TestCase.fromSeed seed
  set_env ("CK_FEED_ADDR", "0")
  set_env ("CK_FEED_IDX", "0")
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = if forkServerEnabled
          then runBranchTracerForked tc opt
          else runTracer tc Branch opt
  let pathHash = parseExecHash sysInfo.["hashLog"]
  let branchTrace = readBranchTrace opt sysInfo.["branchLog"] tryVal
  clearFiles (sysInfo.["hashLog"] :: sysInfo.["branchLog"] :: files)
  pathHash, branchTrace

let getBranchInfoAt opt seed tryVal targPoint =
  let tc = TestCase.fromSeed seed
  set_env ("CK_FEED_ADDR", sprintf "%016x" targPoint.Addr)
  set_env ("CK_FEED_IDX", sprintf "%016x" targPoint.Idx)
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = if forkServerEnabled
          then runBranchTracerForked tc opt
          else runTracer tc Branch opt
  let pathHash = parseExecHash sysInfo.["hashLog"]
  let branchInfoOpt =
    match readBranchTrace opt sysInfo.["branchLog"] tryVal with
    | [] -> None
    | [ branchInfo ] -> Some branchInfo
    | branchInfos ->
      let brStrs = String.concat "," (List.map BranchInfo.toString branchInfos)
      None
  clearFiles (sysInfo.["hashLog"] :: sysInfo.["branchLog"] :: files)
  (pathHash, branchInfoOpt)

let getSyscallTrace opt seed =
  let tc = TestCase.fromSeed seed
  let autoFuzzMode = opt.FuzzMode = AutoFuzz
  let files = setupFiles opt.TargetProg tc autoFuzzMode
  let _ = runTracer tc Syscall opt
  (* Retrieve syscall tracing info *)
  let syscallLog = readAllLines sysInfo.["syscallLog"]
  let inputSrcs = checkInputSource opt.TargetProg tc.Args syscallLog
  clearFiles (sysInfo.["syscallLog"] :: files)
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
  clearFiles files
  signal
