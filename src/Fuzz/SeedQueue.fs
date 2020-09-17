namespace Eclipser

open MBrace.FsPickler
open Config
open Utils
open Options

type ConcolicQueue = {
  FavoredQueue : Queue<Seed>
  NormalQueue : FileQueue
}

module ConcolicQueue =

  let serializer = FsPickler.CreateBinarySerializer ()

  let initialize queueDir  =
    { FavoredQueue = Queue.empty
      NormalQueue = FileQueue.initialize "concolic-seed" queueDir }

  let isEmpty queue =
    Queue.isEmpty queue.FavoredQueue && FileQueue.isEmpty queue.NormalQueue

  let enqueue queue (priority, seed) =
    match priority with
    | Favored ->
      let newFavoredQueue = Queue.enqueue queue.FavoredQueue seed
      { queue with FavoredQueue = newFavoredQueue }
    | Normal ->
      let seedBytes = serializer.Pickle seed
      let newNormalQueue = FileQueue.enqueue queue.NormalQueue seedBytes
      { queue with NormalQueue = newNormalQueue }

  let dequeue queue =
    let favoredQueue = queue.FavoredQueue
    let normalQueue = queue.NormalQueue
    let queueSelection =
      if FileQueue.isEmpty normalQueue then Favored
      elif Queue.isEmpty favoredQueue then Normal
      else Favored
    match queueSelection with
    | Favored ->
      let seed, newFavoredQueue = Queue.dequeue favoredQueue
      let newQueue = { queue with FavoredQueue = newFavoredQueue }
      (Favored, seed, newQueue)
    | Normal ->
      let seedBytes, newNormalQueue = FileQueue.dequeue normalQueue
      let seed = serializer.UnPickle<Seed> seedBytes
      let newQueue = { queue with NormalQueue = newNormalQueue }
      (Normal, seed, newQueue)

type RandFuzzQueue = {
  FavoredQueue : DurableQueue<Seed>
  NormalQueue : FileQueue
  LastMinimizedCount : int
  RemoveCount : int
}

module RandFuzzQueue =

  let serializer = FsPickler.CreateBinarySerializer ()

  let initialize queueDir =
    let dummySeed = Seed.make Args [] 0 0
    { FavoredQueue = DurableQueue.initialize dummySeed
      NormalQueue = FileQueue.initialize "rand-seed" queueDir
      LastMinimizedCount = 0
      RemoveCount = 0 }

  let enqueue queue (priority, seed) =
    match priority with
    | Favored ->
      let newFavoredQueue = DurableQueue.enqueue queue.FavoredQueue seed
      { queue with FavoredQueue = newFavoredQueue }
    | Normal ->
      let seedBytes = serializer.Pickle seed
      let newNormalQueue = FileQueue.enqueue queue.NormalQueue seedBytes
      { queue with NormalQueue = newNormalQueue }

  let dequeue queue =
    let favoredQueue = queue.FavoredQueue
    let normalQueue = queue.NormalQueue
    let queueSelection =
      if FileQueue.isEmpty normalQueue then Favored
      elif random.NextDouble() < FavoredSeedProb then Favored
      else Normal
    match queueSelection with
    | Favored ->
      let seed, newFavoredQueue = DurableQueue.fetch favoredQueue
      let newQueue = { queue with FavoredQueue = newFavoredQueue }
      (Favored, seed, newQueue)
    | Normal ->
      let seedBytes, newNormalQueue = FileQueue.dequeue normalQueue
      let seed = serializer.UnPickle<Seed> seedBytes
      let newQueue = { queue with NormalQueue = newNormalQueue }
      (Normal, seed, newQueue)

  let timeToMinimize queue =
    let curSize = queue.FavoredQueue.Count
    let prevSize = queue.LastMinimizedCount
    curSize > int (float prevSize * SeedCullingThreshold)

  let rec findRedundantsGreedyAux queue seedEntries accRedundantSeeds =
    if List.isEmpty seedEntries then accRedundantSeeds
    else
      (* Choose an entry that has largest number of covered edges *)
      let getEdgeCount (idx, seed, edges) = Set.count edges
      let seedEntriesSorted = List.sortByDescending getEdgeCount seedEntries
      let _, _, chosenEdges = List.head seedEntriesSorted
      let seedEntries = List.tail seedEntriesSorted
      (* Now update (i.e. subtract edge set) seed entries *)
      let subtractEdges edges (i, s, ns) = (i, s, Set.difference ns edges)
      let seedEntries = List.map (subtractEdges chosenEdges) seedEntries
      (* If the edge set entry is empty, it means that seed is redundant *)
      let redundantEntries, seedEntries =
        List.partition (fun (i, s, ns) -> Set.isEmpty ns) seedEntries
      let redundantSeeds = List.map (fun (i, s, _) -> (i, s)) redundantEntries
      let accRedundantSeeds = redundantSeeds @ accRedundantSeeds
      findRedundantsGreedyAux queue seedEntries accRedundantSeeds

  let findRedundantsGreedy seeds queue opt =
    let seedEntries =
      List.fold
        (fun accSets (idx, seed) ->
          (idx, seed, Executor.getEdgeSet opt seed) :: accSets
        ) [] seeds
    findRedundantsGreedyAux queue seedEntries []

  let minimize queue opt =
    let favoredQueue = queue.FavoredQueue
    let seeds = favoredQueue.Elems.[0 .. favoredQueue.Count - 1]
                |> Array.mapi (fun i seed -> (i, seed))
                |> List.ofArray
    let seedsToRemove = findRedundantsGreedy seeds favoredQueue opt
    // Note that we should remove larger index first
    let newFavoredQueue = List.sortByDescending fst seedsToRemove
                          |> List.fold DurableQueue.remove favoredQueue
    { queue with FavoredQueue = newFavoredQueue
                 LastMinimizedCount = newFavoredQueue.Count
                 RemoveCount = queue.RemoveCount + seedsToRemove.Length }

