#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int live, releases, fail_allocate, fail_reallocate_over;
static size_t last_request;
static void* tracked_allocate(size_t size)
{
    if (fail_allocate) return NULL;
    void* block = malloc(size);
    if (block) { ++live; memset(block, 0xA5, size); }
    return block;
}
/* Fails any growth request larger than fail_reallocate_over bytes (0 = never). */
static void* tracked_reallocate(void* previous, size_t size)
{
    last_request = size;
    if (fail_reallocate_over && size > (size_t)fail_reallocate_over) return NULL;
    void* block = realloc(previous, size);
    if (block && !previous) ++live;
    return block;
}
static void tracked_free(void* block)
{
    if (block) { --live; ++releases; }
    free(block);
}
#define malloc tracked_allocate
#define realloc tracked_reallocate
#define free tracked_free
#include "@VECTOR_HEADER@"
#undef malloc
#undef realloc
#undef free

#define CHECK(condition, name) do { if (!(condition)) { fprintf(stderr, "[FAIL] %s\n", name); return 1; } } while (0)

int main(void)
{
    CHECK(sage_int_vector_length(NULL) == 0 && sage_int_vector_capacity(NULL) == 0 && sage_int_vector_limit(NULL) == 0, "null_reads_empty");
    CHECK(sage_int_vector_push(NULL, 1) == SAGE_VECTOR_INVALID && sage_int_vector_reserve(NULL, 1) == SAGE_VECTOR_INVALID, "null_growth_invalid");
    CHECK(!sage_int_vector_has(NULL, 0) && sage_int_vector_get(NULL, 0) == 0 && !sage_int_vector_set(NULL, 0, 1) && !sage_int_vector_pop(NULL), "null_access_fails");
    sage_int_vector_clear(NULL);
    sage_int_vector_drop(NULL);
    CHECK(live == 0, "null_allocates_nothing");

    fail_allocate = 1;
    CHECK(sage_int_vector_create(4) == NULL && live == 0, "owner_allocation_failure");
    fail_allocate = 0;
    CHECK(sage_int_vector_create(SIZE_MAX) == NULL && live == 0, "limit_overflow_rejected");
    if (SIZE_MAX > UINT32_MAX) CHECK(sage_int_vector_create((size_t)UINT32_MAX + 1) == NULL, "limit_above_u32_rejected");

    SageIntVectorState* vector = sage_int_vector_create(10);
    CHECK(vector && vector->data == NULL && live == 1, "create_defers_element_storage");
    CHECK(sage_int_vector_push(vector, 1) == SAGE_VECTOR_OK && sage_int_vector_capacity(vector) == 4, "first_growth_four");
    for (int value = 2; value <= 4; ++value) CHECK(sage_int_vector_push(vector, value) == SAGE_VECTOR_OK, "fill_first_block");
    CHECK(sage_int_vector_push(vector, 5) == SAGE_VECTOR_OK && sage_int_vector_capacity(vector) == 8, "doubling_growth");
    for (int value = 6; value <= 8; ++value) CHECK(sage_int_vector_push(vector, value) == SAGE_VECTOR_OK, "fill_second_block");
    fail_reallocate_over = 9 * sizeof(int32_t) - 1;
    CHECK(sage_int_vector_push(vector, 9) == SAGE_VECTOR_NO_MEMORY, "growth_failure_reported");
    CHECK(sage_int_vector_length(vector) == 8 && sage_int_vector_capacity(vector) == 8 && sage_int_vector_get(vector, 7) == 8, "growth_failure_preserves");
    fail_reallocate_over = 9 * sizeof(int32_t);
    CHECK(sage_int_vector_push(vector, 9) == SAGE_VECTOR_OK && sage_int_vector_capacity(vector) == 9 && last_request == 9 * sizeof(int32_t), "exact_need_retry");
    fail_reallocate_over = 0;
    CHECK(sage_int_vector_push(vector, 10) == SAGE_VECTOR_OK && sage_int_vector_capacity(vector) == 10, "growth_clamped_to_limit");
    CHECK(sage_int_vector_push(vector, 11) == SAGE_VECTOR_FULL && sage_int_vector_length(vector) == 10, "limit_full");
    CHECK(sage_int_vector_reserve(vector, 11) == SAGE_VECTOR_FULL && sage_int_vector_reserve(vector, 3) == SAGE_VECTOR_OK, "reserve_bounds");
    for (int index = 0; index < 10; ++index) CHECK(sage_int_vector_get(vector, (size_t)index) == index + 1, "contents_survive_growth");
    CHECK(!sage_int_vector_has(vector, 10) && sage_int_vector_get(vector, 10) == 0 && !sage_int_vector_set(vector, 10, 0), "index_at_length_unavailable");
    CHECK(sage_int_vector_set(vector, 0, -7) && sage_int_vector_get(vector, 0) == -7, "set_existing");
    CHECK(sage_int_vector_pop(vector) && sage_int_vector_length(vector) == 9 && !sage_int_vector_has(vector, 9), "pop_hides_last");
    sage_int_vector_clear(vector);
    CHECK(sage_int_vector_length(vector) == 0 && sage_int_vector_capacity(vector) == 10 && !sage_int_vector_pop(vector), "clear_keeps_storage");
    sage_int_vector_drop(vector);
    CHECK(live == 0 && releases == 2, "drop_releases_owner_and_storage");

    SageIntVectorState* zero = sage_int_vector_create(0);
    CHECK(zero && sage_int_vector_push(zero, 1) == SAGE_VECTOR_FULL && zero->data == NULL, "zero_limit_never_allocates");
    sage_int_vector_drop(zero);
    CHECK(live == 0, "zero_limit_released");
    return 0;
}
