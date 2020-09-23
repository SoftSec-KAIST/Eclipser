namespace Eclipser

open Utils
open Options

type BranchTrace = BranchInfo list

module BranchTrace =

  let collectAux opt seed tryVal =
    let tryByteVal = Sampled (byte tryVal)
    let trySeed = Seed.updateCurByte seed tryByteVal
    let exitSig, covGain, trace = Executor.getBranchTrace opt trySeed tryVal
    (trace, (trySeed, exitSig, covGain))

  let collect seed opt minVal maxVal =
    let nSpawn = opt.NSpawn
    let tryVals = sampleInt minVal maxVal nSpawn
    List.map (collectAux opt seed) tryVals |> List.unzip

  let getHeadAddr (brTrace: BranchTrace) =
    match brTrace with
    | [] -> failwith "getHeadAddr() called with an empty list"
    | bInfo :: _ -> bInfo.InstAddr

  let getNextAddr (brTrace: BranchTrace) =
    match brTrace with
    | [] -> failwith "getNextAddr() called with an empty list"
    | [ _ ] -> failwith "getNextAddr() called with a length-one list"
    | _ :: bInfo :: _ -> bInfo.InstAddr

  let isLongerThanOne (brTrace: BranchTrace) =
    match brTrace with
    | [] | [ _ ] -> false
    | _ -> true
