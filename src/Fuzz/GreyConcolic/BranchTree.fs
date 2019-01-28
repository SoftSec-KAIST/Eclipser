namespace Eclipser

open Config
open Utils
open Options

type Condition =
  | LinEq of LinearEquation
  | LinIneq of LinearInequality
  | Mono of Monotonicity

type Branch = Condition * BranchPoint

type BranchTree =
  | Nil
  | Step of Branch * DistanceSign * BranchTree
  | Fork of Branch * ((DistanceSign * BranchTree) list)
  | Diverge of (DistanceSign * BranchTree) list
and DistanceSign = Sign

module BranchTree =

  let rec genCombAux accCombs windowElems leftElems n =
    match leftElems with
    | [] -> accCombs
    | headElem :: tailElems ->
      let newCombs =
        combination (n - 1) windowElems // Select 'n-1' from window elements
        |> List.map (fun elems -> elems @ [headElem]) // Use 'headElem' as 'n'th
      let newWindowElems = List.tail windowElems @ [headElem]
      genCombAux (accCombs @ newCombs) newWindowElems tailElems n

  let genComb elems windowSize n =
    if List.length elems < windowSize then combination n elems else
      let headElems, tailElems = splitList windowSize elems
      let initialCombs = combination n headElems
      (* Pass element list of length 'windowSize - 1' as 'windowElems'. *)
      let initialWindow = List.tail headElems
      genCombAux initialCombs initialWindow tailElems n

  /// Check if provided BranchInfos are valid target to infer linearity or
  /// monotonicity. Note that we can skip inference if all the branch distances
  /// are the same.
  let checkValidTarget brInfos =
    if List.length brInfos < 3 then false else
      let brInfo = List.head brInfos
      let tailBrInfos = List.tail brInfos
      List.exists (fun f-> f.Distance <> brInfo.Distance) tailBrInfos

  let rec inferLinEqAux ctx brInfoCombinations =
    match brInfoCombinations with
    | brInfoTriple :: tailCombs ->
      let linEqOpt = LinearEquation.find ctx brInfoTriple
      if Option.isSome linEqOpt then linEqOpt else inferLinEqAux ctx tailCombs
    | [] -> None

  let inferLinEq opt ctx brInfos =
    if checkValidTarget brInfos then
      if opt.Verbosity >= 4 then
        let brStr = String.concat ", " (List.map BranchInfo.toString brInfos)
        log "Infer linearity with : %s" brStr
      genComb brInfos BranchCombinationWindow 3 // XXX
      (* Now convert each [a,b,c] into (a,b,c) *)
      |> List.map (function [ a; b; c ] -> (a,b,c) | _ -> failwith "invalid")
      |> inferLinEqAux ctx
    else None

  let rec inferLinIneqAux ctx brInfoCombinations =
    match brInfoCombinations with
    | brInfoTriple :: tailCombs ->
      let linIneq = LinearInequality.find ctx brInfoTriple
      if Option.isSome linIneq then linIneq else inferLinIneqAux ctx tailCombs
    | [] -> None

  let inferLinIneq opt ctx brInfos =
    if checkValidTarget brInfos then
      if opt.Verbosity >= 4 then
        let brStr = String.concat ", " (List.map BranchInfo.toString brInfos)
        log "Infer linearity with : %s" brStr
      genComb brInfos BranchCombinationWindow 3 // XXX
      (* Now convert each [a,b,c] into (a,b,c) *)
      |> List.map (function a::b::c::[] -> (a,b,c) | _ -> failwith "invalid")
      |> inferLinIneqAux ctx
    else None

  let inferMonotonicity opt brInfos =
    if checkValidTarget brInfos then
      if opt.Verbosity >= 4 then
        let brStr = String.concat ", " (List.map BranchInfo.toString brInfos)
        log "Infer monotonicity with : %s" brStr
      Monotonicity.find brInfos
    else None

  let inspectBranchInfos opt ctx visitCntMap branchInfos =
    // We already filtered out cases where length of BranchInfo is less than 3
    let firstBrInfo = List.head branchInfos
    let targAddr = firstBrInfo.InstAddr
    let targIdx = Map.find targAddr visitCntMap
    let targPt = { Addr = targAddr; Idx = targIdx }
    let brType = firstBrInfo.BrType
    if brType = Equality then
      match inferLinEq opt ctx branchInfos with
      | Some linEq -> Some (LinEq linEq, targPt)
      | None ->
        let monoOpt = inferMonotonicity opt branchInfos
        Option.map (fun mono -> (Mono mono, targPt)) monoOpt
    else
      match inferLinIneq opt ctx branchInfos with
      | Some linIneq -> Some (LinIneq linIneq, targPt)
      | None -> None

  let rec makeAux opt ctx prevBranchOpt accPairs groupedBranchTrace cont =
    match groupedBranchTrace with
    | End -> cont Nil
    | Unison brInfoSet ->
      let brInfoSet = BranchTraceSet.updateVisitCount brInfoSet
      let visitCntMap = brInfoSet.VisitCountMap
      let brInfos = List.map List.head (brInfoSet.BranchTraceList)
      let branchOpt = inspectBranchInfos opt ctx visitCntMap brInfos
      let groupedBranchTrace = BranchTraceSet.stepAndGroup brInfoSet
      match prevBranchOpt with
      | None -> makeAux opt ctx branchOpt [] groupedBranchTrace cont
      | Some branch ->
        let dist = brInfoSet.LastBranchDistance
        let distSign =
          if dist > 0I then Positive
          elif dist = 0I then Zero
          else Negative
        let cont = fun result -> cont (Step (branch, distSign, result))
        makeAux opt ctx branchOpt [] groupedBranchTrace cont
    | Split [] ->
      match prevBranchOpt with
      | None -> cont (Diverge accPairs)
      | Some branch -> cont (Fork (branch, accPairs))
    | Split (branchTraceSet :: tailBranchTraceSets) ->
      let branchTraceSet = BranchTraceSet.updateVisitCount branchTraceSet
      let visitCntMap = branchTraceSet.VisitCountMap
      let branchInfos = List.map List.head (branchTraceSet.BranchTraceList)
      let branchOpt = inspectBranchInfos opt ctx visitCntMap branchInfos
      let dist = branchTraceSet.LastBranchDistance
      let distSign =
        if dist > 0I then Positive
        elif dist = 0I then Zero
        else Negative
      let groupedBranchTrace = BranchTraceSet.stepAndGroup branchTraceSet
      makeAux opt ctx branchOpt [] groupedBranchTrace
        (fun resultTrace ->
          let accPairs = (distSign, resultTrace) :: accPairs
          makeAux opt ctx prevBranchOpt accPairs (Split tailBranchTraceSets) cont
        )

  let make opt ctx branchTraces =
    let branchTraces = List.filter (not << List.isEmpty) branchTraces
    let branchTraceSet = BranchTraceSet.init branchTraces
    let groupedBranchTrace = BranchTraceSet.group branchTraceSet
    makeAux opt ctx None [] groupedBranchTrace identity

  (* Extract linear and monotonic search targets from branch trace, since these
   * targets do not need execution tree information to solve branch condition.
   *)
  let rec extractSimpleConds branchTrace cont =
    match branchTrace with
    | Nil -> cont ([], [])
    | Step ((LinEq lineq, targPt), _, leftTrace) ->
      extractSimpleConds leftTrace
        (fun (eqs, monos) -> cont ((targPt, lineq) :: eqs, monos))
    | Step ((Mono monotonic, targPt), _, leftTrace) ->
      extractSimpleConds leftTrace
        (fun (eqs, monos) -> cont (eqs, ((targPt, monotonic) :: monos)))
    | Step (_, _, leftTrace) -> extractSimpleConds leftTrace cont
    | Diverge [] -> cont ([], [])
    | Diverge ((_, branchTrace) :: tailPairs) ->
      extractSimpleConds branchTrace
        (fun (eqs, monos) ->
          extractSimpleConds (Diverge tailPairs)
            (fun (eqs', monos') -> cont (eqs @ eqs', monos @ monos'))
        )
    | Fork ((LinEq lineq, targPt), []) ->
      let eqs = [(targPt, lineq)]
      cont (eqs, [])
    | Fork ((Mono monotonic, targPt), []) ->
      let monos = [(targPt, monotonic)]
      cont ([], monos)
    | Fork (_, []) -> cont ([], [])
    | Fork (branch, (_, branchTrace) :: tailPairs) ->
      extractSimpleConds branchTrace
        (fun (eqs, monos) ->
          extractSimpleConds (Fork (branch, tailPairs))
            (fun (eqs', monos') -> cont (eqs @ eqs', monos @ monos'))
        )

  (* Leave branches with linear inequalities from the given branch trace. *)
  let rec leaveInequality branchTrace accPairs cont =
    match branchTrace with
    | Nil -> cont Nil
    | Step ((LinIneq inequality, targPt), distSign, leftTrace) ->
      leaveInequality leftTrace []
        (fun trace -> cont (Step ((LinIneq inequality, targPt), distSign, trace)))
    | Step (_, _, leftTrace) -> leaveInequality leftTrace [] cont
    | Diverge [] -> cont (Diverge accPairs)
    | Diverge ((distSign, branchTrace) :: tailPairs) ->
      leaveInequality branchTrace []
        (fun trace ->
          let accPairs = (distSign, trace) :: accPairs
          leaveInequality (Diverge tailPairs) accPairs cont
        )
    | Fork ((LinIneq inequality, targPt), []) ->
      cont (Fork ((LinIneq inequality, targPt), accPairs))
    | Fork (_, []) -> cont (Diverge accPairs)
    | Fork (branch, (distSign, branchTrace) :: tailPairs) ->
      leaveInequality branchTrace []
        (fun trace ->
          let accPairs = (distSign, trace) :: accPairs
          leaveInequality (Fork (branch, tailPairs)) accPairs cont
        )

  let rec getTraceSize branchTrace accSize cont =
    match branchTrace with
    | Nil -> cont accSize
    | Step (_, _, leftTrace) -> getTraceSize leftTrace (accSize + 1) cont
    | Diverge [] -> cont accSize
    | Diverge ((_, branchTrace) :: tailPairs) ->
      getTraceSize branchTrace 0
        (fun size ->
          getTraceSize (Diverge tailPairs) (accSize + size) cont
        )
    | Fork (_, []) -> cont (accSize + 1)
    | Fork (branch, (_, branchTrace) :: tailPairs) ->
      getTraceSize branchTrace 0
        (fun size ->
          getTraceSize (Fork (branch, tailPairs)) (accSize + size) cont
        )

  let rec filterBranchTrace branchTrace num selectInfo accPairs cont =
    match branchTrace with
    | Nil -> cont (num, Nil)
    | Step (branch, distSign, leftTrace) when Set.contains num selectInfo ->
      filterBranchTrace leftTrace (num + 1) selectInfo []
        (fun (num, trace) -> cont (num, Step (branch, distSign, trace)))
    | Step (_, _, leftTrace) ->
      filterBranchTrace leftTrace (num + 1) selectInfo [] cont
    | Diverge [] -> cont (num, Diverge accPairs)
    | Diverge ((distSign, branchTrace) :: tailPairs) ->
      filterBranchTrace branchTrace num selectInfo []
        (fun (num', trace) ->
          let accPairs = (distSign, trace) :: accPairs
          let branchTrace' = Diverge tailPairs
          filterBranchTrace branchTrace' num' selectInfo accPairs cont
        )
    | Fork (branch, []) when Set.contains num selectInfo ->
      cont (num + 1, Fork (branch, accPairs))
    | Fork (_, []) ->
      cont (num, Diverge accPairs)
    | Fork (branch, (distSign, branchTrace) :: tailPairs) ->
      filterBranchTrace branchTrace num selectInfo []
        (fun (num', trace) ->
          let accPairs = (distSign, trace) :: accPairs
          let branchTrace' = Fork (branch, tailPairs)
          filterBranchTrace branchTrace' num' selectInfo accPairs cont
        )

  let selectBranches opt branchTrace =
    let selectN = opt.NSolve
    let size = getTraceSize branchTrace 0 identity
    if selectN > size then branchTrace else
      if opt.Verbosity >= 1 && selectN < size then
        let abandonN = size - selectN
        log "FYI : abandon %d branch conditions" abandonN
      let selectInfo = randomSubset size selectN
      filterBranchTrace branchTrace 0 selectInfo [] identity |> snd
      //let newSize = getTraceSize branchTraceFiltered 0 identity
      //log "new size after filtering : %d" newSize
