namespace Eclipser

open Utils
open Options

type BranchTrace = BranchInfo list

module BranchTrace =

  let collectAux opt seed acc tryVal =
    let accTraces, accCandidates = acc
    let tryByteVal = Sampled (byte tryVal)
    let trySeed = Seed.updateCurByte seed tryByteVal
    let exitSig, covGain, trace = Executor.getBranchTrace opt trySeed tryVal
    let accTraces = trace :: accTraces
    let accCandidates = if covGain = NewEdge || Signal.isCrash exitSig
                        then seed :: accCandidates
                        else accCandidates
    (accTraces, accCandidates)

  let collect seed opt minVal maxVal =
    let nSpawn = opt.NSpawn
    let tryVals = sampleInt minVal maxVal nSpawn
    let traces, candidates = List.fold(collectAux opt seed) ([], []) tryVals
    List.rev traces, List.rev candidates // To preserver order.

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
