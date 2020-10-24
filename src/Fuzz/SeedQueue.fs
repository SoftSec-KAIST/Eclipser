namespace Eclipser

/// A simple, purely functional queue.
type Queue<'a > = {
  Enqueued : 'a list
  ToDequeue : 'a list
}

module Queue =

  exception EmptyException

  let empty = { Enqueued = []; ToDequeue = [] }

  let isEmpty q = List.isEmpty q.Enqueued && List.isEmpty q.ToDequeue

  /// Enqueue an element to a queue.
  let enqueue q elem = { q with Enqueued = elem :: q.Enqueued }

  /// Dequeue an element from a queue. Raises Queue.EmptyException if the
  /// queue is empty.
  let dequeue q =
    match q.Enqueued, q.ToDequeue with
    | [], [] -> raise EmptyException
    | enq, [] ->
      let enqRev = List.rev enq
      let elem, toDequeue = List.head enqRev, List.tail enqRev
      (elem, { Enqueued = []; ToDequeue = toDequeue })
    | enq, deqHd :: deqTail ->
      (deqHd, { Enqueued = enq; ToDequeue = deqTail })

  let toString stringfy q =
    let elems = q.ToDequeue @ (List.rev q.Enqueued)
    let elemStrs = List.map stringfy elems
    String.concat "\n" elemStrs

/// Queue of seeds with 'favored' priority and 'normal' priority.
type SeedQueue = {
  Favoreds : Queue<Seed>
  Normals : Queue<Seed>
}

module SeedQueue =

  let empty =
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
