namespace Eclipser

open Config

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
