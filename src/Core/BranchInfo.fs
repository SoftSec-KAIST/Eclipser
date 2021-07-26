namespace Eclipser

open Utils

type CompareType = Equality | SignedSize | UnsignedSize

type BranchPoint = {
  Addr : uint64
  Idx  : int
}

type Context = {
  Bytes : byte array
  ByteDir : Direction
}

type BranchInfo = {
  InstAddr : uint64
  BrType   : CompareType
  TryVal   : bigint
  OpSize   : int
  Oprnd1   : uint64
  Oprnd2   : uint64
  Distance : bigint
}

module BranchInfo =

  let toString
    { TryVal = x; InstAddr = addr; BrType = typ; Oprnd1 = v1; Oprnd2 = v2} =
    Printf.sprintf "Try = %A : 0x%x vs 0x%x @ 0x%x (%A)" x v1 v2 addr typ

  let interpretAs sign size (x: uint64) =
    match sign with
    | Signed ->
      let signedMax = getSignedMax size
      let x = bigint x
      if x > signedMax then x - (getUnsignedMax size) - 1I else x
    | Unsigned -> bigint x
