#ifndef SAGE_BYTE_FILE_H
#define SAGE_BYTE_FILE_H

#include <limits.h>
#include <stdio.h>
#include <stdlib.h>

_Static_assert(CHAR_BIT == 8, "Sage byte files require 8-bit bytes");

/* Trusted native state. Sage sees only the declared opaque owning resource. */
typedef struct SageByteFileState { FILE* stream; } SageByteFileState;

static SageByteFileState* sage_byte_file_create(const char* path)
{
    if (!path) return NULL;
    FILE* stream = fopen(path, "w+b");
    if (!stream) return NULL;
    SageByteFileState* state = malloc(sizeof(*state));
    if (!state) { (void)fclose(stream); return NULL; }
    state->stream = stream;
    return state;
}

static int sage_byte_file_is_open(const SageByteFileState* state)
{
    return state && state->stream;
}

/* Positioning separates C update-stream input/output, including input -> flush. */
static int sage_byte_file_prepare(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return 0;
    clearerr(state->stream);
    return fseek(state->stream, 0L, SEEK_CUR) == 0;
}

/* 0..255 is data; -1 is clean EOF; -2 is failure (including an empty owner). */
static int sage_byte_file_read(SageByteFileState* state)
{
    if (!sage_byte_file_prepare(state)) return -2;
    int value = fgetc(state->stream);
    if (value != EOF) return value;
    return ferror(state->stream) ? -2 : -1;
}

static int sage_byte_file_write(SageByteFileState* state, unsigned char value)
{
    if (!sage_byte_file_prepare(state)) return 0;
    return fputc((int)value, state->stream) != EOF;
}

static int sage_byte_file_seek_start(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return 0;
    clearerr(state->stream);
    return fseek(state->stream, 0L, SEEK_SET) == 0;
}

static int sage_byte_file_flush(SageByteFileState* state)
{
    if (!sage_byte_file_prepare(state)) return 0;
    return fflush(state->stream) == 0;
}

static int sage_byte_file_close(SageByteFileState* state)
{
    if (!sage_byte_file_is_open(state)) return EOF;
    FILE* stream = state->stream;
    state->stream = NULL; /* fclose consumes the stream even when it reports failure. */
    return fclose(stream);
}

static void sage_byte_file_drop(SageByteFileState* state)
{
    if (!state) return;
    if (state->stream) (void)sage_byte_file_close(state);
    free(state);
}

#endif
