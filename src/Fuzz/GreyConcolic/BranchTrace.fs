namespace Eclipser

open System
open System.Collections.Generic
open Utils

type BranchTrace = BranchInfo list

/// Branch trace set, whose first branch items have the same address.
type BranchTraceSet = {
  LastBranchDistance : bigint
  VisitCountMap      : Map<uint64, int>
  BranchTraceList      : BranchTrace list
}

type GroupedBranchTrace =
  | End (* Reached the end of trace, or no trace left after grouping *)
  | Unison of BranchTraceSet
  | Split of BranchTraceSet list

module BranchTrace =

  let collectAux opt (accTraces, accCandidates) seed v =
    let execID, branchTrace = Executor.getBranchTrace opt seed v
    let accTraces = branchTrace :: accTraces
    let accCandidates = if Manager.isNewPath execID
                        then (execID, seed) :: accCandidates
                        else accCandidates
    (accTraces, accCandidates)

  let collect seed opt minVal maxVal sampleN =
    let tryVals = sampleInt minVal maxVal sampleN
    let tryBytes = List.map (fun v -> Sampled (byte v)) tryVals
    let trySeeds = List.map (Seed.updateCurByte seed) tryBytes
    let traces, candidateSeeds =
      List.fold2 (collectAux opt) ([], []) trySeeds tryVals
    let candidateSeeds = List.map snd (List.distinctBy fst candidateSeeds)
    (List.rev traces, candidateSeeds) // List.rev to maintain order

module BranchTraceSet =

  let init branchTraces =
    { LastBranchDistance = 0I
      VisitCountMap = Map.empty
      BranchTraceList = branchTraces }

  let group (branchTraceSet : BranchTraceSet) : GroupedBranchTrace =
    branchTraceSet.BranchTraceList
    (* Group by the address of next BranchInfo *)
    |> List.groupBy (fun branchTrace -> (List.head branchTrace).InstAddr)
    |> List.unzip |> snd
    |> List.map (fun brs -> { branchTraceSet with BranchTraceList = brs })
    // If the group contains less than 3 traces, there's no point in proceeding.
    |> List.filter (fun grouped -> grouped.BranchTraceList.Length >= 3)
    |> function
       | [] -> End
       | [ traceSet ] -> Unison traceSet
       | traceSets -> Split traceSets

  let updateVisitCount (branchTraceSet : BranchTraceSet) : BranchTraceSet =
    let visitCntMap = branchTraceSet.VisitCountMap
    let branchTraces = branchTraceSet.BranchTraceList
    let branchTrace = List.head branchTraces
    let branchInfo = List.head branchTrace
    let addr = branchInfo.InstAddr
    let cnt = try Map.find addr visitCntMap with :? KeyNotFoundException -> 0
    let newVisitCntMap = Map.add addr (cnt + 1) visitCntMap
    { branchTraceSet with VisitCountMap = newVisitCntMap }

  // TODO : Optimize so that we don't need redundant group() function above.
  let stepAndGroup (branchTraceSet: BranchTraceSet) : GroupedBranchTrace =
    branchTraceSet.BranchTraceList
    (* Group by the address of next BranchInfo *)
    |> List.map (fun brTrace -> List.head brTrace, List.tail brTrace)
    |> List.filter (fun (_, tailBrTrace) -> not (List.isEmpty tailBrTrace))
    |> List.groupBy (fun (_, tailBrTrace) -> (List.head tailBrTrace).InstAddr)
    |> List.map
        (fun (_, branchPairs) ->
          let headBranchInfo, _ = (List.head branchPairs)
          let headBranchDist = headBranchInfo.Distance
          let branchTraces = List.map snd branchPairs // Step on branch trace
          { branchTraceSet with
              LastBranchDistance = headBranchDist
              BranchTraceList = List.filter (not << List.isEmpty) branchTraces }
        )
    // If the group contains less than 3 traces, there's no point in proceeding.
    |> List.filter (fun grouped -> grouped.BranchTraceList.Length >= 3)
    |> function
       | [] -> End
       | [ traceSet ] -> Unison traceSet
       | traceSets -> Split traceSets
