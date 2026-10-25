#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int allocations, releases, live, fail_allocate;
static size_t requested_size;
static void* tracked_allocate(size_t size)
{
    ++allocations;
    requested_size = size;
    if (fail_allocate) return NULL;
    void* state = malloc(size);
    if (state) { ++live; memset(state, 0xA5, size); }
    return state;
}
static void tracked_free(void* state)
{
    if (state) { --live; ++releases; }
    free(state);
}
#define malloc tracked_allocate
#define free tracked_free
#include "@BUFFER_HEADER@"
#undef malloc
#undef free

static int assertions;
#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } ++assertions; } while (0)

int main(void)
{
    CHECK(sage_byte_buffer_length(NULL) == 0, "null_length");
    CHECK(sage_byte_buffer_capacity(NULL) == 0, "null_capacity");
    CHECK(sage_byte_buffer_read(NULL, 0) == -1, "null_read");
    CHECK(!sage_byte_buffer_write(NULL, 0, 42), "null_write");
    CHECK(!sage_byte_buffer_push(NULL, 42), "null_push");
    sage_byte_buffer_clear(NULL);
    sage_byte_buffer_drop(NULL);
    CHECK(live == 0 && releases == 0, "null_cleanup_no_allocation");
    fail_allocate = 1;
    CHECK(sage_byte_buffer_create(8) == NULL && live == 0, "allocation_failure_empty");
    fail_allocate = 0;
    int previous_allocations = allocations;
    CHECK(sage_byte_buffer_create(SIZE_MAX) == NULL && allocations == previous_allocations, "overflow_rejected_before_allocation");
    if (SIZE_MAX > UINT32_MAX)
        CHECK(sage_byte_buffer_create((size_t)UINT32_MAX + 1) == NULL && allocations == previous_allocations, "public_capacity_limit_enforced_natively");
    fail_allocate = 1;
    CHECK(sage_byte_buffer_create(UINT32_MAX) == NULL && live == 0, "maximum_capacity_allocation_failure");
    if ((size_t)UINT32_MAX <= SIZE_MAX - sizeof(SageByteBufferState))
        CHECK(requested_size == sizeof(SageByteBufferState) + (size_t)UINT32_MAX, "maximum_allocation_size_no_wrap");
    fail_allocate = 0;

    SageByteBufferState* state = sage_byte_buffer_create(0);
    CHECK(state && sage_byte_buffer_length(state) == 0 && sage_byte_buffer_capacity(state) == 0, "zero_capacity_is_valid_owner");
    CHECK(sage_byte_buffer_read(state, 0) == -1 && !sage_byte_buffer_write(state, 0, 42), "zero_capacity_no_access");
    CHECK(!sage_byte_buffer_push(state, 42), "zero_capacity_cannot_grow");
    sage_byte_buffer_drop(state);
    CHECK(live == 0 && releases == 1, "zero_capacity_released");

    state = sage_byte_buffer_create(256);
    CHECK(state && sage_byte_buffer_capacity(state) == 256 && sage_byte_buffer_length(state) == 0, "capacity_and_initial_length");
    CHECK(sage_byte_buffer_read(state, 0) == -1 && sage_byte_buffer_read(state, 255) == -1, "poisoned_uninitialized_storage_hidden");
    CHECK(!sage_byte_buffer_write(state, 0, 7) && sage_byte_buffer_length(state) == 0, "write_does_not_expose_uninitialized_bytes");
    int pushed = 1;
    for (int byte = 0; byte < 256; ++byte)
        if (!sage_byte_buffer_push(state, (unsigned char)byte)) pushed = 0;
    CHECK(pushed && sage_byte_buffer_length(state) == 256, "all_byte_values_pushed");
    int roundtrip = 1;
    for (size_t index = 0; index < 256; ++index)
        if (sage_byte_buffer_read(state, index) != (int)index) roundtrip = 0;
    CHECK(roundtrip, "all_byte_values_read");
    CHECK(!sage_byte_buffer_push(state, 9) && sage_byte_buffer_length(state) == 256, "full_push_preserves_length");
    CHECK(sage_byte_buffer_read(state, 255) == 255, "full_push_preserves_content");
    CHECK(sage_byte_buffer_read(state, 256) == -1 && sage_byte_buffer_read(state, SIZE_MAX) == -1, "read_bounds_include_extreme_index");
    CHECK(!sage_byte_buffer_write(state, 256, 7) && !sage_byte_buffer_write(state, SIZE_MAX, 7), "write_bounds_include_extreme_index");
    CHECK(sage_byte_buffer_write(state, 255, 42) && sage_byte_buffer_read(state, 255) == 42, "last_initialized_byte_replaced");
    CHECK(sage_byte_buffer_length(state) == 256, "write_preserves_length");
    sage_byte_buffer_clear(state);
    CHECK(sage_byte_buffer_length(state) == 0 && sage_byte_buffer_capacity(state) == 256, "clear_retains_capacity");
    CHECK(sage_byte_buffer_read(state, 255) == -1 && !sage_byte_buffer_write(state, 255, 1), "clear_hides_old_storage");
    CHECK(sage_byte_buffer_push(state, 7) && sage_byte_buffer_read(state, 0) == 7, "reuse_initializes_new_prefix");
    CHECK(sage_byte_buffer_read(state, 1) == -1, "reuse_hides_old_suffix");
    sage_byte_buffer_drop(state);
    CHECK(live == 0 && releases == 2, "all_native_buffers_released");
    printf("Native byte buffer checks passed: %d assertions.\n", assertions);
    return 0;
}
