namespace Eclipser

open System
open System.IO
open System.Collections.Generic
open Utils
open Options

type Syscall =
  | Open of (string * int)
  | Dup of (int * int)
  | Read of int

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Syscall =
  let rec checkFileReadAux (filename:string) fdMap syscalls =
    match syscalls with
    | [] -> false
    | Open (fname, fd) :: tail_syscalls ->
      let fdMap = Map.add fd fname fdMap
      checkFileReadAux filename fdMap tail_syscalls
    | Dup (old_fd, new_fd) :: tail_syscalls ->
      let fdMap =
        try Map.add new_fd (Map.find old_fd fdMap) fdMap with
        | :? KeyNotFoundException -> fdMap
      checkFileReadAux filename fdMap tail_syscalls
    | Read fd :: tail_syscalls ->
      (* Check if name of the file just read is equal to the 'filename' arg.
       * If they unmatch, continue with recursion. Recall that newline character
       * was escaped in 'fname', not to break syscall log format.
       *)
      let fname = try Map.find fd fdMap with | :? KeyNotFoundException -> ""
      let filenameEscaped = escapeWhiteSpace filename
      System.IO.Path.GetFileName fname = filenameEscaped
      && not (System.IO.Directory.Exists fname)
      || checkFileReadAux filename fdMap tail_syscalls

  let checkFileRead syscalls filename =
    let fdMap =
      Map.empty
      |> Map.add 0 ".STDIN"
      |> Map.add 1 ".STDOUT"
      |> Map.add 2 ".STDERR"
    checkFileReadAux filename fdMap syscalls

  let checkStdInputRead syscalls =
    checkFileRead syscalls ".STDIN"

  let findFileInput syscalls args =
    try List.findIndex (checkFileRead syscalls) (List.ofArray args) with
    | :? KeyNotFoundException -> -1

  let parseSyscallLog targProg (content:string) =
    try
      match List.ofSeq (content.Split ' ')  with
      | ["read"; fdStr] -> Some (Read (int fdStr))
      | ["dup"; oldFdStr; newFdStr] -> Some (Dup (int oldFdStr, int newFdStr))
      | ["open"; fdStr; filename] ->
        let filepath = try Path.GetFullPath filename with _ -> ""
        if filepath <> "" && filepath <> targProg then
          Some (Open (filename, int fdStr)) // Caution : not 'filepath' here.
        else None
      | _ -> (log "[Warning] Wrong syscall log file format : %s" content; None)
    with :? FormatException ->
      (log "[Warning] Wrong syscall log file format : %s" content; None)

  let checkInputSource targProg args syscallLog =
    let syscalls = List.choose (parseSyscallLog targProg) syscallLog
    let hasStdInput = checkStdInputRead syscalls
    let fileArgIdx = findFileInput syscalls args
    let inputSrcs =
      if hasStdInput then Set.singleton InputSrc.StdInput else Set.empty
    let inputSrcs =
      if fileArgIdx <> -1 then
        Set.add (InputSrc.FileInput fileArgIdx) inputSrcs
      else inputSrcs
    inputSrcs
