#include <stdio.h>
#include <stdlib.h>
/* Test-only wrapper: logs every allocation/free of the included runtime header so a
   script can prove each block is released exactly once. realloc logs free + allocate. */
static void record_allocation_event(const char* event, void* pointer)
{
    if (!pointer) return;
    FILE* log = fopen("@LOG_PATH@", "a");
    if (!log) exit(90);
    fprintf(log, "%s %p\n", event, pointer);
    if (fclose(log) != 0) exit(91);
}
static void* record_allocate(size_t size)
{
    void* pointer = malloc(size);
    record_allocation_event("allocate", pointer);
    return pointer;
}
static void* record_reallocate(void* previous, size_t size)
{
    void* pointer = realloc(previous, size);
    if (pointer) { record_allocation_event("free", previous); record_allocation_event("allocate", pointer); }
    return pointer;
}
static void record_free(void* pointer)
{
    record_allocation_event("free", pointer);
    free(pointer);
}
#define malloc record_allocate
#define realloc record_reallocate
#define free record_free
#include "@HEADER@"
#undef malloc
#undef realloc
#undef free
