namespace Eclipser

open System
open Utils

type Tendency = Incr | Decr | Undetermined

/// Represents interval [a,b] where f is monotonic, and f(a) < k < f(b)
type Monotonicity = {
  LowerBound  : bigint // a
  LowerVal    : bigint // f(a)
  UpperBound  : bigint // b
  UpperVal    : bigint // f(b)
  TargetVal   : bigint // k
  Tendency    : Tendency
  ByteLen     : int
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Monotonicity =

  let private checkIntermediate y1 y2 y3 tendency =
    match tendency with
    | Incr -> y1 < y2 && y2 < y3
    | Decr -> y1 > y2 && y2 > y3
    | Undetermined -> y1 < y2 && y2 < y3 || y1 > y2 && y2 > y3

  (* Note that call to checkIntermediate should be preceded *)
  let private generate a fa b fb k =
    let tendency =
      if fa < k && k < fb then Incr
      elif fa > k && k > fb then Decr
      else failwith "Monotonicity.generate() : invalid argument"
    { LowerBound = a; LowerVal = fa; UpperBound = b; UpperVal = fb
      TargetVal = k; Tendency = tendency; ByteLen = 1 }

  let rec private findAux prevX prevY targetY tendency coordinates =
    match coordinates with
    | [] -> None
    | (x, y) :: tailCoords ->
      if prevY = targetY || y = targetY then
        (* One of the sampled seed already penetrates this EQ check. In this
         * case, we don't have to search on this monotonicity.
         *)
        None
      elif checkIntermediate prevY targetY y tendency then
        Some (generate prevX prevY x y targetY)
      elif tendency = Incr && prevY <= y then
        findAux x y targetY Incr tailCoords
      elif tendency = Decr && prevY >= y then
        findAux x y targetY Decr tailCoords
      elif tendency = Undetermined && prevY = y then
        findAux x y targetY Undetermined tailCoords
      elif tendency = Undetermined && prevY < y then
        findAux x y targetY Incr tailCoords
      elif tendency = Undetermined && prevY > y then
        findAux x y targetY Decr tailCoords
      else None (* Monotonicity violated *)

  let find brInfos =
    let headBrInfo, tailBrInfos =
      match brInfos with
      | br :: brs -> br, brs
      | _ -> failwith "Not enough BranchInfo to find monotonicity"
    let sign = if headBrInfo.BrType = UnsignedSize then Unsigned else Signed
    let size = headBrInfo.OpSize
    if List.forall (fun f -> f.Oprnd1 = headBrInfo.Oprnd1) brInfos then
      // *.Oprnd1 is constant, so infer monotonicity in *.Oprnd2
      let firstX = headBrInfo.TryVal
      let firstY = BranchInfo.interpretAs sign size headBrInfo.Oprnd2
      let targetY = BranchInfo.interpretAs sign size headBrInfo.Oprnd1
      let toCoord br = (br.TryVal, BranchInfo.interpretAs sign size br.Oprnd2)
      let coordinates = List.map toCoord tailBrInfos
      findAux firstX firstY targetY Undetermined coordinates
    elif List.forall (fun f -> f.Oprnd2 = headBrInfo.Oprnd2) brInfos then
      // *.Oprnd2 is constant, so infer monotonicity in *.Oprnd1
      let firstX = headBrInfo.TryVal
      let firstY = BranchInfo.interpretAs sign size headBrInfo.Oprnd1
      let targetY = BranchInfo.interpretAs sign size headBrInfo.Oprnd2
      let toCoord br = (br.TryVal, BranchInfo.interpretAs sign size br.Oprnd1)
      let coordinates = List.map toCoord tailBrInfos
      findAux firstX firstY targetY Undetermined coordinates
    else None

  let update monotonic x y =
    let newLowerBound, newUpperBound, newLowerVal, newUpperVal =
      match monotonic.Tendency with
      | Incr ->
        if y < monotonic.TargetVal (* We're not there yet *)
        then (x, monotonic.UpperBound, y, monotonic.UpperVal)
        else (monotonic.LowerBound, x, monotonic.LowerVal, y)
      | Decr ->
        if y < monotonic.TargetVal (* We came too far *)
        then (monotonic.LowerBound, x, monotonic.LowerVal, y)
        else (x, monotonic.UpperBound, y, monotonic.UpperVal)
      | Undetermined -> failwith "Unreachable"
    if newUpperBound - newLowerBound = 1I then (* Time to extend chunk size *)
      let newLowerBound = newLowerBound <<< 8
      (* Set the range conservatively, considering the effect of next byte *)
      let newUpperBound = (newUpperBound <<< 8) + 255I
      { monotonic with // Do not update LowerVal and UpperVal in this case.
          LowerBound = newLowerBound; UpperBound = newUpperBound
          ByteLen = monotonic.ByteLen + 1
      }
    else
      { monotonic with
          LowerBound = newLowerBound; UpperBound = newUpperBound
          LowerVal = newLowerVal; UpperVal = newUpperVal
      }

  let toString mono =
    let a, b = mono.LowerBound, mono.UpperBound
    let fa, fb = mono.LowerVal, mono.UpperVal
    let k = mono.TargetVal
    Printf.sprintf "f(%A)=%A < %A < f(%A)=%A" a fa k b fb
