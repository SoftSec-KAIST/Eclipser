namespace Eclipser

type ConcolicQueue = {
  Favoreds : Queue<Seed>
  Normals : Queue<Seed>
}

module SeedQueue =

  let initialize () =
    { Favoreds = Queue.empty
      Normals = Queue.empty }

  let isEmpty queue =
    Queue.isEmpty queue.Favoreds && Queue.isEmpty queue.Normals

  let enqueue queue (priority, seed) =
    match priority with
    | Favored -> { queue with Favoreds = Queue.enqueue queue.Favoreds seed }
    | Normal -> { queue with Normals = Queue.enqueue queue.Normals seed }

  let dequeue queue =
    let priority = if Queue.isEmpty queue.Favoreds then Normal else Favored
    match priority with
    | Favored ->
      let seed, newFavoreds = Queue.dequeue queue.Favoreds
      let newQueue = { queue with Favoreds = newFavoreds }
      (Favored, seed, newQueue)
    | Normal ->
      let seed, newNormals = Queue.dequeue queue.Normals
      let newQueue = { queue with Normals = newNormals }
      (Normal, seed, newQueue)
