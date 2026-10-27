#ifndef SAGE_INT_VECTOR_H
#define SAGE_INT_VECTOR_H

#include <stdint.h>
#include <stdlib.h>

_Static_assert(SIZE_MAX >= UINT32_MAX, "Sage vectors require at least 32-bit size_t");

/* Private state. The owner pointer stays stable while the element storage grows,
   so growth never invalidates the Sage handle. Only data[0..length) is readable. */
typedef struct SageIntVectorState
{
    size_t length;
    size_t capacity;
    size_t limit;
    int32_t* data;
} SageIntVectorState;

/* Growth outcomes shared with the Sage wrapper. */
enum { SAGE_VECTOR_OK = 0, SAGE_VECTOR_FULL = 1, SAGE_VECTOR_NO_MEMORY = 2, SAGE_VECTOR_INVALID = 3 };

/* limit is the maximum element count. No element storage is allocated yet. */
static SageIntVectorState* sage_int_vector_create(size_t limit)
{
    if (limit > UINT32_MAX || limit > SIZE_MAX / sizeof(int32_t)) return NULL;
    SageIntVectorState* state = malloc(sizeof(*state));
    if (!state) return NULL;
    state->length = 0;
    state->capacity = 0;
    state->limit = limit;
    state->data = NULL;
    return state;
}

static size_t sage_int_vector_length(const SageIntVectorState* state) { return state ? state->length : 0; }
static size_t sage_int_vector_capacity(const SageIntVectorState* state) { return state ? state->capacity : 0; }
static size_t sage_int_vector_limit(const SageIntVectorState* state) { return state ? state->limit : 0; }

/* Ensures room for capacity elements. Failure preserves storage and contents. */
static int sage_int_vector_reserve(SageIntVectorState* state, size_t capacity)
{
    if (!state) return SAGE_VECTOR_INVALID;
    if (capacity <= state->capacity) return SAGE_VECTOR_OK;
    if (capacity > state->limit) return SAGE_VECTOR_FULL;
    int32_t* data = realloc(state->data, capacity * sizeof(int32_t));
    if (!data) return SAGE_VECTOR_NO_MEMORY;
    state->data = data;
    state->capacity = capacity;
    return SAGE_VECTOR_OK;
}

/* Doubles capacity (minimum 4) up to the limit, then retries the exact need. */
static int sage_int_vector_push(SageIntVectorState* state, int32_t value)
{
    if (!state) return SAGE_VECTOR_INVALID;
    if (state->length == state->capacity)
    {
        if (state->length >= state->limit) return SAGE_VECTOR_FULL;
        size_t step = state->capacity < 4 ? 4 : state->capacity;
        size_t wanted = step > state->limit - state->capacity ? state->limit : state->capacity + step;
        int status = sage_int_vector_reserve(state, wanted);
        if (status == SAGE_VECTOR_NO_MEMORY && wanted > state->length + 1)
            status = sage_int_vector_reserve(state, state->length + 1);
        if (status != SAGE_VECTOR_OK) return status;
    }
    state->data[state->length++] = value;
    return SAGE_VECTOR_OK;
}

/* Whether index addresses an initialized element. */
static int sage_int_vector_has(const SageIntVectorState* state, size_t index)
{
    return state && index < state->length;
}

static int32_t sage_int_vector_get(const SageIntVectorState* state, size_t index)
{
    return sage_int_vector_has(state, index) ? state->data[index] : 0;
}

static int sage_int_vector_set(SageIntVectorState* state, size_t index, int32_t value)
{
    if (!sage_int_vector_has(state, index)) return 0;
    state->data[index] = value;
    return 1;
}

/* Removes the last element; callers read it with get(length - 1) first. */
static int sage_int_vector_pop(SageIntVectorState* state)
{
    if (!state || state->length == 0) return 0;
    --state->length;
    return 1;
}

static void sage_int_vector_clear(SageIntVectorState* state)
{
    if (state) state->length = 0;
}

static void sage_int_vector_drop(SageIntVectorState* state)
{
    if (!state) return;
    free(state->data);
    free(state);
}

#endif
