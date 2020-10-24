/// Miscellaneous utility functions.
module Eclipser.Utils

open System
open System.Collections.Immutable

// Give a less confusing name to identity function, 'id'.
let identity = id

let startTime = DateTime.Now

let random = System.Random()

let printLine (str: string) = Console.WriteLine str

let log fmt =
  let elapsed = DateTime.Now - startTime
  let timeStr = "[" + elapsed.ToString("dd\:hh\:mm\:ss") + "] "
  Printf.kprintf (fun str -> printLine <| timeStr + str) fmt

// Auxiliary function for splitList().
let rec private splitListAux n lst accum =
  match lst with
  | head :: tail when (n > 0) -> splitListAux (n - 1) tail (head :: accum)
  | _ -> (List.rev accum, lst)

/// Split a list into two lists of 'n' elements and 'length(lst) - n' elements.
let splitList n lst =
  splitListAux n lst []

/// Return the last element of a list, watch out for slow performance.
let rec listLast = function
  | [] -> failwith "listLast() : empty input"
  | hd :: [] -> hd
  | hd :: tl -> listLast tl

/// Given a list of element, return distinct combinations of 'n' elements.
let rec combination n lst =
  match (n, lst) with
  | (0, _) -> [ [] ]
  | (_, []) -> []
  | (n, x :: xs) ->
    let withX = List.map (fun l -> x :: l) (combination (n - 1) xs)
    let withOutX = combination n xs
    withX @ withOutX

/// Return the maximum signed integer of a given bit width.
let getSignedMax = function
  | 1 -> bigint (int32 (SByte.MaxValue))
  | 2 -> bigint (int32 (Int16.MaxValue))
  | 4 -> bigint (Int32.MaxValue)
  | 8 -> bigint (Int64.MaxValue)
  | i -> (1I <<< (i * 8 - 1)) - 1I

/// Return the maximum unsigned integer of a given bit width.
let getUnsignedMax = function
  | 1 -> bigint (uint32 (Byte.MaxValue))
  | 2 -> bigint (uint32 (UInt16.MaxValue))
  | 4 -> bigint (UInt32.MaxValue)
  | 8 -> bigint (UInt64.MaxValue)
  | i -> (1I <<< (i * 8)) - 1I

// Auxiliary function for randSubset().
let private randomSubsetAux (accumSet : ImmutableHashSet<int>) i =
  let t = random.Next(i + 1) // 't' will be in the range 0 ~ i.
  if accumSet.Contains(t)
  then accumSet.Add(i)
  else accumSet.Add(t)

/// Choose random k integers from { 0 .. (n - 1) }, with Floyd's algorithm.
let randomSubset n k =
  let empty: ImmutableHashSet<int> = ImmutableHashSet.Create()
  if n >= k then
    List.fold randomSubsetAux empty (List.ofSeq { (n - k) .. (n - 1) })
  else
    let seqList = List.ofSeq { 0 .. (n - 1) }
    List.fold (fun (acc: ImmutableHashSet<int>) i -> acc.Add(i)) empty seqList

/// Choose random N elements from a given list.
let randomSelect elemList n =
  randomSubset (List.length elemList) n
  |> List.ofSeq
  |> List.map (fun idx -> List.item idx elemList)

/// Select integers uniformly from the given range.
let sampleInt min max (n : int) =
  if max < min then failwith "sampleInt() : invalid range provided"
  let bigIntN = bigint n
  if max - min + 1I <= bigIntN then List.ofSeq {min .. max} else
    let delta = (max - min + 1I) / bigIntN
    List.init n (fun i -> min + delta * (bigint i))

/// Check if a file exists in the given path, and exit if not.
let assertFileExists file =
  if not (System.IO.File.Exists(file)) then
    printfn "Target file ('%s') does not exist" file
    exit 1

/// Write a file, without throwing exception.
let writeFile filePath content =
  try System.IO.File.WriteAllBytes(filePath, content) with
  | _ -> log "[Warning] Failed to write file '%s'" filePath

/// Remove a file, without throwing exception.
let removeFile file =
  try System.IO.File.Delete(file) with _ -> ()

/// Create a directory if not exists.
let createDirectoryIfNotExists dir =
  if not (System.IO.Directory.Exists dir) then
    ignore (System.IO.Directory.CreateDirectory dir)