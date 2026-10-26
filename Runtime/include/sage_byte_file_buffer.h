#ifndef SAGE_BYTE_FILE_BUFFER_H
#define SAGE_BYTE_FILE_BUFFER_H

#include "sage_byte_file.h"
#include "sage_byte_buffer.h"

/* Status belongs to this exclusive file borrow and is captured immediately by
   the Sage wrapper. No public borrowed data pointer or native result ABI escapes. */
static size_t sage_byte_file_read_buffer(SageByteFileState* file, SageByteBufferState* buffer, size_t limit)
{
    if (!file) return 0;
    file->transfer_eof = 0;
    file->transfer_failed = 1;
    if (!sage_byte_file_is_open(file) || !buffer || limit > buffer->capacity) return 0;
    if (limit == 0) { file->transfer_failed = 0; return 0; }
    if (!sage_byte_file_prepare(file)) return 0;
    size_t count = fread(buffer->data, 1, limit, file->stream);
    buffer->length = count;
    file->transfer_eof = feof(file->stream) != 0;
    file->transfer_failed = ferror(file->stream) != 0 || (count < limit && !file->transfer_eof);
    return count;
}

static size_t sage_byte_file_write_buffer(SageByteFileState* file, const SageByteBufferState* buffer, size_t limit)
{
    if (!file) return 0;
    file->transfer_eof = 0;
    file->transfer_failed = 1;
    if (!sage_byte_file_can_write(file) || !buffer || limit > buffer->length) return 0;
    if (limit == 0) { file->transfer_failed = 0; return 0; }
    if (!sage_byte_file_prepare(file)) return 0;
    size_t count = fwrite(buffer->data, 1, limit, file->stream);
    file->transfer_failed = ferror(file->stream) != 0 || count < limit;
    return count;
}

static int sage_byte_file_transfer_eof(const SageByteFileState* file)
{
    return file && file->transfer_eof;
}

static int sage_byte_file_transfer_failed(const SageByteFileState* file)
{
    return !file || file->transfer_failed;
}

#endif
