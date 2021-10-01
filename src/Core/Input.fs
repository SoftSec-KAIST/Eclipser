namespace Eclipser

open System
open Config
open Utils
open BytesUtils

/// An input that consists of an array of ByteVals.
type Input = {
  /// An array of ByteVal elements.
  ByteVals : ByteVal array
  /// Maximum length allowed for this input.
  MaxLen : int
  /// Specifies the offset within an input (i.e. the index of 'ByteVals'), which
  /// will be used for the next grey-box concolic testing.
  ByteCursor : int
  /// The direction in which ByteCursor should move.
  CursorDir : Direction
}

module Input =

  /// An empty input.
  let empty = { ByteVals = [| |]; MaxLen = 0; ByteCursor = 0; CursorDir = Stay }

  /// Initialize an input for the specified input source, with the specified
  /// maximum length.
  let make inputSrc maxLen =
    let initLen = min maxLen InitInputLen
    let initByte = match inputSrc with
                   | Args | StdIn -> 65uy // Character 'A'.
                   | File -> 0uy // NULL byte.
    let bytes = Array.init initLen (fun _ -> initByte)
    let byteVals = Array.map ByteVal.newByteVal bytes
    { ByteVals = byteVals; MaxLen = maxLen; ByteCursor = 0; CursorDir = Right }

  /// Initialize an input with provided byte array content.
  let makeWith maxLen bytes =
    // Do not allow empty content.
    if Array.length bytes = 0 then failwith "Input.makeWith() with empty bytes"
    let byteVals = Array.map ByteVal.newByteVal bytes
    { ByteVals = byteVals; MaxLen = maxLen; ByteCursor = 0; CursorDir = Right }

  /// Concretize an input into a byte array.
  let concretize input =
    Array.map ByteVal.getConcreteByte input.ByteVals

  /// Concretize an input into a byte array, until the first NULL byte is met.
  let concretizeArg input =
    let bytes = concretize input
    let idxOpt = Array.tryFindIndex (fun b -> b = 0uy) bytes
    match idxOpt with
    | None -> bytesToStr bytes
    | Some idx -> bytesToStr bytes.[0 .. (idx - 1)]

  /// Get current byte value pointed by input's cursor.
  let getCurByteVal input = input.ByteVals.[input.ByteCursor]

  /// Check if the given input has any unfixed ByteVal.
  let hasUnfixedByte input = Array.exists ByteVal.isUnfixed input.ByteVals

  /// Return the index of the first unfixed ByteVal. Raises an exception if
  /// unfixed ByteVal do not exists, so hasUnfixedByte() should precede.
  let getUnfixedByteIndex input =
    Array.findIndex ByteVal.isUnfixed input.ByteVals

  /// Set the byte cursor position of the given input.
  let setCursor input newPos = { input with ByteCursor = newPos }

  /// Set the byte cursor direction of the given input
  let setCursorDir input dir = { input with Input.CursorDir = dir }

  /// Step the byte cursor, following the cursor direction.
  let stepCursor input =
    let byteCursor = input.ByteCursor
    match input.CursorDir with
    | Stay -> None
    | Left when 0 <= byteCursor - 1 ->
      Some (setCursor input (byteCursor - 1))
    | Right when byteCursor + 1 < input.ByteVals.Length ->
      Some (setCursor input (byteCursor + 1))
    | Left _ | Right _ -> None

  // Starting from 'curIdx', find the index of the first unfixed ByteVal.
  let rec private findUnfixedByte (bytes: ByteVal []) curIdx =
    if curIdx < 0 || curIdx >= bytes.Length then -1
    elif ByteVal.isUnfixed bytes.[curIdx] then curIdx
    else findUnfixedByte bytes (curIdx + 1)

  // Starting from 'curIdx', find the index of the first unfixed ByteVal, in a
  // backward direction.
  let rec private findUnfixedByteBackward (bytes: ByteVal []) curIdx =
    if curIdx < 0 || curIdx >= bytes.Length then -1
    elif ByteVal.isUnfixed bytes.[curIdx] then curIdx
    else findUnfixedByteBackward bytes (curIdx - 1)

  /// Move the byte cursor to an unfixed ByteVal. Cursor may stay at the same
  /// position.
  let moveToUnfixedByte input =
    let byteCursor = input.ByteCursor
    let cursorDir = input.CursorDir
    match cursorDir with
    | Stay -> None
    | Left -> let offset = findUnfixedByteBackward input.ByteVals byteCursor
              if offset <> -1 then Some (setCursor input offset) else None
    | Right -> let offset = findUnfixedByte input.ByteVals byteCursor
               if offset <> -1 then Some (setCursor input offset) else None

  /// Move the byte cursor to the next unfixed ByteVal. Cursor should move at
  /// least one offset toward the current cursor direction.
  let proceedCursor input =
    match stepCursor input with
    | None -> None
    | Some newInput -> moveToUnfixedByte newInput

  // Auxiliary function for byteValsToStr() that handles 'Untouched' ByteVals.
  let private untouchedToStr untouchedList =
    if List.isEmpty untouchedList then ""
    elif List.length untouchedList < 4 then
      " " + String.concat " " (List.map ByteVal.toString untouchedList)
    else sprintf " ...%dbytes..." (List.length untouchedList)

  // Stringfy ByteVal list.
  let rec private byteValsToStr accumUntouched accumStrs byteVals =
    match byteVals with
    | [] -> accumStrs + untouchedToStr (List.rev accumUntouched)
    | headByteVal :: tailByteVals ->
      (match headByteVal with
      | Untouched _ -> // Just accumulate to 'accumUntouched' and continue
        byteValsToStr (headByteVal :: accumUntouched) accumStrs tailByteVals
      | Undecided _ | Fixed _ | Interval _ | Sampled _ ->
        let untouchedStr = untouchedToStr (List.rev accumUntouched)
        let byteValStr = ByteVal.toString headByteVal
        let accumStrs = accumStrs + untouchedStr + " " + byteValStr
        byteValsToStr [] accumStrs tailByteVals) // reset accumUntouched to []

  /// Stringfy an input.
  let toString input =
    let byteVals = List.ofArray input.ByteVals
    let byteStr = byteValsToStr [] "" byteVals
    sprintf "%s (%d) (%A)" byteStr input.ByteCursor input.CursorDir


/// A sequence of inputs. For example, a command-line argument is represented
/// with an InputSeq, where each input corresponds to each string element of
/// argv[] array.
type InputSeq = {
  /// An array of Inputs.
  Inputs : Input array
  /// Specifies the offset within a sequence (i.e. the index of 'Inputs'), which
  /// will be used for the next grey-box concolic testing.
  InputCursor : int
  /// The direction in which CursorPos should move.
  CursorDir : Direction
}

module InputSeq =
  /// An empty input sequence.
  let empty = { Inputs = [| |]; InputCursor = 0; CursorDir = Stay }

  /// Initialize an input sequence for the specified input source, with the
  /// specified maximum lengths.
  let make inputSrc maxLens =
    let inputs = Array.ofList maxLens
                 |> Array.filter (fun len -> len <> 0)
                 |> Array.map (Input.make inputSrc)
    { Inputs = inputs; InputCursor = 0; CursorDir = Right }

  /// Concretize an input sequence into an array of 'byte array'.
  let concretize inputSeq =
    Array.map Input.concretize inputSeq.Inputs

  /// Concretize an argument input sequence into a string array.
  let concretizeArg inputSeq =
    Array.map Input.concretizeArg inputSeq.Inputs
    |> Array.filter (fun str -> str <> "") // Filter out empty string args

  let isEmpty inputSeq =
    inputSeq.Inputs.Length = 0

  let isSingular inputSeq =
    inputSeq.Inputs.Length = 1

  /// Get the current input pointed by cursor.
  let getCurInput inputSeq = inputSeq.Inputs.[inputSeq.InputCursor]

  /// Update (replace) the current input pointed by cursor.
  let setCurInput inputSeq newInput =
    let newInputs = Array.copy inputSeq.Inputs
    newInputs.[inputSeq.InputCursor] <- newInput
    { inputSeq with Inputs = newInputs }

  /// Set the input cursor position of the given input sequence.
  let setCursor inputSeq newPos =
    { inputSeq with InputCursor = newPos}

  /// Set the input cursor direction of the given input sequence.
  let setCursorDir inputSeq dir =
    { inputSeq with InputSeq.CursorDir = dir }

  /// Step the input cursor toward currently pointing cursor direction.
  let stepCursor inputSeq =
    let cursorPos = inputSeq.InputCursor
    let cursorDir = inputSeq.CursorDir
    match cursorDir with
    | Left when 0 <= cursorPos - 1 ->
      Some (setCursor inputSeq (cursorPos - 1))
    | Right when cursorPos + 1 < inputSeq.Inputs.Length ->
      Some (setCursor inputSeq (cursorPos + 1))
    | Stay | Left _ | Right _ -> None

  // Find an input that has any unfixed byte, starting from the given index.
  // If such input is found, return a new input with updated byte cursor, along
  // with the index of the found input.
  let rec private findUnfixedInput (inputs: Input []) curIdx =
    if curIdx < 0 || curIdx >= inputs.Length then None else
      let curInput = inputs.[curIdx]
      if Input.hasUnfixedByte curInput then
        let unfixedOffset = Input.getUnfixedByteIndex curInput
        let newInput = Input.setCursor curInput unfixedOffset
                       |> Input.setCursorDir <| Right
        Some (newInput, curIdx)
      else findUnfixedInput inputs (curIdx + 1)

  // Find an input that has any unfixed byte, starting from the given index, in
  // a backward direction. If such input is found, return a new input with
  // updated byte cursor, along with the index of the found input.
  let rec private findUnfixedInputBackward (inputs: Input []) curIdx =
    if curIdx < 0 || curIdx >= inputs.Length then None else
      let curInput = inputs.[curIdx]
      if Input.hasUnfixedByte curInput then
        let unfixedOffset = Input.getUnfixedByteIndex curInput
        let newInput = Input.setCursor curInput unfixedOffset
                       |> Input.setCursorDir <| Right
        Some (newInput, curIdx)
      else findUnfixedInputBackward inputs (curIdx - 1)

  /// From a given input sequence, find an input that has any unfixed ByteVal,
  /// and return a new input sequence with updated input/byte cursors. The
  /// cursor may stay at the same position.
  let moveToUnfixedInput inputSeq =
    let inputCursor = inputSeq.InputCursor
    let newInputOpt =
      match inputSeq.CursorDir with
      | Stay -> None
      | Left -> findUnfixedInputBackward inputSeq.Inputs inputCursor
      | Right -> findUnfixedInput inputSeq.Inputs inputCursor
    match newInputOpt with
    | None -> None
    | Some (newInput, inputIdx) ->
      let newInputSeq = setCurInput (setCursor inputSeq inputIdx) newInput
      Some newInputSeq

  /// Proceed the byte cursor to the next input that has any unfixed ByteVal.
  /// Cursor should move at least one offset toward the current cursor
  /// direction.
  let proceedCursor inputSeq =
    match stepCursor inputSeq with
    | None -> None
    | Some newInputSeq -> moveToUnfixedInput newInputSeq
