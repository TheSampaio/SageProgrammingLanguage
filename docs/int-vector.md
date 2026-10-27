# Owned int vector (v0.33.1)

`use int_vector;` imports `IntVector`, an exclusive owner of a growable `i32`
sequence with a fixed maximum length (its *limit*), plus the `IntRead` and
`VectorGrow` result structs. No `unsafe` code is needed. Moves, borrows,
consuming parameters, owning returns, reassignment and automatic cleanup follow
the same rules as [`Text`](text.md) and [`ByteBuffer`](byte-buffers.md).

```sage
use int_vector;

func sum(values: &IntVector): i32
{
    var total: i32 = 0;
    var index: u32 = 0;
    while (index < values.length())
    {
        var item: IntRead = values.get(index);
        total = total + item.value;
        index = index + 1;
    }
    return total;
}

func main(): none
{
    var scores: IntVector = int_vector::create(1000); // at most 1000 elements
    var grow: VectorGrow = scores.push(42);
    if (!grow.ok) { /* grow.full or grow.out_of_memory: contents unchanged */ }
    var total: i32 = sum(&scores);
} // owner and element storage are freed once here
```

## Operations

| Operation | Receiver | Result |
| --- | --- | --- |
| `int_vector::create(limit: u32)` | - | Empty vector, no element storage yet; empty owner if the owner allocation fails |
| `int_vector::empty()` | - | Empty owner |
| `is_allocated()` | `&IntVector` | Whether the owner holds state |
| `length()`, `capacity()`, `limit()` | `&IntVector` | Elements / allocated slots / maximum length |
| `get(index: u32)` | `&IntVector` | `IntRead { value, has_value }`; `has_value=false`, `value=0` when `index >= length` |
| `set(index: u32, element: i32)` | `&mut IntVector` | Replaces an existing element; `false` out of bounds |
| `push(element: i32)` | `&mut IntVector` | `VectorGrow { ok, full, out_of_memory }` |
| `reserve(capacity: u32)` | `&mut IntVector` | Pre-allocates slots; same outcome struct |
| `pop()` | `&mut IntVector` | Removes and returns the last element; `has_value=false` when empty |
| `clear()` | `&mut IntVector` | Length 0, storage kept |
| `int_vector::release(value: IntVector)` | consumes | Frees owner and storage |

Growth is explicit and bounded: storage starts empty, the first push allocates
four slots, then capacity doubles, never beyond the limit. If the doubled
allocation fails, the exact next slot is tried once. `full=true` means the limit
is reached; `out_of_memory=true` means the allocator failed. Both leave length,
capacity and every element unchanged. An empty owner reports all flags false.
There is no global allocator or hidden reallocation outside push/reserve; the
owner handle never moves when storage grows, so borrows remain valid between
calls. Element storage is only reachable through checked accessors.

## Rejected programs

Use after move/release (S140), push/pop through `&IntVector` (S171), passing a
call result beside a borrowed receiver such as `v.push(v.length() as i32)` (S177;
bind it first), `v.release()` as a method (S159), release during a live borrow
(S178), anonymous release (S194), direct native calls (S164), private storage
access (S109), and implicit `u64` indexes (S161).

## Native implementation and limits

`Runtime/include/sage_int_vector.h` keeps `{ length, capacity, limit, data }` in
a stable owner allocation and the elements in a separate `realloc`ed block.
`limit` is checked against `u32` and the byte-size overflow before allocation.
Only `i32` elements are provided (Sage has no generics yet); there is no
iteration protocol, slicing, sorting, or thread safety.

Validated by `tests/int-vector.ps1` (native growth/failure/limit checks with an
allocator that fails on demand, 26 Sage assertions, exactly-once cleanup of
owners and reallocated storage, 11 rejected programs) and
`Sandbox/tests/int_vector.test.sg`.
