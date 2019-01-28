module Eclipser.BytesUtils

open System
open Utils

/// Types and functions related to byte sequence manipulation.

type Endian = LE | BE

let allBytes = [ Byte.MinValue .. Byte.MaxValue ]

let whiteSpaces = [ 9uy; 10uy; 32uy ] // '\t', '\n', ' '.

let private isCharacter x =
  x = 0uy || 33uy <= x && x < 127uy || List.contains x whiteSpaces

let charBytes = List.filter isCharacter allBytes

let strToBytes (str: string) = str.ToCharArray () |> Array.map byte

let bytesToStr (bytes: byte[]) = Array.map char bytes |> String


// Auxiliary function for bigIntToBytes().
let rec private bigIntToBytesAux accBytes leftSize value =
  if leftSize = 0 then accBytes else
    let accBytes = (byte (value &&& bigint 0xFF)) :: accBytes
    bigIntToBytesAux accBytes (leftSize - 1) (value >>> 8)

/// Convert a big integer into a byte array using specified endianess. The
/// length of converted array is specified as 'size' argument. For example,
/// 0x4142I is converted into [| 0x0uy, 0x0uy 0x41uy, 0x42uy |]] if big endian
/// of 4-byte array is requested.
let bigIntToBytes endian size value =
  bigIntToBytesAux [] size value
  |> if endian = LE then List.rev else identity
  |> Array.ofList

// Auxiliary function for bytesToBigInt().
let rec private bytesToBigIntAux accumBigInt bytes =
  match bytes with
  | [] -> accumBigInt
  | headByte :: tailBytes ->
    let accumInt = (accumBigInt <<< 8) + bigint (uint32 headByte)
    bytesToBigIntAux accumInt tailBytes

/// Convert a byte array into big integer using specified endianess. For
/// example, [| 0x41uy, 0x42uy |] is converted into 0x4142I in big endian.
let bytesToBigInt endian (bytes: byte[]) =
  Array.toList bytes
  |> if endian = LE then List.rev else identity
  |> bytesToBigIntAux 0I

// Auxiliary function for uIntToBytes().
let rec private uIntToBytesAux accBytes leftSize value =
  if leftSize = 0 then accBytes else
    let accBytes = (byte (value &&& 0xFFu)) :: accBytes
    uIntToBytesAux accBytes (leftSize - 1) (value >>> 8)

/// Convert an integer into a byte array using specified endianess. The length
/// of converted array is specified as 'size' argument. For example, 0x4142I is
/// converted into [| 0x0uy, 0x0uy 0x41uy, 0x42uy |]] if big endian of 4-byte
/// array is requested.
let uIntToBytes endian size (value:uint32) =
  uIntToBytesAux [] size value
  |> if endian = LE then List.rev else identity
  |> Array.ofList

// Auxiliary function for bytesToUInt().
let rec private bytesToUIntAux accumInt bytes =
  match bytes with
  | [] -> accumInt
  | headByte :: tailBytes ->
    let accumInt = (accumInt <<< 8) + uint32 headByte
    bytesToUIntAux accumInt tailBytes

/// Convert a byte array into an integer using specified endianess. For example,
/// [| 0x41uy, 0x42uy |] is converted into 0x4142u in big endian.
let bytesToUInt endian (bytes: byte[]) =
  Array.toList bytes
  |> if endian = LE then List.rev else identity
  |> bytesToUIntAux 0u
