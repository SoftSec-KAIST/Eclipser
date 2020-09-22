namespace Eclipser

open Utils
open Options

type BranchTrace = BranchInfo list

module BranchTrace =

  let collectAux opt (accTraces, accNewPathSeeds, accPaths) seed v =
    let pathHash, branchTrace = Executor.getBranchTrace opt seed v
    let accTraces = branchTrace :: accTraces
    let accNewPathSeeds =
      if Manager.isNewPath pathHash && not (Set.contains pathHash accPaths)
      then seed :: accNewPathSeeds
      else accNewPathSeeds
    let accPaths = Set.add pathHash accPaths
    (accTraces, accNewPathSeeds, accPaths)

  let collect seed opt minVal maxVal =
    let nSpawn = opt.NSpawn
    let tryVals = sampleInt minVal maxVal nSpawn
    let tryBytes = List.map (fun v -> Sampled (byte v)) tryVals
    let trySeeds = List.map (Seed.updateCurByte seed) tryBytes
    let traces, newPathSeeds, _ =
      List.fold2 (collectAux opt) ([], [], Set.empty) trySeeds tryVals
    (List.rev traces, newPathSeeds) // List.rev to maintain order

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
