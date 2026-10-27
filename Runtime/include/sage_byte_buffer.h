#ifndef SAGE_BYTE_BUFFER_H
#define SAGE_BYTE_BUFFER_H

#include <limits.h>
#include <stdint.h>
#include <stdlib.h>

_Static_assert(CHAR_BIT == 8, "Sage byte buffers require 8-bit bytes");
_Static_assert(SIZE_MAX >= UINT32_MAX, "Sage byte buffers require at least 32-bit size_t");

/* Private storage. Only data[0..length) is initialized and readable. */
typedef struct SageByteBufferState
{
    size_t length;
    size_t capacity;
    unsigned char data[];
} SageByteBufferState;

static SageByteBufferState* sage_byte_buffer_create(size_t capacity)
{
    if (capacity > UINT32_MAX || capacity > SIZE_MAX - sizeof(SageByteBufferState)) return NULL;
    SageByteBufferState* state = malloc(sizeof(*state) + capacity);
    if (!state) return NULL;
    state->length = 0;
    state->capacity = capacity;
    return state;
}

static size_t sage_byte_buffer_length(const SageByteBufferState* state)
{
    return state ? state->length : 0;
}

static size_t sage_byte_buffer_capacity(const SageByteBufferState* state)
{
    return state ? state->capacity : 0;
}

/* -1 is unavailable; all 256 nonnegative byte values are valid data. */
static int sage_byte_buffer_read(const SageByteBufferState* state, size_t index)
{
    if (!state || index >= state->length) return -1;
    return state->data[index];
}

static int sage_byte_buffer_write(SageByteBufferState* state, size_t index, unsigned char byte)
{
    if (!state || index >= state->length) return 0;
    state->data[index] = byte;
    return 1;
}

static int sage_byte_buffer_push(SageByteBufferState* state, unsigned char byte)
{
    if (!state || state->length >= state->capacity) return 0;
    state->data[state->length] = byte;
    ++state->length;
    return 1;
}

static void sage_byte_buffer_clear(SageByteBufferState* state)
{
    if (state) state->length = 0;
}

static void sage_byte_buffer_drop(SageByteBufferState* state)
{
    free(state);
}

#endif
