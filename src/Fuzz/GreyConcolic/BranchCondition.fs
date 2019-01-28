namespace Eclipser

open System
open Utils
open Options
open BytesUtils

/// Indicates how the seeds was generated
type Origin =  Equation | Inequality | Monoton | Spawn

module BranchCondition =

  (* Functions related to solving linear equations *)
  let rec findNextCharAux seed opt targPt accStr accBrInfos tryVals =
    match tryVals with
    | [] -> None
    | tryVal :: tailVals ->
      let tryStr = Array.append accStr [| byte tryVal |]
      let trySeed = Seed.fixCurBytes seed Right tryStr
      let _, brInfoOpt = Executor.getBranchInfoAt opt trySeed tryVal targPt
      match brInfoOpt with
      | None -> // Failed to observe target point, proceed with the next tryVal
        findNextCharAux seed opt targPt accStr accBrInfos tailVals
      | Some brInfo ->
        let accBrInfos = accBrInfos @ [brInfo]
        let ctx = { Bytes = [| |]; ByteDir = Right }
        match BranchTree.inferLinEq opt ctx accBrInfos with
        | None -> // No linear equation found yet, proceed with more brInfo
          findNextCharAux seed opt targPt accStr accBrInfos tailVals
        | Some linEq -> (* The solution of this equation is next character *)
          match linEq.Solutions with
          | [] -> failwith "Linear Equation w/ empty solution never generated"
          | [ sol ] -> Some sol
          | sol :: _ ->
            log "Warning : multiple candidates found in string search"
            Some sol

  let findNextChar seed opt targPt accStr =
    let sampleVals = sampleInt 0I 255I opt.NSpawn
    findNextCharAux seed opt targPt accStr [] sampleVals

  let rec tryStrSol seed opt maxLen targPt accRes tryStr =
    let trySeed = Seed.fixCurBytes seed Right tryStr
    // Use dummy value as 'tryVal', since our interest is in branch distance.
    match Executor.getBranchInfoAt opt trySeed 0I targPt with
    | pathHash, Some brInfo when brInfo.Distance = 0I ->
      if Manager.isNewPath pathHash
      then (trySeed :: accRes)
      else accRes
    | _, Some _ -> // Non-zero branch distance, try next character.
      if Array.length tryStr >= maxLen then accRes else
        let nextCharOpt = findNextChar seed opt targPt tryStr
        match nextCharOpt with
        | None -> accRes
        | Some nextChar ->
          let tryStr = Array.append tryStr [| byte nextChar |]
          tryStrSol seed opt maxLen targPt accRes tryStr
    | _, None -> accRes // Target point disappeared, halt.

  let solveAsString seed opt targPt linEq accRes =
    let initStrs = List.map (bigIntToBytes BE 1) linEq.Solutions
    let maxLen = Seed.queryUpdateBound seed Right
    List.fold (tryStrSol seed opt maxLen targPt) accRes initStrs

  let tryChunkSol seed opt dir targPt endian size accRes sol =
    let tryBytes = bigIntToBytes endian size sol
    let trySeed = Seed.fixCurBytes seed dir tryBytes
    // Use dummy value as 'tryVal', since our interest is in branch distance.
    match Executor.getBranchInfoAt opt trySeed 0I targPt with
    | pathHash, Some brInfo when brInfo.Distance = 0I ->
      if Manager.isNewPath pathHash
      then trySeed :: accRes
      else accRes
    | _, Some _ -> accRes // Non-zero branch distance, failed.
    | _, None -> accRes // Target point disappeared, halt.

  let solveAsChunk seed opt dir targPt linEq accRes =
    let sols = linEq.Solutions
    let size = linEq.ChunkSize
    let endian = linEq.Endian
    List.fold (tryChunkSol seed opt dir targPt endian size) accRes sols

  let solveEquation seed opt dir accRes (targPt, linEq) =
    if opt.Verbosity >= 4 then
      log "Solving @ 0x%x(#%d)" targPt.Addr targPt.Idx
      log "Linear equation : %s" (LinearEquation.toString linEq)
    if linEq.ChunkSize = 1
    then solveAsString seed opt targPt linEq accRes
    else solveAsChunk seed opt dir targPt linEq accRes

  let rec removeDuplicateSolution (linEqs : (BranchPoint * LinearEquation) list)
     accBrs accSols =
    match linEqs with
    | [] -> accBrs
    | (tarPt, linEq) :: tailLinEqs when linEq.ChunkSize = 1 ->
      (* We should not filter equations that have 1-byte size solution,
       * since we will consider these branches as string comparison.
       *)
      removeDuplicateSolution tailLinEqs ((tarPt, linEq) :: accBrs) accSols
    | (tarPt, linEq) :: tailLinEqs ->
      if Set.contains linEq.Solutions accSols
      then removeDuplicateSolution tailLinEqs accBrs accSols
      else
        let accSols = Set.add linEq.Solutions accSols
        removeDuplicateSolution tailLinEqs ((tarPt, linEq) :: accBrs) accSols

  let filterEquations opt linEqs =
    if List.length linEqs > 5000 then linEqs
    else removeDuplicateSolution linEqs [] Set.empty

  let solveEquations seed opt dir linEqs =
    let solveN = opt.NSolve / 3
    //let linEqs = filterEquations opt linEqs
    let linEqsChosen = randomSelect linEqs solveN
    if opt.Verbosity >= 1 && solveN < List.length linEqs then
      let abandonN = List.length linEqs - solveN
      log "FYI : abandon %d linear equations" abandonN
    List.fold (solveEquation seed opt dir) [] linEqsChosen
    |> List.rev // To preserve original order

  (* Functions related to binary search on monotonic function *)

  let getFunctionValue monotonic brInfo =
    let sign = if brInfo.BrType = UnsignedSize then Unsigned else Signed
    let size = brInfo.OpSize
    (* Since we checked brInfo.Distance <> 0, if Oprnd1 is equal to target
    * value it means that Oprnd2 corresponds to the output of the monotonic
    * function, and vice versa.
    *)
    if BranchInfo.interpretAs sign size brInfo.Oprnd1 = monotonic.TargetVal
    then BranchInfo.interpretAs sign size brInfo.Oprnd2
    else BranchInfo.interpretAs sign size brInfo.Oprnd1

  let rec binarySearch seed opt dir maxLen targPt accRes mono =
    let tryVal = (mono.LowerBound + mono.UpperBound) / 2I
    if opt.Verbosity >= 4 then log "Trying %A" tryVal
    let endian = if dir = Left then LE else BE
    let tryBytes = bigIntToBytes endian mono.ByteLen tryVal
    let trySeed = Seed.fixCurBytes seed dir tryBytes
    match Executor.getBranchInfoAt opt trySeed tryVal targPt with
    | _, None -> accRes // Target point disappeared, halt.
    | pathHash, Some brInfo when brInfo.Distance = 0I ->
      if Manager.isNewPath pathHash
      then trySeed :: accRes
      else accRes
    | _, Some brInfo -> // Caution : In this case, pathHash is incorrect
      let newY = getFunctionValue mono brInfo
      (* TODO : check monotonicity violation, too. *)
      let newMono = Monotonicity.update mono tryVal newY
      if newMono.ByteLen <= maxLen then
        binarySearch seed opt dir maxLen targPt accRes newMono
      else accRes

  let solveMonotonic seed opt accRes (targPt, mono) =
    if opt.Verbosity >= 4 then
      log "Solving @ 0x%x(#%d)" targPt.Addr targPt.Idx
      log "Monotonicity : %s" (Monotonicity.toString mono)
    let maxLenR = Seed.queryUpdateBound seed Right
    let maxLenL = Seed.queryUpdateBound seed Left
    match seed.SourceCursor with
    | InputKind.Args ->
      binarySearch seed opt Right maxLenR targPt accRes mono
    | _ -> // Try big endian first, and stop if any result is found.
      let res = binarySearch seed opt Right maxLenR targPt [] mono
      if List.isEmpty res
      then binarySearch seed opt Left maxLenL targPt accRes mono
      else res @ accRes

  let solveMonotonics seed opt monotonics =
    let solveN = opt.NSolve / 3
    if opt.Verbosity >= 1 && solveN < List.length monotonics then
      let abandonN = List.length monotonics - solveN
      log "FYI : abandon %d binary search targets" abandonN
    let monosChosen = randomSelect monotonics solveN
    List.fold (solveMonotonic seed opt) [] monosChosen
    |> List.rev // To preserve original order

  (* Functions related to solving linear inequalities *)

  let differentSign i1 i2 = i1 < 0I && i2 > 0I || i1 > 0I && i2 < 0I

  let sameSign i1 i2 = i1 < 0I && i2 < 0I || i1 > 0I && i2 > 0I

  let splitWithSolution (sol, sign) = (sol - 1I, sol + 1I, sign)

  let rec generateRangesAux prevHigh prevSign max splitPoints accRes1 accRes2 =
    match splitPoints with
    | [] ->
      let accRes1, accRes2 =
        if prevSign = Positive
        then accRes1, (prevHigh, max) :: accRes2
        else (prevHigh, max) :: accRes1, accRes2
      accRes1, accRes2
    | (low, high, sign) :: tailSplitPoints ->
      let accRes1, accRes2 =
        if sign = Positive
        then (prevHigh, low) :: accRes1, accRes2
        else accRes1, (prevHigh, low) :: accRes2
      if high > max then (accRes1, accRes2) else
        generateRangesAux high sign max tailSplitPoints accRes1 accRes2

  let extractMSB size (i1:bigint, i2:bigint, sign) =
    (i1 >>> ((size - 1) * 8), i2 >>> ((size - 1) * 8), sign)

  // Currently we consider constraints just for MSB.
  let rec generateMSBRanges splitPoints size sign =
    if List.isEmpty splitPoints then [], [] else
      let splitPoints = List.sortBy (fun (x, _, _) -> x) splitPoints
      let splitPoints = List.map (extractMSB size) splitPoints
      let max = if sign = Signed then 127I else 255I
      // 'Positive' is used as a dummy argument.
      generateRangesAux 0I Positive max splitPoints [] []

  let rec generateRanges splitPoints size =
    if List.isEmpty splitPoints then [], [] else
      let splitPoints = List.sortBy (fun (x, _, _) -> x) splitPoints
      let max = getUnsignedMax size
      // 'Positive' is used as a dummy argument.
      generateRangesAux 0I Positive max splitPoints [] []

  let checkSolutionAux seed opt dir endian size targPt accRes sol =
    let tryBytes = bigIntToBytes endian size sol
    let trySeed = Seed.fixCurBytes seed dir tryBytes
    // Use dummy value as 'tryVal', since our interest is in branch distance.
    match Executor.getBranchInfoAt opt trySeed 0I targPt with
    | _, Some brInfo when brInfo.Distance = 0I ->
      let tryBytes' = bigIntToBytes endian size (sol - 1I)
      let trySeed' = Seed.fixCurBytes seed dir tryBytes'
      match Executor.getBranchInfoAt opt trySeed' 0I targPt with
      | _, Some brInfo' ->
        let sign = if brInfo'.Distance > 0I then Positive else Negative
        (sol, sign) :: accRes
      | _ -> accRes
    | _, _ -> accRes

  let checkSolution seed opt dir equation targPt =
    let solutions = equation.Solutions
    let endian = equation.Endian
    let size = equation.ChunkSize
    let sign = equation.Linearity.Slope.Numerator
    List.fold (checkSolutionAux seed opt dir endian size targPt) [] solutions

  let checkSplitAux seed opt dir endian size targPt accRes (sol1, sol2) =
    let tryBytes1 = bigIntToBytes endian size sol1
    let trySeed1 = Seed.fixCurBytes seed dir tryBytes1
    let tryBytes2 = bigIntToBytes endian size sol2
    let trySeed2 = Seed.fixCurBytes seed dir tryBytes2
    // Use dummy value as 'tryVal', since our interest is in branch distance.
    let _, brInfoOpt1 = Executor.getBranchInfoAt opt trySeed1 0I targPt
    let _, brInfoOpt2 = Executor.getBranchInfoAt opt trySeed2 0I targPt
    match brInfoOpt1, brInfoOpt2 with
    | Some brInfo1, Some brInfo2 ->
      if sameSign brInfo1.Distance brInfo2.Distance
      then accRes
      else let sign = if brInfo1.Distance > 0I then Positive else Negative
           (sol1, sol2, sign) :: accRes
    | _ -> accRes

  let checkSplitPoint seed opt dir ineq targPt =
    let splitPoints = ineq.SplitPoints
    let endian = ineq.Endian
    let size = ineq.ChunkSize
    List.fold (checkSplitAux seed opt dir endian size targPt) [] splitPoints

  let extractSplitPoint seed opt dir inequality targPt =
    match inequality.TightInequality, inequality.LooseInequality with
    | Some eq, Some ineq ->
      let tightSols = checkSolution seed opt dir eq targPt
      if not (List.isEmpty tightSols) then
        let splits = List.map splitWithSolution tightSols
        (eq.ChunkSize, eq.Endian, splits)
      else
        let splits = checkSplitPoint seed opt dir ineq targPt
        (ineq.ChunkSize, ineq.Endian, splits)
    | Some eq, None ->
      let tightSols = checkSolution seed opt dir eq targPt
      let splits = List.map splitWithSolution tightSols
      (eq.ChunkSize, eq.Endian, splits)
    | None, Some ineq ->
      let splits = checkSplitPoint seed opt dir ineq targPt
      (ineq.ChunkSize, ineq.Endian, splits)
    | None, None -> failwith "Unreachable"

  let extractCond seed opt dir inequality targPt =
    let size, endian, splitPoints = extractSplitPoint seed opt dir inequality targPt
    let sign = inequality.Sign
    let posMSBRanges, negMSBRanges = generateMSBRanges splitPoints size sign
    let posCondition = Constraint.make posMSBRanges endian size
    let negCondition = Constraint.make negMSBRanges endian size
    //log "Encountered inequality : %s" (LinearInequality.toString inequality)
    //log "Condition for positive distance : %A" posCondition
    //log "Condition for negative distance : %A" negCondition
    (posCondition, negCondition)

  let updateConditions pc distSign (condP:Constraint) (condN:Constraint) =
    if distSign = Positive
    then (Constraint.conjunction pc condP, Constraint.conjunction pc condN)
    else (Constraint.conjunction pc condN, Constraint.conjunction pc condP)

  let encodeCondition seed opt dir condition =
    if opt.Verbosity >= 4 then log "Encoding condition called with %A" condition
    if Constraint.isTop condition then []
    elif Seed.queryLenToward seed dir < List.length condition then []
    else
      if opt.Verbosity >= 4 then log "Encoding condition %A" condition
      let byteConds =
        if dir = Right
        then List.mapi (fun i byteCond -> (i, byteCond)) condition
        else
          let len = List.length condition
          List.mapi (fun i byteCond -> (len - i - 1, byteCond)) condition
      let newSeeds =
        List.fold (fun accSeeds (offset, byteCond) ->
          if ByteConstraint.isTop byteCond then accSeeds else
            List.collect (fun range ->
              match range with
              | Between (low, high) ->
                let low = if low < 0I then 0uy
                          elif low > 255I then 255uy
                          else byte low
                let high = if high < 0I then 0uy
                           elif high > 255I then 255uy
                           else byte high
                List.map
                  (fun accSeed ->
                    Seed.constrainByteAt accSeed dir offset low high
                  ) accSeeds
              | Bottom -> []
              | Top -> failwith "Unreachable"
            ) byteCond
          ) [seed] byteConds
      //log "Encoded seeds"
      //List.iter (fun s -> log "%s" (Seed.toString s)) newSeeds
      newSeeds


  let rec solveBranches seed opt dir pc branchTree =
    match branchTree with
    | Nil ->
      List.map (fun s -> (s, Inequality)) (encodeCondition seed opt dir pc)
    | Step ((LinIneq ineq, targPt), distSign, childTree) ->
      if opt.Verbosity >= 4 then
        log "Solving @ 0x%x(#%d)" targPt.Addr targPt.Idx
        log "Linear inequality : %s" (LinearInequality.toString ineq)
      let condP, condN = extractCond seed opt dir ineq targPt
      let pc, flipCond = updateConditions pc distSign condP condN
      if opt.Verbosity >= 4 then
        log "PC updated to %A while stepping" pc
        log "Will generate seed with cond %A" flipCond
      let seeds = encodeCondition seed opt dir flipCond
      let results = List.map (fun s -> (s, Inequality)) seeds
      let childResults = solveBranches seed opt dir pc childTree
      results @ childResults
    | Step ((LinEq linEq, targPt), _, childTree) ->
      let seeds = solveEquation seed opt dir [] (targPt, linEq)
      let results = List.map (fun s -> (s, Equation)) seeds
      let childResults = solveBranches seed opt dir pc childTree
      results @ childResults
    | Step ((Mono mono, targPt), _, childTree) ->
      let seeds = solveMonotonic seed opt [] (targPt, mono)
      let results = List.map (fun s -> (s, Monoton)) seeds
      let childResults = solveBranches seed opt dir pc childTree
      results @ childResults
    | Fork ((LinIneq ineq, targPt), pairs) ->
      if opt.Verbosity >= 4 then
        log "Solving @ 0x%x(#%d)" targPt.Addr targPt.Idx
        log "Linear inequality : %s" (LinearInequality.toString ineq)
      let condP, condN = extractCond seed opt dir ineq targPt
      List.fold (fun accRes (distSign, childTree) ->
        let pc, _ = updateConditions pc distSign condP condN
        if opt.Verbosity >= 4 then
          log "PC updated to %A while forking" pc
        solveBranches seed opt dir pc childTree @ accRes
      ) [] pairs
    | Fork ((LinEq linEq, targPt), pairs) ->
      let seeds = solveEquation seed opt dir [] (targPt, linEq)
      let results = List.map (fun s -> (s, Equation)) seeds
      let childTrees = List.map snd pairs
      let childResults = List.collect (solveBranches seed opt dir pc) childTrees
      results @ childResults
    | Fork ((Mono mono, targPt), pairs) ->
      let seeds = solveMonotonic seed opt [] (targPt, mono)
      let results = List.map (fun s -> (s, Monoton)) seeds
      let childTrees = List.map snd pairs
      let childResults = List.collect (solveBranches seed opt dir pc) childTrees
      results @ childResults
    | Diverge pairs ->
      List.fold (fun accSeeds (_, brTree) ->
        solveBranches seed opt dir pc brTree @ accSeeds
      ) [] pairs

  let solve seed opt byteDir branchTree =
    let branchTree = BranchTree.selectBranches opt branchTree
    let initPC = Constraint.top
    solveBranches seed opt byteDir initPC branchTree
