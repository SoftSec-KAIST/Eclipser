namespace Eclipser

open Config
open Utils

/// An input that consists of an array of ByteVals.
type Seed = {
  /// An array of ByteVal elements.
  ByteVals : ByteVal array
  /// Indes of the byte to be used for the next grey-box concolic testing.
  CursorPos : int
  /// The direction in which ByteCursor should move.
  CursorDir : Direction
  /// Input source.
  Source : InputSource
}

module Seed =

  let dummy = {
    ByteVals = [| |]
    CursorPos = 0
    CursorDir = Right
    Source = StdInput
  }

  /// Initialize a seed for the specified input source, with the specified
  /// maximum length.
  let make src =
    let initByte = match src with
                   | StdInput -> 65uy // Character 'A'.
                   | FileInput _ -> 0uy // NULL byte.
    let bytes = Array.init INIT_INPUT_LEN (fun _ -> initByte)
    let byteVals = Array.map ByteVal.newByteVal bytes
    { ByteVals = byteVals; CursorPos = 0; CursorDir = Right; Source = src }

  /// Initialize a seed with provided byte array content.
  let makeWith src bytes =
    // Do not allow empty content.
    if Array.length bytes = 0 then failwith "Seed.makeWith() with empty bytes"
    let byteVals = Array.map ByteVal.newByteVal bytes
    { ByteVals = byteVals; CursorPos = 0; CursorDir = Right; Source = src }

  /// Concretize a seed into a byte array.
  let concretize seed =
    Array.map ByteVal.getConcreteByte seed.ByteVals

  (**************************** Getter functions ****************************)

  /// Get the current ByteVal pointed by the cursor.
  let getCurByteVal seed = seed.ByteVals.[seed.CursorPos]

  /// Get the length of byte values.
  let getCurLength seed = seed.ByteVals.Length

  /// Return the index of the first unfixed ByteVal. Raises an exception if
  /// unfixed ByteVal do not exists, so hasUnfixedByte() should precede.
  let getUnfixedByteIndex seed =
    Array.findIndex ByteVal.isUnfixed seed.ByteVals

  /// Get the direction of the cursor.
  let getByteCursorDir seed =
    seed.CursorDir

  /// Get the concrete value of the ByteVal at the specified offset of the
  /// current seed.
  let getConcreteByteAt seed pos =
    ByteVal.getConcreteByte seed.ByteVals.[pos]

  /// Get the concrete values of ByteVals starting from the specified offset of
  /// the current seed.
  let getConcreteBytesFrom seed pos len =
    Array.map ByteVal.getConcreteByte seed.ByteVals.[pos .. (pos + len - 1)]

  (**************************** Query functions ****************************)

  /// Check if the given seed has any unfixed ByteVal.
  let hasUnfixedByte seed = Array.exists ByteVal.isUnfixed seed.ByteVals

  /// Check if the byte at the given offset of current seed is unfixed.
  let isUnfixedByteAt seed offset =
    ByteVal.isUnfixed seed.ByteVals.[offset]

  /// Find the remaining length toward the given direction, starting from the
  /// current byte position.
  let queryLenToward seed direction =
    match direction with
    | Stay -> failwith "queryLenToward() cannot be called with 'Stay'"
    | Right -> seed.ByteVals.Length - seed.CursorPos
    | Left -> seed.CursorPos + 1

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
  let private queryUpdateBoundRight (byteVals: ByteVal []) byteCursor =
    let byteVals' =
      if byteCursor + MaxChunkLen < byteVals.Length
      then byteVals.[byteCursor .. byteCursor + MaxChunkLen]
      else byteVals.[byteCursor .. ]
    // We use an heuristic to bound update until the adjacent *fixed* ByteVal.
    match Array.tryFindIndex ByteVal.isFixed byteVals' with
    | None -> MaxChunkLen
    | Some idx -> idx

  /// Find the maximum length that can be updated for grey-box concolic testing.
  let queryUpdateBound seed direction =
    let byteVals = seed.ByteVals
    let byteCursor = seed.CursorPos
    match direction with
    | Stay -> failwith "queryUpdateBound() cannot be called with 'Stay'"
    | Left -> queryUpdateBoundLeft byteVals byteCursor
    | Right -> queryUpdateBoundRight byteVals byteCursor

  /// Get adjacent concrete byte values, toward the given direction.
  let queryNeighborBytes seed direction =
    let byteVals = seed.ByteVals
    let byteCursor = seed.CursorPos
    match direction with
    | Stay -> failwith "queryNeighborBytes() cannot be called with 'Stay'"
    | Right ->
      let upperBound = min (byteVals.Length - 1) (byteCursor + MaxChunkLen)
      Array.map ByteVal.getConcreteByte byteVals.[byteCursor + 1 .. upperBound]
    | Left ->
      let lowerBound = max 0 (byteCursor - MaxChunkLen)
      Array.map ByteVal.getConcreteByte byteVals.[lowerBound .. byteCursor - 1]

  (************************ Content update functions ************************)

  /// Impose a constraint with lower and upper bounds, on the ByteVal at the
  /// given offset.
  let constrainByteAt seed direction offset low upper =
    let byteCursor =
      match direction with
      | Stay -> failwith "constrainByteAt() cannot be called with 'Stay'"
      | Right -> seed.CursorPos + offset
      | Left -> seed.CursorPos - offset
    let newByteVals = Array.copy seed.ByteVals
    let newByteVal = if low <> upper then Interval (low, upper) else Fixed low
    newByteVals.[byteCursor] <- newByteVal
    { seed with ByteVals = newByteVals }

  /// Fix the current ByteVals pointed by the cursor, with the provided bytes.
  let fixCurBytes seed dir bytes =
    let nBytes = Array.length bytes
    let curByteVals = seed.ByteVals
    let byteCursor = seed.CursorPos
    let startPos = if dir = Right then byteCursor else byteCursor - nBytes + 1
    let newByteVals =
      // Note that 'MaxLen' is already checked in queryUpdateBound().
      if startPos + nBytes > curByteVals.Length then
        let reqSize = startPos + nBytes - curByteVals.Length
        Array.append curByteVals (Array.init reqSize (fun _ -> Undecided 0uy))
      else Array.copy curByteVals
    Array.iteri (fun i b -> newByteVals.[startPos + i] <- Fixed b) bytes
    { seed with ByteVals = newByteVals }

  /// Update the current ByteVal pointed by the cursor.
  let updateCurByte seed byteVal =
    let curByteVals = seed.ByteVals
    let byteCursor = seed.CursorPos
    let newByteVals = Array.copy curByteVals
    newByteVals.[byteCursor] <- byteVal
    { seed with ByteVals = newByteVals }

  (************************* Cursor update functions *************************)

  /// Set the byte cursor position of the seed.
  let setCursorPos seed newPos = { seed with CursorPos = newPos }

  /// Set the byte cursor direction of the seed.
  let setCursorDir seed dir = { seed with CursorDir = dir }

  /// Step the byte cursor, following the cursor direction.
  let stepCursor seed =
    let byteCursor = seed.CursorPos
    match seed.CursorDir with
    | Stay -> None
    | Left when 0 <= byteCursor - 1 ->
      Some (setCursorPos seed (byteCursor - 1))
    | Right when byteCursor + 1 < seed.ByteVals.Length ->
      Some (setCursorPos seed (byteCursor + 1))
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
  let private moveToUnfixedByte seed =
    let byteCursor = seed.CursorPos
    let cursorDir = seed.CursorDir
    match cursorDir with
    | Stay -> None
    | Left -> let offset = findUnfixedByteBackward seed.ByteVals byteCursor
              if offset <> -1 then Some (setCursorPos seed offset) else None
    | Right -> let offset = findUnfixedByte seed.ByteVals byteCursor
               if offset <> -1 then Some (setCursorPos seed offset) else None

  /// Move the byte cursor to the next unfixed ByteVal. Cursor should move at
  /// least one offset toward the current cursor direction.
  let proceedCursor seed =
    match stepCursor seed with
    | None -> None
    | Some newSeed -> moveToUnfixedByte newSeed

  /// Update the byte cursor direction of current input.
  let setByteCursorDir seed dir =
    { seed with CursorDir = dir }

  /// Randomly move byte cursor position within current input.
  let shuffleByteCursor seed =
    let curLength = seed.ByteVals.Length
    let newByteCursor = random.Next(curLength)
    let newCursorDir = if newByteCursor > (curLength / 2) then Left else Right
    { seed with CursorPos = newByteCursor; CursorDir = newCursorDir}

  /// Return seeds with byte cursor updated to point unfixed ByteVal. Probing
  /// should occur in both left side and right side.
  let relocateCursor seed =
    let curByteVal = getCurByteVal seed
    let leftwardSeed = setByteCursorDir seed Left
    let leftwardSeeds = // Avoid sampling at the same offset.
      if ByteVal.isSampledByte curByteVal then
        match stepCursor leftwardSeed with
        | None -> [] | Some s -> [ s ]
      else [ leftwardSeed ]
    let rightwardSeed = setByteCursorDir seed Right
    let rightwardSeeds =
      match stepCursor rightwardSeed with
      | None -> [] | Some s -> [ s ]
    List.choose moveToUnfixedByte (leftwardSeeds @ rightwardSeeds)

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

  /// Stringfy the given seed.
  let toString seed =
    let byteVals = List.ofArray seed.ByteVals
    let byteStr = byteValsToStr [] "" byteVals
    sprintf "%s (%d) (%A)" byteStr seed.CursorPos seed.CursorDir
