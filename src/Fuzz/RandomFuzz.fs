module Eclipser.RandomFuzz

open Config
open Utils
open BytesUtils
open Options

// Constants
let TRIM_MIN_BYTES = 4
let TRIM_START_STEPS = 16
let TRIM_END_STEPS = 1024

let ARITH_MAX = 35
let BLOCK_SIZE_MAX = 32

let HAVOC_STACK_POW = 7
let HAVOC_BLOCK_SMALL = 32
let HAVOC_BLOCK_MEDIUM = 128
let HAVOC_BLOCK_LARGE = 1500
let HAVOC_BLOCK_XLARGE = 32768

// Mutable variables for statistics management.
let mutable private recentExecNums: Queue<int> = Queue.empty
let mutable private recentNewPathNums: Queue<int> = Queue.empty

let updateStatus opt execN newPathN =
  let recentExecNums' = if Queue.getSize recentExecNums > RecentRoundN
                        then Queue.drop recentExecNums
                        else recentExecNums
  recentExecNums <- Queue.enqueue recentExecNums' execN
  let recentNewPathNums' = if Queue.getSize recentNewPathNums > RecentRoundN
                           then Queue.drop recentNewPathNums
                           else recentNewPathNums
  recentNewPathNums <- Queue.enqueue recentNewPathNums' newPathN

let evaluateEfficiency () =
  let execNum = List.sum (Queue.elements recentExecNums)
  let newPathNum = List.sum (Queue.elements recentNewPathNums)
  if execNum = 0 then 1.0 else float newPathNum / float execNum

let chooseBlockSize limit =
  // XXX: Original havoc mutation in AFL also depends on queue cycle
  let minVal, maxVal =
    match random.Next(4) with
    | 0 -> 1, HAVOC_BLOCK_SMALL
    | 1 | 2 -> HAVOC_BLOCK_SMALL, HAVOC_BLOCK_MEDIUM // P=1/3 in original havoc
    | 3 -> if random.Next(10) <> 0
           then HAVOC_BLOCK_MEDIUM, HAVOC_BLOCK_LARGE
           else HAVOC_BLOCK_LARGE, HAVOC_BLOCK_XLARGE
    | _ -> failwith "Unexpected random value"
  let minVal = if minVal >= limit then 1 else minVal
  let maxVal = min maxVal limit
  minVal + random.Next(maxVal - minVal + 1)

let rec roundUpExpAux accVal input =
  if accVal >= input then accVal else roundUpExpAux (accVal <<< 1) input

let roundUpExp input =
  roundUpExpAux 1 input

let rec trimAux opt accSeed edgeHash trimMinSize trimSize pos =
  if trimSize < trimMinSize then
    accSeed // Cannot lower trimSize anymore, time to stop
  elif pos + trimSize >= Seed.getCurInputLen accSeed then
    (* Reached end, retry with more fine granularity (reset idx to 0). *)
    trimAux opt accSeed edgeHash trimMinSize (trimSize / 2) 0
  else  let trySeed = Seed.removeBytesFrom accSeed pos trimSize
        let tryEdgeHash = Executor.getEdgeHash opt trySeed
        if tryEdgeHash = edgeHash then // Trimming succeeded.
          (* Caution : Next trimming  position is not 'pos + trimSize' *)
          trimAux opt trySeed edgeHash trimMinSize trimSize pos
        else (* Trimming failed, move on to next position *)
          let newPos = (pos + trimSize)
          trimAux opt accSeed edgeHash trimMinSize trimSize newPos

let trim opt edgeHash seed =
  let inputLen = Seed.getCurInputLen seed
  let inputLenRounded = roundUpExp inputLen
  let trimSize = max (inputLenRounded / TRIM_START_STEPS) TRIM_MIN_BYTES
  let trimMinSize = max (inputLenRounded / TRIM_END_STEPS) TRIM_MIN_BYTES
  let trimmedSeed = trimAux opt seed edgeHash trimMinSize trimSize 0
  // Should adjust byte cursor again within a valid range.
  Seed.shuffleByteCursor trimmedSeed

let printFoundSeed seed newEdgeN =
  let seedStr = Seed.toString seed
  let edgeStr = if newEdgeN > 0 then sprintf "(%d new edges) " newEdgeN else ""
  log "[*] Found by random fuzzing %s: %s" edgeStr seedStr

let evalSeedsAux opt accItems seed =
  let newEdgeN, pathHash, edgeHash, exitSig = Executor.getCoverage opt seed
  let isNewPath = Manager.storeSeed opt seed newEdgeN pathHash edgeHash exitSig
  if newEdgeN > 0 && opt.Verbosity >= 0 then printFoundSeed seed newEdgeN
  if isNewPath && not (Signal.isTimeout exitSig) && not (Signal.isCrash exitSig)
  then
    let priority = if newEdgeN > 0 then Favored else Normal
    // Trimming is only for seeds that found new edges
    let seed' = if newEdgeN > 0 then trim opt edgeHash seed else seed
    (priority, seed') :: accItems
  else accItems

let evalSeeds opt seeds =
  List.fold (evalSeedsAux opt) [] seeds

(* Functions related to mutation *)

let rec getRandomPositionAux opt seed inputLen size =
  let bytePos = random.Next(inputLen + 1 - size)
  if Seed.isUnfixedByteAt seed bytePos then bytePos
  elif random.Next(100) >= SkipFixedProb then bytePos // XXX
  else getRandomPositionAux opt seed inputLen size

let getRandomPosition opt seed size =
  let inputLen = Seed.getCurInputLen seed
  getRandomPositionAux opt seed inputLen size

let flipBit opt seed =
  let bytePos = getRandomPosition opt seed 1
  let bitPos = random.Next(8)
  Seed.flipBitAt seed bytePos bitPos

let randomByte opt seed =
  let pos = getRandomPosition opt seed 1
  let newByte = allBytes.[random.Next(allBytes.Length)]
  let newByteVal = Undecided newByte
  Seed.updateByteValAt seed pos newByteVal

let i_byte = [|0x80uy; 0xffuy; 0uy; 1uy; 16uy; 32uy; 64uy; 100uy; 127uy|]

let interestingByte opt seed =
  let pos = getRandomPosition opt seed 1
  let newBytes = [| i_byte.[random.Next(Array.length i_byte)] |]
  Seed.updateBytesFrom seed pos newBytes

let i_word = [|0x80us; 0xffus; 0us; 1us; 16us; 32us; 64us; 100us; 127us;
               0x8000us; 0xff7fus; 128us; 255us; 256us; 512us; 1000us;
               1024us; 4096us; 32767us|]

let interestingWord opt seed =
  // Assume input length is greater than 2.
  let pos = getRandomPosition opt seed 2
  let endian = if random.Next(2) = 0 then LE else BE
  let interest = i_word.[random.Next(Array.length i_word)]
  let newBytes = uIntToBytes endian 2 (uint32 interest)
  Seed.updateBytesFrom seed pos newBytes

let i_dword = [|0x80u; 0xffu; 0u; 1u; 16u; 32u; 64u; 100u; 127u; 0x8000u;
               0xff7fu; 128u; 255u; 256u; 512u; 1000u; 1024u; 4096u; 32767u;
               0x80000000u; 0xfa0000fau; 0xffff7fffu; 0x8000u; 0xffffu;
               0x10000u; 0x5ffff05u; 0x7fffffffu|]

let interestingDWord opt seed =
  // Assume input length is greater than 4.
  let pos = getRandomPosition opt seed 4
  let endian = if random.Next(2) = 0 then LE else BE
  let interest = i_dword.[random.Next(Array.length i_dword)]
  let newBytes = uIntToBytes endian 4 interest
  Seed.updateBytesFrom seed pos newBytes

let arithByte opt seed =
  let pos = getRandomPosition opt seed 1
  let curVal = uint32 (Seed.getConcreteByteAt seed pos)
  let delta = uint32 (1 + random.Next(ARITH_MAX))
  let newVal = if random.Next(2) = 0 then curVal + delta else curVal - delta
  let newByte = byte (newVal &&& 0xffu)
  let newByteVal = Undecided newByte
  Seed.updateByteValAt seed pos newByteVal

let arithWord opt seed =
  // Assume input length is greater than 2.
  let pos = getRandomPosition opt seed 2
  let endian = if random.Next(2) = 0 then LE else BE
  let curBytes = Seed.getConcreteBytesFrom seed pos 2
  let curVal = bytesToUInt endian curBytes
  let delta = uint32 (1 + random.Next(ARITH_MAX))
  let newVal = if random.Next(2) = 0 then curVal + delta else curVal - delta
  let newBytes = uIntToBytes endian 2 newVal
  Seed.updateBytesFrom seed pos newBytes

let arithDword opt seed =
  // Assume input length is greater than 4.
  let pos = getRandomPosition opt seed 4
  let endian = if random.Next(2) = 0 then LE else BE
  let curBytes = Seed.getConcreteBytesFrom seed pos 4
  let curVal = bytesToUInt endian curBytes
  let delta = uint32 (1 + random.Next(ARITH_MAX))
  let newVal = if random.Next(2) = 0 then curVal + delta else curVal - delta
  let newBytes = uIntToBytes endian 4 newVal
  Seed.updateBytesFrom seed pos newBytes

let insertBytes seed =
  let inputLen = Seed.getCurInputLen seed
  (* '+ 1' since we want to insert at the end of input, too. *)
  let insertPosition = random.Next(inputLen + 1)
  let insertSize = chooseBlockSize HAVOC_BLOCK_MEDIUM
  let genNewByte () = allBytes.[random.Next(allBytes.Length)]
  let insertContent = Array.init insertSize (fun _ -> genNewByte())
  Seed.insertBytesInto seed insertPosition insertContent

let havocInsert seed =
  let inputLen = Seed.getCurInputLen seed
  (* '+ 1' since we want to insert at the end of input, too. *)
  let insertPosition = random.Next(inputLen + 1)
  let insertContent =
    if random.Next(2) = 0 then // 1/4 in original havoc
      let insertSize = chooseBlockSize HAVOC_BLOCK_XLARGE
      let newByte = allBytes.[random.Next(allBytes.Length)]
      Array.init insertSize (fun _ -> newByte)
    else
      let insertSize = chooseBlockSize inputLen
      (* '+ 1' is needed to allow copying from the last byte. *)
      let copyFrom = random.Next(inputLen - insertSize + 1)
      Seed.getConcreteBytesFrom seed copyFrom insertSize
  Seed.insertBytesInto seed insertPosition insertContent

let overwriteBytes seed =
  let inputLen = Seed.getCurInputLen seed
  let writeSize = chooseBlockSize (inputLen / 2) // 'inputLen' in orig. havoc
  (* '+ 1' is needed to allow overwriting the last byte. *)
  let writePosition = random.Next(inputLen - writeSize + 1)
  let writeContent =
    if random.Next(2) <> 0 then // 1/4 in original havoc
      let newByte = allBytes.[random.Next(allBytes.Length)]
      Array.init writeSize (fun _ -> newByte)
    else
      (* '+ 1' is needed to allow copying from the last byte. *)
      let copyFrom = random.Next(inputLen - writeSize + 1)
      Seed.getConcreteBytesFrom seed copyFrom writeSize
  Seed.updateBytesFrom seed writePosition writeContent

let removeBytes seed =
  // Assume input length is greater than 4.
  let inputLen = Seed.getCurInputLen seed
  let removeSize = chooseBlockSize (inputLen / 2) // 'inputLen' in orig. havoc
  let removePosition = random.Next(inputLen - removeSize)
  Seed.removeBytesFrom seed removePosition removeSize

let havocMutate opt seed =
  let curInputLen = Seed.getCurInputLen seed
  let maxN = if curInputLen < 2 then 8 elif curInputLen < 4 then 11 else 16
  match random.Next(maxN) with
  | 0 -> flipBit opt seed
  | 1 -> randomByte opt seed
  | 2 -> havocInsert seed
  | 3 -> insertBytes seed
  | 4 -> overwriteBytes seed
  | 5 | 6 -> arithByte opt seed
  | 7 -> interestingByte opt seed
  // Require input length >= 2
  | 8 | 9 -> arithWord opt seed
  | 10 -> interestingWord opt seed
  // Require input length >= 4
  | 11 | 12 -> arithDword opt seed
  | 13 -> interestingDWord opt seed
  | 14 | 15  -> removeBytes seed
  | _ -> failwith "Wrong mutation code"

let rec repRandomMutateAux seed opt depth depthLimit  accumSeed =
  if depth >= depthLimit then accumSeed else
    let accumSeed = havocMutate opt accumSeed
    repRandomMutateAux seed opt (depth + 1) depthLimit accumSeed

let repRandomMutate seed opt =
  let seed = Seed.shuffleInputCursor seed
  let curInputLen = Seed.getCurInputLen seed
  let maxMutateN = max 1 (int (float curInputLen * MutateRatio))
  let mutateN = min maxMutateN (1 <<< (1 + random.Next(HAVOC_STACK_POW)))
  let mutatedSeed = repRandomMutateAux seed opt 0 mutateN seed
  Seed.shuffleByteCursor mutatedSeed

let run seed opt =
  let tryCnt = Config.RandSchTryCount
  let mutatedSeeds = List.init tryCnt (fun _ -> repRandomMutate seed opt)
  evalSeeds opt mutatedSeeds
