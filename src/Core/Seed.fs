namespace Eclipser

open System
open Config
open Utils
open BytesUtils

type InputSrc =
  | StdInput
  | FileInput of int (* argv[] index *)

/// Represents the file path, which can be either an index of command-line
/// argument, or a constant string.
type Filepath = ArgIdx of int | ConstPath of string | Unknown

/// FileInput is an 'Input' coupled with its file path.
type FileInput = {
  Path : Filepath
  Content : InputSeq
}

/// Seed represents an input to a program, along with various information (e.g.
/// approximate path conditions, cursor, etc.) needed for test case generation.
type Seed = {
  /// Command line arguments represented with a sequence of input.
  Args : InputSeq
  /// Standard input. Currently, we do not consider a sequence of inputs.
  StdIn : InputSeq
  /// File input. Currently, we do not consider multiple file inputs.
  File : FileInput
  /// Specifies input source that will be used for the next grey-box concolic
  /// testing.
  SourceCursor : InputKind
}

module Seed =

  /// Initialize a seed with specified maximum lengths.
  let make inputSrc argMaxLens fileMaxLen stdInMaxLen =
    { Args = InputSeq.make Args argMaxLens
      StdIn = InputSeq.make StdIn [stdInMaxLen]
      File = { Path = Unknown; Content = InputSeq.make File [fileMaxLen] }
      SourceCursor = inputSrc }

  /// Initialize a seed with the specified content bytes, input source and
  /// maximum length.
  let makeWith inputSrc maxLen (initBytes: byte[])  =
    let initInput = Input.makeWith initBytes maxLen
    let initInputSeq =
      { Inputs = [| initInput |]; InputCursor = 0; CursorDir = Right }
    let argInput = if inputSrc = Args then initInputSeq else InputSeq.empty
    let fileContent = if inputSrc = File then initInputSeq else InputSeq.empty
    let fileInput = { Path = Unknown; Content = fileContent }
    let stdIn = if inputSrc = StdIn then initInputSeq else InputSeq.empty
    { Args = argInput
      StdIn = stdIn
      File = fileInput
      SourceCursor = inputSrc }

  /// Concretize input file path into a string.
  let concretizeFilepath seed =
    let argStrs = InputSeq.concretizeArg seed.Args
    match seed.File.Path with
    | ArgIdx i -> argStrs.[i]
    | ConstPath path -> path
    | Unknown -> ""

  (**************************** Getter functions ****************************)

  /// Get the current input sequence pointed by the cursor.
  let getCurInputSeq seed =
    match seed.SourceCursor with
    | Args -> seed.Args
    | StdIn -> seed.StdIn
    | File -> seed.File.Content

  /// Get the current input pointed by the cursor.
  let getCurInput seed =
    let curInputSeq = getCurInputSeq seed
    InputSeq.getCurInput curInputSeq

  let getByteCursorDir seed =
    let curInput = getCurInput seed
    curInput.CursorDir

  /// Get the length of current input pointed by the cursor.
  let getCurInputLen seed =
    let curInput = getCurInput seed
    curInput.ByteVals.Length

  /// Get the current ByteVal pointed by the cursor.
  let getCurByteVal seed =
    let curInput = getCurInput seed
    Input.getCurByteVal curInput

  /// Get the concrete value of the ByteVal at the specified offset of the
  /// current input.
  let getConcreteByteAt seed pos =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    ByteVal.getConcreteByte curByteVals.[pos]

  /// Get the concrete values of ByteVals starting from the specified offset of
  /// the current input.
  let getConcreteBytesFrom seed pos len =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    Array.map ByteVal.getConcreteByte curByteVals.[pos .. (pos + len - 1)]

  (**************************** Query functions ****************************)

  /// Check if the byte at the given offset of current input is unfixed.
  let isUnfixedByteAt seed offset =
    let curInput = getCurInput seed
    ByteVal.isUnfixed curInput.ByteVals.[offset]

  /// Find the remaining length toward the given direction, starting from the
  /// current byte position.
  let queryLenToward seed direction =
    let curInput = getCurInput seed
    let bytePos = curInput.ByteCursor
    match direction with
    | Stay -> failwith "queryLenToward() cannot be called with 'Stay'"
    | Right -> curInput.ByteVals.Length - bytePos
    | Left -> bytePos + 1

  // Auxiliary function for queryUpdateBound()
  let private queryUpdateBoundLeft (byteVals: ByteVal []) byteCursor =
    let byteVals' =
      if byteCursor - MaxChunkLen >= 0
      then byteVals.[byteCursor - MaxChunkLen .. byteCursor]
      else byteVals.[ .. byteCursor]
    // We use an heuristic to bound update until the adjacent *fixed* ByteVal.
    match Array.tryFindIndexBack ByteVal.isFixed byteVals' with
    | None -> byteVals'.Length
    | Some idx -> byteVals'.Length - idx - 1

  // Auxiliary function for queryUpdateBound()
  let private queryUpdateBoundRight (byteVals: ByteVal []) byteCursor maxLen =
    let byteVals' =
      if byteCursor + MaxChunkLen < byteVals.Length
      then byteVals.[byteCursor .. byteCursor + MaxChunkLen]
      else byteVals.[byteCursor .. ]
    // We use an heuristic to bound update until the adjacent *fixed* ByteVal.
    match Array.tryFindIndex ByteVal.isFixed byteVals' with
    | None -> min MaxChunkLen (maxLen - byteCursor)
    | Some idx -> idx

  /// Find the maximum length that can be updated for grey-box concolic testing.
  let queryUpdateBound seed direction =
    let curInput = getCurInput seed
    let byteVals = curInput.ByteVals
    let byteCursor = curInput.ByteCursor
    match direction with
    | Stay -> failwith "queryUpdateBound() cannot be called with 'Stay'"
    | Left -> queryUpdateBoundLeft byteVals byteCursor
    | Right -> queryUpdateBoundRight byteVals byteCursor curInput.MaxLen

  /// Get adjacent concrete byte values, toward the given direction.
  let queryNeighborBytes seed direction =
    let curInput = getCurInput seed
    let byteVals = curInput.ByteVals
    let byteCursor = curInput.ByteCursor
    match direction with
    | Stay -> failwith "queryNeighborBytes() cannot be called with 'Stay'"
    | Right ->
      let upperBound = min (byteVals.Length - 1) (byteCursor + MaxChunkLen)
      Array.map ByteVal.getConcreteByte byteVals.[byteCursor + 1 .. upperBound]
    | Left ->
      let lowerBound = max 0 (byteCursor - MaxChunkLen)
      Array.map ByteVal.getConcreteByte byteVals.[lowerBound .. byteCursor - 1]

  (************************ Content update functions ************************)

  /// Update (replace) the current input sequence pointed by cursor.
  let setCurInputSeq seed inputSeq =
    match seed.SourceCursor with
    | Args -> { seed with Args = inputSeq }
    | File -> { seed with File = { seed.File with Content = inputSeq } }
    | StdIn -> { seed with StdIn = inputSeq }

  /// Update (replace) the current input pointed by cursor.
  let setCurInput seed input =
    let curInputSeq = getCurInputSeq seed
    let newInputSeq = InputSeq.setCurInput curInputSeq input
    setCurInputSeq seed newInputSeq

  /// Update argument input sequence with given command-line argument string.
  /// Note that maximum length of each input is set with the length of each
  /// argument string.
  let setArgs seed (cmdLine: string) =
    let delim = [| ' '; '\t'; '\n' |]
    let initArgs = cmdLine.Split(delim, StringSplitOptions.RemoveEmptyEntries)
    let argInputs =
      Array.map strToBytes initArgs
      |> Array.filter (fun arg -> Array.length arg <> 0)
      |> Array.map (fun bytes -> Input.makeWith bytes (Array.length bytes))
    let argInputSeq = { Inputs = argInputs; InputCursor = 0; CursorDir = Right }
    {seed with Seed.Args = argInputSeq }

  /// Update input file path with the given string.
  let setFilepath seed filepath =
    { seed with File = { seed.File with Path = ConstPath filepath } }

  /// Impose a constraint with lower and upper bounds, on the ByteVal at the
  /// given offset.
  let constrainByteAt seed direction offset low upper =
    let curInput = getCurInput seed
    let byteCursor =
      match direction with
      | Stay -> failwith "constrainByteAt() cannot be called with 'Stay'"
      | Right -> curInput.ByteCursor + offset
      | Left -> curInput.ByteCursor - offset
    let newByteVals = Array.copy curInput.ByteVals
    let newByteVal = if low <> upper then Interval (low, upper) else Fixed low
    newByteVals.[byteCursor] <- newByteVal
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Fix the current ByteVals pointed by the cursor, with the provided bytes.
  let fixCurBytes seed dir bytes =
    let nBytes = Array.length bytes
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let byteCursor = curInput.ByteCursor
    let startPos = if dir = Right then byteCursor else byteCursor - nBytes + 1
    let newByteVals =
      // Note that 'MaxLen' is already checked in queryUpdateBound().
      if startPos + nBytes > curByteVals.Length then
        let reqSize = startPos + nBytes - curByteVals.Length
        Array.append curByteVals (Array.init reqSize (fun _ -> Undecided 0uy))
      else Array.copy curByteVals
    Array.iteri (fun i b -> newByteVals.[startPos + i] <- Fixed b) bytes
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Update the current ByteVal pointed by the cursor.
  let updateCurByte seed byteVal =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let byteCursor = curInput.ByteCursor
    let newByteVals = Array.copy curByteVals
    newByteVals.[byteCursor] <- byteVal
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Update the ByteVal at given offset of current input.
  let updateByteValAt seed pos byteVal =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let newByteVals = Array.copy curByteVals
    newByteVals.[pos] <- byteVal
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Update the bytes of current input, starting from the given offset.
  /// Approximate path conditions of the updated ByteVals are abandoned.
  let updateBytesFrom seed pos bytes =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let newByteVals = Array.copy curByteVals
    Array.iteri (fun i b -> newByteVals.[pos + i] <- Undecided b) bytes
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Flip the bit at the given byte/bit offset of current input.
  let flipBitAt seed bytePos bitPos =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let newByteVals = Array.copy curByteVals
    let curByteVal = curByteVals.[bytePos]
    let curByte = ByteVal.getConcreteByte curByteVal
    let newByte = curByte ^^^ ((byte 1) <<< bitPos)
    newByteVals.[bytePos] <- Undecided newByte
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Insert bytes at the given offset of the current input.
  let insertBytesInto seed pos bytes =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let curBytesLen = Array.length curByteVals
    let headByteVals = curByteVals.[0 .. (pos - 1)]
    let tailByteVals = curByteVals.[pos .. (curBytesLen - 1)]
    let byteVals = Array.map (fun b -> Undecided b) bytes
    let newByteVals = Array.concat [headByteVals; byteVals; tailByteVals]
    let newByteVals = if Array.length newByteVals > curInput.MaxLen
                      then newByteVals.[.. (curInput.MaxLen - 1)]
                      else newByteVals
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  /// Remove bytes starting from the given offset of the current input.
  let removeBytesFrom seed pos n =
    let curInput = getCurInput seed
    let curByteVals = curInput.ByteVals
    let curBytesLen = Array.length curByteVals
    let headByteVals = curByteVals.[0 .. (pos - 1)]
    let tailByteVals = curByteVals.[(pos + n) .. (curBytesLen - 1)]
    let newByteVals = Array.append headByteVals tailByteVals
    let newInput = { curInput with ByteVals = newByteVals }
    setCurInput seed newInput

  (************************* Cursor update functions *************************)

  /// Update the byte cursor direction of current input.
  let setByteCursorDir seed dir =
    let curInput = getCurInput seed
    let newInput = Input.setCursorDir curInput dir
    setCurInput seed newInput

  /// Update the input cursor direction of current input sequence.
  let setInputCursorDir seed dir =
    let curInputSeq = getCurInputSeq seed
    let newInputSeq = InputSeq.setCursorDir curInputSeq dir
    setCurInputSeq  seed newInputSeq

  /// Update the input source cursor of the given seed.
  let setSourceCursor seed newInputSrc =
    { seed with SourceCursor = newInputSrc }

  /// Randomly move byte cursor position within current input.
  let shuffleByteCursor seed =
    let curInput = getCurInput seed
    let curInputLen = curInput.ByteVals.Length
    let newByteCursor = random.Next(curInputLen)
    let newCursorDir = if newByteCursor > (curInputLen / 2) then Left else Right
    let newInput = Input.setCursor curInput newByteCursor
    let newInput = Input.setCursorDir newInput newCursorDir
    setCurInput seed newInput

  /// Step the byte cursor within the current input, following the cursor
  /// direction.
  let stepByteCursor seed =
    let curInput = getCurInput seed
    match Input.stepCursor curInput with
    | None -> None
    | Some newInput -> Some (setCurInput seed newInput)

  /// Move the byte cursor within current input, to the next unfixed ByteVal.
  let moveToUnfixedByte seed =
    let curInput = getCurInput seed
    match Input.moveToUnfixedByte curInput with
    | None -> None
    | Some newInput -> Some (setCurInput seed newInput)

  /// Proceed the byte cursor within the current input, to the next unfixed
  /// ByteVal, following the current cursor direction.
  let proceedByteCursor seed =
    let curInput = getCurInput seed
    match Input.proceedCursor curInput with
    | None -> None
    | Some newInput -> Some (setCurInput seed newInput)

  /// Randomly move input cursor within current input sequence pointed by the
  /// source cursor.
  let shuffleInputCursor seed =
    let curInputSeq = getCurInputSeq seed
    if InputSeq.isSingular curInputSeq then seed
    else let curInputSeqLen = curInputSeq.Inputs.Length
         let newInputCursor = random.Next(curInputSeqLen)
         let newInputSeq = InputSeq.setCursor curInputSeq newInputCursor
         setCurInputSeq seed newInputSeq

  /// Step the input cursor, following the cursor direction.
  let stepInputCursor seed =
    let curInputSeq = getCurInputSeq seed
    match InputSeq.stepCursor curInputSeq with
    | None -> None
    | Some newInputSeq -> Some (setCurInputSeq seed newInputSeq)

  /// Within the current input sequence, find an input that has any unfixed
  /// ByteVal, and return a new input sequence with updated input/byte cursors.
  /// The cursor may stay at the same position.
  let moveToUnfixedInput seed =
    let curInputSeq = getCurInputSeq seed
    match InputSeq.moveToUnfixedInput curInputSeq with
    | None -> None
    | Some newInputSeq -> Some (setCurInputSeq seed newInputSeq)

  /// Proceed input cursor to the next unfixed input, following the current
  /// cursor direction.
  let proceedInputCursor seed =
    let curInputSeq = getCurInputSeq seed
    match InputSeq.proceedCursor curInputSeq with
    | None -> None
    | Some newInputSeq -> Some (setCurInputSeq seed newInputSeq)

  /// Proceed input cursor to the next unfixed input, and byte cursor toe the
  /// next unfixed ByteVal, following the current cursor directions.
  let proceedCursors seed =
    List.choose identity [ proceedByteCursor seed; proceedInputCursor seed ]

  /// Return seeds with byte cursor updated to point unfixed ByteVal. Probing
  /// should occur in both left side and right side.
  let moveByteCursor isFromConcolic seed =
    let curInput = getCurInput seed
    let curByteVal = Input.getCurByteVal curInput
    let leftwardSeed = setByteCursorDir seed Left
    let leftwardSeeds = // Avoid sampling at the same offset.
      if isFromConcolic && ByteVal.isSampledByte curByteVal then
        match stepByteCursor leftwardSeed with
        | None -> [] | Some s -> [ s ]
      else [ leftwardSeed ]
    let rightwardSeed = setByteCursorDir seed Right
    let rightwardSeeds =
      match stepByteCursor rightwardSeed with
      | None -> [] | Some s -> [ s ]
    List.choose moveToUnfixedByte (leftwardSeeds @ rightwardSeeds)
    |> List.map (fun seed -> setInputCursorDir seed Stay)

  /// Return seeds with field cursor updated to point unfixed field. Probing
  /// should occur in both left side and right side.
  let moveInputCursor seed =
    List.map (setInputCursorDir seed) [Left; Right]
    |> List.choose stepInputCursor
    |> List.choose moveToUnfixedInput

  // Auxiliary function for moveSourceCursor().
  let private moveSourceCursorAux seed inputSrc =
    match inputSrc with
    | InputSrc.StdInput when seed.SourceCursor <> StdIn &&
                             not (InputSeq.isEmpty seed.StdIn) ->
      moveToUnfixedInput (setSourceCursor seed StdIn)
    | InputSrc.FileInput argIdx when seed.SourceCursor <> File &&
                                     not (InputSeq.isEmpty seed.File.Content) ->
      let newFile = { seed.File with Path = ArgIdx argIdx }
      let newSeed = { seed with File = newFile }
      moveToUnfixedInput (setSourceCursor newSeed File)
    | _ -> None // If inputSrc is equal to current input source, return nothing

  /// Return seeds with source cursor updated to point new input sources, using
  /// the provided syscall trace information.
  let moveSourceCursor seed inputSrcs =
    List.choose (moveSourceCursorAux seed) (Set.toList inputSrcs)

  /// Return seeds with moved byte cursors, input cursors, and source cursors.
  /// This is equivalent to applying moveByteCursor(), moveInputCursor(), and
  /// moveSourceCursor() at once.
  let moveCursors seed isFromConcolic inputSrcs =
    let byteCursorMoved = moveByteCursor isFromConcolic seed
    let inputCursorMoved = moveInputCursor seed
    let sourceCursorMoved = moveSourceCursor seed inputSrcs
    byteCursorMoved @ inputCursorMoved @ sourceCursorMoved

  (*************************** Stringfy functions ***************************)

  let private argToStr args =
    let argInputToStr argInput = sprintf "\"%s\"" (Input.concretizeArg argInput)
    let argStrs = Array.map argInputToStr args.Inputs
    String.concat " " argStrs

  let toString seed =
    let argStr = argToStr seed.Args
    let curInput = getCurInput seed
    let inputSrc = seed.SourceCursor
    let inputCursor = if inputSrc = Args then seed.Args.InputCursor else 0
    let inputStr = Input.toString curInput
    sprintf "(%s) %A[%d]=(%s)" argStr inputSrc inputCursor inputStr
